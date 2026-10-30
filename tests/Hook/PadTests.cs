using System.Diagnostics;
using System.IO.Pipes;

namespace NexusKeypad.Tests;

// The pad's test tool outils\pad.ps1, against a fake plugin's pipe (NEXUSKEYPAD_PAD_PIPE: a unique name per test, so even
// a broken guard could never reach the real pad). Guard of 25/10: a press or a reading needs --reel, refused before any
// connection.
public sealed class PadTests
{
    private readonly String _tube = "nexuskeypad-test-" + Guid.NewGuid().ToString("N");

    private (Int32 Code, String Sortie, String Erreur, TimeSpan Duree) Pad(params String[] arguments)
    {
        var info = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-File", Path.Combine(Entree.Depot, "outils", "pad.ps1") }.Concat(arguments))
        {
            info.ArgumentList.Add(a);
        }

        info.Environment["NEXUSKEYPAD_PAD_PIPE"] = this._tube;
        var horloge = Stopwatch.StartNew();
        using var pwsh = Process.Start(info)!;
        var sortie = pwsh.StandardOutput.ReadToEndAsync();
        var erreur = pwsh.StandardError.ReadToEndAsync();
        Assert.True(pwsh.WaitForExit(30000), "pad.ps1 did not end.");
        return (pwsh.ExitCode, sortie.Result.Trim(), erreur.Result, horloge.Elapsed);
    }

    [Theory]
    [InlineData("appui", "1")]
    [InlineData("releve")]
    public void Geste_reel_refuse_sans_reel(params String[] commande)
    {
        using var serveur = new NamedPipeServerStream(this._tube, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var connexion = serveur.WaitForConnectionAsync(TestContext.Current.CancellationToken);
        var (code, _, erreur, _) = this.Pad(commande);
        Assert.Equal(2, code);
        Assert.Contains("--reel", erreur);
        Assert.False(connexion.IsCompleted, "the refused gesture must not even connect to the pipe");
    }

    [Fact]
    public void Une_commande_une_ligne_de_reponse()
    {
        var recue = new TaskCompletionSource<String>();
        var plugin = Task.Run(async () =>
        {
            await using var serveur = new NamedPipeServerStream(this._tube, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await serveur.WaitForConnectionAsync();
            using var lecteur = new StreamReader(serveur, leaveOpen: true);
            await using var ecrivain = new StreamWriter(serveur, leaveOpen: true) { AutoFlush = true };
            recue.SetResult(await lecteur.ReadLineAsync() ?? "");
            await ecrivain.WriteLineAsync("""{"schema":1,"source":"cached-pad-snapshot"}""");
        });
        var (code, sortie, _, _) = this.Pad("snapshot");
        plugin.Wait(TestContext.Current.CancellationToken);
        Assert.Equal(0, code);
        Assert.Equal("snapshot", recue.Task.Result);
        Assert.Equal("""{"schema":1,"source":"cached-pad-snapshot"}""", sortie);
    }

    [Fact]
    public void Plugin_absent_apres_une_seconde()
    {
        var (code, sortie, _, duree) = this.Pad("snapshot");
        Assert.Equal(1, code);
        Assert.Contains("absent", sortie);
        Assert.InRange(duree.TotalSeconds, 1, 9);
    }
}

using System.Diagnostics;
using System.IO.Pipes;

namespace NexusKeypad.Tests;

// The pad's test tool outils\pad.ps1, against a fake plugin's pipe (NEXUSKEYPAD_PAD_PIPE: a unique name per test, so even
// a broken guard could never reach the real pad). Guard of 25/10: a press or a reading needs --reel, refused before any
// connection.
public sealed class PadTests
{
    private readonly String _tube = "nexuskeypad-test-" + Guid.NewGuid().ToString("N");

    private async Task<(Int32 Code, String Sortie, String Erreur, TimeSpan Duree)> Pad(params String[] arguments)
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
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        limite.CancelAfter(TimeSpan.FromSeconds(30));
        try { await pwsh.WaitForExitAsync(limite.Token); }
        catch (OperationCanceledException)
        {
            if (!pwsh.HasExited) { pwsh.Kill(entireProcessTree: true); }
            throw;
        }

        return (pwsh.ExitCode, (await sortie).Trim(), await erreur, horloge.Elapsed);
    }

    [Theory]
    [InlineData("appui", "1")]
    [InlineData("releve")]
    public async Task Geste_reel_refuse_sans_reel(params String[] commande)
    {
        using var serveur = new NamedPipeServerStream(this._tube, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var connexion = serveur.WaitForConnectionAsync(TestContext.Current.CancellationToken);
        var (code, _, erreur, _) = await this.Pad(commande);
        Assert.Equal(2, code);
        Assert.Contains("--reel", erreur);
        Assert.False(connexion.IsCompleted, "the refused gesture must not even connect to the pipe");
    }

    [Fact]
    public async Task Une_commande_une_ligne_de_reponse()
    {
        // Register the server before starting the client: a queued Task.Run can otherwise miss the client's one-second
        // absent-plugin window on a busy runner, leaving an unbounded wait for a connection that will never arrive.
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        limite.CancelAfter(TimeSpan.FromSeconds(30));
        await using var serveur = new NamedPipeServerStream(this._tube, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        async Task<String> Repondre()
        {
            await serveur.WaitForConnectionAsync(limite.Token);
            using var lecteur = new StreamReader(serveur, leaveOpen: true);
            await using var ecrivain = new StreamWriter(serveur, leaveOpen: true) { AutoFlush = true };
            var recue = await lecteur.ReadLineAsync(limite.Token) ?? "";
            await ecrivain.WriteLineAsync("""{"schema":1,"source":"cached-pad-snapshot"}""".AsMemory(), limite.Token);
            return recue;
        }

        var plugin = Repondre();
        try
        {
            var (code, sortie, _, _) = await this.Pad("snapshot");
            Assert.Equal(0, code);
            Assert.Equal("snapshot", await plugin);
            Assert.Equal("""{"schema":1,"source":"cached-pad-snapshot"}""", sortie);
        }
        finally { await limite.CancelAsync(); }
    }

    [Fact]
    public async Task Plugin_absent_apres_une_seconde()
    {
        var (code, sortie, _, duree) = await this.Pad("snapshot");
        Assert.Equal(1, code);
        Assert.Contains("absent", sortie);
        Assert.InRange(duree.TotalSeconds, 1, 9);
    }
}

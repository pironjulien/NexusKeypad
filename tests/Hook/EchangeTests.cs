using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NexusKeypad.Tests;

// Update of the complete edition: the executable swaps the plugin's folder, detached from the plugin; a failure puts the
// old one back. Temporary folders only (never the real plugins folder nor the real Logi service: outside the real folder,
// only the processes named by NEXUSKEYPAD_SWAP_PROCESSES are stopped). One class per case: the failure paths wait for the
// swap's own retries, so the cases run in parallel.
public abstract class EchangeTests : IDisposable
{
    protected readonly String _d = Directory.CreateTempSubdirectory("nexuskeypad-swap-").FullName;

    public void Dispose()
    {
        for (var essai = 0; ; essai++)
        {
            try
            {
                Directory.Delete(this._d, recursive: true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && essai < 50)
            {
                Thread.Sleep(100);
            }
        }
    }

    protected static void Paquet(String dossier, String version)
    {
        Directory.CreateDirectory(Path.Combine(dossier, "metadata"));
        File.WriteAllText(Path.Combine(dossier, "metadata", "LoupedeckPackage.yaml"), $"type: plugin4\nversion: {version}\n");
    }

    protected static String Version(String cible) =>
        File.ReadAllText(Path.Combine(cible, "metadata", "LoupedeckPackage.yaml")).Split("version: ")[1].Trim();

    protected (Int32 Code, String Cible, String Ancien, JsonNode Etat) Echanger(Boolean nouveauPresent = true, Func<String, IDisposable?>? tenir = null,
        Dictionary<String, String?>? env = null)
    {
        var cible = Path.Combine(this._d, "Plugins", "NexusKeypad");
        var nouveau = Path.Combine(this._d, "maj", "nouveau");
        var ancien = Path.Combine(this._d, "maj", "ancien");
        Paquet(cible, "1.3.0");
        Directory.CreateDirectory(Path.GetDirectoryName(nouveau)!);
        if (nouveauPresent)
        {
            Paquet(nouveau, "1.3.1");
        }

        var etat = Path.Combine(this._d, "status", "keypad.json");
        var environnement = new Dictionary<String, String?> { ["NEXUSKEYPAD_SWAP_PROCESSES"] = "", ["NEXUSKEYPAD_SWAP_AGENT"] = "" };
        foreach (var (cle, valeur) in env ?? [])
        {
            environnement[cle] = valeur;
        }

        var tenu = tenir?.Invoke(cible); // the folder held as by the Logi service (loaded DLLs)
        ExecutableTests.Sortie sortie;
        try
        {
            sortie = ExecutableTests.Lancer(["swap", nouveau, cible, ancien, etat, "1.3.1"], "", environnement, delai: 90000);
        }
        finally
        {
            tenu?.Dispose();
        }

        return (sortie.Code, cible, ancien, JsonNode.Parse(File.ReadAllText(etat))!);
    }

    protected static String S(JsonNode etat, String cle) => etat[cle]!.GetValue<String>();
}

public sealed class EchangeReussiTests : EchangeTests
{
    [Fact]
    public void Echange_reussi()
    {
        var (code, cible, ancien, etat) = this.Echanger();
        Assert.Equal(0, code);
        Assert.Equal("1.3.1", Version(cible));
        Assert.False(Directory.Exists(ancien), "sauvegarde effacée");
        Assert.Equal(("done", "1.3.1", "keypad"), (S(etat, "state"), S(etat, "version"), S(etat, "product")));
    }

}

public sealed class EchangeEchecTests : EchangeTests
{
    [Fact]
    public void Echec_remet_l_ancien()
    {
        var (code, cible, _, etat) = this.Echanger(nouveauPresent: false);
        Assert.Equal(1, code);
        Assert.Equal("1.3.0", Version(cible));
        Assert.Equal(("failed", "1.3.0", "1.3.1"), (S(etat, "state"), S(etat, "version"), S(etat, "latest"))); // the installed version, not null
        Assert.Contains("putting the new version in place", S(etat, "message"));
    }

}

public sealed class EchangeDossierTenuTests : EchangeTests
{
    [Fact]
    public void Dossier_tenu_sans_service_a_arreter()
    {
        // 27/10: plugin file held, nothing to stop: failure, old version in place and reported, readable message (not the raw
        // resource key "UnauthorizedAccess_IODenied_Path").
        var (code, cible, _, etat) = this.Echanger(tenir: c => File.OpenRead(Path.Combine(c, "metadata", "LoupedeckPackage.yaml")));
        Assert.Equal(1, code);
        Assert.Equal("1.3.0", Version(cible));
        Assert.Equal(("failed", "1.3.0"), (S(etat, "state"), S(etat, "version")));
        Assert.Contains("moving the installed plugin aside", S(etat, "message"));
        Assert.DoesNotMatch(new Regex("[A-Za-z]+_[A-Za-z]+_"), S(etat, "message"));
    }

}

public sealed class EchangeServiceArreteTests : EchangeTests
{
    [Fact]
    public void Dossier_tenu_service_arrete_puis_agent_relance()
    {
        // The Logi service holds the folder: it is stopped for the swap, then the agent restarted detached. Here a fake service
        // (a copy of cmd.exe, current folder in the plugin) and a fake agent that leaves a trace.
        var faux = Path.Combine(this._d, "outils", "fauxlogi.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(faux)!);
        File.Copy(Path.Combine(Environment.GetEnvironmentVariable("SystemRoot")!, "System32", "cmd.exe"), faux);
        var trace = Path.Combine(this._d, "relance.txt");
        Process? service = null;
        try
        {
            IDisposable? Tenir(String cible)
            {
                service = Process.Start(new ProcessStartInfo(faux, "/k")
                {
                    WorkingDirectory = cible, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                });
                Thread.Sleep(500);
                return null;
            }

            var env = new Dictionary<String, String?>
            {
                ["NEXUSKEYPAD_SWAP_PROCESSES"] = "fauxlogi", ["NEXUSKEYPAD_SWAP_AGENT"] = $"\"{faux}\" /c echo relance>\"{trace}\"",
            };
            var (code, cible, _, etat) = this.Echanger(tenir: Tenir, env: env);
            Assert.True(code == 0, etat.ToJsonString());
            Assert.Equal("1.3.1", Version(cible));
            Assert.Equal(("done", "1.3.1"), (S(etat, "state"), S(etat, "version")));
            Assert.True(service!.HasExited, "faux service arrêté");
            for (var i = 0; i < 50 && !File.Exists(trace); i++)
            {
                Thread.Sleep(100);
            }

            Assert.True(File.Exists(trace), "faux agent relancé");
        }
        finally
        {
            if (service is { HasExited: false })
            {
                service.Kill();
            }

            service?.WaitForExit();
            service?.Dispose();
            // The fake agent, restarted detached, writes its trace before ending: while it runs, fauxlogi.exe cannot be
            // deleted, nor the temporary folder (intermittent failure, 27/10). Waited for, otherwise stopped.
            AttendreFin(faux);
        }
    }

    // Only the processes started from our own fake executable (exact path), never another fauxlogi.exe.
    private static void AttendreFin(String chemin, Int32 secondes = 10)
    {
        Process[] Presents() => Process.GetProcessesByName(Path.GetFileNameWithoutExtension(chemin)).Where(p =>
        {
            try
            {
                return String.Equals(p.MainModule?.FileName, chemin, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }).ToArray();

        var fin = Stopwatch.StartNew();
        while (Presents().Length > 0 && fin.Elapsed.TotalSeconds < secondes)
        {
            Thread.Sleep(100);
        }

        foreach (var p in Presents())
        {
            p.Kill();
            p.WaitForExit(5000);
        }
    }
}

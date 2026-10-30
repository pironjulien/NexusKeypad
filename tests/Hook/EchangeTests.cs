using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NexusKeypad.Tests;

// Update of the complete edition: the executable swaps the plugin's folder, detached from the plugin; a failure puts the
// old one back. Temporary folders and controller scripts only (never the real plugins folder nor real Logi service).
// One class per case: the failure paths wait for the
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
        var environnement = new Dictionary<String, String?> { ["NEXUSKEYPAD_SWAP_CONTROL_SCRIPT"] = "" };
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
    [Theory]
    [InlineData("success")]
    [InlineData("stop-failed")]
    [InlineData("start-failed")]
    [InlineData("rollback")]
    [InlineData("missing-controller")]
    public void Controleur_verifie_avant_permutation_et_relance_apres(String scenario)
    {
        // A controller fixture for an isolated plugin folder. It never accesses a process, real service or input device.
        // Its stop command checks that the old plugin is still in place; its start checks the final/rolled-back version.
        var script = Path.Combine(this._d, "control.ps1");
        File.WriteAllText(script, """
            param([string]$LogiService)
            $ErrorActionPreference = 'Stop'
            $target = Join-Path $PSScriptRoot 'Plugins\NexusKeypad\metadata\LoupedeckPackage.yaml'
            $version = [regex]::Match([IO.File]::ReadAllText($target), '(?m)^version:\s*(\S+)').Groups[1].Value
            $case = $env:NEXUSKEYPAD_TEST_CONTROL_CASE
            if ($LogiService -eq 'stop' -and $version -ne '1.3.0') { throw 'The target changed before service stop.' }
            if ($LogiService -eq 'start' -and $version -ne $(if ($case -eq 'rollback') { '1.3.0' } else { '1.3.1' })) { throw 'The service restarted before the final plugin was in place.' }
            [IO.File]::AppendAllText((Join-Path $PSScriptRoot 'control.log'), $LogiService + "`n")
            if (($case -eq 'stop-failed' -and $LogiService -eq 'stop') -or ($case -eq 'start-failed' -and $LogiService -eq 'start')) { exit 1 }
            """);
        var controleur = scenario == "missing-controller" ? Path.Combine(this._d, "absent.ps1") : script;
        var env = new Dictionary<String, String?>
        {
            ["NEXUSKEYPAD_SWAP_CONTROL_SCRIPT"] = controleur, ["NEXUSKEYPAD_TEST_CONTROL_CASE"] = scenario,
        };
        var (code, cible, _, etat) = this.Echanger(nouveauPresent: scenario != "rollback", env: env);
        var trace = Path.Combine(this._d, "control.log");
        var commandes = File.Exists(trace) ? File.ReadAllLines(trace) : [];
        if (scenario == "success")
        {
            Assert.True(code == 0, etat.ToJsonString());
            Assert.Equal("1.3.1", Version(cible));
            Assert.Equal(new[] { "stop", "start" }, commandes);
            Assert.Equal("done", S(etat, "state"));
        }
        else
        {
            Assert.Equal(1, code);
            Assert.Equal(scenario == "start-failed" ? "1.3.1" : "1.3.0", Version(cible));
            Assert.Equal("failed", S(etat, "state"));
            String[] attendues = scenario == "missing-controller" ? [] : scenario == "stop-failed" ? ["stop"] : ["stop", "start"];
            Assert.Equal(attendues, commandes);
        }
    }

    [Fact]
    public void Le_vrai_dossier_utilise_le_controleur_du_paquet()
    {
        var cible = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Logi", "LogiPluginService", "Plugins", "NexusKeypad");
        var logi = NexusKeypad.Hook.Echange.LogiPour(cible);
        Assert.NotNull(logi);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "get.ps1"), logi.Script);
    }
}

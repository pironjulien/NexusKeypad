using System.Text.Json;
using Loupedeck.TrinitePlugin;
using static NexusKeypad.Tests.Commun;

namespace NexusKeypad.Tests;

// State written for the Nexus app when the plugin loads, and the installed version read from its package. Temporary
// folders only.
public sealed class EtatPluginTests
{
    [Fact]
    public void Version_et_etat_pour_l_appli_Nexus()
    {
        var dossier = Directory.CreateTempSubdirectory("nexus-etat").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(dossier, "metadata"));
            File.WriteAllText(Path.Combine(dossier, "metadata", "LoupedeckPackage.yaml"), "name: NexusKeypad\nversion: 1.3.1\n");
            Vrai(EtatPlugin.Version(dossier) == "1.3.1", "état : version lue dans le paquet");
            Vrai(EtatPlugin.Version(Path.Combine(dossier, "absent")) == "", "état : pas de paquet, pas de version");
            var etat = Path.Combine(dossier, "status", "keypad.json");
            EtatPlugin.Ecrire("1.3.1", "installed", etat);
            var lu = JsonDocument.Parse(File.ReadAllText(etat)).RootElement;
            Vrai(lu.GetProperty("product").GetString() == "keypad" && lu.GetProperty("version").GetString() == "1.3.1"
                && lu.GetProperty("state").GetString() == "idle" && lu.TryGetProperty("checked_at", out _), "état : au contrat de l'appli Nexus");
        }
        finally
        {
            Directory.Delete(dossier, true);
        }
    }
}

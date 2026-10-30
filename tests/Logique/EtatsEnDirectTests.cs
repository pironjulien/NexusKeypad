using System.Text.Json.Nodes;
using Loupedeck.TrinitePlugin;
using static NexusKeypad.Tests.Commun;

namespace NexusKeypad.Tests;

// Live states: the plugin sets and removes the hooks (other tools' kept, idempotent), Antigravity's format, and nothing
// changed when setting them fails. Temporary folders only.
public sealed class EtatsEnDirectTests
{
    private static JsonObject J(String s) => (JsonObject)JsonNode.Parse(s);

    [Fact]
    public void Le_catalogue_actif_precede_un_ancien_catalogue_desactive()
    {
        var reglages = J("{\"enabledPlugins\":{\"nexus-keypad@nexus\":false,\"nexus-keypad@nexus-plugins\":true}}");
        Vrai(EtatsEnDirect.PluginCompletDeclare(reglages) == EtatsEnDirect.PluginClaudePublic
            && EtatsEnDirect.PluginComplet(reglages) == true && EtatsEnDirect.ClaudeAvecPad(reglages),
            "The selected public identity owns activity; a disabled private declaration must not override it.");
    }

    [Fact]
    public void Hooks_du_pad_poses_et_retires_sans_toucher_aux_autres()
    {
        var autre = """{"type": "command", "command": "& share-session.ps1", "timeout": 30}""";
        var ancien = """{"type": "command", "command": "python -I -B (Join-Path $env:OneDrive 'Documents\\NexusHarness\\NexusKeypad\\etat\\etat.py') claude SessionEnd"}""";
        var reglages = J("{\"model\": \"x\", \"hooks\": {\"SessionEnd\": [{\"hooks\": [" + ancien + ", " + autre + "]}], \"Stop\": [{\"hooks\": [" + autre + "]}]}}");
        EtatsEnDirect.ArrangerClaude(reglages, pose: true);
        EtatsEnDirect.ArrangerClaude(reglages, pose: true);
        var commandes = reglages["hooks"]!.AsObject().SelectMany(p => p.Value!.AsArray()).SelectMany(g => g!["hooks"]!.AsArray())
            .Select(h => h!["command"]!.GetValue<String>()).ToList();
        Vrai(commandes.Count(c => c == "& share-session.ps1") == 2 && commandes.Count(c => c.Contains("nexuskeypad-hook")) == EtatsEnDirect.EvenementsClaude.Length
            && !commandes.Any(c => c.Contains("etat.py")) && reglages["model"]!.GetValue<String>() == "x",
            "états en direct : un hook du pad par événement, etat.py remplacé, ceux des autres gardés, idempotent");
        Vrai(EtatsEnDirect.ClaudeAvecPad(reglages) && reglages["hooks"]!["Stop"]!.AsArray().Last()!["hooks"]![0]!["async"]!.GetValue<Boolean>(),
            "états en direct : hooks de Claude en tâche de fond");
        EtatsEnDirect.ArrangerClaude(reglages, pose: false);
        Vrai(!EtatsEnDirect.ClaudeAvecPad(reglages) && reglages["hooks"]!.AsObject().Count == 2
            && reglages["hooks"]!.AsObject().All(p => p.Value!.AsArray().Count == 1),
            "états en direct : retrait = seulement ceux du pad, événements vidés retirés");
        Vrai(!EtatsEnDirect.DuPad(J("""{"command": "pwsh -File audit.ps1 -Project NexusKeypad"}""")), "états en direct : un outil qui cite le projet n'est pas du pad");
        var agy = J("""{"autre-outil": {}}""");
        EtatsEnDirect.ArrangerAntigravity(agy, @"C:\X\nexuskeypad-hook.exe");
        var entree = agy[EtatsEnDirect.CleAntigravity]!;
        Vrai(agy.ContainsKey("autre-outil") && entree["PreToolUse"]![0]!["matcher"]!.GetValue<String>() == "*"
            && entree["Stop"]![0]!["command"]!.GetValue<String>() == @"C:\X\nexuskeypad-hook.exe antigravity Stop",
            "états en direct : format d'Antigravity (outils en enveloppe { matcher, hooks }, sinon gestionnaires directs)");
    }

    [Theory]
    [InlineData(EtatsEnDirect.PluginClaude)]
    [InlineData(EtatsEnDirect.PluginClaudePublic)]
    public void Plugin_complet_prive_ou_du_catalogue_NexusSystem_sans_hooks_en_double(String identite)
    {
        var dossier = Directory.CreateTempSubdirectory("nexus-etats-complet").FullName;
        try
        {
            var claude = Path.Combine(dossier, ".claude", "settings.json");
            var gemini = Path.Combine(dossier, ".gemini", "config", "hooks.json");
            Directory.CreateDirectory(Path.GetDirectoryName(claude));
            Directory.CreateDirectory(Path.GetDirectoryName(gemini));
            var paquet = Path.Combine(dossier, "paquet", "nexuskeypad-hook.exe");
            Directory.CreateDirectory(Path.Combine(dossier, "paquet", EtatsEnDirect.DossierModsPaquet, ".claude-plugin"));
            File.WriteAllText(paquet, "exe");
            File.WriteAllText(Path.Combine(dossier, "paquet", EtatsEnDirect.DossierModsPaquet, ".claude-plugin", "marketplace.json"), "{}");
            var exe = Path.Combine(dossier, "local", "nexuskeypad-hook.exe");
            var catalogue = Path.Combine(dossier, "local", "claude-marketplace");
            File.WriteAllText(claude, new JsonObject { ["hooks"] = new JsonObject(), ["enabledPlugins"] = new JsonObject { [identite] = false } }.ToJsonString());
            Vrai(EtatsEnDirect.PluginCompletDeclare(J(File.ReadAllText(claude))) == identite, "identité du plugin complet reconnue : " + identite);
            Vrai(EtatsEnDirect.Appliquer(paquet, true, claude, gemini, exe, catalogue), "pose avec le plugin complet " + identite);
            var pose = J(File.ReadAllText(claude));
            Vrai(pose["hooks"]!.AsObject().Count == 0 && pose["enabledPlugins"]![identite]!.GetValue<Boolean>() && pose["enabledPlugins"]!.AsObject().Count == 1
                && !pose.ContainsKey("extraKnownMarketplaces"), "plugin complet activé, aucun hook ni mod de quota en double : " + identite);
            Vrai(EtatsEnDirect.Appliquer(paquet, false, claude, gemini, exe, catalogue) && !J(File.ReadAllText(claude))["enabledPlugins"]![identite]!.GetValue<Boolean>(),
                "retrait : plugin complet coupé : " + identite);
        }
        finally
        {
            Directory.Delete(dossier, true);
        }
    }

    [Fact]
    public void Pose_et_retrait_dans_des_profils_d_essai()
    {
        var dossier = Directory.CreateTempSubdirectory("nexus-etats").FullName;
        try
        {
            var claude = Path.Combine(dossier, ".claude", "settings.json");
            var gemini = Path.Combine(dossier, ".gemini", "config", "hooks.json");
            Directory.CreateDirectory(Path.GetDirectoryName(claude));
            Directory.CreateDirectory(Path.GetDirectoryName(gemini));
            var paquet = Path.Combine(dossier, "paquet", "nexuskeypad-hook.exe");
            var exe = Path.Combine(dossier, "local avec espace", "nexuskeypad-hook.exe");
            var catalogue = Path.Combine(dossier, "local avec espace", "claude-marketplace");
            File.WriteAllText(claude, """{"hooks": {}, "enabledPlugins": {"autre@x": true}}""");
            var reglagesAvant = File.ReadAllText(claude);
            Vrai(!EtatsEnDirect.Appliquer(paquet, true, claude, gemini, exe, catalogue) && File.ReadAllText(claude) == reglagesAvant && !File.Exists(gemini),
                "états en direct : pose échouée (exécutable absent) → rien de modifié");
            Directory.CreateDirectory(Path.GetDirectoryName(paquet));
            File.WriteAllText(paquet, "exe");
            Vrai(!EtatsEnDirect.Appliquer(paquet, true, claude, gemini, exe, catalogue) && File.ReadAllText(claude) == reglagesAvant && !File.Exists(gemini),
                "états en direct : pose échouée (mod du quota absent du paquet) → rien de modifié");
            var mod = Path.Combine(dossier, "paquet", EtatsEnDirect.DossierModsPaquet, ".claude-plugin", "marketplace.json");
            Directory.CreateDirectory(Path.GetDirectoryName(mod));
            File.WriteAllText(mod, "{}");
            Vrai(EtatsEnDirect.Appliquer(paquet, true, claude, gemini, exe, catalogue) && File.Exists(exe)
                && EtatsEnDirect.ClaudeAvecPad(J(File.ReadAllText(claude))) && !J(File.ReadAllText(gemini))[EtatsEnDirect.CleAntigravity]!.ToJsonString().Contains("avec espace"),
                "états en direct : pose (exécutable copié, Claude, Antigravity par un chemin sans espace)");
            var pose = J(File.ReadAllText(claude));
            Vrai(File.Exists(Path.Combine(catalogue, ".claude-plugin", "marketplace.json"))
                && pose["extraKnownMarketplaces"]![EtatsEnDirect.Catalogue]!["source"]!["path"]!.GetValue<String>() == catalogue
                && pose["extraKnownMarketplaces"]![EtatsEnDirect.Catalogue]!["source"]!["source"]!.GetValue<String>() == "directory"
                && pose["enabledPlugins"]![EtatsEnDirect.ModQuota]!.GetValue<Boolean>() && pose["enabledPlugins"]!["autre@x"]!.GetValue<Boolean>(),
                "états en direct : mod du quota (catalogue local copié, déclaré et activé ; autres plugins gardés)");
            Vrai(EtatsEnDirect.Appliquer(paquet, false, claude, gemini, exe, catalogue) && !EtatsEnDirect.ClaudeAvecPad(J(File.ReadAllText(claude)))
                && !J(File.ReadAllText(gemini)).ContainsKey(EtatsEnDirect.CleAntigravity),
                "états en direct : retrait chez les deux");
            var retire = J(File.ReadAllText(claude));
            Vrai(!retire.ContainsKey("extraKnownMarketplaces") && retire["enabledPlugins"]!.AsObject().Count == 1 && !Directory.Exists(catalogue),
                "états en direct : retrait du mod du quota (clés et catalogue), autres plugins gardés");

            // Complete Claude plugin declared (nexus-keypad@nexus): it carries hooks and quota; the pad enables or disables it,
            // never setting its hooks or the old quota mod twice.
            File.WriteAllText(claude, new JsonObject { ["hooks"] = new JsonObject(), ["enabledPlugins"] = new JsonObject { [EtatsEnDirect.PluginClaude] = false } }.ToJsonString());
            Vrai(!EtatsEnDirect.ClaudeAvecPad(J(File.ReadAllText(claude))), "états en direct : plugin complet coupé → pas posé");
            var complet = EtatsEnDirect.Appliquer(paquet, true, claude, gemini, exe, catalogue) ? J(File.ReadAllText(claude)) : null;
            Vrai(complet is not null && EtatsEnDirect.ClaudeAvecPad(complet) && complet["hooks"]!.AsObject().Count == 0
                && complet["enabledPlugins"]![EtatsEnDirect.PluginClaude]!.GetValue<Boolean>()
                && complet["enabledPlugins"]![EtatsEnDirect.ModQuota] is null && !complet.ContainsKey("extraKnownMarketplaces"),
                "états en direct : pose avec le plugin complet (activé, aucun hook ni mod de quota en double)");
            Vrai(EtatsEnDirect.Appliquer(paquet, false, claude, gemini, exe, catalogue) && !EtatsEnDirect.ClaudeAvecPad(J(File.ReadAllText(claude)))
                && !J(File.ReadAllText(claude))["enabledPlugins"]![EtatsEnDirect.PluginClaude]!.GetValue<Boolean>(),
                "états en direct : retrait avec le plugin complet (coupé)");
        }
        finally
        {
            Directory.Delete(dossier, true);
        }
    }
}

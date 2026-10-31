using System.Reflection;
using System.Text.Json.Nodes;
using Loupedeck.TrinitePlugin;

namespace NexusKeypad.Tests;

// The connectors' setup, repair, migration and rollback, in isolated profiles only (never the live workstation).
public sealed class ConnecteursTests
{
    private static void Vrai(Boolean condition, String regle) => Assert.True(condition, regle);

    private static String Depot => typeof(ConnecteursTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "NexusKeypad.Repository").Value;

    [Fact]
    public void Un_seul_dossier_connecteur_pour_les_trois_IA()
    {
        // One folder, one guide: each host's manifest carries the connector version, and no per-host copy remains.
        var dossier = Path.Combine(Depot, "connecteurs");
        foreach (var manifeste in new[] { ".claude-plugin/plugin.json", ".codex-plugin/plugin.json", "plugin.json" })
        {
            Assert.Equal(Connecteurs.Version, JsonNode.Parse(File.ReadAllText(Path.Combine(dossier, manifeste)))!["version"]!.GetValue<String>());
        }
        Assert.Single(Directory.GetFiles(dossier, "SKILL.md", SearchOption.AllDirectories));
        Assert.False(Directory.Exists(Path.Combine(dossier, "antigravity")), "Ancienne copie Antigravity restante.");
    }

    [Fact]
    public void Tableaux_et_plugins_tiers_du_profil_conserves()
    {
        // Real profiles contain TOML arrays (TomlArray does not necessarily implement the non-generic IList).
        var tableauxActives = Connecteurs.ActiverToml("model = 'keep'\nnotify = ['one', 'two']\n[plugins.'other@personal']\nenabled = false\n", "nexus-keypad@personal", true);
        Vrai(tableauxActives.Contains("notify = ['one', 'two']") && tableauxActives.Contains("other@personal"), "Les tableaux et plugins tiers du profil doivent être conservés.");
    }

    [Theory]
    [InlineData("# retained\nmodel = \"a\"\n[plugins.'nexus-keypad@personal']\nenabled = false # keep this\nother = 2\n")]
    [InlineData("[plugins]\n'nexus-keypad@personal' = { enabled = false, keep = 'x' }\n")]
    [InlineData("plugins = { 'nexus-keypad@personal' = { enabled = false, keep = 'x' }, other = { enabled = true } }\n")]
    [InlineData("[plugins.'nexus-keypad@personal']\nkeep = \"\"\"\n[pretend.table]\nenabled = false\n\"\"\"\n")]
    [InlineData("[plugins]\nother = { enabled = true }\n")]
    [InlineData("model = 'x'\n")]
    public void Variantes_de_syntaxe_TOML(String texte)
    {
        var actif = Connecteurs.ActiverToml(texte, "nexus-keypad@personal", true);
        Vrai(actif.Contains("enabled = true"), "enable TOML syntax variant");
        var inactif = Connecteurs.ActiverToml(actif, "nexus-keypad@personal", false);
        Vrai(inactif.Contains("enabled = false"), "disable TOML syntax variant");
        if (texte.Contains("# retained"))
        {
            Vrai(actif.Contains("# retained") && actif.Contains("# keep this"), "TOML comments retained");
        }
    }

    [Fact]
    public void Plugin_Claude_du_catalogue_public_NexusSystem_reconnu_sans_doublon()
    {
        // The complete plugin installed from NexusSystem's public catalog (nexus-keypad@nexus-plugins) is the connector:
        // enabled in place, never a second public identity nor the pad's own hooks or quota mod.
        var racine = Directory.CreateTempSubdirectory("nexuskeypad-connecteurs-public-").FullName;
        try
        {
            var maison = Path.Combine(racine, "user");
            var local = Path.Combine(racine, "local");
            var paquet = Path.Combine(racine, "package");
            Directory.CreateDirectory(paquet);
            var executable = Path.Combine(paquet, "nexuskeypad-hook.exe");
            File.WriteAllText(executable, "isolated fixture, never executed");
            foreach (var fichier in Directory.EnumerateFiles(Path.Combine(Depot, "connecteurs"), "*", SearchOption.AllDirectories))
            {
                var cible = Path.Combine(paquet, "connecteurs", Path.GetRelativePath(Path.Combine(Depot, "connecteurs"), fichier));
                Directory.CreateDirectory(Path.GetDirectoryName(cible));
                File.Copy(fichier, cible);
            }

            var quota = Path.Combine(paquet, "claude-quota", "nexuskeypad-quota", "hooks", "register.ts");
            Directory.CreateDirectory(Path.GetDirectoryName(quota));
            File.Copy(Path.Combine(Depot, "claude-quota", "nexuskeypad-quota", "hooks", "register.ts"), quota);
            Connecteurs.DossiersEssai(maison, local, executable);
            var appels = new List<String>();
            Connecteurs.CommandeClaudeEssai(arguments => { appels.Add(String.Join("|", arguments)); return (0, "{\"outcome\":\"ok\"}"); });
            var installe = Path.Combine(maison, ".claude", "plugins", "cache", "nexus-plugins", "nexus-keypad", "2.2.0");
            Directory.CreateDirectory(Path.Combine(installe, ".claude-plugin"));
            Directory.CreateDirectory(Path.Combine(installe, "hooks"));
            Directory.CreateDirectory(Path.Combine(installe, "skills", "nexus-keypad"));
            File.WriteAllText(Path.Combine(installe, ".claude-plugin", "plugin.json"), "{\"name\":\"nexus-keypad\",\"version\":\"2.2.0\"}");
            File.WriteAllText(Path.Combine(installe, "hooks", "hooks.json"), "{\"hooks\":{}}");
            File.Copy(Path.Combine(Depot, "connecteurs", "skills", "nexus-keypad", "SKILL.md"), Path.Combine(installe, "skills", "nexus-keypad", "SKILL.md"));
            var reglagesChemin = Path.Combine(maison, ".claude", "settings.json");
            File.WriteAllText(reglagesChemin, "{\"enabledPlugins\":{\"nexus-keypad@nexus-plugins\":false,\"third-party@other\":true}}");
            File.WriteAllText(Path.Combine(maison, ".claude", "plugins", "installed_plugins.json"), new JsonObject { ["version"] = 2, ["plugins"] = new JsonObject {
                [EtatsEnDirect.PluginClaudePublic] = new JsonArray(new JsonObject { ["scope"] = "user", ["installPath"] = installe }) } }.ToJsonString());

            // A real catalog migration leaves an old disabled private plugin installed while the selected public one
            // is still pending in Claude's manager. No fallback may reactivate the old identity or forge a ready receipt.
            var prive = Path.Combine(maison, ".claude", "plugins", "cache", "nexus", "nexus-keypad", "2.1.4");
            foreach (var fichier in Directory.EnumerateFiles(installe, "*", SearchOption.AllDirectories))
            {
                var cible = Path.Combine(prive, Path.GetRelativePath(installe, fichier));
                Directory.CreateDirectory(Path.GetDirectoryName(cible));
                File.Copy(fichier, cible);
            }

            var registre = Path.Combine(maison, ".claude", "plugins", "installed_plugins.json");
            var installs = new JsonObject { ["version"] = 2, ["plugins"] = new JsonObject {
                [EtatsEnDirect.PluginClaude] = new JsonArray(new JsonObject { ["scope"] = "user", ["installPath"] = prive }) } };
            File.WriteAllText(registre, installs.ToJsonString());
            File.WriteAllText(reglagesChemin, "{\"enabledPlugins\":{\"nexus-keypad@nexus\":false,\"third-party@other\":true}}");
            Vrai(Connecteurs.Activer(0).Etat == EtatConnexion.Pret, "old private connector prepared before catalog migration");
            var recuChemin = Path.Combine(local, "NexusKeypad", "connecteurs", "etats", "claude.json");
            var recuAvant = File.ReadAllBytes(recuChemin);
            var migration = "{\"unrelated\":42,\"enabledPlugins\":{\"nexus-keypad@nexus\":false,\"nexus-keypad@nexus-plugins\":true,\"third-party@other\":true}}\n";
            File.WriteAllText(reglagesChemin, migration);
            Vrai(Connecteurs.LireConfiguration(0).Etat == EtatConnexion.EnCours, "public plugin absent from manager is pending, not the old private connector");
            Vrai(Connecteurs.Activer(0).Etat == EtatConnexion.EnCours, "enable waits for selected public plugin instead of reactivating private copy");
            Vrai(File.ReadAllText(reglagesChemin) == migration && File.ReadAllBytes(recuChemin).AsSpan().SequenceEqual(recuAvant),
                "pending migration preserves every setting and previous receipt");
            installs["plugins"]![EtatsEnDirect.PluginClaudePublic] = new JsonArray(new JsonObject { ["scope"] = "user", ["installPath"] = installe + "-pending" });
            File.WriteAllText(registre, installs.ToJsonString());
            Vrai(Connecteurs.Activer(0).Etat == EtatConnexion.EnCours, "manager entry without a completed cache still waits");
            Vrai(Connecteurs.Desactiver(0).Etat == EtatConnexion.Activer, "a pending selected plugin remains explicitly disableable without its cache");
            var desactive = JsonNode.Parse(File.ReadAllText(reglagesChemin))!;
            Vrai(!desactive["enabledPlugins"]![EtatsEnDirect.PluginClaudePublic]!.GetValue<Boolean>()
                && !desactive["enabledPlugins"]![EtatsEnDirect.PluginClaude]!.GetValue<Boolean>(), "pending disable keeps the old private identity disabled");
            Vrai(Connecteurs.Activer(0).Etat == EtatConnexion.EnCours
                && !JsonNode.Parse(File.ReadAllText(reglagesChemin))!["enabledPlugins"]![EtatsEnDirect.PluginClaude]!.GetValue<Boolean>(),
                "a declared public catalog keeps ownership after disable; enable never falls back to disabled private cache");
            File.WriteAllText(reglagesChemin, migration);
            File.WriteAllBytes(recuChemin, recuAvant);
            installs["plugins"]![EtatsEnDirect.PluginClaudePublic]![0]!["installPath"] = installe;
            File.WriteAllText(registre, installs.ToJsonString());
            var hooksPublics = Path.Combine(installe, "hooks", "hooks.json");
            var hooksAvant = File.ReadAllBytes(hooksPublics);
            File.Delete(hooksPublics);
            Vrai(Connecteurs.Activer(0).Etat == EtatConnexion.EnCours && File.ReadAllText(reglagesChemin) == migration,
                "an incomplete public cache stays pending without falling back to the complete private cache");
            File.WriteAllBytes(hooksPublics, hooksAvant);
            Vrai(Connecteurs.LireConfiguration(0).Etat == EtatConnexion.Reparer, "complete public cache still requires adopting its identity, not a fabricated receipt");

            var active = Connecteurs.Activer(0);
            Vrai(active.Etat == EtatConnexion.Pret, "public NexusSystem Claude plugin activated as the connector: " + active.Detail);
            var reglages = JsonNode.Parse(File.ReadAllText(reglagesChemin))!.AsObject();
            Vrai(reglages["enabledPlugins"]![EtatsEnDirect.PluginClaudePublic]!.GetValue<Boolean>()
                && !reglages["enabledPlugins"]![EtatsEnDirect.PluginClaude]!.GetValue<Boolean>()
                && reglages["enabledPlugins"]!["nexus-keypad@nexuskeypad"] is null && reglages["enabledPlugins"]!["third-party@other"]!.GetValue<Boolean>(),
                "public NexusSystem identity enabled in place, no duplicate identity, third parties kept");
            Vrai(File.ReadAllText(reglagesChemin) == migration, "adopting the installed public identity preserves settings byte for byte");
            Vrai(appels.Count == 0, "the official manager is not asked to install a competing plugin");
            Vrai(!EtatsEnDirect.ClaudeAvecPad(new JsonObject { ["hooks"] = reglages["hooks"]?.DeepClone() }), "no pad hooks set next to the plugin's own");
            var recu = JsonNode.Parse(File.ReadAllText(Path.Combine(local, "NexusKeypad", "connecteurs", "etats", "claude.json")))!;
            Vrai(recu["identity"]!.GetValue<String>() == EtatsEnDirect.PluginClaudePublic, "receipt names the public NexusSystem identity");
            Vrai(Connecteurs.LireConfiguration(0).Etat == EtatConnexion.Pret && Connecteurs.EvenementsActifs("claude"), "configuration read back as ready");
            var nouveauCache = Path.Combine(Path.GetDirectoryName(installe), "2.3.0");
            foreach (var fichier in Directory.EnumerateFiles(installe, "*", SearchOption.AllDirectories))
            {
                var cible = Path.Combine(nouveauCache, Path.GetRelativePath(installe, fichier));
                Directory.CreateDirectory(Path.GetDirectoryName(cible));
                File.Copy(fichier, cible);
            }
            installs["plugins"]![EtatsEnDirect.PluginClaudePublic]![0]!["installPath"] = nouveauCache;
            File.WriteAllText(registre, installs.ToJsonString());
            Vrai(Connecteurs.LireConfiguration(0).Etat == EtatConnexion.Reparer, "same identity with a new manager-selected cache never attests the still-present old cache");
            Vrai(Connecteurs.CopieCatalogueMiseAJour(0), "a catalog copy updated by Claude's manager is re-attested without a question");
            Vrai(Connecteurs.Activer(0).Etat == EtatConnexion.Pret && File.ReadAllText(reglagesChemin) == migration,
                "explicit repair adopts the current manager cache without changing catalog choices");
            var fichiersAdoptes = JsonNode.Parse(File.ReadAllText(recuChemin))!["files"]!.AsObject();
            Vrai(fichiersAdoptes.ContainsKey(Path.Combine(nouveauCache, "hooks/hooks.json"))
                && !fichiersAdoptes.ContainsKey(Path.Combine(installe, "hooks/hooks.json")), "receipt attests the actual selected cache only");
            var doublon = migration.Replace("\"nexus-keypad@nexus\":false", "\"nexus-keypad@nexus\":true", StringComparison.Ordinal);
            File.WriteAllText(reglagesChemin, doublon);
            Vrai(Connecteurs.LireConfiguration(0).Etat == EtatConnexion.Reparer && !Connecteurs.CopieCatalogueMiseAJour(0)
                && Connecteurs.Activer(0).Etat == EtatConnexion.Erreur
                && File.ReadAllText(reglagesChemin) == doublon, "ambiguous duplicate activation is refused without changing user choices");
            File.WriteAllText(reglagesChemin, migration);
            Vrai(Connecteurs.Desactiver(0).Etat == EtatConnexion.Activer
                && !JsonNode.Parse(File.ReadAllText(reglagesChemin))!["enabledPlugins"]![EtatsEnDirect.PluginClaudePublic]!.GetValue<Boolean>(),
                "disabling turns the public NexusSystem plugin off");
        }
        finally
        {
            Connecteurs.CommandeClaudeEssai(null);
            Directory.Delete(racine, recursive: true);
        }
    }

    [Fact]
    public void Plugin_Codex_des_catalogues_Nexus_jamais_en_double()
    {
        // NexusSystem's catalog copy (nexus-keypad@nexus-plugins), once Codex installed it, is the connector, as for
        // Claude; without that catalog the connector installs a personal copy. One identity only ever carries the hooks.
        var racine = Directory.CreateTempSubdirectory("nexuskeypad-connecteurs-codex-").FullName;
        try
        {
            var maison = Path.Combine(racine, "user");
            var local = Path.Combine(racine, "local");
            var paquet = Path.Combine(racine, "package");
            var executable = Path.Combine(paquet, "nexuskeypad-hook.exe");
            foreach (var fichier in Directory.EnumerateFiles(Path.Combine(Depot, "connecteurs"), "*", SearchOption.AllDirectories))
            {
                var cible = Path.Combine(paquet, "connecteurs", Path.GetRelativePath(Path.Combine(Depot, "connecteurs"), fichier));
                Directory.CreateDirectory(Path.GetDirectoryName(cible));
                File.Copy(fichier, cible);
            }

            var quota = Path.Combine(paquet, "claude-quota", "nexuskeypad-quota", "hooks", "register.ts");
            Directory.CreateDirectory(Path.GetDirectoryName(quota));
            File.Copy(Path.Combine(Depot, "claude-quota", "nexuskeypad-quota", "hooks", "register.ts"), quota);
            File.WriteAllText(executable, "isolated fixture, never executed");
            Connecteurs.DossiersEssai(maison, local, executable);
            var config = Path.Combine(maison, ".codex", "config.toml");
            Directory.CreateDirectory(Path.GetDirectoryName(config));
            String Texte() => File.ReadAllText(config);

            // Without NexusSystem's catalog, the connector installs its own personal copy (the same files).
            File.WriteAllText(config, "model = 'keep'\n[plugins.\"nexus-keypad@nexus\"]\nenabled = true\n[plugins.\"other@nexus\"]\nenabled = true\n");
            Vrai(Connecteurs.Activer(1).Etat == EtatConnexion.Pret, "personal Codex connector without the NexusSystem catalog");
            Vrai(Actif(Texte(), "nexus-keypad@personal") && !Actif(Texte(), EtatsEnDirect.PluginClaude), "personal copy enabled, private copy turned off");
            var catalogue = Path.Combine(maison, ".agents", "plugins", "marketplace.json");
            Vrai(File.ReadAllText(catalogue).Contains("./.codex/plugins/nexus-keypad"), "personal catalog entry");

            // NexusSystem's catalog declared, Codex not done installing it: wait, change nothing.
            File.WriteAllText(config, Connecteurs.ActiverToml(Texte(), EtatsEnDirect.PluginClaudePublic, false));
            var avant = Texte();
            var attente = Connecteurs.Activer(1);
            Vrai(attente.Etat == EtatConnexion.EnCours && attente.CodeAttente == "codex_plugin_install_pending" && Texte() == avant,
                "declared catalog copy not installed yet: pending, profile unchanged");

            // Installed by Codex: that copy is adopted as the connector; the personal one leaves.
            var installe = Path.Combine(maison, ".codex", "plugins", "cache", "nexus-plugins", "nexus-keypad", Connecteurs.Version);
            foreach (var fichier in new[] { ".codex-plugin/plugin.json", "skills/nexus-keypad/SKILL.md" })
            {
                var cible = Path.Combine(installe, fichier);
                Directory.CreateDirectory(Path.GetDirectoryName(cible));
                File.Copy(Path.Combine(Depot, "connecteurs", fichier), cible);
            }
            var active = Connecteurs.Activer(1);
            Vrai(active.Etat == EtatConnexion.Pret, "NexusSystem catalog copy adopted: " + active.Detail);
            Vrai(Connecteurs.EvenementsActifs("codex") && Actif(Texte(), EtatsEnDirect.PluginClaudePublic), "catalog identity enabled");
            Vrai(!Actif(Texte(), "nexus-keypad@personal") && !Actif(Texte(), EtatsEnDirect.PluginClaude), "every other copy turned off");
            Vrai(Texte().Contains("model = 'keep'") && Actif(Texte(), "other@nexus"), "third-party settings kept");
            Vrai(!File.ReadAllText(catalogue).Contains("nexus-keypad"), "personal catalog entry removed");
            var recu = JsonNode.Parse(File.ReadAllText(Path.Combine(local, "NexusKeypad", "connecteurs", "etats", "codex.json")))!;
            Vrai(recu["identity"]!.GetValue<String>() == EtatsEnDirect.PluginClaudePublic
                && recu["files"]!.AsObject().ContainsKey(Path.Combine(installe, ".codex-plugin/plugin.json")), "receipt attests Codex's installed copy");

            // A copy re-enabled later is reported, then turned off again by the next activation; never doubled.
            File.WriteAllText(config, Connecteurs.ActiverToml(Texte(), "nexus-keypad@personal", true));
            Vrai(Connecteurs.LireConfiguration(1).Etat == EtatConnexion.Reparer && !Connecteurs.CopieCatalogueMiseAJour(1),
                "a re-enabled personal copy is reported as doubled hooks, and keeps its question");
            Vrai(Connecteurs.Activer(1).Etat == EtatConnexion.Pret && !Actif(Texte(), "nexus-keypad@personal"), "repair turns the copy off again");

            // Codex installs a newer version: the connector adopts the new folder on its next activation.
            var nouveau = Path.Combine(Path.GetDirectoryName(installe)!, "99.0.0");
            foreach (var fichier in Directory.EnumerateFiles(installe, "*", SearchOption.AllDirectories))
            {
                var cible = Path.Combine(nouveau, Path.GetRelativePath(installe, fichier));
                Directory.CreateDirectory(Path.GetDirectoryName(cible));
                File.Copy(fichier, cible);
            }
            Vrai(Connecteurs.LireConfiguration(1).Etat == EtatConnexion.Reparer && Connecteurs.CopieCatalogueMiseAJour(1)
                && Connecteurs.Activer(1).Etat == EtatConnexion.Pret, "a newer installed copy is adopted without a question");
            Vrai(Connecteurs.Desactiver(1).Etat == EtatConnexion.Activer && !Actif(Texte(), EtatsEnDirect.PluginClaudePublic), "disabling turns the catalog copy off");
        }
        finally
        {
            Directory.Delete(racine, recursive: true);
        }

        // True when the profile enables this plugin (read as TOML).
        static Boolean Actif(String texte, String identite) =>
            Tomlyn.TomlSerializer.Deserialize<Tomlyn.Model.TomlTable>(texte) is { } profil && profil.TryGetValue("plugins", out var p)
            && p is Tomlyn.Model.TomlTable plugins && plugins.TryGetValue(identite, out var t) && t is Tomlyn.Model.TomlTable table
            && table.TryGetValue("enabled", out var v) && v is true;
    }

    // One scenario: the connectors' folders are redirected for the whole process, and each step builds on the previous one.
    [Fact]
    public void Installation_reparation_migration_et_retour_arriere()
    {
        var source = Depot;
        var racine = Directory.CreateTempSubdirectory("nexuskeypad-connecteurs-").FullName;
        try
        {
            Scenario(source, racine);
        }
        finally
        {
            Directory.Delete(racine, recursive: true);
        }
    }

    private static void Scenario(String source, String racine)
    {
        var maison = Path.Combine(racine, "user");
        var local = Path.Combine(racine, "local");
        var paquet = Path.Combine(racine, "package");
        Directory.CreateDirectory(maison);
        Directory.CreateDirectory(local);
        Directory.CreateDirectory(paquet);
        var executable = Path.Combine(paquet, "nexuskeypad-hook.exe");
        File.WriteAllText(executable, "isolated fixture, never executed");
        foreach (var fichier in Directory.EnumerateFiles(Path.Combine(source, "connecteurs"), "*", SearchOption.AllDirectories))
        {
            var cible = Path.Combine(paquet, "connecteurs", Path.GetRelativePath(Path.Combine(source, "connecteurs"), fichier));
            Directory.CreateDirectory(Path.GetDirectoryName(cible));
            File.Copy(fichier, cible);
        }

        var quota = Path.Combine(paquet, "claude-quota", "nexuskeypad-quota", "hooks", "register.ts");
        Directory.CreateDirectory(Path.GetDirectoryName(quota));
        File.Copy(Path.Combine(source, "claude-quota", "nexuskeypad-quota", "hooks", "register.ts"), quota);
        Connecteurs.DossiersEssai(maison, local, executable);
        var appelsClaude = new List<String>();
        var echecClaude = false;
        var fauxSuccesClaude = false;
        var miseAJourClaudeInchangee = false;
        (Int32, String) GestionnaireClaude(String[] arguments)
        {
            appelsClaude.Add(String.Join("|", arguments));
            if (echecClaude) { return (1, "{\"outcome\":\"failed\"}"); }
            if (fauxSuccesClaude) { return (0, "{\"outcome\":\"ok\"}"); }
            if (miseAJourClaudeInchangee && arguments[1] == "update") { return (0, "{\"outcome\":\"ok\",\"updateOutcome\":\"up_to_date\"}"); }
            var catalogue = Path.Combine(local, "NexusKeypad", "connecteurs", "claude");
            var registre = Path.Combine(maison, ".claude", "plugins");
            Directory.CreateDirectory(registre);
            if (arguments.Take(3).SequenceEqual(new[] { "plugin", "marketplace", "add" }))
            {
                File.WriteAllText(Path.Combine(registre, "known_marketplaces.json"), new JsonObject { ["nexuskeypad"] = new JsonObject {
                    ["source"] = new JsonObject { ["source"] = "directory", ["path"] = catalogue }, ["installLocation"] = catalogue } }.ToJsonString());
            }
            else if (arguments[1] == "uninstall")
            {
                File.Delete(Path.Combine(registre, "installed_plugins.json"));
            }
            else if (arguments[1] is "install" or "update")
            {
                var cible = Path.Combine(registre, "cache", "nexuskeypad", "nexus-keypad", Connecteurs.Version);
                foreach (var fichier in Directory.EnumerateFiles(Path.Combine(catalogue, "nexus-keypad"), "*", SearchOption.AllDirectories))
                {
                    var copie = Path.Combine(cible, Path.GetRelativePath(Path.Combine(catalogue, "nexus-keypad"), fichier));
                    Directory.CreateDirectory(Path.GetDirectoryName(copie)); File.Copy(fichier, copie, true);
                }

                File.WriteAllText(Path.Combine(registre, "installed_plugins.json"), new JsonObject { ["version"] = 2, ["plugins"] = new JsonObject {
                    ["nexus-keypad@nexuskeypad"] = new JsonArray(new JsonObject { ["scope"] = "user", ["version"] = Connecteurs.Version, ["installPath"] = cible }) } }.ToJsonString());
            }
            else if (arguments[1] != "enable") { throw new InvalidOperationException("Unexpected plugin-management command."); }

            return (0, "{\"outcome\":\"ok\"}");
        }

        Connecteurs.CommandeClaudeEssai(GestionnaireClaude);

        String Recu(Int32 agent) => Path.Combine(local, "NexusKeypad", "connecteurs", "etats", new[] { "claude", "codex", "antigravity" }[agent] + ".json");
        DateTime VieillirPreparation(Int32 agent)
        {
            var date = DateTime.UtcNow.AddDays(-1);
            var recu = JsonNode.Parse(File.ReadAllText(Recu(agent)));
            recu["preparedAt"] = date.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            File.WriteAllText(Recu(agent), recu.ToJsonString() + "\n");
            return date;
        }

        for (var agent = 0; agent < 3; agent++)
        {
            Vrai(Connecteurs.LireConfiguration(agent).Etat == EtatConnexion.Absent, "absent app " + agent);
            var dossier = agent switch { 0 => Path.Combine(maison, ".claude"), 1 => Path.Combine(maison, ".codex"), _ => Path.Combine(maison, ".gemini", "config") };
            Directory.CreateDirectory(dossier);
            Vrai(Connecteurs.LireConfiguration(agent).Etat == EtatConnexion.Activer, "not configured " + agent);
            if (agent == 1) { File.WriteAllText(Path.Combine(dossier, "config.toml"), "model = 'keep'\n[plugins.'third-party@personal']\nenabled = false\n"); }
            if (agent == 2) { File.WriteAllText(Path.Combine(dossier, "hooks.json"), "{\"third-party\":{\"Stop\":[]}}"); }
            var resultat = Connecteurs.Activer(agent);
            Vrai(resultat.Etat == EtatConnexion.Pret, "install verified " + agent + ": " + resultat.Detail);
            Vrai(Connecteurs.EvenementsActifs(new[] { "claude", "codex", "antigravity" }[agent]), "events enabled " + agent);
            Vrai(Connecteurs.Lire(agent, true, false).Etat == (agent == 0 ? EtatConnexion.Pret : EtatConnexion.Relancer), "port absent prevents ready " + agent);
            var anciennePreparation = VieillirPreparation(agent);
            var recuAvant = File.ReadAllBytes(Recu(agent));
            Vrai(Connecteurs.Activer(agent).PrepareLe == anciennePreparation, "identical enable preserves setup date " + agent);
            Vrai(File.ReadAllBytes(Recu(agent)).AsSpan().SequenceEqual(recuAvant), "identical enable leaves receipt unchanged " + agent);
            Vrai(Connecteurs.Desactiver(agent).Etat == EtatConnexion.Activer, "disable " + agent);
            Vrai(!Connecteurs.EvenementsActifs(new[] { "claude", "codex", "antigravity" }[agent]), "events disabled " + agent);
            if (agent == 1) { Vrai(File.ReadAllText(Path.Combine(dossier, "config.toml")).Contains("third-party@personal") && File.ReadAllText(Path.Combine(dossier, "config.toml")).Contains("model = 'keep'"), "Codex third party retained"); }
            if (agent == 2) { Vrai(JsonNode.Parse(File.ReadAllText(Path.Combine(dossier, "hooks.json")))["third-party"] is not null, "AGY third party hook retained"); }
            var reactive = Connecteurs.Activer(agent);
            Vrai(reactive.Etat == EtatConnexion.Pret, "reactivate " + agent);
            Vrai(reactive.PrepareLe > anciennePreparation, "real reactivation starts a new setup date " + agent);
            anciennePreparation = VieillirPreparation(agent);
            var guide = Path.Combine(agent switch { 0 => Path.Combine(local, "NexusKeypad", "connecteurs", "claude", "nexus-keypad"), 1 => Path.Combine(maison, ".codex", "plugins", "nexus-keypad"), _ => Path.Combine(maison, ".gemini", "config", "plugins", "nexus-keypad") }, "skills", "nexus-keypad", "SKILL.md");
            File.AppendAllText(guide, "changed");
            Vrai(Connecteurs.LireConfiguration(agent).Etat == EtatConnexion.Reparer, "changed file detected " + agent);
            var repare = Connecteurs.Activer(agent);
            Vrai(repare.Etat == EtatConnexion.Pret, "repair " + agent);
            Vrai(repare.PrepareLe > anciennePreparation, "repair never reuses prior setup evidence " + agent);
        }

        var recuCommun = Path.Combine(local, "NexusKeypad", "connecteurs", "etats", "runtime.json");
        var partageAvant = JsonNode.Parse(File.ReadAllText(recuCommun))["files"].AsObject();
        // Existing installs have the runtime hashes embedded in each connector's receipt. Migration must leave the
        // other two receipts byte-for-byte intact while taking over their shared hashes.
        for (var agent = 0; agent < 3; agent++)
        {
            var ancien = JsonNode.Parse(File.ReadAllText(Recu(agent))).AsObject();
            ancien.Remove("sharedRuntime");
            foreach (var fichier in partageAvant) { ancien["files"][fichier.Key] = fichier.Value.DeepClone(); }
            File.WriteAllText(Recu(agent), ancien.ToJsonString() + "\n");
        }

        File.Delete(recuCommun);
        Vrai(Enumerable.Range(0, 3).All(a => Connecteurs.LireConfiguration(a).Etat == EtatConnexion.Pret), "legacy receipts still verify their complete installation before migration");

        // One helper serves all three connectors. Updating it through any connector must not invalidate the other
        // two receipts; their own installed plugin files and setup evidence stay independent.
        for (var agent = 0; agent < 3; agent++)
        {
            var autres = Enumerable.Range(0, 3).Where(a => a != agent).ToDictionary(a => a, a => File.ReadAllBytes(Recu(a)));
            File.WriteAllText(executable, "updated shared helper fixture " + agent);
            Vrai(Connecteurs.Activer(agent).Etat == EtatConnexion.Pret, "update shared helper through connector " + agent);
            foreach (var autre in autres)
            {
                Vrai(Connecteurs.LireConfiguration(autre.Key).Etat == EtatConnexion.Pret, "shared helper update preserves other connector " + autre.Key);
                Vrai(File.ReadAllBytes(Recu(autre.Key)).AsSpan().SequenceEqual(autre.Value), "shared update never rewrites another connector receipt " + autre.Key);
            }
        }

        var communInstalle = Path.Combine(local, "NexusKeypad", "hook", "nexuskeypad-hook.exe");
        File.AppendAllText(communInstalle, "damaged runtime");
        Vrai(Enumerable.Range(0, 3).All(a => Connecteurs.LireConfiguration(a).Etat == EtatConnexion.Reparer), "shared helper tampering invalidates every connector");
        var guideCodexInstalle = Path.Combine(maison, ".codex", "plugins", "nexus-keypad", "skills", "nexus-keypad", "SKILL.md");
        File.AppendAllText(guideCodexInstalle, "damaged connector");
        Vrai(Connecteurs.Activer(2).Etat == EtatConnexion.Pret && Connecteurs.LireConfiguration(0).Etat == EtatConnexion.Pret,
            "repairing one connector restores the shared runtime for the others");
        Vrai(Connecteurs.LireConfiguration(1).Etat == EtatConnexion.Reparer, "shared repair never hides damage in another connector's own plugin");
        Vrai(Connecteurs.Activer(1).Etat == EtatConnexion.Pret, "damaged own plugin repaired separately");
        var runtimeAvant = File.ReadAllBytes(recuCommun);
        File.Delete(recuCommun);
        Vrai(Enumerable.Range(0, 3).All(a => Connecteurs.LireConfiguration(a).Etat == EtatConnexion.Reparer), "new receipts require the shared runtime attestation");
        File.WriteAllBytes(recuCommun, runtimeAvant);

        // A changed package must start a fresh observation interval even while the previous installed copy is valid.
        var guidePaquet = Path.Combine(paquet, "connecteurs", "skills", "nexus-keypad", "SKILL.md");
        var guideOriginal = File.ReadAllBytes(guidePaquet);
        var avantMiseAJour = VieillirPreparation(1);
        File.AppendAllText(guidePaquet, "\nchanged package fixture\n");
        Vrai(Connecteurs.LireConfiguration(1).Etat == EtatConnexion.Pret, "old installed package is still internally valid before update");
        var actualise = Connecteurs.Activer(1);
        Vrai(actualise.Etat == EtatConnexion.Pret && actualise.PrepareLe > avantMiseAJour, "new package does not inherit old callback interval");
        File.WriteAllBytes(guidePaquet, guideOriginal);
        Vrai(Connecteurs.Activer(1).Etat == EtatConnexion.Pret, "package fixture restored through normal installation");
        File.WriteAllText(Recu(1), "{broken");
        Vrai(Connecteurs.LireConfiguration(1).Etat == EtatConnexion.Reparer && Connecteurs.Activer(1).Etat == EtatConnexion.Pret,
            "malformed receipt can be repaired without inheriting any old setup evidence");

        // Codex hooks travel inline in the manifest, the same in the marketplace source and the installed cache: the
        // program is reached by %LOCALAPPDATA%, so nothing is rendered per PC, and nothing is trusted on the user's behalf.
        var codexPlugin = Path.Combine(maison, ".codex", "plugins", "nexus-keypad");
        var codexCache = Path.Combine(maison, ".codex", "plugins", "cache", "personal", "nexus-keypad", Connecteurs.Version);
        var codexManifestText = File.ReadAllText(Path.Combine(codexPlugin, ".codex-plugin", "plugin.json"));
        var codexHooks = JsonNode.Parse(codexManifestText)["hooks"]["hooks"].AsObject();
        Vrai(codexHooks.Count == 10, "Codex declared lifecycle hooks");
        Vrai(!codexManifestText.Contains("__NEXUS_KEYPAD_HOOK__") && !codexManifestText.Contains("OneDrive") && !codexManifestText.Contains("python"), "Codex portable native hooks");
        Vrai(JsonNode.DeepEquals(JsonNode.Parse(codexManifestText)["hooks"]["hooks"], Connecteurs.HooksCodex(Connecteurs.ProgrammeCodex)["hooks"]), "Codex hooks reach the stable program");
        Vrai(!File.Exists(Path.Combine(codexPlugin, "hooks", "hooks.json")), "Codex has no separate hooks file");
        Vrai(codexManifestText == File.ReadAllText(Path.Combine(codexCache, ".codex-plugin", "plugin.json")), "Codex cache manifest identical");
        Vrai(!File.Exists(Path.Combine(maison, ".codex", "hooks.json")), "Codex no global hook duplication");
        Vrai(!Directory.EnumerateFiles(Path.Combine(maison, ".codex"), "*trust*", SearchOption.AllDirectories).Any(), "Codex installer never creates hook trust");
        foreach (var evenement in codexHooks)
        {
            var commande = evenement.Value[0]["hooks"][0];
            Vrai(commande["async"].GetValue<Boolean>() == false, "Codex native event ordering " + evenement.Key);
            Vrai(commande["command"].GetValue<String>().EndsWith(" codex " + evenement.Key), "Codex exact event command " + evenement.Key);
            if (evenement.Key is "Interrupt" or "SessionEnd") { Vrai(commande["timeout"].GetValue<Int32>() <= 3, "Codex short terminal timeout " + evenement.Key); }
        }

        // An external UI toggle is authoritative too, including callbacks already loaded by a running app.
        var agyConfig = Path.Combine(maison, ".gemini", "config", "config.json");
        var avantBasculeExterne = VieillirPreparation(2);
        var agySettings = JsonNode.Parse(File.ReadAllText(agyConfig));
        agySettings["plugins"]["nexus-keypad"]["enabled"] = false;
        File.WriteAllText(agyConfig, agySettings.ToJsonString());
        Vrai(!Connecteurs.EvenementsActifs("antigravity") && Connecteurs.LireConfiguration(2).Etat == EtatConnexion.Activer, "external AGY off switch respected");
        var agyReactive = Connecteurs.Activer(2);
        Vrai(agyReactive.Etat == EtatConnexion.Pret, "AGY explicit reactivate");
        Vrai(agyReactive.PrepareLe > avantBasculeExterne, "external off switch invalidates earlier setup interval");

        // Hooks belong to the plugin, and enabling twice never recreates a global registration.
        Connecteurs.Activer(2);
        var agyHooks = JsonNode.Parse(File.ReadAllText(Path.Combine(maison, ".gemini", "config", "hooks.json")));
        Vrai(agyHooks[EtatsEnDirect.CleAntigravity] is null, "AGY no global activity hook");
        var pluginHooksPath = Path.Combine(maison, ".gemini", "config", "plugins", "nexus-keypad", "hooks.json");
        var pluginHooks = JsonNode.Parse(File.ReadAllText(pluginHooksPath));
        Vrai(pluginHooks["nexus-keypad"]?["Stop"] is JsonArray { Count: 1 }, "AGY plugin owns Stop");
        Vrai(!File.ReadAllText(pluginHooksPath).Contains("__NEXUS_KEYPAD_HOOK__"), "AGY command template rendered");
        Vrai(!File.ReadAllText(pluginHooksPath).Contains("OneDrive") && !File.ReadAllText(pluginHooksPath).Contains("python"), "AGY standalone native hooks");

        // Migrate our former handlers while preserving third-party handlers inside the same group.
        var oldHooks = new JsonObject();
        EtatsEnDirect.ArrangerAntigravity(oldHooks, "old/nexuskeypad-hook.exe");
        ((JsonArray)oldHooks[EtatsEnDirect.CleAntigravity]["Stop"]).Add(new JsonObject { ["command"] = "third-party.exe" });
        oldHooks["nexus-keypad-sons"] = JsonNode.Parse("{\"Stop\":[{\"command\":\"python -I %OneDrive%/Documents/NexusHarness/NexusKeypad/sons/jouer.py antigravity --hook\"}]}");
        oldHooks["son-fin-de-tache"] = JsonNode.Parse("{\"Stop\":[{\"command\":\"python -I %OneDrive%/Documents/NexusHarness/NexusKeypad/sons/jouer.py antigravity --hook\"},{\"command\":\"other-sound.exe\"}]}");
        File.WriteAllText(Path.Combine(maison, ".gemini", "config", "hooks.json"), oldHooks.ToJsonString());
        Vrai(Connecteurs.Activer(2).Etat == EtatConnexion.Pret, "AGY legacy migration");
        var migrated = JsonNode.Parse(File.ReadAllText(Path.Combine(maison, ".gemini", "config", "hooks.json")));
        Vrai(migrated["nexus-keypad-sons"] is null, "AGY legacy sound removed");
        Vrai(migrated[EtatsEnDirect.CleAntigravity]["Stop"].AsArray().Count == 1 && migrated[EtatsEnDirect.CleAntigravity]["Stop"][0]["command"].GetValue<String>() == "third-party.exe", "AGY shared legacy group retained");
        Vrai(migrated["son-fin-de-tache"]["Stop"].AsArray().Count == 1, "AGY shared legacy sound group retained");
        var receipt = JsonNode.Parse(File.ReadAllText(Path.Combine(local, "NexusKeypad", "connecteurs", "etats", "antigravity.json")));
        Vrai(receipt["hookOwner"].GetValue<String>() == "plugin", "AGY sound migration receipt");
        File.WriteAllText(pluginHooksPath, "{}");
        Vrai(Connecteurs.LireConfiguration(2).Etat == EtatConnexion.Reparer, "AGY missing bundled hooks detected");
        Vrai(Connecteurs.Activer(2).Etat == EtatConnexion.Pret, "AGY bundled hooks repaired");

        // The installed/public Claude identity must be attested by the official manager, not just settings and file presence.
        Vrai(appelsClaude.Any(x => x.StartsWith("plugin|marketplace|add|", StringComparison.Ordinal))
            && appelsClaude.Any(x => x.StartsWith("plugin|install|nexus-keypad@nexuskeypad|", StringComparison.Ordinal)), "public Claude uses official marketplace add then install");
        var appelsAvant = appelsClaude.Count;
        Vrai(Connecteurs.Activer(0).Etat == EtatConnexion.Pret && appelsClaude.Count == appelsAvant, "unchanged ready Claude never reruns manager");
        var registrePublic = Path.Combine(maison, ".claude", "plugins", "installed_plugins.json");
        File.Delete(registrePublic);
        Vrai(Connecteurs.LireConfiguration(0).Etat == EtatConnexion.Reparer, "public Claude settings alone never prove installation");
        Connecteurs.CommandeClaudeEssai(null);
        Vrai(Connecteurs.Activer(0).Etat == EtatConnexion.Erreur && Connecteurs.LireConfiguration(0).Etat != EtatConnexion.Pret, "missing official CLI never reports ready");
        Connecteurs.CommandeClaudeEssai(GestionnaireClaude);
        echecClaude = true;
        Vrai(Connecteurs.Activer(0).Etat == EtatConnexion.Erreur && Connecteurs.LireConfiguration(0).Etat != EtatConnexion.Pret && !Connecteurs.EvenementsActifs("claude"), "failed native install keeps pending connector inactive");
        echecClaude = false;
        fauxSuccesClaude = true;
        Vrai(Connecteurs.Activer(0).Etat == EtatConnexion.Erreur && Connecteurs.LireConfiguration(0).Etat != EtatConnexion.Pret, "exit zero without official registry never proves installation");
        fauxSuccesClaude = false;
        Vrai(Connecteurs.Activer(0).Etat == EtatConnexion.Pret, "official install repairs interrupted public setup");
        var cachePublicClaude = JsonNode.Parse(File.ReadAllText(registrePublic))["plugins"]["nexus-keypad@nexuskeypad"][0]["installPath"].GetValue<String>();
        File.AppendAllText(Path.Combine(cachePublicClaude, "skills", "nexus-keypad", "SKILL.md"), "damaged same-version cache");
        miseAJourClaudeInchangee = true;
        Vrai(Connecteurs.Activer(0).Etat == EtatConnexion.Pret && appelsClaude.Any(x => x.StartsWith("plugin|uninstall|nexus-keypad@nexuskeypad|", StringComparison.Ordinal)), "same-version damaged cache repaired by official reinstall after up_to_date");
        miseAJourClaudeInchangee = false;
        var reglagesPublics = Path.Combine(maison, ".claude", "settings.json");
        var cataloguesPublics = Path.Combine(maison, ".claude", "plugins", "known_marketplaces.json");
        foreach (var fichierConflit in new[] { reglagesPublics, cataloguesPublics })
        {
            var original = File.ReadAllBytes(fichierConflit);
            var contenu = JsonNode.Parse(original);
            var entree = fichierConflit == reglagesPublics ? contenu["extraKnownMarketplaces"]["nexuskeypad"] : contenu["nexuskeypad"];
            entree["source"]["path"] = Path.Combine(maison, "different-marketplace");
            File.WriteAllText(fichierConflit, contenu.ToJsonString());
            var avantConflit = File.ReadAllBytes(fichierConflit);
            var avantRecu = File.ReadAllBytes(Recu(0));
            var avantAppels = appelsClaude.Count;
            Vrai(Connecteurs.Activer(0).Etat == EtatConnexion.Erreur, "foreign Claude marketplace is refused before setup");
            Vrai(File.ReadAllBytes(fichierConflit).AsSpan().SequenceEqual(avantConflit) && File.ReadAllBytes(Recu(0)).AsSpan().SequenceEqual(avantRecu)
                && appelsClaude.Count == avantAppels, "foreign Claude catalogue/receipt is unchanged and manager not invoked");
            File.WriteAllBytes(fichierConflit, original);
        }

        // Preserve the existing private Claude connector rather than install a competing public identity.
        var privateRoot = Path.Combine(maison, ".claude", "plugins", "cache", "nexus", "nexus-keypad", "2.1.0");
        Directory.CreateDirectory(Path.Combine(privateRoot, ".claude-plugin"));
        Directory.CreateDirectory(Path.Combine(privateRoot, "hooks"));
        File.WriteAllText(Path.Combine(privateRoot, ".claude-plugin", "plugin.json"), "{\"name\":\"nexus-keypad\",\"version\":\"2.1.0\"}");
        File.WriteAllText(Path.Combine(privateRoot, "hooks", "hooks.json"), "{\"hooks\":{}}");
        Directory.CreateDirectory(Path.Combine(privateRoot, "skills", "nexus-keypad"));
        File.Copy(Path.Combine(source, "connecteurs", "skills", "nexus-keypad", "SKILL.md"), Path.Combine(privateRoot, "skills", "nexus-keypad", "SKILL.md"));
        var privateSettingsPath = Path.Combine(maison, ".claude", "settings.json");
        File.WriteAllText(privateSettingsPath, "{\"other\":true,\"enabledPlugins\":{\"nexus-keypad@nexus\":false,\"third-party@other\":true}}");
        var installed = new JsonObject { ["version"] = 2, ["plugins"] = new JsonObject { ["nexus-keypad@nexus"] = new JsonArray(new JsonObject { ["scope"] = "user", ["installPath"] = privateRoot }) } };
        File.WriteAllText(Path.Combine(maison, ".claude", "plugins", "installed_plugins.json"), installed.ToJsonString());
        Vrai(Connecteurs.Activer(0).Etat == EtatConnexion.Pret, "private Claude connector activated");
        var privateSettings = JsonNode.Parse(File.ReadAllText(privateSettingsPath));
        Vrai(privateSettings["enabledPlugins"]["nexus-keypad@nexus"].GetValue<Boolean>() && privateSettings["enabledPlugins"]["nexus-keypad@nexuskeypad"] is null, "private Claude identity preserved without duplicate");
        Vrai(privateSettings["other"].GetValue<Boolean>() && privateSettings["enabledPlugins"]["third-party@other"].GetValue<Boolean>(), "private Claude third parties preserved");
        var avantPrive = VieillirPreparation(0);
        Vrai(Connecteurs.Activer(0).PrepareLe == avantPrive, "private Claude identical enable preserves setup date");
        File.WriteAllText(Path.Combine(privateRoot, ".claude-plugin", "plugin.json"), "{\"name\":\"nexus-keypad\",\"version\":\"2.1.1\"}");
        var priveActualise = Connecteurs.Activer(0);
        Vrai(priveActualise.Etat == EtatConnexion.Pret && priveActualise.PrepareLe > avantPrive, "private Claude package change starts a new observation interval");

        var codexConfig = Path.Combine(maison, ".codex", "config.toml");
        File.WriteAllText(codexConfig, "[broken =");
        var before = File.ReadAllText(codexConfig);
        Vrai(Connecteurs.Activer(1).Etat == EtatConnexion.Erreur, "malformed TOML fails");
        Vrai(File.ReadAllText(codexConfig) == before, "malformed TOML profile unchanged");
        var hooksAgy = Path.Combine(maison, ".gemini", "config", "hooks.json");
        File.WriteAllText(hooksAgy, "{broken");
        Vrai(Connecteurs.Activer(2).Etat == EtatConnexion.Erreur && File.ReadAllText(hooksAgy) == "{broken", "malformed AGY preserved");

        var rollbackA = Path.Combine(local, "NexusKeypad", "rollback-a.json");
        var rollbackB = Path.Combine(local, "NexusKeypad", "rollback-b.json");
        File.WriteAllText(rollbackA, "original bytes");
        var transaction = new Connecteurs.Transaction();
        transaction.Poser(rollbackA, System.Text.Encoding.UTF8.GetBytes("new bytes"));
        transaction.Poser(rollbackB, System.Text.Encoding.UTF8.GetBytes("created bytes"));
        Assert.ThrowsAny<IOException>(() => transaction.Executer(() => false));
        Vrai(File.ReadAllText(rollbackA) == "original bytes" && !File.Exists(rollbackB), "failed verification restores all original bytes");
        var drift = new Connecteurs.Transaction();
        drift.Poser(rollbackA, System.Text.Encoding.UTF8.GetBytes("planned bytes"));
        File.WriteAllText(rollbackA, "outside writer");
        Assert.ThrowsAny<IOException>(() => drift.Executer(() => true));
        Vrai(File.ReadAllText(rollbackA) == "outside writer", "concurrent profile writer preserved");
    }
}

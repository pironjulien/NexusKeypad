#if !POSTE
namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json.Nodes;
    using Tomlyn;
    using Tomlyn.Model;
    using Tomlyn.Parsing;
    using Tomlyn.Syntax;
    using Tomlyn.Serialization;

    [TomlSerializable(typeof(TomlTable))]
    internal partial class ConnecteurTomlContext : TomlSerializerContext
    {
    }

    public enum EtatConnexion { Absent, Activer, Relancer, Reparer, Pret, EnCours, Erreur }

    public sealed record Connexion(EtatConnexion Etat, String Detail)
    {
        public DateTime? PrepareLe { get; init; }
        // null : non vérifié ; false : premier callback pas encore observé. La configuration prête ne l'invente pas.
        public Boolean? PremierEvenementObserve { get; init; }
        public String CodeAttente { get; init; }
    }

    // Three host adapters, one installed keypad runtime. Merely loading the pad never changes an AI profile.
    public static class Connecteurs
    {
        public const String Nom = "nexus-keypad";
        public const String Version = "1.2.3";
        private const String ProgrammeHook = "__NEXUS_KEYPAD_HOOK__";
        private static readonly String[] Agents = ["claude", "codex", "antigravity"];
        private static readonly Object Verrou = new();
        private static String _exePaquet;
        private static String _maison;
        private static String _local;
        private static Func<String[], (Int32 Code, String Sortie)> _commandeClaudeEssai;

        private static String Maison => _maison ?? Environment.GetEnvironmentVariable("USERPROFILE") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        private static String Local => _local ?? Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        private static String Racine => Path.Combine(Local, "NexusKeypad", "connecteurs");
        private static String Executable => Path.Combine(Local, "NexusKeypad", "hook", EtatsEnDirect.NomExe);
        private static String Source => Path.Combine(Path.GetDirectoryName(_exePaquet ?? "") ?? "", "connecteurs");
        private static String Codex => Path.Combine(Maison, ".codex");
        private static String Gemini => Path.Combine(Maison, ".gemini", "config");
        private static String Claude => _maison is null && Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } d
            ? d : Path.Combine(Maison, ".claude");
        private static String Recu(Int32 agent) => Path.Combine(Racine, "etats", Agents[agent] + ".json");
        private static String RecuCommun => Path.Combine(Racine, "etats", "runtime.json");

        public static void Initialiser(String exePaquet) => _exePaquet = Path.GetFullPath(exePaquet);

        // Tests compile this source in their own assembly and use isolated profiles, never the live workstation.
        internal static void DossiersEssai(String maison, String local, String exePaquet)
        {
            _maison = Path.GetFullPath(maison);
            _local = Path.GetFullPath(local);
            Initialiser(exePaquet);
        }

        internal static void CommandeClaudeEssai(Func<String[], (Int32 Code, String Sortie)> commande) => _commandeClaudeEssai = commande;

        private static void AgentValide(Int32 agent)
        {
            if (agent < 0 || agent >= Agents.Length) { throw new ArgumentOutOfRangeException(nameof(agent)); }
        }

        private static JsonObject LireJson(String chemin) => File.Exists(chemin)
            ? JsonNode.Parse(File.ReadAllText(chemin, Encoding.UTF8).TrimStart('\uFEFF')) as JsonObject
                ?? throw new InvalidDataException("Le fichier de réglages doit contenir un objet JSON.")
            : new JsonObject();

        private static JsonObject Objet(JsonObject parent, String cle)
        {
            if (parent[cle] is null) { parent[cle] = new JsonObject(); }
            return parent[cle] as JsonObject ?? throw new InvalidDataException("Une section de réglages a un type inattendu.");
        }

        private static String Json(JsonObject objet) => objet.ToJsonString() + "\n";
        private static String Empreinte(Byte[] contenu) => Convert.ToHexString(SHA256.HashData(contenu));
        private static Byte[] Utf8(String contenu) => new UTF8Encoding(false).GetBytes(contenu);

        private static Boolean FichierCommun(String chemin)
        {
            var complet = Path.GetFullPath(chemin);
            var dossier = Path.GetDirectoryName(Executable);
            return MemeChemin(complet, Executable)
                || complet.StartsWith(Path.Combine(dossier, "connecteurs") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || MemeChemin(complet, Path.Combine(dossier, "claude-quota", "nexuskeypad-quota", "hooks", "register.ts"));
        }

        private static Boolean FichiersConformes(JsonObject fichiers, Boolean communs = false) => fichiers.All(fichier =>
            Autorise(fichier.Key) && (!communs || FichierCommun(fichier.Key)) && File.Exists(fichier.Key)
                && Empreinte(File.ReadAllBytes(fichier.Key)) == fichier.Value?.GetValue<String>());

        private static Boolean Present(Int32 agent) => Directory.Exists(agent switch { 0 => Claude, 1 => Codex, _ => Gemini });

        private static String IdentiteCodex(JsonObject catalogue)
        {
            var nom = catalogue["name"]?.GetValue<String>();
            if (String.IsNullOrWhiteSpace(nom) || nom.IndexOfAny(['/', '\\']) >= 0 || nom is "." or "..")
            {
                throw new InvalidDataException("Le nom du catalogue personnel Codex est invalide.");
            }

            return Nom + "@" + nom;
        }

        private static String CatalogueCodex => Path.Combine(Maison, ".agents", "plugins", "marketplace.json");

        private static JsonObject LireCatalogueCodex()
        {
            var objet = LireJson(CatalogueCodex);
            if (objet["name"] is null) { objet["name"] = "personal"; }
            if (objet["plugins"] is null) { objet["plugins"] = new JsonArray(); }
            if (objet["plugins"] is not JsonArray) { throw new InvalidDataException("Le catalogue Codex est invalide."); }
            return objet;
        }

        private static TomlTable LireToml(String texte) => TomlSerializer.Deserialize(texte, ConnecteurTomlContext.Default.TomlTable)
            ?? throw new InvalidDataException("La configuration TOML est vide.");

        private static TomlTable Table(TomlTable parent, String cle, Boolean creer)
        {
            if (!parent.TryGetValue(cle, out var valeur))
            {
                if (!creer) { return null; }
                parent[cle] = valeur = new TomlTable();
            }

            return valeur as TomlTable ?? throw new InvalidDataException("Une section TOML a un type inattendu.");
        }

        // nexus-keypad plugins of the Nexus catalogs (private nexus, NexusSystem's nexus-plugins) enabled next to the
        // connector's own identity: their hooks would double the connector's.
        private static String[] CodexDoublons(String texte, String identite)
        {
            var plugins = Table(LireToml(texte), "plugins", false);
            return plugins is null ? [] : EtatsEnDirect.IdentitesNexus.Where(p => p != identite)
                .Where(p => Table(plugins, p, false) is { } t && t.TryGetValue("enabled", out var v) && v is true).ToArray();
        }

        private static Boolean CodexActif(String identite)
        {
            var chemin = Path.Combine(Codex, "config.toml");
            if (!File.Exists(chemin)) { return false; }
            var plugins = Table(LireToml(File.ReadAllText(chemin)), "plugins", false);
            var plugin = plugins is null ? null : Table(plugins, identite, false);
            return plugin is not null && plugin.TryGetValue("enabled", out var valeur) && valeur is true;
        }

        private static String[] Cle(KeySyntax cle) => new[] { Morceau(cle.Key) }.Concat(cle.DotKeys.Select(x => Morceau(x.Key))).ToArray();
        private static String Morceau(BareKeyOrStringValueSyntax cle) => cle switch
        {
            StringValueSyntax s => s.Value,
            BareKeySyntax b => b.Key.Text,
            _ => throw new InvalidDataException("Clé TOML non reconnue."),
        };

        private static IEnumerable<(String[] Chemin, ValueSyntax Valeur)> Valeurs(IEnumerable<KeyValueSyntax> entrees, String[] prefixe)
        {
            foreach (var entree in entrees)
            {
                var chemin = prefixe.Concat(Cle(entree.Key)).ToArray();
                yield return (chemin, entree.Value);
                if (entree.Value is InlineTableSyntax inline)
                {
                    foreach (var enfant in Valeurs(inline.Items.Select(x => x.KeyValue), chemin)) { yield return enfant; }
                }
            }
        }

        // Parse with Tomlyn, change only the selected boolean or insert into its real AST container, then compare the
        // complete semantic result. Multiline values, quoted keys, inline tables and all unrelated bytes survive.
        internal static String ActiverToml(String texte, String identite, Boolean actif)
        {
            var modele = LireToml(texte);
            var attendu = LireToml(texte);
            var plugin = Table(Table(attendu, "plugins", true), identite, true);
            plugin["enabled"] = actif;
            var syntaxe = SyntaxParser.ParseStrict(texte);
            var valeurs = Valeurs(syntaxe.KeyValues, []).Concat(syntaxe.Tables.SelectMany(t => Valeurs(t.Items, Cle(t.Name)))).ToList();
            var cible = new[] { "plugins", identite, "enabled" };
            var valeur = valeurs.FirstOrDefault(x => x.Chemin.SequenceEqual(cible)).Valeur;
            String resultat;
            var booleen = actif ? "true" : "false";
            if (valeur is not null)
            {
                resultat = texte[..valeur.Span.Offset] + booleen + texte[(valeur.Span.Offset + valeur.Span.Length)..];
            }
            else
            {
                var nl = texte.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                var insertion = "enabled = " + booleen;
                InlineTableSyntax dans = null;
                TableSyntaxBase table = null;
                for (var longueur = 2; longueur >= 1 && dans is null && table is null; longueur--)
                {
                    var prefixe = cible.Take(longueur).ToArray();
                    dans = valeurs.FirstOrDefault(x => x.Chemin.SequenceEqual(prefixe)).Valeur as InlineTableSyntax;
                    table = syntaxe.Tables.FirstOrDefault(x => Cle(x.Name).SequenceEqual(prefixe));
                    if (dans is not null || table is not null)
                    {
                        insertion = longueur == 2 ? insertion : TomlChaine(identite) + (dans is null ? ".enabled = " + booleen : " = { " + insertion + " }");
                    }
                }

                if (dans is not null)
                {
                    var position = dans.CloseBrace.Span.Offset;
                    resultat = texte[..position] + (dans.Items.Any() ? ", " : " ") + insertion + " " + texte[position..];
                }
                else if (table is not null)
                {
                    var position = table.CloseBracket.Span.Offset + table.CloseBracket.Span.Length;
                    position = texte.IndexOf('\n', position);
                    position = position < 0 ? texte.Length : position + 1;
                    resultat = texte[..position] + (position == texte.Length && !texte.EndsWith('\n') ? nl : "") + insertion + nl + texte[position..];
                }
                else
                {
                    resultat = texte.TrimEnd('\r', '\n') + nl + nl + "[plugins." + TomlChaine(identite) + "]" + nl + insertion + nl;
                }
            }

            if (!MemeToml(LireToml(resultat), attendu))
            {
                throw new InvalidDataException("Le changement TOML ne préserve pas exactement les autres réglages.");
            }

            return resultat;
        }

        private static String TomlChaine(String texte) => new StringValueSyntax(texte).ToString();

        private static Boolean MemeToml(Object a, Object b)
        {
            if (a is TomlTable ta && b is TomlTable tb)
            {
                return ta.Count == tb.Count && ta.All(x => tb.TryGetValue(x.Key, out var autre) && MemeToml(x.Value, autre));
            }

            if (a is not String && b is not String && a is System.Collections.IEnumerable ea && b is System.Collections.IEnumerable eb)
            {
                var la = ea.Cast<Object>().ToArray();
                var lb = eb.Cast<Object>().ToArray();
                return la.Length == lb.Length && Enumerable.Range(0, la.Length).All(i => MemeToml(la[i], lb[i]));
            }

            return Equals(a, b);
        }

        private static readonly String[] FichiersClaudeNexus = [".claude-plugin/plugin.json", "hooks/hooks.json", "skills/nexus-keypad/SKILL.md"];

        // Preserve the selected catalog even while Claude's manager has not installed its cache. Falling back to a
        // disabled legacy catalog would silently reverse a migration and enable two sets of hooks.
        private static (String Identite, String Dossier)? PluginClaudeNexus(JsonObject reglages)
        {
            var actives = EtatsEnDirect.IdentitesNexus.Where(p => (reglages["enabledPlugins"] as JsonObject)?[p]?.GetValue<Boolean>() == true).ToArray();
            if (actives.Length > 1) { throw new InvalidDataException("Plusieurs connecteurs NexusKeypad sont activés dans Claude ; conserver une seule identité."); }
            var identite = EtatsEnDirect.PluginCompletDeclare(reglages);
            if (identite is null) { return null; }
            var installs = LireJson(Path.Combine(Claude, "plugins", "installed_plugins.json"))["plugins"] as JsonObject;
            var dossier = (installs?[identite] as JsonArray)?.OfType<JsonObject>().Where(x => x["scope"]?.GetValue<String>() == "user")
                .Select(x => x["installPath"]?.GetValue<String>()).FirstOrDefault(x => !String.IsNullOrEmpty(x));
            return (identite, dossier);
        }

        private static Boolean CacheClaudeComplet(String dossier) => !String.IsNullOrEmpty(dossier)
            && FichiersClaudeNexus.All(f => File.Exists(Path.Combine(dossier, f)));

        private static Connexion AttenteClaude(String identite) => new(EtatConnexion.EnCours,
            $"{identite} est sélectionné ; son installation et son cache par Claude restent à terminer. Les autres identités restent inchangées.")
            { CodeAttente = "claude_plugin_install_pending" };

        public static Connexion LireConfiguration(Int32 agent)
        {
            AgentValide(agent);
            lock (Verrou)
            {
                try
                {
                    if (!Present(agent)) { return new(EtatConnexion.Absent, "Application absente de ce profil Windows."); }
                    var selectionClaude = agent == 0 ? PluginClaudeNexus(LireJson(Path.Combine(Claude, "settings.json"))) : null;
                    if (selectionClaude is { } selection && !CacheClaudeComplet(selection.Dossier)) { return AttenteClaude(selection.Identite); }
                    var recu = LireJson(Recu(agent));
                    if (selectionClaude is { } configure && recu["active"]?.GetValue<Boolean>() == true
                        && recu["identity"]?.GetValue<String>() != configure.Identite)
                    {
                        return new(EtatConnexion.Reparer, "Le plugin Claude sélectionné est installé ; son identité reste à adopter par le connecteur.");
                    }
                    if (recu["pending"]?.GetValue<Boolean>() == true) { return new(EtatConnexion.Reparer, "L’installation officielle du connecteur Claude reste à terminer."); }
                    if (recu["active"]?.GetValue<Boolean>() != true) { return new(EtatConnexion.Activer, "Activer NexusKeypad pour cette IA."); }
                    if (recu["version"]?.GetValue<String>() != Version) { return new(EtatConnexion.Reparer, "Le connecteur doit être mis à jour."); }
                    if (recu["files"] is not JsonObject fichiers || fichiers.Count < 2) { return new(EtatConnexion.Reparer, "Installation incomplète."); }
                    var runtimeCommun = File.Exists(RecuCommun);
                    if (runtimeCommun)
                    {
                        var runtime = LireJson(RecuCommun);
                        if (runtime["version"]?.GetValue<String>() != Version || runtime["files"] is not JsonObject partages
                            || !partages.ContainsKey(Executable) || !FichiersConformes(partages, communs: true))
                        {
                            return new(EtatConnexion.Reparer, "Un fichier du programme commun manque ou a changé.");
                        }
                    }

                    if (recu["sharedRuntime"]?.GetValue<Boolean>() == true && !runtimeCommun)
                    {
                        return new(EtatConnexion.Reparer, "Le programme commun ne peut pas être vérifié.");
                    }

                    // Legacy receipts include the same shared files three times. A verified runtime receipt owns
                    // those hashes now; each connector keeps attesting its own installed plugin without rewriting it.
                    var propres = new JsonObject();
                    foreach (var fichier in fichiers.Where(f => !runtimeCommun || !FichierCommun(f.Key)))
                    {
                        propres[fichier.Key] = fichier.Value?.DeepClone();
                    }

                    if (propres.Count < 2 || !FichiersConformes(propres))
                    {
                        return new(EtatConnexion.Reparer, "Un fichier du connecteur manque ou a changé.");
                    }

                    var actif = agent switch
                    {
                        0 => (LireJson(Path.Combine(Claude, "settings.json"))["enabledPlugins"] as JsonObject)?[recu["identity"]!.GetValue<String>()]?.GetValue<Boolean>() == true,
                        1 => CodexActif(recu["identity"]!.GetValue<String>()),
                        _ => (LireJson(Path.Combine(Gemini, "config.json"))["plugins"] as JsonObject)?[Nom] is JsonObject choix
                            && choix["enabled"]?.GetValue<Boolean>() == true,
                    };
                    if (!actif) { return new(EtatConnexion.Activer, "NexusKeypad est désactivé dans cette IA."); }
                    if (agent == 2 && !HooksAgyConformes()) { return new(EtatConnexion.Reparer, "Les événements Antigravity ne sont plus reliés au connecteur."); }
                    if (agent == 0 && recu["identity"]!.GetValue<String>() == Nom + "@nexuskeypad")
                    {
                        var chemin = (LireJson(Path.Combine(Claude, "settings.json"))["extraKnownMarketplaces"] as JsonObject)?["nexuskeypad"]?["source"]?["path"]?.GetValue<String>();
                        if (!String.Equals(chemin, Path.Combine(Racine, "claude"), StringComparison.OrdinalIgnoreCase))
                        {
                            return new(EtatConnexion.Reparer, "Le catalogue du connecteur Claude n’est plus enregistré.");
                        }
                        if (!ClaudePublicInstalle()) { return new(EtatConnexion.Reparer, "Le connecteur doit être installé par le gestionnaire officiel de Claude."); }
                    }

                    if (agent == 1)
                    {
                        if (CodexDoublons(File.ReadAllText(Path.Combine(Codex, "config.toml")), recu["identity"]!.GetValue<String>()).Length > 0)
                        {
                            return new(EtatConnexion.Reparer, "Un autre plugin nexus-keypad est actif dans Codex : ses hooks doubleraient ceux du connecteur.");
                        }

                        var catalogue = LireCatalogueCodex();
                        if (((JsonArray)catalogue["plugins"]).OfType<JsonObject>().Count(x => x["name"]?.GetValue<String>() == Nom
                            && x["source"]?["source"]?.GetValue<String>() == "local"
                            && x["source"]?["path"]?.GetValue<String>() == "./.codex/plugins/" + Nom) != 1)
                        {
                            return new(EtatConnexion.Reparer, "Le catalogue du connecteur Codex n’est plus enregistré.");
                        }
                    }

                    var date = DateTime.Parse(recu["preparedAt"]!.GetValue<String>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                    return new(EtatConnexion.Pret, "Fichiers et activation du connecteur vérifiés ; le pad contrôle ensuite la liaison avec l’application.") { PrepareLe = date };
                }
                catch (Exception ex)
                {
                    return new(EtatConnexion.Reparer, "La configuration ne peut pas être vérifiée (" + ex.GetType().Name + ").");
                }
            }
        }

        // These are receipts written by Claude's own plugin manager, never constructed by NexusKeypad.
        private static Boolean ClaudePublicInstalle()
        {
            try
            {
                var catalogue = LireJson(Path.Combine(Claude, "plugins", "known_marketplaces.json"))["nexuskeypad"];
                var attendu = Path.Combine(Racine, "claude");
                if (catalogue?["source"]?["source"]?.GetValue<String>() != "directory"
                    || !MemeChemin(catalogue?["source"]?["path"]?.GetValue<String>(), attendu)
                    || !MemeChemin(catalogue?["installLocation"]?.GetValue<String>(), attendu)) { return false; }
                var entrees = LireJson(Path.Combine(Claude, "plugins", "installed_plugins.json"))["plugins"]?[Nom + "@nexuskeypad"] as JsonArray;
                var utilisateurs = entrees?.OfType<JsonObject>().Where(e => e["scope"]?.GetValue<String>() == "user").ToArray();
                if (utilisateurs is not { Length: 1 }) { return false; }
                var installe = utilisateurs[0];
                var cache = installe["installPath"]?.GetValue<String>();
                if (installe["version"]?.GetValue<String>() != Version || String.IsNullOrEmpty(cache)
                    || !Path.GetFullPath(cache).StartsWith(Path.GetFullPath(Path.Combine(Claude, "plugins", "cache")) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { return false; }
                var source = Path.Combine(attendu, Nom);
                var manifeste = LireJson(Path.Combine(cache, ".claude-plugin", "plugin.json"));
                if (manifeste["name"]?.GetValue<String>() != Nom || manifeste["version"]?.GetValue<String>() != Version || !Directory.Exists(source)) { return false; }
                foreach (var fichier in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                {
                    var copie = Path.Combine(cache, Path.GetRelativePath(source, fichier));
                    if (!File.Exists(copie) || Empreinte(File.ReadAllBytes(copie)) != Empreinte(File.ReadAllBytes(fichier))) { return false; }
                }
                return true;
            }
            catch (Exception) { return false; }
        }

        private static Boolean MemeChemin(String a, String b) => !String.IsNullOrEmpty(a) && !String.IsNullOrEmpty(b)
            && String.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

        // Claude Desktop installs its official plugin-management CLI here. Do not execute an arbitrary PATH command.
        private static String CliClaude()
        {
            var racine = Path.Combine(Local, "Claude-Data");
            if (!Directory.Exists(racine)) { return null; }
            return Directory.EnumerateDirectories(racine).Where(d => (File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0)
                .Select(d => Path.Combine(d, "claude-code")).Where(Directory.Exists)
                .SelectMany(d => Directory.EnumerateDirectories(d)).Where(d => System.Version.TryParse(Path.GetFileName(d), out _))
                .OrderByDescending(d => System.Version.Parse(Path.GetFileName(d)))
                .SelectMany(d => Directory.EnumerateDirectories(d)).Select(d => Path.Combine(d, "claude.exe"))
                .FirstOrDefault(File.Exists);
        }

        private static void CommandeClaude(params String[] arguments)
        {
            (Int32 Code, String Sortie) resultat;
            if (_maison is not null && _commandeClaudeEssai is not null) { resultat = _commandeClaudeEssai(arguments); }
            else
            {
                var executable = CliClaude() ?? throw new FileNotFoundException("Le gestionnaire officiel Claude est absent. Ouvrez Claude Code une première fois.");
                var commande = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
                    WorkingDirectory = Maison, WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var argument in arguments) { commande.ArgumentList.Add(argument); }
                commande.Environment["USERPROFILE"] = Maison;
                commande.Environment["HOME"] = Maison;
                commande.Environment["LOCALAPPDATA"] = Local;
                commande.Environment["CLAUDE_CONFIG_DIR"] = Claude;
                foreach (var nom in new[] { "CLAUDECODE", "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN" }) { commande.Environment.Remove(nom); }
                using var processus = Process.Start(commande) ?? throw new IOException("Le gestionnaire officiel Claude ne démarre pas.");
                processus.StandardInput.Close();
                var sortie = processus.StandardOutput.ReadToEndAsync();
                var erreur = processus.StandardError.ReadToEndAsync();
                if (!processus.WaitForExit(30000)) { processus.Kill(entireProcessTree: true); throw new TimeoutException("Le gestionnaire officiel Claude n’a pas terminé."); }
                resultat = (processus.ExitCode, sortie.GetAwaiter().GetResult());
                _ = erreur.GetAwaiter().GetResult(); // host diagnostics may contain private paths; never forward them
            }
            // Commands must attest their outcome; exit zero without the official structured result is insufficient.
            var derniere = resultat.Sortie.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            var reponse = derniere is null ? null : JsonNode.Parse(derniere);
            if (resultat.Code != 0 || reponse?["outcome"]?.GetValue<String>() != "ok")
            {
                throw new IOException("Le gestionnaire officiel Claude n’a pas confirmé l’installation.");
            }
        }

        private static void InstallerClaudePublic()
        {
            VerifierCatalogueClaude();
            var connu = LireJson(Path.Combine(Claude, "plugins", "known_marketplaces.json"))["nexuskeypad"];
            if (connu is null) { CommandeClaude("plugin", "marketplace", "add", Path.Combine(Racine, "claude"), "--scope", "user", "--json"); }
            var existe = (LireJson(Path.Combine(Claude, "plugins", "installed_plugins.json"))["plugins"]?[Nom + "@nexuskeypad"] as JsonArray)?.OfType<JsonObject>()
                .Any(e => e["scope"]?.GetValue<String>() == "user") == true;
            CommandeClaude("plugin", existe ? "update" : "install", Nom + "@nexuskeypad", "--scope", "user", "--json");
            if (existe && !ClaudePublicInstalle())
            {
                // Claude's update can legitimately report up_to_date without repairing a damaged same-version
                // cache. Ask its manager to reinstall this user-scoped plugin only; never hand-edit its registry.
                CommandeClaude("plugin", "uninstall", Nom + "@nexuskeypad", "--scope", "user", "--json");
                CommandeClaude("plugin", "install", Nom + "@nexuskeypad", "--scope", "user", "--json");
            }
            if ((LireJson(Path.Combine(Claude, "settings.json"))["enabledPlugins"] as JsonObject)?[Nom + "@nexuskeypad"]?.GetValue<Boolean>() != true)
            {
                CommandeClaude("plugin", "enable", Nom + "@nexuskeypad", "--scope", "user", "--json");
            }
            if (!ClaudePublicInstalle()) { throw new IOException("Le gestionnaire officiel Claude n’a pas enregistré le paquet attendu."); }
        }

        private static void VerifierCatalogueClaude()
        {
            var declare = LireJson(Path.Combine(Claude, "settings.json"))["extraKnownMarketplaces"]?["nexuskeypad"];
            var connu = LireJson(Path.Combine(Claude, "plugins", "known_marketplaces.json"))["nexuskeypad"];
            foreach (var entree in new[] { declare, connu }.Where(e => e is not null))
            {
                if (entree["source"]?["source"]?.GetValue<String>() != "directory"
                    || !MemeChemin(entree["source"]?["path"]?.GetValue<String>(), Path.Combine(Racine, "claude")))
                {
                    throw new IOException("Un autre catalogue Claude utilise déjà ce nom.");
                }
            }
        }

        public static Connexion Lire(Int32 agent, Boolean applicationOuverte, Boolean portDisponible)
        {
            var configuration = LireConfiguration(agent);
            if (configuration.Etat != EtatConnexion.Pret) { return configuration; }
            return applicationOuverte && agent != 0 && !portDisponible
                ? configuration with { Etat = EtatConnexion.Relancer, Detail = "Relancer cette IA depuis le pad pour terminer sa connexion." }
                : configuration;
        }

        // Old installations keep reporting until migrated explicitly. After migration, an off switch in either host
        // or connector also stops callbacks already loaded by a running process.
        public static Boolean EvenementsActifs(String agent)
        {
            var numero = Array.IndexOf(Agents, agent);
            if (numero < 0) { return false; }
            try
            {
                if (!File.Exists(Recu(numero))) { return true; }
                var recu = LireJson(Recu(numero));
                if (recu["active"]?.GetValue<Boolean>() != true) { return false; }
                return numero switch
                {
                    0 => (LireJson(Path.Combine(Claude, "settings.json"))["enabledPlugins"] as JsonObject)?[recu["identity"]!.GetValue<String>()]?.GetValue<Boolean>() == true,
                    1 => CodexActif(recu["identity"]!.GetValue<String>()),
                    _ => (LireJson(Path.Combine(Gemini, "config.json"))["plugins"] as JsonObject)?[Nom]?["enabled"]?.GetValue<Boolean>() == true,
                };
            }
            catch (Exception) { return false; }
        }

        private static Boolean HooksAgyConformes()
        {
            var actuel = LireJson(Path.Combine(Gemini, "plugins", Nom, "hooks.json"));
            var attendu = new JsonObject { [Nom] = EtatsEnDirect.GroupeAntigravity(CheminHook(Executable)) };
            return JsonNode.DeepEquals(actuel, attendu);
        }

        private static Byte[] RendreHooksAgy(Byte[] modele)
        {
            var source = JsonNode.Parse(Encoding.UTF8.GetString(modele)) as JsonObject;
            var attendu = new JsonObject { [Nom] = EtatsEnDirect.GroupeAntigravity(ProgrammeHook) };
            if (!JsonNode.DeepEquals(source, attendu)) { throw new InvalidDataException("Les hooks du paquet Antigravity sont incohérents."); }
            return Utf8(Json(new JsonObject { [Nom] = EtatsEnDirect.GroupeAntigravity(CheminHook(Executable)) }));
        }

        private static Byte[] RendreHooksCodex(Byte[] modele)
        {
            var source = JsonNode.Parse(Encoding.UTF8.GetString(modele)) as JsonObject;
            var attendu = HooksCodex(ProgrammeHook);
            if (!JsonNode.DeepEquals(source, attendu)) { throw new InvalidDataException("Les hooks du paquet Codex sont incohérents."); }
            return Utf8(Json(HooksCodex(CheminHook(Executable))));
        }

        private static JsonObject HooksCodex(String programme)
        {
            var evenements = new JsonObject();
            foreach (var evenement in new[] { "SessionStart", "UserPromptSubmit", "PreToolUse", "PermissionRequest", "PostToolUse",
                "SubagentStart", "SubagentStop", "Stop", "Interrupt", "SessionEnd" })
            {
                evenements[evenement] = new JsonArray(new JsonObject { ["hooks"] = new JsonArray(new JsonObject
                {
                    ["type"] = "command", ["command"] = $"{programme} codex {evenement}", ["async"] = false,
                    ["timeout"] = evenement is "Interrupt" or "SessionEnd" ? 3 : 10,
                }) });
            }
            return new JsonObject { ["description"] = "NexusKeypad activity only; no decisions, messages or extra completion sound.", ["hooks"] = evenements };
        }

        private static String CheminHook(String chemin)
        {
            // The existing Antigravity hook command parser does not accept a quoted executable. Use the Windows short
            // path only when required, and refuse when the volume cannot provide one; never invent a command.
            if (!chemin.Contains(' ')) { return chemin; }
            var tampon = new StringBuilder(32768);
            return GetShortPathNameW(chemin, tampon, tampon.Capacity) > 0 && !tampon.ToString().Contains(' ')
                ? tampon.ToString() : throw new IOException("Le chemin du programme Antigravity n’a pas de forme prise en charge.");
        }

        private static Boolean Autorise(String chemin)
        {
            var complet = Path.GetFullPath(chemin);
            return new[] { Maison, Path.Combine(Local, "NexusKeypad") }.Any(r => complet.StartsWith(Path.GetFullPath(r).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        }

        private static void SansLien(String chemin)
        {
            if (!Autorise(chemin)) { throw new IOException("Chemin hors du profil NexusKeypad."); }
            var courant = Path.GetFullPath(chemin);
            while (!String.IsNullOrEmpty(courant))
            {
                if ((File.Exists(courant) || Directory.Exists(courant)) && (File.GetAttributes(courant) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("Un lien de système de fichiers empêche une installation sûre.");
                }

                courant = Path.GetDirectoryName(courant);
            }
        }

        internal sealed class Transaction
        {
            private readonly Dictionary<String, Byte[]> _avant = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<String, Byte[]> _apres = new(StringComparer.OrdinalIgnoreCase);

            internal Boolean Identique => this._apres.All(item => this._avant[item.Key] is { } avant
                && avant.AsSpan().SequenceEqual(item.Value));

            public void Poser(String chemin, Byte[] contenu)
            {
                SansLien(chemin);
                if (!this._avant.ContainsKey(chemin)) { this._avant[chemin] = File.Exists(chemin) ? File.ReadAllBytes(chemin) : null; }
                this._apres[chemin] = contenu;
            }

            public void PoserJson(String chemin, JsonObject contenu)
            {
                this.Poser(chemin, Utf8(Json(contenu)));
                // Les migrations peuvent retirer/réinsérer une propriété sans changer le réglage. Préserver alors
                // ses octets, son ordre et sa date ; ce changement de présentation n'est pas une nouvelle activation.
                if (this._avant[chemin] is not { } avant) { return; }
                try
                {
                    if (JsonNode.DeepEquals(JsonNode.Parse(avant), contenu)) { this._apres[chemin] = avant; }
                }
                catch (System.Text.Json.JsonException) { } // le remplacement explicitement prévu reste soumis à la transaction
            }

            private static void Ecrire(String chemin, Byte[] contenu)
            {
                SansLien(chemin);
                Directory.CreateDirectory(Path.GetDirectoryName(chemin));
                var temporaire = chemin + ".nexuskeypad-" + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.WriteAllBytes(temporaire, contenu); File.Move(temporaire, chemin, overwrite: true); }
                finally { if (File.Exists(temporaire)) { File.Delete(temporaire); } }
            }

            public void Executer(Func<Boolean> verifier)
            {
                var ecrits = new List<String>();
                try
                {
                    // Detect profile changes made after planning before writing any file.
                    foreach (var item in this._avant)
                    {
                        var actuel = File.Exists(item.Key) ? File.ReadAllBytes(item.Key) : null;
                        if ((actuel is null) != (item.Value is null) || actuel is not null && !actuel.AsSpan().SequenceEqual(item.Value))
                        {
                            throw new IOException("Le profil a changé pendant la préparation. Réessayer après cette opération.");
                        }
                    }

                    foreach (var item in this._apres)
                    {
                        if (this._avant[item.Key] is { } avant && avant.AsSpan().SequenceEqual(item.Value)) { continue; }
                        Ecrire(item.Key, item.Value);
                        ecrits.Add(item.Key);
                    }

                    if (!verifier()) { throw new IOException("La vérification finale du connecteur a échoué."); }
                }
                catch (Exception erreur)
                {
                    var incomplet = false;
                    foreach (var chemin in ecrits.AsEnumerable().Reverse())
                    {
                        try
                        {
                            var attendu = this._apres[chemin];
                            if (!File.Exists(chemin) || !File.ReadAllBytes(chemin).AsSpan().SequenceEqual(attendu))
                            {
                                incomplet = true; // preserve outside edits, but still roll back every other owned write
                                continue;
                            }

                            if (this._avant[chemin] is { } ancien) { Ecrire(chemin, ancien); }
                            else { File.Delete(chemin); }
                        }
                        catch (Exception) { incomplet = true; }
                    }

                    if (incomplet) { throw new IOException("Retour arrière incomplet : une écriture extérieure ou un fichier verrouillé doit être vérifié.", erreur); }
                    throw;
                }
            }
        }

        public static Connexion Activer(Int32 agent) => Changer(agent, true);
        public static Connexion Desactiver(Int32 agent) => Changer(agent, false);

        public static Boolean RetirerInstallations()
        {
            var succes = true;
            for (var agent = 0; agent < Agents.Length; agent++)
            {
                if (File.Exists(Recu(agent)) && Desactiver(agent).Etat is not (EtatConnexion.Activer or EtatConnexion.Absent))
                {
                    succes = false;
                }
            }

            return succes;
        }

        private static Connexion Changer(Int32 agent, Boolean actif)
        {
            AgentValide(agent);
            lock (Verrou)
            {
                using var profil = new System.Threading.Mutex(false, "Local\\NexusKeypadConnecteurs-" + Empreinte(Utf8(Path.GetFullPath(Maison).ToUpperInvariant()))[..24]);
                var pris = false;
                try
                {
                    try { pris = profil.WaitOne(TimeSpan.FromSeconds(30)); }
                    catch (System.Threading.AbandonedMutexException) { pris = true; }
                    if (!pris) { return new(EtatConnexion.EnCours, "Une autre configuration NexusKeypad est en cours."); }
                    if (!Present(agent)) { return new(EtatConnexion.Absent, "Application absente de ce profil Windows."); }
                    if (actif && (String.IsNullOrEmpty(_exePaquet) || !File.Exists(_exePaquet)))
                    {
                        throw new FileNotFoundException("Le programme NexusKeypad manque dans le paquet.");
                    }

                    // La date décrit une vraie préparation, pas chaque demande « enable ». On ne la réutilise
                    // que si l'installation et l'activation actuelles sont encore vérifiées, puis comparées au plan.
                    var precedente = actif ? LireConfiguration(agent) : null;
                    var ancienRecu = precedente?.Etat == EtatConnexion.Pret ? LireJson(Recu(agent)) : null;

                    var transaction = new Transaction();
                    var fichiers = new JsonObject();
                    var identite = Nom;
                    void Fichier(String chemin, Byte[] contenu)
                    {
                        transaction.Poser(chemin, contenu);
                        fichiers[chemin] = Empreinte(contenu);
                    }

                    if (actif)
                    {
                        var communs = new JsonObject();
                        void Commun(String chemin, Byte[] contenu)
                        {
                            transaction.Poser(chemin, contenu);
                            communs[chemin] = Empreinte(contenu);
                        }

                        Commun(Executable, File.ReadAllBytes(_exePaquet));
                        // The installed diagnostic command can repair/enable itself later without a source checkout or
                        // a running Logitech process. The same small packages travel next to its stable executable.
                        if (!Directory.Exists(Source)) { throw new DirectoryNotFoundException("Les connecteurs manquent dans le paquet NexusKeypad."); }
                        if (Directory.EnumerateDirectories(Source, "*", SearchOption.AllDirectories).Any(d => (File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0))
                        {
                            throw new IOException("Le paquet des connecteurs contient un lien.");
                        }
                        foreach (var fichier in Directory.EnumerateFiles(Source, "*", SearchOption.AllDirectories))
                        {
                            Commun(Path.Combine(Path.GetDirectoryName(Executable), "connecteurs", Path.GetRelativePath(Source, fichier)), File.ReadAllBytes(fichier));
                        }
                        var quotaSource = Path.Combine(Path.GetDirectoryName(_exePaquet), "claude-quota", "nexuskeypad-quota", "hooks", "register.ts");
                        Commun(Path.Combine(Path.GetDirectoryName(Executable), "claude-quota", "nexuskeypad-quota", "hooks", "register.ts"), File.ReadAllBytes(quotaSource));
                        transaction.PoserJson(RecuCommun, new JsonObject { ["version"] = Version, ["files"] = communs });
                    }
                    var cible = agent switch
                    {
                        0 => Path.Combine(Racine, "claude", Nom),
                        1 => Path.Combine(Codex, "plugins", Nom),
                        _ => Path.Combine(Gemini, "plugins", Nom),
                    };
                    var reglagesClaude = agent == 0 ? LireJson(Path.Combine(Claude, "settings.json")) : null;
                    var complet = agent == 0 ? PluginClaudeNexus(reglagesClaude) : null;
                    if (actif && complet is { } selection && !CacheClaudeComplet(selection.Dossier)) { return AttenteClaude(selection.Identite); }
                    var prive = complet?.Dossier;
                    if (agent == 0 && actif && prive is null) { VerifierCatalogueClaude(); } // before any transaction write
                    if (actif && prive is null)
                    {
                        var paquet = Path.Combine(Source, Agents[agent]);
                        if (!Directory.Exists(paquet)) { throw new DirectoryNotFoundException("Le connecteur manque dans le paquet NexusKeypad."); }
                        foreach (var entree in Directory.EnumerateFiles(paquet, "*", SearchOption.AllDirectories))
                        {
                            if ((File.GetAttributes(entree) & FileAttributes.ReparsePoint) != 0) { throw new IOException("Le paquet contient un lien."); }
                            var relatif = Path.GetRelativePath(paquet, entree);
                            var contenu = File.ReadAllBytes(entree);
                            if (agent == 2 && relatif == "hooks.json") { contenu = RendreHooksAgy(contenu); }
                            if (agent == 1 && relatif == Path.Combine("hooks", "hooks.json")) { contenu = RendreHooksCodex(contenu); }
                            Fichier(Path.Combine(cible, relatif), contenu);
                        }
                    }

                    if (agent == 0)
                    {
                        EtatsEnDirect.ArrangerClaude(reglagesClaude, false); // remove only our former global hooks
                        EtatsEnDirect.ArrangerQuota(reglagesClaude, null); // quota now travels with the connector
                        identite = complet?.Identite ?? Nom + "@nexuskeypad";
                        if (actif && prive is not null)
                        {
                            // Preserve the selected manager-installed plugin; never add another catalog or hooks.
                            foreach (var fichier in FichiersClaudeNexus)
                            {
                                var chemin = Path.Combine(prive, fichier);
                                if (!File.Exists(chemin)) { throw new IOException("Le plugin privé Claude est incomplet."); }
                                fichiers[chemin] = Empreinte(File.ReadAllBytes(chemin));
                            }
                        }
                        else if (actif)
                        {
                            var quota = Path.Combine(Path.GetDirectoryName(_exePaquet), "claude-quota", "nexuskeypad-quota", "hooks", "register.ts");
                            Fichier(Path.Combine(cible, "hooks", "quota.ts"), File.ReadAllBytes(quota));
                            var catalogue = new JsonObject { ["name"] = "nexuskeypad", ["description"] = "NexusKeypad connections for the official desktop AI applications.", ["owner"] = new JsonObject { ["name"] = "Julien Piron" },
                                ["plugins"] = new JsonArray(new JsonObject { ["name"] = Nom, ["source"] = "./" + Nom, ["description"] = "NexusKeypad connector for Claude." }) };
                            Fichier(Path.Combine(Racine, "claude", ".claude-plugin", "marketplace.json"), Utf8(Json(catalogue)));
                            Objet(reglagesClaude, "extraKnownMarketplaces")["nexuskeypad"] = new JsonObject { ["source"] = new JsonObject { ["source"] = "directory", ["path"] = Path.Combine(Racine, "claude") } };
                        }

                        Objet(reglagesClaude, "enabledPlugins")[identite] = actif;
                        transaction.PoserJson(Path.Combine(Claude, "settings.json"), reglagesClaude);
                    }
                    else if (agent == 1)
                    {
                        var catalogue = LireCatalogueCodex();
                        identite = IdentiteCodex(catalogue);
                        var liste = (JsonArray)catalogue["plugins"];
                        foreach (var ancien in liste.OfType<JsonObject>().Where(x => x["name"]?.GetValue<String>() == Nom).ToList()) { liste.Remove(ancien); }
                        liste.Add((JsonNode)new JsonObject { ["name"] = Nom, ["source"] = new JsonObject { ["source"] = "local", ["path"] = "./.codex/plugins/" + Nom },
                            ["policy"] = new JsonObject { ["installation"] = "AVAILABLE", ["authentication"] = "ON_INSTALL" }, ["category"] = "Productivity" });
                        transaction.PoserJson(CatalogueCodex, catalogue);
                        var configuration = Path.Combine(Codex, "config.toml");
                        var texte = ActiverToml(File.Exists(configuration) ? File.ReadAllText(configuration) : "", identite, actif);
                        if (actif)
                        {
                            // A nexus-keypad plugin from the Nexus catalogs cannot carry hooks with this PC's executable path:
                            // only this connector keeps them, so another enabled copy is turned off instead of doubling them.
                            foreach (var doublon in CodexDoublons(texte, identite)) { texte = ActiverToml(texte, doublon, false); }
                        }

                        transaction.Poser(configuration, Utf8(texte));
                        if (actif)
                        {
                            // Codex resolves the installed cache, not only the marketplace source directory.
                            var cache = Path.Combine(Codex, "plugins", "cache", catalogue["name"]!.GetValue<String>(), Nom, Version);
                            foreach (var entree in Directory.EnumerateFiles(Path.Combine(Source, "codex"), "*", SearchOption.AllDirectories))
                            {
                                var relatif = Path.GetRelativePath(Path.Combine(Source, "codex"), entree);
                                var contenu = File.ReadAllBytes(entree);
                                if (relatif == Path.Combine("hooks", "hooks.json")) { contenu = RendreHooksCodex(contenu); }
                                Fichier(Path.Combine(cache, relatif), contenu);
                            }
                        }
                    }
                    else
                    {
                        var configuration = LireJson(Path.Combine(Gemini, "config.json"));
                        Objet(Objet(configuration, "plugins"), Nom)["enabled"] = actif;
                        transaction.PoserJson(Path.Combine(Gemini, "config.json"), configuration);
                        var hooks = LireJson(Path.Combine(Gemini, "hooks.json"));
                        EtatsEnDirect.RetirerAnciensHooksAntigravity(hooks);
                        transaction.PoserJson(Path.Combine(Gemini, "hooks.json"), hooks);
                    }

                    var proprietaireHooks = agent is 0 or 2 ? "plugin" : null;
                    var identique = actif && precedente?.Etat == EtatConnexion.Pret && precedente.PrepareLe is DateTime date
                        && date.ToUniversalTime() <= DateTime.UtcNow && transaction.Identique
                        && ancienRecu?["identity"]?.GetValue<String>() == identite
                        && ancienRecu?["hookOwner"]?.GetValue<String>() == proprietaireHooks
                        && JsonNode.DeepEquals(ancienRecu?["files"], fichiers);
                    var prepareLe = identique ? precedente.PrepareLe!.Value : DateTime.UtcNow;
                    var recuFinal = new JsonObject { ["version"] = Version, ["active"] = actif,
                        ["identity"] = identite, ["preparedAt"] = prepareLe.ToString("O", CultureInfo.InvariantCulture), ["files"] = fichiers,
                        ["hookOwner"] = proprietaireHooks, ["sharedRuntime"] = actif };
                    if (agent == 0 && actif && prive is null && !identique)
                    {
                        // Stage our files first; Claude alone owns its marketplace/installed-plugin registries.
                        // An official install may modify those registries and settings. Never roll them back as if
                        // they were our transaction; leave a repairable partial setup and do not issue a ready receipt.
                        if (!(_maison is not null && _commandeClaudeEssai is not null) && CliClaude() is null)
                        {
                            throw new FileNotFoundException("Le gestionnaire officiel Claude est absent. Ouvrez Claude Code une première fois.");
                        }
                        transaction.PoserJson(Recu(agent), new JsonObject { ["version"] = Version, ["active"] = false,
                            ["identity"] = identite, ["pending"] = true });
                        transaction.Executer(() => true);
                        InstallerClaudePublic();
                        var reception = new Transaction();
                        reception.PoserJson(Recu(agent), recuFinal);
                        reception.Executer(() => LireConfiguration(agent).Etat == EtatConnexion.Pret);
                        return LireConfiguration(agent);
                    }
                    transaction.PoserJson(Recu(agent), recuFinal);
                    transaction.Executer(() => LireConfiguration(agent).Etat == (actif ? EtatConnexion.Pret : EtatConnexion.Activer));
                    return LireConfiguration(agent);
                }
                catch (Exception ex)
                {
                    // Never include profile contents or credentials in user-facing diagnostics.
                    return new(EtatConnexion.Erreur, "Activation interrompue (" + ex.GetType().Name + "). Vérifiez la connexion avant de réessayer.");
                }
                finally { if (pris) { profil.ReleaseMutex(); } }
            }
        }

        // Commands bundled in all three plugins use the shared native executable. Status is read-only; enable/disable
        // require an explicit user request through the host's own confirmation flow.
        public static Int32 Executer(String[] args)
        {
            if (args.Length != 3 || args[0] != "connecteur") { return 2; }
            var agent = Array.IndexOf(Agents, args[1]);
            if (agent < 0) { return 2; }
            var resultat = args[2] switch { "status" => LireConfiguration(agent), "enable" => Activer(agent), "disable" => Desactiver(agent), _ => null };
            if (resultat is null) { return 2; }
            Console.WriteLine(new JsonObject { ["agent"] = Agents[agent], ["state"] = resultat.Etat.ToString(), ["detail"] = resultat.Detail,
                ["preparedAt"] = resultat.PrepareLe?.ToString("O", CultureInfo.InvariantCulture), ["pendingReason"] = resultat.CodeAttente }.ToJsonString());
            return resultat.Etat is EtatConnexion.Erreur or EtatConnexion.Reparer ? 1 : 0;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern UInt32 GetShortPathNameW(String longPath, StringBuilder shortPath, Int32 capacity);
    }
}
#endif

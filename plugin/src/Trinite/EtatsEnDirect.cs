#if !POSTE
namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Text.RegularExpressions;
    using System.Threading;

    // États en direct de Claude et d'Antigravity (travaille, t'attend, fini, à l'instant même) : leurs hooks appellent
    // nexuskeypad-hook.exe, livré dans le paquet et copié à une place fixe (%LOCALAPPDATA%\NexusKeypad\hook), qui ne bouge
    // pas d'une version ou d'une édition du plugin à l'autre. Les nouveaux connecteurs (Connecteurs/ConnexionPad) gèrent
    // l'activation par IA ; ces lecteurs et migrateurs conservent la compatibilité avec les installations précédentes.
    public static class EtatsEnDirect
    {
        public static readonly String[] EvenementsClaude =
            ["UserPromptSubmit", "PreToolUse", "PostToolUse", "PostToolUseFailure", "Notification", "Stop", "StopFailure", "SessionEnd"];

        public static readonly String[] EvenementsAntigravity = ["PreInvocation", "PreToolUse", "PostToolUse", "Stop"];

        public const String NomExe = "nexuskeypad-hook.exe";

        // Entrée d'Antigravity dans ~/.gemini/config/hooks.json (la même que l'installateur des sources).
        public const String CleAntigravity = "trinite-pad";

        private static readonly String Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        private static readonly String Maison = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        public static String DossierHook => Path.Combine(Local, "NexusKeypad", "hook");

        // Quota exact de Claude : le mod nexuskeypad-quota (claude-quota/ du dépôt, livré dans le paquet), servi à Claude par
        // un catalogue local (extraKnownMarketplaces, source « directory ») et activé par enabledPlugins, posé et retiré
        // avec les hooks.
        public const String Catalogue = "nexuskeypad";

        public const String ModQuota = "nexuskeypad-quota@" + Catalogue;

        public const String DossierModsPaquet = "claude-quota";

        public static String DossierCatalogue => Path.Combine(Local, "NexusKeypad", "claude-marketplace");

        public static String Exe => Path.Combine(DossierHook, NomExe);

        private static String Choix => Path.Combine(Local, "NexusKeypad", "etats-en-direct.json");

        public static String ReglagesClaude =>
            Path.Combine(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } d ? d : Path.Combine(Maison, ".claude"), "settings.json");

        public static String HooksAntigravity => Path.Combine(Maison, ".gemini", "config", "hooks.json");

        private static readonly JsonSerializerOptions Ecriture = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        private static Int32 _question;

        // nexuskeypad-hook.exe livré à côté de la DLL du plugin (donné au chargement : le service charge la DLL en mémoire,
        // sans chemin d'assemblage).
        private static String _exePaquet;

        // Hook posé par le pad : il appelle etat.py des sources ou l'exécutable du paquet (pas un autre outil qui citerait
        // le projet).
        public static Boolean DuPad(JsonNode hook) =>
            Regex.IsMatch((hook?["command"]?.GetValue<String>() ?? "") + " ", @"(etat[\\/]etat\.py|nexuskeypad-hook(\.exe)?)'?\)? (claude|antigravity) ");

        public static JsonObject CommandeClaude(String evenement) => new()
        {
            ["type"] = "command",
            ["shell"] = "powershell",
            ["command"] = $"& (Join-Path $env:LOCALAPPDATA 'NexusKeypad\\hook\\{NomExe}') claude {evenement}",
            ["async"] = true,
            ["timeout"] = 10,
        };

        // Retire les hooks du pad (et seulement eux : un groupe peut porter ceux d'autres outils) ; un groupe vidé disparaît.
        // Avec pose=true, ajoute ensuite un groupe par événement, en tâche de fond pour ne jamais ralentir un tour.
        public static void ArrangerClaude(JsonObject reglages, Boolean pose)
        {
            var hooks = reglages["hooks"] as JsonObject;
            if (hooks is null)
            {
                if (!pose)
                {
                    return;
                }

                reglages["hooks"] = hooks = new JsonObject();
            }

            foreach (var evenement in hooks.Select(p => p.Key).Union(pose ? EvenementsClaude : []).ToList())
            {
                if (hooks[evenement] is not JsonArray groupes)
                {
                    if (!pose || !EvenementsClaude.Contains(evenement))
                    {
                        continue;
                    }

                    hooks[evenement] = groupes = new JsonArray();
                }

                foreach (var groupe in groupes.OfType<JsonObject>())
                {
                    if (groupe["hooks"] is JsonArray liste)
                    {
                        foreach (var h in liste.Where(DuPad).ToList())
                        {
                            liste.Remove(h);
                        }
                    }
                }

                foreach (var vide in groupes.OfType<JsonObject>().Where(g => g["hooks"] is not JsonArray { Count: > 0 }).ToList())
                {
                    groupes.Remove(vide);
                }

                if (pose && EvenementsClaude.Contains(evenement))
                {
                    groupes.Add((JsonNode)new JsonObject { ["hooks"] = new JsonArray(CommandeClaude(evenement)) });
                }
                else if (groupes.Count == 0)
                {
                    hooks.Remove(evenement);
                }
            }
        }

        // Catalogue local et mod du quota de Claude : posés (catalogue = son dossier) ou retirés (null), sans toucher aux
        // autres catalogues ni plugins ; un objet vidé disparaît.
        public static void ArrangerQuota(JsonObject reglages, String catalogue)
        {
            void Poser(String cle, String nom, JsonNode valeur)
            {
                var objet = reglages[cle] as JsonObject;
                if (valeur is null)
                {
                    objet?.Remove(nom);
                    if (objet is { Count: 0 })
                    {
                        reglages.Remove(cle);
                    }

                    return;
                }

                if (objet is null)
                {
                    reglages[cle] = objet = new JsonObject();
                }

                objet[nom] = valeur;
            }

            Poser("extraKnownMarketplaces", Catalogue,
                catalogue is null ? null : new JsonObject { ["source"] = new JsonObject { ["source"] = "directory", ["path"] = catalogue } });
            Poser("enabledPlugins", ModQuota, catalogue is null ? null : JsonValue.Create(true));
        }

        // Antigravity : événements d'outil dans une enveloppe { matcher, hooks } (« * » = tous les outils), PreInvocation et
        // Stop directement sous l'événement. Sa commande ne passe pas les guillemets : chemin sans espace (nom court sinon).
        public static void ArrangerAntigravity(JsonObject hooks, String exeSansEspace)
        {
            if (exeSansEspace is null)
            {
                hooks.Remove(CleAntigravity);
                return;
            }

            hooks[CleAntigravity] = GroupeAntigravity(exeSansEspace);
        }

        public static JsonObject GroupeAntigravity(String exeSansEspace)
        {
            JsonObject Gestionnaire(String e) => new() { ["type"] = "command", ["command"] = $"{exeSansEspace} antigravity {e}", ["timeout"] = 10 };
            var entree = new JsonObject();
            foreach (var e in EvenementsAntigravity)
            {
                entree[e] = e.EndsWith("ToolUse", StringComparison.Ordinal)
                    ? new JsonArray(new JsonObject { ["matcher"] = "*", ["hooks"] = new JsonArray(Gestionnaire(e)) })
                    : new JsonArray(Gestionnaire(e));
            }

            return entree;
        }

        // Migration vers les hooks du plugin : seuls nos gestionnaires disparaissent, même si un ancien groupe
        // contient aussi un hook ajouté par l'utilisateur. Les autres groupes restent intacts.
        public static void RetirerAnciensHooksAntigravity(JsonObject hooks)
        {
            Boolean DuProduit(JsonNode hook) => DuPad(hook) || Regex.IsMatch(hook?["command"]?.GetValue<String>() ?? "",
                @"NexusKeypad[\\/]sons[\\/]jouer\.py\s+antigravity(?:\s|$)", RegexOptions.IgnoreCase);
            foreach (var cle in new[] { CleAntigravity, "nexus-keypad-sons", "son-fin-de-tache" })
            {
                if (hooks[cle] is not JsonObject groupe) { continue; }
                foreach (var evenement in groupe.Select(p => p.Key).ToList())
                {
                    if (groupe[evenement] is not JsonArray liste) { continue; }
                    foreach (var gestionnaire in liste.ToList())
                    {
                        if (gestionnaire is JsonObject enveloppe && enveloppe["hooks"] is JsonArray enfants)
                        {
                            foreach (var enfant in enfants.Where(DuProduit).ToList()) { enfants.Remove(enfant); }
                            if (enfants.Count == 0) { liste.Remove(gestionnaire); }
                        }
                        else if (DuProduit(gestionnaire)) { liste.Remove(gestionnaire); }
                    }
                    if (liste.Count == 0) { groupe.Remove(evenement); }
                }
                if (!groupe.Any(p => p.Value is JsonArray)) { hooks.Remove(cle); }
            }
        }

        public static Boolean ClaudeAvecPad(JsonObject reglages) =>
            (reglages?["hooks"] as JsonObject)?.Any(p => p.Value is JsonArray g &&
                g.OfType<JsonObject>().Any(x => x["hooks"] is JsonArray l && l.Any(DuPad))) == true ||
            PluginComplet(reglages) == true;

        // Plugin Claude complet de NexusKeypad (hooks, note de fin de tâche et quota dans un seul plugin) : catalogue privé
        // nexus, ou catalogue public de NexusSystem (nexus-plugins). Là où il est déclaré, c'est lui qui porte les états en
        // direct. Null : absent, le pad pose ses hooks lui-même.
        public const String PluginClaude = "nexus-keypad@nexus";
        public const String PluginClaudePublic = "nexus-keypad@nexus-plugins";
        // Les mêmes identités nomment le plugin nexus-keypad de Codex dans les catalogues Nexus (Connecteurs.CodexDoublons).
        public static readonly String[] IdentitesNexus = [PluginClaude, PluginClaudePublic];

        // The currently enabled identity owns the hooks. A disabled legacy declaration cannot outrank that choice.
        // With no enabled identity, an explicit enable still uses the already declared catalog instead of adding one.
        public static String PluginCompletDeclare(JsonObject reglages) =>
            IdentitesNexus.FirstOrDefault(p => (reglages?["enabledPlugins"] as JsonObject)?[p] is JsonValue v && v.TryGetValue(out Boolean actif) && actif)
                ?? IdentitesNexus.Reverse().FirstOrDefault(p => (reglages?["enabledPlugins"] as JsonObject)?[p] is JsonValue v && v.TryGetValue(out Boolean _));

        public static Boolean? PluginComplet(JsonObject reglages) =>
            PluginCompletDeclare(reglages) is { } p && reglages["enabledPlugins"]![p] is JsonValue v && v.TryGetValue(out Boolean actif) ? actif : null;

        private static JsonObject Lire(String chemin)
        {
            try
            {
                return File.Exists(chemin) ? JsonNode.Parse(File.ReadAllText(chemin, Encoding.UTF8).TrimStart('﻿')) as JsonObject : new JsonObject();
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                return null; // illisible : on n'y touche pas
            }
        }

        private static Boolean ClaudePresent => Directory.Exists(Path.GetDirectoryName(ReglagesClaude));

        private static Boolean AntigravityPresent => Directory.Exists(Path.GetDirectoryName(HooksAntigravity));

        // Posés par le pad (paquet ou sources), chez Claude ou chez Antigravity.
        public static Boolean Poses() =>
            ClaudeAvecPad(Lire(ReglagesClaude)) || Lire(HooksAntigravity)?.ContainsKey(CleAntigravity) == true;

        // Au chargement du plugin : remet l'exécutable à jour s'il est posé ; sinon pose la question, une seule fois, quand
        // Claude ou Antigravity est installé.
        public static void Demarrer(String exePaquet)
        {
            _exePaquet = exePaquet;
            Connecteurs.Initialiser(exePaquet);
            var fil = new Thread(() =>
            {
                try
                {
                    if (File.Exists(Exe))
                    {
                        Copier(exePaquet);
                        if (Directory.Exists(DossierCatalogue))
                        {
                            CopierCatalogue(exePaquet, DossierCatalogue);
                        }
                    }
                    // La connexion se propose sur la touche de chaque IA, jamais par une question globale au chargement.
                }
                catch (Exception ex)
                {
                    PluginLog.Warning(ex, "états en direct : démarrage");
                }
            })
            { IsBackground = true, Name = "EtatsEnDirect" };
            fil.Start();
        }

        // Action « Live states » : propose de poser, ou de retirer s'ils le sont déjà.
        public static void Basculer() =>
            new Thread(() => Demander(_exePaquet, premiereFois: false)) { IsBackground = true, Name = "EtatsEnDirect" }.Start();

        private static void Demander(String exePaquet, Boolean premiereFois)
        {
            if (Interlocked.Exchange(ref _question, 1) == 1)
            {
                return; // une question est déjà à l'écran
            }

            try
            {
                var francais = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr";
                if (Poses())
                {
                    var retirer = francais
                        ? "Retirer les états en direct de Claude et d'Antigravity ?\n\nLes hooks de NexusKeypad et son module de quota pour Claude sont retirés de leurs réglages ; ceux des autres outils restent. Leurs têtes restent sur le pad, au repos."
                        : "Remove live states for Claude and Antigravity?\n\nNexusKeypad's hooks and its usage module for Claude are removed from their settings; other tools' hooks stay. Their heads stay on the keypad, at rest.";
                    if (Oui(retirer))
                    {
                        Appliquer(exePaquet, pose: false);
                    }

                    return;
                }

                var poser = francais
                    ? "Afficher en direct ce que font Claude et Antigravity (travaille, t'attend, fini) ?\n\n" +
                      "NexusKeypad ajoute ses hooks à leurs réglages (~/.claude/settings.json, ~/.gemini/config/hooks.json). Ils lancent un petit programme du plugin, copié dans %LOCALAPPDATA%\\NexusKeypad\\hook, qui note seulement leur état dans ~/.nexus/trinite, sur ce PC. Pour Claude s'ajoute un module (nexuskeypad-quota) qui note son quota exact au même endroit. Rien n'est envoyé.\n\n" +
                      "Pour changer d'avis plus tard : l'action « Live states » du groupe Trinité dans Logi Options+."
                    : "Show what Claude and Antigravity are doing, live (working, waiting for you, done)?\n\n" +
                      "NexusKeypad adds its hooks to their settings (~/.claude/settings.json, ~/.gemini/config/hooks.json). They run a small program from the plugin, copied to %LOCALAPPDATA%\\NexusKeypad\\hook, that only writes their state to ~/.nexus/trinite on this PC. For Claude, a module (nexuskeypad-quota) also writes its exact usage there. Nothing is sent anywhere.\n\n" +
                      "To change your mind later: the \"Live states\" action in the Trinity group of Logi Options+.";
                var accord = Oui(poser);
                if (premiereFois)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Choix));
                    File.WriteAllText(Choix, new JsonObject { ["choix"] = accord ? "oui" : "non" }.ToJsonString());
                }

                if (accord)
                {
                    Appliquer(exePaquet, pose: true);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "états en direct : question");
            }
            finally
            {
                Volatile.Write(ref _question, 0);
            }
        }

        // Pose ou retire chez Claude et chez Antigravity ; une erreur remet les deux fichiers tels qu'ils étaient.
        public static Boolean Appliquer(String exePaquet, Boolean pose, String reglagesClaude = null, String hooksAntigravity = null, String exe = null,
            String catalogue = null)
        {
            reglagesClaude ??= ReglagesClaude;
            hooksAntigravity ??= HooksAntigravity;
            exe ??= Exe;
            catalogue ??= DossierCatalogue;
            var avant = new[] { reglagesClaude, hooksAntigravity }.ToDictionary(c => c, c => File.Exists(c) ? File.ReadAllBytes(c) : null);
            try
            {
                if (pose)
                {
                    Copier(exePaquet, exe);
                    CopierCatalogue(exePaquet, catalogue);
                }

                if (Directory.Exists(Path.GetDirectoryName(reglagesClaude)) && Lire(reglagesClaude) is { } reglages)
                {
                    if (PluginComplet(reglages) is not null)
                    {
                        // Le plugin complet porte tout : on l'active ou on le coupe, sans hooks ni mod de quota en double.
                        ArrangerClaude(reglages, false);
                        ArrangerQuota(reglages, null);
                        reglages["enabledPlugins"]![PluginCompletDeclare(reglages)!] = pose;
                    }
                    else
                    {
                        ArrangerClaude(reglages, pose);
                        ArrangerQuota(reglages, pose ? catalogue : null);
                    }

                    Ecrire(reglagesClaude, reglages);
                }

                if (!pose && Directory.Exists(catalogue))
                {
                    Directory.Delete(catalogue, recursive: true); // plus cité par les réglages
                }

                if (Directory.Exists(Path.GetDirectoryName(hooksAntigravity)) && Lire(hooksAntigravity) is { } hooks
                    && (pose || hooks.ContainsKey(CleAntigravity)))
                {
                    ArrangerAntigravity(hooks, pose ? SansEspace(exe) : null);
                    Ecrire(hooksAntigravity, hooks);
                }

                PluginLog.Info(pose ? "états en direct posés" : "états en direct retirés");
                return true;
            }
            catch (Exception ex)
            {
                foreach (var (chemin, contenu) in avant)
                {
                    try
                    {
                        if (contenu is null)
                        {
                            File.Delete(chemin);
                        }
                        else
                        {
                            File.WriteAllBytes(chemin, contenu);
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

                PluginLog.Error(ex, "états en direct : rien n'a changé");
                return false;
            }
        }

        private static void Ecrire(String chemin, JsonObject contenu)
        {
            var temporaire = chemin + ".nexuskeypad.tmp";
            File.WriteAllText(temporaire, contenu.ToJsonString(Ecriture) + "\n", new UTF8Encoding(false));
            File.Move(temporaire, chemin, overwrite: true);
        }

        // Catalogue du mod de quota, copié du paquet (claude-quota, à côté de l'exécutable du hook) vers sa place fixe ;
        // recopié à chaque chargement du plugin (nouvelle version).
        private static void CopierCatalogue(String exePaquet, String catalogue)
        {
            var source = Path.Combine(Path.GetDirectoryName(exePaquet) ?? "", DossierModsPaquet);
            if (!Directory.Exists(source))
            {
                throw new DirectoryNotFoundException($"mod du quota absent du paquet : {source}");
            }

            foreach (var fichier in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var cible = Path.Combine(catalogue, Path.GetRelativePath(source, fichier));
                Directory.CreateDirectory(Path.GetDirectoryName(cible));
                File.Copy(fichier, cible, overwrite: true);
            }
        }

        // Copie l'exécutable du paquet à sa place fixe s'il a changé (un hook en cours le tient : réessayé au prochain
        // chargement).
        private static void Copier(String exePaquet, String exe = null)
        {
            exe ??= Exe;
            if (!File.Exists(exePaquet))
            {
                throw new FileNotFoundException("exécutable du hook absent du paquet", exePaquet);
            }

            if (File.Exists(exe) && File.ReadAllBytes(exe).AsSpan().SequenceEqual(File.ReadAllBytes(exePaquet)))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(exe));
            try
            {
                File.Copy(exePaquet, exe, overwrite: true);
            }
            catch (IOException ex) when (File.Exists(exe))
            {
                PluginLog.Warning(ex, "hook en cours d'usage : copie remise au prochain chargement");
            }
        }

        private static String SansEspace(String chemin)
        {
            if (!chemin.Contains(' '))
            {
                return chemin;
            }

            var tampon = new StringBuilder(32768);
            return GetShortPathNameW(chemin, tampon, tampon.Capacity) > 0 && !tampon.ToString().Contains(' ') ? tampon.ToString() : null;
        }

        // Au-dessus des fenêtres mais sans prendre le clavier, « Non » par défaut : une touche Entrée tapée ailleurs pendant
        // qu'elle apparaît ne vaut pas accord (vu le 25/10 : avec « Oui » par défaut et le premier plan, un Entrée l'a validée).
        private static Boolean Oui(String texte) =>
            MessageBoxW(IntPtr.Zero, texte, "NexusKeypad", 0x4 | 0x20 | 0x100 | 0x40000) == 6; // YESNO, ?, DEFBUTTON2, TOPMOST ; IDYES

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern Int32 MessageBoxW(IntPtr fenetre, String texte, String titre, UInt32 type);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern UInt32 GetShortPathNameW(String chemin, StringBuilder court, Int32 taille);
    }
}
#endif

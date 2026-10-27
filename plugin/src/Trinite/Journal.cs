#if NEXUS || STREAMDECK
namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Text.Json;

    // Journal commun des outils Nexus (NexusHarness\NexusShared\journal\FORMAT.md, produit « keypad ») : une ligne par
    // touche pressée dans %LOCALAPPDATA%\NexusJournal\keypad\<IA visée>\<AAAA-MM>.jsonl, pour la revue des outils.
    // Seulement dans les éditions de Julien (NexusKeypad, Stream Deck) : rien dans les éditions du Marketplace.
    // Jamais de titre de conversation, de texte ni de chemin : action = nom fixe de la touche, erreur = nom du type
    // d'exception ou une cause fixe. Local au PC, jamais envoyé ; NEXUS_JOURNAL=0 le coupe. Jamais bloquant.
    internal static class Journal
    {
        private static readonly String[] Agents = ["claude", "codex", "antigravity"];
        private static readonly String[] Applis = ["claude.exe", "codex.exe", "antigravity.exe"];
        private static readonly Object Verrou = new();
#if STREAMDECK
        private const String Cadre = "streamdeck";
#else
        private const String Cadre = "logi";
#endif
        // Version du paquet installé (Plugins/<nom>/metadata), sinon celle de l'assemblage (Stream Deck).
        private static readonly String Outil = MiseAJour.Version(
            Path.GetDirectoryName(Path.GetDirectoryName(typeof(Journal).Assembly.Location)) ?? "") is { Length: > 0 } version
                ? version
                : typeof(Journal).Assembly.GetName().Version?.ToString(3);

        private static Boolean Actif => Environment.GetEnvironmentVariable("NEXUS_JOURNAL")?.Trim().ToLowerInvariant()
            is not ("0" or "off" or "false" or "no");

        private static String Racine => Path.Combine(
            Environment.GetEnvironmentVariable("NEXUS_JOURNAL_HOME") is { Length: > 0 } dossier
                ? dossier
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusJournal"),
            "keypad");

        // ia : 0 Claude, 1 Codex, 2 Antigravity (hors de 0..2 : rien, le journal est rangé par IA visée).
        // issue : ✓, ~, ✗ (FORMAT.md) ; erreur : cause courte et fixe, ou null.
        public static void Ecrire(Int32 ia, String action, String issue, Double ms, String erreur = null)
        {
            try
            {
                if (ia is < 0 or > 2 || !Actif)
                {
                    return;
                }

                var maintenant = DateTime.UtcNow;
                var ligne = JsonSerializer.Serialize(new
                {
                    v = 1,
                    ts = maintenant.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
                    agent = Agents[ia],
                    outil = Outil,
                    appel = (String)null,
                    app = Applis[ia],
                    cadre = Cadre,
                    action,
                    technique = (String)null,
                    issue,
                    ms = (Int64)Math.Round(ms),
                    premier_plan = true, // une touche du pad amène l'appli de l'IA devant ou y tape
                    erreur,
                });
                var chemin = Path.Combine(Racine, Agents[ia], maintenant.ToString("yyyy-MM", CultureInfo.InvariantCulture) + ".jsonl");
                lock (Verrou)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(chemin));
                    File.AppendAllText(chemin, ligne + "\n");
                }
            }
            catch (Exception)
            {
                // un journal ne bloque jamais une touche
            }
        }
    }
}
#endif

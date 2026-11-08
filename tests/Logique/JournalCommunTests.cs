using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Loupedeck.TrinitePlugin;
using static NexusKeypad.Tests.Commun;

namespace NexusKeypad.Tests;

[CollectionDefinition(nameof(JournalIsole), DisableParallelization = true)]
public sealed class JournalIsole;

// The common Nexus journal (NexusShared\journal\FORMAT.md, product « keypad »): every key press handled by the pad writes
// one line, so a journal that goes silent (as the relay's did for 9 days after a rewrite) breaks this test instead of
// passing unnoticed. The pad's gestures themselves drive real windows, so the line is written by the same call each
// key's end runs (Moteur.Journaliser), under an isolated NEXUS_JOURNAL_HOME: the real ~\.nexus\journal is never touched.
[Collection(nameof(JournalIsole))]
public sealed class JournalCommunTests : IDisposable
{
    private const String Titre = "Secret conversation title 7f3a";
    private static readonly String[] Agents = ["claude", "codex", "antigravity"];
    private static readonly String[] Applis = ["claude.exe", "codex.exe", "antigravity.exe"];
    private static readonly String[] Issues = ["✓", "=", "~", "✗", "refus", "différé", "arrêt"];
    private static readonly String[] Gestes = ["connect", "capture", "photo", "dictate", "open_conversation", "open_app", "usage"];
    private static readonly String[] Cles = ["v", "ts", "agent", "outil", "appel", "app", "cadre", "action", "technique", "issue", "ms", "premier_plan", "erreur"];

    private readonly String _racine = Directory.CreateTempSubdirectory("nexus-journal").FullName;
    private readonly String? _homeAvant = Environment.GetEnvironmentVariable("NEXUS_JOURNAL_HOME");
    private readonly String? _actifAvant = Environment.GetEnvironmentVariable("NEXUS_JOURNAL");

    public JournalCommunTests()
    {
        Environment.SetEnvironmentVariable("NEXUS_JOURNAL_HOME", this._racine);
        Environment.SetEnvironmentVariable("NEXUS_JOURNAL", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("NEXUS_JOURNAL_HOME", this._homeAvant);
        Environment.SetEnvironmentVariable("NEXUS_JOURNAL", this._actifAvant);
        Directory.Delete(this._racine, true);
    }

    private static String VersionDuPlugin() => Regex.Match(
        File.ReadAllText(Path.Combine(Depot, "plugin", "src", "TrinitePadPlugin.csproj")), "<Version>([^<]+)</Version>").Groups[1].Value;

    private List<JsonElement> Lignes(Int32 ia)
    {
        var mois = DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var fichier = Path.Combine(this._racine, "keypad", Agents[ia], mois + ".jsonl");
        Vrai(File.Exists(fichier), $"journal commun : {fichier} écrit (keypad\\<agent>\\<AAAA-MM>.jsonl)");
        var texte = File.ReadAllText(fichier);
        Vrai(!texte.Contains(Titre, StringComparison.Ordinal), "journal commun : jamais le titre d'une conversation");
        return texte.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonDocument.Parse(l).RootElement.Clone()).ToList();
    }

    private static void Conforme(JsonElement ligne, Int32 ia, String action, String issue, String? erreur)
    {
        var cles = ligne.EnumerateObject().Select(p => p.Name).ToArray();
        Vrai(cles.Order(StringComparer.Ordinal).SequenceEqual(Cles.Order(StringComparer.Ordinal)), $"journal commun : champs du format v1 seulement ({String.Join(", ", cles)})");
        Vrai(ligne.GetProperty("v").GetInt32() == 1, "journal commun : v = 1");
        Vrai(DateTime.TryParseExact(ligne.GetProperty("ts").GetString(), "yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var ts) && Math.Abs((DateTime.UtcNow - ts).TotalMinutes) < 5,
            "journal commun : ts UTC ISO 8601, à l'heure de l'appui");
        var agent = ligne.GetProperty("agent").GetString();
        Vrai(agent == Agents[ia] && Agents.Contains(agent), "journal commun : agent = l'IA visée par la touche");
        Vrai(ligne.GetProperty("outil").GetString() == VersionDuPlugin(), "journal commun : outil = version du plugin");
        Vrai(ligne.GetProperty("appel").ValueKind == JsonValueKind.Null, "journal commun : appel présent (null, pas de lot)");
        Vrai(ligne.GetProperty("app").GetString() == Applis[ia], "journal commun : app = l'appli de l'IA visée");
        Vrai(ligne.GetProperty("cadre").GetString() == "logi", "journal commun : cadre logi (édition NexusKeypad)");
        Vrai(ligne.GetProperty("action").GetString() == action && Gestes.Contains(action), "journal commun : action = nom fixe de la touche");
        var lue = ligne.GetProperty("issue").GetString();
        Vrai(lue == issue && Issues.Contains(lue), $"journal commun : issue {issue} (lue : {lue})");
        Vrai(ligne.GetProperty("ms").ValueKind == JsonValueKind.Number && ligne.GetProperty("ms").TryGetInt64(out var ms) && ms >= 0,
            "journal commun : ms entier positif ou nul");
        Vrai(ligne.GetProperty("premier_plan").ValueKind is JsonValueKind.True or JsonValueKind.False, "journal commun : premier_plan booléen");
        var cause = ligne.GetProperty("erreur");
        Vrai(erreur is null ? cause.ValueKind == JsonValueKind.Null : cause.GetString() == erreur, "journal commun : erreur = cause courte ou null");
        Vrai(cause.ValueKind == JsonValueKind.Null || cause.GetString() is { Length: <= 80 } c && c.IndexOfAny(['"', ':', '(', '[']) < 0,
            "journal commun : erreur sans texte d'écran (80 caractères, rien après un guillemet, deux-points, parenthèse ou crochet)");
    }

    [Fact]
    public void Chaque_appui_ecrit_sa_ligne_au_format_v1()
    {
        Moteur.Journaliser(1, "open_app", "✓", null, 312.6);
        Moteur.Journaliser(0, "open_conversation", "~", "not in front", 3001);
        Moteur.Journaliser(2, "usage", "✗", nameof(InvalidOperationException), 0.2);
        Moteur.Journaliser(0, "photo", "✓", "draft not empty", 41); // abandon sans échec explicite : un échec

        var codex = this.Lignes(1);
        Vrai(codex.Count == 1, "journal commun : une ligne par appui (Codex)");
        Conforme(codex[0], 1, "open_app", "✓", null);
        Vrai(codex[0].GetProperty("ms").GetInt64() == 313, "journal commun : durée arrondie en ms");

        var claude = this.Lignes(0);
        Vrai(claude.Count == 2, "journal commun : une ligne par appui (Claude)");
        Conforme(claude[0], 0, "open_conversation", "~", "not in front");
        Conforme(claude[1], 0, "photo", "✗", "draft not empty");

        var antigravity = this.Lignes(2);
        Vrai(antigravity.Count == 1, "journal commun : une ligne par appui (Antigravity)");
        Conforme(antigravity[0], 2, "usage", "✗", nameof(InvalidOperationException));
    }

    [Fact]
    public void Rien_hors_des_IA_et_journal_coupe_par_NEXUS_JOURNAL()
    {
        Moteur.Journaliser(-1, "?", "✓", null, 5); // touche de la page 2 ou horloge : aucune IA visée
        Environment.SetEnvironmentVariable("NEXUS_JOURNAL", "0");
        Moteur.Journaliser(1, "open_app", "✓", null, 5);
        Vrai(!Directory.Exists(Path.Combine(this._racine, "keypad")) || !Directory.EnumerateFiles(Path.Combine(this._racine, "keypad"), "*", SearchOption.AllDirectories).Any(),
            "journal commun : rien hors des IA, rien quand NEXUS_JOURNAL=0");
    }
}

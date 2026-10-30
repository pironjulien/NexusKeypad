using System.Text.Json;
using System.Text.Json.Nodes;
using Loupedeck.TrinitePlugin;
using static NexusKeypad.Tests.Commun;

namespace NexusKeypad.Tests;

// Antigravity's quotas read on its Settings › Models page or its usage panel (synthetic text only, no profile or screen
// read), and Claude's quota read from its desktop history (synthetic profiles only). QuotaAntigravity.Fichier is shared
// state: every test that redirects it lives in this class, whose tests never run in parallel with each other.
public sealed class QuotaTests
{
    // Real reading of 20/10: most used group per window.
    internal const String Page = """
        Models & Usage
        Plan
        Your Plan: Google AI Pro
        Gemini Models
        Weekly Limit Remaining
        You have used some of your weekly limit, it will fully refresh in 6 days, 9 hours.
        33%
        Five Hour Limit Remaining
        You have used some of your 5-hour limit, it will fully refresh in 14 minutes.
        3%
        Claude and GPT models
        Weekly Limit Remaining
        100%
        Five Hour Limit Remaining
        100%
        """;

    private static Boolean Proche(Double? valeur, Double attendu) => valeur is Double v && Math.Abs(v - attendu) < 1e-9;

    [Fact]
    public void Page_Settings_Models_d_Antigravity()
    {
        var qa = QuotaAntigravity.Analyser(Page);
        Vrai(Math.Abs((qa?.Cinq ?? -1) - 0.97) < 1e-9, "Antigravity 5 h : 3 % restants = 97 % utilisés");
        Vrai(Math.Abs((qa?.Semaine ?? -1) - 0.67) < 1e-9, "Antigravity semaine : 33 % restants = 67 % utilisés");
        Vrai(QuotaAntigravity.Analyser("autre dialogue") is null, "page illisible → rien");
        var finCinq = (qa?.Groupes.Single(g => g.Cinq && g.Utilise > 0.9).Fin ?? DateTime.MinValue) - DateTime.UtcNow;
        Vrai(finCinq > TimeSpan.FromMinutes(13) && finCinq <= TimeSpan.FromMinutes(14), "Antigravity 5 h : remise à zéro dans 14 min");
        var finSemaine = (qa?.Groupes.Single(g => !g.Cinq && g.Utilise > 0.6).Fin ?? DateTime.MinValue) - DateTime.UtcNow;
        Vrai(finSemaine > new TimeSpan(6, 8, 59, 0) && finSemaine <= new TimeSpan(6, 9, 0, 0), "Antigravity semaine : remise à zéro dans 6 j 9 h");
        // Most used group reset: the next one takes the gauge (Gemini 80 % done, Claude and GPT 50 % for 4 more hours).
        var instant = DateTime.UtcNow;
        var groupes = new Quota(null, null)
        {
            Groupes = [new(true, 0.8, instant.AddMinutes(-1)), new(true, 0.5, instant.AddHours(4)), new(false, 0.6, null)],
        };
        var actuel = QuotaAntigravity.Actuel(groupes, instant.AddDays(-8));
        Vrai(actuel.Cinq == 0.5 && actuel.Semaine == 0, "remise à zéro par groupe ; sans heure, au bout de sa durée");
        Vrai(QuotaAntigravity.Actuel(groupes, instant).Semaine == 0.6, "semaine sans heure, lue à l'instant : gardée");
    }

    [Fact]
    public void Ancien_cache_garde_ses_heures_de_remise_a_zero()
    {
        var instant = DateTime.UtcNow;
        var cache = Path.Combine(Directory.CreateTempSubdirectory("nexus-quota").FullName, "quota-antigravity.json");
        var vraiCache = QuotaAntigravity.Fichier;
        QuotaAntigravity.Fichier = cache; // temporary file: the running plugin's real cache is never touched
        try
        {
            File.WriteAllText(cache, JsonSerializer.Serialize(new
            {
                cinq = 0.97, semaine = 0.4, finCinq = instant.AddMinutes(-1), finSemaine = instant.AddDays(3), lu = instant.AddMinutes(-15),
            }));
            var garde = QuotaAntigravity.Garde(out _);
            Vrai(garde?.Cinq == 0 && garde?.Semaine == 0.4, "ancien cache : heure de remise à zéro gardée appliquée");
        }
        finally
        {
            QuotaAntigravity.Fichier = vraiCache;
            Directory.Delete(Path.GetDirectoryName(cache), true);
        }
    }

    [Fact]
    public void Delais_et_scripts_du_releve()
    {
        Vrai(QuotaAntigravity.Delai("it will fully refresh in 4 days, 23 hours.") == new TimeSpan(4, 23, 0, 0)
            && QuotaAntigravity.Delai("Weekly Limit Remaining") is null, "délai de remise à zéro lu en jours, heures, minutes");
        // The settings window (Models tab), never the first [role=dialog] found (a "Conversation Archived … Undo"
        // notification is one too and blocked the reading, 26/10).
        foreach (var script in new[] { "Passif", "Actif" })
        {
            var code = (String)typeof(QuotaAntigravity).GetField(script, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
            Vrai(!code.Contains("document.querySelector('[role=dialog]')") && code.Contains("=== 'Models'"), $"quota d'Antigravity ({script}) : vise la fenêtre des réglages");
        }
    }

    [Fact]
    public void Panneau_d_usage_actuel_et_formats_anciens()
    {
        var maintenant = DateTime.UtcNow;
        var actuel = QuotaAntigravity.Analyser("""
            Usage
            Gemini Models
            Weekly limit
            93% left
            Resets in 6d 6h
            5h limit
            100% left
            Claude and GPT models
            Weekly limit
            100% left
            5h limit
            100% left
            Gemini 4 Argon
            Weekly limit
            100% left
            """);
        Vrai(Proche(actuel?.Cinq, 0) && Proche(actuel?.Semaine, .07), "Panneau visible actuel : 0 % utilisés en 5 h et 7 % semaine.");
        Vrai(actuel?.Groupes.Count == 5, "Les cinq fenêtres des trois groupes restent séparées.");
        Vrai(actuel.Groupes.Count(g => g.Cinq) == 2, "Le groupe Argon hebdomadaire ne fabrique pas de fenêtre 5 h.");
        Vrai(actuel.Groupes[0].Fin is DateTime fin && fin >= maintenant.AddDays(6).AddHours(6) && fin < maintenant.AddDays(6).AddHours(6).AddSeconds(2), "Délai après la valeur conservé sur sa fenêtre.");
        Vrai(actuel.Groupes.Skip(1).All(g => g.Fin is null), "Aucun délai propagé aux fenêtres ou groupes suivants.");

        foreach (var ordre in new[] { "Resets in 42m\n75% left", "75% left\nResets in 42m" })
        {
            var q = QuotaAntigravity.Analyser("Gemini Models\n5h limit\n" + ordre);
            Vrai(Proche(q?.Cinq, .25) && q?.Semaine is null, "Valeur et fenêtre courtes préservées dans les deux ordres.");
            Vrai(q.Groupes[0].Fin is DateTime finCourte && finCourte > DateTime.UtcNow.AddMinutes(41) && finCourte <= DateTime.UtcNow.AddMinutes(42), "Délai court avant ou après valeur.");
        }

        var ancien = QuotaAntigravity.Analyser("Gemini Models\nWeekly Limit Remaining\nYou have used some of your weekly limit, it will fully refresh in 6 days, 9 hours.\n33%\nFive Hour Limit Remaining\n3%");
        Vrai(Proche(ancien?.Cinq, .97) && Proche(ancien?.Semaine, .67), "Ancien format et valeurs sans suffixe left conservés.");
        Vrai(ancien.Groupes[0].Fin is not null && ancien.Groupes[1].Fin is null, "Délai ancien limité à sa fenêtre.");
    }

    [Fact]
    public void Panneau_fragmente_et_ambiguites()
    {
        var memesLignes = QuotaAntigravity.Analyser("Gemini Models\nWeekly limit 93% left\n5h limit 100% left");
        Vrai(Proche(memesLignes?.Cinq, 0) && Proche(memesLignes?.Semaine, .07), "Libellé et valeur sur une même ligne visuelle.");
        var fragments = QuotaAntigravity.Analyser("Gemini\nModels\nWeekly\nlimit\n93\n% left\n5h limit\n100%\nleft");
        Vrai(Proche(fragments?.Cinq, 0) && Proche(fragments?.Semaine, .07), "Nœuds DOM séparés des libellés et pourcentages.");
        var separes = QuotaAntigravity.Analyser("Gemini Models\nWeekly limit\nResets in 3d\nClaude and GPT models\n5h limit\n80% left");
        Vrai(separes?.Semaine is null && Proche(separes?.Cinq, .2) && separes.Groupes[0].Fin is null, "Groupe sans valeur ne récupère ni valeur ni délai du groupe suivant.");
        var fenetres = QuotaAntigravity.Analyser("Gemini Models\nWeekly limit\nResets in 3d\n5h limit\n80% left");
        Vrai(fenetres?.Semaine is null && fenetres?.Groupes[0].Fin is null, "Fenêtre sans valeur ne prend pas celle de la suivante.");
        Vrai(QuotaAntigravity.Analyser("Usage\nWeekly limit\n93% left") is null, "Un pourcentage sans groupe identifié ne crée pas de quota.");
        Vrai(QuotaAntigravity.Analyser("Gemini Models\nWeekly limit\n200% left") is null, "Pourcentage supérieur à cent ignoré.");
        Vrai(QuotaAntigravity.Analyser("Gemini Models\nWeekly limit\n20% left\n30% left") is null, "Plusieurs valeurs pour une fenêtre restent ambiguës.");
        Vrai(QuotaAntigravity.Analyser("Gemini Models\nWeekly limit\nAutre panneau\n93% left") is null, "Texte étranger coupe l'association fenêtre-valeur.");
        var argon = QuotaAntigravity.Analyser("Gemini4Argon\nWeekly limit\n50% left");
        Vrai(Proche(argon?.Semaine, .5) && argon?.Cinq is null, "Nom Argon compact accepté sans fausse fenêtre 5 h.");
        foreach (var partArgon in new[] { 100, 50 })
        {
            var inverse = QuotaAntigravity.Analyser($"Gemini Models\nWeekly limit\n93% left\n5h limit\n100% left\nClaude and GPT models\nWeekly limit\n100% left\n5h limit\n100% left\nGemini 4 Argon\n{partArgon}\n% left\nWeekly limit");
            Vrai(inverse?.Groupes.Count == 5 && Proche(inverse.Groupes[4].Utilise, 1 - partArgon / 100d), "Argon : valeur avant libellé conservée, 100 % ou 50 % restants.");
            Vrai(inverse.Groupes[4].Cinq == false && inverse.Groupes[4].Fin is null && Proche(inverse.Cinq, 0), "Bloc Argon inversé reste hebdomadaire, sans délai ou quota 5 h emprunté.");
        }

        Vrai(QuotaAntigravity.Analyser("Gemini Models\n50% left\nGemini 4 Argon\nWeekly limit") is null, "Argon ne récupère pas une valeur d'un autre groupe.");
        Vrai(QuotaAntigravity.Analyser("Gemini 4 Argon\n50% left\nClaude and GPT models\nWeekly limit") is null, "Une valeur Argon sans libellé ne fuit pas vers le groupe suivant.");
        Vrai(QuotaAntigravity.Analyser("Gemini 4 Argon\n50% left\n5h limit") is null, "Valeur préalable Argon jamais attribuée à une fenêtre 5 h.");
        Vrai(QuotaAntigravity.Analyser("Gemini 4 Argon\n50% left\n100% left\nWeekly limit") is null, "Deux valeurs Argon préalables restent ambiguës.");
        foreach (var delaiFragmente in new[] { "Resets in\n6d 6h", "Resets in 6d\n6h", "Resets\nin\n6d\n6h" })
        {
            var q = QuotaAntigravity.Analyser("Gemini Models\nWeekly limit\n93% left\n" + delaiFragmente);
            Vrai(Proche(q?.Semaine, .07) && q.Groupes[0].Fin is DateTime remise && remise > DateTime.UtcNow.AddHours(149.99) && remise <= DateTime.UtcNow.AddHours(150), "Délai fragmenté recomposé intégralement dans sa fenêtre.");
        }

        var coupeDelai = QuotaAntigravity.Analyser("Gemini Models\nWeekly limit\n93% left\nResets in 6d\n5h limit\n50% left\n6h");
        Vrai(coupeDelai.Groupes[0].Fin is DateTime finSixJours && finSixJours > DateTime.UtcNow.AddHours(143.99) && finSixJours <= DateTime.UtcNow.AddHours(144)
            && coupeDelai.Groupes[1].Fin is null, "Fragments du délai ne traversent jamais une fenêtre.");
        var coupeGroupeDelai = QuotaAntigravity.Analyser("Gemini Models\nWeekly limit\n93% left\nResets in\nClaude and GPT models\n5h limit\n50% left\n6h");
        Vrai(coupeGroupeDelai.Groupes.Count == 2 && coupeGroupeDelai.Groupes.All(g => g.Fin is null), "Fragments du délai ne traversent jamais un groupe.");
    }

    [Theory]
    [InlineData("Resets in 6d 6h", 6 * 24 * 60 + 6 * 60)]
    [InlineData("Resets in 42m", 42)]
    [InlineData("Resets in 1h42m", 102)]
    [InlineData("it will fully refresh in 4 days, 23 hours.", 4 * 24 * 60 + 23 * 60)]
    [InlineData("Resets in 0m", 0)]
    public void Delai_ancien_ou_compact(String texte, Int32 minutes) =>
        Assert.Equal(TimeSpan.FromMinutes(minutes), QuotaAntigravity.Delai(texte));

    [Fact]
    public void Delai_en_secondes() => Assert.Equal(TimeSpan.FromSeconds(10), QuotaAntigravity.Delai("Resets in 10s"));

    [Theory]
    [InlineData("Resets in 999999999999999999999d")]
    [InlineData("Resets in 8d 1s")]
    [InlineData("Resets in 6daylight")]
    [InlineData("Resets in 2.5 hours")]
    [InlineData("Weekly limit")]
    public void Delai_invalide_ignore(String texte) => Assert.Null(QuotaAntigravity.Delai(texte));

    [Fact]
    public void Modele_selectionne_et_cache_partage()
    {
        var plusieurs = "Gemini Models\nWeekly limit\n70% left\n5h limit\n20% left\nClaude and GPT models\nWeekly limit\n100% left\n5h limit\n100% left\nGemini 4 Argon\nWeekly limit\n0% left";
        var gemini = QuotaAntigravity.Analyser(plusieurs, "Select model, current: Gemini 3.8 Flash High");
        Vrai(Proche(gemini.Cinq, .8) && Proche(gemini.Semaine, .3) && gemini.GroupeSelectionne == "gemini", "Le modèle réellement sélectionné ne prend pas le quota Argon épuisé.");
        Vrai(gemini.Groupes.Count == 5 && gemini.Groupes.Count(g => g.Principal) == 2, "Le détail de tous les groupes et le marqueur principal préexistant sont conservés.");
        Vrai(Proche(QuotaAntigravity.Analyser(plusieurs).Semaine, 1), "Sans sélection prouvée, le maximum historique reste inchangé.");
        Vrai(Proche(QuotaAntigravity.Analyser(plusieurs, "Unknown model").Semaine, 1), "Modèle inconnu : aucun groupe inventé.");
        var claude = QuotaAntigravity.Analyser(plusieurs, "Select model, current: Claude Opus 5.5 Medium");
        Vrai(Proche(claude.Cinq, 0) && Proche(claude.Semaine, 0) && claude.GroupeSelectionne == "claude-gpt", "Le groupe Claude et GPT suit également la sélection réelle.");
        var argonChoisi = QuotaAntigravity.Analyser(plusieurs, "Gemini4Argon Medium");
        Vrai(argonChoisi.Cinq is null && Proche(argonChoisi.Semaine, 1) && argonChoisi.GroupeSelectionne == "argon", "Argon choisi : aucune fenêtre 5 h empruntée à Gemini.");
        Vrai(QuotaAntigravity.FamilleModele("GPT-OSS 120B") == "claude-gpt" && QuotaAntigravity.FamilleModele(null) is null, "Noms de modèles et lecture absente distincts.");
        var absent = QuotaAntigravity.Analyser("Gemini Models\nWeekly limit\n70% left", "Claude Opus 5.5");
        Vrai(absent.Cinq is null && absent.Semaine is null, "Famille choisie absente du relevé : quota inconnu, jamais un autre groupe.");
        var remis = QuotaAntigravity.Actuel(gemini with { Groupes = gemini.Groupes.Select(g => g.Groupe == "gemini" ? g with { Fin = DateTime.UtcNow.AddSeconds(-1) } : g).ToArray() }, DateTime.UtcNow);
        Vrai(Proche(remis.Cinq, 0) && Proche(remis.Semaine, 0), "Remise à zéro du modèle choisi : Argon épuisé ne prend jamais sa place.");
        var precedent = QuotaAntigravity.Fichier;
        var temporaire = Path.Combine(Path.GetTempPath(), "nexuskeypad-quota-" + Guid.NewGuid().ToString("N"));
        try
        {
            QuotaAntigravity.Fichier = Path.Combine(temporaire, "quota.json");
            var lu = DateTime.UtcNow.AddMinutes(-6);
            QuotaAntigravity.Garder(gemini, lu);
            var relu = QuotaAntigravity.Garde(out var date);
            Vrai(date == lu && relu.GroupeSelectionne == "gemini" && Proche(relu.Semaine, .3), "Cache partagé : famille, consommation et vraie date conservées.");
            Vrai(relu.Groupes.All(g => g.Groupe is not null) && relu.Groupes.Count(g => g.Principal) == 2, "Noms des groupes et anciens marqueurs relus sans perte.");
            var plusRecente = lu.AddMinutes(1);
            QuotaAntigravity.Garder(gemini with { Groupes = gemini.Groupes.Select(g => g.Groupe == "gemini" ? g with { Utilise = .4 } : g).ToArray() }, plusRecente);
            QuotaAntigravity.Garder(gemini, lu);
            var conservee = QuotaAntigravity.Garde(out var dateRecente);
            Vrai(dateRecente == plusRecente && Proche(conservee.Semaine, .4), "Choix sur copie ancienne : le relevé de consommation plus récent n'est pas écrasé.");
            File.WriteAllText(QuotaAntigravity.Fichier, "{\"cinq\":0.2,\"semaine\":0.4,\"groupes\":[{\"cinq\":true,\"utilise\":0.2,\"fin\":null,\"principal\":true}],\"lu\":\"" + DateTime.UtcNow.ToString("O") + "\"}");
            var compatible = QuotaAntigravity.Garde(out _);
            Vrai(Proche(compatible.Cinq, .2) && compatible.Groupes[0].Principal && compatible.GroupeSelectionne is null, "Cache précédent sans noms de groupes toujours compatible.");
        }
        finally
        {
            QuotaAntigravity.Fichier = precedent;
            if (Directory.Exists(temporaire))
            {
                Directory.Delete(temporaire, true);
            }
        }
    }

    private static readonly DateTime MaintenantClaude = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    private static JsonObject Echantillon(String org, Double cinq, Double? semaine, DateTime? quand = null) => new()
    {
        ["org"] = org,
        ["t"] = new DateTimeOffset(quand ?? MaintenantClaude).ToUnixTimeMilliseconds(),
        ["u"] = semaine is { } s ? new JsonObject { ["fh"] = cinq, ["sd"] = s } : new JsonObject { ["fh"] = cinq },
    };

    private static String Historique(params JsonNode[] samples) => new JsonObject { ["version"] = 2, ["samples"] = new JsonArray(samples) }.ToJsonString();

    private static Boolean Egale(Quota q, Double? cinq, Double? semaine) => q is not null && q.Cinq == cinq && q.Semaine == semaine;

    [Fact]
    public void Quota_Claude_de_l_organisation_courante()
    {
        var maintenant = MaintenantClaude;
        var historique = Historique(Echantillon("current-org", 9, 21), Echantillon("other-org", 91, 87));
        Vrai(Egale(Observateur.QuotaHistoriqueClaude(historique, maintenant, "current-org"), .09, .21), "Le filtre retient l'organisation courante, pas le dernier autre compte.");
        Vrai(Observateur.QuotaHistoriqueClaude(historique, maintenant, "missing-org") is null, "Une organisation absente reste inconnue.");
        Vrai(Observateur.QuotaHistoriqueClaude(Historique(Echantillon("other-org", 91, 87)), maintenant, "current-org") is null,
            "Un historique d'une autre organisation ne sert jamais de secours.");
        var malforme = Echantillon("other-org", 91, 87);
        malforme["org"] = 7;
        Vrai(Egale(Observateur.QuotaHistoriqueClaude(Historique(Echantillon("current-org", 9, 21), malforme), maintenant, "current-org"), .09, .21),
            "Un identifiant d'organisation malformé est ignoré, sans masquer le bon échantillon.");
        Vrai(Egale(Observateur.QuotaHistoriqueClaude(Historique(Echantillon("current-org", 7, null), Echantillon("other-org", 91, 87)), maintenant, "current-org"), .07, null),
            "Une fenêtre absente reste inconnue, sans prendre celle d'une autre organisation.");
        Vrai(Egale(Observateur.QuotaHistoriqueClaude(Historique(Echantillon("current-org", 9, 21, maintenant.AddHours(-6)), Echantillon("other-org", 91, 87)), maintenant, "current-org"), 0, .21),
            "L'expiration porte sur l'organisation sélectionnée, pas le dernier autre échantillon.");
        var sansOrg = Echantillon("current-org", 9, 21);
        sansOrg.Remove("org");
        Vrai(Observateur.QuotaHistoriqueClaude(Historique(sansOrg), maintenant, "current-org") is null, "Un échantillon sans organisation ne prouve pas l'organisation courante.");
        Vrai(Egale(Observateur.QuotaHistoriqueClaude(Historique(Echantillon("single-org", 9, 21)), maintenant), .09, .21), "Le parseur sans filtre garde son contrat historique.");
    }

    [Fact]
    public void Quota_Claude_par_compte_et_perimetre()
    {
        var maintenant = MaintenantClaude;
        var historique = Historique(Echantillon("current-org", 9, 21), Echantillon("other-org", 91, 87));
        var racine = Directory.CreateTempSubdirectory("keypad-claude-quota-").FullName;
        try
        {
            var sessions = Path.Combine(racine, "claude-code-sessions");
            void Session(String compte, String org, String id)
            {
                var chemin = Path.Combine(sessions, compte, org);
                Directory.CreateDirectory(chemin);
                File.WriteAllText(Path.Combine(chemin, id + ".json"), new JsonObject { ["sessionId"] = id, ["cliSessionId"] = "cli-" + id }.ToJsonString());
            }

            Session("account-a", "current-org", "local_current");
            Session("account-b", "other-org", "local_other");
            File.WriteAllText(Path.Combine(racine, "plan-usage-history.json"), historique);
            Vrai(Egale(Observateur.QuotaHistoriqueClaudeCompte(racine, "account-a", maintenant, "local_current"), .09, .21),
                "Le chemin réel du lecteur transmet l'organisation prouvée par la fiche affichée.");
            Vrai(Egale(Observateur.QuotaHistoriqueClaudeCompte(racine, "account-b", maintenant, "local_other"), .91, .87), "Le second compte conserve son propre quota.");
            Vrai(Observateur.QuotaHistoriqueClaudeCompte(racine, "missing-account", maintenant, "local_current") is null, "Un compte absent n'emprunte pas le quota d'un autre.");
            Vrai(Observateur.QuotaHistoriqueClaudeCompte(racine, null, maintenant, "local_current") is null, "Un compte inconnu laisse les lecteurs courants décider.");
            Vrai(Observateur.QuotaHistoriqueClaudeCompte(racine, "..", maintenant, "local_current") is null, "Un nom de compte ne peut pas sortir de son dossier.");
            Vrai(Observateur.QuotaHistoriqueClaudeCompte(racine, "account-a", maintenant) is null,
                "Un dossier d'organisation unique ne prouve pas l'organisation affichée quand aucune conversation n'est identifiée.");
            Session("account-a", "second-org", "local_second");
            Vrai(Egale(Observateur.QuotaHistoriqueClaudeCompte(racine, "account-a", maintenant, "local_current"), .09, .21),
                "Avec plusieurs organisations, l'ID exact garde la bonne organisation.");
            Vrai(Observateur.QuotaHistoriqueClaudeCompte(racine, "account-a", maintenant, "local_other") is null,
                "Une fiche d'un autre compte ne prouve aucune organisation du compte courant.");
            Session("account-a", "second-org", "local_current");
            Vrai(Observateur.QuotaHistoriqueClaudeCompte(racine, "account-a", maintenant, "local_current") is null,
                "Deux fiches portant le même ID sont ambiguës et ne fournissent pas de quota historique.");
            Directory.CreateDirectory(Path.Combine(sessions, "account-empty"));
            Vrai(Observateur.QuotaHistoriqueClaudeCompte(racine, "account-empty", maintenant, "local_missing") is null, "Aucune fiche affichée ne produit pas de quota.");

            var perimetreA = Observateur.PerimetreQuotaClaudeCompte(racine, "account-b", "local_other");
            var perimetreB = Observateur.PerimetreQuotaClaudeCompte(racine, "account-a", "local_second");
            Vrai(perimetreA is not null && perimetreB is not null && perimetreA != perimetreB, "Compte et organisation définissent des périmètres distincts.");
            Session("account-a", "current-org", "local_current_unique");
            var autreOrg = Observateur.PerimetreQuotaClaudeCompte(racine, "account-a", "local_current_unique");
            Vrai(autreOrg is not null && autreOrg != perimetreB, "Changer d'organisation dans le même compte change le périmètre.");
            Vrai(Observateur.FusionnerQuotaClaude(new(.1, .2), null, perimetreB, autreOrg) is null,
                "Le changement d'organisation seul invalide la jauge du même compte.");
            Vrai(Egale(Observateur.FusionnerQuotaClaude(new(.1, .2), new(.3, null), perimetreA, perimetreA), .3, .2), "Une lecture partielle conserve la seconde fenêtre du même périmètre prouvé.");
            Vrai(Observateur.FusionnerQuotaClaude(new(.1, .2), null, perimetreA, perimetreB) is null, "Un changement de compte/org sans nouvelle lecture invalide l'ancienne jauge.");
            Vrai(Egale(Observateur.FusionnerQuotaClaude(new(.1, .2), new(.3, null), perimetreA, perimetreB), .3, null), "Une nouvelle lecture partielle ne récupère pas une fenêtre de l'ancien compte/org.");
            Vrai(Observateur.FusionnerQuotaClaude(new(.1, .2), null, perimetreA, null) is null, "Un périmètre inconnu ne conserve pas de jauge ancienne.");
            Vrai(Egale(Observateur.FusionnerQuotaClaude(new(.1, .2), new(null, .3), null, null), null, .3), "Un lecteur courant partiel en périmètre inconnu reste partiel.");
            Vrai(Egale(Observateur.FusionnerQuotaClaude(new(.1, .2), null, perimetreA, perimetreA), .1, .2), "Un lecteur muet du même périmètre prouvé peut garder sa mesure antérieure.");
            File.WriteAllText(Path.Combine(racine, "plan-usage-history.json"), "broken JSON");
            Vrai(Observateur.QuotaHistoriqueClaudeCompte(racine, "account-b", maintenant, "local_other") is null, "Un historique illisible reste inconnu.");
        }
        finally
        {
            Directory.Delete(racine, recursive: true);
        }
    }

    [Fact]
    public void Copie_privee_du_module_de_quota_identique()
    {
        // The private Claude plugin (NexusClaude catalog, nexus-keypad) ships the same quota module as the public mod
        // claude-quota\nexuskeypad-quota: one quota code, two packages. Absent outside the author's PC (public clone, CI).
        var prive = Path.GetFullPath(Path.Combine(Depot, "..", "NexusClaude", "plugins", "nexus-keypad", "hooks", "quota.ts"));
        Assert.SkipUnless(File.Exists(prive), "NexusClaude is not next to this repository: nothing to compare.");
        var publique = Path.Combine(Depot, "claude-quota", "nexuskeypad-quota", "hooks", "register.ts");
        Assert.True(File.ReadAllText(prive).Replace("\r\n", "\n") == File.ReadAllText(publique).Replace("\r\n", "\n"),
            @"NexusClaude\plugins\nexus-keypad\hooks\quota.ts differs from claude-quota\nexuskeypad-quota\hooks\register.ts: copy it again.");
    }
}

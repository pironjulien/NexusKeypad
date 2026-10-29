using Loupedeck.TrinitePlugin;
using System.Text.Json.Nodes;

var maintenant = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
var controles = 0;
void Vrai(Boolean condition, String nom)
{
    controles++;
    if (!condition) { throw new InvalidOperationException(nom); }
}

JsonObject Echantillon(String org, Double cinq, Double? semaine, DateTime? quand = null) => new()
{
    ["org"] = org,
    ["t"] = new DateTimeOffset(quand ?? maintenant).ToUnixTimeMilliseconds(),
    ["u"] = semaine is { } s ? new JsonObject { ["fh"] = cinq, ["sd"] = s } : new JsonObject { ["fh"] = cinq },
};
String Historique(params JsonNode[] samples) => new JsonObject { ["version"] = 2, ["samples"] = new JsonArray(samples) }.ToJsonString();
Boolean Egale(Quota q, Double? cinq, Double? semaine) => q is not null && q.Cinq == cinq && q.Semaine == semaine;

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
    Console.WriteLine($"Quota Claude : {controles} contrôles réussis, profils synthétiques uniquement.");
    return 0;
}
finally
{
    Directory.Delete(racine, recursive: true);
}

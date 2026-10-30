using Loupedeck.TrinitePlugin;
using static NexusKeypad.Tests.Commun;

namespace NexusKeypad.Tests;

// States noted by the hooks, alerts, order of a key, overall state of an AI and Claude's conversations.
public sealed class EtatsTests
{
    internal static readonly DateTime Maintenant = DateTime.UtcNow;

    internal static Conversation C(Int32 a, Etat e, Boolean vu = false, Boolean relais = false) =>
        new(a, "id", "titre", e, Maintenant, Maintenant, vu, relais);

    internal static readonly Quota[] SansQuota = [null, null, null];

    internal static Instantane Instantane() =>
        new([[C(0, Etat.Travaille)], [], []], [1, 0, 0], SansQuota, [true, true, false], false, null, -1);

    [Fact]
    public void Disponibilite_et_premiere_observation_de_Claude_sont_distinctes()
    {
        // Availability and first observation are two distinct proofs: no progress while Claude is idle.
        var preparationClaude = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        var connexionClaude = new Connexion(EtatConnexion.Pret, "configuration verified") { PrepareLe = preparationClaude };
        var sansEvenementClaude = ConnexionPad.AvecObservationClaude(connexionClaude, false);
        Vrai(sansEvenementClaude.Etat == EtatConnexion.Pret && sansEvenementClaude.PrepareLe == preparationClaude,
            "Claude au repos reste disponible sans progression d'une opération inexistante.");
        Vrai(sansEvenementClaude.PremierEvenementObserve == false && sansEvenementClaude.Detail.Contains("has not been observed"),
            "Le diagnostic conserve explicitement l'absence du premier callback réel.");
        var avecEvenementClaude = ConnexionPad.AvecObservationClaude(connexionClaude, true);
        Vrai(avecEvenementClaude.Etat == EtatConnexion.Pret && avecEvenementClaude.PremierEvenementObserve == true
            && avecEvenementClaude.PrepareLe == preparationClaude, "Une vraie observation est distinguée sans modifier la préparation.");
        Vrai(connexionClaude.PremierEvenementObserve is null, "Une configuration seule ne fabrique aucune observation.");
        var connexionClaudeAReparer = new Connexion(EtatConnexion.Reparer, "missing file");
        Vrai(ConnexionPad.AvecObservationClaude(connexionClaudeAReparer, true) == connexionClaudeAReparer,
            "Un callback ne transforme jamais une configuration à réparer en connexion prête.");
    }

    [Fact]
    public void Etats_notes_par_les_hooks_expirent()
    {
        // A question or an end stays 12 h, work 20 min (beyond that, the agent is dead).
        Vrai(Observateur.Traduire("wait", Maintenant.AddMinutes(-1)) == Etat.Attend, "wait récent → attend");
        Vrai(Observateur.Traduire("ask", Maintenant.AddMinutes(-1)) == Etat.Travaille, "ask → travaille (boîte de fin pas encore posée)");
        Vrai(Observateur.Traduire("run", Maintenant.AddMinutes(-30)) == Etat.Travaille, "run de 30 min → travaille (commande longue)");
        Vrai(Observateur.Traduire("run", Maintenant.AddHours(-3)) == Etat.Repos, "run de 3 h → repos");
        Vrai(Observateur.Traduire("done", Maintenant.AddHours(-1)) == Etat.Fini, "done d'1 h → fini");
        Vrai(Observateur.Traduire("done", Maintenant.AddHours(-13)) == Etat.Repos, "done de 13 h → repos");
    }

    [Fact]
    public void Clignotement_seulement_question_ou_fin_non_vues_hors_relais()
    {
        Vrai(C(1, Etat.Attend).Alerte, "question non vue → alerte");
        Vrai(C(1, Etat.Fini).Alerte, "fin non vue → alerte");
        Vrai(!C(1, Etat.Attend, relais: true).Alerte, "question du relais → pas d'alerte");
        Vrai(!C(1, Etat.Fini, vu: true).Alerte, "fin vue → pas d'alerte");
        Vrai(!C(1, Etat.Travaille).Alerte, "travail → pas d'alerte");
    }

    [Fact]
    public void Ordre_d_une_touche()
    {
        // Question, end, work for the user, relay work, the rest.
        Vrai(Observateur.Rang(C(0, Etat.Attend)) > Observateur.Rang(C(0, Etat.Fini)), "question avant fin");
        Vrai(Observateur.Rang(C(0, Etat.Fini)) > Observateur.Rang(C(0, Etat.Travaille)), "fin avant travail");
        Vrai(Observateur.Rang(C(0, Etat.Travaille)) > Observateur.Rang(C(0, Etat.Travaille, relais: true)), "travail de l'utilisateur avant relais");
    }

    [Fact]
    public void Etat_d_ensemble_d_une_IA_et_relance()
    {
        // App closed → closed; open without a conversation → idle.
        var instantane = Instantane();
        Vrai(instantane.EtatIA(0) == Etat.Travaille, "conversation qui travaille → travaille");
        Vrai(instantane.EtatIA(1) == Etat.Repos, "appli ouverte sans conversation → repos");
        Vrai(instantane.EtatIA(2) == Etat.Ferme, "appli fermée → fermé");
        Vrai(ConnexionPad.RelanceOccupee(0, instantane), "relance refusée pendant un travail");
        Vrai(!ConnexionPad.RelanceOccupee(1, instantane), "une autre IA au repos peut être relancée");
        Vrai(ConnexionPad.RelanceOccupee(1, instantane with { DicteeIa = 1 }), "relance refusée pendant sa dictée");
        Vrai(ConnexionPad.RelanceOccupee(0, null), "état inconnu : pas de relance");
        Vrai(ConnexionPad.RelanceOccupee(0, instantane with { Conversations = [[C(0, Etat.Attend)], [], []] }), "relance refusée pendant une question");
    }

    [Fact]
    public void Quota_de_Claude_lu_dans_son_historique_de_bureau()
    {
        // plan-usage-history.json
        var exempleClaude = """{"version":2,"samples":[{"t":1793221096102,"org":"e35395a4","u":{"fh":9,"sd":69}}]}""";
        var tClaude = DateTimeOffset.FromUnixTimeMilliseconds(1793221096102).UtcDateTime;
        var qClaude = Observateur.QuotaHistoriqueClaude(exempleClaude, tClaude);
        Vrai(qClaude?.Cinq == 0.09 && qClaude?.Semaine == 0.69, "historique Claude : 5 h à 9 % et semaine à 69 %");
        var qClaudeExpCinq = Observateur.QuotaHistoriqueClaude(exempleClaude, tClaude.AddHours(6));
        Vrai(qClaudeExpCinq?.Cinq == 0 && qClaudeExpCinq?.Semaine == 0.69, "historique Claude après 6 h : 5 h remise à 0 %, semaine conservée");
        var qClaudeExpSemaine = Observateur.QuotaHistoriqueClaude(exempleClaude, tClaude.AddDays(8));
        Vrai(qClaudeExpSemaine?.Cinq == 0 && qClaudeExpSemaine?.Semaine == 0, "historique Claude après 8 j : 5 h et semaine remises à 0 %");
    }

    [Fact]
    public void Conversations_actives_de_Claude_filtrees_par_ID()
    {
        var maintClaude = DateTime.UtcNow;
        var cTravaille = new Conversation(0, "c1", "Tâche en cours", Etat.Travaille, maintClaude.AddHours(-10), maintClaude.AddHours(-10), false);
        Vrai(Observateur.ConversationClaudeActive(cTravaille, @"C:\ProjetA", "Autre", @"C:\ProjetB", maintClaude), "Claude : tâche en cours toujours active");
        var cAffichee = new Conversation(0, "c2", "Tâche affichée", Etat.Repos, maintClaude.AddHours(-10), maintClaude.AddHours(-10), true);
        Vrai(Observateur.ConversationClaudeActive(cAffichee, @"C:\ProjetA", cAffichee.Id, @"C:\ProjetA", maintClaude), "Claude : session exacte affichée à l'écran toujours active");
        var cVieille = new Conversation(0, "c3", "Vieille tâche", Etat.Repos, maintClaude.AddHours(-8), maintClaude.AddHours(-8), true);
        Vrai(!Observateur.ConversationClaudeActive(cVieille, @"C:\ProjetA", "Autre", @"C:\ProjetA", maintClaude), "Claude : tâche au repos > 6 h ignorée");
        var cAutreProjet = new Conversation(0, "c4", "Tâche autre projet", Etat.Repos, maintClaude.AddMinutes(-30), maintClaude.AddMinutes(-30), true);
        Vrai(!Observateur.ConversationClaudeActive(cAutreProjet, @"C:\ProjetB", cAffichee.Id, @"C:\ProjetA", maintClaude), "Claude : tâche au repos d'un autre projet ignorée");
        var cMemeProjet = new Conversation(0, "c5", "Tâche même projet récente", Etat.Repos, maintClaude.AddMinutes(-30), maintClaude.AddMinutes(-30), true);
        Vrai(Observateur.ConversationClaudeActive(cMemeProjet, @"C:\ProjetA", cAffichee.Id, @"C:\ProjetA", maintClaude), "Claude : tâche au repos du même projet < 6 h retenue");
        var cMemeTitre = cAffichee with { Id = "c6" };
        Vrai(!Observateur.ConversationClaudeActive(cMemeTitre, @"C:\ProjetA", cAffichee.Id, @"C:\ProjetA", maintClaude),
            "Claude : deux vieux chats de même titre, seul l'ID réellement affiché est retenu.");
        Vrai(!Observateur.ConversationClaudeActive(cAffichee, @"C:\ProjetA", null, @"C:\ProjetA", maintClaude),
            "Claude : titre connu sans ID affiché ne conserve pas une vieille session.");
        Vrai(!Observateur.ConversationClaudeActive(cMemeTitre with { Activite = maintClaude.AddMinutes(-1) }, @"C:\ProjetB", cAffichee.Id, @"C:\ProjetA", maintClaude),
            "Claude : homonyme récent d'un autre projet reste filtré.");
        foreach (var etatUrgent in new[] { Etat.Travaille, Etat.Attend, Etat.Autorisation, Etat.Fini, Etat.Erreur, Etat.Limite })
        {
            Vrai(Observateur.ConversationClaudeActive(cMemeTitre with { Etat = etatUrgent }, @"C:\ProjetB", cAffichee.Id, @"C:\ProjetA", maintClaude),
                "Claude : état actif préservé indépendamment du titre, de l'âge et du projet : " + etatUrgent);
        }
    }
}

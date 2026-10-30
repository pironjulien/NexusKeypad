using System.Text.Json;
using Loupedeck.TrinitePlugin;
using static NexusKeypad.Tests.Commun;

namespace NexusKeypad.Tests;

// Codex's journal: asynchronous questions (request_user_input_async, format recorded on 27/10) and the hooks that speed up
// the states of a proven parent turn, the journal keeping all its lasting information.
public sealed class JournalCodexTests
{
    private static String L(String h, String charge) => $$"""{"timestamp":"2026-10-27T{{h}}Z",{{charge}}}""";

    private static readonly String Debut = L("20:48:34.000", "\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}");
    private static readonly String Question = L("20:49:06.000", "\"type\":\"response_item\",\"payload\":{\"type\":\"function_call\",\"status\":\"completed\",\"name\":\"request_user_input_async\",\"arguments\":\"{\\\"questions\\\":[{\\\"title\\\":\\\"Courts ou détaillés ?\\\"}]}\",\"call_id\":\"call_dfc1744cb4404887847e77d2911650c0\"}");
    private static readonly String Fin = L("20:51:12.000", "\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"last_agent_message\":\"Relecture faite.\"}");
    private static readonly String Reponse = L("20:55:00.000", "\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"role\":\"user\",\"content\":[{\"type\":\"input_text\",\"text\":\"<send_user_message_question_reply>\\n[{\\\"questionItemId\\\":\\\"[\\\\\\\"request_user_input_async\\\\\\\",\\\\\\\"call_dfc1744cb4404887847e77d2911650c0\\\\\\\"]\\\"}]\"}]}");
    private static readonly String Ordinaire = L("20:56:00.000", "\"type\":\"event_msg\",\"payload\":{\"type\":\"user_message\",\"message\":\"Autre chose\"}");
    private static readonly String Citee = L("20:57:00.000", "\"type\":\"response_item\",\"payload\":{\"type\":\"function_call_output\",\"call_id\":\"call_autre\",\"output\":\"<send_user_message_question_reply> call_dfc1744cb4404887847e77d2911650c0\"}");
    private static readonly String Reprise = L("20:58:00.000", "\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}");
    private static readonly DateTime InstantJournal = new(2026, 10, 27, 21, 0, 0, DateTimeKind.Utc);

    private static String Multi() => L("20:49:30.000", $$"""
        "type":"response_item","payload":{"type":"function_call","name":"request_user_input_async","arguments":"{\"questions\":[{\"title\":\"q0\"},{\"title\":\"q1\"}]}","call_id":"call_multi123"}
        """.Trim());

    private static String ReponseA(String rangs) => L("20:59:00.000", $$"""
        "type":"response_item","payload":{"type":"message","role":"user","content":[{"type":"input_text","text":"<send_user_message_question_reply>\n[{{rangs}}]"}]}
        """.Trim());

    private static String Item(Int32 r) => $$"""{\"questionItemId\":\"[\\\"request_user_input_async\\\",\\\"call_multi123\\\",{{r}}]\"}""";

    private static readonly String NouvelleFin = L("20:59:30.000", "\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"last_agent_message\":\"Nouveau contrôle terminé.\"}");
    private static readonly String NouvelleQuestion = Question.Replace("20:49:06.000", "20:58:30.000").Replace("call_dfc1744cb4404887847e77d2911650c0", "call_nouvelle");
    private static readonly String NouvelleReponse = Reponse.Replace("call_dfc1744cb4404887847e77d2911650c0", "call_nouvelle").Replace("20:55:00.000", "20:59:45.000");
    private static readonly String Interruption = L("20:52:00.000", "\"type\":\"event_msg\",\"payload\":{\"type\":\"turn_aborted\"}");

    private static (Etat, DateTime) Journal(String[] lignes, Boolean relais, DateTime quand) => Observateur.EtatJournalCodex(lignes, relais, quand);

    [Fact]
    public void Questions_async_jusqu_a_leur_reponse()
    {
        // The chat shows a question until the reply (<send_user_message_question_reply> quoting the call_id), even after the
        // turn ended; never for the relay.
        var debut = Debut; var question = Question; var fin = Fin; var reponse = Reponse; var instantJournal = InstantJournal;
        Vrai(Journal([debut, question], false, instantJournal).Item1 == Etat.Travaille, "question async pendant le tour → travail non bloqué");
        Vrai(Journal([debut, question, fin], false, instantJournal) is (Etat.Attend, var q) && q.Minute == 49, "tour fini, question sans réponse → question (à son heure)");
        Vrai(Journal([debut, question, fin, reponse], false, instantJournal).Item1 == Etat.Fini, "réponse qui cite le call_id → la question se ferme");
        Vrai(Journal([debut, question, fin, Ordinaire], false, instantJournal).Item1 == Etat.Attend, "message ordinaire : pas une réponse, la carte reste");
        Vrai(Journal([debut, question, fin, Citee], false, instantJournal).Item1 == Etat.Attend, "citation dans une sortie d'outil → ne ferme rien");
        Vrai(Journal([debut, question, fin, Reprise], false, instantJournal.AddHours(13)).Item1 == Etat.Travaille,
            "question de plus de 12 h : effacée, le travail en cours se voit");
        Vrai(Journal([debut, question, fin], true, instantJournal).Item1 == Etat.Fini, "chat du relais → jamais de question");
        Vrai(Journal([debut, Multi(), fin, ReponseA(Item(0))], false, instantJournal).Item1 == Etat.Attend, "deux questions, une seule répondue → question");
        Vrai(Journal([debut, Multi(), fin, ReponseA(Item(0)), ReponseA(Item(1))], false, instantJournal).Item1 == Etat.Fini, "les deux répondues l'une après l'autre → fermé");
        Vrai(Journal([debut, Multi(), fin, ReponseA(Item(0) + "," + Item(1))], false, instantJournal).Item1 == Etat.Fini, "réponses groupées → fermé");
        Vrai(Journal([debut, fin], false, instantJournal).Item1 == Etat.Fini, "sans question async → inchangé");
        Vrai(Journal([debut, question, fin, Reprise], false, instantJournal).Item1 == Etat.Travaille,
            "ancienne question sans réponse : le nouveau tour montre le travail");
        Vrai(Journal([debut, question, fin, Reprise, NouvelleFin], false, instantJournal).Item1 == Etat.Fini,
            "ancienne question sans réponse : ne revient pas à la fin du nouveau tour");
        Vrai(Journal([debut, question, fin, Reprise, NouvelleQuestion, NouvelleFin, reponse], false, instantJournal).Item1 == Etat.Attend,
            "réponse tardive à l'ancienne question : la nouvelle question reste visible");
        Vrai(Journal([debut, question, fin, Reprise, NouvelleQuestion, NouvelleFin, reponse, NouvelleReponse], false, instantJournal).Item1 == Etat.Fini,
            "nouvelle question répondue : le nouveau tour retrouve sa fin");
    }

    [Fact]
    public void Tour_annule_avec_carte_async()
    {
        var debut = Debut; var question = Question; var interruption = Interruption; var instantJournal = InstantJournal;
        Vrai(Journal([debut, question, interruption], false, instantJournal) is (Etat.Repos, var annule) && annule.Minute == 52,
            "tour annulé avec carte async : repos à l'heure de l'annulation");
        Vrai(Journal([debut, question, interruption, Reponse], false, instantJournal).Item1 == Etat.Repos,
            "réponse réelle après annulation : le tour reste au repos");
        Vrai(Journal([debut, question, interruption, Reprise], false, instantJournal).Item1 == Etat.Travaille,
            "tour annulé avec carte sans réponse : le nouveau tour travaille");
        Vrai(Journal([debut, question, interruption, Reprise, NouvelleFin], false, instantJournal).Item1 == Etat.Fini,
            "tour annulé avec carte sans réponse : la nouvelle fin ne reprend pas sa question");
        Vrai(Journal([debut, question, interruption, Reprise, NouvelleQuestion, NouvelleFin], false, instantJournal).Item1 == Etat.Attend,
            "question d'un nouveau tour après annulation : signalée à sa fin");
        var reponseTardive = Reponse.Replace("20:55:00.000", "20:59:40.000");
        Vrai(Journal([debut, question, interruption, Reprise, NouvelleQuestion, NouvelleFin, reponseTardive], false, instantJournal).Item1 == Etat.Attend,
            "réponse réelle à la carte du tour annulé : ne ferme pas la nouvelle question");
        Vrai(Journal([debut, question, interruption, Reprise, NouvelleQuestion, NouvelleFin, reponseTardive, NouvelleReponse], false, instantJournal).Item1 == Etat.Fini,
            "réponse réelle à la nouvelle question après annulation : fin du nouveau tour");
    }

    private static String HookCodex(String etat, String evenement, String tour = "tour-a", String heure = "20:50:00.000") => JsonSerializer.Serialize(new
    {
        schema = 1, scope = "main", state = etat, @event = evenement, turn_id = tour,
        at = DateTimeOffset.Parse("2026-10-27T" + heure + "Z").ToUnixTimeMilliseconds() / 1000.0,
    });

    private static Etat Fusion(IEnumerable<String> journal, String hook, Boolean relais = false, DateTime? quand = null) =>
        Observateur.EtatJournalCodexAvecHook(journal, relais, quand ?? InstantJournal, hook).Item1;

    [Fact]
    public void Hooks_Codex_accelerent_un_tour_parent_prouve()
    {
        var debutHook = Debut.Replace("\"task_started\"", "\"task_started\",\"turn_id\":\"tour-a\"");
        var repriseHook = Reprise.Replace("\"task_started\"", "\"task_started\",\"turn_id\":\"tour-b\"");
        var question = Question; var fin = Fin; var interruption = Interruption;
        Vrai(Fusion([debutHook], HookCodex("perm", "PermissionRequest")) == Etat.Autorisation,
            "Codex : permission parent immédiate avant son événement de journal");
        var accordHook = L("20:49:10.000", "\"type\":\"event_msg\",\"payload\":{\"type\":\"exec_approval_request\"}");
        Vrai(Fusion([debutHook, accordHook], HookCodex("run", "PreToolUse")) == Etat.Travaille,
            "Codex : reprise après permission par hook parent récent");
        Vrai(Fusion([debutHook], HookCodex("perm", "PermissionRequest", heure: "20:48:00.000")) == Etat.Travaille,
            "Codex : ancien hook n'écrase pas un journal plus récent");
        Vrai(Fusion([debutHook], HookCodex("done", "Stop", tour: "autre-tour")) == Etat.Travaille,
            "Codex : fin d'un autre tour ignorée");
        Vrai(Fusion([debutHook, fin, repriseHook], HookCodex("done", "Stop", heure: "20:59:00.000")) == Etat.Travaille,
            "Codex : hook tardif du tour précédent ne finit pas le nouveau tour");
        Vrai(Fusion([debutHook], HookCodex("done", "Stop").Replace("\"main\"", "\"subagent\"")) == Etat.Travaille,
            "Codex : enfant ne finit pas la conversation parent");
        Vrai(Fusion([debutHook], HookCodex("done", "Stop").Replace("\"schema\":1", "\"schema\":0")) == Etat.Travaille,
            "Codex : ancien schéma sans preuve d'origine ignoré");
        Vrai(Fusion([Debut], HookCodex("perm", "PermissionRequest")) == Etat.Travaille,
            "Codex : journal sans identité de tour conserve son comportement historique");
        Vrai(Fusion([debutHook], "{broken") == Etat.Travaille && Fusion([debutHook], "{}") == Etat.Travaille,
            "Codex : fichier incomplet ou illisible garde le secours journal");
        Vrai(Fusion([debutHook], HookCodex("done", "UnknownEvent")) == Etat.Travaille,
            "Codex : événement inconnu ignoré");
        Vrai(Fusion([debutHook], HookCodex("unknown", "Stop")) == Etat.Travaille,
            "Codex : état inconnu ignoré");
        Vrai(Fusion([debutHook], HookCodex("done", "Stop", heure: "21:01:00.000")) == Etat.Travaille,
            "Codex : horodatage futur ne crée pas une fausse fin");
        Vrai(Fusion([debutHook], HookCodex("perm", "PermissionRequest"), quand: InstantJournal.AddHours(13)) == Etat.Travaille,
            "Codex : hook expiré ne remplace pas le lecteur du journal");
        Vrai(Fusion([debutHook], HookCodex("done", "Stop")) == Etat.Fini,
            "Codex : Stop parent accélère la fin avant task_complete");
        Vrai(Fusion([debutHook, question], HookCodex("done", "Stop")) == Etat.Attend,
            "Codex : Stop hook garde une carte async sans réponse");
        Vrai(Fusion([debutHook, question], HookCodex("done", "Stop"), relais: true) == Etat.Fini,
            "Codex : carte async du relais ne sollicite pas l'utilisateur");
        Vrai(Fusion([debutHook, fin], HookCodex("run", "PostToolUse", heure: "20:52:00.000")) == Etat.Fini,
            "Codex : callback tardif ne ressuscite pas un tour terminé au journal");
        Vrai(Fusion([debutHook, question, interruption], HookCodex("done", "Stop", heure: "20:53:00.000")) == Etat.Repos,
            "Codex : annulation réelle du journal prime sur un Stop tardif");
        Vrai(Fusion([debutHook, question], HookCodex("repos", "Interrupt")) == Etat.Repos,
            "Codex : interruption immédiate ne réaffiche pas une ancienne carte");
        Vrai(Fusion([debutHook, question, fin, repriseHook], HookCodex("done", "Stop", tour: "tour-b", heure: "20:59:00.000")) == Etat.Fini,
            "Codex : le nouveau tour ne reprend pas les questions du précédent");
    }

    [Fact]
    public void Delais_robustes() => Robustesse.Delais(Vrai);

    [Fact]
    public void Lecteurs_robustes_aux_entrees_abimees() =>
        Robustesse.Tout(Vrai, [Debut, Question, Fin, Reponse, Multi(), ReponseA(Item(0) + "," + Item(1)), Ordinaire, Citee], QuotaTests.Page);
}

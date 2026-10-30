using System.Text.Json;
using Loupedeck.TrinitePlugin;
using static NexusKeypad.Tests.Commun;

namespace NexusKeypad.Tests;

// Contract of the pad's snapshot (pure: no engine built, no interface read) and of a gesture's result.
public sealed class SnapshotTests
{
    [Fact]
    public void Snapshot_date_sans_titre_ni_detail()
    {
        var maintenant = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
        var secret = "must-not-export-this-title-or-detail";
        var cache = new Instantane(
            [[new(0, "local_session", secret, Etat.Attend, maintenant.AddSeconds(-2), maintenant, false)],
             [new(1, "codex-session", secret, Etat.Travaille, maintenant.AddSeconds(-1), maintenant, true, true)], []],
            [1, 1, 0], [new(.34, .71), new(null, .4), null], [true, true, false], false, null, 1)
        {
            Dictee = true, DicteeIa = 0,
            Connexions = [new(EtatConnexion.Pret, secret) { PrepareLe = maintenant.AddMinutes(-1), PremierEvenementObserve = false }, null, new(EtatConnexion.Activer, secret)],
        };
        var brut = Moteur.SnapshotJson(cache, maintenant.AddMilliseconds(-400), maintenant);
        using var doc = JsonDocument.Parse(brut);
        var root = doc.RootElement;
        Vrai(root.GetProperty("schema").GetInt32() == 1 && root.GetProperty("source").GetString() == "cached-pad-snapshot", "Version et origine du snapshot.");
        Vrai(root.GetProperty("available").GetBoolean(), "Le cache lu est disponible.");
        Vrai(root.GetProperty("sampled_at").GetString().StartsWith("2026-10-29T11:59:59.600"), "Date de la lecture conservée.");
        Vrai(!brut.Contains(secret) && !brut.Contains("Titre") && !brut.Contains("Detail"), "Aucun titre, détail libre ou texte de conversation.");
        var agents = root.GetProperty("agents");
        Vrai(agents.GetArrayLength() == 3 && agents[0].GetProperty("id").GetString() == "claude" && agents[2].GetProperty("id").GetString() == "antigravity", "Trois IA explicitement identifiées.");
        Vrai(agents[0].GetProperty("conversations")[0].GetProperty("id").GetString() == "local_session", "ID conservé sans titre.");
        Vrai(agents[0].GetProperty("state").GetString() == "Attend" && agents[2].GetProperty("state").GetString() == "Ferme", "États réellement présents dans le cache.");
        Vrai(agents[1].GetProperty("conversations")[0].GetProperty("relay").GetBoolean(), "Origine Relay conservée.");
        Vrai(agents[1].GetProperty("quota").GetProperty("five_hour_used").ValueKind == JsonValueKind.Null, "Quota inconnu reste inconnu.");
        Vrai(agents[0].GetProperty("quota").GetProperty("week_used").GetDouble() == .71, "Part du quota conservée.");
        Vrai(agents[1].GetProperty("connection").ValueKind == JsonValueKind.Null && agents[0].GetProperty("connection").GetProperty("state").GetString() == "Pret", "Connexion absente non remplacée par un faux succès.");
        Vrai(!agents[0].GetProperty("connection").GetProperty("first_activity_event_observed").GetBoolean(), "Disponibilité distincte de la preuve du premier callback.");
        Vrai(agents[2].GetProperty("connection").GetProperty("first_activity_event_observed").ValueKind == JsonValueKind.Null, "Preuve non vérifiée conservée comme inconnue.");
        Vrai(root.GetProperty("dictation").GetProperty("agent").GetString() == "claude" && root.GetProperty("foreground_agent").GetString() == "codex", "Dictée et premier plan du cache.");
        using var vide = JsonDocument.Parse(Moteur.SnapshotJson(Instantane.Vide, DateTime.MinValue, maintenant));
        Vrai(!vide.RootElement.GetProperty("available").GetBoolean() && vide.RootElement.GetProperty("sampled_at").ValueKind == JsonValueKind.Null, "Cache non encore lu explicitement indisponible.");
        using var choisi = JsonDocument.Parse(Moteur.SnapshotJson(cache with { Quotas = [null, null,
            new(.8, .3) { GroupeSelectionne = "gemini", Groupes = [new(false, .3, null, true, "gemini")] }] }, maintenant, maintenant));
        var quotaChoisi = choisi.RootElement.GetProperty("agents")[2].GetProperty("quota");
        Vrai(quotaChoisi.GetProperty("selected_group").GetString() == "gemini", "Le diagnostic nomme la famille réellement sélectionnée.");
        Vrai(quotaChoisi.GetProperty("groups")[0].GetProperty("group").GetString() == "gemini"
            && quotaChoisi.GetProperty("groups")[0].GetProperty("principal").GetBoolean(), "Le diagnostic conserve les identités et marqueurs des groupes.");
        using var enAttente = JsonDocument.Parse(Moteur.SnapshotJson(cache with { Connexions = [
            new(EtatConnexion.EnCours, secret) { CodeAttente = "claude_plugin_install_pending" }, null, null] }, maintenant, maintenant));
        var connexionEnAttente = enAttente.RootElement.GetProperty("agents")[0].GetProperty("connection");
        Vrai(connexionEnAttente.GetProperty("state").GetString() == "EnCours"
            && connexionEnAttente.GetProperty("pending_reason").GetString() == "claude_plugin_install_pending"
            && connexionEnAttente.GetProperty("prepared_at").ValueKind == JsonValueKind.Null,
            "Pending public plugin installation has a structured reason without a fabricated preparation date.");
        Vrai(!enAttente.RootElement.GetRawText().Contains(secret), "Pending reason never exports free-form details.");
    }

    [Fact]
    public void Resultat_d_un_geste_jamais_annonce_fait_sans_preuve()
    {
        Vrai(Moteur.ResultatGeste("✓", null) == "fait", "Effet vérifié annoncé comme fait.");
        Vrai(Moteur.ResultatGeste("~", "app not ready") == "inconnu : app not ready", "Résultat inconnu jamais annoncé comme fait.");
        Vrai(Moteur.ResultatGeste("✓", "draft not empty") == "échec : draft not empty", "Refus avec cause jamais annoncé comme fait.");
        Vrai(Moteur.ResultatGeste("✗", "InvalidOperationException").StartsWith("échec : "), "Exception conservée comme échec du pilote.");
    }
}

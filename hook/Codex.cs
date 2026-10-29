// État immédiat de Codex, complément des journaux : aucun contenu de prompt ni de réponse n'est conservé.
// La preuve du transcript est volontairement stricte : un format inconnu laisse les journaux faire foi.
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace NexusKeypad.Hook;

public static class Codex
{
    private static readonly HashSet<string> Evenements = ["SessionStart", "SessionEnd", "UserPromptSubmit", "PreToolUse",
        "PermissionRequest", "PostToolUse", "SubagentStart", "SubagentStop", "Stop", "Interrupt"];

    private static string Texte(JsonNode? valeur) => valeur is JsonValue v && v.TryGetValue<string>(out var texte) ? texte : "";
    private static double Nombre(JsonNode? valeur) => valeur is JsonValue v && v.TryGetValue<double>(out var nombre) ? nombre : 0;
    private static bool Vrai(JsonNode? valeur) => valeur is JsonValue v && v.TryGetValue<bool>(out var oui) && oui;
    private static bool Identifiant(string valeur) => valeur.Length is > 0 and <= 80
        && valeur.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    // Les hooks enfants réutilisent session_id du parent. Seule une en-tête de transcript identifiant vraiment le
    // parent permet de publier son état. La lecture est bornée et ne conserve aucun texte de conversation.
    public static bool ParentProuve(string session, string transcript)
    {
        try
        {
            // Les chemins locaux renvoyés par le moteur Windows peuvent être canonisés en \\?\C:\… .
            // Ce préfixe étendu n'est pas un partage UNC ; les vrais chemins réseau restent refusés.
            if (transcript.StartsWith(@"\\?\", StringComparison.Ordinal) && transcript.Length > 6
                && char.IsAsciiLetter(transcript[4]) && transcript[5] == ':' && transcript[6] == '\\')
            {
                transcript = transcript[4..];
            }
            if (!Identifiant(session) || !Path.IsPathFullyQualified(transcript) || transcript.StartsWith(@"\\", StringComparison.Ordinal)) { return false; }
            using var flux = new FileStream(transcript, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var lecteur = new StreamReader(flux, Encoding.UTF8, true);
            var texte = new StringBuilder();
            var morceau = new char[8192];
            while (texte.Length < 1024 * 1024)
            {
                var lus = lecteur.Read(morceau, 0, Math.Min(morceau.Length, (1024 * 1024) - texte.Length));
                if (lus == 0) { break; }
                var fin = Array.IndexOf(morceau, '\n', 0, lus);
                texte.Append(morceau, 0, fin < 0 ? lus : fin);
                if (fin >= 0) { break; }
            }
            var racine = JsonNode.Parse(texte.ToString());
            if (Texte(racine?["type"]) != "session_meta" || racine?["payload"] is not JsonObject meta
                || Texte(meta["id"]) != session || meta["source"] is JsonObject
                || Texte(meta["parent_thread_id"]).Length > 0 || Texte(meta["originator"]) == "codex_exec") { return false; }
            var source = Texte(meta["source"]);
            var genre = Texte(meta["thread_source"]);
            return source is "vscode" or "cli" && (genre is "" or "user");
        }
        catch (Exception) { return false; }
    }

    // Pas d'attente longue dans un hook synchrone. Un verrou de fichier partagé est libéré par Windows à la mort du
    // processus ; le fichier vide reste en place, donc aucune course de suppression/recréation n'est possible.
    private static FileStream? Prendre(string chemin)
    {
        var attente = Stopwatch.StartNew();
        do
        {
            try { return new FileStream(chemin, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { Thread.Sleep(10); }
        } while (attente.ElapsedMilliseconds < 162);
        return null;
    }

    private static JsonObject Objet(JsonObject parent, string cle)
    {
        if (parent[cle] is JsonObject objet) { return objet; }
        parent[cle] = objet = new JsonObject();
        return objet;
    }

    private static JsonArray Liste(JsonObject parent, string cle)
    {
        if (parent[cle] is JsonArray liste) { return liste; }
        parent[cle] = liste = new JsonArray();
        return liste;
    }

    private static bool Contient(JsonArray liste, string id) => liste.Any(x => Texte(x) == id);

    // PermissionRequest ne porte pas de tool_use_id. Le nom et l'empreinte des arguments permettent de rattacher
    // uniquement les appels déjà connus, sans enregistrer leurs arguments ni fabriquer un identifiant d'appel.
    private static string CleOutil(JsonObject charge) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        Texte(charge["tool_name"]) + "\0" + (charge["tool_input"] ?? new JsonObject()).ToJsonString())));

    private static string EtatActif(JsonObject etat)
    {
        var outils = Objet(etat, "tools");
        return Vrai(etat["unmatched_permission"]) || outils.Any(x => Texte(x.Value?["state"]) == "perm") ? "perm"
            : outils.Any(x => Texte(x.Value?["state"]) == "wait") ? "wait" : "run";
    }

    private static string EtatCourant(JsonObject etat) => Texte(etat["phase"]) == "stopped" && Vrai(etat["stop_seen"])
        && Vrai(etat["prompt_seen"]) && Vrai(etat["tracking_complete"])
        && Objet(etat, "tools").Count == 0 && Objet(etat, "children").Count == 0
            ? Vrai(etat["stop_question"]) ? "wait" : "done" : EtatActif(etat);

    public static bool Noter(string evenement, JsonObject charge, double debut, string? racine = null)
    {
        var publie = Publier(evenement, charge, debut, racine);
        // Accusé passif d'un véritable appel reçu. Il distingue un hook absent d'un événement reçu mais ignoré,
        // sans publier un état de conversation supposé et sans conserver prompt, arguments ou réponse.
        try
        {
            var session = Texte(charge["session_id"]);
            if (!Identifiant(session) || !Evenements.Contains(evenement)) { return publie; }
            var dossier = Path.Combine(racine ?? Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nexus", "trinite"), "diagnostics", "codex");
            Directory.CreateDirectory(dossier);
            var transcript = Texte(charge["transcript_path"]);
            var diagnostic = new JsonObject { ["schema"] = 1, ["event"] = evenement, ["at"] = debut,
                ["published"] = publie, ["transcript_provided"] = transcript.Length > 0,
                ["extended_local_path"] = transcript.StartsWith(@"\\?\", StringComparison.Ordinal),
                ["reason"] = publie ? "state_written" : transcript.Length == 0 ? "missing_transcript"
                    : ParentProuve(session, transcript) ? "state_not_published" : "unproven_main_session" };
            var fichier = Path.Combine(dossier, session + ".json");
            var temporaire = fichier + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporaire, diagnostic.ToJsonString(), new UTF8Encoding(false)); File.Move(temporaire, fichier, overwrite: true); }
            finally { if (File.Exists(temporaire)) { File.Delete(temporaire); } }
        }
        catch (Exception) { } // le diagnostic ne change jamais le résultat de l'observation
        return publie;
    }

    private static bool Publier(string evenement, JsonObject charge, double debut, string? racine)
    {
        try
        {
            var session = Texte(charge["session_id"]);
            var tour = Texte(charge["turn_id"]);
            if (!Evenements.Contains(evenement) || (Texte(charge["hook_event_name"]) is { Length: > 0 } fourni && fourni != evenement)
                || !ParentProuve(session, Texte(charge["transcript_path"]))
                || (evenement is not ("SessionStart" or "SessionEnd") && !Identifiant(tour))) { return false; }
            var dossier = Path.Combine(racine ?? Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nexus", "trinite"), "codex");
            Directory.CreateDirectory(dossier);
            var fichier = Path.Combine(dossier, session + ".json");
            using var verrou = Prendre(Path.Combine(dossier, session + ".lock"));
            if (verrou is null) { return false; }
            JsonObject etat;
            try { etat = JsonNode.Parse(File.ReadAllText(fichier, Encoding.UTF8)) as JsonObject ?? []; }
            catch (Exception) { etat = []; }
            var ancienTour = Texte(etat["turn_id"]);
            var retraites = Liste(etat, "retired_turns");
            if (evenement == "UserPromptSubmit")
            {
                if (Contient(retraites, tour) || debut < Nombre(etat["at"])) { return false; }
                if (ancienTour == tour) { return false; } // un doublon n'efface pas les appels déjà actifs
                if (ancienTour.Length > 0) { retraites.Add((JsonNode)JsonValue.Create(ancienTour)!); }
                etat = new JsonObject { ["retired_turns"] = retraites.DeepClone(), ["turn_id"] = tour,
                    ["phase"] = "active", ["prompt_seen"] = true, ["tracking_complete"] = true, ["state"] = "run" };
            }
            else if (evenement is "SessionStart" or "SessionEnd")
            {
                if (debut < Nombre(etat["at"])) { return false; }
                if (evenement == "SessionEnd") { etat["phase"] = "ended"; etat["state"] = "end"; }
                else if (etat["state"] is null) { etat["state"] = "repos"; }
            }
            else
            {
                if (ancienTour.Length == 0) { etat["turn_id"] = tour; etat["phase"] = "active"; }
                else if (ancienTour != tour) { return false; }
                if (Texte(etat["phase"]) is "interrupted" or "ended") { return false; }
                var outils = Objet(etat, "tools");
                var termines = Liste(etat, "finished_tools");
                var enfants = Objet(etat, "children");
                var appel = Texte(charge["tool_use_id"]);
                var enfant = Texte(charge["agent_id"]);
                switch (evenement)
                {
                    case "PreToolUse":
                    {
                        if (Texte(etat["phase"]) == "stopped") { return false; }
                        if (!Identifiant(appel)) { etat["tracking_complete"] = false; etat["state"] = EtatActif(etat); break; }
                        if (Contient(termines, appel)) { return false; }
                        if (Nombre(outils[appel]?["at"]) > debut) { return false; }
                        var outil = Texte(charge["tool_name"]);
                        var nom = outil.Split('.').Last().Split("__", StringSplitOptions.None).Last();
                        outils[appel] = new JsonObject { ["state"] = nom == "request_user_input" ? "wait" : "run",
                            ["at"] = debut, ["key"] = CleOutil(charge) };
                        etat["state"] = EtatActif(etat);
                        break;
                    }
                    case "PermissionRequest":
                    {
                        if (Texte(etat["phase"]) == "stopped" || debut < Nombre(etat["at"])) { return false; }
                        var cle = CleOutil(charge);
                        var correspondances = outils.Where(x => Texte(x.Value?["key"]) == cle).ToList();
                        if (correspondances.Count == 0)
                        {
                            etat["unmatched_permission"] = true;
                            etat["tracking_complete"] = false; // hooks partiellement actifs ou format inconnu : secours journaux
                        }
                        foreach (var correspondance in correspondances)
                        {
                            correspondance.Value!["state"] = "perm";
                            correspondance.Value["at"] = debut;
                        }
                        etat["state"] = "perm";
                        break;
                    }
                    case "PostToolUse":
                        if (Texte(etat["phase"]) == "stopped" && (!Identifiant(appel) || !outils.ContainsKey(appel))) { return false; }
                        if (!Identifiant(appel)) { etat["tracking_complete"] = false; etat["state"] = EtatActif(etat); break; }
                        if (Contient(termines, appel)) { return false; }
                        outils.Remove(appel);
                        if (!Contient(termines, appel)) { termines.Add((JsonNode)JsonValue.Create(appel)!); }
                        etat["state"] = EtatCourant(etat);
                        break;
                    case "SubagentStart":
                        if (Texte(etat["phase"]) == "stopped") { return false; }
                        if (!Identifiant(enfant)) { etat["tracking_complete"] = false; etat["state"] = EtatActif(etat); break; }
                        if (!Contient(Liste(etat, "finished_children"), enfant)) { enfants[enfant] = true; }
                        etat["state"] = EtatActif(etat);
                        break;
                    case "SubagentStop":
                        if (Texte(etat["phase"]) == "stopped" && (!Identifiant(enfant) || !enfants.ContainsKey(enfant))) { return false; }
                        if (!Identifiant(enfant)) { etat["tracking_complete"] = false; etat["state"] = EtatActif(etat); break; }
                        var finis = Liste(etat, "finished_children");
                        if (Contient(finis, enfant)) { return false; }
                        enfants.Remove(enfant);
                        if (!Contient(finis, enfant)) { finis.Add((JsonNode)JsonValue.Create(enfant)!); }
                        etat["state"] = EtatCourant(etat); // seul un Stop parent déjà attesté peut terminer le tour
                        break;
                    case "Interrupt":
                        if (debut < Nombre(etat["at"])) { return false; }
                        etat["phase"] = "interrupted";
                        etat["state"] = "repos";
                        break;
                    case "Stop":
                        if (debut < Nombre(etat["at"])) { return false; }
                        etat["phase"] = "stopped";
                        etat["unmatched_permission"] = false;
                        var question = Texte(charge["last_assistant_message"]).TrimEnd('*', '_', ' ', ')', '»', '"', '\'', '\n', '\r').EndsWith('?');
                        etat["stop_seen"] = true;
                        etat["stop_question"] = question;
                        etat["state"] = EtatCourant(etat);
                        break;
                }
            }
            etat["schema"] = 1;
            etat["scope"] = "main";
            etat["at"] = Math.Max(debut, Nombre(etat["at"]));
            etat["event"] = evenement;
            var temporaire = Path.Combine(dossier, session + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(temporaire, etat.ToJsonString(), new UTF8Encoding(false));
                File.Move(temporaire, fichier, overwrite: true);
            }
            finally { if (File.Exists(temporaire)) { File.Delete(temporaire); } }
            return true;
        }
        catch (Exception) { return false; } // un hook d'observation ne commande jamais Codex
    }
}

// Note l'état d'une IA de la Trinité pour le pad (appelé par les hooks de Claude et d'Antigravity), sans Python.
//
// Usage : nexuskeypad-hook <agent> <evenement>   (le JSON du hook arrive sur stdin)
// Règles tenues cas par cas par tests/Hook (ExecutableTests) (relevées sur etat.py, la version Python d'origine) : écrit
// %USERPROFILE%\.nexus\trinite\<agent>\<session>.json = {"state", "at", "event"} sous un verrou par session, sans jamais
// remplacer un état noté par un événement parti après lui (la liste des sous-agents Claude en arrière-plan, « agents »,
// suit pourtant chaque événement) ; rend « {} » sur stdout (« {"decision": "allow"} » au
// PreToolUse d'Antigravity, qui refuse l'outil sans décision) ; ne lève jamais.
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NexusKeypad.Hook;

public static class Program
{
    private static readonly string Racine = Path.Combine(
        Environment.GetEnvironmentVariable("USERPROFILE") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".nexus", "trinite");

    // Outils qui posent une question à l'utilisateur : le tour s'arrête sur lui (Claude, Antigravity).
    private static readonly HashSet<string> Questions = ["AskUserQuestion", "ask_question"];

    // Boîte de fin de tour de l'appli : une question si « ask » est rempli.
    private const string Choix = "mcp__ccd_turn__wrap_up";

    public static int Main(string[] args)
    {
        try
        {
            if (args is ["connecteur", ..])
            {
                Loupedeck.TrinitePlugin.Connecteurs.Initialiser(Environment.ProcessPath!);
                return Loupedeck.TrinitePlugin.Connecteurs.Executer(args);
            }

            // Outils de get.ps1 (installation sans Python) : retrait des états en direct, pose ou retrait des touches.
            switch (args)
            {
                case ["--remove"]:
                    // hooks du pad et mod du quota, sans question (le retrait a été demandé)
                    Loupedeck.TrinitePlugin.Connecteurs.Initialiser(Environment.ProcessPath!);
                    return Loupedeck.TrinitePlugin.Connecteurs.RetirerInstallations()
                        && Loupedeck.TrinitePlugin.EtatsEnDirect.Appliquer(null, pose: false) ? 0 : 1;
                case ["keys", var profil, var plugin]:
                    return Touches.Executer(profil, plugin, retirer: false);
                case ["keys", var profil, var plugin, "--remove"]:
                    return Touches.Executer(profil, plugin, retirer: true);
                case ["swap", var nouveau, var cible, var ancien, var etat, var version]:
                    // échange des dossiers d'une mise à jour, lancé détaché par le plugin
                    return Echange.Executer(nouveau, cible, ancien, etat, version, Echange.LogiPour(cible));
                case ["--play-note", var note] when note is "claude" or "antigravity":
                    // Processus bref, sans fenêtre : le hook appelant a déjà rendu sa réponse JSON.
                    Son.Jouer(note);
                    return 0;
            }

            var debut = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            if (args.Length < 2)
            {
                Console.Out.Write("{}\n");
                return 0;
            }

            var (agent, evenement) = (args[0], args[1]);
            JsonObject charge;
            try
            {
                using var entree = Console.OpenStandardInput();
                using var lecteur = new StreamReader(entree, new UTF8Encoding(false));
                var texte = lecteur.ReadToEnd();
                charge = JsonNode.Parse(string.IsNullOrWhiteSpace(texte) ? "{}" : texte) as JsonObject ?? [];
            }
            catch (Exception)
            {
                charge = [];
            }

            // Antigravity : son PreToolUse exige une décision (sans « allow », l'outil est REFUSÉ). Ailleurs, « {} ».
            Console.Out.Write(agent == "antigravity" && evenement == "PreToolUse" ? "{\"decision\": \"allow\"}\n" : "{}\n");
            Console.Out.Flush();
            if (!Loupedeck.TrinitePlugin.Connecteurs.EvenementsActifs(agent))
            {
                return 0;
            }

            if (agent == "codex")
            {
                Codex.Noter(evenement, charge, debut, Racine);
                return 0;
            }

            if (Noter(agent, evenement, charge, debut) && Son.AppartientAuPlugin(agent))
            {
                Son.Demander(agent);
            }
        }
        catch (Exception)
        {
            // un hook ne doit jamais gêner l'agent
        }

        return 0;
    }

    // Vrai au sens de Python : ni absent, ni vide, ni zéro, ni faux.
    private static bool Vrai(JsonNode? n) => n switch
    {
        null => false,
        JsonObject o => o.Count > 0,
        JsonArray a => a.Count > 0,
        JsonValue v when v.TryGetValue<bool>(out var b) => b,
        JsonValue v when v.TryGetValue<string>(out var s) => s.Length > 0,
        JsonValue v when v.TryGetValue<double>(out var d) => d != 0,
        _ => true,
    };

    // « a or b or … » de Python, en texte (« str() » d'une valeur non textuelle).
    private static string Texte(params JsonNode?[] noeuds)
    {
        foreach (var n in noeuds)
        {
            if (Vrai(n))
            {
                return n is JsonValue v && v.TryGetValue<string>(out var s) ? s : n!.ToJsonString();
            }
        }

        return "";
    }

    private static JsonObject Objet(JsonNode? n) => n as JsonObject ?? [];

    private static string Outil(JsonObject charge) => Texte(charge["tool_name"], Objet(charge["toolCall"])["name"]);

    // Texte du dernier message de l'assistant dans le journal du tour (fin du fichier seulement). Claude : lignes
    // {"type": "assistant", "message": {...}} ; Antigravity : {"type": "PLANNER_RESPONSE", "content": ...}, son
    // transcriptPath pouvant être un dossier de morceaux (le plus récent fait foi).
    public static string DerniereReponse(string journal)
    {
        if (string.IsNullOrEmpty(journal))
        {
            return "";
        }

        var chemin = journal;
        if (Directory.Exists(chemin))
        {
            chemin = new DirectoryInfo(chemin).EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
                .OrderBy(f => f.LastWriteTimeUtc).LastOrDefault()?.FullName;
            if (chemin is null)
            {
                return "";
            }
        }

        string[] lignes;
        try
        {
            using var f = new FileStream(chemin, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            f.Seek(Math.Max(0, f.Length - 256 * 1024), SeekOrigin.Begin);
            using var lecteur = new StreamReader(f, new UTF8Encoding(false, false));
            lignes = lecteur.ReadToEnd().Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        }
        catch (Exception)
        {
            return "";
        }

        for (var i = lignes.Length - 1; i >= 0; i--)
        {
            JsonObject entree;
            try
            {
                if (JsonNode.Parse(lignes[i]) is not JsonObject o)
                {
                    continue;
                }

                entree = o;
            }
            catch (Exception)
            {
                continue;
            }

            var type = Texte(entree["type"]);
            if (type == "PLANNER_RESPONSE" && Vrai(entree["content"]))
            {
                return Texte(entree["content"]).Trim();
            }

            var message = Objet(entree["message"]);
            if (type != "assistant" || Texte(message["role"]) != "assistant")
            {
                continue;
            }

            var textes = (message["content"] as JsonArray ?? [])
                .OfType<JsonObject>()
                .Where(b => Texte(b["type"]) == "text")
                .Select(b => Texte(b["text"]))
                .ToList();
            if (textes.Count > 0)
            {
                return textes[^1].Trim();
            }
        }

        return "";
    }

    // Session Claude lancée par une autre session (sa fiche porte « spawnedFrom ») : elle travaille pour Claude, qui rend
    // compte à l'utilisateur ; ses fins de tour ne le préviennent pas.
    private static bool LanceeParUneSession(string session)
    {
        if (string.IsNullOrEmpty(session))
        {
            return false;
        }

        var racine = new DirectoryInfo(Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "", "Claude-Data"));
        if (!racine.Exists)
        {
            return false;
        }

        foreach (var compte in racine.EnumerateDirectories())
        {
            var sessions = new DirectoryInfo(Path.Combine(compte.FullName, "claude-code-sessions"));
            if (!sessions.Exists)
            {
                continue;
            }

            foreach (var fiche in sessions.EnumerateFiles("local_*.json", SearchOption.AllDirectories))
            {
                string texte;
                try
                {
                    texte = File.ReadAllText(fiche.FullName, Encoding.UTF8);
                }
                catch (Exception)
                {
                    continue;
                }

                if (!texte.Contains(session, StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    if (JsonNode.Parse(texte) is JsonObject donnees && Texte(donnees["cliSessionId"]) == session)
                    {
                        return Vrai(donnees["spawnedFrom"]);
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        return false;
    }

    public static string? EtatPour(string evenement, JsonObject charge, JsonObject precedent)
    {
        switch (evenement)
        {
            case "Notification":
            {
                var genre = Texte(charge["notification_type"]);
                var message = Texte(charge["message"]).ToLowerInvariant();
                return genre == "permission_prompt" || message.Contains("permission", StringComparison.Ordinal) ? "perm" : null;
            }
            case "PreToolUse":
            {
                var outil = Outil(charge);
                if (Questions.Contains(outil))
                {
                    return "wait";
                }

                return outil == Choix && Vrai(Objet(charge["tool_input"])["ask"]) ? "ask" : "run";
            }
            case "PostToolUse" or "PostToolUseFailure":
                // la boîte de fin pose sa question : « ask » doit tenir jusqu'au Stop
                return Outil(charge) == Choix && Texte(precedent["state"]) == "ask" ? null : "run";
            case "UserPromptSubmit" or "PreInvocation" or "PostInvocation":
                return "run";
            case "StopFailure":
            {
                var erreur = Texte(charge["error"], charge["error_type"]);
                return erreur is "rate_limit" or "billing_error" ? "limit" : "error";
            }
            case "Stop":
            {
                if (charge["fullyIdle"] is JsonValue idle && idle.TryGetValue<bool>(out var b) && !b)
                {
                    return "run"; // Antigravity : un sous-agent travaille encore
                }

                if (Texte(charge["terminationReason"]) == "USER_CANCELED")
                {
                    return "repos"; // une annulation n'est pas une tâche terminée
                }

                if (Texte(precedent["event"]) == "Stop" && Texte(precedent["state"]) == "repos")
                {
                    return "repos"; // un doublon ne transforme pas une annulation en réussite
                }

                if (LanceeParUneSession(Texte(charge["session_id"])))
                {
                    return "repos";
                }

                if (Texte(precedent["state"]) is "ask" or "wait")
                {
                    return "wait";
                }

                var reponse = DerniereReponse(Texte(charge["transcript_path"], charge["transcriptPath"]));
                return reponse.TrimEnd('*', '_', ' ', ')', '»', '"', '\'').EndsWith('?') ? "wait" : "done";
            }
            default:
                return null;
        }
    }

    private static double Nombre(JsonNode? n) =>
        n is JsonValue v && v.TryGetValue<double>(out var d) ? d : 0;

    // Sous-agents Claude lancés en arrière-plan (outil Agent, « Task » de son ancien nom, run_in_background ou lancement
    // asynchrone dans sa réponse) : Claude peut finir son tour pendant qu'ils travaillent. Tenus par session dans
    // « agents » (id → départ), recopiés à chaque écriture. Le PreToolUse ne connaît que l'appel (« tool:<tool_use_id> »),
    // remplacé au PostToolUse par l'agentId que SubagentStop rend ensuite (agent_id). Un agent jamais vu finir s'efface
    // après 2 h, l'âge où le pad ne croit plus un « run ».
    private static readonly HashSet<string> OutilsAgent = ["Agent", "Task"];

    private const double DureeAgent = 2 * 3600;

    private static string IdAgent(string id) => id.StartsWith("agent-", StringComparison.Ordinal) ? id["agent-".Length..] : id;

    // Textes d'une réponse d'outil (objet, tableau de blocs ou texte), mis bout à bout.
    private static void Textes(JsonNode? n, StringBuilder sortie)
    {
        switch (n)
        {
            case JsonObject o:
                foreach (var (_, v) in o)
                {
                    Textes(v, sortie);
                }

                break;
            case JsonArray a:
                foreach (var v in a)
                {
                    Textes(v, sortie);
                }

                break;
            case JsonValue v when v.TryGetValue<string>(out var s):
                sortie.Append(s).Append('\n');
                break;
        }
    }

    // Réponse de l'outil Agent : lancement en arrière-plan ? et son agentId. Forme relevée dans les journaux de Claude
    // Code : {"isAsync": true, "status": "async_launched", "agentId": "…"} ; en texte : « Async agent launched
    // successfully. … agentId: … ».
    private static (bool Lance, string? Id) Lancement(JsonNode? reponse)
    {
        var o = Objet(reponse);
        var lance = Texte(o["status"]) == "async_launched" || (o["isAsync"] is JsonValue a && a.TryGetValue<bool>(out var b) && b);
        var id = Texte(o["agentId"]);
        var texte = new StringBuilder();
        Textes(reponse, texte);
        var t = texte.ToString();
        lance = lance || t.Contains("Async agent launched", StringComparison.OrdinalIgnoreCase);
        if (id.Length == 0 && t.IndexOf("agentId:", StringComparison.Ordinal) is var i and >= 0)
        {
            id = new string(t[(i + "agentId:".Length)..].TrimStart().TakeWhile(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        }

        return (lance, id.Length > 0 ? IdAgent(id) : null);
    }

    // Changement de la liste des sous-agents en arrière-plan qu'apporte un événement Claude : clés retirées, clé ajoutée.
    public static (string[] Retires, string? Ajoute) AgentsArrierePlan(string evenement, JsonObject charge)
    {
        if (evenement == "SubagentStop")
        {
            var id = Texte(charge["agent_id"]);
            if (id.Length == 0)
            {
                id = Path.GetFileNameWithoutExtension(Texte(charge["agent_transcript_path"]));
                id = id.StartsWith("agent-", StringComparison.Ordinal) ? id : "";
            }

            return (id.Length > 0 ? [IdAgent(id)] : [], null);
        }

        if (evenement is not ("PreToolUse" or "PostToolUse" or "PostToolUseFailure") || !OutilsAgent.Contains(Outil(charge)))
        {
            return ([], null);
        }

        var appel = Texte(charge["tool_use_id"]);
        string[] cleAppel = appel.Length > 0 ? ["tool:" + appel] : [];
        var demande = Objet(charge["tool_input"])["run_in_background"] is JsonValue v && v.TryGetValue<bool>(out var fond) && fond;
        switch (evenement)
        {
            case "PreToolUse":
                return ([], demande && cleAppel.Length > 0 ? cleAppel[0] : null);
            case "PostToolUseFailure":
                return (cleAppel, null);
            default:
            {
                var (lance, id) = Lancement(charge["tool_response"]);
                if (!lance && !demande)
                {
                    return (cleAppel, null); // sous-agent au premier plan : il a fini avec son outil
                }

                return id is null ? ([], cleAppel.Length > 0 ? cleAppel[0] : null) : (cleAppel, id);
            }
        }
    }

    // Écrit l'état sous son verrou ; ignore un événement parti avant celui déjà noté. Renvoie true uniquement
    // lorsqu'un nouveau Stop terminal Claude/AGY justifie sa note. La lecture/écriture seule ne joue aucun son.
    public static bool Noter(string agent, string evenement, JsonObject charge, double debut, string? racine = null)
    {
        var dossier = Path.Combine(racine ?? Racine, agent);
        Directory.CreateDirectory(dossier);
        var brut = Texte(charge["session_id"], charge["conversationId"], charge["cascadeId"]);
        var session = new string((brut.Length > 0 ? brut : "defaut").Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        session = session.Length > 80 ? session[..80] : session;
        if (session.Length == 0)
        {
            session = "defaut";
        }

        var fichier = Path.Combine(dossier, session + ".json");
        var verrou = Path.Combine(dossier, session + ".lock");
        var pris = false;
        for (var i = 0; i < 1000 && !pris; i++) // 20 s au plus ; un verrou de plus de 30 s est celui d'un appel mort
        {
            try
            {
                new FileStream(verrou, FileMode.CreateNew, FileAccess.Write).Dispose();
                pris = true;
            }
            catch (IOException)
            {
                try
                {
                    if (File.Exists(verrou) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(verrou)).TotalSeconds > 30)
                    {
                        File.Delete(verrou);
                    }
                }
                catch (Exception)
                {
                }

                Thread.Sleep(20);
            }
        }

        if (!pris)
        {
            return false;
        }

        try
        {
            JsonObject precedent;
            try
            {
                precedent = JsonNode.Parse(File.ReadAllText(fichier, Encoding.UTF8)) as JsonObject ?? [];
            }
            catch (Exception)
            {
                precedent = [];
            }

            // Sous-agents en arrière-plan : la liste suit chaque événement, même parti avant l'état noté (elle ne fait
            // qu'ajouter ou retirer des agents) ; l'état, lui, n'est jamais remplacé par un événement plus ancien.
            var anciens = Objet(precedent["agents"]);
            var agents = new JsonObject();
            foreach (var (cle, quand) in anciens)
            {
                if (debut - Nombre(quand) < DureeAgent)
                {
                    agents[cle] = Nombre(quand);
                }
            }

            var (retires, ajoute) = agent == "claude" ? AgentsArrierePlan(evenement, charge) : ([], null);
            foreach (var cle in retires)
            {
                agents.Remove(cle);
            }

            if (ajoute is not null && !agents.ContainsKey(ajoute))
            {
                agents[ajoute] = debut;
            }

            if (evenement == "SessionEnd")
            {
                agents.Clear();
            }

            var attendait = Vrai(precedent["awaiting"]);
            var listeChangee = !JsonNode.DeepEquals(agents, anciens);
            string? etat;
            var attend = attendait;
            JsonObject contenu;
            if (Nombre(precedent["at"]) > debut)
            {
                if (!listeChangee || precedent["state"] is null)
                {
                    return false; // un événement parti après celui-ci a déjà écrit
                }

                etat = null;
                contenu = new JsonObject { ["state"] = precedent["state"]!.DeepClone(), ["at"] = precedent["at"]!.DeepClone(), ["event"] = precedent["event"]?.DeepClone() };
                if (attendait && agents.Count == 0)
                {
                    // Seule exception : l'état plus récent n'était « run » que pour ces agents (le tour de Claude est fini,
                    // un nouveau tour aurait effacé « awaiting ») ; sans eux, la fin attendue est atteinte.
                    contenu["state"] = "done";
                    contenu["event"] = evenement;
                    attend = false;
                }
            }
            else
            {
                etat = evenement == "SessionEnd" ? "end" : EtatPour(evenement, charge, precedent);
                if (evenement is "Stop" or "StopFailure" or "UserPromptSubmit" or "SessionEnd")
                {
                    attend = false; // le tour (re)part ou se termine : la fin attendue est décidée ici
                }

                if (agent == "claude" && evenement == "Stop" && etat == "done" && agents.Count > 0)
                {
                    etat = "run"; // fin de tour, mais des sous-agents en arrière-plan travaillent encore pour Claude
                    attend = true;
                }
                else if (evenement == "SubagentStop" && attendait && agents.Count == 0)
                {
                    // Le dernier a fini : Claude reprend aussitôt pour lire son rapport ; la fin de ce tour-là sonnera.
                    etat = "done";
                    attend = false;
                }

                if (etat is null && (!listeChangee && attend == attendait || precedent["state"] is null))
                {
                    return false;
                }

                contenu = etat is null
                    ? new JsonObject { ["state"] = precedent["state"]!.DeepClone(), ["at"] = precedent["at"]?.DeepClone(), ["event"] = precedent["event"]?.DeepClone() }
                    : new JsonObject { ["state"] = etat, ["at"] = debut, ["event"] = evenement };
            }

            if (agents.Count > 0)
            {
                contenu["agents"] = agents;
            }

            if (attend)
            {
                contenu["awaiting"] = true;
            }

            var temporaire = Path.Combine(dossier, $"{session}.{Environment.ProcessId}.tmp");
            File.WriteAllText(temporaire, contenu.ToJsonString(), new UTF8Encoding(false));
            File.Move(temporaire, fichier, overwrite: true);
            // Le son suit la même activation et le même verrou que l'état, une seule fois par fin de tour.
            // Un PreInvocation/outil du tour suivant réarme naturellement le son. Ne jamais annoncer une annulation
            // ou un arrêt laissant des sous-agents travailler ; une question terminale garde sa note d'attention.
            return agent is ("claude" or "antigravity") && evenement == "Stop" && brut.Length > 0 && etat is ("done" or "wait")
                && Texte(charge["terminationReason"]) != "USER_CANCELED"
                && !(Texte(precedent["event"]) == "Stop" && Texte(precedent["state"]) is ("done" or "wait"));
        }
        finally
        {
            try
            {
                File.Delete(verrou);
            }
            catch (Exception)
            {
            }
        }
    }
}

// Note l'état d'une IA de la Trinité pour le pad (appelé par les hooks de Claude et d'Antigravity), sans Python.
//
// Usage : nexuskeypad-hook <agent> <evenement>   (le JSON du hook arrive sur stdin)
// Règles tenues cas par cas par tests/test_hook.py (relevées sur etat.py, la version Python d'origine) : écrit
// %USERPROFILE%\.nexus\trinite\<agent>\<session>.json = {"state", "at", "event"} sous un verrou par session, sans jamais
// remplacer un état noté par un événement parti après lui ; rend « {} » sur stdout (« {"decision": "allow"} » au
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
            // Outils de get.ps1 (installation sans Python) : retrait des états en direct, pose ou retrait des touches.
            switch (args)
            {
                case ["--remove"]:
                    // hooks du pad et mod du quota, sans question (le retrait a été demandé)
                    return Loupedeck.TrinitePlugin.EtatsEnDirect.Appliquer(null, pose: false) ? 0 : 1;
                case ["keys", var profil, var plugin]:
                    return Touches.Executer(profil, plugin, retirer: false);
                case ["keys", var profil, var plugin, "--remove"]:
                    return Touches.Executer(profil, plugin, retirer: true);
                case ["swap", var nouveau, var cible, var ancien, var etat, var version]:
                    // échange des dossiers d'une mise à jour, lancé détaché par le plugin
                    return Echange.Executer(nouveau, cible, ancien, etat, version, Echange.LogiPour(cible));
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
            Noter(agent, evenement, charge, debut);
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

    // Écrit l'état de la session, sous son verrou ; un événement parti avant celui déjà noté est ignoré.
    public static void Noter(string agent, string evenement, JsonObject charge, double debut, string? racine = null)
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
            return;
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

            if (Nombre(precedent["at"]) > debut)
            {
                return; // un événement parti après celui-ci a déjà écrit
            }

            var etat = evenement == "SessionEnd" ? "end" : EtatPour(evenement, charge, precedent);
            if (etat is null)
            {
                return;
            }

            var temporaire = Path.Combine(dossier, $"{session}.{Environment.ProcessId}.tmp");
            var contenu = new JsonObject { ["state"] = etat, ["at"] = debut, ["event"] = evenement };
            File.WriteAllText(temporaire, contenu.ToJsonString(), new UTF8Encoding(false));
            File.Move(temporaire, fichier, overwrite: true);
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

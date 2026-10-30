using System.Diagnostics;
using System.Text.Json.Nodes;
using NexusKeypad.Hook;

namespace NexusKeypad.Tests;

// Codex events written by the hook (temporary profiles only): parent proof, tools, sub-agents, questions, interruptions,
// ordering, concurrency and the bounded lock.
public sealed class CodexTests : IDisposable
{
    private readonly String _racine = Directory.CreateTempSubdirectory("nexuskeypad-hooks-codex-").FullName;

    public void Dispose() => Directory.Delete(this._racine, recursive: true);

    private static void Vrai(Boolean condition, String nom) => Assert.True(condition, nom);

    private String Transcript(String nom, String id, String genre = "user", JsonNode? source = null)
    {
        var chemin = Path.Combine(this._racine, nom + ".jsonl");
        var meta = new JsonObject { ["type"] = "session_meta", ["payload"] = new JsonObject { ["id"] = id,
            ["thread_source"] = genre, ["source"] = source ?? JsonValue.Create("vscode"), ["originator"] = "Codex Desktop",
            ["base_instructions"] = new String('x', 24000) } };
        File.WriteAllText(chemin, meta.ToJsonString() + "\n");
        return chemin;
    }

    [Fact]
    public void Preuve_du_parent()
    {
        var transcript = this.Transcript("parent", "s1");
        var enfantTranscript = this.Transcript("enfant", "child", "subagent", new JsonObject { ["subagent"] = new JsonObject() });
        Vrai(Codex.ParentProuve("s1", transcript), "La preuve parent lit une en-tête réelle de plus de 8Ko.");
        Vrai(Codex.ParentProuve("s1", @"\\?\" + transcript), "Un chemin Windows local étendu ne devient pas un partage UNC.");
        Vrai(!Codex.ParentProuve("s1", enfantTranscript), "Un transcript enfant ne prouve pas son parent.");
        Vrai(!Codex.ParentProuve("child", enfantTranscript), "Même son propre id ne fait pas d'un enfant un parent.");
        Vrai(!Codex.ParentProuve("s1", this.Transcript("inconnu", "s1", "future", JsonValue.Create("future"))), "Un format d'origine inconnu laisse les journaux faire foi.");
        Vrai(!Codex.ParentProuve("../s1", transcript), "Un identifiant non sûr ne devient pas un chemin.");
        Vrai(!Codex.ParentProuve("s1", Path.Combine(this._racine, "absent")), "Un transcript absent ne fabrique aucune preuve.");
    }

    // One scenario: each turn builds on the state the previous one left.
    [Fact]
    public void Tours_outils_enfants_questions_interruptions_et_verrou()
    {
        var transcript = this.Transcript("parent", "s1");
        var enfantTranscript = this.Transcript("enfant", "child", "subagent", new JsonObject { ["subagent"] = new JsonObject() });
        var dossier = Path.Combine(this._racine, "etats");
        var sortie = Path.Combine(dossier, "codex", "s1.json");
        var horloge = 1000.0;
        JsonObject Charge(String tour = "t1", String? appel = null, String? enfant = null, String? outil = null) => new()
        {
            ["session_id"] = "s1", ["transcript_path"] = transcript, ["turn_id"] = tour,
            ["tool_use_id"] = appel, ["agent_id"] = enfant, ["tool_name"] = outil,
        };
        Boolean Noter(String e, JsonObject? charge = null, Double? date = null) => Codex.Noter(e, charge ?? Charge(), date ?? ++horloge, dossier);
        JsonObject Etat() => JsonNode.Parse(File.ReadAllText(sortie))!.AsObject();
        String Actuel() => Etat()["state"]!.GetValue<String>();

        Vrai(Noter("UserPromptSubmit") && Actuel() == "run", "Une nouvelle demande démarre le tour.");
        var etat = Etat();
        Vrai(etat["schema"]!.GetValue<Int32>() == 1 && etat["scope"]!.GetValue<String>() == "main"
            && etat["turn_id"]!.GetValue<String>() == "t1", "Le contrat de fusion contient origine, schéma et tour.");
        var avant = File.ReadAllText(sortie);
        var enfantCharge = Charge(appel: "child-tool");
        enfantCharge["transcript_path"] = enfantTranscript;
        Vrai(!Noter("PreToolUse", enfantCharge) && File.ReadAllText(sortie) == avant, "Un outil enfant ne remplace pas l'état parent.");
        Vrai(!Noter("Stop", enfantCharge) && File.ReadAllText(sortie) == avant, "Un Stop enfant ne termine pas le parent.");

        Noter("PreToolUse", Charge(appel: "a", outil: "Bash"), 1010);
        Noter("PreToolUse", Charge(appel: "b", outil: "Other"), 1009);
        Vrai(Etat()["tools"]!.AsObject().Count == 2, "Les outils parallèles plus anciens ne disparaissent pas au verrou.");
        var permission = Charge(outil: "Bash");
        permission.Remove("tool_use_id");
        Noter("PermissionRequest", permission, 1012);
        Vrai(Actuel() == "perm", "PermissionRequest officiel sans tool_use_id signale l'attente.");
        Noter("PreToolUse", Charge(appel: "a", outil: "Bash"), 1011);
        Vrai(Actuel() == "perm", "Un Pre ancien n'efface pas une permission plus récente.");
        Noter("PostToolUse", Charge(appel: "a"), 1013);
        Vrai(Actuel() == "run" && Etat()["tools"]!.AsObject().Count == 1, "La fin d'un outil ne ferme pas son voisin.");
        Noter("Stop", date: 1014);
        Vrai(Actuel() == "run", "Le Stop parent attend les appels encore actifs.");
        Noter("PostToolUse", Charge(appel: "b"), 1015);
        Vrai(Actuel() == "done", "Le dernier Post attendu applique un Stop parent déjà attesté.");
        Noter("Stop", date: 1016);
        Vrai(Actuel() == "done", "Un Stop parent sans appel restant termine le tour.");
        avant = File.ReadAllText(sortie);
        Vrai(!Noter("PostToolUse", Charge(appel: "a"), 1013) && !Noter("PostToolUse", Charge(appel: "unknown"), 1013)
            && File.ReadAllText(sortie) == avant, "Un doublon ou callback inconnu ne remet pas un tour fini en travail.");
        Vrai(!Noter("PostToolUse", Charge(), 1017) && !Noter("PreToolUse", Charge(), 1018)
            && !Noter("SubagentStop", Charge(), 1019) && File.ReadAllText(sortie) == avant,
            "Un callback tardif mal formé ne dégrade pas une fin prouvée.");
        Vrai(!File.ReadAllText(sortie).Contains("base_instructions"), "Aucun contenu de transcript n'est conservé.");

        Noter("UserPromptSubmit", Charge("t2"), 1020);
        Noter("PostToolUse", Charge("t2", "already-finished"), 1022);
        Vrai(!Noter("PreToolUse", Charge("t2", "already-finished"), 1021), "Un Post arrivé avant le Pre garde son appel terminé.");
        Vrai(Etat()["tools"]!.AsObject().Count == 0, "Le Pre retardé ne rouvre pas l'appel.");
        Noter("SubagentStart", Charge("t2", enfant: "worker"), 1023);
        Noter("Stop", Charge("t2"), 1024);
        Vrai(Actuel() == "run", "Un Stop parent ne finit pas tant qu'un enfant travaille.");
        Noter("SubagentStop", Charge("t2", enfant: "worker"), 1025);
        Vrai(Actuel() == "done", "La fin du dernier enfant applique seulement le Stop parent déjà attesté.");
        Noter("Stop", Charge("t2"), 1026);
        Vrai(Actuel() == "done", "Le parent peut confirmer sa fin après l'enfant.");
        avant = File.ReadAllText(sortie);
        Vrai(!Noter("SubagentStop", Charge("t2", enfant: "worker"), 1025)
            && File.ReadAllText(sortie) == avant, "Un doublon de fin enfant ne remet pas le parent fini en travail.");

        Noter("UserPromptSubmit", Charge("t3"), 1030);
        Noter("SubagentStart", Charge("t3", enfant: "worker"), 1030.1);
        Noter("SubagentStop", Charge("t3", enfant: "worker"), 1030.2);
        Vrai(Actuel() == "run", "Une fin enfant sans Stop parent ne termine jamais le tour.");
        Noter("PreToolUse", Charge("t3", "question", outil: "request_user_input"), 1031);
        Vrai(Actuel() == "wait", "Une question bloquante attend une réponse.");
        Noter("PostToolUse", Charge("t3", "question"), 1032);
        Vrai(Actuel() == "run", "La réponse à la question reprend le travail.");
        Noter("PreToolUse", Charge("t3", "async", outil: "request_user_input_async"), 1033);
        Vrai(Actuel() == "run", "Une carte asynchrone ne bloque pas le tour.");
        Noter("PostToolUse", Charge("t3", "async"), 1034);
        var arretQuestion = Charge("t3");
        arretQuestion["last_assistant_message"] = "Tu valides **?**";
        Noter("Stop", arretQuestion, 1035);
        Vrai(Actuel() == "wait", "Un message terminal interrogatif demande l'attention.");

        Noter("UserPromptSubmit", Charge("t4"), 1040);
        Noter("PreToolUse", Charge("t4", "ongoing"), 1041);
        Noter("Interrupt", Charge("t4"), 1042);
        avant = File.ReadAllText(sortie);
        Vrai(Actuel() == "repos" && !Noter("PostToolUse", Charge("t4", "ongoing"), 1043)
            && !Noter("Stop", Charge("t4"), 1044) && File.ReadAllText(sortie) == avant, "Les callbacks tardifs ne ressuscitent pas un tour interrompu.");
        Noter("UserPromptSubmit", Charge("t5"), 1050);
        avant = File.ReadAllText(sortie);
        Vrai(!Noter("UserPromptSubmit", Charge("t3"), 1051) && !Noter("Stop", Charge("t4"), 1052)
            && File.ReadAllText(sortie) == avant, "Les tours retirés ne remplacent jamais le nouveau tour.");
        Noter("SessionStart", Charge("t5"), 1049);
        Vrai(File.ReadAllText(sortie) == avant, "Un SessionStart tardif ne remet pas le tour au repos.");
        Noter("PreToolUse", Charge("t5"), 1053);
        Noter("Stop", Charge("t5"), 1054);
        Vrai(Actuel() == "run", "Un appel sans id invalide le suivi terminal au lieu de simuler une fin.");

        // Partially enabled hooks cannot prove a whole turn.
        var sansPrompt = Charge("t-new");
        sansPrompt["session_id"] = "s2";
        sansPrompt["transcript_path"] = this.Transcript("parent2", "s2");
        Codex.Noter("Stop", sansPrompt, 1060, dossier);
        Vrai(JsonNode.Parse(File.ReadAllText(Path.Combine(dossier, "codex", "s2.json")))!["state"]!.GetValue<String>() == "run",
            "Un Stop isolé n'est pas une preuve de fin.");

        Noter("UserPromptSubmit", Charge("ordering"), 1065);
        Noter("PreToolUse", Charge("ordering", "newer", outil: "Bash"), 1067);
        avant = File.ReadAllText(sortie);
        Vrai(!Noter("Stop", Charge("ordering"), 1066) && !Noter("Interrupt", Charge("ordering"), 1066)
            && File.ReadAllText(sortie) == avant, "Un ancien terminal n'est pas blanchi par le dernier horodatage.");
        var permissionRecente = Charge("ordering", outil: "Bash");
        permissionRecente.Remove("tool_use_id");
        Vrai(Noter("PermissionRequest", permissionRecente, 1068) && Actuel() == "perm",
            "Le vieux Stop n'empêche pas une vraie permission ultérieure.");

        Noter("UserPromptSubmit", Charge("concurrent"), 1070);
        Parallel.For(0, 8, i =>
        {
            if (!Noter("PreToolUse", Charge("concurrent", "parallel-" + i), 1071 + (i / 100.0))) { throw new InvalidOperationException("Pre concurrent perdu."); }
        });
        Vrai(Etat()["tools"]!.AsObject().Count == 8, "Les écritures réellement concurrentes conservent tous les appels.");
        Parallel.For(0, 8, i =>
        {
            if (!Noter("PostToolUse", Charge("concurrent", "parallel-" + i), 1072 + (i / 100.0))) { throw new InvalidOperationException("Post concurrent perdu."); }
        });
        Vrai(Etat()["tools"]!.AsObject().Count == 0 && Actuel() == "run", "Les appels parallèles terminés attendent toujours leur Stop parent.");
        Noter("Stop", Charge("concurrent"), 1073);
        Vrai(Actuel() == "done", "La fin parent confirme les appels parallèles terminés.");

        // The lock is cross-process and bounded; an existing file is not a stale lock.
        var verrouPath = Path.Combine(dossier, "codex", "s1.lock");
        using (var tenu = new FileStream(verrouPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var chrono = Stopwatch.StartNew();
            Vrai(!Noter("SessionEnd", Charge("concurrent"), 1081) && chrono.ElapsedMilliseconds < 500, "Un verrou occupé ne bloque jamais le harnais une seconde.");
        }

        Vrai(Noter("SessionEnd", Charge("concurrent"), 1082) && Actuel() == "end", "Le verrou se reprend sans supprimer un fichier actif.");
        Vrai(!Directory.EnumerateFiles(Path.Combine(dossier, "codex"), "*.tmp").Any(), "L'écriture atomique ne laisse pas de temporaire.");
    }
}

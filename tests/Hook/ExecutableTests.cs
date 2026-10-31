using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NexusKeypad.Tests;

// The published nexuskeypad-hook.exe itself (Native AOT, as shipped in the plugin package), run in temporary profiles:
// live states of Claude and Antigravity (rules recorded on etat.py, the original version retired on 26/10), the passive
// Codex contract (temporary folders only: never the real plugins folder nor the real Logi service).
public sealed class ExecutableTests : IDisposable
{
    private const String Choix = "mcp__ccd_turn__wrap_up"; // the app's end-of-turn box: a question when "ask" is filled

    private readonly String _d = Directory.CreateTempSubdirectory("nexuskeypad-hook-exe-").FullName;

    public void Dispose()
    {
        for (var essai = 0; ; essai++)
        {
            try
            {
                Directory.Delete(this._d, recursive: true);
                return;
            }
            catch (IOException) when (essai < 50)
            {
                Thread.Sleep(100); // a detached fake agent may still be finishing
            }
        }
    }

    internal sealed record Sortie(Int32 Code, String Stdout, String Stderr);

    // Runs the executable with the given profile; no window, stdin closed after the input.
    internal static Sortie Lancer(IEnumerable<String> arguments, String entree, IDictionary<String, String?> environnement, Int32 delai = 30000)
    {
        Assert.True(File.Exists(Entree.Hook), $"nexuskeypad-hook.exe is not published at {Entree.Hook} (the test build publishes it).");
        var info = new ProcessStartInfo(Entree.Hook)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in arguments)
        {
            info.ArgumentList.Add(a);
        }

        foreach (var (cle, valeur) in environnement)
        {
            if (valeur is null)
            {
                info.Environment.Remove(cle);
            }
            else
            {
                info.Environment[cle] = valeur;
            }
        }

        using var processus = Process.Start(info)!;
        var stdout = processus.StandardOutput.ReadToEndAsync();
        var stderr = processus.StandardError.ReadToEndAsync();
        processus.StandardInput.Write(entree);
        processus.StandardInput.Close();
        if (!processus.WaitForExit(delai))
        {
            processus.Kill();
            Assert.Fail($"nexuskeypad-hook {String.Join(' ', arguments)}: no exit after {delai} ms.");
        }

        return new(processus.ExitCode, stdout.Result, stderr.Result);
    }

    private Sortie Etat(String agent, String evenement, String entree = "{}")
    {
        var sortie = Lancer([agent, evenement], entree, new Dictionary<String, String?>
        {
            ["USERPROFILE"] = this._d, ["LOCALAPPDATA"] = Path.Combine(this._d, "local"), ["CLAUDE_CONFIG_DIR"] = null,
        });
        Assert.Equal(0, sortie.Code);
        return sortie;
    }

    private static JsonNode Json(String texte) => JsonNode.Parse(texte)!;

    private String Trinite(String agent) => Path.Combine(this._d, ".nexus", "trinite", agent);

    [Fact]
    public void Connecteur_desactive_ne_publie_plus()
    {
        var recus = Path.Combine(this._d, "local", "NexusKeypad", "connecteurs", "etats");
        Directory.CreateDirectory(recus);
        foreach (var agent in new[] { "claude", "antigravity" })
        {
            File.WriteAllText(Path.Combine(recus, agent + ".json"), "{\"active\":false}");
            var sortie = this.Etat(agent, "PreToolUse", "{\"session_id\":\"s1\",\"conversationId\":\"s1\"}");
            Assert.True(JsonNode.DeepEquals(Json(sortie.Stdout), Json(agent == "antigravity" ? "{\"decision\":\"allow\"}" : "{}")), agent);
            Assert.False(File.Exists(Path.Combine(this.Trinite(agent), "s1.json")), agent);
        }
    }

    // (agent, event, payload, state before, last message of the turn, expected state).
    public static TheoryData<String, String, String, String?, String?, String> Cas => new()
    {
        { "claude", "UserPromptSubmit", "{}", "wait", null, "run" }, // turn resumed
        { "claude", "PreToolUse", """{"tool_name":"Bash"}""", "wait", null, "run" },
        { "claude", "PreToolUse", """{"tool_name":"AskUserQuestion"}""", null, null, "wait" },
        { "claude", "PreToolUse", $$$"""{"tool_name":"{{{Choix}}}","tool_input":{"ask":"On y va ?"}}""", null, null, "ask" },
        { "claude", "PreToolUse", $$$"""{"tool_name":"{{{Choix}}}","tool_input":{}}""", null, null, "run" },
        { "claude", "PostToolUse", $$$"""{"tool_name":"{{{Choix}}}"}""", "ask", null, "ask" }, // the box keeps its question until Stop
        { "claude", "PostToolUse", """{"tool_name":"Bash"}""", "perm", null, "run" }, // permission granted
        { "claude", "PostToolUseFailure", """{"tool_name":"Bash"}""", "perm", null, "run" }, // granted, the tool failed
        { "claude", "Notification", """{"notification_type":"permission_prompt"}""", null, null, "perm" },
        { "claude", "Notification", """{"message":"Claude needs your Permission"}""", null, null, "perm" },
        { "claude", "Notification", """{"message":"idle"}""", "run", null, "run" },
        { "claude", "StopFailure", """{"error":"rate_limit"}""", null, null, "limit" },
        { "claude", "StopFailure", """{"error_type":"billing_error"}""", null, null, "limit" },
        { "claude", "StopFailure", """{"error_type":"server_error"}""", null, null, "error" },
        { "claude", "Stop", "{}", "ask", null, "wait" },
        { "claude", "Stop", "{}", "perm", null, "done" }, // refused: the turn ends, no more padlock
        { "claude", "Stop", "{}", null, "C'est fait.", "done" },
        { "claude", "Stop", "{}", null, "On le pousse ?", "wait" },
        { "claude", "Stop", "{}", null, "Tu valides **?**", "wait" },
        { "claude", "Stop", "{}", null, "« Prêt ? »", "wait" },
        { "claude", "SessionEnd", "{}", "run", null, "end" },
        { "antigravity", "PreInvocation", "{}", "done", null, "run" },
        { "antigravity", "PostToolUse", "{}", "wait", null, "run" },
        { "antigravity", "Stop", """{"terminationReason":"USER_CANCELED"}""", "run", null, "repos" },
        { "antigravity", "Stop", """{"terminationReason":"USER_CANCELED","fullyIdle":false}""", "run", null, "run" },
        { "antigravity", "PreToolUse", """{"toolCall":{"name":"ask_question"}}""", null, null, "wait" },
        { "antigravity", "PreToolUse", """{"toolCall":{"name":"run_command"}}""", null, null, "run" },
        { "antigravity", "Stop", """{"fullyIdle":false}""", null, null, "run" }, // a sub-agent is still working
        { "antigravity", "Stop", """{"transcriptPath":"<morceaux>"}""", null, null, "wait" }, // PLANNER_RESPONSE ending with "?"
        { "antigravity", "Inconnu", "{}", "run", null, "run" },
    };

    [Theory]
    [MemberData(nameof(Cas))]
    public void Regles_des_etats(String agent, String evenement, String charge, String? avant, String? texte, String attendu)
    {
        var morceaux = Path.Combine(this._d, "chunks");
        Directory.CreateDirectory(morceaux);
        File.WriteAllText(Path.Combine(morceaux, "00000001.jsonl"),
            JsonSerializer.Serialize(new { type = "PLANNER_RESPONSE", content = "Je relance ?" }) + "\n");
        var objet = Json(charge).AsObject();
        if (objet["transcriptPath"]?.GetValue<String>() == "<morceaux>")
        {
            objet["transcriptPath"] = morceaux;
        }

        objet[agent == "claude" ? "session_id" : "conversationId"] = "s1";
        if (texte is not null)
        {
            var journal = Path.Combine(this._d, "t.jsonl");
            var ligne = new JsonObject
            {
                ["type"] = "assistant",
                ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = texte }) },
            };
            File.WriteAllText(journal, ligne.ToJsonString() + "\n");
            objet["transcript_path"] = journal;
        }

        var dossier = this.Trinite(agent);
        Directory.CreateDirectory(dossier);
        var fichier = Path.Combine(dossier, "s1.json");
        if (avant is not null)
        {
            File.WriteAllText(fichier, JsonSerializer.Serialize(new { state = avant, at = 1.0, @event = "x" }));
        }

        var sortie = this.Etat(agent, evenement, objet.ToJsonString());
        // Antigravity's PreToolUse: without "allow" it REFUSES the tool (all its tools blocked on 23/10).
        var voulu = agent == "antigravity" && evenement == "PreToolUse" ? "{\"decision\":\"allow\"}" : "{}";
        Assert.True(JsonNode.DeepEquals(Json(sortie.Stdout), Json(voulu)), $"{agent} {evenement}: {sortie.Stdout}");
        var obtenu = File.Exists(fichier) ? Json(File.ReadAllText(fichier))["state"]!.GetValue<String>() : null;
        Assert.Equal(attendu, obtenu);
        Assert.All(Directory.GetFiles(dossier), f => Assert.Equal(".json", Path.GetExtension(f))); // neither lock nor temporary
    }

    [Fact]
    public void Evenement_en_retard_ignore()
    {
        // Background hooks: a PreToolUse sent before the Stop but written after it must not replace it.
        var dossier = this.Trinite("claude");
        Directory.CreateDirectory(dossier);
        File.WriteAllText(Path.Combine(dossier, "s1.json"), JsonSerializer.Serialize(new { state = "done", at = 9e12, @event = "Stop" }));
        this.Etat("claude", "PreToolUse", JsonSerializer.Serialize(new { session_id = "s1", tool_name = "Bash" }));
        Assert.Equal("done", Json(File.ReadAllText(Path.Combine(dossier, "s1.json")))["state"]!.GetValue<String>());
        Assert.Equal(["s1.json"], Directory.GetFiles(dossier).Select(Path.GetFileName)); // neither lock nor temporary
    }

    [Fact]
    public void Entree_vide_ou_illisible()
    {
        foreach (var entree in new[] { "", "pas du json" })
        {
            Assert.Equal("{}", this.Etat("claude", "Stop", entree).Stdout.Trim());
        }

        Assert.Equal("done", Json(File.ReadAllText(Path.Combine(this.Trinite("claude"), "defaut.json")))["state"]!.GetValue<String>());
    }

    [Fact]
    public void Connecteur_Codex_declare_ses_hooks_vers_le_programme_installe_sans_doublon()
    {
        // The shipped executable, next to its package's connectors as in the plugin folder, sets up Codex in a throwaway
        // profile that already enables nexus-keypad from both Nexus catalogs: one plugin keeps the hooks, the others go off.
        var paquet = Path.Combine(this._d, "Plugins", "NexusKeypad", "bin");
        Directory.CreateDirectory(paquet);
        var exe = Path.Combine(paquet, "nexuskeypad-hook.exe");
        File.Copy(Entree.Hook, exe);
        foreach (var fichier in Directory.EnumerateFiles(Path.Combine(Entree.Depot, "connecteurs"), "*", SearchOption.AllDirectories))
        {
            var cible = Path.Combine(paquet, "connecteurs", Path.GetRelativePath(Path.Combine(Entree.Depot, "connecteurs"), fichier));
            Directory.CreateDirectory(Path.GetDirectoryName(cible)!);
            File.Copy(fichier, cible);
        }

        var quota = Path.Combine(paquet, "claude-quota", "nexuskeypad-quota", "hooks", "register.ts");
        Directory.CreateDirectory(Path.GetDirectoryName(quota)!);
        File.Copy(Path.Combine(Entree.Depot, "claude-quota", "nexuskeypad-quota", "hooks", "register.ts"), quota);
        var config = Path.Combine(this._d, ".codex", "config.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        // Without NexusSystem's catalog in Codex: the connector's personal copy; another enabled copy is turned off.
        File.WriteAllText(config, "[plugins.\"nexus-keypad@nexus\"]\nenabled = true\n");
        var profil = new Dictionary<String, String?> { ["USERPROFILE"] = this._d, ["LOCALAPPDATA"] = Path.Combine(this._d, "local"), ["CLAUDE_CONFIG_DIR"] = null };
        Sortie Connecteur(String action)
        {
            var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in new[] { "connecteur", "codex", action }) { info.ArgumentList.Add(a); }
            foreach (var (cle, valeur) in profil) { if (valeur is null) { info.Environment.Remove(cle); } else { info.Environment[cle] = valeur; } }
            using var p = Process.Start(info)!;
            var o = p.StandardOutput.ReadToEndAsync();
            var e = p.StandardError.ReadToEndAsync();
            Assert.True(p.WaitForExit(30000));
            return new(p.ExitCode, o.Result, e.Result);
        }

        for (var fois = 0; fois < 2; fois++)
        {
            var active = Connecteur("enable");
            Assert.True(active.Code == 0 && Json(active.Stdout)["state"]!.GetValue<String>() == "Pret", active.Stdout + active.Stderr);
        }

        var texte = File.ReadAllText(config);
        Assert.Contains("[plugins.\"nexus-keypad@nexus\"]\nenabled = false", texte.Replace("\r\n", "\n"));
        var manifeste = Path.Combine(this._d, ".codex", "plugins", "nexus-keypad", ".codex-plugin", "plugin.json");
        var hooks = Json(File.ReadAllText(manifeste))["hooks"]!["hooks"]!.AsObject();
        Assert.Equal(10, hooks.Count);
        const String Prefixe = "cmd.exe /d /c \"%LOCALAPPDATA%\\NexusKeypad\\hook\\nexuskeypad-hook.exe\" codex ";
        foreach (var (evenement, groupes) in hooks)
        {
            Assert.Single(groupes!.AsArray());
            var commande = groupes![0]!["hooks"]![0]!["command"]!.GetValue<String>();
            Assert.Equal(Prefixe + evenement, commande);
        }
        // %LOCALAPPDATA% names the program the connector installed there.
        Assert.True(File.Exists(Path.Combine(profil["LOCALAPPDATA"]!, "NexusKeypad", "hook", "nexuskeypad-hook.exe")));

        Assert.Equal("Pret", Json(Connecteur("status").Stdout)["state"]!.GetValue<String>());
    }

    // --- Passive Codex contract: outputs never decide, incomplete input creates no state, disabling stops observation.

    private Sortie Codex(String evenement, String charge)
    {
        var sortie = Lancer(["codex", evenement], charge, new Dictionary<String, String?>
        {
            ["USERPROFILE"] = this._d, ["LOCALAPPDATA"] = Path.Combine(this._d, "local"),
        }, delai: 3000);
        Assert.Equal(0, sortie.Code);
        Assert.True(JsonNode.DeepEquals(Json(sortie.Stdout), Json("{}")), sortie.Stdout);
        Assert.Equal("", sortie.Stderr);
        return sortie;
    }

    private String TranscriptCodex()
    {
        var transcript = Path.Combine(this._d, "transcript.jsonl");
        File.WriteAllText(transcript, JsonSerializer.Serialize(new { type = "session_meta", payload = new { id = "s1", source = "vscode", thread_source = "user" } }) + "\n");
        return transcript;
    }

    [Fact]
    public void Codex_sorties_ne_decident_jamais()
    {
        var transcript = this.TranscriptCodex();
        var fichier = Path.Combine(this.Trinite("codex"), "s1.json");
        foreach (var (evenement, detail) in new (String, Object)[]
        {
            ("SessionStart", new { }), ("UserPromptSubmit", new { }),
            ("PreToolUse", new { tool_use_id = "a", tool_name = "Bash" }),
            ("PermissionRequest", new { tool_name = "Bash" }),
            ("PostToolUse", new { tool_use_id = "a", tool_name = "Bash" }),
            ("Stop", new { last_assistant_message = "Fini." }), ("Interrupt", new { }), ("SessionEnd", new { }),
        })
        {
            var charge = JsonSerializer.SerializeToNode(detail)!.AsObject();
            charge["session_id"] = "s1";
            charge["turn_id"] = "t1";
            charge["transcript_path"] = transcript;
            this.Codex(evenement, charge.ToJsonString());
            var attendu = evenement switch { "UserPromptSubmit" => "run", "PermissionRequest" => "perm", "PostToolUse" => "run", "Stop" => "done", _ => null };
            if (attendu is not null)
            {
                Assert.Equal(attendu, Json(File.ReadAllText(fichier))["state"]!.GetValue<String>());
            }
        }

        Assert.Equal("end", Json(File.ReadAllText(fichier))["state"]!.GetValue<String>());
    }

    [Fact]
    public void Codex_entree_incomplete_ne_cree_pas_d_etat()
    {
        foreach (var charge in new[] { "", "{", "[]", "{}", """{"session_id":"../s1","turn_id":"t1"}""" })
        {
            this.Codex("Stop", charge);
        }

        Assert.False(Directory.Exists(this.Trinite("codex")));
    }

    [Fact]
    public void Codex_desactivation_conserve_le_flux_et_coupe_l_observation()
    {
        var recus = Path.Combine(this._d, "local", "NexusKeypad", "connecteurs", "etats");
        Directory.CreateDirectory(recus);
        var recu = Path.Combine(recus, "codex.json");
        File.WriteAllText(recu, """{"active":true,"identity":"nexus-keypad@personal"}""");
        Directory.CreateDirectory(Path.Combine(this._d, ".codex"));
        File.WriteAllText(Path.Combine(this._d, ".codex", "config.toml"), "[plugins.\"nexus-keypad@personal\"]\nenabled = true\n");
        var transcript = this.TranscriptCodex();
        this.Codex("UserPromptSubmit", JsonSerializer.Serialize(new { session_id = "s1", turn_id = "t1", transcript_path = transcript }));
        var fichier = Path.Combine(this.Trinite("codex"), "s1.json");
        var avant = File.ReadAllBytes(fichier);
        File.WriteAllText(recu, """{"active":false,"identity":"nexus-keypad@personal"}""");
        this.Codex("PreToolUse", JsonSerializer.Serialize(new { session_id = "s1", turn_id = "t1", transcript_path = transcript, tool_use_id = "a", tool_name = "Bash" }));
        Assert.Equal(avant, File.ReadAllBytes(fichier));
    }
}

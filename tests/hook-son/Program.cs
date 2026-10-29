using System.Text.Json.Nodes;
using HookProgram = NexusKeypad.Hook.Program;

internal static class HookSoundTests
{
    private static int _controles;

    private static void Verifier(bool condition, string message)
    {
        if (!condition) { throw new Exception(message); }
        _controles++;
    }

    private static JsonObject Charge(string id = "s1") => new() { ["conversationId"] = id, ["fullyIdle"] = true };

    public static int Main()
    {
        var racine = Path.Combine(Path.GetTempPath(), "nexuskeypad-hook-son-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(racine);
        var profilAvant = Environment.GetEnvironmentVariable("USERPROFILE");
        var localAvant = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        var claudeAvant = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        Environment.SetEnvironmentVariable("USERPROFILE", Path.Combine(racine, "profil"));
        Environment.SetEnvironmentVariable("LOCALAPPDATA", Path.Combine(racine, "local"));
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);
        try
        {
            double date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            bool Noter(string evenement, JsonObject? charge = null, string agent = "antigravity") =>
                HookProgram.Noter(agent, evenement, charge ?? Charge(), ++date, racine);
            Verifier(!Noter("PreInvocation"), "Le démarrage ne sonne pas.");
            Verifier(Noter("Stop"), "Une fin complète demande sa note.");
            Verifier(!Noter("Stop"), "Un Stop terminal dupliqué reste silencieux.");
            Verifier(!Noter("PreInvocation"), "Le tour suivant réarme sans son.");
            Verifier(Noter("Stop"), "Le tour suivant a sa propre note.");

            Noter("PreInvocation");
            var sousAgent = Charge();
            sousAgent["fullyIdle"] = false;
            Verifier(!Noter("Stop", sousAgent), "Aucune note pendant le travail d'un sous-agent.");
            Verifier(Noter("Stop"), "Le vrai Stop final après le sous-agent sonne.");

            Noter("PreInvocation");
            var annule = Charge();
            annule["terminationReason"] = "USER_CANCELED";
            Verifier(!Noter("Stop", annule), "Une annulation reste silencieuse.");
            Verifier(!Noter("Stop"), "Un doublon terminal après annulation ne joue pas une fausse fin.");

            Noter("PreInvocation");
            var question = Charge();
            question["toolCall"] = new JsonObject { ["name"] = "ask_question" };
            Verifier(!Noter("PreToolUse", question), "Poser une question ne sonne pas avant le Stop.");
            Verifier(Noter("Stop"), "La fin de tour demandant une réponse garde sa note d'attention.");
            Verifier(!Noter("Stop"), "Une question terminale dupliquée ne sonne pas deux fois.");

            // Une autre conversation ne doit pas être muette parce que la première a déjà sonné.
            Verifier(Noter("Stop", Charge("s2")), "Le dédoublonnage est propre à chaque session.");
            Verifier(!HookProgram.Noter("antigravity", "Stop", Charge("s2"), date - 100, racine),
                "Un ancien événement ne peut pas déclencher un son après une fin plus récente.");
            Verifier(!Noter("Stop", new JsonObject { ["session_id"] = "codex-test" }, "codex"),
                "Le helper n'ajoute pas de note au son propre de Codex.");
            Verifier(!Noter("Stop", new JsonObject()), "Une charge sans identité de session ne joue pas de note.");

            JsonObject Claude(string id = "claude-main") => new() { ["session_id"] = id };
            Verifier(!Noter("UserPromptSubmit", Claude(), "claude"), "Le démarrage Claude réarme sans son.");
            Verifier(Noter("Stop", Claude(), "claude"), "Le Stop Claude demande sa propre note.");
            Verifier(!Noter("Stop", Claude(), "claude"), "Un doublon Stop Claude ne joue pas une seconde note.");
            Noter("UserPromptSubmit", Claude(), "claude");
            var demandeClaude = Claude();
            demandeClaude["tool_name"] = "AskUserQuestion";
            Verifier(!Noter("PreToolUse", demandeClaude, "claude"), "La question Claude n'annonce pas une fin avant Stop.");
            Verifier(Noter("Stop", Claude(), "claude"), "La fin Claude attendant l'utilisateur conserve sa note.");
            Verifier(!Noter("Stop", Claude(), "claude"), "La question terminale Claude ne sonne qu'une fois.");
            Noter("UserPromptSubmit", Claude(), "claude");
            var annulationClaude = Claude();
            annulationClaude["terminationReason"] = "USER_CANCELED";
            Verifier(!Noter("Stop", annulationClaude, "claude"), "Une annulation Claude reste silencieuse.");
            Verifier(!Noter("Stop", Claude(), "claude"), "Un doublon ne transforme pas l'annulation Claude en réussite.");
            Noter("UserPromptSubmit", Claude(), "claude");
            Verifier(Noter("Stop", Claude(), "claude"), "Le vrai tour suivant de Claude réarme sa note.");
            Verifier(!Noter("SubagentStop", Claude(), "claude"), "Une fin de sous-agent ne joue pas la note du parent Claude.");
            Verifier(!Noter("StopFailure", Claude(), "claude"), "Un échec Claude n'annonce pas une réussite sonore.");
            Verifier(!Noter("SessionEnd", Claude(), "claude"), "Fermer la session Claude ne rejoue pas sa note.");
            var sessionsClaude = Path.Combine(racine, "local", "Claude-Data", "compte", "claude-code-sessions");
            Directory.CreateDirectory(sessionsClaude);
            File.WriteAllText(Path.Combine(sessionsClaude, "local_child.json"), "{\"cliSessionId\":\"claude-child\",\"spawnedFrom\":\"claude-main\"}");
            Noter("UserPromptSubmit", Claude("claude-child"), "claude");
            Verifier(!Noter("Stop", Claude("claude-child"), "claude"), "Une session Claude spawnedFrom reste silencieuse.");
            Verifier(!Noter("Stop", Claude("claude-child"), "claude"), "Un doublon de session enfant reste silencieux.");

            var recu = Path.Combine(racine, "local", "NexusKeypad", "connecteurs", "etats", "antigravity.json");
            var config = Path.Combine(racine, "profil", ".gemini", "config", "config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(recu)!);
            Directory.CreateDirectory(Path.GetDirectoryName(config)!);
            File.WriteAllText(config, "{\"plugins\":{\"nexus-keypad\":{\"enabled\":true}}}");
            Verifier(!NexusKeypad.Hook.Son.AppartientAuPlugin("antigravity"), "Une installation ancienne n'ajoute pas une seconde source de son.");
            File.WriteAllText(recu, "{\"active\":true,\"hookOwner\":\"plugin\"}");
            Verifier(NexusKeypad.Hook.Son.AppartientAuPlugin("antigravity"), "Le plugin activé possède sa note.");
            File.WriteAllText(config, "{\"plugins\":{\"nexus-keypad\":{\"enabled\":false}}}");
            Verifier(!NexusKeypad.Hook.Son.AppartientAuPlugin("antigravity"), "Le bouton désactiver du plugin coupe aussi son son.");
            File.WriteAllText(config, "{\"plugins\":{\"nexus-keypad\":{\"enabled\":true}}}");
            File.WriteAllText(recu, "{\"active\":false,\"hookOwner\":\"plugin\"}");
            Verifier(!NexusKeypad.Hook.Son.AppartientAuPlugin("antigravity"), "La désactivation du connecteur coupe le son.");
            File.WriteAllText(recu, "illisible");
            Verifier(!NexusKeypad.Hook.Son.AppartientAuPlugin("antigravity"), "Une configuration illisible reste silencieuse.");

            var recuClaude = Path.Combine(racine, "local", "NexusKeypad", "connecteurs", "etats", "claude.json");
            var configClaude = Path.Combine(racine, "profil", ".claude", "settings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configClaude)!);
            File.WriteAllText(configClaude, "{\"enabledPlugins\":{\"nexus-keypad@nexus\":true}}");
            File.WriteAllText(recuClaude, "{\"active\":true,\"hookOwner\":\"plugin\",\"identity\":\"nexus-keypad@nexus\"}");
            Verifier(NexusKeypad.Hook.Son.AppartientAuPlugin("claude"), "Le plugin privé Claude actif possède sa note.");
            File.WriteAllText(configClaude, "{\"enabledPlugins\":{\"nexus-keypad@nexus\":false}}");
            Verifier(!NexusKeypad.Hook.Son.AppartientAuPlugin("claude"), "Le bouton désactiver Claude coupe sa note.");
            File.WriteAllText(configClaude, "{\"enabledPlugins\":{\"nexus-keypad@nexuskeypad\":true}}");
            File.WriteAllText(recuClaude, "{\"active\":true,\"hookOwner\":\"plugin\",\"identity\":\"nexus-keypad@nexuskeypad\"}");
            Verifier(NexusKeypad.Hook.Son.AppartientAuPlugin("claude"), "Le plugin public Claude possède aussi sa note native.");
            File.WriteAllText(recuClaude, "{\"active\":false,\"hookOwner\":\"plugin\",\"identity\":\"nexus-keypad@nexuskeypad\"}");
            Verifier(!NexusKeypad.Hook.Son.AppartientAuPlugin("claude"), "Désactiver le connecteur Claude coupe sa note.");
            Verifier(!NexusKeypad.Hook.Son.AppartientAuPlugin("codex"), "Le helper n'intercepte jamais le son natif de Codex.");

            using var wav = typeof(HookProgram).Assembly.GetManifestResourceStream("NexusKeypad.Hook.antigravity.wav");
            Verifier(wav is { Length: > 44 }, "Le son est embarqué dans le paquet public.");
            Span<byte> entete = stackalloc byte[12];
            _ = wav!.Read(entete);
            Verifier(entete[..4].SequenceEqual("RIFF"u8) && entete[8..].SequenceEqual("WAVE"u8), "L'asset est un WAV lisible.");
            using var wavClaude = typeof(HookProgram).Assembly.GetManifestResourceStream("NexusKeypad.Hook.claude.wav");
            Verifier(wavClaude is { Length: > 44 }, "Le WAV Claude est aussi embarqué dans le paquet public.");
            _ = wavClaude!.Read(entete);
            Verifier(entete[..4].SequenceEqual("RIFF"u8) && entete[8..].SequenceEqual("WAVE"u8), "Le WAV Claude est lisible.");
            using var copieClaude = new MemoryStream();
            using var copieAgy = new MemoryStream();
            wavClaude.CopyTo(copieClaude);
            wav.CopyTo(copieAgy);
            Verifier(!copieClaude.ToArray().SequenceEqual(copieAgy.ToArray()), "Claude et AGY gardent deux notes distinctes.");
            Console.WriteLine($"Hook son : {_controles} contrôles réussis, aucun son joué.");
            return 0;
        }
        finally
        {
            Environment.SetEnvironmentVariable("USERPROFILE", profilAvant);
            Environment.SetEnvironmentVariable("LOCALAPPDATA", localAvant);
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", claudeAvant);
            Directory.Delete(racine, recursive: true);
        }
    }
}

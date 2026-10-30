using System.Text.Json.Nodes;
using HookProgram = NexusKeypad.Hook.Program;

namespace NexusKeypad.Tests;

// End-of-turn notes of Claude and Antigravity (no sound played): one note per real end, none for a start, a sub-agent, a
// cancellation, a duplicate or Codex; the note belongs to the enabled plugin only. USERPROFILE and LOCALAPPDATA point to a
// temporary profile for the whole test (this collection runs alone).
[Collection(nameof(ProfilRedirige))]
public sealed class SonTests : IDisposable
{
    private readonly String _racine = Path.Combine(Path.GetTempPath(), "nexuskeypad-hook-son-" + Guid.NewGuid().ToString("N"));
    private readonly String _profilAvant = Environment.GetEnvironmentVariable("USERPROFILE");
    private readonly String _localAvant = Environment.GetEnvironmentVariable("LOCALAPPDATA");
    private readonly String _claudeAvant = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
    private Double _date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

    public SonTests()
    {
        Directory.CreateDirectory(this._racine);
        Environment.SetEnvironmentVariable("USERPROFILE", Path.Combine(this._racine, "profil"));
        Environment.SetEnvironmentVariable("LOCALAPPDATA", Path.Combine(this._racine, "local"));
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("USERPROFILE", this._profilAvant);
        Environment.SetEnvironmentVariable("LOCALAPPDATA", this._localAvant);
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", this._claudeAvant);
        Directory.Delete(this._racine, recursive: true);
    }

    private static void Verifier(Boolean condition, String message) => Assert.True(condition, message);

    private static JsonObject Charge(String id = "s1") => new() { ["conversationId"] = id, ["fullyIdle"] = true };

    private static JsonObject Claude(String id = "claude-main") => new() { ["session_id"] = id };

    private Boolean Noter(String evenement, JsonObject? charge = null, String agent = "antigravity") =>
        HookProgram.Noter(agent, evenement, charge ?? Charge(), ++this._date, this._racine);

    [Fact]
    public void Notes_d_Antigravity()
    {
        Verifier(!this.Noter("PreInvocation"), "Le démarrage ne sonne pas.");
        Verifier(this.Noter("Stop"), "Une fin complète demande sa note.");
        Verifier(!this.Noter("Stop"), "Un Stop terminal dupliqué reste silencieux.");
        Verifier(!this.Noter("PreInvocation"), "Le tour suivant réarme sans son.");
        Verifier(this.Noter("Stop"), "Le tour suivant a sa propre note.");

        this.Noter("PreInvocation");
        var sousAgent = Charge();
        sousAgent["fullyIdle"] = false;
        Verifier(!this.Noter("Stop", sousAgent), "Aucune note pendant le travail d'un sous-agent.");
        Verifier(this.Noter("Stop"), "Le vrai Stop final après le sous-agent sonne.");

        this.Noter("PreInvocation");
        var annule = Charge();
        annule["terminationReason"] = "USER_CANCELED";
        Verifier(!this.Noter("Stop", annule), "Une annulation reste silencieuse.");
        Verifier(!this.Noter("Stop"), "Un doublon terminal après annulation ne joue pas une fausse fin.");

        this.Noter("PreInvocation");
        var question = Charge();
        question["toolCall"] = new JsonObject { ["name"] = "ask_question" };
        Verifier(!this.Noter("PreToolUse", question), "Poser une question ne sonne pas avant le Stop.");
        Verifier(this.Noter("Stop"), "La fin de tour demandant une réponse garde sa note d'attention.");
        Verifier(!this.Noter("Stop"), "Une question terminale dupliquée ne sonne pas deux fois.");

        // Another conversation must not be silent because the first one already rang.
        Verifier(this.Noter("Stop", Charge("s2")), "Le dédoublonnage est propre à chaque session.");
        Verifier(!HookProgram.Noter("antigravity", "Stop", Charge("s2"), this._date - 100, this._racine),
            "Un ancien événement ne peut pas déclencher un son après une fin plus récente.");
        Verifier(!this.Noter("Stop", new JsonObject { ["session_id"] = "codex-test" }, "codex"),
            "Le helper n'ajoute pas de note au son propre de Codex.");
        Verifier(!this.Noter("Stop", new JsonObject()), "Une charge sans identité de session ne joue pas de note.");
    }

    [Fact]
    public void Notes_de_Claude()
    {
        Verifier(!this.Noter("UserPromptSubmit", Claude(), "claude"), "Le démarrage Claude réarme sans son.");
        Verifier(this.Noter("Stop", Claude(), "claude"), "Le Stop Claude demande sa propre note.");
        Verifier(!this.Noter("Stop", Claude(), "claude"), "Un doublon Stop Claude ne joue pas une seconde note.");
        this.Noter("UserPromptSubmit", Claude(), "claude");
        var demandeClaude = Claude();
        demandeClaude["tool_name"] = "AskUserQuestion";
        Verifier(!this.Noter("PreToolUse", demandeClaude, "claude"), "La question Claude n'annonce pas une fin avant Stop.");
        Verifier(this.Noter("Stop", Claude(), "claude"), "La fin Claude attendant l'utilisateur conserve sa note.");
        Verifier(!this.Noter("Stop", Claude(), "claude"), "La question terminale Claude ne sonne qu'une fois.");
        this.Noter("UserPromptSubmit", Claude(), "claude");
        var annulationClaude = Claude();
        annulationClaude["terminationReason"] = "USER_CANCELED";
        Verifier(!this.Noter("Stop", annulationClaude, "claude"), "Une annulation Claude reste silencieuse.");
        Verifier(!this.Noter("Stop", Claude(), "claude"), "Un doublon ne transforme pas l'annulation Claude en réussite.");
        this.Noter("UserPromptSubmit", Claude(), "claude");
        Verifier(this.Noter("Stop", Claude(), "claude"), "Le vrai tour suivant de Claude réarme sa note.");
        Verifier(!this.Noter("SubagentStop", Claude(), "claude"), "Une fin de sous-agent ne joue pas la note du parent Claude.");
        Verifier(!this.Noter("StopFailure", Claude(), "claude"), "Un échec Claude n'annonce pas une réussite sonore.");
        Verifier(!this.Noter("SessionEnd", Claude(), "claude"), "Fermer la session Claude ne rejoue pas sa note.");
        var sessionsClaude = Path.Combine(this._racine, "local", "Claude-Data", "compte", "claude-code-sessions");
        Directory.CreateDirectory(sessionsClaude);
        File.WriteAllText(Path.Combine(sessionsClaude, "local_child.json"), "{\"cliSessionId\":\"claude-child\",\"spawnedFrom\":\"claude-main\"}");
        this.Noter("UserPromptSubmit", Claude("claude-child"), "claude");
        Verifier(!this.Noter("Stop", Claude("claude-child"), "claude"), "Une session Claude spawnedFrom reste silencieuse.");
        Verifier(!this.Noter("Stop", Claude("claude-child"), "claude"), "Un doublon de session enfant reste silencieux.");
    }

    [Fact]
    public void La_note_appartient_au_plugin_active()
    {
        var recu = Path.Combine(this._racine, "local", "NexusKeypad", "connecteurs", "etats", "antigravity.json");
        var config = Path.Combine(this._racine, "profil", ".gemini", "config", "config.json");
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

        var recuClaude = Path.Combine(this._racine, "local", "NexusKeypad", "connecteurs", "etats", "claude.json");
        var configClaude = Path.Combine(this._racine, "profil", ".claude", "settings.json");
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
    }

    [Fact]
    public void Deux_notes_WAV_distinctes_embarquees()
    {
        using var wav = typeof(HookProgram).Assembly.GetManifestResourceStream("NexusKeypad.Hook.antigravity.wav");
        Verifier(wav is { Length: > 44 }, "Le son est embarqué dans le paquet public.");
        Span<Byte> entete = stackalloc Byte[12];
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
    }
}

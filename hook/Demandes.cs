// Demande d'autorisation de Claude, à trancher depuis le pad (touches Approuver / Refuser).
//
// Le hook PermissionRequest dépose la demande dans ~/.nexus/trinite/claude/demandes/<session>.json
// ({"id", "session", "outil", "resume", "at"}) et attend la réponse du pad, <session>.decision.json ({"id", "behavior":
// "allow" | "deny"}), écrite par le plugin. Dans l'appli de bureau, la carte d'autorisation reste à l'écran pendant ce
// temps : hôte et hook décident en parallèle, le premier l'emporte (doc des hooks, PermissionRequest). Ailleurs (terminal),
// le hook n'attend pas : il retarderait la question. L'attente cesse dès que la demande est tranchée à l'écran (l'état de
// la session quitte « perm » après elle), ou après 10 minutes ; la demande est alors retirée.
using System.Text;
using System.Text.Json.Nodes;

namespace NexusKeypad.Hook;

public static class Demandes
{
    public static readonly TimeSpan Delai = TimeSpan.FromMinutes(10);

    public static string Dossier => Path.Combine(Program.DossierEtats, "claude", "demandes");

    // Ce qui est demandé, en court pour la touche du milieu : la commande, le fichier, sinon la description.
    public static string Resume(JsonObject charge)
    {
        var entree = charge["tool_input"] as JsonObject ?? [];
        foreach (var cle in new[] { "command", "file_path", "path", "url", "description", "pattern" })
        {
            if (entree[cle] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s))
            {
                s = s.ReplaceLineEndings(" ").Trim();
                return s.Length > 80 ? s[..80] : s;
            }
        }

        return "";
    }

    public static string Attendre(JsonObject charge, double debut)
    {
        if (Environment.GetEnvironmentVariable("CLAUDE_CODE_ENTRYPOINT") != "claude-desktop")
        {
            return "{}";
        }

        var session = Program.Session(charge);
        var id = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Dossier);
        var demande = Path.Combine(Dossier, session + ".json");
        var decision = Path.Combine(Dossier, session + ".decision.json");
        var etat = Path.Combine(Program.DossierEtats, "claude", session + ".json");
        var outil = charge["tool_name"] is JsonValue o && o.TryGetValue<string>(out var t) ? t : "";
        Ecrire(demande, new JsonObject { ["id"] = id, ["session"] = session, ["outil"] = outil, ["resume"] = Resume(charge), ["at"] = debut });
        try
        {
            var fin = DateTime.UtcNow + Delai;
            while (DateTime.UtcNow < fin)
            {
                Thread.Sleep(200);
                if (Lire(decision) is { } d && d["id"]?.GetValue<string>() == id
                    && d["behavior"]?.GetValue<string>() is "allow" or "deny" && d["behavior"]!.GetValue<string>() is var choix)
                {
                    var reponse = new JsonObject { ["behavior"] = choix };
                    if (choix == "deny")
                    {
                        reponse["message"] = "Refusé depuis le pad NexusKeypad.";
                    }

                    return new JsonObject
                    {
                        ["hookSpecificOutput"] = new JsonObject { ["hookEventName"] = "PermissionRequest", ["decision"] = reponse },
                    }.ToJsonString();
                }

                // Tranchée à l'écran : un événement parti après la demande a fait quitter « perm » à la session.
                if (Lire(etat) is { } e && Program.Nombre(e["at"], 0) > debut && e["state"]?.GetValue<string>() != "perm")
                {
                    return "{}";
                }

                // Remplacée par une demande plus récente de la même session (l'ancienne n'attend plus rien).
                if (Lire(demande) is { } courante && courante["id"]?.GetValue<string>() != id)
                {
                    return "{}";
                }
            }

            return "{}";
        }
        finally
        {
            if (Lire(demande) is { } courante && courante["id"]?.GetValue<string>() == id)
            {
                Supprimer(demande);
            }

            if (Lire(decision) is { } d && d["id"]?.GetValue<string>() == id)
            {
                Supprimer(decision);
            }
        }
    }

    private static JsonObject? Lire(string fichier)
    {
        try
        {
            using var flux = new FileStream(fichier, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var lecteur = new StreamReader(flux, new UTF8Encoding(false));
            return JsonNode.Parse(lecteur.ReadToEnd()) as JsonObject;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void Ecrire(string fichier, JsonObject contenu)
    {
        var temporaire = $"{fichier}.{Environment.ProcessId}.tmp";
        File.WriteAllText(temporaire, contenu.ToJsonString(), new UTF8Encoding(false));
        File.Move(temporaire, fichier, overwrite: true);
    }

    private static void Supprimer(string fichier)
    {
        try
        {
            File.Delete(fichier);
        }
        catch (Exception)
        {
        }
    }
}

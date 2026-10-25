namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;

    // Demandes d'autorisation de Claude à trancher depuis le pad (touches Approuver / Refuser), déposées par le hook
    // PermissionRequest (hook/Demandes.cs, etat/etat.py) : demandes/<session>.json ; réponse : demandes/<session>.decision.json.
    public sealed partial class Observateur
    {
        private static String DossierDemandes => Path.Combine(DossierEtats, "claude", "demandes");

        // La plus ancienne demande en attente ; plus vieille que l'attente du hook (10 min), elle est périmée.
        internal static Demande DemandeClaude(String dossier = null)
        {
            dossier ??= DossierDemandes;
            if (!Directory.Exists(dossier))
            {
                return null;
            }

            return Directory.EnumerateFiles(dossier, "*.json")
                .Where(f => !f.EndsWith(".decision.json", StringComparison.OrdinalIgnoreCase))
                .Select(LireDemande)
                .Where(d => d is not null && DateTime.UtcNow - d.Depuis < TimeSpan.FromMinutes(11))
                .OrderBy(d => d.Depuis)
                .FirstOrDefault();
        }

        private static Demande LireDemande(String fichier)
        {
            try
            {
                using var flux = new FileStream(fichier, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var doc = JsonDocument.Parse(flux);
                var r = doc.RootElement;
                String Champ(String nom) => r.TryGetProperty(nom, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";
                var quand = DateTimeOffset.FromUnixTimeMilliseconds((Int64)(r.GetProperty("at").GetDouble() * 1000)).UtcDateTime;
                return Champ("id").Length == 0 ? null : new Demande(Path.GetFileNameWithoutExtension(fichier), Champ("id"), Champ("outil"), Champ("resume"), quand);
            }
            catch (Exception)
            {
                return null; // en cours d'écriture, ou retirée entre-temps
            }
        }

        // Réponse du pad à une demande : le hook qui attend la rend à Claude.
        internal static void Repondre(Demande d, Boolean accord, String dossier = null)
        {
            dossier ??= DossierDemandes;
            Directory.CreateDirectory(dossier);
            var fichier = Path.Combine(dossier, d.Session + ".decision.json");
            var temporaire = fichier + ".pad.tmp";
            File.WriteAllText(temporaire, JsonSerializer.Serialize(new { id = d.Id, behavior = accord ? "allow" : "deny" }), new UTF8Encoding(false));
            File.Move(temporaire, fichier, overwrite: true);
        }
    }
}

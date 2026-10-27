namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    // Claude : sessions de l'appli de bureau, compte connecté.
    public sealed partial class Observateur
    {
        // Claude : fichiers de session de l'appli de bureau (titre, activité, dernier regard) + état des hooks.
        private IEnumerable<Conversation> Claude()
        {
            var racine = Directory.Exists(Path.Combine(Local, "Claude-Data"))
                ? Directory.EnumerateDirectories(Path.Combine(Local, "Claude-Data")).Select(d => Path.Combine(d, "claude-code-sessions")).FirstOrDefault(Directory.Exists)
                : null;
            if (racine is null)
            {
                yield break;
            }

            // Plusieurs comptes Claude sur le PC : l'appli ne montre que les conversations du compte connecté
            // (lastKnownAccountUuid de son config.json, un dossier par compte).
            var config = Path.Combine(Path.GetDirectoryName(racine), "config.json");
            var ecritConfig = File.Exists(config) ? File.GetLastWriteTimeUtc(config) : DateTime.MinValue;
            if (ecritConfig != this._compteClaude.Ecrit)
            {
                this._compteClaude = (ecritConfig, CompteClaude(config));
            }

            if (this._compteClaude.Compte is String compte && Directory.Exists(Path.Combine(racine, compte)))
            {
                racine = Path.Combine(racine, compte);
            }

            var clis = new HashSet<String>(); // remplacé d'un bloc à la fin : jamais lu à moitié rempli
            foreach (var fichier in Directory.EnumerateFiles(racine, "local_*.json", SearchOption.AllDirectories))
            {
                var ecrit = File.GetLastWriteTimeUtc(fichier);
                if (DateTime.UtcNow - ecrit > Fenetre)
                {
                    continue;
                }

                if (!this._cacheClaude.TryGetValue(fichier, out var cache) || cache.Ecrit != ecrit)
                {
                    cache = (ecrit, Debut(fichier, 6000));
                    this._cacheClaude[fichier] = cache;
                }

                var meta = cache.Meta;
                if (Champ(meta, "isArchived") == "true" || Champ(meta, "sessionId") is not String id)
                {
                    continue;
                }

                var titre = Champ(meta, "title") ?? "Session Claude";
                var activite = Instant(Champ(meta, "lastActivityAt")) ?? ecrit;
                var regard = Instant(Champ(meta, "lastFocusedAt")) ?? DateTime.MinValue;
                var cli = Champ(meta, "cliSessionId");
                if (cli is not null)
                {
                    clis.Add(cli);
                }

                var hook = cli is not null ? Hook("claude", cli) : null;
                var etat = hook is { } h ? Traduire(h.Etat, h.Quand) : Etat.Repos;
                var depuis = hook?.Quand ?? activite;
                var vu = regard > depuis || this.Vu(0, id, depuis);
                yield return new(0, id, titre, etat is Etat.Fini or Etat.Erreur && vu ? Etat.Repos : etat, depuis, activite, vu);
            }

            this._clisClaude = clis;
        }

        // Sessions récentes du compte Claude connecté (cliSessionId), relevées par Claude().
        private HashSet<String> _clisClaude = [];

        // Quota écrit par le plugin Claude « Nexus Pad » (facultatif, non publié ici) à chaque réponse : les deux fenêtres exactes,
        // sans réseau ni menu ouvert. Gardé seulement s'il vient d'une session du compte connecté ; une fenêtre dont la
        // remise à zéro est passée vaut 0 %. Null : pas de plugin, autre compte, ou fichier illisible.
        // valable : publication lisible du compte connecté ; elle fait alors foi, même périmée (null : jauge grise).
        private Quota QuotaClaudePlugin(out Boolean valable)
        {
            valable = false;
            try
            {
                var fichier = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nexus", "trinite", "quota-claude.json");
                if (!File.Exists(fichier))
                {
                    return null;
                }

                using var doc = JsonDocument.Parse(File.ReadAllText(fichier));
                var r = doc.RootElement;
                if (r.GetProperty("session").GetString() is not String session || !this._clisClaude.Contains(session))
                {
                    return null;
                }

                var ecrit = r.TryGetProperty("at", out var at) && at.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(at.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var e) ? e.UtcDateTime : DateTime.MinValue;
                (Double? Part, DateTime? Remise) Fenetre(String nom, TimeSpan duree)
                {
                    if (!r.TryGetProperty(nom, out var f) || f.ValueKind != JsonValueKind.Object)
                    {
                        return (null, null);
                    }

                    if (!f.TryGetProperty("remise", out var rm) || rm.ValueKind != JsonValueKind.String)
                    {
                        // Sans date de remise : valable une fenêtre au plus après la mesure.
                        return (DateTime.UtcNow - ecrit < duree ? f.GetProperty("pourcent").GetDouble() / 100 : null, null);
                    }

                    var remise = DateTimeOffset.TryParse(rm.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.UtcDateTime : (DateTime?)null;
                    return (remise is { } quand && quand < DateTime.UtcNow ? 0 : f.GetProperty("pourcent").GetDouble() / 100, remise);
                }

                valable = true;
                var cinq = Fenetre("cinq", TimeSpan.FromHours(5));
                var semaine = Fenetre("semaine", TimeSpan.FromDays(7));
                return cinq.Part is not null || semaine.Part is not null
                    ? new(cinq.Part, semaine.Part) { FinCinq = cinq.Remise, FinSemaine = semaine.Remise } : null;
            }
            catch (Exception)
            {
                return null; // en cours d'écriture : relu au tour suivant
            }
        }

        private static String CompteClaude(String config)
        {
            try
            {
                using var doc = JsonDocument.Parse(Debut(config, 4 << 20));
                return doc.RootElement.TryGetProperty("lastKnownAccountUuid", out var compte) ? compte.GetString() : null;
            }
            catch (Exception)
            {
                return null; // absent ou en cours d'écriture : tous les comptes, comme avant
            }
        }
    }
}

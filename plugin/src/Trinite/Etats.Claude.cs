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
            this._sessionClaudeAffiche = null;
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
                var nouveauCompte = CompteClaude(config);
                if (nouveauCompte != this._compteClaude.Compte)
                {
                    this._quotas[0] = null;
                    this._perimetreQuotaClaude = null;
                }
                this._compteClaude = (ecritConfig, nouveauCompte);
            }

            this._dossierClaude = Path.GetDirectoryName(racine);
            if (this._compteClaude.Compte is String compte && Directory.Exists(Path.Combine(racine, compte)))
            {
                racine = Path.Combine(racine, compte);
            }

            this._sessionClaudeAffiche = Uia.SessionClaudeAffichee();
            var maintenantUtc = DateTime.UtcNow;
            // remplacés d'un bloc à la fin : jamais lus à moitié remplis. Organisation = dossier de la fiche sous le compte
            // (claude-code-sessions\<compte>\<org>\local_*.json ; « <compte>\<org> » quand le compte est inconnu).
            var orgParCli = new Dictionary<String, String>(StringComparer.Ordinal);
            String orgAffichee = null;
            var brutes = new List<(Conversation Conv, String Cwd)>();
            foreach (var fichier in Directory.EnumerateFiles(racine, "local_*.json", SearchOption.AllDirectories))
            {
                var ecrit = File.GetLastWriteTimeUtc(fichier);
                if (maintenantUtc - ecrit > Fenetre)
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
                var cwd = Champ(meta, "cwd");
                var activite = Instant(Champ(meta, "lastActivityAt")) ?? ecrit;
                var regard = Instant(Champ(meta, "lastFocusedAt")) ?? DateTime.MinValue;
                var cli = Champ(meta, "cliSessionId");
                var org = Path.GetRelativePath(racine, Path.GetDirectoryName(fichier));
                if (id == this._sessionClaudeAffiche) { orgAffichee = org; }
                if (cli is not null)
                {
                    orgParCli[cli] = org;
                }

                var hook = cli is not null ? Hook("claude", cli) : null;
                var etat = hook is { } h ? Traduire(h.Etat, h.Quand, Instant(Champ(meta, "lastTurnEdgeAt"))) : Etat.Repos;
                var depuis = hook?.Quand ?? activite;
                var vu = regard > depuis || this.Vu(0, id, depuis);
                // Regardée : sa question, sa fin ou son erreur retourne au repos (le « ? » s'efface dès l'arrivée, 29/10).
                var conv = new Conversation(0, id, titre, etat is Etat.Fini or Etat.Erreur or Etat.Attend && vu ? Etat.Repos : etat, depuis, activite, vu);
                brutes.Add((conv, cwd));
            }

            this._orgParCliClaude = orgParCli;
            this._orgQuotaClaude = OrganisationQuotaClaude(orgParCli, orgAffichee);

            var cwdActif = this._sessionClaudeAffiche is not null
                ? brutes.FirstOrDefault(b => b.Conv.Id == this._sessionClaudeAffiche).Cwd
                : null;

            foreach (var (conv, cwd) in brutes)
            {
                if (ConversationClaudeActive(conv, cwd, this._sessionClaudeAffiche, cwdActif, maintenantUtc))
                {
                    yield return conv;
                }
            }
        }

        private static readonly TimeSpan FenetreReposClaude = TimeSpan.FromHours(6);

        public static Boolean ConversationClaudeActive(Conversation c, String cwd, String sessionAffichee, String cwdActif, DateTime maintenantUtc, TimeSpan? fenetreRepos = null)
        {
            if (c.Etat is not Etat.Repos)
            {
                return true;
            }

            // Le même titre peut revenir chaque jour : seule la session réellement affichée échappe au délai de repos.
            if (sessionAffichee is not null && c.Id == sessionAffichee)
            {
                return true;
            }

            if (maintenantUtc - c.Activite > (fenetreRepos ?? FenetreReposClaude))
            {
                return false;
            }

            if (cwdActif is not null && cwd is not null && !String.Equals(NormaliserDossier(cwd), NormaliserDossier(cwdActif), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private static String NormaliserDossier(String d) => String.IsNullOrWhiteSpace(d) ? null : d.Replace('/', '\\').TrimEnd('\\');

        // Sessions récentes du compte Claude connecté (cliSessionId → organisation), relevées par Claude().
        private IReadOnlyDictionary<String, String> _orgParCliClaude = new Dictionary<String, String>();
        private String _sessionClaudeAffiche;
        private String _orgQuotaClaude;
        private String _dossierClaude;

        // Organisation dont le pad montre le quota : celle de la conversation affichée ; sans elle, l'organisation unique des
        // sessions récentes du compte (plusieurs : aucune devinée).
        internal static String OrganisationQuotaClaude(IReadOnlyDictionary<String, String> orgParCli, String orgAffichee)
        {
            if (orgAffichee is not null)
            {
                return orgAffichee;
            }

            var orgs = orgParCli.Values.Distinct(StringComparer.Ordinal).Take(2).ToList();
            return orgs.Count == 1 ? orgs[0] : null;
        }

        // Le quota est celui du compte et de l'organisation, pas d'une conversation : la publication de n'importe quelle
        // session récente du compte connecté vaut si elle est de la même organisation (une autre organisation ou un autre
        // compte a son propre quota : jamais mélangés, voir l'historique plus bas).
        internal static Boolean PublicationQuotaClaudeAcceptee(String session, IReadOnlyDictionary<String, String> orgParCli, String orgQuota) =>
            session is not null && orgQuota is not null && orgParCli.TryGetValue(session, out var org) && org == orgQuota;

        // Quota écrit par le plugin Claude « Nexus Pad » (facultatif, non publié ici) à chaque réponse : les deux fenêtres exactes,
        // sans réseau ni menu ouvert. Gardé seulement s'il vient d'une session récente du compte connecté et de l'organisation
        // affichée ; une fenêtre dont la remise à zéro est passée vaut 0 %. Null : pas de plugin, autre compte/organisation,
        // ou fichier illisible. valable : publication lisible acceptée ; elle fait alors foi, même périmée (null : jauge grise).
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
                if (r.GetProperty("session").GetString() is not String session || !PublicationQuotaClaudeAcceptee(session, this._orgParCliClaude, this._orgQuotaClaude))
                {
                    return null;
                }

                var ecrit = r.TryGetProperty("at", out var at) && at.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(at.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var e) ? e.UtcDateTime : DateTime.MinValue;
                Double? Fenetre(String nom, TimeSpan duree)
                {
                    if (!r.TryGetProperty(nom, out var f) || f.ValueKind != JsonValueKind.Object)
                    {
                        return null;
                    }

                    if (!f.TryGetProperty("remise", out var rm) || rm.ValueKind != JsonValueKind.String)
                    {
                        // Sans date de remise : valable une fenêtre au plus après la mesure.
                        return DateTime.UtcNow - ecrit < duree ? f.GetProperty("pourcent").GetDouble() / 100 : null;
                    }

                    var remise = f.TryGetProperty("remise", out var m) && m.ValueKind == JsonValueKind.String ? (DateTimeOffset.TryParse(m.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.UtcDateTime : (DateTime?)null) : null;
                    return remise is { } quand && quand < DateTime.UtcNow ? 0 : f.GetProperty("pourcent").GetDouble() / 100;
                }

                valable = true;
                return Fenetre("cinq", TimeSpan.FromHours(5)) is var cinq && Fenetre("semaine", TimeSpan.FromDays(7)) is var semaine && (cinq is not null || semaine is not null)
                    ? new(cinq, semaine) : null;
            }
            catch (Exception)
            {
                return null; // en cours d'écriture : relu au tour suivant
            }
        }

        // Quota tenu sur disque par Claude de bureau (plan-usage-history.json) : historique officiel des deux
        // fenêtres exactes (5 h et semaine), tenu par l'application elle-même sans réseau ni interface.
        public static Quota QuotaHistoriqueClaude(String json, DateTime maintenantUtc, String org = null)
        {
            if (String.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("samples", out var samples) || samples.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                var len = samples.GetArrayLength();
                if (len == 0)
                {
                    return null;
                }

                JsonElement? retenu = null;
                for (var i = len - 1; i >= 0; i--)
                {
                    var s = samples[i];
                    if (s.ValueKind == JsonValueKind.Object && s.TryGetProperty("u", out var u) && u.ValueKind == JsonValueKind.Object
                        && (u.TryGetProperty("fh", out _) || u.TryGetProperty("sd", out _))
                        && (org is null || (s.TryGetProperty("org", out var o) && o.ValueKind == JsonValueKind.String && o.GetString() == org)))
                    {
                        retenu = s;
                        break;
                    }
                }

                if (retenu is not { } ech || !ech.TryGetProperty("u", out var usage))
                {
                    return null;
                }

                var quand = ech.TryGetProperty("t", out var t) && t.TryGetInt64(out var ms)
                    ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime
                    : DateTime.MinValue;

                Double? Fenetre(String nom, TimeSpan duree)
                {
                    if (!usage.TryGetProperty(nom, out var v) || !v.TryGetDouble(out var part) || Double.IsNaN(part) || Double.IsInfinity(part))
                    {
                        return null;
                    }

                    if (quand != DateTime.MinValue && maintenantUtc - quand > duree)
                    {
                        return 0;
                    }

                    return Math.Clamp(part / 100.0, 0, 1);
                }

                var cinq = Fenetre("fh", TimeSpan.FromHours(5));
                var semaine = Fenetre("sd", TimeSpan.FromDays(7));
                return cinq is not null || semaine is not null ? new(cinq, semaine) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private Quota QuotaClaudeHistorique()
        {
            try
            {
                var dossier = DossierHistoriqueClaude();
                if (dossier is null)
                {
                    return null;
                }

                return QuotaHistoriqueClaudeCompte(dossier, this._compteClaude.Compte, DateTime.UtcNow, this._sessionClaudeAffiche);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static String DossierHistoriqueClaude() => Directory.Exists(Path.Combine(Local, "Claude-Data"))
            ? Directory.EnumerateDirectories(Path.Combine(Local, "Claude-Data")).FirstOrDefault(d => File.Exists(Path.Combine(d, "plan-usage-history.json")))
            : null;

        // Périmètre prouvé par la fiche affichée ; sans elle, celui de l'organisation retenue pour la publication du quota.
        private String PerimetreQuotaClaude() => PerimetreQuotaClaudeCompte(DossierHistoriqueClaude(), this._compteClaude.Compte, this._sessionClaudeAffiche)
            ?? (this._orgQuotaClaude is String org && this._dossierClaude is String dossier
                ? Path.GetFullPath(dossier) + "\0" + this._compteClaude.Compte + "\0" + org : null);

        internal static String PerimetreQuotaClaudeCompte(String dossier, String compte, String sessionAffichee = null)
        {
            var org = OrganisationHistoriqueClaude(dossier, compte, sessionAffichee);
            return org is null ? null : Path.GetFullPath(dossier) + "\0" + compte + "\0" + org;
        }

        // Une nouvelle lecture partielle peut compléter seulement une jauge du même périmètre prouvé.
        internal static Quota FusionnerQuotaClaude(Quota precedente, Quota lue, String ancienPerimetre, String perimetre)
        {
            var garde = perimetre is not null && String.Equals(ancienPerimetre, perimetre, StringComparison.Ordinal) ? precedente : null;
            return lue is null ? garde : new(lue.Cinq ?? garde?.Cinq, lue.Semaine ?? garde?.Semaine);
        }

        private static String OrganisationHistoriqueClaude(String dossier, String compte, String sessionAffichee)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(dossier) || String.IsNullOrWhiteSpace(compte) || compte is "." or ".." || Path.GetFileName(compte) != compte
                    || String.IsNullOrWhiteSpace(sessionAffichee) || Path.GetFileName(sessionAffichee) != sessionAffichee)
                {
                    return null;
                }

                var sessions = Path.Combine(dossier, "claude-code-sessions", compte);
                if (!Directory.Exists(sessions)) { return null; }
                String retenue = null;
                foreach (var org in Directory.EnumerateDirectories(sessions))
                {
                    var fiche = Path.Combine(org, sessionAffichee + ".json");
                    if (!File.Exists(fiche)) { continue; }
                    using var doc = JsonDocument.Parse(File.ReadAllText(fiche));
                    if (!doc.RootElement.TryGetProperty("sessionId", out var id) || id.ValueKind != JsonValueKind.String || id.GetString() != sessionAffichee)
                    {
                        continue;
                    }
                    if (retenue is not null) { return null; } // deux fiches homonymes : aucune organisation devinée
                    retenue = Path.GetFileName(org);
                }
                return retenue;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // L'historique mélange les organisations de l'application. L'ID exact affiché et sa fiche du compte courant
        // prouvent l'organisation ; sans cette preuve, le lecteur courant UIA reste le secours.
        internal static Quota QuotaHistoriqueClaudeCompte(String dossier, String compte, DateTime maintenantUtc, String sessionAffichee = null)
        {
            try
            {
                var org = OrganisationHistoriqueClaude(dossier, compte, sessionAffichee);
                if (org is null) { return null; }
                var fichier = Path.Combine(dossier, "plan-usage-history.json");
                return QuotaHistoriqueClaude(File.ReadAllText(fichier), maintenantUtc, org);
            }
            catch (Exception)
            {
                return null;
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

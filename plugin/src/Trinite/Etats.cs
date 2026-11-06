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

    // Lit les conversations actives des trois IA, l'écran et le téléphone ; appelé chaque seconde hors du fil de dessin.
    public sealed partial class Observateur
    {
#if !POSTE
        public static readonly String[] Agents = ["claude", "codex", "antigravity"];
        public static readonly String[] Processus = ["claude", "ChatGPT", "Antigravity"];
#endif

        // Édition PC Dashboard : aucun agent lu, aucun de leurs fichiers ouvert, rien d'eux compilé.
        private static readonly String Maison = Environment.GetEnvironmentVariable("USERPROFILE") ?? "";
        private static readonly String Local = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";
#if !POSTE
        private static readonly String DossierEtats = Path.Combine(Maison, ".nexus", "trinite");
        private static readonly String DossierCodex = Path.Combine(Maison, ".codex");
        private static readonly String DossierRelais = Path.Combine(Maison, ".nexus", "relay", "v1");
        private static readonly TimeSpan Fenetre = TimeSpan.FromDays(3); // au-delà, une conversation n'est plus « active »
        private static readonly System.Net.Http.HttpClient CodexLb = new() { Timeout = TimeSpan.FromSeconds(2) };

        private readonly ConcurrentDictionary<String, DateTime> _vues = ChargerVues(); // agent:id → dernier regard de l'utilisateur
        private Boolean _vuesChangees;
        private DateTime _vuesGardees = DateTime.MinValue;

        // Regards gardés sur disque (24 h) : un redémarrage du plugin ou de Logi ne rallume pas une fin déjà vue (05/11).
        private static String FichierVues => Path.Combine(Local, "NexusKeypad", "vues.json");

        private static ConcurrentDictionary<String, DateTime> ChargerVues()
        {
            var vues = new ConcurrentDictionary<String, DateTime>();
            try
            {
                if (File.Exists(FichierVues))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(FichierVues));
                    foreach (var p in doc.RootElement.EnumerateObject())
                    {
                        if (p.Value.TryGetDouble(out var ms))
                        {
                            var quand = DateTimeOffset.FromUnixTimeMilliseconds((Int64)ms).UtcDateTime;
                            if (DateTime.UtcNow - quand < TimeSpan.FromHours(24))
                            {
                                vues[p.Name] = quand;
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // illisible : on repart des regards de l'appli elle-même
            }

            return vues;
        }

        // Non-lus de l'appli Codex (.codex-global-state.json, electron-thread-read-state-v1.unreadByIdentity) : une
        // conversation qui en sort a été lue dans Codex, même sans son port ; seul ce passage compte (une conversation
        // jamais marquée non lue garde l'alerte du pad, comme avant). Format absent ou changé : rien n'est déduit.
        private HashSet<String> _nonLusCodex;
        private DateTime _nonLusCodexEcrit = DateTime.MinValue;

        private void SuivreNonLusCodex()
        {
            var fichier = Path.Combine(DossierCodex, ".codex-global-state.json");
            try
            {
                var ecrit = File.Exists(fichier) ? File.GetLastWriteTimeUtc(fichier) : DateTime.MinValue;
                if (ecrit == this._nonLusCodexEcrit)
                {
                    return;
                }

                this._nonLusCodexEcrit = ecrit;
                var nonLus = ecrit == DateTime.MinValue ? null : NonLusCodex(File.ReadAllText(fichier));
                if (this._nonLusCodex is not null && nonLus is not null)
                {
                    foreach (var id in this._nonLusCodex.Where(i => !nonLus.Contains(i)))
                    {
                        this.MarquerVu(1, id);
                    }

                    // « Marquer comme non lu » (Ctrl+Shift+U) : la fin déjà vue se rallume sur le pad.
                    foreach (var id in nonLus.Where(i => !this._nonLusCodex.Contains(i)))
                    {
                        this._vuesChangees |= this._vues.TryRemove($"1:{id}", out _);
                    }
                }

                this._nonLusCodex = nonLus;
            }
            catch (Exception)
            {
                // fichier en cours d'écriture : relu au prochain changement
                this._nonLusCodexEcrit = DateTime.MinValue;
            }
        }

        internal static HashSet<String> NonLusCodex(String json)
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("electron-thread-read-state-v1", out var etat) || etat.ValueKind != JsonValueKind.Object
                || !etat.TryGetProperty("unreadByIdentity", out var parIdentite) || parIdentite.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var ids = new HashSet<String>(StringComparer.Ordinal);
            foreach (var identite in parIdentite.EnumerateObject().Where(i => i.Value.ValueKind == JsonValueKind.Object))
            {
                foreach (var hote in identite.Value.EnumerateObject().Where(h => h.Value.ValueKind == JsonValueKind.Array))
                {
                    foreach (var id in hote.Value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String))
                    {
                        ids.Add(id.GetString());
                    }
                }
            }

            return ids;
        }

        private void GarderVues()
        {
            if (!this._vuesChangees || DateTime.UtcNow - this._vuesGardees < TimeSpan.FromSeconds(5))
            {
                return;
            }

            try
            {
                var limite = DateTime.UtcNow - TimeSpan.FromHours(24);
                var json = JsonSerializer.Serialize(this._vues.Where(v => v.Value > limite)
                    .ToDictionary(v => v.Key, v => new DateTimeOffset(v.Value, TimeSpan.Zero).ToUnixTimeMilliseconds()));
                Directory.CreateDirectory(Path.GetDirectoryName(FichierVues));
                var temporaire = FichierVues + ".tmp";
                File.WriteAllText(temporaire, json);
                File.Move(temporaire, FichierVues, overwrite: true);
                this._vuesChangees = false;
                this._vuesGardees = DateTime.UtcNow;
            }
            catch (Exception)
            {
                // réessayé au tour suivant
            }
        }
        private readonly Dictionary<String, (DateTime Ecrit, String Meta)> _cacheClaude = [];
#endif
        private TimeSpan _delaiEcran = TimeSpan.FromMinutes(10); // relu auprès de Windows (DelaiEcran)
        private DateTime _delaiLu = DateTime.MinValue;
#if !POSTE
        private (DateTime Ecrit, Dictionary<String, String> Noms) _indexCodex = (DateTime.MinValue, []);
        private List<(String Id, String Titre, DateTime Activite, Boolean Selection)> _lignesAgy = [];
        private readonly Dictionary<String, String> _sujets = [];
        private readonly Dictionary<String, Int32> _sources = [];
        private readonly Quota[] _quotas = [null, null, QuotaAntigravity.Garde(out _)];
        private readonly Process[] _lancees = new Process[3];
        private readonly DateTime[] _recherche = new DateTime[3];
        private readonly DateTime[] _vueLancee = new DateTime[3];
        private DateTime _quotaAgyLu = QuotaAntigravity.Lu(); // dernière lecture réussie de sa page Settings › Models
        private DateTime _quotaAgyEssai = DateTime.MinValue;
        private String _selectionCodex;
        private String _selectionAgy;
        private Int64 _tour;
        private (DateTime Ecrit, String Compte) _compteClaude = (DateTime.MinValue, null);
        private String _perimetreQuotaClaude;
#endif

        public Telephone Telephone { get; } = new();

#if !POSTE
        // Relevé forcé (pilote) : la page Settings › Models est ouverte un instant, comme au relevé automatique.
        public Quota ReleverAntigravity()
        {
            if (!System.Threading.Monitor.TryEnter(Interface.Verrou, 5000))
            {
                return null; // un geste ou un autre relevé occupe l'interface
            }

            Quota quota;
            try
            {
                quota = QuotaAntigravity.Lire(ouvrir: true);
            }
            finally
            {
                System.Threading.Monitor.Exit(Interface.Verrou);
            }

            if (quota is not null)
            {
                this._quotas[2] = quota;
                this._quotaAgyLu = DateTime.UtcNow;
                QuotaAntigravity.Garder(quota);
            }

            return quota;
        }

        // Dictée lancée par le pad : suivie dans son appli seulement, jusqu'à ce qu'elle s'arrête ; la touche reste rouge
        // pendant ce temps. 3 s de grâce au départ : le bouton d'arrêt met un instant à paraître.
        private DateTime _dicteeLancee = DateTime.MinValue;
        private Boolean _dicteeSuivie;
        private Int32 _dicteeIa = -1;

        public void SuivreDictee(Int32 ia)
        {
            this._dicteeLancee = DateTime.UtcNow;
            this._dicteeIa = ia;
            this._dicteeSuivie = true;
        }

        // Suivie tant que son bouton d'arrêt est vu ; arrêtée quand son appli est lue sans arrêt, ou fermée (une lecture
        // muette, port qui ne répond pas, ne vaut pas arrêt), et au plus 30 min.
        private Boolean DicteeEnCours()
        {
            if (!this._dicteeSuivie)
            {
                return false;
            }

            if (this._dicteeIa >= 0 && !Connecteurs.EvenementsActifs(Agents[this._dicteeIa]))
            {
                this._dicteeSuivie = false;
                return false;
            }

            Boolean? Lu(JsonElement? r) => r is { ValueKind: JsonValueKind.True } ? true : r is { ValueKind: JsonValueKind.False } ? false : null;
            Boolean? claude = false, codex = false, agy = false;
            try
            {
                claude = this._dicteeIa == 0 && this.Lancee(0) ? Uia.DicteeClaudeObservee() : false;
            }
            catch (Exception)
            {
                claude = null; // illisible ce tour-ci
            }

            codex = this._dicteeIa != 1 || !this.Lancee(1) ? false
                : (Lu(Cdp.Evaluer(Cdp.PortCodex, Js.DicteeEnCours)),
                   Cdp.PageOuverte(Cdp.PortCodex, Cdp.BulleVocaleCodex) switch
                   {
                       false => false, // pas de bulle : pas de chat vocal
                       true => Lu(Cdp.Evaluer(Cdp.PortCodex, Js.DicteeEnCours, Cdp.BulleVocaleCodex)),
                       null => null,
                   }) switch
                {
                    (true, _) or (_, true) => true,
                    (false, false) => false,
                    _ => null, // une des deux pages n'a pas répondu
                };
            agy = this._dicteeIa == 2 && this.Lancee(2) ? Lu(Cdp.Evaluer(Cdp.PortAntigravity, Js.DicteeEnCours)) : false;
            var enCours = claude == true || codex == true || agy == true;
            var arreteLu = claude == false && codex == false && agy == false;
            if ((!enCours && arreteLu && DateTime.UtcNow - this._dicteeLancee > TimeSpan.FromSeconds(3))
                || DateTime.UtcNow - this._dicteeLancee > TimeSpan.FromMinutes(30))
            {
                this._dicteeSuivie = false;
            }

            return enCours || this._dicteeSuivie;
        }

        private readonly Object _verrouListes = new(); // lecture des journaux : jamais deux à la fois (caches partagés)

        // La conversation affichée dans l'appli : titre de l'en-tête pour Claude, identifiant sélectionné pour les deux autres.
        public String ConversationAffichee(Int32 agent, Boolean relire = true)
        {
            if (agent == 0)
            {
                return Uia.TitreClaudeAffiche();
            }

            if (relire)
            {
                this.LireCdp();
            }

            return agent == 1 ? this._selectionCodex : this._selectionAgy;
        }

#endif
        // Un appui du pad, noté par le moteur : la page Models d'Antigravity n'est jamais ouverte pendant un geste ni dans
        // les 10 s qui suivent (une interface ouverte prenait le clavier du geste, 23/10).
        public void NoterGeste() => this._dernierGeste = DateTime.UtcNow;

        private DateTime _dernierGeste = DateTime.MinValue;

        private Boolean SansGesteRecent() => DateTime.UtcNow - this._dernierGeste > TimeSpan.FromSeconds(10);

#if !POSTE
        public void MarquerVu(Int32 agent, String id)
        {
            this._vues[$"{agent}:{id}"] = DateTime.UtcNow;
            this._vuesChangees = true;
        }

        public Instantane Lire()
        {
            this._tour++;
            var premier = PremierPlan();
            if (this._tour % 3 == 1)
            {
                this.LireCdp(); // listes d'interface toutes les 3 s : un aller-retour WebSocket chacune
            }

            var ouverte = Enumerable.Range(0, Processus.Length).Select(this.Lancee).ToArray();
            if (premier == Processus[1] && this._selectionCodex is not null)
            {
                this.MarquerVu(1, this._selectionCodex);
            }

            this.SuivreNonLusCodex();
            var selAgy = this._selectionAgy;
            if (premier == Processus[2] && selAgy is not null)
            {
                this.MarquerVu(2, selAgy);
            }

            List<Conversation>[] listes;
            lock (this._verrouListes)
            {
                listes = new[]
                {
                    Connecteurs.EvenementsActifs("claude") ? this.Claude().ToList() : [],
                    Connecteurs.EvenementsActifs("codex") ? this.Codex().ToList() : [],
                    Connecteurs.EvenementsActifs("antigravity") ? this.Antigravity().ToList() : [],
                };
            }

            this.GarderVues();
            var conversations = listes.Select(l => l
                .OrderByDescending(Rang)
                .ThenByDescending(c => c.Activite)
                .ToArray()).ToArray();
            // Tout ce que l'appli montre, chats du relais compris (rangés par lui après 12 h sans message).
            // Appli fermée : 0 conversation affichée (pas d'infos d'une IA non lancée).
            var ouvertes = listes.Select((l, i) => ouverte[i] ? l.Count : 0).ToArray();

            // Quotas : Claude par le fichier du plugin Nexus Pad, Codex par Codex LB (10 s ; sans lui, ses journaux),
            // Antigravity par sa page Settings › Models (détails plus bas) ; la dernière valeur lue reste affichée entre
            // deux lectures.
            // Chaque IA à part : l'échec de l'une ne prive pas les autres de leur lecture.
            void Quota(Int32 agent, Boolean maintenant, Func<Quota> lire)
            {
                try
                {
                    this._quotas[agent] = maintenant ? lire() ?? this._quotas[agent] : this._quotas[agent];
                }
                catch (Exception e)
                {
                    PluginLog.Error(e, $"quota de {Agents[agent]}");
                }
            }

            // Claude : le fichier du plugin Nexus Pad, toutes les 5 s, même Claude fermé (sa remise à zéro compte) ; sans lui,
            // le nom du bouton Utilisation, Claude ouvert.
            var eteint = this.EcranEteint();
            if (Connecteurs.EvenementsActifs("claude") && this._tour % 5 == 1)
            {
                try
                {
                    var perimetre = this.PerimetreQuotaClaude();
                    this._quotas[0] = FusionnerQuotaClaude(this._quotas[0], null, this._perimetreQuotaClaude, perimetre);
                    this._perimetreQuotaClaude = perimetre;
                    var publie = this.QuotaClaudePlugin(out var valable);
                    var historique = this.QuotaClaudeHistorique(out var dateHistorique);
                    var lu = ChoisirQuotaClaude(perimetre is not null && valable ? publie : null, historique, dateHistorique, DateTime.UtcNow,
                        ouverte[0] ? () => Uia.QuotaClaude(this._sessionClaudeAffiche) : null);
                    this._quotas[0] = FusionnerQuotaClaude(this._quotas[0], lu, perimetre, perimetre);
                }
                catch (Exception e)
                {
                    PluginLog.Error(e, "quota de Claude");
                }
            }
            Quota(1, Connecteurs.EvenementsActifs("codex") && ouverte[1] && this._tour % 10 == 1, this.QuotaCodex);
            // Antigravity : sa page Settings › Models, comme un humain (fenêtre ouverte seulement : sans elle, rien à lire).
            // Lue sans rien toucher si l'utilisateur l'a ouverte (toutes les 20 s) ; ouverte un instant au plus tous les quarts
            // d'heure, échecs compris (un essai toutes les 2 min refaisait l'appel de quota à Google, 25/10), poste déverrouillé. Antigravity devant : l'utilisateur sans rien toucher
            // depuis 30 s ; derrière, sa page ne gêne personne et s'ouvre aussitôt (attendre 1 min d'inactivité ailleurs ne
            // passait presque jamais en journée : quota de plus de 5 h, jauge vide, 24/10).
            var agyDevant = premier == Processus[2];
            var ouvrirAgy = !Verrouille() && this.SansGesteRecent() && (!agyDevant || Inactif() > TimeSpan.FromSeconds(30))
                && DateTime.UtcNow - this._quotaAgyLu > TimeSpan.FromMinutes(15) && DateTime.UtcNow - this._quotaAgyEssai > TimeSpan.FromMinutes(15);
            Quota(2, Connecteurs.EvenementsActifs("antigravity") && ouverte[2] && (ouvrirAgy || this._tour % 20 == 7), () =>
            {
                if (ouvrirAgy)
                {
                    this._quotaAgyEssai = DateTime.UtcNow;
                }

                var ouvrir = ouvrirAgy && System.Threading.Monitor.TryEnter(Interface.Verrou);
                try
                {
                    var quota = QuotaAntigravity.Lire(ouvrir);
                    if (quota is not null)
                    {
                        this._quotaAgyLu = DateTime.UtcNow;
                        QuotaAntigravity.Garder(quota);
                    }

                    return quota;
                }
                finally
                {
                    if (ouvrir)
                    {
                        System.Threading.Monitor.Exit(Interface.Verrou);
                    }
                }
            });
            // Chaque groupe repart à zéro à son heure, même sans nouveau relevé (Antigravity fermé, ou relevé suivant dans un
            // quart d'heure) ; plus de 5 h sans relevé avec Antigravity ouvert (il a pu servir) : la valeur ne dit plus rien.
            this._quotas[2] = ouverte[2] && DateTime.UtcNow - this._quotaAgyLu > TimeSpan.FromHours(5) ? null
                : QuotaAntigravity.Actuel(this._quotas[2], this._quotaAgyLu);

            var dictee = this.DicteeEnCours();
            var quotasVisibles = Enumerable.Range(0, Processus.Length)
                .Select(i => ouverte[i] && Connecteurs.EvenementsActifs(Agents[i]) ? this._quotas[i] : null)
                .ToArray();
            return new(conversations, ouvertes, quotasVisibles, ouverte, eteint, this.Telephone.Derniere, Array.IndexOf(Processus, premier))
            {
                Dictee = dictee,
                DicteeIa = dictee ? this._dicteeIa : -1,
                Connexions = ConnexionPad.Lire(),
            };
        }

        // Ordre d'une touche : ce qui appelle l'utilisateur (question, puis fin), puis ce qui travaille pour lui, puis le reste.
        internal static Int32 Rang(Conversation c) => c switch
        {
            { Alerte: true, Etat: Etat.Attend or Etat.Autorisation or Etat.Erreur } => 5,
            { Alerte: true } => 4,
            { Etat: Etat.Travaille, Relais: false } => 3,
            { Etat: Etat.Travaille } => 2,
            { Relais: false } => 1,
            _ => 0,
        };

        private Boolean Vu(Int32 agent, String id, DateTime depuis) => this._vues.TryGetValue($"{agent}:{id}", out var quand) && quand > depuis;

        // État noté par les hooks (nexuskeypad-hook.exe) pour une session ; null si aucun.
        private static (String Etat, DateTime Quand)? Hook(String agent, String session)
        {
            try
            {
                var fichier = Path.Combine(DossierEtats, agent, session + ".json");
                if (!File.Exists(fichier))
                {
                    return null;
                }

                // Partage complet : le hook remplace le fichier pendant qu'on le lit, sans échec ni perte.
                using var flux = new FileStream(fichier, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var doc = JsonDocument.Parse(flux);
                var quand = DateTimeOffset.FromUnixTimeMilliseconds((Int64)(doc.RootElement.GetProperty("at").GetDouble() * 1000)).UtcDateTime;
                return (doc.RootElement.GetProperty("state").GetString(), quand);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Uuid de la dernière réponse noté par le Stop de Claude (« reply ») ; vide si aucun.
        private static String HookReponse(String agent, String session)
        {
            try
            {
                using var flux = new FileStream(Path.Combine(DossierEtats, agent, session + ".json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var doc = JsonDocument.Parse(flux);
                return doc.RootElement.TryGetProperty("reply", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        // finTour : dernier début ou fin de tour noté par l'appli (lastTurnEdgeAt de Claude). Un arrêt par Échap n'envoie
        // aucun hook : un « run » plus ancien que ce bord est un tour fini, même sans Stop (31/10).
        internal static Etat Traduire(String etat, DateTime quand, DateTime? finTour = null) => (etat, DateTime.UtcNow - quand) switch
        {
            ("run" or "ask", _) when finTour > quand => Etat.Repos,
            ("wait", var age) when age < TimeSpan.FromHours(12) => Etat.Attend,
            ("perm", var age) when age < TimeSpan.FromHours(12) => Etat.Autorisation,
            ("error", var age) when age < TimeSpan.FromHours(12) => Etat.Erreur,
            ("limit", var age) when age < TimeSpan.FromHours(5) => Etat.Limite,
            // Une commande longue (compilation, essais) ne donne aucun signe pendant des dizaines de minutes : 2 h.
            ("run" or "ask", var age) when age < TimeSpan.FromHours(2) => Etat.Travaille,
            ("done", var age) when age < TimeSpan.FromHours(12) => Etat.Fini,
            _ => Etat.Repos,
        };

        public static Int32 SourceDuNomDeFichier(String nom)
        {
            var morceaux = nom.Split('.');
            if (morceaux.Length >= 2)
            {
                return morceaux[1].ToLowerInvariant() switch
                {
                    "claude" => 0,
                    "codex" => 1,
                    "antigravity" or "agy" => 2,
                    _ => -1,
                };
            }
            return -1;
        }

        // Conversation ouverte par le relais : son titre est le début du message de réveil (« NexusRelay » depuis
        // NexusRelay 1.13.2, « Nexus Relay » avant). Le seul nom ne suffit pas : une session de travail sur le produit,
        // « NexusRelay : sujet Codex bloqué… », passait pour un échange du relais et animait le pad (06/11).
        private static readonly String[] DebutsDuReveil =
        [
            "NexusRelay: message from a peer", "NexusRelay : message d'un pair",
            "Nexus Relay: message from a peer", "Nexus Relay : message d'un pair",
        ];
        public static Boolean TitreDuRelais(String titre) =>
            DebutsDuReveil.Any(debut => titre.StartsWith(debut, StringComparison.Ordinal));

        // Relu chaque seconde par conversation du relais : le résultat est gardé tant que le dossier des messages n'a pas
        // changé (un message ajouté change sa date), 30 s au plus. Avant, chaque relevé énumérait et datait tous les
        // messages (profil du 06/11/2026).
        private static readonly Dictionary<String, (DateTime Dossier, DateTime Lu, Int32 Source)> SourcesLues = new();

        public static Int32 SourcePourSujet(String sujet, Int32 defaut = 0)
        {
            try
            {
                var messages = Path.Combine(DossierRelais, "messages");
                if (!Directory.Exists(messages))
                {
                    return defaut;
                }

                var dossier = Directory.GetLastWriteTimeUtc(messages);
                var maintenant = DateTime.UtcNow;
                lock (SourcesLues)
                {
                    if (SourcesLues.TryGetValue(sujet, out var lu) && lu.Dossier == dossier && maintenant - lu.Lu < TimeSpan.FromSeconds(30))
                    {
                        return lu.Source >= 0 ? lu.Source : defaut;
                    }
                }

                var dernier = Directory.EnumerateFiles(messages, $"*.*.*.{sujet}.json")
                    .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                    .FirstOrDefault();
                var src = dernier is null ? -1 : SourceDuNomDeFichier(Path.GetFileNameWithoutExtension(dernier));
                lock (SourcesLues)
                {
                    SourcesLues[sujet] = (dossier, maintenant, src);
                }

                if (src >= 0)
                {
                    return src;
                }
            }
            catch (Exception)
            {
            }
            return defaut;
        }

#endif
        private static String Champ(String json, String nom)
        {
            var m = Regex.Match(json, $"\"{nom}\":(?:\"((?:[^\"\\\\]|\\\\.)*)\"|([^,}}\\]]+))");
            return !m.Success ? null : m.Groups[1].Success ? ChaineJson(m.Groups[1].Value) : m.Groups[2].Value.Trim();
        }

        // Contenu d'une chaîne JSON lue telle quelle entre ses guillemets, décodé en JSON (Regex.Unescape levait une
        // exception sur un échappement que JSON ignore, « \l », et arrêtait la lecture ; FsCheck, 27/10). Illisible : brut.
        private static String ChaineJson(String brut)
        {
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<String>("\"" + brut + "\"") ?? brut;
            }
            catch (System.Text.Json.JsonException)
            {
                return brut;
            }
        }

        private static DateTime? Instant(String ms) =>
            Int64.TryParse(ms, out var v) ? DateTimeOffset.FromUnixTimeMilliseconds(v).UtcDateTime : null;

        private static DateTime? Horodatage(String ligne)
        {
            var m = Regex.Match(ligne, "\"timestamp\":\"([^\"]+)\"");
            return m.Success && DateTime.TryParse(m.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : null;
        }

        private static String Debut(String chemin, Int32 octets)
        {
            try
            {
                using var flux = new FileStream(chemin, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var tampon = new Byte[Math.Min(octets, flux.Length)];
                flux.ReadExactly(tampon);
                return Encoding.UTF8.GetString(tampon);
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static IEnumerable<String> Lignes(String chemin, Int32 octets)
        {
            try
            {
                using var flux = new FileStream(chemin, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var taille = (Int32)Math.Min(octets, flux.Length);
                flux.Seek(-taille, SeekOrigin.End);
                var tampon = new Byte[taille];
                flux.ReadExactly(tampon);
                var lignes = Encoding.UTF8.GetString(tampon).Split('\n', StringSplitOptions.RemoveEmptyEntries);
                return taille < flux.Length ? lignes.Skip(1).ToArray() : lignes; // la première ligne peut être coupée
            }
            catch (Exception)
            {
                return [];
            }
        }
    }
}

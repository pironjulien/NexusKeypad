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

    // Antigravity : barre latérale lue par son port, état des hooks.
    public sealed partial class Observateur
    {
        // Antigravity : lignes de sa barre latérale (port 9227) + état des hooks par conversation. Les chats du relais
        // sont aussi connus par le relais lui-même (topics/<sujet>/agy.json) : la barre ne les montre plus quand sa
        // section Projects est repliée (24/10), ils restent vus tant qu'ils travaillent ou attendent.
        private IEnumerable<Conversation> Antigravity()
        {
            var sujets = SujetsAgy();
            var vues = new HashSet<String>();
            foreach (var (id, titre, activite, _) in this._lignesAgy)
            {
                vues.Add(id);
                var relais = TitreDuRelais(titre) || sujets.ContainsKey(id);
                var sujet = relais && sujets.TryGetValue(id, out var s) ? s : (relais ? "NexusRelay" : null);
                var source = relais && sujet is not null ? SourcePourSujet(sujet, 0) : -1;
                if (this.ConversationAgy(id, relais ? "Relais · " + sujet : titre, activite, relais, source) is { } c)
                {
                    yield return c;
                }
            }

            foreach (var (id, sujet) in sujets)
            {
                if (vues.Add(id))
                {
                    var source = SourcePourSujet(sujet, 0);
                    if (this.ConversationAgy(id, "Relais · " + sujet, DateTime.MinValue, true, source) is { } c)
                    {
                        yield return c;
                    }
                }
            }

            // Barre latérale illisible (Antigravity lancé sans son port) : les conversations connues par leurs hooks
            // depuis moins de 12 h restent suivies (travail, question, fin), titre de sa note. Avec la barre, elle seule
            // fait foi : une conversation supprimée n'y revient pas.
            if (!this._barreAgyLue)
            {
                foreach (var id in HooksRecents("antigravity", TimeSpan.FromHours(12)).Where(i => !vues.Contains(i)))
                {
                    if (this.ConversationAgy(id, NoteAntigravity(id).Titre ?? "Antigravity", DateTime.MinValue, false) is { Etat: not Etat.Repos } c)
                    {
                        yield return c;
                    }
                }
            }
        }

        private Boolean _barreAgyLue;

        // Sessions dont le hook a écrit depuis « age » (nom du fichier d'état).
        private static IEnumerable<String> HooksRecents(String agent, TimeSpan age)
        {
            var dossier = Path.Combine(DossierEtats, agent);
            if (!Directory.Exists(dossier))
            {
                yield break;
            }

            foreach (var f in new DirectoryInfo(dossier).EnumerateFiles("*.json"))
            {
                if (DateTime.UtcNow - f.LastWriteTimeUtc < age)
                {
                    yield return Path.GetFileNameWithoutExtension(f.Name);
                }
            }
        }

        private Conversation ConversationAgy(String id, String titre, DateTime activite, Boolean relais, Int32 source = -1)
        {
            var hook = Hook("antigravity", id);
            var etat = hook is { } h ? Traduire(h.Etat, h.Quand) : Etat.Repos;
            if (relais && etat is not (Etat.Travaille or Etat.Attend or Etat.Autorisation))
            {
                return null;
            }

            var note = NoteAntigravity(id);
            if (note.Archivee)
            {
                return null; // archivée dans Antigravity : ni montrée ni comptée
            }

            var depuis = hook?.Quand ?? activite;
            var vu = this.Vu(2, id, depuis) || note.Vue > depuis;
            var regardee = (etat is Etat.Fini or Etat.Erreur && (vu || relais)) || (etat == Etat.Attend && vu); // question vue : « ? » effacé
            titre = !relais && note.Titre is { Length: > 0 } renomme ? renomme : titre;
            return new(2, id, titre, regardee ? Etat.Repos : etat, depuis, activite == DateTime.MinValue ? depuis : activite, vu, relais, source);
        }

        // Dernière vue de la conversation notée par Antigravity lui-même (annotations/<id>.pbtxt, last_user_view_time) :
        // vue même quand il est lancé sans son port 9227 (raccourci Windows), où le pad ne voit pas la sélection (05/11).
        private static String AnnotationsAgy => Path.Combine(Maison, ".gemini", "antigravity", "annotations");

        // Note entière : dernière vue, titre donné par l'utilisateur (renommage), archivée.
        private static (DateTime Vue, String Titre, Boolean Archivee) NoteAntigravity(String id)
        {
            try
            {
                var f = Path.Combine(AnnotationsAgy, id + ".pbtxt");
                if (!File.Exists(f))
                {
                    return (DateTime.MinValue, null, false);
                }

                using var flux = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var lecteur = new StreamReader(flux);
                return LireNoteAgy(lecteur.ReadToEnd());
            }
            catch (Exception)
            {
                return (DateTime.MinValue, null, false); // fichier en cours d'écriture : relu au tour suivant
            }
        }

        internal static (DateTime Vue, String Titre, Boolean Archivee) LireNoteAgy(String pbtxt)
        {
            // Champs sur une ligne (« title:"…" archived:true last_user_view_time:{…} »), texte en UTF-8 brut.
            var titre = Regex.Match(pbtxt ?? "", "(?<![a-z_])title\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            var archivee = Regex.IsMatch(pbtxt ?? "", "(?<![a-z_])archived\\s*:\\s*true\\b");
            return (DerniereVueAgy(pbtxt), titre.Success ? TextePbtxt(titre.Groups[1].Value) : null, archivee);
        }

        // Échappements du format texte de protobuf : \" \\ \n \t et octets en octal (\303\251), recomposés en UTF-8.
        internal static String TextePbtxt(String brut)
        {
            var octets = new List<Byte>();
            for (var i = 0; i < brut.Length; i++)
            {
                if (brut[i] != '\\' || i + 1 >= brut.Length)
                {
                    var paire = Char.IsHighSurrogate(brut[i]) && i + 1 < brut.Length;
                    octets.AddRange(Encoding.UTF8.GetBytes(brut.Substring(i, paire ? 2 : 1)));
                    i += paire ? 1 : 0;
                    continue;
                }

                var c = brut[++i];
                if (c is >= '0' and <= '7')
                {
                    var n = 0;
                    var j = 0;
                    for (; j < 3 && i + j < brut.Length && brut[i + j] is >= '0' and <= '7'; j++)
                    {
                        n = n * 8 + (brut[i + j] - '0');
                    }

                    octets.Add((Byte)n);
                    i += j - 1;
                    continue;
                }

                octets.AddRange(Encoding.UTF8.GetBytes(c switch { 'n' => "\n", 't' => "\t", 'r' => "\r", _ => c.ToString() }));
            }

            return Encoding.UTF8.GetString(octets.ToArray());
        }

        internal static DateTime DerniereVueAgy(String pbtxt)
        {
            var m = Regex.Match(pbtxt ?? "", @"last_user_view_time\s*:?\s*\{\s*seconds\s*:\s*(\d+)(?:\s+nanos\s*:\s*(\d+))?");
            if (!m.Success)
            {
                return DateTime.MinValue;
            }

            var quand = DateTimeOffset.FromUnixTimeSeconds(Int64.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).UtcDateTime;
            return m.Groups[2].Success ? quand.AddTicks(Int64.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) / 100) : quand;
        }

        // Conversations d'Antigravity ouvertes par le relais, par sujet : conversation → sujet.
        private static Dictionary<String, String> SujetsAgy()
        {
            var sujets = new Dictionary<String, String>();
            var dossier = Path.Combine(DossierRelais, "topics");
            foreach (var d in Directory.Exists(dossier) ? Directory.EnumerateDirectories(dossier) : [])
            {
                try
                {
                    var f = Path.Combine(d, "agy.json");
                    if (File.Exists(f))
                    {
                        using var flux = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        using var doc = JsonDocument.Parse(flux);
                        if (doc.RootElement.TryGetProperty("conversation", out var c) && c.GetString() is { Length: > 0 } id)
                        {
                            sujets[id] = Path.GetFileName(d);
                        }
                    }
                }
                catch (Exception)
                {
                    // ce sujet en train d'être écrit par le relais : relu au tour suivant, les autres sont lus
                }
            }

            return sujets;
        }

        private void LireCdp()
        {
            lock (this._verrouCdp)
            {
                this.LireCdpSeul();
            }
        }

        private readonly Object _verrouCdp = new(); // le moteur et un geste : jamais une lecture ancienne sur une récente

        private void LireCdpSeul()
        {
            // Lignes de sa barre latérale, et la conversation affichée par son adresse (/c/<id>) : la barre peut être masquée,
            // les lignes absentes ; la liste d'avant est alors gardée, la sélection vient de l'adresse.
            var agy = Connecteurs.EvenementsActifs("antigravity") ? Cdp.Evaluer(Cdp.PortAntigravity, "({ lignes: [...document.querySelectorAll('[data-testid=conversation-row-sidebar]')].map(r => [r.getAttribute('data-cascade-id'), r.innerText, r.getAttribute('data-selected') === 'true']), affichee: (location.pathname.match(/^\\/c\\/([0-9a-f-]+)/) || [])[1] || null, modele: " + Js.ModeleAntigravity + " })") : null;
            if (agy is { ValueKind: JsonValueKind.Object } page)
            {
                var lignes = page.GetProperty("lignes").EnumerateArray().Select(l =>
                {
                    var morceaux = (l[1].GetString() ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    return (l[0].GetString(), morceaux.FirstOrDefault() ?? "Antigravity", AgeAffiche(morceaux.Skip(1).FirstOrDefault()), l[2].GetBoolean());
                }).Where(l => l.Item1 is not null).ToList();
                var affichee = page.GetProperty("affichee").ValueKind == JsonValueKind.String ? page.GetProperty("affichee").GetString() : null;
                if (lignes.Count > 0 || affichee is null)
                {
                    this._lignesAgy = lignes;
                }

                this._selectionAgy = lignes.FirstOrDefault(l => l.Item4).Item1 ?? affichee;
                if (page.TryGetProperty("modele", out var modele) && modele.ValueKind == JsonValueKind.String && this._quotas[2] is { } quota)
                {
                    var famille = QuotaAntigravity.FamilleModele(modele.GetString());
                    if (quota.GroupeSelectionne != famille)
                    {
                        this._quotas[2] = QuotaAntigravity.Actuel(quota with { GroupeSelectionne = famille }, this._quotaAgyLu);
                        // Choisir un modèle ne rafraîchit pas les données de consommation : garder leur vraie date.
                        QuotaAntigravity.Garder(this._quotas[2], this._quotaAgyLu);
                        if (QuotaAntigravity.Garde(out var date) is { } partagee)
                        {
                            this._quotas[2] = partagee;
                            this._quotaAgyLu = date;
                        }
                    }
                }
            }
            else
            {
                this._selectionAgy = null; // port muet : sélection inconnue, pas l'ancienne
            }

            this._barreAgyLue = agy is { ValueKind: JsonValueKind.Object };

            var codex = Connecteurs.EvenementsActifs("codex") ? Cdp.Evaluer(Cdp.PortCodex, Js.SelectionCodex) : null;
            this._selectionCodex = codex is { ValueKind: JsonValueKind.String } s ? s.GetString() : null;
        }

        // « 10m », « 3h », « 2d », « now » : âge affiché par Antigravity → instant approximatif.
        private static DateTime AgeAffiche(String texte)
        {
            var m = Regex.Match(texte ?? "", @"^(\d+)\s*([smhdw])");
            if (!m.Success)
            {
                return DateTime.UtcNow;
            }

            var n = Int32.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            return DateTime.UtcNow - m.Groups[2].Value switch
            {
                "s" => TimeSpan.FromSeconds(n),
                "m" => TimeSpan.FromMinutes(n),
                "h" => TimeSpan.FromHours(n),
                "d" => TimeSpan.FromDays(n),
                _ => TimeSpan.FromDays(7 * n),
            };
        }
    }
}

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
                var relais = titre.StartsWith("Nexus Relay", StringComparison.Ordinal) || sujets.ContainsKey(id);
                if (this.ConversationAgy(id, relais && sujets.TryGetValue(id, out var sujet) ? "Relais · " + sujet : titre, activite, relais) is { } c)
                {
                    yield return c;
                }
            }

            foreach (var (id, sujet) in sujets)
            {
                if (!vues.Contains(id) && this.ConversationAgy(id, "Relais · " + sujet, DateTime.MinValue, true) is { } c)
                {
                    yield return c;
                }
            }
        }

        private Conversation ConversationAgy(String id, String titre, DateTime activite, Boolean relais)
        {
            var hook = Hook("antigravity", id);
            var etat = hook is { } h ? Traduire(h.Etat, h.Quand) : Etat.Repos;
            if (relais && etat is not (Etat.Travaille or Etat.Attend or Etat.Autorisation))
            {
                return null;
            }

            var depuis = hook?.Quand ?? activite;
            var vu = this.Vu(2, id, depuis);
            return new(2, id, titre, etat == Etat.Fini && (vu || relais) ? Etat.Repos : etat, depuis, activite == DateTime.MinValue ? depuis : activite, vu, relais);
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
            var agy = Connecteurs.EvenementsActifs("antigravity") ? Cdp.Evaluer(Cdp.PortAntigravity, "({ lignes: [...document.querySelectorAll('[data-testid=conversation-row-sidebar]')].map(r => [r.getAttribute('data-cascade-id'), r.innerText, r.getAttribute('data-selected') === 'true']), affichee: (location.pathname.match(/^\\/c\\/([0-9a-f-]+)/) || [])[1] || null })") : null;
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
            }
            else
            {
                this._selectionAgy = null; // port muet : sélection inconnue, pas l'ancienne
            }

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

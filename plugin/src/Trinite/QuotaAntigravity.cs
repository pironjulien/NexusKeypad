namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    // Quota d'Antigravity lu sur son écran, comme un humain : sa page Settings › Models, par son port de débogage (9227).
    // Aucun appel à son moteur (language_server) : son jeton interne n'est jamais lu ni utilisé (risque pour le compte).
    public static class QuotaAntigravity
    {
        // Page Models ou boîte « View Usage » déjà ouverte par l'utilisateur (ou par la touche de quota) : lue sans rien toucher ;
        // sinon null.
        private const String Passif = """
            (() => {
              const dansVue = r => r.width > 0 && r.height > 0 && r.right > 0 && r.bottom > 0
                && r.left < window.innerWidth && r.top < window.innerHeight;
              const masque = e => {
                for (let p = e; p; p = p.parentElement) {
                  const c = getComputedStyle(p);
                  if (p.hidden || p.hasAttribute('data-closed') || p.hasAttribute('inert')
                    || p.getAttribute('aria-hidden') === 'true' || c.display === 'none'
                    || c.visibility === 'hidden' || c.visibility === 'collapse'
                    || c.contentVisibility === 'hidden' || Number(c.opacity) === 0) return true;
                }
                return false;
              };
              const visible = e => !masque(e) && [...e.getClientRects()].some(dansVue);
              const texteVisible = (racine, role) => {
                const morceaux = [], textes = document.createTreeWalker(racine, NodeFilter.SHOW_TEXT);
                for (let n = textes.nextNode(); n; n = textes.nextNode()) {
                  const e = n.parentElement;
                  if (!n.nodeValue.trim() || e.closest(role) !== racine || masque(e)) continue;
                  const r = document.createRange(); r.selectNodeContents(n);
                  if ([...r.getClientRects()].some(dansVue)) morceaux.push(n.nodeValue.trim());
                }
                return morceaux.join('\n');
              };
              const d = [...document.querySelectorAll('[role=dialog]')].find(x => visible(x)
                && [...x.querySelectorAll('button')].some(b => visible(b) && (b.innerText || '').trim() === 'Models'));
              if (d) {
                const t = texteVisible(d, '[role=dialog]');
                if (t.includes('Models & Usage') && t.includes('%')) return t;
              }
              const s = document.querySelector('[data-testid=model-selector-trigger]');
              if (!s || s.getAttribute('aria-expanded') !== 'true' || !visible(s)) return null;
              const candidats = [...document.querySelectorAll('[role=menu]')].filter(visible)
                .map(m => texteVisible(m, '[role=menu]'))
                .filter(t => /\b\d{1,3}(?:[.,]\d+)?\s*%/.test(t) && (/\bLimit\s+Remaining\b/i.test(t)
                  || (/\bUsage\b/i.test(t) && /\b(?:Weekly|5\s*h)\s+limit\b/i.test(t))));
              return candidats.length === 1 ? candidats[0] : null;
            })()
            """;

        // Ouvre Settings › Models, lit le texte, puis rend la vue d'origine (retour arrière, puis Fermer sur SON dialogue).
        // Lit la fenêtre des réglages si elle est déjà ouverte, sans rien toucher ; si l'utilisateur clique ou tape pendant le
        // relevé, le script renonce à tout rangement : la vue est à lui ; de même si le pad ouvre une conversation pendant ce temps (ses clics
        // ne sont pas des événements « de confiance » : il pose window.__nexusRepris). Mêmes gestes que le relevé du relais.
        private const String Actif = """
            (async () => {
              const pause = ms => new Promise(r => setTimeout(r, ms));
              // La fenêtre des réglages (celle qui porte l'onglet Models), jamais le premier dialogue venu : une notification
              // « Conversation Archived … Undo » est aussi un [role=dialog] qui reste dans le DOM (constaté le 26/10).
              const dialogue = () => [...document.querySelectorAll('[role=dialog]')].find(d => [...d.querySelectorAll('button')].some(b => (b.innerText || '').trim() === 'Models'));
              const nom = b => (b.getAttribute('aria-label') || b.innerText || '').trim();
              const deja = dialogue();
              if (deja) { const t = deja.innerText || ''; return t.includes('Models & Usage') && t.includes('%') ? t : null; }
              const reglages = [...document.querySelectorAll('button, [role=button]')].find(b => nom(b) === 'Settings');
              if (!reglages) return null;
              window.__nexusRepris = false;
              let reprisDom = false;
              const repris = e => { if (e.isTrusted) reprisDom = true; };
              const utilisateurVu = () => reprisDom || window.__nexusRepris === true;
              const evenements = ['pointerdown', 'keydown', 'wheel'];
              evenements.forEach(e => window.addEventListener(e, repris, true));
              try {
                const origine = location.href;
                reglages.click();
                for (let i = 0; i < 20 && !dialogue() && !utilisateurVu(); i++) await pause(150);
                const sien = dialogue();
                let lu = null;
                const onglet = sien && [...sien.querySelectorAll('button')].find(b => (b.innerText || '').trim() === 'Models');
                if (onglet && !utilisateurVu()) {
                  onglet.click();
                  for (let i = 0; i < 30 && !lu && !utilisateurVu(); i++) { const t = dialogue()?.innerText; if (t && t.includes('Models & Usage') && t.includes('%')) lu = t; else await pause(150); }
                }
                if (utilisateurVu()) return lu;
                for (let i = 0; i < 4 && location.href !== origine && !utilisateurVu(); i++) { history.back(); await pause(300); }
                const reste = dialogue();
                if (!utilisateurVu() && reste && (reste === sien || (reste.innerText || '').includes('Models & Usage')))
                  [...reste.querySelectorAll('button')].find(b => nom(b) === 'Close')?.click();
                return lu;
              } finally {
                evenements.forEach(e => window.removeEventListener(e, repris, true));
              }
            })()
            """;

        // ouvrir : la page peut être ouverte un instant ; sinon seulement lue si l'utilisateur l'a déjà sous les yeux.
        public static Quota Lire(Boolean ouvrir)
        {
            var texte = Cdp.Evaluer(Cdp.PortAntigravity, ouvrir ? Actif : Passif, attente: 10);
            return texte is { ValueKind: JsonValueKind.String } t ? Analyser(t.GetString()) : null;
        }

        // Jauge à l'instant : chaque groupe passé son heure de remise à zéro est à 0 % (sans heure connue : au bout de sa
        // durée, 5 h ou 7 jours, depuis la lecture lu), puis le groupe le plus entamé de chaque fenêtre. Un groupe remis à
        // zéro laisse la place au suivant (Gemini à 80 % remis à zéro, Claude et GPT à 50 % : 50 %). Sans détail, le quota
        // tel quel (relecture de Codex, 25/10).
        public static Quota Actuel(Quota quota, DateTime lu)
        {
            if (quota is null || quota.Groupes.Count == 0)
            {
                return quota;
            }

            var maintenant = DateTime.UtcNow;
            Double? Jauge(Boolean cinq) => quota.Groupes.Where(g => g.Cinq == cinq)
                .Select(g => maintenant >= (g.Fin ?? lu + (cinq ? TimeSpan.FromHours(5) : TimeSpan.FromDays(7))) ? 0 : g.Utilise)
                .Select(u => (Double?)u).DefaultIfEmpty(null).Max();
            return new(Jauge(true), Jauge(false)) { Groupes = quota.Groupes };
        }

        // Dernière lecture gardée sur disque, avec son détail par groupe (lue au plus tous les quarts d'heure : sans elle, la
        // jauge serait grise après chaque rechargement du plugin) ; reprise jusqu'à 7 jours, la durée de sa fenêtre la
        // plus longue, chaque groupe remis à zéro à son heure.
        // Changé seulement par le vérificateur (fichier temporaire : jamais le vrai cache du plugin en service).
        public static String Fichier { get; set; } = System.IO.Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "", "NexusKeypad", "quota-antigravity.json");

        public static void Garder(Quota quota)
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Fichier));
                System.IO.File.WriteAllText(Fichier + ".tmp", JsonSerializer.Serialize(new
                {
                    cinq = quota.Cinq,
                    semaine = quota.Semaine,
                    groupes = quota.Groupes.Select(g => new { cinq = g.Cinq, utilise = g.Utilise, fin = g.Fin }),
                    lu = DateTime.UtcNow,
                }));
                System.IO.File.Move(Fichier + ".tmp", Fichier, true);
            }
            catch (Exception e)
            {
                PluginLog.Error(e, "quota d'Antigravity gardé");
            }
        }

        public static DateTime Lu()
        {
            Garde(out var lu);
            return lu;
        }

        public static Quota Garde(out DateTime lu)
        {
            lu = DateTime.MinValue;
            try
            {
                using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(Fichier));
                var r = doc.RootElement;
                lu = r.GetProperty("lu").GetDateTime().ToUniversalTime();
                if (DateTime.UtcNow - lu >= TimeSpan.FromDays(7))
                {
                    return null;
                }

                Double? Valeur(String nom) => r.GetProperty(nom).ValueKind == JsonValueKind.Number ? r.GetProperty(nom).GetDouble() : null;
                var groupes = r.TryGetProperty("groupes", out var liste) && liste.ValueKind == JsonValueKind.Array
                    ? liste.EnumerateArray().Select(g => new GroupeQuota(g.GetProperty("cinq").GetBoolean(), g.GetProperty("utilise").GetDouble(),
                        g.GetProperty("fin").ValueKind == JsonValueKind.String ? g.GetProperty("fin").GetDateTime().ToUniversalTime() : null)).ToList()
                    : [];
                if (groupes.Count == 0)
                {
                    // Ancien format, sans détail : une fenêtre par valeur, remise à zéro à son heure gardée (finCinq,
                    // finSemaine), sinon au bout de sa durée.
                    DateTime? Fin(String nom) => r.TryGetProperty(nom, out var f) && f.ValueKind == JsonValueKind.String ? f.GetDateTime().ToUniversalTime() : null;
                    groupes = [.. new[] { (true, Valeur("cinq"), Fin("finCinq")), (false, Valeur("semaine"), Fin("finSemaine")) }
                        .Where(v => v.Item2 is not null).Select(v => new GroupeQuota(v.Item1, v.Item2.Value, v.Item3))];
                }

                return Actuel(new(Valeur("cinq"), Valeur("semaine")) { Groupes = groupes }, lu);
            }
            catch (Exception)
            {
                return null; // rien de gardé encore
            }
        }

        // Chiffres ASCII seulement : \d accepte tous les chiffres Unicode, que Double.Parse refuse (FormatException relevée par
        // Codex, 27/10).
        private static readonly Regex Pourcentage = new(@"^([0-9]{1,3}(?:[.,][0-9]{1,6})?)\s*%(?:\s+left)?$", RegexOptions.IgnoreCase);

        // « …, it will fully refresh in 4 days, 23 hours. » → délai avant la remise à zéro de la fenêtre.
        private static readonly Regex Remise = new(@"(?:refresh(?:es)?|resets?)\s+in\s+(.+?)\.?$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        // Nombre entier (jamais un morceau de nombre plus long : « 1000000 seconds » n'est pas « 000000 seconds »).
        private static readonly Regex Morceau = new(@"(?<![0-9])([0-9]+)\s*(days?|hours?|minutes?|seconds?|[dhms])(?![A-Za-z])", RegexOptions.IgnoreCase);

        // Une fenêtre dure au plus une semaine : au-delà de 8 jours, un délai (une composante ou leur somme) est un texte
        // abîmé ; tout le délai est alors ignoré (pas d'heure de remise à zéro, la part lue reste), sans calcul qui déborde.
        private static readonly Int64 Plafond = (Int64)TimeSpan.FromDays(8).TotalSeconds;

        public static TimeSpan? Delai(String ligne)
        {
            if (Remise.Match(ligne) is not { Success: true } m)
            {
                return null;
            }

            Int64 secondes = 0;
            var lu = false;
            foreach (Match p in Morceau.Matches(m.Groups[1].Value))
            {
                var unite = Char.ToLowerInvariant(p.Groups[2].Value[0]) switch
                {
                    'd' => 86400L,
                    'h' => 3600L,
                    'm' => 60L,
                    _ => 1L,
                };
                if (!Int64.TryParse(p.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n > Plafond / unite
                    || (secondes += n * unite) > Plafond)
                {
                    return null;
                }

                lu = true;
            }

            var reste = Morceau.Replace(m.Groups[1].Value, "");
            return lu && Regex.IsMatch(reste, @"^(?:\s|,|\.|\band\b)*$", RegexOptions.IgnoreCase)
                ? TimeSpan.FromSeconds(secondes) : null;
        }

        // Texte de la page (« Gemini Models », « Five Hour Limit Remaining », « … refresh in 2 hours, 42 minutes. »,
        // « 34% »…) → chaque groupe de modèles, sa part utilisée et son heure de remise à zéro par fenêtre ; jauge : le
        // groupe le plus entamé de chaque fenêtre ; null si rien n'est lisible.
        public static Quota Analyser(String texte)
        {
            var groupes = new System.Collections.Generic.List<GroupeQuota>();
            TimeSpan? delai = null;
            Boolean? cinq = null;
            Double? restant = null;
            var pourcentages = 0;
            var groupe = false;
            var argon = false;
            var premiereFenetre = true;
            Double? avantFenetre = null;
            var valeursAvant = 0;
            String fragmentsDelai = null;
            var maintenant = DateTime.UtcNow;
            void Terminer()
            {
                if (cinq is Boolean fenetre && restant is Double valeur)
                {
                    groupes.Add(new(fenetre, 1 - (valeur / 100), delai is TimeSpan attente ? maintenant + attente : null));
                }
                cinq = null;
                restant = null;
                pourcentages = 0;
                delai = null;
                fragmentsDelai = null;
            }
            // Les libellés et valeurs peuvent être dans plusieurs nœuds DOM, ou sur une même ligne visuelle.
            var lignes = Regex.Replace(texte ?? "", @"\b(?:Gemini(?:\s*[0-9.]+)?\s*(?:Models|Argon)|Claude\s+and\s+GPT\s+models)\b",
                m => "\n" + Regex.Replace(m.Value, @"\s+", " ") + "\n", RegexOptions.IgnoreCase);
            lignes = Regex.Replace(lignes, @"\b(?:Five\s+Hour\s+Limit\s+Remaining|Weekly\s+Limit(?:\s+Remaining)?|5\s*h\s+limit)\b",
                m => "\n" + Regex.Replace(m.Value, @"\s+", " ") + "\n", RegexOptions.IgnoreCase);
            lignes = Regex.Replace(lignes, @"(?<![0-9.,])([0-9]{1,3}(?:[.,][0-9]{1,6})?)\s*%(?:\s+left)?", m => "\n" + Regex.Replace(m.Value, @"\s+", " ") + "\n", RegexOptions.IgnoreCase);
            lignes = Regex.Replace(lignes, @"(?:refresh(?:es)?|resets?)\s+in\b", m => Regex.Replace(m.Value, @"\s+", " "), RegexOptions.IgnoreCase);
            foreach (var ligne in lignes.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
            {
                if (Regex.IsMatch(ligne, @"^(?:Gemini(?:\s*[0-9.]+)?\s*(?:Models|Argon)|Claude and GPT models)$", RegexOptions.IgnoreCase))
                {
                    Terminer();
                    groupe = true;
                    argon = ligne.EndsWith("Argon", StringComparison.OrdinalIgnoreCase);
                    premiereFenetre = true;
                    avantFenetre = null;
                    valeursAvant = 0;
                }
                else if (ligne.Equals("Five Hour Limit Remaining", StringComparison.OrdinalIgnoreCase)
                    || ligne.Equals("5h limit", StringComparison.OrdinalIgnoreCase)
                    || ligne.Equals("5 h limit", StringComparison.OrdinalIgnoreCase)
                    || ligne.Equals("Weekly Limit Remaining", StringComparison.OrdinalIgnoreCase)
                    || ligne.Equals("Weekly limit", StringComparison.OrdinalIgnoreCase))
                {
                    Terminer();
                    if (groupe)
                    {
                        cinq = ligne.StartsWith("Five", StringComparison.OrdinalIgnoreCase) || ligne.StartsWith('5');
                        // Le petit groupe Argon expose sa part avant son unique libellé hebdomadaire. Ce tampon
                        // appartient seulement à ce groupe, avant sa première fenêtre ; aucune valeur empruntée ailleurs.
                        if (argon && premiereFenetre && cinq == false && valeursAvant == 1)
                        {
                            restant = avantFenetre;
                            pourcentages = 1;
                        }
                        premiereFenetre = false;
                    }
                    avantFenetre = null;
                    valeursAvant = 0;
                }
                else if (cinq is not null && Regex.IsMatch(ligne, @"(?:refresh(?:es)?|resets?)\s+in(?:\s|$)", RegexOptions.IgnoreCase))
                {
                    fragmentsDelai = ligne;
                    delai = Delai(fragmentsDelai); // délai abîmé : part gardée, sans heure de remise inventée
                }
                else if (cinq is not null && fragmentsDelai is not null && Morceau.IsMatch(ligne)
                    && Regex.IsMatch(Morceau.Replace(ligne, ""), @"^(?:\s|,|\.|\band\b)*$", RegexOptions.IgnoreCase))
                {
                    fragmentsDelai += " " + ligne;
                    delai = Delai(fragmentsDelai);
                }
                else if ((cinq is not null || (argon && premiereFenetre)) && Pourcentage.Match(ligne) is { Success: true } m)
                {
                    // Au-delà de 100 % restants : texte abîmé, ignoré (une part négative faussait la jauge ; FsCheck, 27/10).
                    var valeur = Double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
                    if (cinq is not null) { restant = ++pourcentages == 1 && valeur <= 100 ? valeur : null; }
                    else { avantFenetre = ++valeursAvant == 1 && valeur <= 100 ? valeur : null; }
                }
                else if (cinq is not null) { Terminer(); } // texte étranger : aucune association avec la valeur suivante
                else { avantFenetre = null; valeursAvant = 0; } // aucune mise en réserve à travers un texte étranger
            }
            Terminer();

            return groupes.Count == 0 ? null : Actuel(new(null, null) { Groupes = groupes }, maintenant);
        }
    }
}

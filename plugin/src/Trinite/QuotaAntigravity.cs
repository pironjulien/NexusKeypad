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
        private const String Passif =
            "(() => { const d = document.querySelector('[role=dialog]'); const t = d ? d.innerText : '';" +
            " if (t.includes('Models & Usage') && t.includes('%')) return t;" +
            " const b = [...document.querySelectorAll('[role=menu]:not([data-closed])')].map(m => m.innerText).find(m => m.includes('Limit Remaining') && m.includes('%'));" +
            " return b ?? null })()";

        // Ouvre Settings › Models, lit le texte, puis rend la vue d'origine (retour arrière, puis Fermer sur SON dialogue).
        // Ne touche à rien si un dialogue existe déjà (même vide) ; si l'utilisateur clique ou tape pendant le relevé, le script
        // renonce à tout rangement : la vue est à lui ; de même si le pad ouvre une conversation pendant ce temps (ses clics
        // ne sont pas des événements « de confiance » : il pose window.__nexusRepris). Mêmes gestes que le relevé du relais.
        private const String Actif = """
            (async () => {
              const pause = ms => new Promise(r => setTimeout(r, ms));
              const dialogue = () => document.querySelector('[role=dialog]');
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

        // Dernière lecture gardée sur disque (lue au plus tous les quarts d'heure : sans elle, la jauge serait grise après
        // chaque rechargement du plugin) ; reprise si elle a moins de 5 h, la durée de sa fenêtre la plus courte.
        private static String Fichier => System.IO.Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "", "NexusKeypad", "quota-antigravity.json");

        public static void Garder(Quota quota)
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Fichier));
                System.IO.File.WriteAllText(Fichier + ".tmp", JsonSerializer.Serialize(new { cinq = quota.Cinq, semaine = quota.Semaine, lu = DateTime.UtcNow }));
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
                Double? Valeur(String nom) => r.GetProperty(nom).ValueKind == JsonValueKind.Number ? r.GetProperty(nom).GetDouble() : null;
                // Au-delà de 5 h, la fenêtre courte est forcément repartie à zéro si Antigravity n'a pas servi (fermé) : 0 %
                // utilisé ; la semaine reste juste jusqu'à 7 jours. Antigravity ouvert, l'appelant ne s'en sert pas.
                var age = DateTime.UtcNow - lu;
                return age < TimeSpan.FromHours(5) ? new(Valeur("cinq"), Valeur("semaine"))
                    : age < TimeSpan.FromDays(7) ? new(0, Valeur("semaine"))
                    : null;
            }
            catch (Exception)
            {
                return null; // rien de gardé encore
            }
        }

        private static readonly Regex Pourcentage = new(@"^(\d{1,3}(?:[.,]\d+)?)\s*%$");

        // Texte de la page (« Gemini Models », « Five Hour Limit Remaining », « 34% »…) → part utilisée, groupe de modèles
        // le plus entamé pour chaque fenêtre ; null si rien n'est lisible.
        public static Quota Analyser(String texte)
        {
            Double? cinq = null, semaine = null;
            String fenetre = null;
            var groupe = false;
            foreach (var ligne in (texte ?? "").Split('\n').Select(l => l.Trim()))
            {
                if (ligne.Equals("Gemini Models", StringComparison.OrdinalIgnoreCase) || ligne.Equals("Claude and GPT models", StringComparison.OrdinalIgnoreCase))
                {
                    groupe = true;
                    fenetre = null;
                }
                else if (groupe && ligne is "Five Hour Limit Remaining" or "Weekly Limit Remaining")
                {
                    fenetre = ligne;
                }
                else if (fenetre is not null && Pourcentage.Match(ligne) is { Success: true } m)
                {
                    var utilise = 1 - (Double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture) / 100);
                    if (fenetre.StartsWith("Five", StringComparison.Ordinal))
                    {
                        cinq = Math.Max(cinq ?? 0, utilise);
                    }
                    else
                    {
                        semaine = Math.Max(semaine ?? 0, utilise);
                    }

                    fenetre = null;
                }
            }

            return cinq is null && semaine is null ? null : new(cinq, semaine);
        }
    }
}

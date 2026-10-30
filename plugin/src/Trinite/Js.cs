namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.Text.Json;

    // Scripts injectés par le port de débogage (repris du relais : la conversation d'une ligne Codex est dans ses props React).
    public static class Js
    {
        private const String LignesCodex =
            "for (const r of document.querySelectorAll('[data-app-action-sidebar-thread-row]')) {" +
            " const k = Object.keys(r).find(k => k.startsWith('__reactFiber')); let f = k ? r[k] : null, id = null;" +
            " for (let i = 0; i < 12 && f && !id; i++) { const p = f.memoizedProps || {}; if (typeof p.conversationId === 'string') id = p.conversationId; f = f.return; }";

        // Le champ de saisie VISIBLE (Codex garde un second éditeur caché, placé avant), modifiable, hors fenêtre modale.
        private const String Champ = "[...document.querySelectorAll('[contenteditable=true], textarea')].filter(e => e.getClientRects().length && !e.disabled && !e.readOnly" +
            " && !e.closest('[role=dialog], [aria-modal=true], [inert]')).pop()";

        public const String SelectionCodex =
            "(() => { " + LignesCodex + " if (id && r.getAttribute('data-app-action-sidebar-thread-active') === 'true') return id; } return null })()";

        public static String OuvrirCodex(String conversation) =>
            "(() => { " + LignesCodex + $" if (id === {JsonSerializer.Serialize(conversation)}) {{ r.scrollIntoView({{block: 'nearest'}}); r.click(); return true }} }} return false }})()";

        // Antigravity : clic sur sa ligne de la barre latérale ; route : si elle reste absente (repliée, ou vue seulement
        // dans une notification), sa route /c/<id> (l'interface est servie en https://127.0.0.1:<port>/ ; recharge la page).
        public static String OuvrirAntigravity(String conversation, Boolean route = false) =>
            $"(() => {{ window.__nexusRepris = true; const id = {JsonSerializer.Serialize(conversation)}; const r = document.querySelector('[data-testid=conversation-row-sidebar][data-cascade-id=' + JSON.stringify(id) + ']');" +
            " if (location.pathname === '/c/' + id) return true;" +
            " if (r) { r.scrollIntoView({block: 'nearest'}); (r.querySelector('a') || r).click(); return true }" +
            (route ? " if (location.protocol !== 'https:' && location.protocol !== 'http:') return false;" +
                " location.assign(location.origin + '/c/' + encodeURIComponent(id)); return true })()" : " return false })()");

        // Lecture seule avant le geste unique : le chargement d'une barre latérale ne justifie aucun clic rejoué.
        public static String NavigationAntigravity(String conversation) =>
            $"(() => {{ const id = {JsonSerializer.Serialize(conversation)};" +
            EtatNavigationAntigravity + " })()";

        public const String EtatNavigationAntigravity = """
            if (document.readyState === 'loading') return null;
            const v = document.querySelector('[data-testid=conversation-view]');
            if (location.pathname === '/c/' + id && v) return 'open';
            return v || document.querySelector('[data-testid=model-selector-trigger]') ? 'ready' : null;
            """;

        // Conversation descendue jusqu'au dernier message : sa plus grande zone visible qui défile (le fil ; Codex garde
        // aussi les fils déjà vus, cachés, à 0 px), hors barre latérale et dialogue. Leur bouton « en bas » reste dans la
        // page même invisible : un clic dessus ne faisait rien (24/10). « ok », sinon « absent ».
        public const String Descendre =
            "(() => { const z = [...document.querySelectorAll('*')].filter(e => { const o = getComputedStyle(e).overflowY;" +
            " return (o === 'auto' || o === 'scroll') && e.clientHeight > 150 && e.scrollHeight > e.clientHeight + 4 && e.checkVisibility({ visibilityProperty: true })" +
            " && !e.closest('nav, aside, [role=dialog], [inert]') });" +
            " if (!z.length) return 'absent'; const f = z.reduce((a, b) => b.clientWidth * b.clientHeight > a.clientWidth * a.clientHeight ? b : a);" +
            " f.scrollTop = f.scrollHeight; return 'ok' })()";

        // Antigravity prêt pour sa boîte des quotas : sélecteur de modèle présent (rien n'est cliqué).
        public const String SelecteurAntigravity = "!!document.querySelector('[data-testid=model-selector-trigger]')";

        public const String ModeleAntigravity =
            "(() => { const b = document.querySelector('[data-testid=model-selector-trigger]'); return b?.getClientRects().length ? ((b.getAttribute('aria-label') || b.innerText || '').trim() || null) : null })()";

        // Menu des modèles d'Antigravity ouvert (sa boîte des quotas comprise) : centre du sélecteur, pour le refermer d'un
        // clic de confiance ; null s'il est fermé. Son état est celui du sélecteur (aria-expanded) : fenêtre cachée, un menu
        // fermé reste dans la page (data-closed) tant que son animation de sortie n'a pas joué.
        public const String MenuModelesAntigravity =
            "(() => { const s = document.querySelector('[data-testid=model-selector-trigger]'); if (s?.getAttribute('aria-expanded') !== 'true') return null;" +
            " const r = s.getBoundingClientRect(); return [r.x + (r.width / 2), r.y + (r.height / 2)] })()";

        // Preuve du détail des quotas, sans clic : ancien « Limit Remaining », ou panneau actuel « Usage » et ses
        // limites. UIA peut annoncer ses enfants hors écran : on vérifie le texte réellement peint dans le DOM,
        // jamais innerText d'un menu parent qui pourrait aussi contenir un sous-menu fermé ou transparent.
        public const String UtilisationAntigravityOuverte = """
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
              const s = document.querySelector('[data-testid=model-selector-trigger]');
              if (!s) return null;
              if (s.getAttribute('aria-expanded') !== 'true' || !visible(s)) return false;
              return [...document.querySelectorAll('[role=menu]')].some(m => {
                if (!visible(m)) return false;
                const morceaux = [], textes = document.createTreeWalker(m, NodeFilter.SHOW_TEXT);
                for (let n = textes.nextNode(); n; n = textes.nextNode()) {
                  const e = n.parentElement;
                  if (!n.nodeValue.trim() || e.closest('[role=menu]') !== m || masque(e)) continue;
                  const r = document.createRange();
                  r.selectNodeContents(n);
                  if ([...r.getClientRects()].some(dansVue)) morceaux.push(n.nodeValue);
                }
                const texte = morceaux.join(' ');
                return /\b\d{1,3}(?:[.,]\d+)?\s*%/.test(texte) && (/\bLimit\s+Remaining\b/i.test(texte)
                  || (/\bUsage\b/i.test(texte) && /\b(?:Weekly|5\s*h)\s+limit\b/i.test(texte)));
              });
            })()
            """;

        // Antigravity : sa boîte « View Usage » (menu des modèles › View Usage), comme la boîte « Utilisation » de Claude,
        // laissée ouverte pour l'utilisateur (il la ferme d'un clic ailleurs). __nexusRepris : un relevé du quota en cours lui
        // laisse la main.
        public const String BoiteUtilisationAntigravity = """
            (async () => {
              const pause = ms => new Promise(r => setTimeout(r, ms));
              window.__nexusRepris = true;
              const usage = () => [...document.querySelectorAll('[role=menu]:not([data-closed]) [role=menuitem][aria-haspopup=menu]')]
                .find(e => e.innerText.trim() === 'View Usage');
              const s = document.querySelector('[data-testid=model-selector-trigger]');
              if (!s) return 'sélecteur de modèle d\'Antigravity introuvable';
              if (s.getAttribute('aria-expanded') !== 'true') s.click();
              let u = usage();
              for (let i = 0; i < 20 && !u; i++) { await pause(100); u = usage(); }
              if (!u) return 'entrée View Usage d\'Antigravity introuvable';
              u.click();
              return 'ok';
            })()
            """;

        // Focus sur le champ (curseur à la fin) ; vide : refusé s'il contient déjà un brouillon de l'utilisateur.
        public static String PreparerSaisie(Boolean vide) =>
            $"(() => {{ const e = {Champ}; if (!e) return 'champ de saisie introuvable';" +
            (vide ? " if ((e.value ?? e.innerText).trim()) return 'le champ contient déjà un brouillon';" : "") +
            " e.focus(); const s = getSelection(); if (e.isContentEditable && s) { s.selectAllChildren(e); s.collapseToEnd() }" +
            " return document.activeElement === e || e.contains(document.activeElement) ? 'ok' : 'le champ n\\'a pas pris le focus' })()";

        // Micros (l'arrêt passe par Arrets, avant) : Codex par Cliquer, Antigravity par un clic natif (Uia.Cliquer) ;
        // Antigravity : son mémo vocal.
        public static readonly String[] MicroAntigravity = ["Record voice memo"];

        // Codex : sa dictée en priorité (« Dicter », puis « Transcrire et insérer » : texte dans le champ, sans envoi).
        // Son chat vocal reste le secours lorsque la dictée n'est pas proposée par cette version. Chaque libellé français est
        // suivi de son anglais (messages composer.realtime.* et composer.dictation.* de l'appli).
        public static readonly String[] MicroCodex = ["Dicter", "Dictate", "Démarrer le chat vocal", "Start voice chat"];

        // Une dictée en cours, quelle qu'elle soit : son bouton d'arrêt (chat vocal ou dictée de Codex, mémo vocal
        // d'Antigravity). Le texte dicté reste dans le champ, sans envoi. Le bouton « Transcrire et insérer » se dit
        // « Transcribe and send » en anglais : jamais cliqué sous ce nom, l'arrêt passe par « Stop dictation ».
        public static readonly String[] Arrets =
            ["Arrêter le chat vocal", "Stop voice chat", "Arrêter la dictée", "Stop dictation", "Transcrire et insérer", "Stop recording"];

        // Une dictée ou un enregistrement est en cours : un bouton d'arrêt visible (rien n'est cliqué).
        public static readonly String DicteeEnCours =
            $"[...document.querySelectorAll('[aria-label],[title]')].some(b => b.getClientRects().length && {JsonSerializer.Serialize(Arrets)}.includes(b.getAttribute('aria-label') || b.title))";

        // Clic sur le premier bouton visible au nom exact, dans l'ordre donné ; « ok », sinon la raison.
        public static String Cliquer(String[] noms) =>
            "(() => { const v = [...document.querySelectorAll('[aria-label],[title]')].filter(b => b.getClientRects().length); const nom = b => b.getAttribute('aria-label') || b.title;" +
            $" let b = null; for (const n of {JsonSerializer.Serialize(noms)}) {{ b = v.find(x => nom(x) === n); if (b) break }}" +
            " if (!b) return 'bouton de dictée introuvable'; b.click(); return 'ok' })()";
    }
}

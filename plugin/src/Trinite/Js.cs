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
            " if (r) { r.scrollIntoView({block: 'nearest'}); (r.querySelector('a') || r).click(); return true }" +
            (route ? " if (location.protocol !== 'https:' && location.protocol !== 'http:') return false;" +
                " location.assign(location.origin + '/c/' + encodeURIComponent(id)); return true })()" : " return false })()");

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

        // Menu des modèles d'Antigravity ouvert (sa boîte des quotas comprise) : centre du sélecteur, pour le refermer d'un
        // clic de confiance ; null s'il est fermé. Son état est celui du sélecteur (aria-expanded) : fenêtre cachée, un menu
        // fermé reste dans la page (data-closed) tant que son animation de sortie n'a pas joué.
        public const String MenuModelesAntigravity =
            "(() => { const s = document.querySelector('[data-testid=model-selector-trigger]'); if (s?.getAttribute('aria-expanded') !== 'true') return null;" +
            " const r = s.getBoundingClientRect(); return [r.x + (r.width / 2), r.y + (r.height / 2)] })()";

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

        // Codex : son chat vocal dans la conversation ouverte (« Démarrer le chat vocal ») ; à défaut, la dictée
        // (« Dicter », puis « Transcrire et insérer » : le texte va dans le champ, sans envoi). Chaque libellé français est
        // suivi de son anglais (messages composer.realtime.* et composer.dictation.* de l'appli).
        public static readonly String[] MicroCodex = ["Démarrer le chat vocal", "Start voice chat", "Dicter", "Dictate"];

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

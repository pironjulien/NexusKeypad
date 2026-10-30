using Loupedeck.TrinitePlugin;
using System.Text.Json;
using System.Text.RegularExpressions;

if (args.SequenceEqual(new[] { "--probe-claude" }))
{
    Console.WriteLine(JsonSerializer.Serialize(new { window = Uia.FenetrePrincipale("claude").ToInt64(), usage = Uia.UtilisationClaudeOuverte(), recording = Uia.DicteeClaudeObservee() }));
    return 0;
}

if (args.SequenceEqual(new[] { "--probe-antigravity" }))
{
    var lu = Cdp.Evaluer(Cdp.PortAntigravity, """
        (() => {
          const root = document.querySelector('#root'), css = root && getComputedStyle(root), rect = root?.getBoundingClientRect();
          return JSON.stringify({title:document.title,route:location.pathname,ready:document.readyState,visibility:document.visibilityState,
            rows:[...document.querySelectorAll('[data-testid=conversation-row-sidebar]')].map(r=>r.getAttribute('data-cascade-id')),
            model:document.querySelector('[data-testid=model-selector-trigger]')?.getAttribute('aria-label'),
            root:!!root,rootChildren:root?.childElementCount,rootStyle:css && {display:css.display,visibility:css.visibility,opacity:css.opacity},
            rootRect:rect && {width:rect.width,height:rect.height},textLength:document.body?.innerText.length,
            controls:{buttons:document.querySelectorAll('button').length,inputs:document.querySelectorAll('textarea,[contenteditable=true]').length,
              scripts:document.querySelectorAll('script').length,frames:document.querySelectorAll('iframe').length},
            restore:[...document.querySelectorAll('button')].some(e=>(e.innerText||'').trim()==='Restore'),
            labels:[...document.querySelectorAll('button[aria-label]')].map(e=>e.getAttribute('aria-label')).filter(x=>/conversation|model|archive|record|send|error|retry|back|close/i.test(x)),
            testIds:[...new Set([...document.querySelectorAll('[data-testid]')].map(e=>e.getAttribute('data-testid')))].slice(0,70),
            fault:['TypeError','ReferenceError','SyntaxError','Unexpected Application Error'].filter(x=>(document.body?.innerText||'').includes(x))});
        })()
        """, attente: 2);
    Console.WriteLine(lu is { ValueKind: JsonValueKind.String } texte ? texte.GetString() : "unavailable");
    return lu is { ValueKind: JsonValueKind.String } ? 0 : 1;
}
if (args is ["--real-navigation", var cible] && Guid.TryParse(cible, out _))
{
    var horloge = System.Diagnostics.Stopwatch.StartNew();
    var ouvert = NavigationAntigravity.Ouvrir(cible);
    Console.WriteLine(JsonSerializer.Serialize(new { verified = ouvert, elapsed_ms = horloge.ElapsedMilliseconds, target = cible }));
    return ouvert ? 0 : 1;
}
if (args is ["--probe-navigation", var identite] && Guid.TryParse(identite, out _))
{
    var etat = Cdp.Evaluer(Cdp.PortAntigravity, Js.NavigationAntigravity(identite), attente: 2);
    Console.WriteLine(etat is { ValueKind: JsonValueKind.String } valeur ? valeur.GetString() : "unavailable");
    return etat is { ValueKind: JsonValueKind.String } ? 0 : 1;
}

var controles = 0;
void Vrai(bool condition, string regle) { controles++; if (!condition) throw new InvalidOperationException(regle); }
long temps = 0;
int gestes = 0, lectures = 0;
bool Ouvrir() { gestes++; return true; }
bool Executer(Func<string> lire, Func<bool> geste = null) => NavigationAntigravity.Executer(lire, geste ?? Ouvrir, () => temps, ms => temps += ms);
Vrai(Executer(() => "open") && gestes == 0, "Conversation déjà affichée : aucun clic ni rechargement.");
Vrai(Executer(() => ++lectures < 4 ? null : gestes == 0 ? "ready" : "open") && gestes == 1,
    "Chargement retardé : lectures seules avant un geste unique et ID vérifié.");
temps = gestes = lectures = 0;
Vrai(!Executer(() => null) && gestes == 0 && temps == 5000, "Port muet : délai borné sans geste.");
temps = gestes = lectures = 0;
Vrai(!Executer(() => "ready", () => { gestes++; return false; }) && gestes == 1 && temps == 0,
    "Résultat de transport inconnu : jamais de deuxième clic.");
temps = gestes = lectures = 0;
Vrai(!Executer(() => "ready") && gestes == 1 && temps == 4000,
    "Geste parti sans changement d'ID : échec borné, jamais rejoué.");
temps = gestes = lectures = 0;
Vrai(!Executer(() => { temps += 1000; return null; }) && gestes == 0 && temps <= 6000,
    "Lectures lentes : durée réelle incluse dans le délai.");
var idEncode = Regex.Match(Js.OuvrirAntigravity("an\"id", true), "const id = (\"(?:\\\\.|[^\"])*\");").Groups[1].Value;
Vrai(JsonSerializer.Deserialize<string>(idEncode) == "an\"id", "ID encodé comme une chaîne JSON, sans injection.");
Console.WriteLine($"Navigation Antigravity : {controles} contrôles hors ligne réussis.");
return 0;

using Loupedeck.TrinitePlugin;

var controles = 0;
void Vrai(Boolean valeur, String nom) { controles++; if (!valeur) { throw new InvalidOperationException(nom); } }
var a = "local_04f9c523-7c1a-4c47-a40a-9da08561fdbb";
var b = "local_fef1d477-2678-4fb8-b383-a516ebc9da87";
String Adresse(String id) => "https://claude.ai/epitaxy/" + id;
String Lire(params String[] adresses) => Uia.SessionClaudeDepuisAdresses(adresses);
Vrai(Lire(Adresse(a)) == a, "La route visible de Claude donne son sessionId exact.");
Vrai(Lire("file:///app.asar/window.html", Adresse(a)) == a, "Le document enveloppe n'écrase pas la session exacte.");
Vrai(Lire(Adresse(a), Adresse(a)) == a, "Deux représentations du même document ne sont pas deux sessions.");
Vrai(Lire(Adresse(a), Adresse(b)) is null, "Deux sessions visibles différentes sont ambiguës.");
Vrai(Lire() is null && Lire("Session de même titre") is null, "Aucun titre ne constitue une preuve d'identité.");
Vrai(Lire(Adresse(a) + "?section=test#latest") == a, "Les paramètres d'affichage ne changent pas l'identité.");
Vrai(Lire(Adresse(a.ToUpperInvariant()).Replace("LOCAL_", "local_")) == a, "Les UUID se normalisent sans confondre deux IDs.");
foreach (var invalide in new[]
{
    "http://claude.ai/epitaxy/" + a,
    "https://claude.ai.evil.invalid/epitaxy/" + a,
    "https://claude.ai:8443/epitaxy/" + a,
    new UriBuilder(Adresse(a)) { UserName = "example" }.Uri.AbsoluteUri,
    "https://claude.ai/epitaxy/" + a + "/other",
    "https://claude.ai/epitaxy/local_not-an-id",
    "https://claude.ai/epitaxy/local_------------------------------------",
    "https://claude.ai/customize/plugins/" + a,
    "/epitaxy/" + a,
    null,
}) { Vrai(Lire(invalide) is null, "Adresse étrangère ou non prouvée rejetée : " + invalide); }

Int64 horloge = 0;
var gestes = 0;
var lectures = 0;
Boolean Retarde() => Uia.GestePuisEtat(() => gestes++, () => ++lectures >= 3 ? true : null,
    true, 200, ms => horloge += ms, () => horloge);
Vrai(Retarde() && gestes == 1 && lectures == 3, "Une confirmation tardive relit sans rejouer le geste.");
horloge = 0; gestes = 0; lectures = 0;
Vrai(!Uia.GestePuisEtat(() => gestes++, () => { lectures++; return null; }, false, 200,
    ms => horloge += ms, () => horloge) && gestes == 1 && lectures == 3,
    "Une lecture inconnue ne prouve jamais la fermeture et ne répète pas le clic.");
horloge = 0; gestes = 0;
Vrai(!Uia.GestePuisEtat(() => { gestes++; throw new IOException("après envoi"); }, () => true,
    true, 200, ms => horloge += ms, () => horloge) && gestes == 1,
    "Une erreur d'envoi conserve l'issue inconnue sans second geste.");
horloge = 0; gestes = 0;
Vrai(!Uia.GestePuisEtat(() => gestes++, () => throw new IOException("lecture perdue"), false, 200,
    ms => horloge += ms, () => horloge) && gestes == 1,
    "Une erreur de relecture n'est pas une fermeture confirmée.");
horloge = 0;
Vrai(Uia.ConfirmerEtat(() => false, false, 200, ms => horloge += ms, () => horloge),
    "Un état fermé effectivement relu vaut confirmation.");
horloge = 0; gestes = 0;
Vrai(!Uia.GestePuisEtat(() => gestes++, () => { horloge += 500; return null; }, true, 200,
    ms => horloge += ms, () => horloge) && gestes == 1 && horloge == 500,
    "Une lecture lente consomme le budget, sans délai ni geste supplémentaire.");

var quotasLus = 0;
var identitesLues = 0;
var q = Uia.QuotaClaudeVerifiee(a, () => { identitesLues++; return a; }, () => { quotasLus++; return new(.1, null); });
Vrai(q?.Cinq == .1 && q?.Semaine is null && quotasLus == 1 && identitesLues == 2,
    "La jauge partielle est acceptée uniquement avec la même identité avant et après.");
quotasLus = 0;
Vrai(Uia.QuotaClaudeVerifiee(a, () => b, () => { quotasLus++; return new(.9, .8); }) is null && quotasLus == 0,
    "Une autre session avant lecture ne fournit aucune jauge, même si son titre était identique.");
identitesLues = 0; quotasLus = 0;
Vrai(Uia.QuotaClaudeVerifiee(a, () => ++identitesLues == 1 ? a : b,
    () => { quotasLus++; return new(.9, .8); }) is null && quotasLus == 1 && identitesLues == 2,
    "Un changement de session pendant la lecture invalide la mesure, sans chercher une autre fenêtre.");
identitesLues = 0;
Vrai(Uia.QuotaClaudeVerifiee(a, () => ++identitesLues == 1 ? a : null, () => new(.9, .8)) is null,
    "Une identité devenue illisible ne conserve aucune jauge attribuée au mauvais périmètre.");
quotasLus = 0;
Vrai(Uia.QuotaClaudeVerifiee(null, () => a, () => { quotasLus++; return new(.9, .8); }) is null && quotasLus == 0,
    "Une session attendue inconnue ne choisit pas celle d'une fenêtre arbitraire.");
Vrai(Uia.QuotaClaudeVerifiee(a, () => a, () => null) is null,
    "Une jauge illisible reste inconnue, sans valeur inventée.");
Vrai(Uia.QuotaClaudeVerifiee(a, () => a, () => throw new IOException("fenêtre disparue")) is null,
    "Une erreur pendant la mesure ne déclenche aucun secours sur une autre fenêtre.");
Console.WriteLine($"Parité UIA : {controles} contrôles hors ligne réussis, aucun appel au bureau.");

// Seule dépendance de signature des fonctions de quota UIA non exécutées dans ce banc. Le modèle de quota du pad
// est vérifié par verifier/ ; ce double évite de charger l'API Logi pour tester identité et confirmation.
namespace Loupedeck.TrinitePlugin { public sealed record Quota(Double? Cinq, Double? Semaine); }

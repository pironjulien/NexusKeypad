using Loupedeck.TrinitePlugin;

namespace NexusKeypad.Tests;

// UI Automation proofs, offline (no call to the desktop): Claude's exact session, confirmation without a replayed
// gesture, and a quota only for the same identity before and after reading it.
public sealed class UiaTests
{
    private const String A = "local_04f9c523-7c1a-4c47-a40a-9da08561fdbb";
    private const String B = "local_fef1d477-2678-4fb8-b383-a516ebc9da87";

    private static void Vrai(Boolean valeur, String nom) => Assert.True(valeur, nom);

    private static String Adresse(String id) => "https://claude.ai/epitaxy/" + id;

    private static String Lire(params String[] adresses) => Uia.SessionClaudeDepuisAdresses(adresses);

    [Fact]
    public void Session_Claude_depuis_la_route_visible()
    {
        Vrai(Lire(Adresse(A)) == A, "La route visible de Claude donne son sessionId exact.");
        Vrai(Lire("file:///app.asar/window.html", Adresse(A)) == A, "Le document enveloppe n'écrase pas la session exacte.");
        Vrai(Lire(Adresse(A), Adresse(A)) == A, "Deux représentations du même document ne sont pas deux sessions.");
        Vrai(Lire(Adresse(A), Adresse(B)) is null, "Deux sessions visibles différentes sont ambiguës.");
        Vrai(Lire() is null && Lire("Session de même titre") is null, "Aucun titre ne constitue une preuve d'identité.");
        Vrai(Lire(Adresse(A) + "?section=test#latest") == A, "Les paramètres d'affichage ne changent pas l'identité.");
        Vrai(Lire(Adresse(A.ToUpperInvariant()).Replace("LOCAL_", "local_")) == A, "Les UUID se normalisent sans confondre deux IDs.");
    }

    public static TheoryData<String> AdressesInvalides => new()
    {
        "http://claude.ai/epitaxy/" + A,
        "https://claude.ai.evil.invalid/epitaxy/" + A,
        "https://claude.ai:8443/epitaxy/" + A,
        new UriBuilder(Adresse(A)) { UserName = "example" }.Uri.AbsoluteUri,
        "https://claude.ai/epitaxy/" + A + "/other",
        "https://claude.ai/epitaxy/local_not-an-id",
        "https://claude.ai/epitaxy/local_------------------------------------",
        "https://claude.ai/customize/plugins/" + A,
        "/epitaxy/" + A,
        null,
    };

    [Theory]
    [MemberData(nameof(AdressesInvalides))]
    public void Adresse_etrangere_ou_non_prouvee_rejetee(String invalide) =>
        Vrai(Lire(invalide) is null, "Adresse étrangère ou non prouvée rejetée : " + invalide);

    [Fact]
    public void Confirmation_sans_geste_rejoue()
    {
        Int64 horloge = 0;
        var gestes = 0;
        var lectures = 0;
        Vrai(Uia.GestePuisEtat(() => gestes++, () => ++lectures >= 3 ? true : null, true, 200, ms => horloge += ms, () => horloge)
            && gestes == 1 && lectures == 3, "Une confirmation tardive relit sans rejouer le geste.");
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
    }

    [Fact]
    public void Quota_Claude_seulement_pour_la_meme_identite()
    {
        var quotasLus = 0;
        var identitesLues = 0;
        var q = Uia.QuotaClaudeVerifiee(A, () => { identitesLues++; return A; }, () => { quotasLus++; return new(.1, null); });
        Vrai(q?.Cinq == .1 && q?.Semaine is null && quotasLus == 1 && identitesLues == 2,
            "La jauge partielle est acceptée uniquement avec la même identité avant et après.");
        quotasLus = 0;
        Vrai(Uia.QuotaClaudeVerifiee(A, () => B, () => { quotasLus++; return new(.9, .8); }) is null && quotasLus == 0,
            "Une autre session avant lecture ne fournit aucune jauge, même si son titre était identique.");
        identitesLues = 0; quotasLus = 0;
        Vrai(Uia.QuotaClaudeVerifiee(A, () => ++identitesLues == 1 ? A : B,
            () => { quotasLus++; return new(.9, .8); }) is null && quotasLus == 1 && identitesLues == 2,
            "Un changement de session pendant la lecture invalide la mesure, sans chercher une autre fenêtre.");
        identitesLues = 0;
        Vrai(Uia.QuotaClaudeVerifiee(A, () => ++identitesLues == 1 ? A : null, () => new(.9, .8)) is null,
            "Une identité devenue illisible ne conserve aucune jauge attribuée au mauvais périmètre.");
        quotasLus = 0;
        Vrai(Uia.QuotaClaudeVerifiee(null, () => A, () => { quotasLus++; return new(.9, .8); }) is null && quotasLus == 0,
            "Une session attendue inconnue ne choisit pas celle d'une fenêtre arbitraire.");
        Vrai(Uia.QuotaClaudeVerifiee(A, () => A, () => null) is null,
            "Une jauge illisible reste inconnue, sans valeur inventée.");
        Vrai(Uia.QuotaClaudeVerifiee(A, () => A, () => throw new IOException("fenêtre disparue")) is null,
            "Une erreur pendant la mesure ne déclenche aucun secours sur une autre fenêtre.");
    }
}

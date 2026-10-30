using Loupedeck.TrinitePlugin;
using static NexusKeypad.Tests.Commun;

namespace NexusKeypad.Tests;

// Opening an Antigravity conversation, and the web apps' keys (Gmail, Messages, site).
public sealed class AppliWebTests
{
    [Fact]
    public void Identifiant_Antigravity_passe_en_chaine_JSON()
    {
        // The identifier stays a JSON string (no injection into the script).
        var js = Js.OuvrirAntigravity("abc'); alert(1); ('", route: true);
        Vrai(js.Contains("location.assign", StringComparison.Ordinal), "route /c/<id> si la ligne manque");
        Vrai(!js.Contains("'); alert(1)", StringComparison.Ordinal) && js.Contains("abc" + (Char)92 + "u0027", StringComparison.Ordinal), "identifiant passé en chaîne JSON échappée");
    }

    [Fact]
    public void Non_lus_de_Gmail_et_Messages()
    {
        // The number in brackets in their window title; a Chrome tab does not count.
        Vrai(AppliWeb.Gmail.NonLus(["Codex LB", "Boîte de réception (12) - a@example.com - Gmail"]) == 12, "Gmail : 12 non-lus dans le titre");
        Vrai(AppliWeb.Gmail.NonLus(["Boîte de réception - a@example.com - Gmail"]) == 0, "Gmail sans nombre : 0");
        Vrai(AppliWeb.Gmail.NonLus(["Boîte de réception (3) - Gmail - Google Chrome"]) is null, "onglet Gmail de Chrome : pas une fenêtre d'appli");
        Vrai(AppliWeb.Messages.NonLus(["Messages - Google Messages pour le Web : conversations"]) == 0, "Messages : titre réel à espaces insécables reconnu");
        Vrai(AppliWeb.Messages.NonLus(["(2) Google Messages pour le Web"]) == 2 && AppliWeb.Messages.NonLus([]) is null, "Messages : titre, sinon rien");
        Vrai(AppliWeb.Gmail.NonLus(["(2) Issues · pironjulien/nexus-gmail", "Questions sur l'API Gmail (4)"]) is null
            && AppliWeb.Messages.NonLus(["(3) Messages pour le Web - aide"]) is null, "onglets qui citent Gmail ou Messages : pas de fausse pastille");
    }

    private static Boolean Sous(String page, String adresse) => AppliWeb.Sous(new Uri(page), new Uri(adresse));

    [Fact]
    public void Fenetre_du_site_sous_son_adresse()
    {
        // Site key without a title: its window is the one whose page is under the address (origin, then path by segment).
        Vrai(Sous("https://github.com/pironjulien/NexusKeypad/issues", "https://github.com/pironjulien/NexusKeypad")
            && Sous("https://www.example.org/a", "https://example.org/") && Sous("https://example.org/", "https://example.org"),
            "site : page sous l'adresse, www ignoré, racine");
        Vrai(!Sous("http://localhost:5173/", "http://localhost:3000/") && !Sous("http://example.org/", "https://example.org/"),
            "site : port ou schéma différents → autre site");
        Vrai(!Sous("https://github.com/pironjulien/NexusBrowser", "https://github.com/pironjulien/NexusKeypad")
            && !Sous("https://github.com/pironjulien/NexusKeypad-fork", "https://github.com/pironjulien/NexusKeypad"),
            "site : autre dépôt GitHub, ou chemin qui ne s'arrête pas au segment → autre site");
        Vrai(Sous("https://example.org/view?id=alpha", "https://example.org/view?id=alpha") && !Sous("https://example.org/view?id=beta", "https://example.org/view?id=alpha")
            && !Sous("https://example.org/view", "https://example.org/view?id=alpha") && Sous("https://example.org/view?id=beta", "https://example.org/view"),
            "site : requête de l'adresse exigée à l'identique (aucune requête donnée : toutes acceptées)");
        Vrai(Sous("https://example.org/app#/document/alpha", "https://example.org/app#/document/alpha")
            && !Sous("https://example.org/app#/document/beta", "https://example.org/app#/document/alpha"),
            "site : fragment de l'adresse exigé à l'identique");
    }
}

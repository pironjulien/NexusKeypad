// Usage : dotnet run --project verifier   →   code 0 si toutes les règles tiennent, sinon la liste des échecs.
using System.Runtime.Loader;
using System.Text.Json;
using Loupedeck;
using Loupedeck.TrinitePlugin;

var service = @"C:\Program Files\Logi\LogiPluginService\";
AssemblyLoadContext.Default.Resolving += (_, nom) =>
{
    var chemin = Path.Combine(service, nom.Name + ".dll");
    return File.Exists(chemin) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(chemin) : null;
};

var echecs = new List<String>();
void Vrai(Boolean condition, String regle)
{
    if (!condition)
    {
        echecs.Add(regle);
    }
}

var maintenant = DateTime.UtcNow;
Conversation C(Int32 a, Etat e, Boolean vu = false, Boolean relais = false) => new(a, "id", "titre", e, maintenant, maintenant, vu, relais);

// États notés par les hooks : une question ou une fin restent 12 h, un travail 20 min (au-delà, l'agent est mort).
Vrai(Observateur.Traduire("wait", maintenant.AddMinutes(-1)) == Etat.Attend, "wait récent → attend");
Vrai(Observateur.Traduire("ask", maintenant.AddMinutes(-1)) == Etat.Travaille, "ask → travaille (boîte de fin pas encore posée)");
Vrai(Observateur.Traduire("run", maintenant.AddMinutes(-30)) == Etat.Travaille, "run de 30 min → travaille (commande longue)");
Vrai(Observateur.Traduire("run", maintenant.AddHours(-3)) == Etat.Repos, "run de 3 h → repos");
Vrai(Observateur.Traduire("done", maintenant.AddHours(-1)) == Etat.Fini, "done d'1 h → fini");
Vrai(Observateur.Traduire("done", maintenant.AddHours(-13)) == Etat.Repos, "done de 13 h → repos");

// Clignotement : jamais pour le relais, jamais une fois vu, seulement question ou fin.
Vrai(C(1, Etat.Attend).Alerte, "question non vue → alerte");
Vrai(C(1, Etat.Fini).Alerte, "fin non vue → alerte");
Vrai(!C(1, Etat.Attend, relais: true).Alerte, "question du relais → pas d'alerte");
Vrai(!C(1, Etat.Fini, vu: true).Alerte, "fin vue → pas d'alerte");
Vrai(!C(1, Etat.Travaille).Alerte, "travail → pas d'alerte");

// Ordre d'une touche : question, fin, travail pour l'utilisateur, travail du relais, le reste.
Vrai(Observateur.Rang(C(0, Etat.Attend)) > Observateur.Rang(C(0, Etat.Fini)), "question avant fin");
Vrai(Observateur.Rang(C(0, Etat.Fini)) > Observateur.Rang(C(0, Etat.Travaille)), "fin avant travail");
Vrai(Observateur.Rang(C(0, Etat.Travaille)) > Observateur.Rang(C(0, Etat.Travaille, relais: true)), "travail de l'utilisateur avant relais");

// État d'ensemble d'une IA : appli fermée → fermé ; ouverte sans conversation → repos.
Quota[] sansQuota = [null, null, null];
var instantane = new Instantane([[C(0, Etat.Travaille)], [], []], [1, 0, 0], sansQuota, [true, true, false], false, null, -1);
Vrai(instantane.EtatIA(0) == Etat.Travaille, "conversation qui travaille → travaille");
Vrai(instantane.EtatIA(1) == Etat.Repos, "appli ouverte sans conversation → repos");
Vrai(instantane.EtatIA(2) == Etat.Ferme, "appli fermée → fermé");

// Quotas d'Antigravity lus sur sa page Settings › Models (relevé réel du 20/10) : groupe le plus entamé par fenêtre.
var page = """
Models & Usage
Plan
Your Plan: Google AI Pro
Gemini Models
Weekly Limit Remaining
You have used some of your weekly limit, it will fully refresh in 6 days, 9 hours.
33%
Five Hour Limit Remaining
You have used some of your 5-hour limit, it will fully refresh in 14 minutes.
3%
Claude and GPT models
Weekly Limit Remaining
100%
Five Hour Limit Remaining
100%
""";
var qa = QuotaAntigravity.Analyser(page);
Vrai(Math.Abs((qa?.Cinq ?? -1) - 0.97) < 1e-9, "Antigravity 5 h : 3 % restants = 97 % utilisés");
Vrai(Math.Abs((qa?.Semaine ?? -1) - 0.67) < 1e-9, "Antigravity semaine : 33 % restants = 67 % utilisés");
Vrai(QuotaAntigravity.Analyser("autre dialogue") is null, "page illisible → rien");
var finCinq = (qa?.FinCinq ?? DateTime.MinValue) - DateTime.UtcNow;
Vrai(finCinq > TimeSpan.FromMinutes(13) && finCinq <= TimeSpan.FromMinutes(14), "Antigravity 5 h : remise à zéro dans 14 min");
var finSemaine = (qa?.FinSemaine ?? DateTime.MinValue) - DateTime.UtcNow;
Vrai(finSemaine > new TimeSpan(6, 8, 59, 0) && finSemaine <= new TimeSpan(6, 9, 0, 0), "Antigravity semaine : remise à zéro dans 6 j 9 h");
Vrai(QuotaAntigravity.Delai("it will fully refresh in 4 days, 23 hours.") == new TimeSpan(4, 23, 0, 0)
    && QuotaAntigravity.Delai("Weekly Limit Remaining") is null, "délai de remise à zéro lu en jours, heures, minutes");

// Ouverture d'une conversation d'Antigravity : l'identifiant reste une chaîne JSON (pas d'injection dans le script).
var js = Js.OuvrirAntigravity("abc'); alert(1); ('", route: true);
Vrai(js.Contains("location.assign", StringComparison.Ordinal), "route /c/<id> si la ligne manque");
Vrai(!js.Contains("'); alert(1)", StringComparison.Ordinal) && js.Contains("abc" + (Char)92 + "u0027", StringComparison.Ordinal), "identifiant passé en chaîne JSON échappée");

// Non-lus de Gmail et Messages : le nombre entre parenthèses du titre de leur fenêtre ; un onglet de Chrome ne compte pas.
Vrai(AppliWeb.Gmail.NonLus(["Codex LB", "Boîte de réception (12) - a@example.com - Gmail"]) == 12, "Gmail : 12 non-lus dans le titre");
Vrai(AppliWeb.Gmail.NonLus(["Boîte de réception - a@example.com - Gmail"]) == 0, "Gmail sans nombre : 0");
Vrai(AppliWeb.Gmail.NonLus(["Boîte de réception (3) - Gmail - Google Chrome"]) is null, "onglet Gmail de Chrome : pas une fenêtre d'appli");
Vrai(AppliWeb.Messages.NonLus(["(2) Google Messages pour le Web"]) == 2 && AppliWeb.Messages.NonLus([]) is null, "Messages : titre, sinon rien");
Vrai(AppliWeb.Gmail.NonLus(["(2) Issues · pironjulien/nexus-gmail", "Questions sur l'API Gmail (4)"]) is null
    && AppliWeb.Messages.NonLus(["(3) Messages pour le Web - aide"]) is null, "onglets qui citent Gmail ou Messages : pas de fausse pastille");

// Cache d'images : une scène identique donne des pixels identiques ; la photo ne bouge pas d'un instant à l'autre ;
// sans téléphone, la touche Photo est une horloge animée.
var midi = new DateTime(2026, 10, 24, 12, 34, 56);
Scene S(Double t, Instantane i = null) => new() { Instantane = i ?? instantane, Temps = t, Appuis = Enumerable.Repeat(-9.0, Moteur.Touches).ToArray(), PhotoCopiee = -9, CaptureCollee = -9, Maintenant = midi };
for (var k = 0; k < 9; k++)
{
    Vrai(Rendu.Image(k, S(1.0)).Pixels.AsSpan().SequenceEqual(Rendu.Image(k, S(1.0)).Pixels), $"touche {k + 1} : même scène, mêmes pixels");
}

using (var carre = new BitmapBuilder(Rendu.T, Rendu.T))
{
    carre.Clear(new BitmapColor(200, 120, 40));
    var avecPhoto = instantane with { Photo = new("/sdcard/DCIM/Camera/x.jpg", "x.jpg", BitmapImage.FromArray(carre.ToArray(BitmapImageFormat.Png))) };
    Vrai(Rendu.Image(Moteur.Photo, S(0.1, avecPhoto)).Pixels.AsSpan().SequenceEqual(Rendu.Image(Moteur.Photo, S(3.7, avecPhoto)).Pixels), "photo immobile");
}

Vrai(!Rendu.Image(Moteur.Photo, S(0.1, instantane with { Photo = null })).Pixels.AsSpan().SequenceEqual(Rendu.Image(Moteur.Photo, S(0.6, instantane with { Photo = null })).Pixels), "sans téléphone : horloge animée");
Vrai(!Rendu.Image(0, S(0.1)).Pixels.AsSpan().SequenceEqual(Rendu.Image(0, S(0.6)).Pixels), "tête qui travaille animée");

foreach (var e in echecs)
{
    Console.WriteLine($"ÉCHEC : {e}");
}

Console.WriteLine(echecs.Count == 0 ? "verifier : toutes les règles tiennent." : $"verifier : {echecs.Count} échec(s).");
return echecs.Count == 0 ? 0 : 1;

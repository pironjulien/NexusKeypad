// Usage : dotnet run --project verifier   →   code 0 si toutes les règles tiennent, sinon la liste des échecs.
using System.Runtime.InteropServices;
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

// Essai réel du presse-papiers (écrase son contenu : jamais dans test.ps1) : dotnet run --project verifier -- presse-papiers
if (args.Contains("presse-papiers"))
{
    var dossier = Directory.CreateTempSubdirectory("nexus-presse").FullName;
    var png = Path.Combine(dossier, "essai.png");
    var webp = Path.Combine(dossier, "essai.webp");
    using (var image = new SkiaSharp.SKBitmap(7, 3))
    {
        image.Erase(SkiaSharp.SKColors.Red);
        File.WriteAllBytes(png, image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100).ToArray());
        File.WriteAllBytes(webp, image.Encode(SkiaSharp.SKEncodedImageFormat.Webp, 100).ToArray());
    }

    Vrai(PressePapiers.Poser(png, webp), "presse-papiers : trois formats posés");
    Vrai(Essai.Formats().SetEquals([8u, 15u, Essai.Png]), "presse-papiers : PNG, CF_DIB et CF_HDROP lisibles");
    Vrai(Essai.Fichier() == webp, "presse-papiers : fichier joint = le WebP");
    Vrai(Essai.Dib() == (7, 3, 32), "presse-papiers : DIB 7×3 en 32 bits");
    Directory.Delete(dossier, true);
    Console.WriteLine(echecs.Count == 0 ? "presse-papiers : essai réussi." : String.Join("\n", echecs));
    return echecs.Count == 0 ? 0 : 1;
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
var finCinq = (qa?.Groupes.Single(g => g.Cinq && g.Utilise > 0.9).Fin ?? DateTime.MinValue) - DateTime.UtcNow;
Vrai(finCinq > TimeSpan.FromMinutes(13) && finCinq <= TimeSpan.FromMinutes(14), "Antigravity 5 h : remise à zéro dans 14 min");
var finSemaine = (qa?.Groupes.Single(g => !g.Cinq && g.Utilise > 0.6).Fin ?? DateTime.MinValue) - DateTime.UtcNow;
Vrai(finSemaine > new TimeSpan(6, 8, 59, 0) && finSemaine <= new TimeSpan(6, 9, 0, 0), "Antigravity semaine : remise à zéro dans 6 j 9 h");
// Groupe le plus entamé remis à zéro : le suivant prend la jauge (Gemini 80 % fini, Claude et GPT 50 % encore 4 h).
var instant = DateTime.UtcNow;
var groupes = new Quota(null, null)
{
    Groupes = [new(true, 0.8, instant.AddMinutes(-1)), new(true, 0.5, instant.AddHours(4)), new(false, 0.6, null)],
};
var actuel = QuotaAntigravity.Actuel(groupes, instant.AddDays(-8));
Vrai(actuel.Cinq == 0.5 && actuel.Semaine == 0, "remise à zéro par groupe ; sans heure, au bout de sa durée");
Vrai(QuotaAntigravity.Actuel(groupes, instant).Semaine == 0.6, "semaine sans heure, lue à l'instant : gardée");
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

// Relecture du presse-papiers pour l'essai réel (API Win32, comme les applis qui collent).
static class Essai
{
    public static readonly UInt32 Png = RegisterClipboardFormat("PNG");

    public static HashSet<UInt32> Formats()
    {
        var formats = new HashSet<UInt32>();
        if (OpenClipboard(IntPtr.Zero))
        {
            for (var f = EnumClipboardFormats(0); f != 0; f = EnumClipboardFormats(f))
            {
                formats.Add(f);
            }

            CloseClipboard();
        }

        formats.IntersectWith([8u, 15u, Png]);
        return formats;
    }

    public static String Fichier()
    {
        if (!OpenClipboard(IntPtr.Zero))
        {
            return null;
        }

        try
        {
            var depot = GetClipboardData(15);
            var nom = new System.Text.StringBuilder(1024);
            return DragQueryFile(depot, 0, nom, nom.Capacity) > 0 ? nom.ToString() : null;
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static (Int32, Int32, Int16) Dib()
    {
        if (!OpenClipboard(IntPtr.Zero))
        {
            return default;
        }

        try
        {
            var adresse = GlobalLock(GetClipboardData(8));
            var resultat = (Marshal.ReadInt32(adresse, 4), Marshal.ReadInt32(adresse, 8), Marshal.ReadInt16(adresse, 14));
            GlobalUnlock(GetClipboardData(8));
            return resultat;
        }
        finally
        {
            CloseClipboard();
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern UInt32 RegisterClipboardFormat(String nom);
    [DllImport("user32.dll")] private static extern Boolean OpenClipboard(IntPtr h);
    [DllImport("user32.dll")] private static extern Boolean CloseClipboard();
    [DllImport("user32.dll")] private static extern UInt32 EnumClipboardFormats(UInt32 format);
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(UInt32 format);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern UInt32 DragQueryFile(IntPtr depot, UInt32 i, System.Text.StringBuilder nom, Int32 max);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr m);
    [DllImport("kernel32.dll")] private static extern Boolean GlobalUnlock(IntPtr m);
}

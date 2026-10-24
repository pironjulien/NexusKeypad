// Usage : dotnet run -- <dossier> [photo.jpg]   →   une planche PNG par situation (9 touches, 4 instants chacune).
// Langue des jours et des mois : celle du poste, ou NEXUS_APERCU_CULTURE (en-US pour les captures du README anglais).
using System.Globalization;
using System.Runtime.Loader;
using Loupedeck;
using Loupedeck.TrinitePlugin;

if (Environment.GetEnvironmentVariable("NEXUS_APERCU_CULTURE") is { Length: > 0 } langue)
{
    CultureInfo.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo(langue);
}

var service = @"C:\Program Files\Logi\LogiPluginService\";
AssemblyLoadContext.Default.Resolving += (_, nom) =>
{
    var chemin = Path.Combine(service, nom.Name + ".dll");
    return File.Exists(chemin) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(chemin) : null;
};

var sortie = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(sortie);
var maintenant = DateTime.UtcNow;
Photo photo = null;
if (args.Length > 1)
{
    // La miniature du plugin lui-même (recadrage et orientation EXIF compris).
    photo = new("/sdcard/DCIM/Camera/x.jpg", args[1], Telephone.Miniature(args[1]));
}

Conversation C(Int32 a, String titre, Etat e, Double depuisS = 5, Boolean vu = false, Boolean relais = false) =>
    new(a, "id", titre, e, maintenant.AddSeconds(-depuisS), maintenant, vu, relais);

Conversation[][] actives =
[
    [C(0, "Configuration MX Pad et voie centrale", Etat.Attend), C(0, "Nexus Windows child session", Etat.Travaille)],
    [C(1, "Revue du relais P2", Etat.Fini), C(1, "Traiter le message Nexus Relay", Etat.Travaille)],
    [C(2, "Analyse du projet", Etat.Attend)],
];
Conversation[][] calmes =
[
    [C(0, "Configuration MX Pad", Etat.Fini, 300)],
    [C(1, "Relais · nexuswindows", Etat.Travaille, 120, relais: true)],
    [],
];
Conversation[][] relais =
[
    [C(0, "Revue NexusKeypad", Etat.Travaille)],
    [C(1, "Relais · nexuskeypad", Etat.Travaille, 60, relais: true)],
    [C(2, "Relais · nexuskeypad", Etat.Travaille, 60, relais: true)],
];
Quota[] quotas = [new(0.15, 0.70), new(0.27, 0.07), new(0.63, null)];

var situations = new (String Nom, Instantane I, Double[] Temps)[]
{
    ("actif", new(actives, [5, 2, 1], quotas, [true, true, true], false, photo, 0), [0.0, 0.3, 0.6, 1.1]),
    ("relais", new(relais, [1, 1, 1], quotas, [true, true, true], false, photo, -1), [0.2, 0.9, 1.6, 2.4]),
    ("calme", new(calmes, [3, 0, 0], [quotas[0], quotas[1], null], [true, true, false], false, photo, -1), [0.0, 0.3, 4.1, 2.2]),
};

foreach (var (nom, instantane, temps) in situations)
{
    // Planche : 4 colonnes d'instants, chacune une grille 3 × 3 de touches de 116 px séparées de 16 px.
    const Int32 T = 116, E = 16, Grille = (3 * T) + (2 * E), Marge = 24;
    using var planche = new BitmapBuilder((temps.Length * (Grille + Marge)) + Marge, Grille + (2 * Marge));
    planche.Clear(new BitmapColor(217, 214, 207));
    for (var c = 0; c < temps.Length; c++)
    {
        var scene = new Scene { Instantane = instantane, Temps = temps[c], Appuis = Enumerable.Repeat(-9.0, Moteur.Touches).ToArray(), PhotoCopiee = -9, CaptureCollee = -9 };
        for (var k = 0; k < 9; k++)
        {
            planche.DrawImage(Rendu.Png(Moteur.Disposition[k], scene), Marge + (c * (Grille + Marge)) + (k % 3 * (T + E)), Marge + (k / 3 * (T + E)), BitmapRotation.None);
        }
    }

    File.WriteAllBytes(Path.Combine(sortie, $"{nom}.png"), planche.ToArray(BitmapImageFormat.Png));
    Console.WriteLine($"{nom}.png");
}

// Gros plan des trois têtes (touches 1-3, × 3) pour juger le pixel art.
var gros = new Scene { Instantane = situations[0].I, Temps = 0.3, Appuis = Enumerable.Repeat(-9.0, Moteur.Touches).ToArray(), PhotoCopiee = -9, CaptureCollee = -9 };
using var zoom = new BitmapBuilder(3 * 348, 348);
for (var k = 0; k < 3; k++)
{
    zoom.DrawImage(Rendu.Png(Moteur.Disposition[k], gros, 348), k * 348, 0, BitmapRotation.None);
}

File.WriteAllBytes(Path.Combine(sortie, "tetes.png"), zoom.ToArray(BitmapImageFormat.Png));

// Planche des états : une ligne par état (repos, travaille, attend, fini, fermé), pour chaque IA 4 instants.
Etat[] etats = [Etat.Repos, Etat.Travaille, Etat.Attend, Etat.Fini, Etat.Ferme];
Double[] instants = [0.0, 0.35, 0.7, 1.3];
using var tableau = new BitmapBuilder(3 * instants.Length * 120, etats.Length * 120);
tableau.Clear(new BitmapColor(217, 214, 207));
for (var ligne = 0; ligne < etats.Length; ligne++)
{
    var e = etats[ligne];
    Conversation[][] une = [.. Enumerable.Range(0, 3).Select(a => e is Etat.Repos or Etat.Ferme ? Array.Empty<Conversation>() : [C(a, "état", e)])];
    var ouverte = Enumerable.Repeat(e != Etat.Ferme, 3).ToArray();
    var i = new Instantane(une, [1, 1, 1], quotas, ouverte, false, photo, -1);
    for (var a = 0; a < 3; a++)
    {
        for (var c = 0; c < instants.Length; c++)
        {
            var scene = new Scene { Instantane = i, Temps = instants[c], Appuis = Enumerable.Repeat(-9.0, Moteur.Touches).ToArray(), PhotoCopiee = -9, CaptureCollee = -9 };
            tableau.DrawImage(Rendu.Png(a, scene), ((a * instants.Length) + c) * 120, ligne * 120, BitmapRotation.None);
        }
    }
}

File.WriteAllBytes(Path.Combine(sortie, "etats.png"), tableau.ToArray(BitmapImageFormat.Png));
Console.WriteLine("etats.png");

// Page 2 (le poste) : son qui joue avec mails et messages en attente, puis son coupé sans téléphone ; 2 instants chacun.
(Poste P, Boolean Store)[] postes =
[
    (new(0.42, false, 0.55, 0.23, 0.61) { Mails = 3, Messages = 1 }, true),
    (new(0.42, true, 0, 0.88, 0.93), false),
];
using var page2 = new BitmapBuilder(2 * 2 * ((3 * 116) + 40), (3 * 116) + 32);
page2.Clear(new BitmapColor(217, 214, 207));
for (var v = 0; v < postes.Length; v++)
{
    for (var c = 0; c < 2; c++)
    {
        var temps = 20 + (c * 0.4);
        var scene = new Scene
        {
            Instantane = situations[0].I, Temps = temps, Appuis = Enumerable.Repeat(-9.0, Moteur.Touches).ToArray(), PhotoCopiee = -9,
            CaptureCollee = -9, Poste = postes[v].P, MiseAJour = postes[v].Store ? -99 : temps - 5,
        };
        for (var k = 0; k < 9; k++)
        {
            var x = (((v * 2) + c) * ((3 * 116) + 40)) + 16 + (k % 3 * 118);
            page2.DrawImage(Rendu.Png(Moteur.Disposition[9 + k], scene), x, 16 + (k / 3 * 118), BitmapRotation.None);
        }
    }
}

File.WriteAllBytes(Path.Combine(sortie, "page2.png"), page2.ToArray(BitmapImageFormat.Png));
Console.WriteLine("page2.png");

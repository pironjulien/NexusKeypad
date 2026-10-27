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

// Icône de chaque édition (metadata/Icon256x256.png) : l'icône de la famille Nexus, le pad dans le médaillon de
// julienpiron.fr (kit NexusCommunication\icones, nexuskeypad-mark-256.png), réduite dans les 192 px du centre
// sur fond transparent (règle de Logitech pour Options+ et le Marketplace).
// Usage : dotnet run -- icone <nexuskeypad-mark-256.png> <fichier.png>
if (args is ["icone", var marque, var icone])
{
    using var b = new BitmapBuilder(256, 256);
    b.Clear(new BitmapColor(0, 0, 0, 0));
    b.DrawImage(BitmapImage.FromArray(File.ReadAllBytes(marque)), 32, 32, 192, 192, BitmapRotation.None);
    File.WriteAllBytes(icone, b.ToArray(BitmapImageFormat.Png));
    return;
}

// Images successives (8 par seconde comme sur le pad, 4 s) de la ligne du haut dans chaque état, puis pendant un relais :
// <dossier>/<nom>/000.png… à assembler en animations. Usage : dotnet run -- animations <dossier>
if (args is ["animations", var dossierAnim])
{
    var d = DateTime.UtcNow;
    Conversation E(Int32 a, Etat e, Boolean relaisChat = false) => new(a, "id", "anim", e, d, d, false, relaisChat); // état qui vient de changer : célébration et secousse visibles
    var lignes = new (String Nom, Etat Etat, Boolean Ouverte, Int32 Dictee, Double Lance)[]
    {
        ("repos", Etat.Repos, true, -1, 99), ("travaille", Etat.Travaille, true, -1, 99), ("question", Etat.Attend, true, -1, 99),
        ("autorisation", Etat.Autorisation, true, -1, 99), ("fini", Etat.Fini, true, -1, 99), ("erreur", Etat.Erreur, true, -1, 99),
        ("limite", Etat.Limite, true, -1, 99), ("ferme", Etat.Ferme, false, -1, 99), ("ecoute", Etat.Repos, true, 0, 99),
        ("reveil", Etat.Ferme, false, -1, 0), ("relais", Etat.Travaille, true, -1, 99),
    };
    const Int32 Cote = 232, Ecart = 72; // touches × 2, écart entre touches à la même échelle
    foreach (var (nom, etat, ouverte, dictee, lance) in lignes)
    {
        var estRelais = nom == "relais";

        var dossier = Path.Combine(dossierAnim, nom);
        Directory.CreateDirectory(dossier);
        for (var f = 0; f < (estRelais ? 58 : 32); f++) // le relais : un aller-retour complet (3,6 s)
        {
            using var image = new BitmapBuilder((3 * Cote) + (2 * Ecart) + 48, Cote + 48);
            image.Clear(new BitmapColor(24, 24, 28));
            for (var k = 0; k < 3; k++)
            {
                var agent = Moteur.Disposition[k];
                Conversation[][] conversations = estRelais
                    ? [[E(0, Etat.Travaille)], [E(1, Etat.Travaille, true)], [E(2, Etat.Travaille, true)]]
                    : [.. Enumerable.Range(0, 3).Select(a => etat is Etat.Repos or Etat.Ferme ? Array.Empty<Conversation>() : [E(a, etat)])];
                var jauges = etat == Etat.Limite ? new Quota[] { new(1, 0.5), new(1, 0.5), new(1, 0.5) } : [null, null, null];
                var i = new Instantane(conversations, [1, 1, 1], jauges, Enumerable.Repeat(ouverte, 3).ToArray(), false, null, -1)
                {
                    Dictee = dictee >= 0, DicteeIa = dictee >= 0 ? agent : -1,
                };
                var scene = new Scene
                {
                    Instantane = i, Temps = 10 + (f / 8.0), Appuis = Enumerable.Repeat(-9.0, Moteur.Touches).ToArray(), PhotoCopiee = -9,
                    CaptureCollee = -9, Maintenant = d.AddSeconds(f / 8.0).ToLocalTime(),
                    Lancements = Enumerable.Repeat(lance + (f / 8.0), 3).ToArray(),
                };
                image.DrawImage(BitmapImage.FromArray(Rendu.Png(agent, scene, Cote)), 24 + (k * (Cote + Ecart)), 24, Cote, Cote, BitmapRotation.None);
            }

            File.WriteAllBytes(Path.Combine(dossier, $"{f:D3}.png"), image.ToArray(BitmapImageFormat.Png));
        }

        Console.WriteLine(nom);
    }

    return;
}

// Sprites pour la vidéo de l'écosystème : les trois personnages dans chaque état, les messagers du relais et les planches
// des situations, en suites de PNG à 30 i/s couvrant un cycle d'animation, + index.json. Dessinés par le plugin lui-même
// à sa taille (aucun redessin), puis agrandis au plus proche voisin (pixel art net, sans lissage) ; fond transparent
// sauf pour les planches. Le cycle est cherché image par image : la plus courte période qui se répète à l'identique
// (« exacte »), sinon celle qui retombe au plus près de la première image : à l'identique (« fermée ») ou presque
// (« approchée », écart en part de pixels). Usage : dotnet run -- sprites <dossier>
// Avec --photo <photo.jpg> : seulement la planche « photo » (la situation active, cette photo sur la touche Photo, en
// miniature du plugin lui-même), ajoutée à l'index.json existant.
if (args is ["sprites", _] or ["sprites", _, "--photo", _])
{
    var dossierSprites = args[1];
    var photoSprites = args.Length > 3 ? new Photo("/sdcard/DCIM/Camera/x.jpg", args[3], Telephone.Miniature(args[3])) : null;
    const Int32 Fps = 30, MaxImages = 12 * Fps;
    var d = DateTime.UtcNow;
    var fichierIndex = Path.Combine(dossierSprites, "index.json");
    var index = photoSprites is not null && File.Exists(fichierIndex)
        ? System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(fichierIndex))!.AsObject()
        : new System.Text.Json.Nodes.JsonObject { ["fps"] = Fps, ["sequences"] = new System.Text.Json.Nodes.JsonArray() };

    // Rend la suite (dessin natif de largeur × hauteur, agrandi × echelle), cherche son cycle, écrit les images du cycle.
    void Suite(String chemin, Int32 largeur, Int32 hauteur, Int32 echelle, Boolean transparent, Func<Double, Byte[]> dessin, Boolean boucle = true, Int32 images = 0)
    {
        SkiaSharp.SKBitmap Agrandi(Byte[] png)
        {
            using var natif = SkiaSharp.SKBitmap.Decode(png);
            return natif.Resize(new SkiaSharp.SKImageInfo(largeur * echelle, hauteur * echelle, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Unpremul), SkiaSharp.SKFilterQuality.None);
        }

        var natifs = new List<Byte[]>();
        var empreintes = new List<String>();
        var n = images;
        var exacte = true;
        var ecart = 0.0;
        if (boucle)
        {
            Byte[] premiers = null;
            var meilleur = (N: 0, Ecart: Double.MaxValue);
            for (var f = 0; f < 2 * MaxImages; f++)
            {
                var png = dessin(10 + ((Double)f / Fps));
                natifs.Add(png);
                using var bmp = SkiaSharp.SKBitmap.Decode(png);
                var octets = bmp.Bytes;
                empreintes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(octets)));
                premiers ??= octets;
                if (f >= Fps && f <= MaxImages)
                {
                    var differents = 0;
                    for (var k = 0; k < octets.Length; k += 4)
                    {
                        if (octets[k] != premiers[k] || octets[k + 1] != premiers[k + 1] || octets[k + 2] != premiers[k + 2] || octets[k + 3] != premiers[k + 3])
                        {
                            differents++;
                        }
                    }

                    var part = differents / (octets.Length / 4.0);
                    if (part < meilleur.Ecart)
                    {
                        meilleur = (f, part);
                    }
                }

            }

            // Période exacte : la plus courte p telle que toutes les images rendues (24 s) se répètent avec un décalage p.
            // (Les animations avancent par paliers : des images voisines identiques ne font pas une période.)
            for (var p = 1; p <= MaxImages && n == 0; p++)
            {
                if (Enumerable.Range(0, empreintes.Count - p).All(k => empreintes[k] == empreintes[k + p]))
                {
                    n = p;
                }
            }

            if (n == 0)
            {
                (n, exacte, ecart) = (meilleur.N, false, meilleur.Ecart);
            }
        }
        else
        {
            for (var f = 0; f < n; f++)
            {
                natifs.Add(dessin((Double)f / Fps));
            }
        }

        Directory.CreateDirectory(Path.Combine(dossierSprites, chemin));
        var (bas, gauche, droite) = (0, Int32.MaxValue, 0);
        for (var f = 0; f < n; f++)
        {
            using var grand = Agrandi(natifs[f]);
            if (transparent)
            {
                for (var y = 0; y < grand.Height; y++)
                {
                    for (var x = 0; x < grand.Width; x++)
                    {
                        if (grand.GetPixel(x, y).Alpha > 0)
                        {
                            (bas, gauche, droite) = (Math.Max(bas, y + 1), Math.Min(gauche, x), Math.Max(droite, x + 1));
                        }
                    }
                }
            }

            using var fichier = File.Create(Path.Combine(dossierSprites, chemin, $"{f:D3}.png"));
            grand.Encode(fichier, SkiaSharp.SKEncodedImageFormat.Png, 100);
        }

        var entree = new System.Text.Json.Nodes.JsonObject
        {
            ["chemin"] = chemin.Replace('\\', '/'), ["images"] = n, ["duree_s"] = Math.Round((Double)n / Fps, 3),
            ["largeur"] = largeur * echelle, ["hauteur"] = hauteur * echelle, ["echelle"] = echelle,
            ["boucle"] = !boucle ? "non" : exacte ? "exacte" : ecart == 0 ? "fermée" : "approchée",
        };
        if (boucle && !exacte && ecart > 0)
        {
            entree["ecart_boucle"] = Math.Round(ecart, 4);
        }

        if (transparent && bas > 0)
        {
            entree["ancrage_sol"] = new System.Text.Json.Nodes.JsonObject { ["x"] = (gauche + droite) / 2, ["y"] = bas };
        }

        var sequences = index["sequences"]!.AsArray();
        foreach (var ancienne in sequences.Where(x => x!["chemin"]!.GetValue<String>() == entree["chemin"]!.GetValue<String>()).ToList())
        {
            sequences.Remove(ancienne);
        }

        sequences.Add(entree);
        Console.WriteLine($"{chemin} : {n} images, boucle {entree["boucle"]}");
    }

    // 1. Les personnages, comme sur leur touche (dessin de 80 px de large, 5 px par case), dans un cadre de 160 × 160
    // qui garde leurs signes et les feux d'artifice ; × 7 : personnage de 350 à 490 px de haut, cadre de 1120 px.
    String[] noms = ["clawd", "codex", "antigravity"];
    var tout = photoSprites is null;
    var etatsSprites = new (String Nom, Etat Etat, Boolean Dort)[]
    {
        ("repos", Etat.Repos, false), ("travaille", Etat.Travaille, false), ("attend", Etat.Attend, false), ("fini", Etat.Fini, false),
        ("autorisation", Etat.Autorisation, false), ("erreur", Etat.Erreur, false), ("limite", Etat.Limite, false), ("dort", Etat.Repos, true),
    };
    const Int32 Cadre = 160;
    for (var a = 0; a < 3 && tout; a++)
    {
        var hauteurDessin = a switch { 0 => 10, 1 => 12, _ => 14 } * 5;
        var (x0, y0) = ((Cadre - 80) / 2, (Cadre - hauteurDessin) / 2);
        foreach (var (nomEtat, etat, dort) in etatsSprites)
        {
            var agent = a;
            Byte[] Dessin(Double t, Double depuis)
            {
                using var b = new BitmapBuilder(Cadre, Cadre);
                b.Clear(new BitmapColor(0, 0, 0, 0));
                Rendu.Personnage(b, agent, x0, y0, 5, etat, t, dort, depuis);
                return b.ToArray(BitmapImageFormat.Png);
            }

            Suite(Path.Combine(noms[a], nomEtat), Cadre, Cadre, 7, true, t => Dessin(t, 99));
            if (etat == Etat.Fini)
            {
                // L'arrivée du « fini » (feux d'artifice des 4 premières secondes), une fois, sans boucle.
                Suite(Path.Combine(noms[a], "fini-arrivee"), Cadre, Cadre, 7, true, t => Dessin(t, t), boucle: false, images: 4 * Fps);
            }
        }
    }

    // 2. Les messagers du relais (2 px par case sur le pad), qui trottent ou sautillent ; cadre 40 × 32, × 24 : messager
    // d'environ 480 px de haut.
    var petitClawd = typeof(Rendu).GetMethod("PetitClawd", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
    var petitPair = typeof(Rendu).GetMethod("PetitPair", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
    for (var a = 0; a < 3 && tout; a++)
    {
        var agent = a;
        Suite(Path.Combine("messagers", noms[a]), 40, 32, 24, true, t =>
        {
            using var b = new BitmapBuilder(40, 32);
            b.Clear(new BitmapColor(0, 0, 0, 0));
            _ = agent == 0 ? petitClawd.Invoke(null, [b, 20, 6, t]) : petitPair.Invoke(null, [b, agent, 20, 6, t]);
            return b.ToArray(BitmapImageFormat.Png);
        });
    }

    // 3. Les planches 9 touches des situations (celles de l'aperçu), horloge figée pour que la boucle se referme ; × 2.
    Conversation S(Int32 a, String titre, Etat e, Double depuisS = 5, Boolean relaisChat = false) =>
        new(a, "id", titre, e, d.AddSeconds(-depuisS), d, false, relaisChat);
    Quota[] q = [new(0.15, 0.70), new(0.27, 0.07), new(0.63, null)];
    Conversation[][] actifs = [[S(0, "Configuration", Etat.Attend), S(0, "Session", Etat.Travaille)], [S(1, "Revue", Etat.Fini), S(1, "Relais", Etat.Travaille)], [S(2, "Analyse", Etat.Attend)]];
    var planches = !tout ? [("photo", new Instantane(actifs, [5, 2, 1], q, [true, true, true], false, photoSprites, 0))] : new (String Nom, Instantane I)[]
    {
        ("actif", new(actifs, [5, 2, 1], q, [true, true, true], false, null, 0)),
        ("relais", new([[S(0, "Revue", Etat.Travaille)], [S(1, "Relais · nexuskeypad", Etat.Travaille, 60, true)], [S(2, "Relais · nexuskeypad", Etat.Travaille, 60, true)]], [1, 1, 1], q, [true, true, true], false, null, -1)),
        ("calme", new([[S(0, "Configuration", Etat.Fini, 300)], [S(1, "Relais · nexuswindows", Etat.Travaille, 120, true)], []], [3, 0, 0], [q[0], q[1], null], [true, true, false], false, null, -1)),
    };
    const Int32 Tp = 116, Ep = 16, Grille = (3 * Tp) + (2 * Ep);
    foreach (var (nom, instantane) in planches)
    {
        Suite(Path.Combine("planches", nom), Grille, Grille, 2, false, t =>
        {
            using var b = new BitmapBuilder(Grille, Grille);
            b.Clear(new BitmapColor(0, 0, 0, 0));
            var scene = new Scene
            {
                Instantane = instantane, Temps = t, Appuis = Enumerable.Repeat(-9.0, Moteur.Touches).ToArray(), PhotoCopiee = -9, CaptureCollee = -9,
                Maintenant = d.ToLocalTime(),
            };
            for (var k = 0; k < 9; k++)
            {
                b.DrawImage(Rendu.Png(Moteur.Disposition[k], scene), k % 3 * (Tp + Ep), k / 3 * (Tp + Ep), BitmapRotation.None);
            }

            return b.ToArray(BitmapImageFormat.Png);
        });
    }

    File.WriteAllText(fichierIndex, index.ToJsonString(new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    return;
}

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

// Planche des états : une ligne par état, pour chaque IA 4 instants ; puis la dictée (il écoute) et le réveil (appli
// lancée par le pad, en cours de démarrage).
Etat[] etats = [Etat.Repos, Etat.Travaille, Etat.Attend, Etat.Autorisation, Etat.Fini, Etat.Erreur, Etat.Limite, Etat.Ferme];
Double[] instants = [0.0, 0.35, 0.7, 1.3];
using var tableau = new BitmapBuilder(3 * instants.Length * 120, (etats.Length + 2) * 120);
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

for (var ligne = 0; ligne < 2; ligne++)
{
    var dictee = ligne == 0;
    for (var a = 0; a < 3; a++)
    {
        var i = new Instantane([[], [], []], [0, 0, 0], quotas, Enumerable.Repeat(dictee, 3).ToArray(), false, photo, -1) { Dictee = dictee, DicteeIa = dictee ? a : -1 };
        for (var c = 0; c < instants.Length; c++)
        {
            var scene = new Scene
            {
                Instantane = i, Temps = instants[c], Appuis = Enumerable.Repeat(-9.0, Moteur.Touches).ToArray(), PhotoCopiee = -9, CaptureCollee = -9,
                Lancements = dictee ? [99, 99, 99] : Enumerable.Repeat(new[] { 0.3, 1.2, 2.0, 4.0 }[c], 3).ToArray(),
            };
            tableau.DrawImage(Rendu.Png(a, scene), ((a * instants.Length) + c) * 120, (etats.Length + ligne) * 120, BitmapRotation.None);
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

// Les 18 touches d'un coup d'œil, par situation : page 1 à gauche, page 2 à droite (pour les planches avant/après).
foreach (var (nom, instantane, temps) in situations)
{
    using var pad = new BitmapBuilder((2 * ((3 * 116) + 32)) + 24, (3 * 116) + 64);
    pad.Clear(new BitmapColor(217, 214, 207));
    var scene = new Scene
    {
        Instantane = instantane, Temps = temps[1], Appuis = Enumerable.Repeat(-9.0, Moteur.Touches).ToArray(), PhotoCopiee = -9,
        CaptureCollee = -9, Poste = postes[0].P, MiseAJour = -99,
    };
    for (var k = 0; k < 18; k++)
    {
        var x = 16 + (k / 9 * ((3 * 116) + 40)) + (k % 3 * 118);
        pad.DrawImage(Rendu.Png(Moteur.Disposition[k], scene), x, 16 + (k % 9 / 3 * 118), BitmapRotation.None);
    }

    File.WriteAllBytes(Path.Combine(sortie, $"pad-{nom}.png"), pad.ToArray(BitmapImageFormat.Png));
    Console.WriteLine($"pad-{nom}.png");
}

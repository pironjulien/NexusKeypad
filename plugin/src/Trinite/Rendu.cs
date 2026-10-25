namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.Collections.Generic;

    // Dessin pixel art des touches, calculé depuis l'horloge (une image en retard montre quand même le bon instant).
    // Repère : touche de 116 × 116 ; les autres tailles sont mises à l'échelle.
    public static partial class Rendu
    {
        public const Int32 T = 116;
        private const Int32 DecalagePhoto = 3; // vers la droite, voir TouchePhoto

        private static readonly BitmapColor Fond = new(13, 13, 16);
        private static readonly BitmapColor Texte = new(229, 231, 235);
        private static readonly BitmapColor Gris = new(156, 163, 175);
        private static readonly BitmapColor Eteint = new(74, 74, 74);
        private static readonly BitmapColor EteintSombre = new(34, 34, 34);
        private static readonly BitmapColor Orange = new(217, 119, 87);
        private static readonly BitmapColor Encre = new(26, 26, 26);
        private static readonly BitmapColor Nuit = new(26, 20, 64);
        private static readonly BitmapColor Ambre = new(251, 191, 36);
        private static readonly BitmapColor Vert = new(74, 222, 128);
        private static readonly BitmapColor Bleu = new(96, 165, 250);
        private static readonly BitmapColor Ciel = new(8, 12, 28);
        private static readonly BitmapColor Rose = new(236, 72, 153);
        private static readonly BitmapColor Cyan = new(34, 211, 238);
        private static readonly BitmapColor Rouge = new(239, 68, 68);

        private const Int32 Marge = 7; // autour du contenu de chaque touche
        private const Int32 Bas = 4; // décalage vers le bas : le cadre du pad cache le haut de l'écran
        private const Int32 Rayon = 16; // coins des anneaux de clignotement
        private const Int32 RayonCoin = 30; // touches du haut à gauche et à droite, au coin très arrondi

        private static readonly String[] Clawd =
        [
            "..oooooooooooo..", "..oooooooooooo..", "..ooEEooooEEoo..", "..ooEEooooEEoo..",
            "oooooooooooooooo", "oooooooooooooooo", "..oooooooooooo..", "..oooooooooooo..",
        ];

        // Logo de Codex : nuage festonné en dégradé violet → bleu, avec des yeux au-dessus de son « >_ ».
        private static readonly String[] Nuage =
        [
            "....cc....cc....", "...cccc..cccc...", "..cccccccccccc..", ".cccccccccccccc.", ".cccccccccccccc.", "cccccccccccccccc",
            "cccccccccccccccc", ".cccccccccccccc.", ".cccccccccccccc.", "..cccccccccccc..", "...cccc..cccc...", "....cc....cc....",
        ];

        // Logo d'Antigravity : arche en « A » aux couleurs Google (crête jaune-orange, flanc vert à gauche, rouge-violet à droite).
        private static readonly String[] Arche =
        [
            "....aaaaaaaa....", "...aaaaaaaaaa...", "..aaaaaaaaaaaa..", "..aaaaaaaaaaaa..", ".aaaaaaaaaaaaaa.", ".aaaaa....aaaaa.",
            ".aaaa......aaaa.", "aaaa........aaaa", "aaa..........aaa", "aaa..........aaa", "aa............aa", "a..............a",
        ];

        private static readonly String[] Micro =
        [
            "....####....", "...######...", "...######...", "...######...", "...######...", "...######...",
            ".#.######.#.", ".#..####..#.", "..#......#..", "...######...", ".....##.....", "...######...",
        ];

        // Écran sur son pied, pour la capture (la dalle est peinte à part).
        private static readonly String[] Moniteur =
        [
            "############", "#..........#", "#..........#", "#..........#", "#..........#", "#..........#",
            "#..........#", "############", ".....##.....", ".....##.....", "...######...", "............",
        ];

        // Appareil photo vu de face : viseur, ampoule du flash (f), objectif (o) et sa lentille (c).
        private static readonly String[] Appareil =
        [
            "..bbb....f.", "bbbbbbbbbbb", "bbbbooobbbb", "bbbocccobbb", "bbbocccobbb", "bbbbooobbbb", "bbbbbbbbbbb",
        ];

        private static readonly String[] Coche =
        [
            "..........##", ".........##.", "........##..", ".......##...", "##....##....", ".##..##.....", "..####......", "...##.......",
        ];

        private enum Teinte { Vive, Dort, Eteinte, Lasse }

        public static BitmapImage Dessiner(Int32 touche, Scene scene, PluginImageSize taille)
        {
            using var b = Construire(touche, scene, taille switch { PluginImageSize.Width60 => 50, PluginImageSize.Width90 => 80, _ => T });
            return b.ToImage();
        }

        // Image d'une touche du pad (116 px) et ses pixels bruts, pour ne l'envoyer que si elle a changé.
        public static (BitmapImage Image, Byte[] Pixels) Image(Int32 touche, Scene scene)
        {
            using var b = Construire(touche, scene, T);
            return (b.ToImage(), b.ToArray(BitmapImageFormat.Rgb565));
        }

        // Image PNG d'une touche, pour l'aperçu hors du pad (outil apercu).
        public static Byte[] Png(Int32 touche, Scene scene, Int32 cote = T)
        {
            using var b = Construire(touche, scene, cote);
            return b.ToArray(BitmapImageFormat.Png);
        }

        private static BitmapBuilder Construire(Int32 touche, Scene scene, Int32 cote)
        {
            var b = new BitmapBuilder(cote, cote);
            try
            {
                if (cote != T)
                {
                    b.Scale(cote / (Single)T, cote / (Single)T);
                }

                b.FillRectangle(0, 0, T, T, Fond);
                // Les touches du haut à gauche et à droite ont un coin très arrondi : anneau plus rond pour elles.
                var rayon = CoinsDe(touche);
                if (touche < 3)
                {
                    Signal(b, touche, scene.Instantane.Premiere(touche), scene.Temps, rayon);
                }

                if (scene.Appui(touche))
                {
                    Rond(b, 0, 0, T, T, rayon, new BitmapColor(70, 70, 76));
                }

                // La photo remplit toute la touche, sans marge (une image ne suit pas l'échelle du dessin).
                if (touche == Moteur.Photo && scene.Instantane.Demande is null)
                {
                    TouchePhoto(b, scene);
                    return b;
                }

                // Marge autour du contenu, un peu plus en haut : le cadre du pad mord sur le haut de l'écran.
                const Int32 Cote = T - (2 * Marge) - Bas; // contenu carré, centré en largeur
                b.Translate((T - Cote) / 2, Marge + Bas);
                b.Scale(Cote / (Single)T, Cote / (Single)T);
                switch (touche)
                {
                    case Moteur.Capture or Moteur.Photo or Moteur.Dictee when scene.Instantane.Demande is { } demande:
                        ToucheDemande(b, touche, demande, scene);
                        break;
                    case Moteur.Capture: ToucheCapture(b, scene); break;
                    case Moteur.Dictee: ToucheDictee(b, scene); break;
                    case < 3: ToucheIA(b, touche, scene); break;
                    case >= Moteur.VolumeMoins: TouchePage2(b, touche, scene); break;
                    default: ToucheInfos(b, touche - 3, scene); break;
                }
            }
            catch (Exception e)
            {
                PluginLog.Error(e, $"dessin de la touche {touche}");
                b.ResetMatrix();
                b.Clear(BitmapColor.Black);
            }

            return b;
        }

        // Arrondit les coins d'une image de T px posée à x : ce qui dépasse d'un quart de cercle de rayon r passe au noir
        // (à droite, le bord de la touche coupe l'image).
        private static void Coins(BitmapBuilder b, Int32 r, Int32 x)
        {
            for (var y = 0; y < r; y++)
            {
                var dy = r - y - 0.5;
                var hors = (Int32)Math.Ceiling(r - Math.Sqrt(Math.Max(0, (r * r) - (dy * dy)))); // pixels hors du cercle sur cette ligne
                if (hors > 0)
                {
                    b.FillRectangle(x, y, hors, 1, BitmapColor.Black);
                    b.FillRectangle(T - hors, y, hors, 1, BitmapColor.Black);
                    b.FillRectangle(x, T - 1 - y, hors, 1, BitmapColor.Black);
                    b.FillRectangle(T - hors, T - 1 - y, hors, 1, BitmapColor.Black);
                }
            }
        }

        // Rectangle aux coins arrondis, en aplats opaques (pas de recouvrement qui fonce les angles).
        private static void Rond(BitmapBuilder b, Int32 x, Int32 y, Int32 l, Int32 h, Int32 r, BitmapColor c)
        {
            r = Math.Min(r, Math.Min(l, h) / 2);
            b.FillRectangle(x + r, y, l - (2 * r), h, c);
            b.FillRectangle(x, y + r, r, h - (2 * r), c);
            b.FillRectangle(x + l - r, y + r, r, h - (2 * r), c);
            foreach (var (cx, cy) in new[] { (x + r, y + r), (x + l - r - 1, y + r), (x + r, y + h - r - 1), (x + l - r - 1, y + h - r - 1) })
            {
                b.FillCircle(cx, cy, r, c);
            }
        }

        // Rayons des quatre coins (haut gauche, haut droit, bas gauche, bas droit).
        private readonly record struct Arrondis(Int32 HautGauche, Int32 HautDroit, Int32 BasGauche, Int32 BasDroit)
        {
            public Arrondis Moins(Int32 d) => new(this.HautGauche - d, this.HautDroit - d, this.BasGauche - d, this.BasDroit - d);
        }

        // Coins d'une touche : l'arrondi normal partout, sauf le coin extérieur du pad, très arrondi (en haut à gauche pour
        // la touche du haut à gauche, en haut à droite pour celle du haut à droite).
        private static Arrondis CoinsDe(Int32 touche) => (Moteur.Place(touche) % 9) switch
        {
            0 => new(RayonCoin, Rayon, Rayon, Rayon),
            2 => new(Rayon, RayonCoin, Rayon, Rayon),
            _ => new(Rayon, Rayon, Rayon, Rayon),
        };

        // Rectangle à coins arrondis chacun à son rayon, rangée par rangée (aplats opaques, sans recouvrement).
        private static void Rond(BitmapBuilder b, Int32 x, Int32 y, Int32 l, Int32 h, Arrondis r, BitmapColor c)
        {
            static Int32 Retrait(Int32 rayon, Int32 i)
            {
                if (rayon <= 0 || i >= rayon)
                {
                    return 0;
                }

                var dy = rayon - i - 0.5;
                return (Int32)Math.Round(rayon - Math.Sqrt((rayon * rayon) - (dy * dy)));
            }

            for (var i = 0; i < h; i++)
            {
                var j = h - 1 - i;
                var gauche = Math.Max(Retrait(r.HautGauche, i), Retrait(r.BasGauche, j));
                var droite = Math.Max(Retrait(r.HautDroit, i), Retrait(r.BasDroit, j));
                if (l - gauche - droite > 0)
                {
                    b.FillRectangle(x + gauche, y + i, l - gauche - droite, 1, c);
                }
            }
        }

        private static void Bord(BitmapBuilder b, BitmapColor c, Int32 e)
        {
            b.FillRectangle(0, 0, T, e, c);
            b.FillRectangle(0, T - e, T, e, c);
            b.FillRectangle(0, e, e, T - (2 * e), c);
            b.FillRectangle(T - e, e, e, T - (2 * e), c);
        }

        private static void Carte(BitmapBuilder b, String[] carte, Int32 x0, Int32 y0, Int32 p, BitmapColor c)
        {
            for (var y = 0; y < carte.Length; y++)
            {
                for (var x = 0; x < carte[y].Length; x++)
                {
                    if (carte[y][x] != '.')
                    {
                        b.FillRectangle(x0 + (x * p), y0 + (y * p), p, p, c);
                    }
                }
            }
        }

        private static void Pixels(BitmapBuilder b, String[] carte, Int32 x0, Int32 y0, Int32 p, Func<Char, Int32, Int32, BitmapColor?> palette)
        {
            for (var y = 0; y < carte.Length; y++)
            {
                for (var x = 0; x < carte[y].Length; x++)
                {
                    var ch = carte[y][x];
                    if (ch != '.' && palette(ch, x, y) is BitmapColor c)
                    {
                        b.FillRectangle(x0 + (x * p), y0 + (y * p), p, p, c);
                    }
                }
            }
        }

        private static BitmapColor Melange(BitmapColor a, BitmapColor z, Double k) =>
            new((Int32)(a.R + ((z.R - a.R) * k)), (Int32)(a.G + ((z.G - a.G) * k)), (Int32)(a.B + ((z.B - a.B) * k)));
    }
}

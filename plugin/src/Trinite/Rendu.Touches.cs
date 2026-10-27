namespace Loupedeck.TrinitePlugin
{
    using System;

    // Touches d'information et d'action : quotas, Capture, photo, Dictée. Toutes vivantes, sauf la photo.
    public static partial class Rendu
    {
        // Cadran de l'horloge sans son aiguille, qui tourne à part.
        private static readonly String[] Cadran =
        [
            "....####....", "..##....##..", ".#........#.", ".#........#.", "#..........#", "#..........#",
            "#..........#", "#..........#", ".#........#.", ".#........#.", "..##....##..", "....####....",
        ];

        // Calendrier sans ses cases, dessinées à part : une case « du jour » avance de l'une à l'autre.
        private static readonly String[] Feuille =
        [
            "..#......#..", "############", "############", "#..........#", "#..........#", "#..........#",
            "#..........#", "#..........#", "#..........#", "#..........#", "############", "............",
        ];

        private static readonly (Int32 X, Int32 Y)[] Cases = [(2, 4), (5, 4), (8, 4), (2, 6), (5, 6), (8, 6), (2, 8), (5, 8)];

        // Bulle sans ses trois points, qui ondulent quand l'IA travaille.
        private static readonly String[] BulleVide =
        [
            "..############..", ".#............#.", "#..............#", "#..............#", "#..............#", "#..............#",
            ".#............#.", "..#####..######.", "......#.#.......", ".......##.......", "........#.......", "................",
        ];

        // Bout de l'aiguille de l'horloge (en pixels du cadran, depuis le centre) : 8 positions, un tour en 8 s.
        private static readonly (Int32 X, Int32 Y)[] Aiguille = [(0, -4), (3, -3), (4, 0), (3, 3), (0, 4), (-3, 3), (-4, 0), (-3, -3)];

        // Ligne d'informations, sans texte : une jauge pour la fenêtre de 5 h (horloge à l'aiguille qui tourne), une
        // pour la semaine (calendrier dont la case du jour avance), qui montrent ce qui reste ; en bas, une bulle et le
        // nombre de conversations ouvertes, ses points ondulent quand l'IA travaille. Une jauge rose : à ce rythme, la
        // fenêtre sera vide avant sa remise à zéro (Rythme).
        private static void ToucheInfos(BitmapBuilder b, Int32 agent, Scene s)
        {
            var q = s.Instantane.Quotas[agent];
            var t = s.Temps + (agent * 0.7); // chaque IA à son rythme
            var maintenant = s.Maintenant.ToUniversalTime();
            // Chaque jauge centrée sur son icône (horloge de 11 à 35 ; calendrier de 43 à 65, anneaux compris) : elles
            // étaient 1 à 2 px plus haut.
            HorlogeVivante(b, 4, 11, q?.Cinq is null ? null : t);
            Jauge(b, 12, q?.Cinq, t, q?.Rapide(true, maintenant) == true);
            CalendrierVivant(b, 4, 43, q?.Semaine is null ? null : t);
            Jauge(b, 44, q?.Semaine, t + 0.5, q?.Rapide(false, maintenant) == true);

            Carte(b, BulleVide, 22, 80, 2, Gris);
            var travaille = s.Instantane.EtatIA(agent) == Etat.Travaille;
            for (var k = 0; k < 3; k++)
            {
                var leve = travaille && Math.Sin((s.Temps * 7) - (k * 1.1)) > 0.4 ? 2 : 0;
                b.FillRectangle(22 + ((3 + (k * 4)) * 2), 86 - leve, 4, 4, travaille ? Texte : Gris);
            }

            b.DrawText(s.Instantane.Ouvertes[agent].ToString(System.Globalization.CultureInfo.InvariantCulture), 58, 74, 50, 34, Texte, 24);
        }

        // Horloge : son aiguille avance d'un cran par seconde ; immobile (t null) quand le quota n'est pas lu.
        private static void HorlogeVivante(BitmapBuilder b, Int32 x0, Int32 y0, Double? t)
        {
            var c = t is null ? Eteint : Gris;
            Carte(b, Cadran, x0, y0, 2, c);
            var (ax, ay) = Aiguille[t is Double v ? (Int32)v % 8 : 0];
            Int32 cx = x0 + 11, cy = y0 + 11;
            for (var k = 0; k <= 4; k++)
            {
                b.FillRectangle(cx + (Int32)Math.Round(ax * k / 4.0), cy + (Int32)Math.Round(ay * k / 4.0), 2, 2, k == 0 ? Texte : c);
            }
        }

        // Calendrier : la case du jour avance d'une case par seconde, avec l'aiguille ; immobile (t null) quand le quota
        // n'est pas lu.
        private static void CalendrierVivant(BitmapBuilder b, Int32 x0, Int32 y0, Double? t)
        {
            var c = t is null ? Eteint : Gris;
            Carte(b, Feuille, x0, y0, 2, c);
            var jour = t is Double v ? (Int32)v % Cases.Length : -1;
            for (var k = 0; k < Cases.Length; k++)
            {
                b.FillRectangle(x0 + (Cases[k].X * 2), y0 + (Cases[k].Y * 2), 4, 2, k == jour ? Texte : c);
            }
        }

        // Une jauge, à droite de son icône ; vide et sombre quand la valeur manque. Elle montre ce qui reste : pleine et
        // verte à 100 %, qui passe par l'ambre à mi-course et finit rouge, presque vide ; un reflet la parcourt en 1 s,
        // toutes les 4 s. Rapide : consommée trop vite pour tenir jusqu'à sa remise à zéro, la jauge passe au rose, hors de
        // l'échelle vert-ambre-rouge, quelle que soit la part qui reste.
        private static void Jauge(BitmapBuilder b, Int32 y, Double? part, Double t, Boolean rapide = false)
        {
            Rond(b, 32, y, T - 36, 22, 8, EteintSombre);
            if (part is not Double p || p >= 1)
            {
                return;
            }

            var reste = 1 - Math.Clamp(p, 0, 1);
            var couleur = rapide ? Rose : reste < 0.5 ? Melange(Rouge, Ambre, reste / 0.5) : Melange(Ambre, Vert, (reste - 0.5) / 0.5);
            // Proportionnelle jusqu'au bout (au plancher de 16 px, 80 % et 99 % consommés se ressemblaient) ; 6 px au moins
            // pour rester visible, coin réduit pour une barre étroite.
            var largeur = Math.Max(6, (Int32)Math.Round((T - 36) * reste));
            Rond(b, 32, y, largeur, 22, Math.Min(8, largeur / 2), couleur);
            if (largeur > 24 && t % 4 < 1)
            {
                // Reflet : une bande plus claire qui glisse de gauche à droite, dans la barre.
                var x = 32 + 6 + (Int32)(((t % 4 * 1.4) - 0.2) * (largeur - 12));
                Int32 debut = Math.Max(x, 38), fin = Math.Min(x + 8, 32 + largeur - 6);
                if (fin > debut)
                {
                    b.FillRectangle(debut, y + 4, fin - debut, 14, Melange(couleur, BitmapColor.White, 0.35));
                }
            }
        }

        // Capture d'écran pour l'IA, sans texte : un écran devant lequel un appareil photo déclenche toutes les 3 s (flash,
        // rayons, dalle qui blanchit un instant) ; entre deux, l'objectif respire et un reflet traverse la dalle. À l'appui,
        // tout l'écran flashe, puis une coche verte une fois l'image collée dans la conversation.
        private static void ToucheCapture(BitmapBuilder b, Scene s)
        {
            const Int32 P = 5, X0 = 26, Y0 = 18; // écran : 60 × 60, décalé à droite derrière l'appareil
            const Int32 Q = 4, CX = 62, CY = 68; // appareil : 44 × 28, devant le coin bas droit de l'écran
            var k = s.Temps / 3 % 1;
            var declenche = s.Appui(Moteur.Capture) || k < 0.07;

            Carte(b, Moniteur, X0, Y0, P, Texte);
            b.FillRectangle(X0 + P, Y0 + P, 10 * P, 6 * P, declenche ? BitmapColor.White : Ciel); // dalle
            if (!declenche)
            {
                var r = s.Temps / 2.2 % 1; // reflet en biais
                for (var i = 0; i < 6; i++)
                {
                    var x = X0 + P + (Int32)(r * 70) - (i * 2) - 8;
                    if (x >= X0 + P && x + 4 <= X0 + (11 * P))
                    {
                        b.FillRectangle(x, Y0 + P + (i * P), 4, P, new BitmapColor(Cyan, 110));
                    }
                }
            }

            var objectif = Melange(Bleu, Cyan, 0.5 + (0.5 * Math.Sin(s.Temps * 3))); // l'objectif respire
            Pixels(b, Appareil, CX, CY, Q, (ch, _, _) => ch switch
            {
                'b' => new BitmapColor(75, 85, 99),
                'o' => Texte,
                'c' => declenche ? BitmapColor.White : objectif,
                'f' => declenche ? BitmapColor.White : Ambre,
                _ => null,
            });

            if (declenche)
            {
                // Rayons du flash autour de l'ampoule (case 9, rangée 0 de l'appareil).
                var fx = CX + (9 * Q) + (Q / 2);
                var fy = CY + (Q / 2);
                foreach (var (dx, dy) in new[] { (0, -1), (1, -1), (1, 0), (-1, -1) })
                {
                    for (var d = 2; d < 5; d++)
                    {
                        b.FillRectangle(fx + (dx * d * 3) - 1, fy + (dy * d * 3) - 1, 3, 3, BitmapColor.White);
                    }
                }
            }

            if (s.Temps - s.CaptureCollee < 1.8)
            {
                Carte(b, Coche, X0 + 12, Y0 + 8, 3, Vert);
            }
        }

        // Dernière photo du téléphone ; sans téléphone (ou sans photo), l'heure et la date.
        private static void TouchePhoto(BitmapBuilder b, Scene s)
        {
            var photo = s.Instantane.Photo;
            if (photo?.Miniature is null)
            {
                ToucheHeure(b, s);
                return;
            }

            // La miniature seule, sur toute la touche, bords arrondis ; une coche verte un instant après l'appui.
            // Décalée de 3 px vers la droite : l'écran de la touche dépasse l'image à droite, la photo y paraissait
            // décalée à gauche ; une fine bande noire à gauche équilibre.
            b.Clear(BitmapColor.Black);
            b.DrawImage(photo.Miniature, DecalagePhoto, 0, T, T, BitmapRotation.None);
            Coins(b, 12, DecalagePhoto);
            if (s.Temps - s.PhotoCopiee < 1.8)
            {
                b.FillCircle(T / 2, T / 2, 26, new BitmapColor(0, 0, 0, 170));
                Carte(b, Coche, (T / 2) - 18, (T / 2) - 14, 3, Vert);
            }
        }

        // Heure et date, sans téléphone : un cadran de 60 points dont les secondes écoulées s'allument en arc-en-ciel qui
        // tourne, une comète sur la seconde en cours ; l'heure en grand (deux-points qui battent), le jour et le mois dessous.
        private static void ToucheHeure(BitmapBuilder b, Scene s)
        {
            var m = s.Maintenant;
            const Double R = 50;
            for (var i = 0; i < 60; i++)
            {
                var a = (i / 60.0 * Math.PI * 2) - (Math.PI / 2);
                Int32 x = (T / 2) + (Int32)Math.Round(Math.Cos(a) * R), y = (T / 2) + (Int32)Math.Round(Math.Sin(a) * R);
                var teinte = ArcEnCiel((i / 60.0) + (s.Temps / 12));
                var c = i == m.Second ? BitmapColor.White : i < m.Second ? teinte : Melange(EteintSombre, teinte, 0.25);
                var cote = i == m.Second ? 6 : i % 5 == 0 ? 4 : 3;
                b.FillRectangle(x - (cote / 2), y - (cote / 2), cote, cote, c);
            }

            var deuxPoints = s.Temps % 1 < 0.5 ? ":" : " ";
            b.DrawText($"{m:HH}{deuxPoints}{m:mm}", 0, 34, T, 34, Texte, 26);
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            b.DrawText(m.ToString("ddd d", culture).Replace(".", "", StringComparison.Ordinal).ToUpperInvariant(), 0, 66, T, 18, ArcEnCiel(s.Temps / 12), 13);
            b.DrawText(m.ToString("MMM", culture).Replace(".", "", StringComparison.Ordinal).ToUpperInvariant(), 0, 82, T, 14, Gris, 10);
        }

        // Couleur de l'arc-en-ciel (0 à 1, en boucle), saturée et claire pour le fond noir.
        private static BitmapColor ArcEnCiel(Double h)
        {
            h = (h % 1 + 1) % 1 * 6;
            var f = h - Math.Floor(h);
            Int32 V(Double k) => (Int32)Math.Round(70 + (185 * k));
            return (Int32)h switch
            {
                0 => new BitmapColor(V(1), V(f), V(0)),
                1 => new BitmapColor(V(1 - f), V(1), V(0)),
                2 => new BitmapColor(V(0), V(1), V(f)),
                3 => new BitmapColor(V(0), V(1 - f), V(1)),
                4 => new BitmapColor(V(f), V(0), V(1)),
                _ => new BitmapColor(V(1), V(0), V(1 - f)),
            };
        }

        // Dictée de l'appli au premier plan (Windows si aucune IA n'est devant) : le micro émet des ondes de chaque côté ;
        // pendant une dictée lancée par le pad, il passe au rouge et ses ondes battent vite, jusqu'à l'arrêt.
        private static void ToucheDictee(BitmapBuilder b, Scene s)
        {
            const Int32 X0 = (T - 48) / 2, Y0 = (T - 48) / 2, Milieu = Y0 + 16;
            var enCours = s.Instantane.Dictee;
            var teinte = enCours ? Rouge : Texte;
            Carte(b, Micro, X0, Y0, 4, teinte);
            for (var i = 0; i < 2; i++)
            {
                var force = Math.Round(Math.Max(0, Math.Sin((s.Temps * (enCours ? 9 : 4)) - (i * 1.3))) * 3) / 3; // 4 intensités
                var c = new BitmapColor(teinte, (Int32)(255 * force));
                var d = 4 + (i * 8); // distance au micro (ses bras touchent les bords de son dessin)
                foreach (var (x, sens) in new[] { (X0 - d - 3, -1), (X0 + 48 + d, 1) })
                {
                    b.FillRectangle(x, Milieu - 8, 3, 4, c);
                    b.FillRectangle(x + (sens * 2), Milieu - 4, 3, 8, c);
                    b.FillRectangle(x, Milieu + 4, 3, 4, c);
                }
            }

        }
    }
}

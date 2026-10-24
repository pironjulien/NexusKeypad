namespace Loupedeck.TrinitePlugin
{
    using System;

    // Personnages de la Trinité : Clawd, le nuage de Codex, l'arche d'Antigravity, leurs états et leurs signaux.
    public static partial class Rendu
    {
        // Une IA : seulement son personnage, en grand et centré ; son animation dit son état (il travaille, attend, a
        // fini, est au repos) ; un petit Clawd court sous lui quand il travaille pour Claude par le relais. Appli fermée :
        // il dort, des « z » montent (un appui la lance). Il clignote seulement quand il appelle l'utilisateur : une question
        // (clignotement franc), une fin (respiration lente) ; jamais pour le relais.
        private static void ToucheIA(BitmapBuilder b, Int32 agent, Scene s)
        {
            var c = s.Instantane.Premiere(agent);
            var ferme = !s.Instantane.Ouverte[agent];
            var etat = ferme ? Etat.Repos : c?.Etat ?? Etat.Repos;
            var hauteur = agent switch { 0 => 10, 1 => 12, _ => 14 } * 5; // en pixels du dessin, avec les jambes ou les réacteurs
            Personnage(b, agent, (T - 80) / 2, (T - hauteur) / 2, 5, etat, s.Temps, dort: ferme);
            if (ferme)
            {
                Zzz(b, T - 30, T / 2, 56, s.Temps, agent * 0.2);
            }

            Coursier(b, agent, s);
        }

        // Écart entre deux touches voisines, en pixels du contenu (marges des deux touches et bord du pad entre elles).
        private const Int32 EcartTouches = 36;

        // Relais : un petit Clawd part de sous Claude, passe sur la touche voisine et trotte sous le logo de Codex ou
        // d'Antigravity, tant que celui-ci travaille pour lui ; un trajet continu d'une touche à l'autre, en boucle.
        private static void Coursier(BitmapBuilder b, Int32 agent, Scene s)
        {
            var claude = Moteur.Place(0) % 3;
            foreach (var pair in new[] { 1, 2 })
            {
                if ((agent != 0 && agent != pair) || !s.Instantane.Ouverte[0] || !s.Instantane.Ouverte[pair] || !s.Instantane.PourClaude(pair))
                {
                    continue;
                }

                var ecart = (Moteur.Place(pair) % 3) - claude;
                var sens = Math.Sign(ecart);
                var trajet = (T / 2) + (Math.Abs(ecart) * (T + EcartTouches)) + 20; // du milieu de Claude à la sortie du pair
                var d = (s.Temps / 3.2 % 1) * trajet;
                var x = agent == 0
                    ? (T / 2) + (sens * d)
                    : (sens > 0 ? 0 : T) + (sens * (d - (T / 2) - EcartTouches - ((Math.Abs(ecart) - 1) * (T + EcartTouches))));
                if (x > -20 && x < T + 20)
                {
                    PetitClawd(b, (Int32)x, T - 20, s.Temps);
                }
            }
        }

        // Clawd en petit (2 px par case), qui trotte : centré en cx, posé en y.
        private static void PetitClawd(BitmapBuilder b, Int32 cx, Int32 y, Double t)
        {
            const Int32 P = 2;
            var x0 = cx - (8 * P);
            var y1 = y + (Math.Floor(t * 8) % 2 == 0 ? 0 : -1);
            Pixels(b, Clawd, x0, y1, P, (ch, _, _) => ch == 'o' ? Orange : Encre);
            var pas = Math.Floor(t * 8) % 2 == 1 ? 1 : 0;
            foreach (var lx in new[] { 3 + pas, 5 + pas, 10 - pas, 12 - pas })
            {
                b.FillRectangle(x0 + (lx * P), y + (8 * P), P, 2 * P, Orange);
            }
        }

        // Trois « z » qui montent en ondulant depuis (x, y), de montee pixels, et s'effacent ; decalage : chaque IA à
        // son rythme.
        private static void Zzz(BitmapBuilder b, Int32 x, Int32 y, Int32 montee, Double t, Double decalage)
        {
            for (var z = 0; z < 3; z++)
            {
                var k = ((t * 0.25) + (z / 3.0) + decalage) % 1;
                var zx = x + (Int32)(Math.Sin(k * 6) * 6);
                var zy = y - (Int32)(k * montee);
                if (zx > -20 && zx < T && zy > -20 && zy < T)
                {
                    b.DrawText("z", zx, zy, 24, 24, new BitmapColor(210, 220, 255, (Int32)(255 * (1 - k))), 12 + (Int32)(k * 12));
                }
            }
        }

        // Clignotement d'une IA qui appelle l'utilisateur, sous le contenu, en anneau arrondi : jusqu'à ce qu'il ouvre la conversation.
        // Claude, le métronome de la Trinité, a ses propres couleurs (rose pour une question, cyan pour une fin).
        private static void Signal(BitmapBuilder b, Int32 agent, Conversation c, Double t, Int32 rayon)
        {
            if (c?.Alerte != true)
            {
                return;
            }

            var question = c.Etat == Etat.Attend;
            var teinte = (agent, question) switch
            {
                (0, true) => Rose,
                (0, false) => Cyan,
                (_, true) => Ambre,
                _ => Vert,
            };
            var force = question
                ? (t * 2 % 1 < 0.5 ? 1.0 : 0.25) // question : franc, deux fois par seconde
                : 0.35 + (0.65 * (0.5 + (0.5 * Math.Sin(t * 2.2)))); // fin : respiration lente
            Rond(b, 0, 0, T, T, rayon, Melange(Fond, teinte, force));
            Rond(b, 5, 5, T - 10, T - 10, rayon - 5, Melange(Fond, teinte, force * 0.22));
        }

        public static void Personnage(BitmapBuilder b, Int32 i, Int32 x0, Int32 y0, Int32 p, Etat etat, Double t, Boolean dort = false)
        {
            var teinte = etat == Etat.Ferme ? Teinte.Eteinte : dort ? Teinte.Dort : Teinte.Vive;
            if (teinte != Teinte.Vive)
            {
                etat = Etat.Repos;
            }

            switch (i)
            {
                case 0: DessinerClawd(b, x0, y0, p, etat, t, teinte); break;
                case 1: DessinerCodex(b, x0, y0, p, etat, t, teinte); break;
                default: DessinerArche(b, x0, y0, p, etat, t, teinte); break;
            }
        }

        // Endormi : couleurs atténuées ; fermé : gris.
        private static BitmapColor Ton(BitmapColor c, Teinte teinte) => teinte switch
        {
            Teinte.Dort => new BitmapColor(c.R * 45 / 100, c.G * 45 / 100, c.B * 45 / 100, c.A),
            Teinte.Eteinte => (c.R + c.G + c.B) / 3 > 90 ? Eteint : EteintSombre,
            _ => c,
        };

        // Clawd : yeux qui tournent quand il travaille, lunettes noires quand il a fini, bras levé quand il attend.
        private static void DessinerClawd(BitmapBuilder b, Int32 x0, Int32 y0, Int32 p, Etat etat, Double t, Teinte teinte)
        {
            var corps = Ton(Orange, teinte);
            var encre = Ton(Encre, teinte);
            var y = y0 + (etat == Etat.Travaille ? (Int32)Math.Round(Math.Sin(t * 10)) * p / 2 : 0);
            Pixels(b, Clawd, x0, y, p, (ch, _, _) => ch == 'o' ? corps : encre);

            if (teinte != Teinte.Vive)
            {
                foreach (var ex in new[] { 4, 10 })
                {
                    b.FillRectangle(x0 + (ex * p), y + (2 * p), 2 * p, 2 * p, corps);
                    b.FillRectangle(x0 + (ex * p), y + (3 * p), 2 * p, Math.Max(2, p / 2), encre);
                }
            }
            else if (etat == Etat.Fini)
            {
                var verre = new BitmapColor(17, 17, 17);
                b.FillRectangle(x0 + (3 * p), y + (2 * p), 4 * p, 2 * p, verre);
                b.FillRectangle(x0 + (9 * p), y + (2 * p), 4 * p, 2 * p, verre);
                b.FillRectangle(x0 + (7 * p), y + (2 * p), 2 * p, Math.Max(2, p / 2), verre);
                b.FillRectangle(x0 + (3 * p) + (p / 2), y + (2 * p) + (p / 3), p, Math.Max(2, p / 2), BitmapColor.White);
                b.FillRectangle(x0 + (9 * p) + (p / 2), y + (2 * p) + (p / 3), p, Math.Max(2, p / 2), BitmapColor.White);
            }
            else if (etat == Etat.Travaille)
            {
                // Anneau aux trois quarts dont le trou tourne : l'œil « mouline ».
                var trou = (Int32)(Math.Floor(t * 8) % 4);
                (Int32 X, Int32 Y)[] quarts = [(0, 0), (1, 0), (1, 1), (0, 1)];
                foreach (var ex in new[] { 4, 10 })
                {
                    for (var q = 0; q < 4; q++)
                    {
                        b.FillRectangle(x0 + ((ex + quarts[q].X) * p), y + ((2 + quarts[q].Y) * p), p, p, q == trou ? corps : encre);
                    }
                }
            }
            else if (t % 4 < 0.25)
            {
                b.FillRectangle(x0 + (4 * p), y + (2 * p), 2 * p, p + (p / 2), corps); // clignement
                b.FillRectangle(x0 + (10 * p), y + (2 * p), 2 * p, p + (p / 2), corps);
            }

            var pas = etat == Etat.Travaille && Math.Floor(t * 4) % 2 == 1 ? 1 : 0;
            foreach (var lx in new[] { 3 + pas, 5 + pas, 10 - pas, 12 - pas })
            {
                b.FillRectangle(x0 + (lx * p), y0 + (8 * p), p, 2 * p, corps);
            }

            if (etat == Etat.Attend)
            {
                var h = (Int32)(Math.Abs(Math.Sin(t * 5)) * p * 2);
                b.FillRectangle(x0, y + (2 * p) - h, p, (2 * p) + h, corps);
            }
        }

        // Yeux d'un logo : blancs 2 × 2, pupille qui regarde, fermés, ou plissés de contentement. Au repos, le regard
        // flâne à gauche et à droite ; au travail, il file vers le bas (le code) ; en attente, il fixe l'utilisateur.
        private static void Yeux(BitmapBuilder b, Int32 x0, Int32 y, Int32 p, Int32[] colonnes, Etat etat, Double t, Teinte teinte, Func<Int32, BitmapColor> peau)
        {
            var blanc = Ton(BitmapColor.White, teinte);
            var pupille = Ton(Nuit, teinte);
            foreach (var ex in colonnes)
            {
                if (teinte != Teinte.Vive || (etat == Etat.Repos && t % 4 < 0.25))
                {
                    b.FillRectangle(x0 + (ex * p), y, 2 * p, 2 * p, peau(ex));
                    b.FillRectangle(x0 + (ex * p), y + p, 2 * p, Math.Max(2, p / 2), blanc); // paupière close
                    continue;
                }

                if (etat == Etat.Fini)
                {
                    b.FillRectangle(x0 + (ex * p), y, 2 * p, 2 * p, peau(ex));
                    b.FillRectangle(x0 + (ex * p), y + p, p, p, blanc); // « ^ » content
                    b.FillRectangle(x0 + ((ex + 1) * p), y, p, p, blanc);
                    b.FillRectangle(x0 + ((ex + 1) * p) + (p / 2), y + p, p / 2, p, blanc);
                    continue;
                }

                b.FillRectangle(x0 + (ex * p), y, 2 * p, 2 * p, blanc);
                var (dx, dy) = etat switch
                {
                    Etat.Travaille => (Math.Sin(t * 3) > 0 ? 1 : 0, 1),
                    Etat.Attend => (Math.Floor(t * 1.5) % 2 == 0 ? 0 : 1, 0),
                    _ => ((Int32)(t / 2.5) % 3 == 0 ? 0 : 1, 1),
                };
                b.FillRectangle(x0 + ((ex + dx) * p), y + (dy * p), p, p, pupille);
            }
        }

        // Point d'interrogation qui flotte au-dessus d'une IA qui attend l'utilisateur.
        private static readonly String[] Question = ["###", "..#", ".##", "...", ".#."];

        private static void Interrogation(BitmapBuilder b, Int32 cx, Int32 y, Int32 p, Double t, BitmapColor c)
        {
            var q = Math.Max(2, p * 3 / 5);
            Carte(b, Question, cx - (3 * q / 2), y - (Int32)(Math.Abs(Math.Sin(t * 4)) * p), q, c);
        }

        // Étincelles autour d'une IA qui a fini : trois croix qui scintillent tour à tour (positions en cases du dessin).
        private static void Etincelles(BitmapBuilder b, Int32 x0, Int32 y, Int32 p, Double t, BitmapColor c)
        {
            (Double X, Double Y)[] places = [(-1, 1), (16, 3), (15.5, 10)];
            for (var k = 0; k < places.Length; k++)
            {
                var eclat = Math.Sin((t * 3) + (k * 2.1));
                if (eclat < 0)
                {
                    continue;
                }

                var cx = x0 + (Int32)(places[k].X * p);
                var cy = y + (Int32)(places[k].Y * p);
                var bras = Math.Max(2, (Int32)(eclat * p));
                var e = Math.Max(2, p / 2);
                b.FillRectangle(cx - bras, cy, (2 * bras) + e, e, c);
                b.FillRectangle(cx, cy - bras, e, (2 * bras) + e, c);
            }
        }

        // Codex : son logo-nuage prend des yeux. Au repos, il respire et flâne du regard ; au travail, il sautille, tape
        // du code après son « > » (le curseur avance) et des bits montent ; en attente, il bondit sous un « ? » ; quand
        // il a fini, il plisse les yeux de contentement au milieu d'étincelles.
        private static void DessinerCodex(BitmapBuilder b, Int32 x0, Int32 y0, Int32 p, Etat etat, Double t, Teinte teinte)
        {
            var haut = new BitmapColor(177, 167, 255);
            var milieu = new BitmapColor(122, 157, 255);
            var bas = new BitmapColor(57, 65, 255);
            BitmapColor Rangee(Int32 y) => Ton(y < 6 ? Melange(haut, milieu, y / 5.5) : Melange(milieu, bas, (y - 5.5) / 5.5), teinte);
            var vive = teinte == Teinte.Vive;

            var saut = !vive ? 0 : etat switch
            {
                Etat.Attend => -(Int32)(Math.Abs(Math.Sin(t * 5)) * 2 * p),
                Etat.Travaille => (Int32)Math.Round(Math.Sin(t * 8)) * p / 2,
                _ => (Int32)Math.Round(Math.Sin(t * 1.6)) * p / 3, // respiration
            };
            var y = y0 + saut;
            Pixels(b, Nuage, x0, y, p, (_, _, ry) => Rangee(ry));
            Yeux(b, x0, y + (3 * p), p, [4, 10], etat, t, teinte, _ => Rangee(3));

            // « > » du logo, en blanc.
            var trait = Ton(BitmapColor.White, teinte);
            foreach (var (cx, cy) in new[] { (5, 6), (6, 7), (5, 8) })
            {
                b.FillRectangle(x0 + (cx * p), y + (cy * p), p, p, trait);
            }

            if (vive && etat == Etat.Travaille)
            {
                // Le code s'écrit : jusqu'à 4 caractères, puis la ligne repart ; le curseur suit.
                var n = (Int32)(t * 5) % 5;
                for (var k = 0; k < n; k++)
                {
                    b.FillRectangle(x0 + ((8 + k) * p), y + (8 * p) + (p / 3), p - 1, p - (p / 3), trait);
                }

                if (Math.Floor(t * 6) % 2 == 0)
                {
                    b.FillRectangle(x0 + ((8 + n) * p), y + (8 * p), Math.Max(2, p / 2), p, trait);
                }

                // Bits qui s'échappent du haut du nuage.
                var bit = new BitmapColor(125, 211, 252);
                for (var j = 0; j < 3; j++)
                {
                    var k = ((t * 0.9) + (j / 3.0)) % 1;
                    var taille = Math.Max(3, p * 4 / 5);
                    b.FillRectangle(x0 + ((3 + (j * 5)) * p), y - (Int32)(k * 3 * p) - (p / 2), taille, taille, new BitmapColor(bit, (Int32)(255 * (1 - k))));
                }
            }
            else if (!vive || Math.Floor(t * 2) % 2 == 0)
            {
                b.FillRectangle(x0 + (8 * p), y + (8 * p), 3 * p, p, trait); // « _ » qui clignote doucement
            }

            if (vive && etat == Etat.Attend)
            {
                Interrogation(b, x0 + (8 * p), y - (3 * p), p, t, Ambre);
            }
            else if (vive && etat == Etat.Fini)
            {
                Etincelles(b, x0, y, p, t, Vert);
            }
        }

        // Dégradé du logo : crête jaune → orange → rouge de gauche à droite, qui fond vers le bleu en descendant,
        // avec une touche de vert sur le flanc gauche.
        private static BitmapColor CouleurArche(Int32 x, Int32 y)
        {
            var k = x / 15.0;
            var crete = k < 0.5
                ? Melange(new BitmapColor(251, 188, 4), new BitmapColor(249, 115, 28), k * 2)
                : Melange(new BitmapColor(249, 115, 28), new BitmapColor(234, 67, 53), (k - 0.5) * 2);
            if (x <= 3 && y is >= 2 and <= 5)
            {
                crete = new BitmapColor(52, 168, 83);
            }

            var flanc = x < 8 ? new BitmapColor(56, 140, 245) : new BitmapColor(98, 110, 240);
            return Melange(crete, flanc, Math.Clamp((y - 0.5) / 5.0, 0, 1));
        }

        // Antigravity : son logo-arche prend des yeux et flotte sur deux réacteurs. Au repos, elle plane doucement ; au
        // travail, ses réacteurs crachent bleu, elle vole plus vite et un satellite gravite autour d'elle ; en attente,
        // elle se balance sous un « ? » ; quand elle a fini, elle se pose, réacteurs coupés, au milieu d'étincelles.
        private static void DessinerArche(BitmapBuilder b, Int32 x0, Int32 y0, Int32 p, Etat etat, Double t, Teinte teinte)
        {
            var vive = teinte == Teinte.Vive;
            var vol = !vive || etat == Etat.Fini ? 0 : (Int32)Math.Round(Math.Sin(t * (etat == Etat.Travaille ? 6 : 2)) * (etat == Etat.Travaille ? 1.5 : 1));
            var x = x0 + (vive && etat == Etat.Attend ? (Int32)Math.Round(Math.Sin(t * 3) * p / 2) : 0);
            var y = y0 + (vol * p / 2);
            var orbite = vive && etat == Etat.Travaille ? t * 3.2 : Double.NaN;
            var satellite = new BitmapColor(125, 211, 252);
            var taille = Math.Max(4, p * 6 / 5);
            Int32 Sx() => x + (8 * p) + (Int32)(Math.Cos(orbite) * 9 * p);
            Int32 Sy() => y + (6 * p) + (Int32)(Math.Sin(orbite) * 3 * p);
            if (!Double.IsNaN(orbite) && Math.Sin(orbite) < 0)
            {
                b.FillRectangle(Sx(), Sy(), taille, taille, new BitmapColor(satellite, 130)); // derrière l'arche
            }

            Pixels(b, Arche, x, y, p, (_, cx, ry) => Ton(CouleurArche(cx, ry), teinte));
            Yeux(b, x, y + (2 * p), p, [5, 9], etat, t, teinte, cx => Ton(CouleurArche(cx, 2), teinte));
            if (!vive)
            {
                return;
            }

            if (!Double.IsNaN(orbite) && Math.Sin(orbite) >= 0)
            {
                b.FillRectangle(Sx(), Sy(), taille, taille, satellite); // devant l'arche
            }

            if (etat != Etat.Fini)
            {
                var flamme = Math.Floor(t * (etat == Etat.Travaille ? 8 : 3)) % 2 == 1; // au repos, une flamme calme
                var c = etat == Etat.Travaille ? satellite : Ambre;
                foreach (var fx in new[] { 0, 15 })
                {
                    b.FillRectangle(x + (fx * p), y + (12 * p), p, flamme ? p + (p / 2) : p, c);
                    if (etat == Etat.Travaille)
                    {
                        b.FillRectangle(x + (fx * p), y + (13 * p) + (p / 2), p, flamme ? p : p / 2, new BitmapColor(satellite, 150));
                    }
                }
            }

            if (etat == Etat.Attend)
            {
                Interrogation(b, x + (8 * p), y - (4 * p), p, t, Ambre);
            }
            else if (etat == Etat.Fini)
            {
                Etincelles(b, x, y, p, t, Vert);
            }
        }
    }
}

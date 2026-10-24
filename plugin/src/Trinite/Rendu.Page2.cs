namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.Globalization;

    // Page 2 : le poste. Volume moins, son coupé, volume plus ; Gmail, Agenda (date et heure), Messages ; santé du PC,
    // Store et Windows Update, le site. Toutes colorées et animées ; dessinées seulement quand la page est affichée.
    public static partial class Rendu
    {
        // Couleurs de Google (Gmail, Agenda, Messages) et du Store.
        private static readonly BitmapColor GBleu = new(66, 133, 244);
        private static readonly BitmapColor GRouge = new(234, 67, 53);
        private static readonly BitmapColor GJaune = new(251, 188, 4);
        private static readonly BitmapColor GVert = new(52, 168, 83);

        private static readonly String[] HautParleur =
        [
            "....#.....", "...##.....", "####O.....", "####O.....", "####O.....", "####O.....", "...##.....", "....#.....",
        ];

        // Enveloppe de Gmail : bords bleu (b), vert (g), rouge (r), jaune (j), le « M » rouge, papier blanc (w).
        private static readonly String[] Enveloppe =
        [
            "brwwwwwwwwwwrj", "brrwwwwwwwwrrj", "bwrrwwwwwwrrwj", "bwwrrwwwwrrwwj", "bwwwrrwwrrwwwj", "bwwwwrrrrwwwwj",
            "bwwwwwrrwwwwwj", "bwwwwwwwwwwwwj", "gwwwwwwwwwwwwj",
        ];

        // Bulle de Google Messages.
        private static readonly String[] BulleMessages =
        [
            "..bbbbbbbbbb..", ".bbbbbbbbbbbb.", "bbbbbbbbbbbbbb", "bbbbbbbbbbbbbb", "bbbbbbbbbbbbbb", "bbbbbbbbbbbbbb",
            "bbbbbbbbbbbbbb", ".bbbbbbbbbbbb.", "..bbbbbbbbbb..", "..bbb.........", ".bb...........",
        ];

        // Sac du Microsoft Store : anse (a), sac (s), quatre carrés de couleur (r, v, b, j).
        private static readonly String[] Sac =
        [
            "...aaaa...", "..a....a..", "ssssssssss", "ssrrrsvvvs", "ssrrrsvvvs", "ssrrrsvvvs", "ssssssssss", "ssbbbsjjjs",
            "ssbbbsjjjs", "ssbbbsjjjs", "ssssssssss",
        ];

        private static void TouchePage2(BitmapBuilder b, Int32 touche, Scene s)
        {
            switch (touche)
            {
                case Moteur.VolumeMoins: ToucheReglage(b, s, -1); break;
                case Moteur.Muet: ToucheMuet(b, s); break;
                case Moteur.VolumePlus: ToucheReglage(b, s, 1); break;
                case Moteur.Gmail: ToucheGmail(b, s); break;
                case Moteur.Agenda: ToucheAgenda(b, s); break;
                case Moteur.Messages: ToucheMessages(b, s); break;
                case Moteur.Sante: ToucheSante(b, s); break;
                case Moteur.MisesAJour: ToucheMisesAJour(b, s); break;
                default: ToucheSite(b, s); break;
            }
        }

        // Volume moins / plus : un grand « − » ou « + » coloré qui pulse, et le niveau en 10 cases dessous.
        private static void ToucheReglage(BitmapBuilder b, Scene s, Int32 sens)
        {
            var couleur = sens < 0 ? Melange(Cyan, Bleu, 0.4) : Vert;
            var appui = s.Appui(sens < 0 ? Moteur.VolumeMoins : Moteur.VolumePlus);
            var pouls = appui ? 6 : (Int32)Math.Round(Math.Abs(Math.Sin(s.Temps * 2)) * 3);
            var c = appui ? Melange(couleur, BitmapColor.White, 0.5) : couleur;
            var demi = 24 + pouls;
            b.FillRectangle((T / 2) - demi, 40, 2 * demi, 12, c);
            if (sens > 0)
            {
                b.FillRectangle((T / 2) - 6, 46 - demi, 12, 2 * demi, c);
            }

            Niveau(b, 92, s.Poste?.Volume ?? 0, couleur, !(s.Poste?.SonCoupe ?? false));
        }

        // Son : haut-parleur et trois ondes qui battent avec le son qui sort ; coupé, rouge et croix. Le niveau en chiffre.
        private static void ToucheMuet(BitmapBuilder b, Scene s)
        {
            const Int32 X0 = 10, Y0 = 18, Milieu = Y0 + 24; // haut-parleur : 30 × 48
            var p = s.Poste;
            if (p?.Volume is not Double niveau)
            {
                Carte(b, HautParleur, X0, Y0, 6, Eteint);
                return;
            }

            var coupe = p.SonCoupe || niveau < 0.005;
            Pixels(b, HautParleur, X0, Y0, 6, (ch, _, _) => ch == 'O' ? Melange(Texte, Fond, 0.4) : coupe ? Rouge : Texte);
            if (coupe)
            {
                var force = 0.6 + (0.4 * Math.Abs(Math.Sin(s.Temps * 3)));
                for (var i = 0; i < 6; i++)
                {
                    b.FillRectangle(X0 + 52 + (i * 6), Milieu - 18 + (i * 6), 6, 6, Melange(Fond, Rouge, force));
                    b.FillRectangle(X0 + 82 - (i * 6), Milieu - 18 + (i * 6), 6, 6, Melange(Fond, Rouge, force));
                }

                return;
            }

            for (var o = 0; o < 3; o++)
            {
                var allumee = niveau > o / 3.0;
                var x = X0 + 42 + (o * 14);
                var demi = 8 + (o * 8);
                var c = !allumee ? EteintSombre : Melange(Cyan, Bleu, o / 2.0);
                if (allumee && p.Son > 0.02 && Math.Sin((s.Temps * 7) - (o * 1.2)) > 0.3)
                {
                    c = Melange(c, BitmapColor.White, 0.45); // l'onde part quand le son sort
                }

                b.FillRectangle(x, Milieu - demi, 5, 5, c);
                b.FillRectangle(x + 5, Milieu - demi + 5, 5, (2 * demi) - 10, c);
                b.FillRectangle(x, Milieu + demi - 5, 5, 5, c);
            }

            b.DrawText($"{niveau * 100:0}", 0, 76, T, 24, Texte, 18);
        }

        // Barre de niveau en 10 cases (vive : son actif, sinon grisée).
        private static void Niveau(BitmapBuilder b, Int32 y, Double niveau, BitmapColor couleur, Boolean vive)
        {
            var allumees = (Int32)Math.Round(Math.Clamp(niveau, 0, 1) * 10);
            for (var i = 0; i < 10; i++)
            {
                b.FillRectangle(14 + (i * 9), y, 7, 10, i >= allumees ? EteintSombre : vive ? Melange(couleur, BitmapColor.White, i / 20.0) : Eteint);
            }
        }

        // Pastille rouge du nombre en attente, qui pulse ; rien si zéro ou inconnu.
        private static void Pastille(BitmapBuilder b, Int32 cx, Int32 cy, Int32? nombre, Double t)
        {
            if (nombre is not > 0)
            {
                return;
            }

            var r = 17 + (Int32)Math.Round(Math.Abs(Math.Sin(t * 3)) * 2);
            b.FillCircle(cx, cy, r + 2, Fond);
            b.FillCircle(cx, cy, r, GRouge);
            var texte = nombre > 99 ? "99+" : nombre.Value.ToString(CultureInfo.InvariantCulture);
            b.DrawText(texte, cx - 20, cy - 12, 40, 24, BitmapColor.White, texte.Length > 2 ? 13 : 18);
        }

        // Gmail : son enveloppe aux quatre couleurs, qui flotte ; le nombre de mails en attente sur le téléphone.
        private static void ToucheGmail(BitmapBuilder b, Scene s)
        {
            var y = 30 + (Int32)Math.Round(Math.Sin(s.Temps * 1.8) * 3);
            Pixels(b, Enveloppe, (T - 84) / 2, y, 6, (ch, _, _) => ch switch
            {
                'b' => GBleu,
                'g' => GVert,
                'r' => GRouge,
                'j' => GJaune,
                _ => new BitmapColor(240, 240, 240),
            });
            Pastille(b, T - 22, 24, s.Poste?.Mails, s.Temps);
        }

        // Agenda : la page du jour de Google Agenda (bandeau bleu, quatre coins de couleur), le jour en grand, l'heure en
        // dessous (deux-points qui battent la seconde).
        private static void ToucheAgenda(BitmapBuilder b, Scene s)
        {
            var maintenant = s.Maintenant;
            const Int32 X = 14, Y = 8, L = T - 28, H = 76;
            Rond(b, X, Y, L, H, 10, new BitmapColor(245, 245, 245));
            Rond(b, X, Y, L, 18, 8, GBleu);
            b.FillRectangle(X, Y + 10, L, 8, GBleu);
            b.FillRectangle(X + L - 14, Y + H - 14, 14, 14, GJaune); // coin replié
            b.FillRectangle(X, Y + H - 6, 30, 6, GVert);
            b.FillRectangle(X + 30, Y + H - 6, L - 44, 6, GRouge);
            b.DrawText(maintenant.ToString("ddd", CultureInfo.CurrentCulture).TrimEnd('.').ToUpperInvariant(), X, Y + 1, L, 16, BitmapColor.White, 12);
            b.DrawText(maintenant.Day.ToString(CultureInfo.InvariantCulture), X, Y + 18, L, 50, GBleu, 40);
            var deuxPoints = s.Temps % 1 < 0.5 ? ":" : " ";
            b.DrawText($"{maintenant:HH}{deuxPoints}{maintenant:mm}", 0, 86, T, 26, Texte, 20);
        }

        // Messages : la bulle bleue de Google Messages, trois points qui s'écrivent ; le nombre de messages en attente.
        private static void ToucheMessages(BitmapBuilder b, Scene s)
        {
            const Int32 X0 = (T - 84) / 2, Y0 = 26;
            Pixels(b, BulleMessages, X0, Y0, 6, (_, _, _) => GBleu);
            for (var i = 0; i < 3; i++)
            {
                var haut = Math.Max(0, Math.Sin((s.Temps * 5) - (i * 0.9))) * 5;
                b.FillCircle(X0 + 22 + (i * 20), Y0 + 26 - (Int32)haut, 6, BitmapColor.White);
            }

            Pastille(b, T - 22, 24, s.Poste?.Messages, s.Temps);
        }

        // Santé du PC : processeur et mémoire en jauges (vert, ambre, rouge selon la charge), reflet qui glisse. Un appui
        // ouvre le Gestionnaire des tâches.
        private static void ToucheSante(BitmapBuilder b, Scene s)
        {
            var p = s.Poste;
            Charge(b, 14, "CPU", p?.Processeur, s.Temps);
            Charge(b, 62, "RAM", p?.Memoire, s.Temps + 1.3);
        }

        private static void Charge(BitmapBuilder b, Int32 y, String nom, Double? charge, Double t)
        {
            b.DrawText(nom, 8, y, 50, 18, Gris, 13);
            Rond(b, 8, y + 20, T - 16, 18, 7, EteintSombre);
            if (charge is not Double c)
            {
                return;
            }

            var couleur = c < 0.5 ? Melange(Vert, Ambre, c / 0.5) : Melange(Ambre, Rouge, (c - 0.5) / 0.5);
            var largeur = Math.Max(12, (Int32)((T - 16) * c));
            Rond(b, 8, y + 20, largeur, 18, 7, couleur);
            var x = 8 + (Int32)((t / 2.5 % 1) * (largeur + 20)) - 10; // reflet
            if (x > 10 && x + 6 < 8 + largeur - 4)
            {
                b.FillRectangle(x, y + 23, 6, 12, Melange(couleur, BitmapColor.White, 0.4));
            }

            b.DrawText($"{c * 100:0} %", 50, y, 58, 18, Texte, 13);
        }

        // Store et Windows Update sur la même touche, tous deux vifs : le sac du Store à gauche, les flèches de Windows
        // Update à droite. Au repos, les flèches tournent doucement ; un appui lance les deux recherches : pendant 30 s
        // les flèches tournent vite et le sac sautille, puis une coche verte 2 s.
        private static void ToucheMisesAJour(BitmapBuilder b, Scene s)
        {
            var depuis = s.Temps - s.MiseAJour;
            var cherche = depuis is >= 0 and < 30;
            if (depuis is >= 30 and < 32)
            {
                Carte(b, Coche, (T / 2) - 18, (T / 2) - 14, 3, Vert);
                return;
            }

            var saut = cherche ? (Int32)Math.Round(Math.Abs(Math.Sin(s.Temps * 6)) * 6) : 0;
            Pixels(b, Sac, 4, 34 - saut, 5, (ch, _, _) => ch switch
            {
                'r' => new BitmapColor(242, 80, 34),
                'v' => new BitmapColor(127, 186, 0),
                'b' => new BitmapColor(0, 164, 239),
                'j' => new BitmapColor(255, 185, 0),
                'a' => Texte,
                _ => new BitmapColor(60, 64, 72),
            });

            // Flèches circulaires : deux arcs de 6 cases avec leur pointe, qui tournent.
            var angle0 = s.Temps * (cherche ? 7 : 1.2);
            const Int32 CX = 84, CY = 58, R = 20;
            var bleu = new BitmapColor(0, 120, 212);
            for (var arc = 0; arc < 2; arc++)
            {
                for (var i = 0; i < 6; i++)
                {
                    var a = angle0 + (arc * Math.PI) + (i * 0.42);
                    b.FillRectangle(CX - 3 + (Int32)(Math.Cos(a) * R), CY - 3 + (Int32)(Math.Sin(a) * R), 6, 6, bleu);
                }

                var pointe = angle0 + (arc * Math.PI) + (6 * 0.42);
                var px = CX + (Int32)(Math.Cos(pointe) * R);
                var py = CY + (Int32)(Math.Sin(pointe) * R);
                b.FillRectangle(px - 5, py - 5, 10, 10, bleu);
            }
        }

        // Globe de la touche du site sans réglage : océan (o), terres (t).
        private static readonly String[] Globe =
        [
            "....oooo....", "..oottoooo..", ".oottttoooo.", ".otttttooto.", "ooottttootto", "oooottoootto",
            "oooootoottoo", "ooooooootooo", ".oooooottoo.", ".ooooootttoo", "..oooootoo..", "....oooo....",
        ];

        // Site : son image (ou un globe qui flotte) dans un anneau qui tourne, aux couleurs du réglage (Signet.cs), sinon
        // en arc-en-ciel.
        private static void ToucheSite(BitmapBuilder b, Scene s)
        {
            const Int32 R = 50;
            var site = Signet.Reglage;
            var debut = s.Temps * 1.4;
            for (var i = 0; i < 36; i++)
            {
                var a = debut + (i / 36.0 * Math.PI * 2);
                var c = site.Anneau is { } anneau ? anneau[i * anneau.Length / 36] : ArcEnCiel(i / 36.0);
                b.FillRectangle((T / 2) - 3 + (Int32)(Math.Cos(a) * R), (T / 2) - 3 + (Int32)(Math.Sin(a) * R), 6, 6, c);
            }

            if (site.Image is { } image)
            {
                b.DrawImage(image, (T / 2) - 42, (T / 2) - 42, 84, 84, BitmapRotation.None);
                return;
            }

            // Le globe tourne : ses terres glissent d'une colonne par demi-seconde.
            var decalage = (Int32)(s.Temps * 2) % 12;
            Pixels(b, Globe, (T / 2) - 36, (T / 2) - 36, 6, (ch, x, y) => ch switch
            {
                'o' or 't' => Globe[y][(x + decalage) % 12] == 't' ? Vert : Bleu,
                _ => null,
            });
        }
    }
}

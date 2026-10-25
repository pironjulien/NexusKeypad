namespace Loupedeck.TrinitePlugin
{
    using System;

    // Personnages de la Trinité : Clawd, le nuage de Codex, l'arche d'Antigravity, leurs états et leurs signaux.
    public static partial class Rendu
    {
        // Une IA : seulement son personnage, en grand et centré ; son animation dit son état (il travaille, attend une
        // réponse ou une autorisation, a fini, s'est arrêté sur une erreur, a atteint sa limite, est au repos). Appli
        // fermée : il dort, des « z » montent ; lancée par le pad, il se réveille pendant le démarrage. Pendant une dictée
        // dans son appli, il écoute. Il clignote seulement quand il appelle l'utilisateur ; jamais pour le relais, qui
        // se dessine en bulles de discussion entre Claude et les deux autres.
        private static void ToucheIA(BitmapBuilder b, Int32 agent, Scene s)
        {
            var c = s.Instantane.Premiere(agent);
            var ferme = !s.Instantane.Ouverte[agent];
            var etat = ferme ? Etat.Repos : c?.Etat ?? Etat.Repos;
            // Limite d'usage atteinte (jauge pleine) : il ne peut plus travailler, sauf s'il travaille encore (la jauge
            // est lue toutes les quelques secondes, le travail en cours fait foi).
            if (!ferme && etat is Etat.Repos or Etat.Fini && QuotaPlein(s.Instantane.Quotas[agent]))
            {
                etat = Etat.Limite;
            }

            var hauteur = agent switch { 0 => 10, 1 => 12, _ => 14 } * 5; // en pixels du dessin, avec les jambes ou les réacteurs
            var (x0, y0) = ((T - 80) / 2, (T - hauteur) / 2);
            var reveil = ferme && s.Lancements[agent] < 15;
            if (reveil)
            {
                Reveil(b, agent, x0, y0, 5, s.Lancements[agent], s.Temps);
            }
            else
            {
                Personnage(b, agent, x0, y0, 5, etat, s.Temps, dort: ferme);
                if (ferme)
                {
                    Zzz(b, T - 30, T / 2, 56, s.Temps, agent * 0.2);
                }
            }

            if (!ferme && s.Instantane.DicteeIa == agent)
            {
                Ecoute(b, T / 2, y0 + (agent == 0 ? 15 : 22), s.Temps);
            }

            Discussion(b, agent, s);
        }

        // Une fenêtre de quota pleine (99,5 % ou plus utilisés).
        private static Boolean QuotaPlein(Quota q) => q is not null && (q.Cinq >= 0.995 || q.Semaine >= 0.995);

        // Écart entre deux touches voisines, en pixels du contenu (marges des deux touches et bord du pad entre elles).
        private const Int32 EcartTouches = 36;

        // Relais : entre Claude et Codex ou Antigravity, à mi-hauteur des personnages, deux voies de chevrons qui passent
        // d'une touche à l'autre tant que le pair travaille pour Claude : en haut, orange, de Claude vers le pair ; en bas,
        // aux couleurs du pair (Codex bleu, Antigravity cyan), du pair vers Claude. Le sens se lit d'un coup d'œil (choix
        // de Julien, 25/10, après les petits Clawd, les bulles et un fil sous les personnages).
        private static void Discussion(BitmapBuilder b, Int32 agent, Scene s)
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
                var distance = Math.Abs(ecart) * (T + EcartTouches); // de centre à centre
                const Int32 Bord = 41; // juste au flanc des personnages (80 px de large, centrés)
                Int32 Local(Double g) => (Int32)(agent == 0 ? (T / 2) + (sens * g) : (T / 2) - (sens * (distance - g)));
                var retour = pair == 1 ? new BitmapColor(122, 157, 255) : new BitmapColor(125, 211, 252);
                var milieu = (T / 2) + 2;
                foreach (var (aller, y, c) in new[] { (true, milieu - 7, Orange), (false, milieu + 7, retour) })
                {
                    // La voie : pointillés discrets.
                    for (var g = (Double)Bord; g <= distance - Bord; g += 4)
                    {
                        var x = Local(g);
                        if (x >= 0 && x <= T - 2)
                        {
                            b.FillRectangle(x, y, 2, 2, new BitmapColor(c, 70));
                        }
                    }

                    // Trois chevrons qui avancent ; vers le pair à l'aller, vers Claude au retour.
                    var versDroite = (aller ? sens : -sens) > 0;
                    for (var k = 0; k < 3; k++)
                    {
                        var phase = ((s.Temps * 0.7) + (k / 3.0) + (aller ? 0 : 1 / 6.0)) % 1;
                        var g = Bord + ((aller ? phase : 1 - phase) * (distance - (2 * Bord)));
                        Chevron(b, Local(g), y + 1, versDroite, c);
                    }
                }
            }
        }

        // Chevron de 4 px d'épaisseur centré en (x, y), pointe à droite ou à gauche.
        private static void Chevron(BitmapBuilder b, Int32 x, Int32 y, Boolean versDroite, BitmapColor c)
        {
            if (x < -6 || x > T + 6)
            {
                return;
            }

            for (var k = 0; k < 4; k++)
            {
                var dx = versDroite ? k * 2 : -k * 2;
                b.FillRectangle(x - 3 + dx, y - 7 + (k * 2), 3, 2, c);
                b.FillRectangle(x - 3 + dx, y + 5 - (k * 2), 3, 2, c);
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

        // Réveil pendant le démarrage de son appli (lancée par le pad il y a ecoule secondes) : il ouvre les yeux, ses
        // couleurs reviennent, il s'étire d'un bond et un « ! » saute au-dessus de lui.
        private static void Reveil(BitmapBuilder b, Int32 agent, Int32 x0, Int32 y0, Int32 p, Double ecoule, Double t)
        {
            var eveille = ecoule > 0.8 || Math.Floor(ecoule * 6) % 2 == 1; // un battement de paupières, puis éveillé
            var bond = ecoule < 2.5 ? -(Int32)(Math.Abs(Math.Sin(ecoule * 3.2)) * 2 * p) : 0;
            Personnage(b, agent, x0, y0 + bond, p, Etat.Repos, t, dort: !eveille);
            if (eveille)
            {
                var saut = (Int32)(Math.Abs(Math.Sin(t * 5)) * p);
                Carte(b, Exclamation, (T / 2) - 3, Math.Max(0, y0 - (4 * p) - saut + bond), 3, Ambre);
            }
        }

        private static readonly String[] Exclamation = ["##", "##", "##", "##", "..", "##"];

        // Ondes de part et d'autre de sa tête pendant une dictée : il écoute (rouge, la couleur de la touche Dictée).
        private static void Ecoute(BitmapBuilder b, Int32 cx, Int32 cy, Double t)
        {
            for (var k = 0; k < 2; k++)
            {
                var onde = ((t * 1.6) + (k * 0.5)) % 1;
                var ecart = 44 + (Int32)(onde * 10);
                var alpha = (Int32)(255 * (1 - onde));
                foreach (var cote in new[] { -1, 1 })
                {
                    var x = cx + (cote * ecart);
                    b.FillRectangle(x - 1, cy - 6, 3, 12, new BitmapColor(Rouge, alpha));
                    b.FillRectangle(x - 1 - (cote * 3), cy - 10, 3, 4, new BitmapColor(Rouge, alpha));
                    b.FillRectangle(x - 1 - (cote * 3), cy + 6, 3, 4, new BitmapColor(Rouge, alpha));
                }
            }
        }

        // Clignotement d'une IA qui appelle l'utilisateur, sous le contenu, en anneau arrondi : jusqu'à ce qu'il ouvre la
        // conversation. Claude, le métronome de la Trinité, a ses propres couleurs (rose pour une question ou une
        // autorisation, cyan pour une fin) ; une erreur clignote rouge, vite.
        private static void Signal(BitmapBuilder b, Int32 agent, Conversation c, Double t, Int32 rayon)
        {
            if (c?.Alerte != true)
            {
                return;
            }

            var erreur = c.Etat == Etat.Erreur;
            var question = c.Appelle;
            var teinte = (agent, question) switch
            {
                _ when erreur => Rouge,
                (0, true) => Rose,
                (0, false) => Cyan,
                (_, true) => Ambre,
                _ => Vert,
            };
            var force = erreur ? (t * 3 % 1 < 0.5 ? 1.0 : 0.2)
                : question ? (t * 2 % 1 < 0.5 ? 1.0 : 0.25) // question : franc, deux fois par seconde
                : 0.35 + (0.65 * (0.5 + (0.5 * Math.Sin(t * 2.2)))); // fin : respiration lente
            Rond(b, 0, 0, T, T, rayon, Melange(Fond, teinte, force));
            Rond(b, 5, 5, T - 10, T - 10, rayon - 5, Melange(Fond, teinte, force * 0.22));
        }

        public static void Personnage(BitmapBuilder b, Int32 i, Int32 x0, Int32 y0, Int32 p, Etat etat, Double t, Boolean dort = false)
        {
            var teinte = etat == Etat.Ferme ? Teinte.Eteinte : dort ? Teinte.Dort : etat == Etat.Limite ? Teinte.Lasse : Teinte.Vive;
            if (teinte is Teinte.Dort or Teinte.Eteinte)
            {
                etat = Etat.Repos;
            }

            switch (i)
            {
                case 0: DessinerClawd(b, x0, y0, p, etat, t, teinte); break;
                case 1: DessinerCodex(b, x0, y0, p, etat, t, teinte); break;
                default: DessinerArche(b, x0, y0, p, etat, t, teinte); break;
            }

            if (teinte is Teinte.Dort or Teinte.Eteinte)
            {
                return;
            }

            // Signes communs au-dessus de la tête : cadenas (autorisation), éclair (erreur), batterie vide (limite).
            var cx = x0 + (8 * p);
            var haut = y0 - (4 * p);
            switch (etat)
            {
                case Etat.Autorisation:
                    Cadenas(b, cx, haut - (Int32)(Math.Abs(Math.Sin(t * 4)) * p), p, t);
                    break;
                case Etat.Erreur:
                    if (t * 3 % 1 < 0.7)
                    {
                        Carte(b, Eclair, cx - (2 * Math.Max(2, p * 3 / 5)), haut, Math.Max(2, p * 3 / 5), Rouge);
                    }

                    break;
                case Etat.Limite:
                    Batterie(b, cx, haut + p, p, t);
                    break;
            }
        }

        // Endormi : couleurs atténuées ; fermé : gris ; las (limite atteinte) : délavé.
        private static BitmapColor Ton(BitmapColor c, Teinte teinte) => teinte switch
        {
            Teinte.Dort => new BitmapColor(c.R * 45 / 100, c.G * 45 / 100, c.B * 45 / 100, c.A),
            Teinte.Eteinte => (c.R + c.G + c.B) / 3 > 90 ? Eteint : EteintSombre,
            Teinte.Lasse => Melange(c, new BitmapColor(110, 110, 118), 0.55) is var m ? new BitmapColor(m.R, m.G, m.B, c.A) : c,
            _ => c,
        };

        private static readonly String[] Eclair = ["...##", "..##.", ".##..", "#####", "..##.", ".##..", ".#...", "#...."];

        private static readonly String[] Anse = [".###.", "#...#", "#...#"];

        // Cadenas ambre centré en cx : son anse, le corps et un trou de serrure qui brille.
        private static void Cadenas(BitmapBuilder b, Int32 cx, Int32 y, Int32 p, Double t)
        {
            var q = Math.Max(2, p * 3 / 5);
            Carte(b, Anse, cx - (5 * q / 2), y, q, Ambre);
            b.FillRectangle(cx - (7 * q / 2), y + (3 * q), 7 * q, 5 * q, Ambre);
            var serrure = t * 2 % 1 < 0.5 ? Encre : new BitmapColor(120, 80, 0);
            b.FillRectangle(cx - (q / 2), y + (4 * q), q, 2 * q, serrure);
        }

        // Batterie vide : contour gris, un dernier trait rouge qui clignote.
        private static void Batterie(BitmapBuilder b, Int32 cx, Int32 y, Int32 p, Double t)
        {
            var q = Math.Max(2, p / 2);
            var (l, h) = (12 * q, 6 * q);
            var x = cx - (l / 2);
            var contour = new BitmapColor(200, 200, 205);
            b.FillRectangle(x, y, l, q, contour);
            b.FillRectangle(x, y + h - q, l, q, contour);
            b.FillRectangle(x, y, q, h, contour);
            b.FillRectangle(x + l - q, y, q, h, contour);
            b.FillRectangle(x + l, y + (2 * q), q, 2 * q, contour);
            if (t * 1.5 % 1 < 0.5)
            {
                b.FillRectangle(x + (2 * q), y + (2 * q), 2 * q, 2 * q, Rouge);
            }
        }

        // Clawd : yeux qui tournent quand il travaille, lunettes noires quand il a fini, bras levé quand il attend ou
        // demande une autorisation, yeux en croix et secousse sur une erreur, affaissé et paupières lourdes à sa limite.
        private static void DessinerClawd(BitmapBuilder b, Int32 x0, Int32 y0, Int32 p, Etat etat, Double t, Teinte teinte)
        {
            var corps = Ton(Orange, teinte);
            var encre = Ton(Encre, teinte);
            var secousse = etat == Etat.Erreur && t * 3 % 1 < 0.3 ? (Int32)(Math.Sin(t * 60) * p / 2) : 0;
            x0 += secousse;
            var y = y0 + etat switch
            {
                Etat.Travaille => (Int32)Math.Round(Math.Sin(t * 10)) * p / 2,
                Etat.Limite => p / 2,
                _ => 0,
            };
            Pixels(b, Clawd, x0, y, p, (ch, _, _) => ch == 'o' ? corps : encre);

            if (teinte is Teinte.Dort or Teinte.Eteinte)
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
            else if (etat == Etat.Erreur)
            {
                foreach (var ex in new[] { 4, 10 })
                {
                    b.FillRectangle(x0 + (ex * p), y + (2 * p), 2 * p, 2 * p, corps);
                    Croix(b, x0 + (ex * p), y + (2 * p), 2 * p, encre);
                }
            }
            else if (etat == Etat.Limite)
            {
                foreach (var ex in new[] { 4, 10 })
                {
                    b.FillRectangle(x0 + (ex * p), y + (2 * p), 2 * p, p, corps); // paupières lourdes
                }
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

            if (etat is Etat.Attend or Etat.Autorisation)
            {
                var h = (Int32)(Math.Abs(Math.Sin(t * 5)) * p * 2);
                b.FillRectangle(x0, y + (2 * p) - h, p, (2 * p) + h, corps);
            }
        }

        // Croix dans un carré de côté c (yeux sonnés).
        private static void Croix(BitmapBuilder b, Int32 x, Int32 y, Int32 c, BitmapColor couleur)
        {
            var e = Math.Max(2, c / 4);
            for (var k = 0; k < 4; k++)
            {
                b.FillRectangle(x + (k * e), y + (k * e), e, e, couleur);
                b.FillRectangle(x + ((3 - k) * e), y + (k * e), e, e, couleur);
            }
        }

        // Yeux d'un logo : blancs 2 × 2, pupille qui regarde, fermés, plissés de contentement, en croix (erreur) ou mi-clos
        // (limite). Au repos, le regard flâne à gauche et à droite ; au travail, il file vers le bas (le code) ; en attente,
        // il fixe l'utilisateur.
        private static void Yeux(BitmapBuilder b, Int32 x0, Int32 y, Int32 p, Int32[] colonnes, Etat etat, Double t, Teinte teinte, Func<Int32, BitmapColor> peau)
        {
            var blanc = Ton(BitmapColor.White, teinte);
            var pupille = Ton(Nuit, teinte);
            foreach (var ex in colonnes)
            {
                if (teinte is Teinte.Dort or Teinte.Eteinte || (etat == Etat.Repos && t % 4 < 0.25))
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

                if (etat == Etat.Erreur)
                {
                    b.FillRectangle(x0 + (ex * p), y, 2 * p, 2 * p, peau(ex));
                    Croix(b, x0 + (ex * p), y, 2 * p, blanc);
                    continue;
                }

                b.FillRectangle(x0 + (ex * p), y, 2 * p, 2 * p, blanc);
                var (dx, dy) = etat switch
                {
                    Etat.Travaille => (Math.Sin(t * 3) > 0 ? 1 : 0, 1),
                    Etat.Attend or Etat.Autorisation => (Math.Floor(t * 1.5) % 2 == 0 ? 0 : 1, 0),
                    Etat.Limite => (0, 1),
                    _ => ((Int32)(t / 2.5) % 3 == 0 ? 0 : 1, 1),
                };
                b.FillRectangle(x0 + ((ex + dx) * p), y + (dy * p), p, p, pupille);
                if (etat == Etat.Limite)
                {
                    b.FillRectangle(x0 + (ex * p), y, 2 * p, p, peau(ex)); // paupières lourdes
                }
            }
        }

        // Point d'interrogation (petit, pour le terminal de Codex).
        private static readonly String[] Question = ["###", "..#", ".##", "...", ".#."];

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
        // du code après son « > » (le curseur avance) et des bits montent. En attente, il penche la tête de côté et son
        // terminal tape une question (« ? » qui clignote, trois points qui s'écrivent au-dessus). Quand il a fini, son
        // terminal affiche une coche, il fait des petits bonds de joie et des confettis à ses couleurs tombent.
        private static void DessinerCodex(BitmapBuilder b, Int32 x0, Int32 y0, Int32 p, Etat etat, Double t, Teinte teinte)
        {
            var haut = new BitmapColor(177, 167, 255);
            var milieu = new BitmapColor(122, 157, 255);
            var bas = new BitmapColor(57, 65, 255);
            BitmapColor Rangee(Int32 y) => Ton(y < 6 ? Melange(haut, milieu, y / 5.5) : Melange(milieu, bas, (y - 5.5) / 5.5), teinte);
            var vive = teinte is Teinte.Vive or Teinte.Lasse;
            var attend = etat is Etat.Attend or Etat.Autorisation;

            var saut = !vive ? 0 : etat switch
            {
                Etat.Travaille => (Int32)Math.Round(Math.Sin(t * 8)) * p / 2,
                Etat.Fini => -(Int32)(Math.Max(0, Math.Sin(t * 5)) * 1.5 * p), // petits bonds de joie
                Etat.Limite => p / 2,
                _ when attend => 0,
                _ => (Int32)Math.Round(Math.Sin(t * 1.6)) * p / 3, // respiration
            };
            var penche = vive && attend ? (Int32)Math.Round(Math.Sin(t * 2.5) * p) : 0; // tête penchée, d'un côté puis de l'autre
            var secousse = etat == Etat.Erreur && t * 3 % 1 < 0.3 ? (Int32)(Math.Sin(t * 60) * p / 2) : 0;
            var x = x0 + penche + secousse;
            var y = y0 + saut;
            Pixels(b, Nuage, x, y, p, (_, _, ry) => Rangee(ry));
            Yeux(b, x, y + (3 * p), p, [4, 10], etat, t, teinte, _ => Rangee(3));

            // « > » du logo, en blanc.
            var trait = Ton(BitmapColor.White, teinte);
            foreach (var (cx, cy) in new[] { (5, 6), (6, 7), (5, 8) })
            {
                b.FillRectangle(x + (cx * p), y + (cy * p), p, p, trait);
            }

            var q = Math.Max(2, p * 3 / 5);
            if (vive && etat == Etat.Travaille)
            {
                // Le code s'écrit : jusqu'à 4 caractères, puis la ligne repart ; le curseur suit.
                var n = (Int32)(t * 5) % 5;
                for (var k = 0; k < n; k++)
                {
                    b.FillRectangle(x + ((8 + k) * p), y + (8 * p) + (p / 3), p - 1, p - (p / 3), trait);
                }

                if (Math.Floor(t * 6) % 2 == 0)
                {
                    b.FillRectangle(x + ((8 + n) * p), y + (8 * p), Math.Max(2, p / 2), p, trait);
                }

                // Bits qui s'échappent du haut du nuage.
                var bit = new BitmapColor(125, 211, 252);
                for (var j = 0; j < 3; j++)
                {
                    var k = ((t * 0.9) + (j / 3.0)) % 1;
                    var taille = Math.Max(3, p * 4 / 5);
                    b.FillRectangle(x + ((3 + (j * 5)) * p), y - (Int32)(k * 3 * p) - (p / 2), taille, taille, new BitmapColor(bit, (Int32)(255 * (1 - k))));
                }
            }
            else if (vive && attend)
            {
                // Le terminal pose sa question : « ? » qui clignote après le « > ».
                if (t * 2 % 1 < 0.6)
                {
                    Carte(b, Question, x + (8 * p), y + (6 * p) + (p / 2), q, trait);
                }

                // Trois points qui s'écrivent au-dessus de lui (sauf pour une autorisation : le cadenas y est).
                if (etat == Etat.Attend)
                {
                    var n = (Int32)(t * 3) % 4;
                    for (var k = 0; k < n; k++)
                    {
                        b.FillRectangle(x + ((5 + (k * 3)) * p), y - (2 * p), p, p, Ambre);
                    }
                }
            }
            else if (vive && etat == Etat.Fini)
            {
                Carte(b, Coche, x + (8 * p), y + (6 * p) + (p / 2), Math.Max(1, p * 2 / 5), Vert); // « ✓ » dans le terminal
                BitmapColor[] teintes = [haut, milieu, Vert, Ambre];
                for (var j = 0; j < 6; j++)
                {
                    var k = ((t * 0.6) + (j / 6.0)) % 1;
                    var cx = x0 + (Int32)(((j * 3.1) - 1) * p) + (Int32)(Math.Sin((t * 3) + j) * p);
                    var cy = y0 - (3 * p) + (Int32)(k * 16 * p);
                    b.FillRectangle(cx, cy, Math.Max(3, p * 3 / 5), Math.Max(3, p * 3 / 5), new BitmapColor(teintes[j % 4], (Int32)(255 * (1 - (k * 0.7)))));
                }
            }
            else if (!vive || Math.Floor(t * 2) % 2 == 0)
            {
                b.FillRectangle(x + (8 * p), y + (8 * p), 3 * p, p, trait); // « _ » qui clignote doucement
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
        // travail, ses réacteurs crachent bleu, elle vole plus vite et un satellite gravite autour d'elle. En attente, elle
        // se balance et son antenne appelle : des ondes radar montent au-dessus d'elle. Quand elle a fini, elle se pose,
        // réacteurs coupés, et deux feux d'artifice éclatent de part et d'autre. Limite : posée, réacteurs éteints.
        private static void DessinerArche(BitmapBuilder b, Int32 x0, Int32 y0, Int32 p, Etat etat, Double t, Teinte teinte)
        {
            var vive = teinte is Teinte.Vive or Teinte.Lasse;
            var posee = etat is Etat.Fini or Etat.Limite or Etat.Erreur;
            var attend = etat is Etat.Attend or Etat.Autorisation;
            var vol = !vive || posee ? 0 : (Int32)Math.Round(Math.Sin(t * (etat == Etat.Travaille ? 6 : 2)) * (etat == Etat.Travaille ? 1.5 : 1));
            var secousse = etat == Etat.Erreur && t * 3 % 1 < 0.3 ? (Int32)(Math.Sin(t * 60) * p / 2) : 0;
            var x = x0 + secousse + (vive && attend ? (Int32)Math.Round(Math.Sin(t * 3) * p / 2) : 0);
            var y = y0 + (vol * p / 2) + (posee ? p : 0);
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

            if (!posee)
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
                Radar(b, x + (8 * p), y - p, p, t);
            }
            else if (etat == Etat.Fini)
            {
                Feux(b, x, y, p, t);
            }
        }

        // Ondes radar : trois chevrons ambre qui montent de la crête et s'effacent.
        private static void Radar(BitmapBuilder b, Int32 cx, Int32 y, Int32 p, Double t)
        {
            var e = Math.Max(2, p / 2);
            for (var k = 0; k < 3; k++)
            {
                var onde = ((t * 1.2) + (k / 3.0)) % 1;
                var demi = (Int32)((1.5 + (onde * 4)) * p);
                var oy = y - (Int32)(onde * 4 * p);
                var c = new BitmapColor(Ambre, (Int32)(255 * (1 - onde)));
                for (var i = 0; i <= demi; i += e)
                {
                    var dy = i / 2;
                    b.FillRectangle(cx - i, oy + dy, e, e, c);
                    b.FillRectangle(cx + i, oy + dy, e, e, c);
                }
            }
        }

        // Deux feux d'artifice de part et d'autre : une fusée monte, puis éclate en huit points qui s'écartent.
        private static void Feux(BitmapBuilder b, Int32 x0, Int32 y, Int32 p, Double t)
        {
            BitmapColor[] teintes = [Vert, Ambre, Rose];
            foreach (var (j, fx) in new[] { (0, -1.0), (1, 17.0) })
            {
                var k = ((t * 0.55) + (j * 0.5)) % 1;
                var cx = x0 + (Int32)(fx * p);
                var e = Math.Max(3, p * 4 / 5);
                if (k < 0.35)
                {
                    b.FillRectangle(cx, y + (12 * p) - (Int32)(k / 0.35 * 11 * p), e, e, new BitmapColor(255, 240, 200)); // la fusée
                    continue;
                }

                var r = (k - 0.35) / 0.65;
                var cy = y + p;
                var c = new BitmapColor(teintes[((Int32)(t * 0.55) + j) % 3], (Int32)(255 * (1 - r)));
                for (var a = 0; a < 8; a++)
                {
                    var angle = a * Math.PI / 4;
                    b.FillRectangle(cx + (Int32)(Math.Cos(angle) * r * 4.5 * p), cy + (Int32)(Math.Sin(angle) * r * 4.5 * p), e, e, c);
                }
            }
        }
    }
}

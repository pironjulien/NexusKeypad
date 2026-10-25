namespace Loupedeck.TrinitePlugin
{
    using System;

    // Personnages de la Trinité : Clawd, le nuage de Codex, l'arche d'Antigravity, leurs états et leurs signaux.
    //
    // Grammaire commune (relectures de Codex et d'Antigravity, 25/10) : une silhouette reconnaissable sur chaque image,
    // un signe d'état persistant au-dessus de la tête, le même pour les trois, puis le mouvement propre à chaque IA qui
    // précise l'état. Signes : travaille = des étoiles à sa couleur qui montent derrière lui, et son geste propre (Clawd
    // trotte, yeux qui moulinent ; Codex tape sur son terminal ; Antigravity vole réacteurs allumés) ; question = « ? » ambre ; autorisation = cadenas
    // ambre ; fini = coche verte ; erreur = triangle rouge « ! » ; limite = jauge vide barrée ; écoute (dictée) = micro
    // et point rouge d'enregistrement ; réveil = roue grise ; fermé = silhouette grise et bouton éteint. Le signe pulse
    // ou flotte, il ne disparaît jamais : une image isolée suffit à lire l'état.
    public static partial class Rendu
    {
        // Une IA : son personnage, en grand et centré, son signe d'état et son mouvement. Appli fermée : grise, endormie ;
        // lancée par le pad, elle se réveille pendant le démarrage. Pendant une dictée dans son appli, elle écoute. Elle
        // clignote seulement quand elle appelle l'utilisateur ; jamais pour le relais, qui se dessine en fil de données
        // entre Claude et les deux autres.
        private static void ToucheIA(BitmapBuilder b, Int32 agent, Scene s)
        {
            var c = s.Instantane.Premiere(agent);
            var ferme = !s.Instantane.Ouverte[agent];
            var etat = ferme ? Etat.Ferme : c?.Etat ?? Etat.Repos;
            // Limite d'usage atteinte (jauge pleine) : il ne peut plus travailler, sauf s'il travaille encore (la jauge
            // est lue toutes les quelques secondes, le travail en cours fait foi).
            if (!ferme && etat is Etat.Repos or Etat.Fini && QuotaPlein(s.Instantane.Quotas[agent]))
            {
                etat = Etat.Limite;
            }

            // Secondes depuis le changement d'état : les célébrations et les secousses ne jouent qu'à l'arrivée.
            var depuis = c is null ? 99 : Math.Max(0, (s.Maintenant.ToUniversalTime() - c.Depuis).TotalSeconds);
            var hauteur = agent switch { 0 => 10, 1 => 12, _ => 14 } * 5; // en pixels du dessin, avec les jambes ou les réacteurs
            var (x0, y0) = ((T - 80) / 2, (T - hauteur) / 2);
            var ecoute = !ferme && s.Instantane.DicteeIa == agent;
            if (etat == Etat.Travaille)
            {
                Etoiles(b, agent, s.Temps); // derrière lui : dessinées avant
            }

            if (ferme && s.Lancements[agent] < 15)
            {
                Reveil(b, agent, x0, y0, 5, s.Lancements[agent], s.Temps);
            }
            else
            {
                Personnage(b, agent, x0, y0, 5, etat, s.Temps, depuis: depuis, signe: !ecoute);
                if (ferme)
                {
                    Zzz(b, T - 30, T / 2, 56, s.Temps, agent * 0.2);
                }
            }

            if (ecoute)
            {
                Ecoute(b, T / 2, y0, agent == 0 ? 15 : 22, s.Temps);
            }

            Liaison(b, agent, s);
        }

        // Étoiles qui montent derrière une IA qui travaille (idée de Julien, 25/10) : six petites étoiles à quatre branches,
        // à sa couleur (Claude pêche, Codex lavande, Antigravity or), qui montent en ondulant et scintillent avant de
        // s'effacer, chacune à son rythme.
        private static void Etoiles(BitmapBuilder b, Int32 agent, Double t)
        {
            var c = agent switch { 0 => new BitmapColor(255, 190, 150), 1 => new BitmapColor(190, 180, 255), _ => new BitmapColor(255, 214, 102) };
            for (var k = 0; k < 6; k++)
            {
                var montee = ((t * 0.32) + (k / 6.0) + (agent * 0.11)) % 1;
                var x = 12 + (k * 18) + (Int32)(Math.Sin((t * 1.7) + (k * 2.3)) * 5);
                var y = T - 6 - (Int32)(montee * (T - 4));
                var alpha = (Int32)(255 * Math.Min(1, (1 - montee) * 1.8) * (0.75 + (0.25 * Math.Sin((t * 6) + k))));
                if (alpha <= 10)
                {
                    continue;
                }

                var e = new BitmapColor(c, alpha);
                var bras = k % 2 == 0 ? 5 : 4;
                b.FillRectangle(x - bras, y - 1, (2 * bras) + 1, 3, e);
                b.FillRectangle(x - 1, y - bras, 3, (2 * bras) + 1, e);
                b.FillRectangle(x, y, 1, 1, new BitmapColor(255, 255, 255, alpha));
            }
        }

        // Une fenêtre de quota pleine (99,5 % ou plus utilisés).
        private static Boolean QuotaPlein(Quota q) => q is not null && (q.Cinq >= 0.995 || q.Semaine >= 0.995);

        // Écart entre deux touches voisines, en pixels du contenu (marges des deux touches et bord du pad entre elles).
        private const Int32 EcartTouches = 36;

        // Relais (choix de Julien, 25/10) : des messagers font le va-et-vient du tout premier relais. Un mini-Clawd part du
        // flanc de Claude, traverse les touches jusqu'au flanc du pair qui travaille pour lui (Codex ou Antigravity) et lui
        // envoie une décharge électrique ; puis un mini-pair repart vers Claude et le secoue d'une décharge à son tour. En
        // boucle, 3,6 s : aller (40 %), décharge sur le pair (10 %), retour (40 %), décharge sur Claude (10 %). Avec les deux
        // pairs, les cycles sont décalés d'une demi-période.
        private static void Liaison(BitmapBuilder b, Int32 agent, Scene s)
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
                const Int32 Flanc = 46; // le messager s'arrête au flanc du personnage visité (80 px de large, centrés)
                Int32 Local(Double g) => (Int32)(agent == 0 ? (T / 2) + (sens * g) : (T / 2) - (sens * (distance - g)));
                var phase = ((s.Temps / 3.6) + (pair == 2 ? 0.5 : 0)) % 1;
                var (aller, k, decharge) = phase switch
                {
                    < 0.4 => (true, Lisse(phase / 0.4), false),
                    < 0.5 => (true, 1.0, true),
                    < 0.9 => (false, Lisse((phase - 0.5) / 0.4), false),
                    _ => (false, 1.0, true),
                };
                var g = Flanc + ((aller ? k : 1 - k) * (distance - (2 * Flanc)));
                var x = Local(g);
                var y = T - 22;
                if (x > -24 && x < T + 24)
                {
                    if (aller)
                    {
                        PetitClawd(b, x, y, decharge ? 0 : s.Temps);
                    }
                    else
                    {
                        PetitPair(b, pair, x, y - 2, decharge ? 0 : s.Temps);
                    }
                }

                // La décharge, sur la touche du visité : un éclair du messager jusqu'au personnage, des étincelles autour.
                var visite = aller ? pair : 0;
                if (decharge && agent == visite)
                {
                    var versCentre = x < T / 2 ? 1 : -1;
                    Decharge(b, x + (versCentre * 6), y - 4, (T / 2) - (versCentre * 18), (T / 2) - 4, s.Temps);
                }
            }
        }

        // Accélère puis ralentit (0 → 1).
        private static Double Lisse(Double k) => k * k * (3 - (2 * k));

        // Éclair jaune-blanc qui crépite de (x1, y1) à (x2, y2), et quatre étincelles autour du point d'arrivée ; il se
        // redessine 16 fois par seconde.
        private static void Decharge(BitmapBuilder b, Int32 x1, Int32 y1, Int32 x2, Int32 y2, Double t)
        {
            var tirage = (Int32)(t * 16);
            var eclat = new BitmapColor(255, 236, 140);
            const Int32 Morceaux = 7;
            var (px, py) = (x1, y1);
            for (var i = 1; i <= Morceaux; i++)
            {
                var k = i / (Double)Morceaux;
                var nx = x1 + (Int32)((x2 - x1) * k);
                var ny = y1 + (Int32)((y2 - y1) * k) + (i == Morceaux ? 0 : (((tirage * 7) + (i * 13)) % 9) - 4);
                for (var j = 0; j <= 4; j++)
                {
                    var ix = px + ((nx - px) * j / 4);
                    var iy = py + ((ny - py) * j / 4);
                    b.FillRectangle(ix - 2, iy - 2, 5, 5, new BitmapColor(eclat, 90));
                    b.FillRectangle(ix - 1, iy - 1, 3, 3, BitmapColor.White);
                }

                (px, py) = (nx, ny);
            }

            for (var e = 0; e < 4; e++)
            {
                var angle = (e * Math.PI / 2) + (tirage * 0.7);
                var r = 8 + ((tirage + e) % 3 * 3);
                b.FillRectangle(x2 + (Int32)(Math.Cos(angle) * r) - 1, y2 + (Int32)(Math.Sin(angle) * r) - 1, 3, 3, eclat);
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

        // Codex ou Antigravity en petit (2 px par case), qui sautille : centré en cx, haut en y.
        private static void PetitPair(BitmapBuilder b, Int32 pair, Int32 cx, Int32 y, Double t)
        {
            const Int32 P = 2;
            var x0 = cx - (8 * P);
            var y1 = y + (Math.Floor(t * 8) % 2 == 0 ? 0 : -1);
            if (pair == 1)
            {
                Pixels(b, Nuage, x0, y1, P, (_, _, ry) => Melange(new BitmapColor(177, 167, 255), new BitmapColor(57, 65, 255), ry / 11.0));
            }
            else
            {
                Pixels(b, Arche, x0, y1, P, (_, cx2, ry) => CouleurArche(cx2, ry));
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
                    b.DrawText("z", zx, zy, 24, 24, new BitmapColor(170, 170, 178, (Int32)(255 * (1 - k))), 12 + (Int32)(k * 12));
                }
            }
        }

        // Réveil pendant le démarrage de son appli (lancée par le pad il y a ecoule secondes) : ses couleurs reviennent
        // du gris, il ouvre les yeux et s'étire une fois ; une roue grise de chargement tourne au-dessus de lui.
        private static void Reveil(BitmapBuilder b, Int32 agent, Int32 x0, Int32 y0, Int32 p, Double ecoule, Double t)
        {
            var eveille = ecoule > 0.8 || Math.Floor(ecoule * 6) % 2 == 1; // un battement de paupières, puis éveillé
            var etire = ecoule < 1.6 ? -(Int32)(Math.Sin(ecoule / 1.6 * Math.PI) * 2 * p) : 0; // un seul étirement
            Personnage(b, agent, x0, y0 + etire, p, eveille ? Etat.Repos : Etat.Ferme, t, signe: false);
            Roue(b, x0 + (8 * p), Math.Max(2 * p, y0 - (3 * p)), p, t, new BitmapColor(200, 200, 206), 3);
        }

        // Écoute pendant une dictée dans son appli : un micro blanc et un point rouge d'enregistrement au-dessus de lui,
        // des ondes cyan régulières de part et d'autre de sa tête (le rouge seul faisait penser à une erreur).
        private static void Ecoute(BitmapBuilder b, Int32 cx, Int32 y0, Int32 tete, Double t)
        {
            var haut = Math.Max(0, y0 - 29);
            Carte(b, Micro, cx - 12, haut, 2, BitmapColor.White);
            var rec = 0.5 + (0.5 * Math.Sin(t * 4));
            Rond(b, cx + 12, haut + 2, 8, 8, 4, Melange(new BitmapColor(120, 20, 20), Rouge, rec));
            var cy = y0 + tete;
            for (var k = 0; k < 2; k++)
            {
                var onde = ((t * 1.2) + (k * 0.5)) % 1;
                var ecart = 44 + (Int32)(onde * 10);
                var alpha = (Int32)(255 * (1 - onde));
                foreach (var cote in new[] { -1, 1 })
                {
                    var x = cx + (cote * ecart);
                    b.FillRectangle(x - 1, cy - 6, 3, 12, new BitmapColor(Cyan, alpha));
                    b.FillRectangle(x - 1 - (cote * 3), cy - 10, 3, 4, new BitmapColor(Cyan, alpha));
                    b.FillRectangle(x - 1 - (cote * 3), cy + 6, 3, 4, new BitmapColor(Cyan, alpha));
                }
            }
        }

        // Clignotement d'une IA qui appelle l'utilisateur, sous le contenu, en anneau arrondi : jusqu'à ce qu'il ouvre la
        // conversation. Claude, le métronome de la Trinité, garde ses propres couleurs (rose pour une question ou une
        // autorisation, cyan pour une fin : choix de Julien) ; une erreur pulse rouge, doucement.
        private static void Signal(BitmapBuilder b, Int32 agent, Conversation c, Double t, Arrondis rayon)
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
            var force = question
                ? (t * 2 % 1 < 0.5 ? 1.0 : 0.25) // question : franc, deux fois par seconde
                : 0.35 + (0.65 * (0.5 + (0.5 * Math.Sin(t * (erreur ? 3.5 : 2.2))))); // fin, erreur : respiration
            Rond(b, 0, 0, T, T, rayon, Melange(Fond, teinte, force));
            Rond(b, 5, 5, T - 10, T - 10, rayon.Moins(5), Melange(Fond, teinte, force * 0.22));
        }

        // Une IA dessinée dans l'état donné ; depuis : secondes depuis le changement d'état (célébration, secousse à
        // l'arrivée) ; signe : son signe d'état au-dessus de la tête (pas pendant l'écoute ni le réveil, qui ont le leur).
        public static void Personnage(BitmapBuilder b, Int32 i, Int32 x0, Int32 y0, Int32 p, Etat etat, Double t, Boolean dort = false, Double depuis = 99, Boolean signe = true)
        {
            var teinte = etat == Etat.Ferme ? Teinte.Eteinte : dort ? Teinte.Dort : etat == Etat.Limite ? Teinte.Lasse : Teinte.Vive;
            var dessin = teinte is Teinte.Dort or Teinte.Eteinte ? Etat.Repos : etat;
            switch (i)
            {
                case 0: DessinerClawd(b, x0, y0, p, dessin, t, teinte, depuis); break;
                case 1: DessinerCodex(b, x0, y0, p, dessin, t, teinte, depuis); break;
                default: DessinerArche(b, x0, y0, p, dessin, t, teinte, depuis); break;
            }

            // Fini : deux feux d'artifice éclatent de part et d'autre, pour les trois (idée de Julien, 25/10), à l'arrivée.
            if (etat == Etat.Fini && depuis < 4 && teinte is Teinte.Vive or Teinte.Lasse)
            {
                Feux(b, x0, y0, p, t);
            }

            if (signe)
            {
                Signe(b, x0 + (8 * p), y0 - (4 * p), p, etat, t);
            }
        }

        // Signe d'état au-dessus de la tête, centré en cx, haut en y ; il pulse ou flotte, sans jamais disparaître.
        private static void Signe(BitmapBuilder b, Int32 cx, Int32 y, Int32 p, Etat etat, Double t)
        {
            var flotte = (Int32)Math.Round(Math.Sin(t * 3) * p / 3);
            y = Math.Max(0, y);
            switch (etat)
            {
                case Etat.Attend:
                    var q = Math.Max(2, p * 4 / 5);
                    Carte(b, Question, cx - (3 * q / 2), y + flotte, q, Ambre);
                    break;
                case Etat.Autorisation:
                    Cadenas(b, cx, y + flotte, p, t);
                    break;
                case Etat.Fini:
                    Carte(b, Coche, cx - 12, y + p + flotte, 2, Vert);
                    break;
                case Etat.Erreur: // un peu plus haut : il touchait la tête (remarque de Julien, 25/10)
                    var haut = Math.Max(0, y - 3);
                    Carte(b, Triangle, cx - 10, haut, 2, Rouge);
                    b.FillRectangle(cx - 1, haut + 5, 2, 6, BitmapColor.White);
                    b.FillRectangle(cx - 1, haut + 13, 2, 2, BitmapColor.White);
                    break;
                case Etat.Limite:
                    JaugeVide(b, cx, Math.Max(3, y - 3));
                    break;
                case Etat.Ferme:
                    Arret(b, cx, Math.Max(0, y - 3));
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

        private static readonly String[] Question = ["###", "..#", ".##", "...", ".#."];

        private static readonly String[] Triangle =
        [
            "....##....", "...####...", "...####...", "..######..", "..######..", ".########.", ".########.", "##########",
            "##########",
        ];

        private static readonly String[] Anse = [".###.", "#...#", "#...#"];

        // Roue de chargement : huit cases en cercle, une tête vive et sa traîne qui tournent ; traine : cases allumées.
        private static void Roue(BitmapBuilder b, Int32 cx, Int32 cy, Int32 p, Double t, BitmapColor c, Int32 traine)
        {
            var tete = (Int32)(t * 10) % 8;
            var e = Math.Max(4, p * 4 / 5);
            for (var k = 0; k < 8; k++)
            {
                var retard = (tete - k + 8) % 8;
                var alpha = retard < traine ? 255 - (retard * 50) : 80;
                var angle = k * Math.PI / 4;
                b.FillRectangle(cx + (Int32)(Math.Cos(angle) * 2 * p) - (e / 2), cy + (Int32)(Math.Sin(angle) * 2 * p) - (e / 2), e, e, new BitmapColor(c, alpha));
            }
        }

        // Cadenas ambre centré en cx : son anse, le corps et un trou de serrure.
        private static void Cadenas(BitmapBuilder b, Int32 cx, Int32 y, Int32 p, Double t)
        {
            var q = Math.Max(2, p * 3 / 5);
            Carte(b, Anse, cx - (5 * q / 2), y, q, Ambre);
            b.FillRectangle(cx - (7 * q / 2), y + (3 * q), 7 * q, 5 * q, Ambre);
            var serrure = t * 2 % 1 < 0.5 ? Encre : new BitmapColor(120, 80, 0);
            b.FillRectangle(cx - (q / 2), y + (4 * q), q, 2 * q, serrure);
        }

        // Jauge de quota vide, barrée : le langage des jauges de la ligne du milieu (pas une batterie, qui évoquait une
        // panne de courant) ; capsule large à contour franc, vide, barrée d'un trait épais, ambre sourd, sans clignoter.
        private static void JaugeVide(BitmapBuilder b, Int32 cx, Int32 y)
        {
            var sourd = new BitmapColor(214, 158, 46);
            Rond(b, cx - 20, y, 40, 16, 8, sourd);
            Rond(b, cx - 16, y + 4, 32, 8, 4, Fond);
            for (var k = 0; k < 9; k++)
            {
                b.FillRectangle(cx + 12 - (k * 3), y - 3 + (k * 3), 5, 5, sourd); // barre épaisse en travers
            }
        }

        // Bouton marche/arrêt éteint, gris : l'appli est fermée.
        private static void Arret(BitmapBuilder b, Int32 cx, Int32 y)
        {
            var gris = new BitmapColor(120, 120, 126);
            for (var k = 0; k < 12; k++)
            {
                var angle = (-Math.PI / 2) + 0.55 + (k * ((2 * Math.PI) - 1.1) / 11);
                b.FillRectangle(cx + (Int32)(Math.Cos(angle) * 7) - 1, y + 8 + (Int32)(Math.Sin(angle) * 7) - 1, 3, 3, gris);
            }

            b.FillRectangle(cx - 1, y, 3, 8, gris);
        }

        // Clawd sans ses bras (colonnes 0-1 et 14-15 des rangées 4-5), dessinés à part selon l'état.
        private static readonly String[] ClawdCorps =
        [
            "..oooooooooooo..", "..oooooooooooo..", "..ooEEooooEEoo..", "..ooEEooooEEoo..",
            "..oooooooooooo..", "..oooooooooooo..", "..oooooooooooo..", "..oooooooooooo..",
        ];

        // Clawd : il respire au repos ; au travail, ses yeux moulinent et il trotte ;
        // en question, il lève un bras ; en autorisation, il tend les deux bras ouverts ; fini, lunettes noires (petit bond
        // à l'arrivée, un reflet passe sur les verres) ; erreur, yeux en croix et une secousse à l'arrivée ; limite,
        // affaissé, paupières lourdes.
        private static void DessinerClawd(BitmapBuilder b, Int32 x0, Int32 y0, Int32 p, Etat etat, Double t, Teinte teinte, Double depuis)
        {
            var corps = Ton(Orange, teinte);
            var encre = Ton(Encre, teinte);
            var vive = teinte is Teinte.Vive or Teinte.Lasse;
            x0 += vive && etat == Etat.Erreur && depuis < 1 ? (Int32)(Math.Sin(t * 60) * p / 2) : 0;
            var y = y0 + (!vive ? 0 : etat switch
            {
                Etat.Travaille => (Int32)Math.Round(Math.Sin(t * 10)) * p / 2,
                Etat.Limite => p / 2,
                Etat.Fini when depuis < 4 => -(Int32)(Math.Max(0, Math.Sin(t * 6)) * p),
                Etat.Repos => (Int32)Math.Round(Math.Sin(t * 1.6)) * p / 3, // respiration, comme les deux autres
                _ => 0,
            });
            Pixels(b, ClawdCorps, x0, y, p, (ch, _, _) => ch == 'o' ? corps : encre);

            // Les bras : à l'horizontale ; en autorisation, tendus et ouverts.
            var tendus = vive && etat == Etat.Autorisation ? p : 0;
            b.FillRectangle(x0 - tendus, y + (4 * p), 2 * p, 2 * p, corps);
            b.FillRectangle(x0 + (14 * p) + tendus, y + (4 * p), 2 * p, 2 * p, corps);

            if (!vive)
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
                if (t % 2 < 0.3)
                {
                    b.FillRectangle(x0 + (12 * p), y + (2 * p), Math.Max(2, p / 2), Math.Max(2, p / 2), BitmapColor.White); // reflet
                }
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

            var pas = vive && etat == Etat.Travaille && Math.Floor(t * 4) % 2 == 1 ? 1 : 0; // il trotte
            var sol = vive && etat == Etat.Fini ? y : y0; // un bond emporte les jambes (relecture de Codex, 25/10)
            foreach (var lx in new[] { 3 + pas, 5 + pas, 10 - pas, 12 - pas })
            {
                b.FillRectangle(x0 + (lx * p), sol + (8 * p), p, 2 * p, corps);
            }

            if (vive && etat == Etat.Attend)
            {
                var h = (Int32)(Math.Abs(Math.Sin(t * 5)) * p * 2);
                b.FillRectangle(x0, y + (2 * p) - h, p, (2 * p) + h, corps); // un bras levé
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
                    continue; // lunettes noires, dessinées après la boucle
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
                    Etat.Travaille => (Math.Sin(t * 3) > 0 ? 1 : 0, 1), // vers la feuille, le terminal, le code
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

            // Fini : lunettes noires pour les trois, comme Clawd (idée de Julien, 25/10) ; verres de 4 cases si les yeux sont
            // écartés (Codex), de 3 sinon (Antigravity), un pont entre les deux, un reflet qui passe.
            if (etat == Etat.Fini && teinte is Teinte.Vive or Teinte.Lasse)
            {
                var verre = new BitmapColor(17, 17, 17);
                var largeur = colonnes[1] - colonnes[0] >= 6 ? 4 : 3;
                var (g1, g2) = (colonnes[0] - 1, colonnes[1] - 1);
                b.FillRectangle(x0 + (g1 * p), y, largeur * p, 2 * p, verre);
                b.FillRectangle(x0 + (g2 * p), y, largeur * p, 2 * p, verre);
                b.FillRectangle(x0 + ((g1 + largeur) * p), y, (g2 - g1 - largeur) * p, Math.Max(2, p / 2), verre);
                foreach (var g in new[] { g1, g2 })
                {
                    b.FillRectangle(x0 + (g * p) + (p / 2), y + (p / 3), p, Math.Max(2, p / 2), BitmapColor.White);
                }

                if (t % 2 < 0.3)
                {
                    b.FillRectangle(x0 + ((g2 + largeur - 1) * p), y, Math.Max(2, p / 2), Math.Max(2, p / 2), BitmapColor.White); // reflet
                }
            }
        }

        // Codex : son logo-nuage prend des yeux. Au repos, il respire et flâne du regard ; au travail, il sautille et tape
        // une ligne de code bien visible après son « > » (le curseur avance), des bits montent. En question, il penche la
        // tête et son terminal affiche un « ? » ; en autorisation, son terminal attend, curseur plein bloqué. Fini : coche
        // dans le terminal, petits bonds à l'arrivée. Erreur : yeux en croix, une secousse à l'arrivée.
        private static void DessinerCodex(BitmapBuilder b, Int32 x0, Int32 y0, Int32 p, Etat etat, Double t, Teinte teinte, Double depuis)
        {
            var haut = new BitmapColor(177, 167, 255);
            var milieu = new BitmapColor(122, 157, 255);
            var bas = new BitmapColor(57, 65, 255);
            BitmapColor Rangee(Int32 y) => Ton(y < 6 ? Melange(haut, milieu, y / 5.5) : Melange(milieu, bas, (y - 5.5) / 5.5), teinte);
            var vive = teinte is Teinte.Vive or Teinte.Lasse;
            var fete = etat == Etat.Fini && depuis < 4;

            var saut = !vive ? 0 : etat switch
            {
                Etat.Travaille => -p, // penché sur son terminal
                Etat.Fini when fete => -(Int32)(Math.Max(0, Math.Sin(t * 5)) * 1.5 * p), // petits bonds de joie
                Etat.Limite => p / 2,
                Etat.Attend or Etat.Autorisation or Etat.Fini or Etat.Erreur => 0,
                _ => (Int32)Math.Round(Math.Sin(t * 1.6)) * p / 3, // respiration
            };
            var penche = vive && etat == Etat.Attend ? (Int32)Math.Round(Math.Sin(t * 2.5) * p) : 0;
            var secousse = vive && etat == Etat.Erreur && depuis < 1 ? (Int32)(Math.Sin(t * 60) * p / 2) : 0;
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

            if (vive && etat == Etat.Travaille)
            {
                Terminal(b, x + (4 * p), y + (9 * p), p, t);
            }
            else if (vive && etat == Etat.Attend)
            {
                Carte(b, Question, x + (8 * p), y + (6 * p) + (p / 2), Math.Max(2, p * 3 / 5), trait); // le terminal demande
            }
            else if (vive && etat == Etat.Autorisation)
            {
                b.FillRectangle(x + (8 * p), y + (6 * p) + (p / 2), p + (p / 2), (2 * p) + (p / 2), trait); // curseur bloqué
            }
            else if (vive && etat == Etat.Fini)
            {
                Carte(b, Coche, x + (8 * p), y + (6 * p) + (p / 2), Math.Max(1, p * 2 / 5), Vert); // « ✓ » dans le terminal
            }
            else if (!vive || Math.Floor(t * 2) % 2 == 0)
            {
                b.FillRectangle(x + (8 * p), y + (8 * p), 3 * p, p, trait); // « _ » qui clignote doucement
            }
        }

        // Codex au travail : un petit terminal posé devant lui (plaque sombre, contour cyan), un gros « > » et une ligne
        // qui s'écrit, curseur qui avance ; deux pattes tapent à tour de rôle sur son bord.
        private static void Terminal(BitmapBuilder b, Int32 x, Int32 y, Int32 p, Double t)
        {
            var (l, h) = (8 * p, 4 * p);
            var cyan = new BitmapColor(125, 211, 252);
            b.FillRectangle(x - 2, y - 2, l + 4, h + 4, cyan);
            b.FillRectangle(x, y, l, h, new BitmapColor(14, 18, 32));
            foreach (var (cx, cy) in new[] { (1, 1), (2, 2), (1, 3) })
            {
                b.FillRectangle(x + (cx * p / 2) + p / 2, y + (cy * p * 3 / 4), Math.Max(2, p * 3 / 5), Math.Max(2, p * 3 / 5), cyan); // « > »
            }

            var n = 1 + ((Int32)(t * 5) % 4);
            for (var k = 0; k < n; k++)
            {
                b.FillRectangle(x + (3 * p) + (k * p), y + (h / 2) - 1, p - 2, Math.Max(2, p / 2), BitmapColor.White);
            }

            if (Math.Floor(t * 6) % 2 == 0)
            {
                b.FillRectangle(x + (3 * p) + (n * p), y + (h / 2) - p / 2, Math.Max(2, p / 3), p, BitmapColor.White);
            }

            // Les pattes : deux ronds clairs sur le bord haut, qui tapent l'un après l'autre.
            var tape = Math.Floor(t * 8) % 2 == 0;
            var patte = new BitmapColor(200, 200, 255);
            b.FillRectangle(x + p, y - p + (tape ? p / 2 : 0), 2 * p, p, patte);
            b.FillRectangle(x + l - (3 * p), y - p + (tape ? 0 : p / 2), 2 * p, p, patte);
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
        // travail, ses réacteurs crachent bleu, elle vole plus vite et un satellite gravite autour d'elle. En question,
        // elle se balance en regardant l'utilisateur ; en autorisation, elle reste en vol stationnaire. Fini : elle se
        // pose, réacteurs coupés. Erreur et limite : posée.
        private static void DessinerArche(BitmapBuilder b, Int32 x0, Int32 y0, Int32 p, Etat etat, Double t, Teinte teinte, Double depuis)
        {
            var vive = teinte is Teinte.Vive or Teinte.Lasse;
            var posee = etat is Etat.Fini or Etat.Limite or Etat.Erreur;
            var vol = !vive || posee || etat == Etat.Autorisation ? 0
                : (Int32)Math.Round(Math.Sin(t * (etat == Etat.Travaille ? 6 : 2)) * (etat == Etat.Travaille ? 1.5 : 1));
            var secousse = vive && etat == Etat.Erreur && depuis < 1 ? (Int32)(Math.Sin(t * 60) * p / 2) : 0;
            var x = x0 + secousse + (vive && etat == Etat.Attend ? (Int32)Math.Round(Math.Sin(t * 3) * p / 2) : 0);
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

        }

        // Deux feux d'artifice de part et d'autre : une fusée monte, puis éclate en huit points qui s'écartent.
        private static void Feux(BitmapBuilder b, Int32 x0, Int32 y, Int32 p, Double t)
        {
            BitmapColor[] teintes = [new(150, 255, 180), new(255, 225, 90), new(255, 150, 210)]; // couleurs franches, lisibles sur l'anneau
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
                var c = new BitmapColor(teintes[((Int32)(t * 0.55) + j) % 3], (Int32)(255 * (1 - (r * r * r)))); // vive jusqu'au bout
                for (var a = 0; a < 8; a++)
                {
                    var angle = a * Math.PI / 4;
                    b.FillRectangle(cx + (Int32)(Math.Cos(angle) * r * 4.5 * p), cy + (Int32)(Math.Sin(angle) * r * 4.5 * p), e, e, c);
                }
            }
        }
    }
}

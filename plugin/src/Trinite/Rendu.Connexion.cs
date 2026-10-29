#if !POSTE
namespace Loupedeck.TrinitePlugin
{
    using System;

    public static partial class Rendu
    {
        // La connexion fait partie du personnage, sans texte ni bandeau. Les messagers gardent toute leur voie
        // y=92..115 ; taille, centre et signes supérieurs sont ceux du personnage normal. Un vrai état d'activité
        // prime toujours : ni question, ni permission, ni fin, ni erreur, ni travail ne sont remplacés par le setup.
        private static Boolean PersonnageConnexion(BitmapBuilder b, Int32 agent, Int32 x0, Int32 y0, Int32 p, Etat etat, Scene s)
        {
            var connexions = s.Instantane.Connexions;
            if (etat is not (Etat.Repos or Etat.Ferme) || connexions is null || agent >= connexions.Length
                || connexions[agent] is not { } connexion || connexion.Etat == EtatConnexion.Pret)
            {
                return false;
            }

            // Le corps ne bouge pas : chaque besoin se distingue par le visage et un objet tenu devant lui.
            // L'absence seule est entièrement grise ; les autres gardent leurs couleurs atténuées.
            Personnage(b, agent, x0, y0, p, connexion.Etat == EtatConnexion.Absent ? Etat.Ferme : Etat.Repos,
                s.Temps, dort: true, signe: false);
            VisageConnexion(b, agent, x0, y0, p, connexion.Etat);
            var cx = x0 + (8 * p);
            const Int32 Cy = 65;
            switch (connexion.Etat)
            {
                case EtatConnexion.Activer:
                    PriseDeconnectee(b, cx, Cy, Ambre);
                    break;
                case EtatConnexion.Relancer:
                    Reconnexion(b, cx, Cy, Ambre, s.Temps);
                    break;
                case EtatConnexion.Reparer:
                    ClefConnexion(b, cx, Cy);
                    break;
                case EtatConnexion.EnCours:
                    ProgressionConnexion(b, cx, Cy, s.Temps);
                    break;
            }

            return true;
        }

        // Les yeux gardent la place et la couleur propres à chaque personnage : noirs chez Clawd, blancs sur les
        // logos. Aucun visage ne monte au-dessus de la silhouette, dans la zone des questions et permissions.
        private static void VisageConnexion(BitmapBuilder b, Int32 agent, Int32 x0, Int32 y0, Int32 p, EtatConnexion etat)
        {
            if (etat == EtatConnexion.Absent) { return; } // endormi, gris, sans objet : absence bien distincte
            var colonnes = agent == 2 ? new[] { 5, 9 } : new[] { 4, 10 };
            var y = y0 + ((agent == 1 ? 3 : 2) * p);
            var encre = agent == 0 ? Encre : Texte;
            BitmapColor Peau(Int32 ex) => agent switch
            {
                0 => Ton(Orange, Teinte.Dort),
                1 => Ton(Melange(new BitmapColor(177, 167, 255), new BitmapColor(122, 157, 255), 3 / 5.5), Teinte.Dort),
                _ => Ton(CouleurArche(ex, 2), Teinte.Dort),
            };
            for (var i = 0; i < colonnes.Length; i++)
            {
                var ex = colonnes[i];
                var x = x0 + (ex * p);
                b.FillRectangle(x, y, 2 * p, 2 * p, Peau(ex));
                if (etat == EtatConnexion.Erreur)
                {
                    Croix(b, x, y, 2 * p, agent == 0 ? new BitmapColor(255, 192, 178) : Texte);
                    b.FillRectangle(x - 2, y + (2 * p) + 1, 5, 3, Rouge); // joues fiévreuses
                    continue;
                }

                b.FillRectangle(x, y + 1, 2 * p, (2 * p) - 1, encre);
                if (agent != 0)
                {
                    b.FillRectangle(x + (i == 0 ? p : 0), y + p, p, p,
                        etat == EtatConnexion.EnCours ? Cyan : Nuit);
                }
                if (etat == EtatConnexion.Reparer)
                {
                    // Sourcils soucieux : leurs coins intérieurs se lèvent, sans déplacer les yeux.
                    for (var k = 0; k < 3; k++)
                    {
                        b.FillRectangle(x + (k * 3), y - 2 - (i == 0 ? k : 2 - k), 3, 2, encre);
                    }
                }
            }

            if (etat == EtatConnexion.Erreur)
            {
                var mx = x0 + (7 * p);
                var my = y0 + ((agent == 0 ? 5 : agent == 1 ? 6 : 5) * p);
                for (var k = 0; k < 5; k++)
                {
                    b.FillRectangle(mx + (k * 3), my + (k % 2 * 2), 3, 2, new BitmapColor(255, 192, 178));
                }
            }
        }

        // Deux demi-prises nettement séparées : brancher le connecteur. Aucun objet ne descend dans la voie Relay.
        private static void PriseDeconnectee(BitmapBuilder b, Int32 cx, Int32 cy, BitmapColor couleur)
        {
            const Int32 Ecart = 4;
            Rond(b, cx - 16, cy - 7, 32, 14, 4, Fond);
            b.FillRectangle(cx - Ecart - 8, cy - 4, 6, 8, couleur);
            b.FillRectangle(cx - Ecart - 2, cy - 3, 3, 2, couleur);
            b.FillRectangle(cx - Ecart - 2, cy + 1, 3, 2, couleur);
            b.FillRectangle(cx - Ecart - 11, cy - 1, 3, 2, couleur);
            b.FillRectangle(cx + Ecart + 2, cy - 4, 6, 8, couleur);
            b.FillRectangle(cx + Ecart - 1, cy - 3, 3, 2, couleur);
            b.FillRectangle(cx + Ecart - 1, cy + 1, 3, 2, couleur);
            b.FillRectangle(cx + Ecart + 8, cy - 1, 3, 2, couleur);
        }

        private static readonly String[] Clef =
        [
            ".........##...", "........###...", ".......###....", ".......###..##",
            ".......#######", "......#######.", ".....######...", "....#####.....",
            "...#####......", "..#####.......", ".######.......", ".##..##.......",
            ".######.......", "..####........",
        ];

        // Clef à mâchoire ouverte et trou dans le manche : sa silhouette reste lisible à la taille native.
        private static void ClefConnexion(BitmapBuilder b, Int32 cx, Int32 cy)
        {
            var x = cx - 14;
            var y = cy - 13;
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++) { Carte(b, Clef, x + dx, y + dy, 2, Fond); }
            }
            Carte(b, Clef, x, y, 2, new BitmapColor(221, 231, 242));
            b.FillRectangle(x + 5, y + 19, 4, 4, Ambre); // le manche, tenu par le personnage
        }

        // Préparation : une progression cyan animée, distincte d'une prise en attente ou d'une relance.
        private static void ProgressionConnexion(BitmapBuilder b, Int32 cx, Int32 cy, Double t)
        {
            Rond(b, cx - 20, cy - 8, 40, 16, 4, Cyan);
            Rond(b, cx - 18, cy - 6, 36, 12, 2, Fond);
            var tete = (Int32)(t * 5) % 5;
            for (var k = 0; k < 5; k++)
            {
                var retard = (tete - k + 5) % 5;
                b.FillRectangle(cx - 15 + (k * 6), cy - 3, 4, 6,
                    retard == 0 ? Texte : retard < 3 ? Cyan : new BitmapColor(17, 67, 78));
            }
        }

        // Petite flèche circulaire dans le personnage, distincte de la roue de réveil située au-dessus.
        private static readonly String[] FlecheRelance =
        [
            "..########....", ".##########...", "###......###..", "##........##..",
            "##......######", "##.......####.", "##........##..", "##............",
            "##............", "###.......##..", ".###.....###..", "..#########...",
            "...#######....", "..............",
        ];

        private static void Reconnexion(BitmapBuilder b, Int32 cx, Int32 cy, BitmapColor couleur, Double t)
        {
            Rond(b, cx - 15, cy - 14, 30, 28, 4, Fond);
            Carte(b, FlecheRelance, cx - 14, cy - 13, 2, couleur);
            if (Math.Floor(t * 2) % 2 == 0)
            {
                b.FillRectangle(cx - 6, cy - 13, 4, 2, Texte);
            }
        }
    }
}
#endif

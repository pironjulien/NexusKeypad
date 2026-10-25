namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.Collections.Generic;

    // Demande d'autorisation de Claude en attente : la ligne du bas de la page 1 change le temps de la réponse.
    // Place 7 (Capture) : ✓ Approuver ; place 8 (Photo) : ce qui est demandé ; place 9 (Dictée) : ✗ Refuser. En pixels,
    // comme les têtes ; les deux choix respirent pour se faire voir.
    public static partial class Rendu
    {
        private static readonly String[] MotifApprouver =
        [
            ".........##",
            "........###",
            ".......###.",
            "##....###..",
            "###..###...",
            ".#####.....",
            "..###......",
        ];

        private static readonly String[] MotifRefuser =
        [
            "##.....##",
            "###...###",
            ".###.###.",
            "..#####..",
            ".###.###.",
            "###...###",
            "##.....##",
        ];

        private static void Motif(BitmapBuilder b, String[] motif, Int32 p, BitmapColor c)
        {
            var x0 = (T - (motif[0].Length * p)) / 2;
            var y0 = (T - (motif.Length * p)) / 2;
            for (var l = 0; l < motif.Length; l++)
            {
                for (var k = 0; k < motif[l].Length; k++)
                {
                    if (motif[l][k] == '#')
                    {
                        b.FillRectangle(x0 + (k * p), y0 + (l * p), p, p, c);
                    }
                }
            }
        }

        // Le texte demandé en lignes courtes pour la touche (mots entiers quand ils tiennent).
        internal static List<String> Lignes(String texte, Int32 largeur, Int32 nombre)
        {
            var lignes = new List<String>();
            var ligne = "";
            foreach (var mot in (texte ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var reste = mot;
                while (reste.Length > 0 && lignes.Count < nombre)
                {
                    var essai = ligne.Length == 0 ? reste : ligne + " " + reste;
                    if (essai.Length <= largeur)
                    {
                        ligne = essai;
                        reste = "";
                    }
                    else if (ligne.Length > 0)
                    {
                        lignes.Add(ligne);
                        ligne = "";
                    }
                    else
                    {
                        lignes.Add(reste[..largeur]);
                        reste = reste[largeur..];
                    }
                }
            }

            if (ligne.Length > 0 && lignes.Count < nombre)
            {
                lignes.Add(ligne);
            }

            return lignes;
        }

        private static void ToucheDemande(BitmapBuilder b, Int32 touche, Demande d, Scene s)
        {
            var souffle = 0.55 + (0.45 * Math.Abs(Math.Sin(s.Temps * 2.5)));
            switch (touche)
            {
                case Moteur.Capture:
                    Rond(b, 4, 4, T - 8, T - 8, 18, Melange(Fond, Vert, 0.18 * souffle));
                    Motif(b, MotifApprouver, 8, Melange(Fond, Vert, 0.4 + (0.6 * souffle)));
                    break;
                case Moteur.Dictee:
                    Rond(b, 4, 4, T - 8, T - 8, 18, Melange(Fond, Rouge, 0.18 * souffle));
                    Motif(b, MotifRefuser, 8, Melange(Fond, Rouge, 0.4 + (0.6 * souffle)));
                    break;
                default:
                    // Ce que Claude demande : l'outil, puis la commande ou le fichier, en court.
                    b.DrawText(String.IsNullOrEmpty(d.Outil) ? "Claude" : d.Outil, 0, 10, T, 22, Orange, 17);
                    var y = 38;
                    foreach (var ligne in Lignes(d.Resume, 14, 4))
                    {
                        b.DrawText(ligne, 0, y, T, 16, Texte, 12);
                        y += 17;
                    }

                    break;
            }
        }
    }
}

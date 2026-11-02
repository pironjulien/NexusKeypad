namespace Loupedeck.TrinitePlugin
{
    using System;

    // Ciel de nuit, écran du PC éteint : les 9 touches forment un même ciel étoilé, la lune au milieu ; une IA qui
    // travaille ou appelle l'utilisateur reste éveillée, les autres dorment.
    public static partial class Rendu
    {
        private static readonly BitmapColor Zenith = new(4, 6, 18);
        private static readonly BitmapColor Lune = new(254, 243, 199);

        // Fond dégradé, du zénith en haut du pad à la nuit bleutée en bas, et étoiles qui scintillent chacune à son rythme.
        private static void CielDeNuit(BitmapBuilder b, Int32 touche, Double t)
        {
            var place = Moteur.Place(touche) % 9;
            var rangee = place / 3;
            for (var y = 0; y < T; y++)
            {
                b.FillRectangle(0, y, T, 1, Melange(Zenith, Nuit, ((rangee * T) + y) / (3.0 * T)));
            }

            // Positions tirées une fois pour toutes par place (même ciel à chaque image), sans Random partagé.
            var graine = (UInt32)(place + 1) * 2654435761u;
            UInt32 Tirage() => graine = (graine * 1664525u) + 1013904223u;
            for (var i = 0; i < 7; i++)
            {
                var x = 6 + (Int32)(Tirage() % (T - 12));
                var y = 6 + (Int32)(Tirage() % (T - 12));
                var phase = Tirage() % 628 / 100.0;
                var vitesse = 0.6 + (Tirage() % 100 / 100.0);
                var grande = i < 2;
                var eclat = 0.25 + (0.75 * (0.5 + (0.5 * Math.Sin((t * vitesse) + phase))));
                var c = Melange(Nuit, grande ? Lune : Texte, eclat);
                b.FillRectangle(x, y, 2, 2, c);
                if (grande && eclat > 0.8)
                {
                    var halo = Melange(Nuit, Lune, (eclat - 0.8) * 3);
                    b.FillRectangle(x - 2, y, 2, 2, halo);
                    b.FillRectangle(x + 2, y, 2, 2, halo);
                    b.FillRectangle(x, y - 2, 2, 2, halo);
                    b.FillRectangle(x, y + 2, 2, 2, halo);
                }
            }

            // Croissant de lune au milieu du pad : un disque clair mordu par un disque couleur du ciel.
            if (place == 4)
            {
                const Int32 Cx = T / 2, Cy = T / 2;
                b.FillCircle(Cx, Cy, 22, Lune);
                b.FillCircle(Cx + 10, Cy - 7, 20, Melange(Zenith, Nuit, (T + Cy - 7) / (3.0 * T)));
            }
        }

#if !POSTE
        private static void NuitIA(BitmapBuilder b, Int32 agent, Scene s)
        {
            var i = s.Instantane;
            var ferme = !i.Ouverte[agent];
            if (!ferme && (i.EtatIA(agent) is not (Etat.Repos or Etat.Fini) || i.Premiere(agent)?.Alerte == true))
            {
                ToucheIA(b, agent, s);
                return;
            }

            var hauteur = agent switch { 0 => 10, 1 => 12, _ => 14 } * 5;
            Personnage(b, agent, (T - 80) / 2, (T - hauteur) / 2, 5, ferme ? Etat.Ferme : Etat.Repos, s.Temps, dort: true, signe: false);
            Zzz(b, T - 30, T / 2, 56, s.Temps, agent * 0.2);
        }
#endif
    }
}

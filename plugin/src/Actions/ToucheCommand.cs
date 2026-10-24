namespace Loupedeck.TrinitePlugin
{
    using System;

    // Une commande à 18 paramètres (deux pages de 9) : chaque touche physique reçoit « Touche N » dans Options+, le plugin décide du reste
    // (Moteur.Disposition : quelle touche du plugin s'affiche à la place N).
    public class ToucheCommand : PluginDynamicCommand
    {
        private static readonly String[] Noms =
        [
            "Codex n°1", "Claude n°1", "Antigravity n°1",
            "Codex n°2", "Claude n°2", "Antigravity n°2",
            "Capture d'écran", "Photo du téléphone", "Dictée",
            "Volume moins", "Son coupé", "Volume plus",
            "Gmail", "Agenda", "Messages",
            "Santé du PC", "Store et Windows Update", "Site",
        ];

        private readonly Boolean[] _dessinees = new Boolean[Moteur.Touches];

        public ToucheCommand()
            : base()
        {
            for (var i = 0; i < Moteur.Touches; i++)
            {
                this.AddParameter($"t{i + 1}", $"Touche {i + 1} · {Noms[i]}", "Trinité");
            }
        }

        private Moteur Moteur => ((TrinitePadPlugin)this.Plugin).Moteur;

        protected override Boolean OnLoad()
        {
            ((TrinitePadPlugin)this.Plugin).Redessiner = t => this.ActionImageChanged($"t{Moteur.Place(t) + 1}");
            return true;
        }

        protected override void RunCommand(String actionParameter)
        {
            var touche = Index(actionParameter);
            if (touche >= 0)
            {
                this.Moteur.Appuyer(touche);
            }
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var touche = Index(actionParameter);
            if (touche >= 0 && !this._dessinees[touche])
            {
                this._dessinees[touche] = true;
                PluginLog.Info($"première image de la touche {touche + 1} ({imageSize})");
            }

            if (touche < 0)
            {
                return null;
            }

            this.Moteur.Demande(touche);

            // Le pad demande 116 px : l'image déjà dessinée et envoyée par le moteur ; une autre taille (Options+) à part.
            return imageSize == PluginImageSize.Width116 && this.Moteur.Image(touche) is { } image
                ? image
                : Rendu.Dessiner(touche, this.Moteur.Scene, imageSize);
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => null;

        private static Int32 Index(String parametre) =>
            parametre is { Length: 2 or 3 } && parametre[0] == 't' && Int32.TryParse(parametre.AsSpan(1), out var n) && n is >= 1 and <= Moteur.Touches
                ? Moteur.Disposition[n - 1] : -1;
    }
}

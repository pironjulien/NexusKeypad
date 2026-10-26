namespace Loupedeck.TrinitePlugin
{
    using System;

    // Une commande à 18 paramètres (deux pages de 9 ; la page 1 seule dans l'édition AI Coding Agents, les 8 premières
    // touches de la page 2 dans PC Dashboard, son site étant à part) : chaque touche physique reçoit « Touche N » dans
    // Options+, le plugin décide du reste (Moteur.Disposition : quelle touche du plugin s'affiche à la place N).
    public class ToucheCommand : PluginDynamicCommand
    {
        // Noms en anglais, langue source du SDK ; le français vient de package/localization/TrinitePad_fr-FR.xliff (Options+ en
        // français). Le nom anglais sert d'identifiant à sa traduction : le changer oblige à refaire le XLIFF.
#if POSTE
        // PC Dashboard : les noms de la page 2 seule (Moteur.Premiere = 9).
        private const Int32 PremierNom = 9;
        private static readonly String[] Noms =
        [
#else
        private const Int32 PremierNom = 0;
        private static readonly String[] Noms =
        [
            "Codex", "Claude", "Antigravity",
            "Codex quota", "Claude quota", "Antigravity quota",
            "Screen capture", "Phone photo", "Dictation",
#endif
            "Volume down", "Mute", "Volume up",
            "Gmail", "Calendar", "Messages",
            "PC health", "Store and Windows Update", "Website",
        ];

#if POSTE
        public const String Groupe = "PC Dashboard";
#else
        public const String Groupe = "Trinity";
#endif

        private readonly Boolean[] _dessinees = new Boolean[Moteur.Touches];

        public ToucheCommand()
            : base()
        {
            for (var i = 0; i < Moteur.Proposees; i++)
            {
                this.AddParameter($"t{i + 1}", $"Key {i + 1} · {Noms[Moteur.Premiere - PremierNom + i]}", Groupe);
            }
        }

        private Moteur Moteur => ((PadPlugin)this.Plugin).Moteur;

        protected override Boolean OnLoad()
        {
            ((PadPlugin)this.Plugin).Redessiner = t => this.ActionImageChanged($"t{Moteur.Place(t) - Moteur.Premiere + 1}");
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

            if (imageSize == PluginImageSize.Width116)
            {
                this.Moteur.Demande(touche); // le pad (Options+ demande d'autres tailles pour son propre affichage)
            }

            // Le pad demande 116 px : l'image déjà dessinée et envoyée par le moteur ; une autre taille (Options+) à part.
            return imageSize == PluginImageSize.Width116 && this.Moteur.Image(touche) is { } image
                ? image
                : Rendu.Dessiner(touche, this.Moteur.Scene, imageSize);
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => null;

        private static Int32 Index(String parametre) =>
            parametre is { Length: 2 or 3 } && parametre[0] == 't' && Int32.TryParse(parametre.AsSpan(1), out var n) && n is >= 1 and <= Moteur.Proposees
                ? Moteur.Disposition[Moteur.Premiere + n - 1] : -1;
    }
}

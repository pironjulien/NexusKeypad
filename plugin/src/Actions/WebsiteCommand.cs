#if POSTE
namespace Loupedeck.TrinitePlugin
{
    using System;

    // Touche du site de l'édition PC Dashboard : l'utilisateur la pose et saisit son adresse dans Options+ (aucun site par
    // défaut). Un globe dans un anneau arc-en-ciel qui tourne, dessiné par le moteur comme les autres touches ; l'appui
    // ouvre l'adresse au premier plan (sa fenêtre si elle est ouverte, sinon une fenêtre d'appli Chrome, sinon le navigateur
    // par défaut ; déjà devant, elle se ferme). Plusieurs touches du site peuvent porter chacune leur adresse.
    public class WebsiteCommand : ActionEditorCommand
    {
        private const String Adresse = "url";
        private const String Titre = "title";

        public WebsiteCommand()
            : base(DeviceType.All)
        {
            this.Name = "Website";
            this.DisplayName = "Website";
            this.Description = "Opens the web address you choose.";
            this.GroupName = ToucheCommand.Groupe;
            this.ActionEditor.AddControlEx(new ActionEditorTextbox(Adresse, "Web address", "The page this key opens")
                .SetPlaceholder("https://example.com")
                .SetRequired());
            this.ActionEditor.AddControlEx(new ActionEditorTextbox(Titre, "Window title (optional)",
                    "How its window title starts, so a second press brings it back instead of opening a new one; by default the site name")
                .SetPlaceholder("Example"));
        }

        private Moteur Moteur => ((PadPlugin)this.Plugin).Moteur;

        protected override Boolean OnLoad()
        {
            ((PadPlugin)this.Plugin).RedessinerSite = this.ActionImageChanged;
            return true;
        }

        protected override Boolean RunCommand(ActionEditorActionParameters parametres)
        {
            var site = Signet.Choisi(parametres.GetString(Adresse, ""), parametres.GetString(Titre, ""));
            if (site.Url is null)
            {
                PluginLog.Warning("touche du site sans adresse valable : rien d'ouvert");
                return false;
            }

            this.Moteur.Appuyer(Moteur.Site, site);
            return true;
        }

        // Toutes les touches du site ont la même image (le globe) : celle du moteur pour le pad (116 px), une autre taille
        // dessinée à part (Options+).
        protected override BitmapImage GetCommandImage(ActionEditorActionParameters parametres, Int32 largeur, Int32 hauteur)
        {
            if (largeur == Rendu.T)
            {
                this.Moteur.Demande(Moteur.Site);
                if (this.Moteur.Image(Moteur.Site) is { } image)
                {
                    return image;
                }
            }

            return BitmapImage.FromArray(Rendu.Png(Moteur.Site, this.Moteur.Scene, Math.Min(largeur, hauteur)));
        }

        // Le nom du site sous la touche dans Options+ ; « Website » tant qu'aucune adresse n'est saisie.
        protected override String GetCommandDisplayName(ActionEditorActionParameters parametres) =>
            Signet.Choisi(parametres.GetString(Adresse, ""), "") is { Url: not null } site ? site.Titre : this.DisplayName;
    }
}
#endif

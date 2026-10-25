// La classe du plugin porte son nom pour le service (« XxxPlugin » → plugin « Xxx ») : les éditions PC Dashboard
// (Edition=Poste) et AI Coding Agents + PC (Edition=Complete) en ont une autre ; le reste du code la nomme PadPlugin.
#if POSTE
global using PadPlugin = Loupedeck.TrinitePlugin.PcDashboardPlugin;
#elif COMPLETE
global using PadPlugin = Loupedeck.TrinitePlugin.AiAgentsPcPlugin;
#else
global using PadPlugin = Loupedeck.TrinitePlugin.TrinitePadPlugin;
#endif

namespace Loupedeck.TrinitePlugin
{
    using System;

    // Plugin Trinité : Claude, Codex et Antigravity en pixel art animé sur le MX Keypad.
#if POSTE
    public class PcDashboardPlugin : Plugin
#elif COMPLETE
    public class AiAgentsPcPlugin : Plugin
#else
    public class TrinitePadPlugin : Plugin
#endif
    {
        public override Boolean UsesApplicationApiOnly => true;

        public override Boolean HasNoApplication => true;

        public Moteur Moteur { get; private set; }

        public Action<Int32> Redessiner { get; set; } = _ => { };

        // Touche du site de l'édition PC Dashboard (commande à part, WebsiteCommand) : redessinée par elle.
        public Action RedessinerSite { get; set; } = () => { };

#if POSTE
        public PcDashboardPlugin()
#elif COMPLETE
        public AiAgentsPcPlugin()
#else
        public TrinitePadPlugin()
#endif
        {
            PluginLog.Init(this.Log);
            this.Moteur = new Moteur(t =>
            {
                if (Moteur.SiteAPart && t == Moteur.Site)
                {
                    this.RedessinerSite();
                }
                else
                {
                    this.Redessiner(t);
                }
            });
        }

        public override void Load()
        {
            PluginLog.Info("moteur démarré");
            this.Moteur.Demarrer();
        }

        // Sans cet arrêt, chaque rechargement laisserait l'ancien moteur tourner.
        public override void Unload()
        {
            PluginLog.Info("moteur arrêté");
            this.Moteur.Dispose();
        }
    }
}

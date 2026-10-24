namespace Loupedeck.TrinitePlugin
{
    using System;

    // Plugin Trinité : Claude, Codex et Antigravity en pixel art animé sur le MX Keypad.
    public class TrinitePadPlugin : Plugin
    {
        public override Boolean UsesApplicationApiOnly => true;

        public override Boolean HasNoApplication => true;

        public Moteur Moteur { get; private set; }

        public Action<Int32> Redessiner { get; set; } = _ => { };

        public TrinitePadPlugin()
        {
            PluginLog.Init(this.Log);
            this.Moteur = new Moteur(t => this.Redessiner(t));
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

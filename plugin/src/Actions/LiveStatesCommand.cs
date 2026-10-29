#if !POSTE
namespace Loupedeck.TrinitePlugin
{
    using System;

    // La même commande de connexion pour chacune des trois IA ; aucune modification sans accord.
    public class LiveStatesCommand : PluginDynamicCommand
    {
        public LiveStatesCommand()
            : base("Live states", "Enable or disable a NexusKeypad AI connection (asks first).", ToucheCommand.Groupe)
        {
            this.AddParameter("claude", "NexusKeypad · Claude", ToucheCommand.Groupe);
            this.AddParameter("codex", "NexusKeypad · Codex", ToucheCommand.Groupe);
            this.AddParameter("antigravity", "NexusKeypad · Antigravity", ToucheCommand.Groupe);
        }

        protected override void RunCommand(String actionParameter) => ConnexionPad.Basculer(actionParameter switch { "codex" => 1, "antigravity" => 2, _ => 0 });
    }
}
#endif

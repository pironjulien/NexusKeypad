#if !POSTE
namespace Loupedeck.TrinitePlugin
{
    using System;

    // Pose ou retire les états en direct de Claude et d'Antigravity (EtatsEnDirect), toujours après une question : un
    // appui ne change rien sans « Oui ». Une action à part, pas une des 18 touches : on la pose le temps de s'en servir.
    public class LiveStatesCommand : PluginDynamicCommand
    {
        public LiveStatesCommand()
            : base("Live states", "Sets up or removes live states for Claude and Antigravity (asks first).", ToucheCommand.Groupe)
        {
        }

        protected override void RunCommand(String actionParameter) => EtatsEnDirect.Basculer();
    }
}
#endif

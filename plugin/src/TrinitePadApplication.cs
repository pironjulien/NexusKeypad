namespace Loupedeck.TrinitePlugin
{
    using System;

    // Le service exige une classe d'application dans chaque plugin, même universel (sans elle : « Cannot load plugin »).
#if POSTE
    public class PcDashboardApplication : ClientApplication
#elif COMPLETE
    public class AiAgentsPcApplication : ClientApplication
#else
    public class TrinitePadApplication : ClientApplication
#endif
    {
        protected override String GetProcessName() => "";

        protected override String GetBundleName() => "";

        public override ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Unknown;
    }
}

namespace Loupedeck.TrinitePlugin
{
    using System;

    // Le service exige une classe d'application dans chaque plugin, même universel (sans elle : « Cannot load plugin »).
    public class TrinitePadApplication : ClientApplication
    {
        protected override String GetProcessName() => "";

        protected override String GetBundleName() => "";

        public override ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Unknown;
    }
}

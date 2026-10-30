// The only plugin members the isolated sources need. The pad's quota model is checked by tests\Logique; these doubles
// avoid loading the Logi API to test launches, identity and confirmation.
namespace Loupedeck.TrinitePlugin
{
    public static class Cdp
    {
        public const Int32 PortCodex = 9223;
        public const Int32 PortAntigravity = 9227;
    }

    public static class PluginLog
    {
        public static void Warning(Exception erreur, String message) => throw new Exception("Les essais ne doivent pas lancer une application.");
    }

    public sealed record Quota(Double? Cinq, Double? Semaine);
}

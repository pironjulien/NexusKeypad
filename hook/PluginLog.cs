// Journal du plugin réduit à rien pour l'exécutable du hook : EtatsEnDirect (compilé ici pour « --remove ») y écrit, et
// get.ps1 lit seulement le code de sortie.
namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.Diagnostics.CodeAnalysis;

    [SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Same signatures as the plugin's PluginLog, called by the shared files; the hook writes no log.")]
    public static class PluginLog
    {
        public static void Info(String text)
        {
        }

        public static void Warning(String text)
        {
        }

        public static void Warning(Exception ex, String text)
        {
        }

        public static void Error(Exception ex, String text) => Console.Error.WriteLine($"{text} : {ex.Message}");
    }
}

// Journal du plugin réduit à rien pour l'exécutable du hook : EtatsEnDirect (compilé ici pour « --remove ») y écrit, et
// get.ps1 lit seulement le code de sortie.
namespace Loupedeck.TrinitePlugin
{
    using System;

    public static class PluginLog
    {
        public static void Info(String text)
        {
        }

        public static void Warning(Exception ex, String text)
        {
        }

        public static void Error(Exception ex, String text) => Console.Error.WriteLine($"{text} : {ex.Message}");
    }
}

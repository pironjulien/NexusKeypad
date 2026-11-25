namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.IO;

    // A helper class that enables logging from the plugin code.
    // Logi recreates the plugin's log file (Logs\plugin_logs\<plugin>.log) on every load attempt: each service start,
    // each update, and the refused second load it tries a moment later ("already loaded"). The history before the last
    // attempt is lost, startup lines included. Every Info, Warning and Error line is therefore also appended to a log of
    // our own that survives them (JournalDurable).

    internal static class PluginLog
    {
        private static PluginLogFile? _pluginLogFile;

        public static void Init(PluginLogFile pluginLogFile)
        {
            pluginLogFile.CheckNullArgument(nameof(pluginLogFile));
            PluginLog._pluginLogFile = pluginLogFile;
            JournalDurable.Actif = true;
        }

        public static void Verbose(String text) => PluginLog._pluginLogFile?.Verbose(text);

        public static void Verbose(Exception ex, String text) => PluginLog._pluginLogFile?.Verbose(ex, text);

        public static void Info(String text)
        {
            PluginLog._pluginLogFile?.Info(text);
            JournalDurable.Ecrire("INFO", text, null);
        }

        public static void Info(Exception ex, String text)
        {
            PluginLog._pluginLogFile?.Info(ex, text);
            JournalDurable.Ecrire("INFO", text, ex);
        }

        public static void Warning(String text)
        {
            PluginLog._pluginLogFile?.Warning(text);
            JournalDurable.Ecrire("WARN", text, null);
        }

        public static void Warning(Exception ex, String text)
        {
            PluginLog._pluginLogFile?.Warning(ex, text);
            JournalDurable.Ecrire("WARN", text, ex);
        }

        public static void Error(String text)
        {
            PluginLog._pluginLogFile?.Error(text);
            JournalDurable.Ecrire("ERROR", text, null);
        }

        public static void Error(Exception ex, String text)
        {
            PluginLog._pluginLogFile?.Error(ex, text);
            JournalDurable.Ecrire("ERROR", text, ex);
        }
    }

    // Append-only log of the plugin, outside Logi's folders: %LOCALAPPDATA%\NexusKeypad\logs\<edition>.log, the edition
    // being the plugin assembly's name without "Plugin" (NexusKeypad, TrinitePad, AiAgentsPc, PcDashboard). Above 2 MB it
    // moves to <edition>.1.log (the previous one dropped): at most 4 MB on disk. Never throws: logging must not break the pad.
    internal static class JournalDurable
    {
        public const Int64 TailleMax = 2 * 1024 * 1024;

        private static readonly Object Verrou = new();

        // Only once Logi has given the plugin its log (Init): code run by the tests never writes to the user's profile.
        internal static Boolean Actif { get; set; }

        // Set by the tests; null: the default file of the running plugin.
        internal static String? Chemin { get; set; }

        // The file written now (the hook's relaunch of the Logi service appends to it).
        internal static String Fichier => Chemin ?? Defaut;

        private static String Defaut
        {
            get
            {
                var edition = (typeof(JournalDurable).Assembly.GetName().Name ?? "NexusKeypad").Replace("Plugin", "", StringComparison.Ordinal);
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(local, "NexusKeypad", "logs", edition + ".log");
            }
        }

        public static void Ecrire(String niveau, String texte, Exception? ex)
        {
            if (!Actif)
            {
                return;
            }

            try
            {
                var chemin = Chemin ?? Defaut;
                var ligne = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {Environment.CurrentManagedThreadId,3} | {niveau,-5} | {texte}"
                    + (ex is null ? "" : Environment.NewLine + ex) + Environment.NewLine;
                lock (Verrou)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(chemin)!);
                    var fichier = new FileInfo(chemin);
                    if (fichier.Exists && fichier.Length >= TailleMax)
                    {
                        File.Move(chemin, Path.ChangeExtension(chemin, ".1.log"), overwrite: true);
                    }

                    using var flux = new FileStream(chemin, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    var octets = System.Text.Encoding.UTF8.GetBytes(ligne);
                    flux.Write(octets, 0, octets.Length);
                }
            }
            catch (Exception)
            {
                // disk full, folder refused: the line stays in Logi's log only
            }
        }
    }
}

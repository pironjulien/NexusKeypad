namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    // Version du plugin chargé, pour l'appli Nexus : ~/.nexus/status/keypad.json {product, version, state, checked_at,
    // message}, écrit à chaque chargement. Les mises à jour passent par get.ps1 (NexusSystem ou l'IA) ou par Logi
    // Options+ (éditions du Marketplace) : le plugin ne se met jamais à jour lui-même.
    public static class EtatPlugin
    {
        public static String FichierEtat => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nexus", "status", "keypad.json");

        // Version du plugin installé, lue dans son paquet (metadata/LoupedeckPackage.yaml).
        public static String Version(String dossierPlugin)
        {
            try
            {
                var yaml = File.ReadAllText(Path.Combine(dossierPlugin, "metadata", "LoupedeckPackage.yaml"));
                return Regex.Match(yaml, @"(?m)^version:\s*(\S+)").Groups[1].Value;
            }
            catch (Exception)
            {
                return "";
            }
        }

        public static void Ecrire(String version, String message, String fichier = null)
        {
            try
            {
                fichier ??= FichierEtat;
                Directory.CreateDirectory(Path.GetDirectoryName(fichier));
                var json = JsonSerializer.Serialize(new
                {
                    product = "keypad",
                    version,
                    state = "idle",
                    checked_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
                    message,
                });
                var temporaire = fichier + ".tmp";
                File.WriteAllText(temporaire, json, new UTF8Encoding(false));
                File.Move(temporaire, fichier, overwrite: true);
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "état du plugin non écrit");
            }
        }
    }
}

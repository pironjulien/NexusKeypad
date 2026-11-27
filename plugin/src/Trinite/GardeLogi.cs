#if NEXUS || DEBUG
namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.Globalization;
    using System.IO;

    // Quand relancer le service Logi qui garde le pad allumé écran éteint (édition NexusKeypad, Moteur.GarderAllume) :
    // seulement quand Windows a annoncé l'écran allumé puis éteint à ce plugin. Un état deviné par l'inactivité ne compte
    // pas, ni un écran déjà éteint au chargement (le service vient alors d'être relancé). Au plus une relance par minute,
    // notée dans un fichier : la limite tenait en mémoire et repartait de zéro avec chaque service relancé (8 relances en
    // 3 min le 27/11).
    internal sealed class GardeLogi(String fichier, Func<DateTime> maintenant)
    {
        public static readonly TimeSpan Intervalle = TimeSpan.FromSeconds(60);

        // %LOCALAPPDATA%\NexusKeypad\logi-garde.txt : l'heure UTC de la dernière relance.
        public static String Defaut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusKeypad", "logi-garde.txt");

        private Boolean _vuAllume; // Windows a annoncé l'écran allumé depuis la dernière extinction vue

        // Chaque lecture de l'écran : connu = annoncé par Windows. Vrai quand le service doit être relancé maintenant.
        public Boolean Relancer(Boolean connu, Boolean eteint)
        {
            if (!connu)
            {
                return false;
            }

            if (!eteint)
            {
                this._vuAllume = true;
                return false;
            }

            if (!this._vuAllume)
            {
                return false;
            }

            this._vuAllume = false;
            var temps = maintenant();
            if (this.Derniere() is { } derniere && temps - derniere >= TimeSpan.Zero && temps - derniere < Intervalle)
            {
                PluginLog.Info("écran éteint : service Logi relancé il y a moins d'une minute, pas de nouvelle relance");
                return false;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fichier)!);
                File.WriteAllText(fichier, temps.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            }
            catch (Exception e)
            {
                PluginLog.Warning(e, "écran éteint : heure de la relance non notée");
            }

            return true;
        }

        private DateTime? Derniere()
        {
            try
            {
                return File.Exists(fichier) && DateTime.TryParse(File.ReadAllText(fichier).Trim(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var lu) ? lu : null;
            }
            catch (Exception)
            {
                return null; // illisible : comme jamais relancé
            }
        }
    }
}
#endif

// Échange des dossiers d'une mise à jour de NexusKeypad (MiseAJour du plugin) : lancé détaché par le plugin, qui est
// déchargé par le service Logi dès que son dossier change. Le dossier en place part en sauvegarde, le nouveau prend sa
// place ; un échec remet la sauvegarde. L'état final va dans ~/.nexus/status/keypad.json.
// Usage : nexuskeypad-hook swap <nouveau> <dossier du plugin> <sauvegarde> <fichier d'état> <version>
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace NexusKeypad.Hook;

public static class Echange
{
    public static int Executer(string nouveau, string cible, string ancien, string etat, string version, int attenteMs = 1500)
    {
        Thread.Sleep(attenteMs); // le plugin finit d'écrire son état et rend la main
        try
        {
            if (Directory.Exists(ancien))
            {
                Directory.Delete(ancien, true);
            }

            Reessayer(() => Directory.Move(cible, ancien));
            try
            {
                Reessayer(() => Directory.Move(nouveau, cible));
            }
            catch (Exception)
            {
                Reessayer(() => Directory.Move(ancien, cible)); // retour à l'ancienne version
                throw;
            }

            try
            {
                Directory.Delete(ancien, true);
            }
            catch (Exception)
            {
                // sauvegarde laissée : effacée au prochain échange
            }

            Ecrire(etat, version, version, "done", "updated");
            return 0;
        }
        catch (Exception ex)
        {
            Ecrire(etat, null, version, "failed", ex.Message);
            return 1;
        }
    }

    // Un fichier encore tenu un instant par le service (rechargement) : quelques essais.
    private static void Reessayer(Action action)
    {
        for (var i = 0; ; i++)
        {
            try
            {
                action();
                return;
            }
            catch (IOException) when (i < 20)
            {
                Thread.Sleep(500);
            }
            catch (UnauthorizedAccessException) when (i < 20)
            {
                Thread.Sleep(500);
            }
        }
    }

    private static void Ecrire(string fichier, string? version, string derniere, string etat, string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fichier)!);
            var json = new JsonObject
            {
                ["product"] = "keypad",
                ["version"] = version,
                ["latest"] = derniere,
                ["state"] = etat,
                ["checked_at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                ["message"] = message,
            };
            File.WriteAllText(fichier + ".tmp", json.ToJsonString(), new UTF8Encoding(false));
            File.Move(fichier + ".tmp", fichier, overwrite: true);
        }
        catch (Exception)
        {
        }
    }
}

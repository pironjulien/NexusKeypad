// Échange des dossiers d'une mise à jour de NexusKeypad (MiseAJour du plugin) : lancé détaché par le plugin, pad au repos.
// Le dossier en place part en sauvegarde, le nouveau prend sa place ; un échec remet la sauvegarde. L'état final va dans
// ~/.nexus/status/keypad.json.
// Le service Logi garde les DLL du plugin ouvertes et ne le décharge pas avant que son dossier change (27/10 : 20 essais
// refusés, UnauthorizedAccess). Le get.ps1 du paquet signé arrête le seul service de plugins par son outil officiel,
// avant tout échange, puis le relance détaché par WMI. L'agent clavier/souris n'est jamais arrêté.
// Usage : nexuskeypad-hook swap <nouveau> <dossier du plugin> <sauvegarde> <fichier d'état> <version>
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NexusKeypad.Hook;

public static partial class Echange
{
    public sealed record Logi(string Script);

    // Real plugins always use the controller from the verified package, copied beside this detached swap executable.
    // A controller fixture is accepted only for a directory outside the real plugins root (isolated tests).
    public static Logi? LogiPour(string cible)
    {
        var plugins = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Logi", "LogiPluginService", "Plugins");
        var parent = Path.GetDirectoryName(Path.GetFullPath(cible).TrimEnd('\\', '/'));
        if (string.Equals(parent, Path.GetFullPath(plugins), StringComparison.OrdinalIgnoreCase))
        {
            return new Logi(Path.Combine(AppContext.BaseDirectory, "get.ps1"));
        }

        var script = Environment.GetEnvironmentVariable("NEXUSKEYPAD_SWAP_CONTROL_SCRIPT");
        return String.IsNullOrWhiteSpace(script) ? null : new Logi(Path.GetFullPath(script));
    }

    public static int Executer(string nouveau, string cible, string ancien, string etat, string version, Logi? logi, int attenteMs = 1500)
    {
        Thread.Sleep(attenteMs); // le plugin finit d'écrire son état et rend la main
        var arrete = false;
        var resultat = 0;
        var etape = "removing the previous backup";
        try
        {
            if (logi is not null)
            {
                etape = "stopping Logi Plugin Service through its official control tool";
                Controler(logi, "stop");
                arrete = true;
            }

            etape = "removing the previous backup";
            if (Directory.Exists(ancien))
            {
                Directory.Delete(ancien, true);
            }

            etape = "moving the installed plugin aside";
            Reessayer(() => Directory.Move(cible, ancien));

            etape = "putting the new version in place";
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
        }
        catch (Exception ex)
        {
            // La version réellement en place (l'ancienne, remise), et un message lisible : le hook est compilé avec
            // UseSystemResourceKeys, ex.Message n'y est qu'une clé de ressource.
            Ecrire(etat, Version(cible), version, "failed", $"update failed while {etape}: {Raison(ex)}");
            resultat = 1;
        }
        finally
        {
            if (arrete)
            {
                try { Controler(logi!, "start"); }
                catch (Exception)
                {
                    resultat = 1;
                    Ecrire(etat, Version(cible), version, "failed", "Logi Plugin Service could not be restarted independently after the plugin update attempt");
                }
            }
        }
        return resultat;
    }

    // Version du plugin d'un dossier (metadata/LoupedeckPackage.yaml) ; null s'il n'y en a pas.
    public static string? Version(string dossier)
    {
        try
        {
            var m = VersionYaml().Match(File.ReadAllText(Path.Combine(dossier, "metadata", "LoupedeckPackage.yaml")));
            return m.Success ? m.Groups[1].Value : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string Raison(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "access denied, a file of the plugin is still in use",
        DirectoryNotFoundException => "folder not found",
        FileNotFoundException => "file not found",
        IOException => "a file of the plugin is in use or the disk refused the change",
        _ => ex.GetType().Name,
    };

    [GeneratedRegex(@"(?m)^version:\s*(\S+)")]
    private static partial Regex VersionYaml();

    private static void Controler(Logi logi, String action)
    {
        if (!File.Exists(logi.Script)) { throw new FileNotFoundException("The verified service controller is absent.", logi.Script); }
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", logi.Script, "-LogiService", action })
        {
            info.ArgumentList.Add(argument);
        }
        info.Environment.Remove("NEXUS_KEYPAD_LOGI");
        info.Environment.Remove("NEXUS_KEYPAD_SOURCE");

        using var client = Process.Start(info) ?? throw new IOException("The service controller did not start.");
        var sortie = client.StandardOutput.ReadToEndAsync();
        var erreur = client.StandardError.ReadToEndAsync();
        // The controller already bounds its command and service waits. An unknown outcome never causes a kill/replay.
        if (!client.WaitForExit(70000)) { throw new TimeoutException("The service controller did not confirm its result."); }
        if (client.ExitCode != 0) { throw new IOException("The official service controller refused the action: " + erreur.GetAwaiter().GetResult()); }
        sortie.GetAwaiter().GetResult();
    }

    // Un fichier encore tenu un instant (processus qui se termine) : quelques essais.
    private static void Reessayer(Action action)
    {
        for (var i = 0; ; i++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && i < 20)
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

// Échange des dossiers d'une mise à jour de NexusKeypad (MiseAJour du plugin) : lancé détaché par le plugin, pad au repos.
// Le dossier en place part en sauvegarde, le nouveau prend sa place ; un échec remet la sauvegarde. L'état final va dans
// ~/.nexus/status/keypad.json.
// Le service Logi garde les DLL du plugin ouvertes et ne le décharge pas avant que son dossier change (27/10 : 20 essais
// refusés, UnauthorizedAccess). Comme get.ps1 : si le dossier reste tenu, le service et l'agent Logi sont arrêtés le temps
// de l'échange, puis l'agent est relancé détaché (hors de l'objet tâche de l'appelant), même en cas d'échec.
// Usage : nexuskeypad-hook swap <nouveau> <dossier du plugin> <sauvegarde> <fichier d'état> <version>
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NexusKeypad.Hook;

public static partial class Echange
{
    // Processus à arrêter pour libérer le dossier, et ligne de commande de l'agent à relancer ensuite.
    public sealed record Logi(string[] Processus, string? Agent);

    // Le vrai service Logi, seulement pour le vrai dossier des plugins. Ailleurs (tests) : NEXUSKEYPAD_SWAP_PROCESSES
    // (noms séparés de virgules) et NEXUSKEYPAD_SWAP_AGENT (ligne de commande), sinon rien n'est arrêté.
    public static Logi? LogiPour(string cible)
    {
        var plugins = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Logi", "LogiPluginService", "Plugins");
        var parent = Path.GetDirectoryName(Path.GetFullPath(cible).TrimEnd('\\', '/'));
        if (string.Equals(parent, Path.GetFullPath(plugins), StringComparison.OrdinalIgnoreCase))
        {
            var agent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LogiOptionsPlus", "logioptionsplus_agent.exe");
            return new Logi(["LogiPluginService", "LogiPluginServiceExt", "logioptionsplus_agent"], File.Exists(agent) ? $"\"{agent}\" /minimized" : null);
        }

        var noms = Environment.GetEnvironmentVariable("NEXUSKEYPAD_SWAP_PROCESSES");
        return string.IsNullOrWhiteSpace(noms)
            ? null
            : new Logi(noms.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), Environment.GetEnvironmentVariable("NEXUSKEYPAD_SWAP_AGENT"));
    }

    public static int Executer(string nouveau, string cible, string ancien, string etat, string version, Logi? logi, int attenteMs = 1500)
    {
        Thread.Sleep(attenteMs); // le plugin finit d'écrire son état et rend la main
        var arrete = false;
        var etape = "removing the previous backup";
        try
        {
            if (Directory.Exists(ancien))
            {
                Directory.Delete(ancien, true);
            }

            etape = "moving the installed plugin aside";
            if (!Essayer(() => Directory.Move(cible, ancien), 4))
            {
                if (logi is not null)
                {
                    etape = "stopping Logi Plugin Service";
                    Arreter(logi);
                    arrete = true;
                    etape = "moving the installed plugin aside";
                }

                Reessayer(() => Directory.Move(cible, ancien));
            }

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
            return 0;
        }
        catch (Exception ex)
        {
            // La version réellement en place (l'ancienne, remise), et un message lisible : le hook est compilé avec
            // UseSystemResourceKeys, ex.Message n'y est qu'une clé de ressource.
            Ecrire(etat, Version(cible), version, "failed", $"update failed while {etape}: {Raison(ex)}");
            return 1;
        }
        finally
        {
            if (arrete && logi!.Agent is { } agent)
            {
                Lancer(agent);
            }
        }
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

    private static void Arreter(Logi logi)
    {
        foreach (var nom in logi.Processus)
        {
            foreach (var p in Process.GetProcessesByName(nom))
            {
                try
                {
                    p.Kill();
                    p.WaitForExit(5000);
                }
                catch (Exception)
                {
                    // déjà parti, ou refusé : l'échange réessaie, et un échec remet l'ancienne version
                }
                finally
                {
                    p.Dispose();
                }
            }
        }

        Thread.Sleep(1000); // le temps que Windows rende les fichiers des processus arrêtés
    }

    private static bool Essayer(Action action, int essais)
    {
        for (var i = 0; i < essais; i++)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(500);
            }
        }

        return false;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int Cb;
        public IntPtr Reserve, Bureau, Titre;
        public int X, Y, L, H, Cx, Cy, Remplissage, Drapeaux;
        public short Affichage, Cb2;
        public IntPtr Reserve2, Entree, Sortie, Erreur;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Processus, Fil;
        public int Pid, Tid;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(IntPtr application, char[] ligne, IntPtr pa, IntPtr ta, bool heriter, uint drapeaux, IntPtr env, string? dossier, ref StartupInfo si, out ProcessInformation pi);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr h);

    // Relance détachée, comme get.ps1 (Start-Logi) : hors de l'objet tâche de l'appelant si Windows l'accepte, sinon
    // seulement détachée (lancé par le plugin, l'échangeur n'est dans aucun objet tâche).
    private static void Lancer(string ligne)
    {
        var dossier = Path.GetDirectoryName(ligne.Trim().Trim('"').Split('"')[0]);
        foreach (var drapeaux in new uint[] { 0x01000000 | 0x00000200 | 0x00000008, 0x00000200 | 0x00000008 })
        {
            var si = new StartupInfo { Cb = Marshal.SizeOf<StartupInfo>() };
            if (CreateProcess(IntPtr.Zero, (ligne + "\0").ToCharArray(), IntPtr.Zero, IntPtr.Zero, false, drapeaux, IntPtr.Zero,
                    string.IsNullOrEmpty(dossier) ? null : dossier, ref si, out var pi))
            {
                CloseHandle(pi.Processus);
                CloseHandle(pi.Fil);
                return;
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

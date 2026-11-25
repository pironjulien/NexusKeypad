// Pad gardé allumé, écran du PC éteint (édition NexusKeypad) : Logi endort le pad sur l'ordre GoToSleep que l'agent
// Options+ envoie à l'extinction ; un service de plugins relancé après cet ordre ne le reçoit pas et garde le pad allumé.
// Le plugin lance ce verbe par WMI (hors du service, qu'il arrête) ; ici, en natif : arrêt coopératif par l'outil officiel
// de Logi, puis surveillance pendant une minute. L'agent relance d'habitude le service une seconde après, mais un service
// relancé peut aussi repartir de lui-même ; sans nouveau service plus de 4 s, il est démarré par l'outil, 3 fois au plus.
// Chaque issue va au journal durable du pad (le service arrêté n'écrit plus rien). L'agent clavier/souris n'est jamais
// touché.
namespace NexusKeypad.Hook;

using System.Diagnostics;
using System.Text;

public static class GarderAllume
{
    public const string Service = "LogiPluginService";
    public const string Extension = "LogiPluginServiceExt";

    // Dépendances remplaçables par les tests : attente, outil officiel (ordre → réussi), processus vivants (nom, id), horloge.
    public static int Executer(string journal, Action<TimeSpan>? pause = null, Func<string, bool>? outil = null,
        Func<IReadOnlyList<(string Nom, int Id)>>? processus = null, Func<DateTime>? maintenant = null)
    {
        pause ??= Thread.Sleep;
        outil ??= OutilOfficiel;
        processus ??= Processus;
        maintenant ??= () => DateTime.UtcNow;

        try
        {
            pause(TimeSpan.FromSeconds(3)); // l'ordre GoToSleep de l'agent est passé
            var anciens = processus().Select(p => p.Id).ToHashSet();
            if (!outil("stop"))
            {
                Ecrire(journal, "WARN ", "pad gardé allumé : arrêt du service refusé par l'outil de Logi");
                return 1;
            }

            // Un service arrêté qui finit de sortir ne compte pas ; une extension encore en vie sans son service non plus
            // (on ne démarre pas un service à côté d'elle).
            var demarrages = 0;
            DateTime? sans = null;
            var fin = maintenant() + TimeSpan.FromSeconds(60);
            while (maintenant() < fin)
            {
                var vivants = processus();
                if (vivants.Any(p => p.Nom == Service && !anciens.Contains(p.Id)))
                {
                    sans = null;
                }
                else if (vivants.Count == 0)
                {
                    sans ??= maintenant();
                    if (maintenant() - sans >= TimeSpan.FromSeconds(4))
                    {
                        if (demarrages == 3)
                        {
                            Ecrire(journal, "ERROR", "pad gardé allumé : le service Logi ne repart pas après 3 démarrages");
                            return 1;
                        }

                        demarrages++;
                        if (!outil("start"))
                        {
                            Ecrire(journal, "WARN ", $"pad gardé allumé : démarrage {demarrages} refusé par l'outil de Logi");
                        }

                        sans = null;
                    }
                }

                pause(TimeSpan.FromMilliseconds(500));
            }

            if (!processus().Any(p => p.Nom == Service && !anciens.Contains(p.Id)))
            {
                Ecrire(journal, "ERROR", "pad gardé allumé : service Logi absent après 1 min de surveillance");
                return 1;
            }

            Ecrire(journal, "INFO ", demarrages == 0
                ? "pad gardé allumé : service Logi relancé par son agent, vérifié pendant 1 min"
                : $"pad gardé allumé : service Logi démarré par l'outil ({demarrages} fois), vérifié pendant 1 min");
            return 0;
        }
        catch (Exception e)
        {
            Ecrire(journal, "ERROR", $"pad gardé allumé : {e.GetType().Name} : {e.Message}");
            return 1;
        }
    }

    private static List<(string Nom, int Id)> Processus()
    {
        var liste = new List<(string, int)>();
        foreach (var nom in new[] { Service, Extension })
        {
            foreach (var p in Process.GetProcessesByName(nom))
            {
                using (p)
                {
                    liste.Add((nom, p.Id));
                }
            }
        }

        return liste;
    }

    // L'outil officiel de Logi (stop / start), lancé par ce processus déjà détaché du service ; 30 s au plus.
    private static bool OutilOfficiel(string ordre)
    {
        var chemin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Logi", "LogiPluginService", "LogiPluginServiceTool.exe");
        if (!File.Exists(chemin))
        {
            return false;
        }

        using var p = Process.Start(new ProcessStartInfo(chemin, ordre) { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(chemin)! });
        return p is not null && p.WaitForExit(30_000) && p.ExitCode == 0;
    }

    // Même format que le journal durable du plugin (JournalDurable), en ajout partagé ; ne lève jamais.
    private static void Ecrire(string journal, string niveau, string texte)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(journal)!);
            using var flux = new FileStream(journal, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            var octets = Encoding.UTF8.GetBytes($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {Environment.CurrentManagedThreadId,3} | {niveau} | {texte}{Environment.NewLine}");
            flux.Write(octets, 0, octets.Length);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

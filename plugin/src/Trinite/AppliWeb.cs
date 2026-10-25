namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text.RegularExpressions;
    using System.Threading;

    // Applis web de la page 2 (Gmail, Agenda, Messages, site), toujours ouvertes AU PREMIER PLAN : leur fenêtre si elle
    // est ouverte ; sinon leur appli installée par Chrome (PWA), sinon une fenêtre d'appli Chrome sans onglets
    // (--app=<url>), sinon le navigateur par défaut. Leur fenêtre ouverte donne aussi les non-lus : le nombre entre
    // parenthèses de son titre (« Boîte de réception (12) - … - Gmail »).
    // Titres : mots qui reconnaissent sa fenêtre (un onglet de Chrome, titré « … - Google Chrome », n'en est pas une).
    // Raccourcis : noms de son raccourci d'appli Chrome (« … --app-id=… », menu Démarrer ou Bureau), qui retrouve l'appli
    // installée sans en figer l'identifiant. (L'id « Gmail » des anciennes applis par défaut de Chrome ouvre un onglet, pas
    // une fenêtre d'appli, 24/10 : sans raccourci, une fenêtre --app.)
    // AuDebut : le titre doit COMMENCER par un de ses mots (le site : « Exemple.org - … ») ; sinon il le contient. Le titre
    // de Gmail contient l'adresse du compte (souvent faite du nom du site) : la touche du site ramenait Gmail.
    public sealed record AppliWeb(String Url, String[] Titres, String[] Raccourcis, Boolean AuDebut = false)
    {
        public static readonly AppliWeb Gmail = new("https://mail.google.com/mail/u/0/#inbox", ["Gmail"], ["Gmail"]);
        public static readonly AppliWeb Agenda = new("https://calendar.google.com/calendar/r", ["Google Agenda", "Google Calendar"], ["Google Agenda", "Google Calendar"]);
        public static readonly AppliWeb Messages = new("https://messages.google.com/web/conversations", ["Google Messages", "Messages pour le Web", "Messages for web"],
            ["Messages", "Google Messages"]);

        public static AppliWeb Site => new(Signet.Reglage.Url, [Signet.Reglage.Titre], [Signet.Reglage.Titre], AuDebut: true);

        private static readonly String Local = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";

        // Sa fenêtre ouverte (appli installée ou fenêtre d'appli), zéro sinon.
        public IntPtr Fenetre() => Uia.Fenetres("chrome").FirstOrDefault(h => this.Reconnue(Uia.Titre(h)));

        private Boolean Reconnue(String titre) =>
            !titre.EndsWith(" - Google Chrome", StringComparison.Ordinal) && this.Titres.Any(t => this.AuDebut
                ? titre.StartsWith(t, StringComparison.OrdinalIgnoreCase)
                : titre.Contains(t, StringComparison.OrdinalIgnoreCase));

        // Non-lus dans le titre de sa fenêtre (sans nombre : 0) ; null si elle n'est pas ouverte.
        public Int32? NonLus(System.Collections.Generic.IEnumerable<String> titres) =>
            titres.FirstOrDefault(this.Reconnue) is { } titre
                ? Regex.Match(titre, @"\((\d+)\)") is { Success: true } m ? Int32.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0
                : null;

        // Au premier plan : sa fenêtre, sinon lancée puis attendue (6 s au plus) et passée devant.
        public void Ouvrir()
        {
            var fenetre = this.Fenetre();
            if (fenetre == IntPtr.Zero)
            {
                this.Lancer();
                for (var i = 0; i < 30 && (fenetre = this.Fenetre()) == IntPtr.Zero; i++)
                {
                    Thread.Sleep(200);
                }
            }

            if (fenetre != IntPtr.Zero)
            {
                Gestes.Devant(fenetre);
            }
            else
            {
                PluginLog.Warning($"{this.Url} : fenêtre pas reconnue en 6 s (laissée où elle s'est ouverte)");
            }
        }

        private void Lancer()
        {
            var chrome = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Local }
                .Select(d => Path.Combine(d, "Google", "Chrome", "Application", "chrome.exe")).FirstOrDefault(File.Exists);
            try
            {
                if (chrome is null)
                {
                    Process.Start(new ProcessStartInfo(this.Url) { UseShellExecute = true });
                }
                else if (this.Raccourci() is { } raccourci)
                {
                    Process.Start(new ProcessStartInfo(raccourci) { UseShellExecute = true });
                }
                else
                {
                    Process.Start(new ProcessStartInfo(chrome, $"--app=\"{this.Url}\"") { UseShellExecute = false });
                }
            }
            catch (Exception e)
            {
                PluginLog.Error(e, $"ouverture de {this.Url}");
            }
        }

        // Raccourci d'appli Chrome (« … --app-id=… ») à son nom, dans le menu Démarrer ou sur le Bureau.
        private String Raccourci()
        {
            var dossiers = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            };
            return dossiers.Where(Directory.Exists)
                .SelectMany(d => Directory.EnumerateFiles(d, "*.lnk", SearchOption.AllDirectories))
                .Where(f => this.Raccourcis.Any(n => String.Equals(Path.GetFileNameWithoutExtension(f), n, StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefault(f => Arguments(f).Contains("--app-id=", StringComparison.Ordinal));
        }

        // Arguments d'un raccourci Windows (WScript.Shell, présent sur tout Windows) ; vide si illisible.
        private static String Arguments(String lnk)
        {
            try
            {
                var shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                var raccourci = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [lnk]);
                return raccourci.GetType().InvokeMember("Arguments", BindingFlags.GetProperty, null, raccourci, null) as String ?? "";
            }
            catch (Exception)
            {
                return "";
            }
        }
    }
}

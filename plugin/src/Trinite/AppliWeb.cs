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
    // AuDebut : le titre doit COMMENCER par un de ses mots (le site : « JulienPiron.fr - … ») ; sinon il le contient. Le titre
    // de Gmail contient l'adresse du compte, qui peut contenir le nom du site : la touche du site ramenait Gmail (25/10).
    // Sans titre (site sans titre donné) : sa fenêtre est une fenêtre d'appli de Chrome dont la page est sous l'adresse
    // demandée (Sous), adresse relue par UI Automation (valeur de son document) à chaque appui, avant de la ramener et
    // encore juste avant de la fermer. Le titre
    // d'une page ne commence presque jamais par le nom de son hôte (« Example Domain », « GitHub - … ») : chercher l'hôte
    // dans le titre ne la trouvait jamais (6 s d'attente et une fenêtre de plus à chaque appui), et retenir « la nouvelle
    // fenêtre de Chrome » pouvait prendre une autre appli ouverte entre-temps (relectures de Codex et d'Antigravity, 25/10).
    public sealed record AppliWeb(String Url, String[] Titres, String[] Raccourcis, Boolean AuDebut = false)
    {

        // Titres stricts : « … - Gmail » (fenêtre, onglet) et le nom complet de Google Messages pour le Web ; un onglet
        // « (2) Issues · …/nexus-gmail » ou « Questions sur l'API Gmail » allumait une fausse pastille (relecture
        // d'Antigravity, 25/10).
        public static readonly AppliWeb Gmail = new("https://mail.google.com/mail/u/0/#inbox", [" - Gmail"], ["Gmail"]);
        public static readonly AppliWeb Agenda = new("https://calendar.google.com/calendar/r", ["Google Agenda", "Google Calendar"], ["Google Agenda", "Google Calendar"]);
        public static readonly AppliWeb Messages = new("https://messages.google.com/web/conversations", ["Google Messages pour le Web", "Google Messages for web"],
            ["Messages", "Google Messages"]);

        // Site d'une touche ; null sans adresse (touche du site de PC Dashboard pas encore réglée).
        public static AppliWeb Site(Signet site) => site.Url is null ? null : new(site.Url, site.Titre is null ? [] : [site.Titre], [site.Nom], AuDebut: true);

        private static readonly String Local = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";

        // Sa fenêtre ouverte (appli installée ou fenêtre d'appli), zéro sinon : par son titre, ou sans titre donné par
        // l'adresse de sa page.
        public IntPtr Fenetre() =>
            Uia.Fenetres("chrome").FirstOrDefault(h => this.Titres.Length == 0 ? this.MemeSite(h) : this.Reconnue(Uia.Titre(h)));

        // Fenêtre d'appli de Chrome (pas une fenêtre d'onglets, titrée « … - Google Chrome ») dont la page est sous l'adresse.
        private Boolean MemeSite(IntPtr h) =>
            !Uia.Titre(h).EndsWith(" - Google Chrome", StringComparison.Ordinal)
            && Uri.TryCreate(Uia.AdresseDocument(h), UriKind.Absolute, out var page)
            && Sous(page, new Uri(this.Url));

        // Une page est sous une adresse : même origine (schéma, hôte, port ; « www. » ignoré, une redirection vers lui ou
        // depuis lui reste le même site) et chemin égal à celui de l'adresse ou en dessous, au segment près. Deux applis sur
        // localhost:3000 et localhost:5173, ou deux dépôts GitHub, ne se confondent pas (contre-relecture de Codex, 25/10).
        // Une requête ou un fragment donnés dans l'adresse désignent une ressource : ils doivent être identiques
        // (« ?id=alpha » n'est pas « ?id=beta », « #/document/alpha » n'est pas « #/document/beta »).
        public static Boolean Sous(Uri page, Uri adresse)
        {
            if (!String.Equals(page.Scheme, adresse.Scheme, StringComparison.OrdinalIgnoreCase) || page.Port != adresse.Port
                || !String.Equals(SansWww(page.Host), SansWww(adresse.Host), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if ((adresse.Query.Length > 0 && page.Query != adresse.Query) || (adresse.Fragment.Length > 0 && page.Fragment != adresse.Fragment))
            {
                return false;
            }

            var cible = adresse.AbsolutePath.TrimEnd('/');
            var chemin = page.AbsolutePath;
            return cible.Length == 0 || chemin.Equals(cible, StringComparison.Ordinal) || chemin.StartsWith(cible + "/", StringComparison.Ordinal);
        }

        private static String SansWww(String hote) => hote.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? hote[4..] : hote;

        private Boolean Reconnue(String titre) =>
            !titre.EndsWith(" - Google Chrome", StringComparison.Ordinal) && this.Titres.Any(t => this.AuDebut
                ? titre.StartsWith(t, StringComparison.OrdinalIgnoreCase)
                : titre.Contains(t, StringComparison.OrdinalIgnoreCase));

        // Non-lus dans le titre de sa fenêtre (sans nombre : 0) ; null si elle n'est pas ouverte.
        public Int32? NonLus(System.Collections.Generic.IEnumerable<String> titres) =>
            titres.FirstOrDefault(this.Reconnue) is { } titre
                ? Regex.Match(titre, @"\((\d+)\)") is { Success: true } m ? Int32.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0
                : null;

        // Au premier plan : sa fenêtre, sinon lancée puis attendue (6 s au plus) et passée devant ; déjà devant, fermée.
        public void Ouvrir()
        {
            var fenetre = this.Fenetre();
            if (fenetre != IntPtr.Zero && this.Titres.Length == 0 && !this.MemeSite(fenetre))
            {
                // Identité revérifiée juste avant d'agir (sa page a pu changer depuis) : refusée, cette fenêtre n'est ni
                // fermée ni ramenée, la page demandée est ouverte.
                fenetre = IntPtr.Zero;
            }

            if (Gestes.FermerSiDevant(fenetre))
            {
                return; // déjà devant : le deuxième appui la ferme
            }

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

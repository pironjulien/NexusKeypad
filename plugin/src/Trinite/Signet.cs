namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text.Json;

    // Touche du site (page 2, en bas à droite) : l'adresse ouverte, l'image au centre et les couleurs de l'anneau qui
    // tourne, lues une fois au chargement du plugin dans ~/.nexus/trinite/site.json :
    //   { "url": "https://exemple.org", "titre": "Exemple", "image": "logo.png", "anneau": ["#0055A4", "#FFFFFF", "#EF4135"] }
    // Image : chemin absolu ou relatif au fichier (PNG ou JPEG, carré de préférence). Titre : le début du titre de sa
    // fenêtre (appli installée), pour la ramener devant ; null s'il n'est pas donné (la fenêtre est alors celle que la
    // touche a ouverte, AppliWeb). Sans réglage : un globe dans un anneau arc-en-ciel, qui ouvre la page du projet.
    public sealed record Signet(String Url, String Titre, BitmapImage Image, BitmapColor[] Anneau)
    {
        // Nom du site : le titre donné, sinon l'hôte (« exemple.org ») ; nom de son raccourci d'appli Chrome.
        public String Nom => this.Titre ?? Hote(this.Url);

        public const String Projet = "https://github.com/pironjulien/NexusKeypad";

        private static readonly String Fichier = Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE") ?? "", ".nexus", "trinite", "site.json");

        private static readonly Lazy<Signet> Lu = new(Lire);

#if POSTE || COMPLETE
        // Éditions PC Dashboard et AI Coding Agents + PC : pas de réglage du poste ni de site par défaut (Logitech refuse la
        // promotion) ; un globe, et l'adresse choisie dans Options+ pour chaque touche du site (WebsiteCommand, Signet.Choisi).
        public static Signet Reglage => Generique;
#else
        public static Signet Reglage => Lu.Value;
#endif

        public static readonly Signet Generique = new(null, null, null, null);

        // Adresse saisie dans Options+ (http ou https, « https:// » ajouté s'il manque) et début du titre de sa fenêtre
        // (vide : null) ; Generique si l'adresse est vide ou illisible.
        public static Signet Choisi(String adresse, String titre)
        {
            adresse = adresse?.Trim() ?? "";
            if (adresse.Length > 0 && !adresse.Contains("://", StringComparison.Ordinal))
            {
                adresse = "https://" + adresse;
            }

            return Uri.TryCreate(adresse, UriKind.Absolute, out var u) && u.Scheme is "https" or "http" && u.Host.Length > 0
                ? new(u.AbsoluteUri, String.IsNullOrWhiteSpace(titre) ? null : titre.Trim(), null, null)
                : Generique;
        }

        private static Signet Lire()
        {
            if (!File.Exists(Fichier))
            {
                return Defaut;
            }

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(Fichier));
                var racine = doc.RootElement;
                var url = racine.TryGetProperty("url", out var u) && Uri.TryCreate(u.GetString(), UriKind.Absolute, out var adresse)
                    && adresse.Scheme is "https" or "http" ? adresse.AbsoluteUri : Projet;
                BitmapImage image = null;
                if (racine.TryGetProperty("image", out var i) && i.GetString() is { Length: > 0 } chemin)
                {
                    chemin = Path.Combine(Path.GetDirectoryName(Fichier), Environment.ExpandEnvironmentVariables(chemin));
                    image = File.Exists(chemin) ? BitmapImage.FromArray(File.ReadAllBytes(chemin)) : null;
                }

                var anneau = racine.TryGetProperty("anneau", out var a) && a.ValueKind == JsonValueKind.Array
                    ? a.EnumerateArray().Select(c => Couleur(c.GetString())).Where(c => c is not null).Select(c => c.Value).ToArray()
                    : null;
                var titre = racine.TryGetProperty("titre", out var t) && t.GetString() is { Length: > 0 } nom ? nom : null;
                return new(url, titre, image, anneau is { Length: > 0 } ? anneau : null);
            }
            catch (Exception e)
            {
                PluginLog.Error(e, $"réglage de la touche du site ({Fichier})");
                return Defaut;
            }
        }

        private static Signet Defaut => new(Projet, null, null, null);

        private static String Hote(String url)
        {
            var hote = new Uri(url).Host;
            return hote.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? hote[4..] : hote;
        }

        // « #RRGGBB » ; null si illisible.
        private static BitmapColor? Couleur(String hex) =>
            hex is { Length: 7 } && hex[0] == '#' && Int32.TryParse(hex[1..], System.Globalization.NumberStyles.HexNumber, null, out var v)
                ? new BitmapColor((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF)
                : null;
    }
}

namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;

    // Mise à jour de l'édition complète (NexusKeypad, installée par get.ps1 dans Plugins\NexusKeypad), par le plugin lui-même :
    // l'appli Nexus du Store ne recopie jamais ses fichiers (une recopie depuis son paquet MSIX resterait dans son conteneur).
    // Une fois par jour, et sur la commande « update » du tube local : dernière version lue par la redirection de
    // github.com/…/releases/latest (pas l'API GitHub), paquet NexusKeypad_X_Y_Z.lplug4 téléchargé, contrôlé contre
    // SHA256SUMS.txt de la release, lui-même signé hors ligne (SHA256SUMS.txt.sig, ECDSA P-256 : une des clés publiques de
    // Cles ; la clé privée n'est jamais sur GitHub, un dépôt compromis ne peut donc pas pousser de mise à jour), déballé et
    // vérifié dans un dossier de travail hors du dossier des plugins. L'échange des
    // dossiers est fait par nexuskeypad-hook.exe, détaché (« swap ») : le service Logi garde les DLL du plugin ouvertes,
    // l'échangeur l'arrête donc le temps de l'échange (comme get.ps1) puis relance l'agent Logi détaché ; le plugin ne peut
    // pas finir l'échange lui-même. Un échec remet l'ancien dossier. Jamais pendant un appui :
    // le pad doit être au repos. Réglage pour la couper : %LOCALAPPDATA%\NexusKeypad\reglages.json {"miseAJourAuto": false}.
    // État pour l'appli Nexus : ~/.nexus/status/keypad.json {product, version, latest, state, checked_at, message}.
    public static class MiseAJour
    {
        public const String Depot = "https://github.com/pironjulien/NexusKeypad";
        public const String Plugin = "NexusKeypad";

        // Clés publiques (SPKI, base64) qui signent les releases (NexusSecure\projects\nexuskeypad). Plusieurs pendant un
        // changement de clé.
        public static readonly String[] Cles = ["MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEwSIk2pqKmyLcWfNgwY6uXVzS7qq6kj14BxckZIvSvE2R3tPqi4yITye4c8czlGrdEMYM1PrnIN8o3ba2APvbJQ=="];

        private static readonly String Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
#if !(MARKETPLACE || POSTE || COMPLETE)
        private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) };
        private static Int32 _enCours;
        private static Timer _quotidien;

        // Dossier du plugin installé (Plugins/NexusKeypad), donné au chargement.
        public static String DossierPlugin { get; private set; }

        // Au chargement de l'édition complète : une vérification 2 min après, puis une par jour.
        public static void Demarrer(Moteur moteur, String dossierPlugin)
        {
            DossierPlugin = dossierPlugin;
            Recharge(Version(dossierPlugin));
            _quotidien = new Timer(_ => Lancer(moteur, demande: false), null, TimeSpan.FromMinutes(2), TimeSpan.FromDays(1));
        }
#endif

        public static String FichierEtat => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nexus", "status", "keypad.json");

        public static String Travail => Path.Combine(Local, "NexusKeypad", "maj");

        // Réglage de l'utilisateur ; absent ou illisible : mise à jour automatique.
        public static Boolean Auto(String fichier = null)
        {
            try
            {
                fichier ??= Path.Combine(Local, "NexusKeypad", "reglages.json");
                using var doc = JsonDocument.Parse(File.ReadAllText(fichier));
                return !(doc.RootElement.TryGetProperty("miseAJourAuto", out var v) && v.ValueKind == JsonValueKind.False);
            }
            catch (Exception)
            {
                return true;
            }
        }

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

        // a > b, versions en nombres séparés de points.
        public static Boolean PlusRecente(String a, String b)
        {
            Int32[] N(String v) => (v ?? "").TrimStart('v').Split('.').Select(p => Int32.TryParse(p, out var n) ? n : 0).ToArray();
            var (x, y) = (N(a), N(b));
            for (var i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                var (u, w) = (i < x.Length ? x[i] : 0, i < y.Length ? y[i] : 0);
                if (u != w)
                {
                    return u > w;
                }
            }

            return false;
        }

        // Au chargement : l'état d'un échange qui vient d'installer cette version (« done ») reste lisible par l'appli Nexus
        // jusqu'à la prochaine vérification ; sinon « idle », avec la dernière version déjà connue.
        public static void Recharge(String version, String fichier = null)
        {
            fichier ??= FichierEtat;
            String etat = null, derniere = null, avant = null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(fichier));
                String Champ(String nom) => doc.RootElement.TryGetProperty(nom, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                (etat, derniere, avant) = (Champ("state"), Champ("latest"), Champ("version"));
            }
            catch (Exception)
            {
                // pas d'état précédent
            }

            if (etat == "done" && avant == version)
            {
                return;
            }

            Ecrire(version, derniere, "idle", "", fichier);
        }

        // SHA256SUMS.txt d'une release, accepté seulement avec une signature valide (SHA256SUMS.txt.sig : ECDSA P-256,
        // SHA-256, format IEEE P1363 en base64) d'une des clés. Rend son texte ; lève sinon.
        public static String Sommes(Byte[] contenu, String signature, String[] cles = null)
        {
            Byte[] octets;
            try
            {
                octets = Convert.FromBase64String((signature ?? "").Trim());
            }
            catch (FormatException)
            {
                throw new InvalidDataException("signature de SHA256SUMS.txt illisible");
            }

            foreach (var cle in cles ?? Cles)
            {
                using var ec = ECDsa.Create();
                ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(cle), out _);
                if (octets.Length > 0 && ec.VerifyData(contenu, octets, HashAlgorithmName.SHA256))
                {
                    return new UTF8Encoding(false).GetString(contenu);
                }
            }

            throw new InvalidDataException("SHA256SUMS.txt sans signature valide : release refusée");
        }

        public static void Ecrire(String version, String derniere, String etat, String message, String fichier = null)
        {
            try
            {
                fichier ??= FichierEtat;
                Directory.CreateDirectory(Path.GetDirectoryName(fichier));
                var json = JsonSerializer.Serialize(new
                {
                    product = "keypad",
                    version,
                    latest = derniere,
                    state = etat,
                    checked_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
                    message,
                });
                var temporaire = fichier + ".tmp";
                File.WriteAllText(temporaire, json, new UTF8Encoding(false));
                File.Move(temporaire, fichier, overwrite: true);
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "mise à jour : état non écrit");
            }
        }

#if !(MARKETPLACE || POSTE || COMPLETE)
        // Dernière version publiée : la redirection de releases/latest (…/releases/tag/vX.Y.Z), sans l'API GitHub.
        public static async Task<String> Derniere()
        {
            using var reponse = await Http.GetAsync(Depot + "/releases/latest");
            var lieu = reponse.Headers.Location?.ToString() ?? "";
            var m = Regex.Match(lieu, @"/releases/tag/v([0-9][^/?#]*)$");
            return m.Success ? m.Groups[1].Value : throw new InvalidDataException($"redirection inattendue : {lieu}");
        }

        public static String NomPaquet(String version) => $"{Plugin}_{version.Replace('.', '_')}.lplug4";

        // Contrôle et déballage d'un paquet dans un dossier neuf : empreinte SHA-256 conforme à SHA256SUMS.txt, plugin
        // NexusKeypad à la version attendue. Rend le dossier prêt ; lève sinon (rien n'est touché ailleurs).
        public static String Preparer(String paquet, String sommes, String version, String travail)
        {
            var nom = NomPaquet(version);
            var attendue = sommes.Split('\n')
                .Select(l => Regex.Match(l.Trim(), @"^([0-9a-f]{64})\s+\*?(.+)$"))
                .Where(m => m.Success && m.Groups[2].Value == nom)
                .Select(m => m.Groups[1].Value)
                .FirstOrDefault() ?? throw new InvalidDataException($"{nom} absent de SHA256SUMS.txt");
            using (var flux = File.OpenRead(paquet))
            {
                var reelle = Convert.ToHexString(SHA256.HashData(flux)).ToLowerInvariant();
                if (reelle != attendue)
                {
                    throw new InvalidDataException($"empreinte SHA-256 de {nom} différente de la release");
                }
            }

            var pret = Path.Combine(travail, "nouveau");
            if (Directory.Exists(pret))
            {
                Directory.Delete(pret, true);
            }

            ZipFile.ExtractToDirectory(paquet, pret);
            if (!File.Exists(Path.Combine(pret, "bin", Plugin + "Plugin.dll")) || Version(pret) != version)
            {
                throw new InvalidDataException("paquet déballé incomplet ou d'une autre version");
            }

            return pret;
        }

        // Le pad au repos : aucun appui depuis 60 s et aucun geste en file. Moteur.Repos le dit.
        public static Boolean Lancer(Moteur moteur, Boolean demande)
        {
            var dossierPlugin = DossierPlugin;
            if (dossierPlugin is null)
            {
                return false;
            }

            if (Interlocked.Exchange(ref _enCours, 1) == 1)
            {
                return false;
            }

            Task.Run(async () =>
            {
                var version = Version(dossierPlugin);
                try
                {
                    Ecrire(version, null, "checking", "");
                    var derniere = await Derniere();
                    if (!PlusRecente(derniere, version))
                    {
                        Ecrire(version, derniere, "idle", "up to date");
                        return;
                    }

                    if (!demande && !Auto())
                    {
                        Ecrire(version, derniere, "idle", "update available; automatic updates are off");
                        return;
                    }

                    Ecrire(version, derniere, "updating", "downloading");
                    Directory.CreateDirectory(Travail);
                    var paquet = Path.Combine(Travail, NomPaquet(derniere));
                    var baseRelease = $"{Depot}/releases/download/v{derniere}/";
                    await Telecharger(baseRelease + NomPaquet(derniere), paquet);
                    var sommes = Sommes(await Octets(baseRelease + "SHA256SUMS.txt"), Encoding.UTF8.GetString(await Octets(baseRelease + "SHA256SUMS.txt.sig")));
                    var pret = Preparer(paquet, sommes, derniere, Travail);
                    // Déballé et contrôlé, le paquet ne sert plus : ni lui ni ceux des mises à jour passées ne restent (ils
                    // s'empilaient dans maj\, un par version). Un fichier qui ne s'efface pas n'empêche pas la mise à jour.
                    foreach (var vieux in Directory.EnumerateFiles(Travail, "*.lplug4"))
                    {
                        try
                        {
                            File.Delete(vieux);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            PluginLog.Warning(ex, $"paquet {Path.GetFileName(vieux)} laissé dans maj");
                        }
                    }

                    // Jamais pendant un appui ni un geste : le pad au repos (10 min au plus, sinon au prochain passage).
                    var fin = DateTime.UtcNow.AddMinutes(10);
                    while (!moteur.Repos && DateTime.UtcNow < fin)
                    {
                        await Task.Delay(5000);
                    }

                    if (!moteur.Repos)
                    {
                        Ecrire(version, derniere, "idle", "update ready; the keypad was busy");
                        return;
                    }

                    // L'échange est fait par l'exécutable détaché (le service décharge le plugin dès que son dossier change).
                    var echangeur = Path.Combine(Travail, "nexuskeypad-hook.exe");
                    File.Copy(Path.Combine(pret, "bin", "nexuskeypad-hook.exe"), echangeur, overwrite: true);
                    Ecrire(version, derniere, "updating", "installing");
                    UserApplication.Start(new ProcessStartInfo(echangeur)
                    {
                        Arguments = UserApplication.ArgumentString("swap", pret, dossierPlugin, Path.Combine(Travail, "ancien"), FichierEtat, derniere),
                        WorkingDirectory = Travail,
                    }, () => { }, 0); // aucun repli enfant : l'échangeur doit survivre à l'arrêt du service Logi
                    PluginLog.Info($"mise à jour vers {derniere} lancée");
                }
                catch (Exception ex)
                {
                    PluginLog.Warning(ex, "mise à jour");
                    Ecrire(version, null, "failed", ex.Message);
                }
                finally
                {
                    Volatile.Write(ref _enCours, 0);
                }
            });
            return true;
        }

        private static async Task Telecharger(String url, String fichier)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) }; // suit les redirections (fichiers de release)
            using var flux = await client.GetStreamAsync(url);
            using var sortie = File.Create(fichier);
            await flux.CopyToAsync(sortie);
        }

        private static async Task<Byte[]> Octets(String url)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            return await client.GetByteArrayAsync(url);
        }
#endif
    }
}

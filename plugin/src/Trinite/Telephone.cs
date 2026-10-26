namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text.Json;

    // Dernière photo du téléphone par adb (USB ou Wi-Fi jumelé, comme nexus-android) : miniature sur la touche,
    // copie dans le presse-papiers (image + fichier) pour un Ctrl+V dans Claude, Codex ou Antigravity. Sans téléphone
    // (pas d'adb, aucun appareil) : pas de photo, la touche montre l'heure et la date.
    public sealed class Telephone : IDisposable
    {
        private static readonly String Local = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";
        // Hors d'AppData : les applis du Store (Codex) ne voient pas AppData\Local d'un autre programme ; le fichier collé
        // leur arrivait vide (0 octet) et la photo n'était pas jointe. Le profil (~\.nexus) leur est lisible.
        private static readonly String Maison = Environment.GetEnvironmentVariable("USERPROFILE") ?? "";
        private static readonly String Cache = Path.Combine(Maison, ".nexus", "trinite", "photos");

        // Appareil photo et captures d'écran ; ls -t trie tous les fichiers ensemble, le plus récent d'abord.
        private const String CommandeDerniere =
            "ls -t /sdcard/DCIM/Camera/*.jpg /sdcard/DCIM/Camera/*.jpeg /sdcard/DCIM/Screenshots/*.png /sdcard/DCIM/Screenshots/*.jpg /sdcard/Pictures/Screenshots/*.png 2>/dev/null | head -1";

        // Dossiers que le téléphone surveille pour prévenir d'une nouvelle image (inotifyd : écriture finie, renommage).
        private static readonly String[] DossiersTelephone = ["/sdcard/DCIM/Camera", "/sdcard/DCIM/Screenshots", "/sdcard/Pictures/Screenshots"];

        private static readonly String WifiNote = Path.Combine(Maison, ".nexus", "nexus-android", "wifi.json");

        // Formats que le presse-papiers sait décoder (System.Drawing : ni WebP ni HEIC).
        private static readonly String[] Images = [".jpg", ".jpeg", ".png"];

        private readonly Object _copie = new();
        private readonly Object _connexion = new();
        private String _adb;
        private DateTime _absentJusque = DateTime.MinValue;
        private String _reconnue; // liaison Wi-Fi reconnue à son numéro de série, sur un port autre que celui noté
        private Process _veilleur;
        private volatile Boolean _fini;

        public Photo Derniere { get; private set; }

        // Notifications en attente sur le téléphone : mails (Gmail) et messages (Google Messages, SMS et RCS ; le fichier des
        // SMS ne voit pas les RCS), résumés de groupe exclus. Un seul appel adb, filtré sur le téléphone. Null : pas de
        // téléphone joignable.
        public (Int32 Mails, Int32 Messages)? Notifications()
        {
            this._adb ??= TrouverAdb();
            var serie = this._adb is null ? null : this.Connecter();
            if (serie is null)
            {
                return null;
            }

            var (code, sortie) = Lancer(this._adb,
                $"-s {serie} shell \"dumpsys notification --noredact | grep -E 'StatusBarNotification\\(pkg=com.google.android.(gm|apps.messaging) '\"", 8000);
            if (code is not (0 or 1)) // 1 : grep sans résultat
            {
                return null;
            }

            // Vrais mails et messages seulement : catégorie email ou msg, ni résumé de groupe ni notification permanente (le
            // service « Messages pour le Web » en tient deux, comptées comme messages, 25/10). Seuls les premiers drapeaux
            // de la ligne sont ceux de la notification (sa version publique suit).
            Boolean Vraie(String l, String categorie)
            {
                var drapeaux = System.Text.RegularExpressions.Regex.Match(l, @"flags=(\S*)").Groups[1].Value;
                return l.Contains($"category={categorie}", StringComparison.Ordinal)
                    && !new[] { "GROUP_SUMMARY", "ONGOING_EVENT", "FOREGROUND_SERVICE", "NO_CLEAR" }.Any(drapeaux.Contains);
            }

            var lignes = sortie.Split('\n');
            return (lignes.Count(l => l.Contains("pkg=com.google.android.gm ", StringComparison.Ordinal) && Vraie(l, "email")),
                lignes.Count(l => l.Contains("pkg=com.google.android.apps.messaging ", StringComparison.Ordinal) && Vraie(l, "msg")));
        }

        public void Rafraichir()
        {
            this._adb ??= TrouverAdb();
            var serie = this._adb is null ? null : this.Connecter();
            if (serie is null)
            {
                this.Derniere = null; // plus de téléphone : la touche passe à l'heure
                return;
            }

            var (code, sortie) = Lancer(this._adb, $"-s {serie} shell \"{CommandeDerniere}\"", 8000);
            var chemin = sortie.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("/sdcard/", StringComparison.Ordinal));
            if (chemin is null || chemin == this.Derniere?.SurTelephone)
            {
                return;
            }

            Directory.CreateDirectory(Cache);
            // Rapatriée sous un nom provisoire puis renommée entière : un autre lecteur (le plugin pendant son
            // rechargement) ne lit jamais une photo à moitié écrite.
            // Dossier compris : Camera/x.jpg et Screenshots/x.jpg ne se confondent pas.
            var local = Path.Combine(Cache, chemin.TrimStart('/').Replace("sdcard/", "", StringComparison.Ordinal).Replace('/', '_'));
            if (!File.Exists(local))
            {
                var partiel = $"{local}.{Guid.NewGuid():N}.part";
                if (Lancer(this._adb, $"-s {serie} pull \"{chemin}\" \"{partiel}\"", 60000).Code != 0)
                {
                    File.Delete(partiel);
                    return;
                }

                try
                {
                    File.Move(partiel, local);
                }
                catch (IOException)
                {
                    File.Delete(partiel); // déjà rapatriée par l'autre lecteur
                }
            }

            // Les 5 dernières, et toujours celle de la touche (peut-être encore dans le presse-papiers).
            var garde = new[] { this.Derniere?.Fichier, this._copiee, local };
            foreach (var vieux in new DirectoryInfo(Cache).EnumerateFiles().Where(f => f.Extension != ".part" && !garde.Contains(f.FullName))
                .OrderByDescending(f => f.LastWriteTimeUtc).Skip(5))
            {
                try
                {
                    vieux.Delete();
                }
                catch (IOException)
                {
                    // encore ouverte (presse-papiers) : supprimée à la prochaine photo
                }
            }

            this.Derniere = new(chemin, local, Miniature(local));
            Mesurer(chemin);
        }

        // Délai entre la prise (heure dans le nom : 20261023_163347.jpg) et l'arrivée sur la touche, au journal.
        private static void Mesurer(String chemin)
        {
            var m = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(chemin), @"(\d{8})_(\d{6})");
            if (m.Success && DateTime.TryParseExact(m.Groups[1].Value + m.Groups[2].Value, "yyyyMMddHHmmss",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out var prise))
            {
                PluginLog.Info($"photo {Path.GetFileName(chemin)} sur la touche {(DateTime.Now - prise).TotalSeconds:0.0} s après la prise");
            }
        }

        // Le téléphone prévient dès qu'une image est écrite (inotifyd, sur une liaison adb gardée ouverte) : nouvelle()
        // est appelé aussitôt, sans attendre le tour suivant. Relancé tant que le plugin vit ; sans liaison, le tour
        // toutes les 3 s reste le filet.
        public void Veiller(Action nouvelle)
        {
            var fil = new System.Threading.Thread(() =>
            {
                while (!this._fini)
                {
                    try
                    {
                        this._adb ??= TrouverAdb();
                        var serie = this._adb is null ? null : this.Connecter();
                        var dossiers = serie is null ? [] : DossiersTelephone
                            .Where(d => Lancer(this._adb, $"-s {serie} shell ls -d {d}", 5000).Sortie.Trim() == d).ToArray();
                        if (dossiers.Length > 0)
                        {
                            this.Ecouter(serie, dossiers, nouvelle);
                        }
                    }
                    catch (Exception e)
                    {
                        PluginLog.Error(e, "veille des photos du téléphone");
                    }

                    System.Threading.Thread.Sleep(5000);
                }
            })
            { IsBackground = true, Name = "veille des photos" };
            fil.Start();
        }

        private void Ecouter(String serie, String[] dossiers, Action nouvelle)
        {
            // w : écriture terminée ; m : fichier arrivé par renommage (photo écrite à part puis déplacée).
            var cibles = String.Join(" ", dossiers.Select(d => d + ":wm"));
            using var p = Process.Start(new ProcessStartInfo(this._adb, $"-s {serie} shell inotifyd - {cibles}")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            this._veilleur = p;
            String ligne;
            while (!this._fini && (ligne = p.StandardOutput.ReadLine()) is not null)
            {
                // « w<TAB>/sdcard/DCIM/Camera<TAB>20261023_163347.jpg » ; un fichier caché est une écriture en cours.
                var nom = ligne.Split('\t').LastOrDefault() ?? "";
                if (!nom.StartsWith('.') && Images.Contains(Path.GetExtension(nom).ToLowerInvariant()))
                {
                    nouvelle();
                }
            }

            if (!p.HasExited)
            {
                p.Kill(true);
            }
        }

        // Image et fichier dans le presse-papiers (PressePapiers : API Win32, sans processus).
        private String _copiee; // fichier publié dans le presse-papiers : jamais élagué

#if !POSTE
        public Boolean Copier()
        {
            var photo = this.Derniere;
            if (photo is null || !File.Exists(photo.Fichier))
            {
                return false;
            }

            // Copie réduite toujours dans le cache (lisible des applis du Store), jamais à côté d'une image du PC de l'utilisateur.
            // L'original reste sur le téléphone.
            Directory.CreateDirectory(Cache);
            return this.CopierImage(photo.Fichier, Path.Combine(Cache, Path.GetFileNameWithoutExtension(photo.Fichier) + "-ia"), "Photo");
        }

        // Une image dans le presse-papiers pour l'IA (photo, capture d'écran), réduite selon la règle commune des images
        // (ImageIa : WebP, ≤ 1568 px, ≤ 500 Ko) en <base_>.webp et <base_>.png.
        public Boolean CopierImage(String source, String base_, String geste)
        {
            String reduite;
            try
            {
                reduite = ImageIa.Ecrire(source, base_);
            }
            catch (Exception e)
            {
                PluginLog.Error(e, $"{geste} : image réduite pour l'IA");
                return false;
            }

            if (reduite is null)
            {
                PluginLog.Warning($"{geste} : image illisible ou trop lourde même réduite, rien n'a été copié");
                return false;
            }

            this._copiee = reduite;
            lock (this._copie)
            {
                try
                {
                    // Le fichier WebP pour les IA (elles le joignent par son nom), l'image pour les autres applis (le PNG).
                    return PressePapiers.Poser(base_ + ".png", reduite);
                }
                catch (Exception e)
                {
                    PluginLog.Error(e, $"{geste} : presse-papiers");
                    return false;
                }
            }
        }
#endif

        public void Dispose()
        {
            this._fini = true;
            try
            {
                this._veilleur?.Kill(true);
            }
            catch (Exception)
            {
                // déjà sorti
            }

        }

        // Miniature carrée 116 px, recadrée au centre et redressée selon l'orientation EXIF.
        public static BitmapImage Miniature(String fichier)
        {
            try
            {
                var octets = File.ReadAllBytes(fichier);
                using var source = BitmapImage.FromArray(octets);
                var rotation = Orientation(octets) switch
                {
                    3 => BitmapRotation.Rotate180,
                    6 => BitmapRotation.Rotate90,
                    8 => BitmapRotation.Rotate270,
                    _ => BitmapRotation.None,
                };
                var tourne = rotation is BitmapRotation.Rotate90 or BitmapRotation.Rotate270;
                Double l = tourne ? source.Height : source.Width, h = tourne ? source.Width : source.Height;
                var echelle = Rendu.T / Math.Min(l, h);
                Int32 lw = (Int32)Math.Ceiling(source.Width * echelle), lh = (Int32)Math.Ceiling(source.Height * echelle);
                using var b = new BitmapBuilder(Rendu.T, Rendu.T);
                b.Clear(BitmapColor.Black);
                // Avec une rotation, le rectangle donné est celui de l'image redressée : ses côtés sont permutés.
                // 4 px de plus : l'arrondi de la rotation laissait une colonne noire sur le bord droit.
                Int32 dl = (tourne ? lh : lw) + 4, dh = (tourne ? lw : lh) + 4;
                b.DrawImage(source, (Rendu.T - dl) / 2, (Rendu.T - dh) / 2, dl, dh, rotation);
                return BitmapImage.FromArray(b.ToArray(BitmapImageFormat.Png));
            }
            catch (Exception e)
            {
                PluginLog.Error(e, "miniature de la photo");
                return null;
            }
        }

        // Orientation EXIF (balise 0x0112) d'un JPEG, 1 par défaut.
        private static Int32 Orientation(Byte[] o)
        {
            for (var i = 2; i + 10 < o.Length && i < 65536; i++)
            {
                if (o[i] != 0xFF || o[i + 1] != 0xE1 || o[i + 4] != (Byte)'E' || o[i + 5] != (Byte)'x')
                {
                    continue;
                }

                var tiff = i + 10;
                var le = o[tiff] == (Byte)'I';
                Int32 Lire16(Int32 k) => le ? o[k] | (o[k + 1] << 8) : (o[k] << 8) | o[k + 1];
                Int32 Lire32(Int32 k) => le ? o[k] | (o[k + 1] << 8) | (o[k + 2] << 16) | (o[k + 3] << 24) : (o[k] << 24) | (o[k + 1] << 16) | (o[k + 2] << 8) | o[k + 3];
                var ifd = tiff + Lire32(tiff + 4);
                var n = Lire16(ifd);
                for (var e = 0; e < n; e++)
                {
                    var entree = ifd + 2 + (e * 12);
                    if (Lire16(entree) == 0x0112)
                    {
                        return Lire16(entree + 8);
                    }
                }

                return 1;
            }

            return 1;
        }

        // Le téléphone de l'utilisateur (numéro noté par nexus-android) : USB d'abord, sinon son adresse Wi-Fi notée.
        // Absent : nouvel essai dans 2 min, pour ne pas relancer un adb connect toutes les 3 s.
        // Une seule reconnexion à la fois : le veilleur et le tour de 3 s se coupaient la liaison (disconnect de l'un).
        private String Connecter()
        {
            lock (this._connexion)
            {
                return this.ConnecterSeul();
            }
        }

        private String ConnecterSeul()
        {
            String connu = null, adresse = null;
            try
            {
                using var note = JsonDocument.Parse(File.ReadAllText(WifiNote));
                connu = note.RootElement.TryGetProperty("serialno", out var s) ? s.GetString() : null;
                adresse = note.RootElement.TryGetProperty("address", out var a) ? a.GetString() : null;
            }
            catch (Exception)
            {
                // pas encore de note : premier appareil branché
            }

            List<String[]> appareils = [];
            String Present()
            {
                appareils = Lancer(this._adb, "devices", 5000).Sortie.Split('\n').Skip(1)
                    .Select(l => l.Split('\t', StringSplitOptions.TrimEntries)).Where(l => l.Length == 2).ToList();
                var prets = appareils.Where(l => l[1] == "device").Select(l => l[0]).ToList();
                return prets.FirstOrDefault(l => l == connu) ?? prets.FirstOrDefault(l => l == adresse)
                    ?? prets.FirstOrDefault(l => connu is not null && l.Contains($"adb-{connu}", StringComparison.Ordinal))
                    // Port du débogage sans fil changé (redémarrage, Wi-Fi retrouvé) : liaison déjà ouverte sur un autre
                    // port, reconnue à son numéro de série. Sans ça, chaque tour la coupait puis la rouvrait (24/10 :
                    // notification « Débogage sans fil connecté » toutes les 3 s sur le téléphone).
                    // Liaison déjà reconnue retenue : un seul getprop par nouveau port, pas un par tour.
                    ?? prets.FirstOrDefault(l => connu is not null && l == this._reconnue)
                    ?? (this._reconnue = prets.FirstOrDefault(l => connu is not null && l.Contains(':', StringComparison.Ordinal)
                        && Lancer(this._adb, $"-s {l} shell getprop ro.serialno", 5000).Sortie.Trim() == connu))
                    ?? (connu is null ? prets.FirstOrDefault() : null);
            }

            var serie = Present();
            if (serie is not null || DateTime.UtcNow < this._absentJusque)
            {
                return serie;
            }

            // Liaison Wi-Fi endormie (« offline », vu sur un téléphone après une veille) : la relancer d'abord.
            if (appareils.Any(l => l[1] == "offline"))
            {
                Lancer(this._adb, "reconnect offline", 10000);
                System.Threading.Thread.Sleep(1500);
                serie = Present();
            }

            // Adresse notée, puis annonce mDNS du téléphone (son nom porte son numéro de série) : après un redémarrage,
            // le port du débogage sans fil change et l'adresse notée ne répond plus.
            var candidates = new List<String>();
            if (adresse is not null)
            {
                candidates.Add(adresse);
            }

            if (connu is not null)
            {
                candidates.AddRange(Lancer(this._adb, "mdns services", 10000).Sortie.Split('\n')
                    .Where(l => l.Contains($"adb-{connu}", StringComparison.Ordinal) && l.Contains("_adb-tls-connect", StringComparison.Ordinal))
                    .Select(l => System.Text.RegularExpressions.Regex.Match(l, @"(\d+\.\d+\.\d+\.\d+:\d+)").Value)
                    .Where(a => a.Length > 0 && a != adresse));
            }

            foreach (var candidate in candidates.TakeWhile(_ => serie is null))
            {
                if (appareils.Any(l => l[0] == candidate && l[1] == "device"))
                {
                    continue; // liaison vivante (un autre appareil) : jamais coupée
                }

                Lancer(this._adb, $"disconnect {candidate}", 5000); // une liaison morte sur cette adresse bloquerait connect
                Lancer(this._adb, $"connect {candidate}", 8000);
                serie = Present() ?? (appareils.Any(l => l[0] == candidate && l[1] == "device") ? candidate : null);
            }

            if (serie is null)
            {
                this._absentJusque = DateTime.UtcNow.AddMinutes(2);
            }

            return serie;
        }

        // Même adb que nexus-android (celui du PATH, sinon Platform Tools) : deux versions d'adb se coupent leur serveur.
        private static String TrouverAdb()
        {
            var chemin = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                .Select(d => Path.Combine(d.Trim(), "adb.exe")).FirstOrDefault(File.Exists);
            var paquets = Path.Combine(Local, "Microsoft", "WinGet", "Packages");
            return chemin ?? (Directory.Exists(paquets)
                ? Directory.EnumerateDirectories(paquets, "Google.PlatformTools_*").Select(d => Path.Combine(d, "platform-tools", "adb.exe")).FirstOrDefault(File.Exists)
                : null);
        }

        private static (Int32 Code, String Sortie) Lancer(String exe, String arguments, Int32 delai)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo(exe, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                var sortie = p.StandardOutput.ReadToEndAsync();
                _ = p.StandardError.ReadToEndAsync(); // lue et jetée : un tube d'erreurs plein bloquerait adb
                if (!p.WaitForExit(delai))
                {
                    p.Kill(true);
                    return (-1, "");
                }

                return (p.ExitCode, sortie.GetAwaiter().GetResult());
            }
            catch (Exception)
            {
                return (-1, "");
            }
        }
    }
}

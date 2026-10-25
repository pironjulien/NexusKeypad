namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    // Ce que voit une touche à un instant donné.
    public sealed class Scene
    {
        public Instantane Instantane { get; init; }
        public Double Temps { get; init; }
        public Double[] Appuis { get; init; }
        public Double PhotoCopiee { get; init; }
        public Double CaptureCollee { get; init; }

        // Heure du poste à cet instant (horloge de la touche Photo sans téléphone, Agenda) : figée par scène, une même
        // scène donne les mêmes pixels.
        public DateTime Maintenant { get; init; } = DateTime.Now;

        // Page 2 : état du poste (null tant que la page n'a pas été affichée) ; dernier appui sur la touche des mises à jour.
        public Poste Poste { get; init; }
        public Double MiseAJour { get; init; } = -99;
        public Double MiseAJourFinie { get; init; } = -99;
        public Boolean MiseAJourReussie { get; init; } = true; // les deux pages lancées ou à jour

        // Site dessiné sur sa touche ; null : le réglage du poste (Signet.Reglage).
        public Signet Site { get; init; }

        public Boolean Appui(Int32 touche) => this.Temps - this.Appuis[touche] < 0.2;
    }

    // Lit l'état chaque seconde (le téléphone toutes les 3 s, et dès qu'il prévient) et redessine les touches animées
    // jusqu'à 8 fois par seconde, en n'envoyant au pad que les images qui ont changé.
    public sealed class Moteur : IDisposable
    {
        public const Int32 Capture = 6;
        public const Int32 Photo = 7;
        public const Int32 Dictee = 8;

        // Page 2 : le poste.
        public const Int32 VolumeMoins = 9;
        public const Int32 Muet = 10;
        public const Int32 VolumePlus = 11;
        public const Int32 Gmail = 12;
        public const Int32 Agenda = 13;
        public const Int32 Messages = 14;
        public const Int32 Sante = 15;
        public const Int32 MisesAJour = 16;
        public const Int32 Site = 17;
        public const Int32 Touches = 18;

        // Touches proposées dans Options+ (« Key 1 » à « Key N ») : à partir de la place Premiere de Disposition. SiteAPart :
        // la touche du site est une commande à part (WebsiteCommand), avec l'adresse choisie par l'utilisateur.
#if MARKETPLACE
        // Édition du Logi Marketplace (Edition=Marketplace) : la Trinité seule, page 1. La page 2 (le poste) n'a pas de
        // lien avec les agents : un plugin du Marketplace ne doit porter que sa fonction principale.
        public const Int32 Premiere = 0;
        public const Int32 Proposees = 9;
        public const Boolean SiteAPart = false;
#elif POSTE
        // Édition PC Dashboard (Edition=Poste), second plugin du Marketplace : la page 2 seule, ses 8 touches, plus le site
        // à part (aucun site par défaut : Logitech refuse la promotion). Ni agents, ni photo du téléphone, ni tube d'essais.
        public const Int32 Premiere = 9;
        public const Int32 Proposees = 8;
        public const Boolean SiteAPart = true;
#else
        // Version complète (GitHub, et Edition=Complete : AI Coding Agents + PC, troisième plugin du Marketplace).
        public const Int32 Premiere = 0;
        public const Int32 Proposees = Touches;
        public const Boolean SiteAPart = false;
#endif

        // Touche affichée à chaque place du pad (de gauche à droite, de haut en bas ; page 1 puis page 2) : Claude au
        // milieu, entre Codex à gauche et Antigravity à droite, qu'il fait travailler par le relais ; ses quotas dessous.
        public static readonly Int32[] Disposition = [1, 0, 2, 4, 3, 5, Capture, Photo, Dictee, 9, 10, 11, 12, 13, 14, 15, 16, 17];

        public static Int32 Place(Int32 touche) => Array.IndexOf(Disposition, touche);

        private readonly Observateur _observateur = new();
        private readonly Stopwatch _horloge = Stopwatch.StartNew();
        private readonly Double[] _appuis = Enumerable.Repeat(-9.0, Touches).ToArray();
        private volatile Int32 _page = Premiere < 9 ? 1 : 2; // page que le pad affiche : celle de sa dernière demande d'image
        private Double _demande = -99; // dernière demande d'image du pad (édition PC Dashboard : rien n'est lu sans elle)
        private Poste _poste;
        private volatile Tuple<Int32, Int32> _notifications; // mails, messages : objet entier, jamais lu à moitié écrit
        private Double _notificationsLues = -99;
        private Int32 _notificationsEnCours;
        private Double _miseAJour = -99; // dernier appui sur la touche des mises à jour (Windows Update et Store)
        private Double _miseAJourFinie = -99; // fin de ce qu'il a lancé
        private Boolean _miseAJourReussie = true;
        private Task _miseAJourTache = Task.CompletedTask;
        private readonly CancellationTokenSource _arretMiseAJour = new(); // annule une mise à jour en cours à l'arrêt
        private volatile String[] _titres = []; // titres des fenêtres de Chrome (non-lus de Gmail et Messages)
        private Double _titresLus = -99;
        private Int32 _titresEnCours;
        private volatile Tuple<Int32, DateTime> _gmailNexusBrowser; // non-lus Gmail donnés par NexusBrowser et leur date

        // Pris seulement s'il a moins de 3 min AU MOMENT de s'en servir (une relecture bloquée ne garde pas un vieux nombre).
        private static Int32? NonPerime(Tuple<Int32, DateTime> compte) =>
            compte is not null && DateTime.UtcNow - compte.Item2 < TimeSpan.FromMinutes(3) ? compte.Item1 : null;

        // ~/.nexus/nexus-browser/compteurs.json, écrit chaque minute par NexusBrowser quand sa case est cochée : une entrée
        // par profil Chrome (« sources »), la somme des comptes de moins de 3 min ; null sinon (NexusBrowser absent, case
        // décochée, Chrome fermé, session Gmail fermée). L'ancien format (un seul « gmail ») est encore lu.
        private static Tuple<Int32, DateTime> GmailNexusBrowser()
        {
            try
            {
                var fichier = Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE") ?? "", ".nexus", "nexus-browser", "compteurs.json");
                if (!File.Exists(fichier))
                {
                    return null;
                }

                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(fichier));
                var entrees = doc.RootElement.TryGetProperty("sources", out var sources) && sources.ValueKind == System.Text.Json.JsonValueKind.Object
                    ? sources.EnumerateObject().Select(p => p.Value).ToList()
                    : [doc.RootElement];
                // Somme datée par sa source la PLUS ANCIENNE : elle périme quand la première de ses sources périme (datée par
                // la plus récente, un vieux compte restait compté jusqu'à 6 min, relecture de Codex, 25/10).
                Int32? somme = null;
                var plusAncienne = DateTime.MaxValue;
                foreach (var e in entrees)
                {
                    if (!e.TryGetProperty("at", out var at) || !DateTime.TryParse(at.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AdjustToUniversal, out var quand) || DateTime.UtcNow - quand > TimeSpan.FromMinutes(3))
                    {
                        continue;
                    }

                    if (e.TryGetProperty("gmail", out var g) && g.ValueKind == System.Text.Json.JsonValueKind.Number && g.TryGetInt32(out var n) && n >= 0)
                    {
                        somme = (somme ?? 0) + n;
                        plusAncienne = quand < plusAncienne ? quand : plusAncienne;
                    }
                }

                return somme is Int32 total ? Tuple.Create(total, plusAncienne) : null;
            }
            catch (Exception)
            {
                return null; // en cours d'écriture : relu dans 3 s
            }
        }
        private readonly Action<Int32> _redessiner;
        private readonly Object _envoi = new();
        private readonly BitmapImage[] _images = new BitmapImage[Touches];
        private readonly Byte[][] _pixels = new Byte[Touches][];
        private Timer _lecture;
        private Timer _animation;
#if POSTE
        private readonly Timer _telephone = null; // pas de photo du téléphone dans l'édition PC Dashboard
#else
        private Timer _telephone;
#endif
        private Instantane _instantane = Instantane.Vide;
        private Double _photoCopiee = -9;
        private String _resume = ""; // reste vide dans l'édition PC Dashboard (aucun agent lu)
        private Int64 _tic;
        private Int32 _telephoneOccupe;
        private Int32 _telephoneEncore;
        private Int32 _lectureOccupee;
        private Int32 _animationOccupee;
        private Pilote _pilote;
        private readonly System.Collections.Concurrent.BlockingCollection<(Double Quand, Int32 Touche, Action Geste, TaskCompletionSource<String> Issue)> _gestes = [];
        private Thread _filGestes;
        private volatile Boolean _arret;
        private Double _captureCollee = -9;
        private Int32 _derniereIa = -1; // dernière IA vue au premier plan : la capture y va quand aucune IA n'est devant
        private Double _teteRatee = -9; // instant où une tête n'a pas amené son appli devant en 3 s
        private readonly String[] _ouverte = new String[3]; // dernière conversation ouverte par chaque tête
        private Int32 _appuiFenetre; // numéro du dernier appui qui amène une fenêtre devant (têtes, page 2)

        public Moteur(Action<Int32> redessiner) => this._redessiner = redessiner;

        // Dernière image envoyée d'une touche (116 px), que le service redemande juste après l'avis de changement.
        public BitmapImage Image(Int32 touche) => this._images[touche];

        // Le pad demande l'image d'une touche (116 px) : sa page est celle qu'il affiche. Le service ne demande que les
        // images des touches affichées, à chaque changement de page et après chaque avis : la page cachée n'est ni relue
        // ni dessinée, et la page affichée l'est, même quand ses images ne changent pas (l'ancienne règle, fondée sur les
        // avis de changement, laissait une page cachée « affichée » tant que son image restait identique, relecture de
        // Codex, 25/10).
        public void Demande(Int32 touche)
        {
            this._page = Place(touche) < 9 ? 1 : 2;
            this._demande = this._horloge.Elapsed.TotalSeconds;
        }

        // Relevé forcé du quota d'Antigravity (pilote) : rend ce qui a été lu.
        public String ReleverAntigravity()
        {
            var q = this._observateur.ReleverAntigravity();
            return q is null ? "rien lu (fenêtre d'Antigravity fermée, ou autre dialogue ouvert)" : $"5 h {q.Cinq * 100:0} % utilisés, semaine {q.Semaine * 100:0} % utilisés";
        }

        // Résumé de la dernière lecture (journal et pilote).
        public String Resume => this._resume;

        // Les 9 touches d'une page telles qu'envoyées au pad, en grille 3 × 3 (PNG) : ce que l'utilisateur voit, pour les
        // essais. Page 2 cachée : son poste est relu et ses touches dessinées à neuf (rien d'envoyé au pad).
        public Byte[] Planche(Int32 page = 1)
        {
            if (page == 2)
            {
                this._poste = this.LirePoste();
            }

            const Int32 Ecart = 12;
            using var b = new BitmapBuilder((3 * Rendu.T) + (4 * Ecart), (3 * Rendu.T) + (4 * Ecart));
            b.Clear(new BitmapColor(200, 198, 192));
            for (var t = 0; t < 9; t++)
            {
                var touche = Disposition[((page - 1) * 9) + t];
                var image = page == 2 ? Rendu.Image(touche, this.Scene).Image : this._images[touche] ?? Rendu.Image(touche, this.Scene).Image;
                b.DrawImage(image, Ecart + (t % 3 * (Rendu.T + Ecart)), Ecart + (t / 3 * (Rendu.T + Ecart)), Rendu.T, Rendu.T, BitmapRotation.None);
            }

            return b.ToArray(BitmapImageFormat.Png);
        }

        // Dessine la touche et ne prévient le service que si l'image a changé : chaque image envoyée au pad lui coûte
        // bien plus cher que son dessin (beaucoup d'images animées sont identiques d'un tour à l'autre).
        private void Envoyer(Int32 touche)
        {
            lock (this._envoi)
            {
                var (image, pixels) = Rendu.Image(touche, this.Scene);
                if (this._pixels[touche] is { } avant && avant.AsSpan().SequenceEqual(pixels))
                {
                    return;
                }

                this._pixels[touche] = pixels;
                this._images[touche] = image;
            }

            this._redessiner(touche);
        }

        public void Demarrer()
        {
            // Les gestes passent l'un après l'autre, sous le verrou commun aux interfaces ouvertes par les relevés ; un geste
            // en attente depuis plus de 5 s (la file était bloquée) est abandonné plutôt que lancé en retard.
            this._filGestes = new Thread(() =>
            {
                foreach (var (quand, touche, geste, issue) in this._gestes.GetConsumingEnumerable())
                {
                    if (this._arret)
                    {
                        issue.TrySetResult("abandonné : plugin en arrêt");
                        break;
                    }

                    if (this._horloge.Elapsed.TotalSeconds - quand > 5)
                    {
                        this.Abandonner(touche);
                        issue.TrySetResult("abandonné : attente de plus de 5 s");
                        continue;
                    }

                    lock (Interface.Verrou)
                    {
                        // Revalidé sous le verrou : un relevé a pu le tenir pendant l'arrêt du plugin ou plus de 5 s.
                        if (this._arret || this._horloge.Elapsed.TotalSeconds - quand > 5)
                        {
                            this.Abandonner(touche);
                            issue.TrySetResult("abandonné : attente de plus de 5 s");
                            continue;
                        }

                        // Pressé avant qu'une tête n'échoue à mettre son appli devant : il viserait l'appli d'avant.
                        if (quand < this._teteRatee)
                        {
                            PluginLog.Warning("geste abandonné : l'appli demandée juste avant n'est pas venue devant");
                            issue.TrySetResult("abandonné : tête précédente ratée");
                            continue;
                        }

                        try
                        {
                            geste();
                        }
                        finally
                        {
                            this._observateur.NoterGeste(); // 10 s sans relevé à interface, comptées après le geste
                            issue.TrySetResult("fait"); // refus éventuel du geste : au journal
                        }
                    }
                }
            })
            { IsBackground = true, Name = "gestes du pad" };
            this._filGestes.Start();
            this._pilote = new Pilote(this);
            this._pilote.Demarrer();
            this._lecture = new Timer(_ => this.Lire(), null, 0, 1000);
            this._animation = new Timer(_ => this.Animer(), null, 500, 125);
#if !POSTE
            // Photo du téléphone (page 1) : l'édition PC Dashboard ne lit du téléphone que ses notifications.
            this._telephone = new Timer(_ => this.Telephone(), null, 1000, 3000);
            this._observateur.Telephone.Veiller(() => Task.Run(this.Telephone));
#endif
        }

        // Photo toujours la plus récente (sans attendre la lecture suivante).
        public Scene Scene => new()
        {
            Instantane = this._instantane with { Photo = this._observateur.Telephone.Derniere },
            Temps = this._horloge.Elapsed.TotalSeconds,
            Appuis = this._appuis,
            PhotoCopiee = this._photoCopiee,
            CaptureCollee = this._captureCollee,
            Poste = this._poste,
            MiseAJour = this._miseAJour,
            MiseAJourFinie = this._miseAJourFinie,
            MiseAJourReussie = this._miseAJourReussie,
        };

        // Un appui : action hors du fil du service (ouvrir une appli ou taper prend du temps).
        // Rend l'issue du geste (« fait », ou la raison de l'abandon), une fois passé dans la file : le pilote l'attend.
        // site : adresse de la touche du site quand elle est une commande à part (WebsiteCommand) ; sinon son réglage.
        public Task<String> Appuyer(Int32 touche, Signet site = null)
        {
            var scene = this.Scene;
            this._appuis[touche] = scene.Temps;
            this._observateur.NoterGeste();
            this.Envoyer(touche);

            // Une touche qui amène une fenêtre devant prend un numéro dès l'appui : une descente n'est valable que tant
            // qu'aucun appui de ce genre ne l'a suivie. Celle de Claude défile par UI Automation, ce qui le ramenait
            // devant (tête 1 puis tête 3 : Claude revenait 1 à 2 s après, 25/10). Le numéro est pris à l'appui, pas au
            // lancement de la descente : un geste encore en cours ne la revalide plus (relecture de Codex, 25/10).
            var appui = touche < Capture || touche >= Gmail
                ? Interlocked.Increment(ref this._appuiFenetre)
                : Volatile.Read(ref this._appuiFenetre);

            // Les gestes passent l'un après l'autre, dans l'ordre des appuis : deux appuis rapides (tête puis Dictée,
            // Dictée deux fois) ne se chevauchent plus.
            var issue = new TaskCompletionSource<String>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (this._arret || this._gestes.IsAddingCompleted)
            {
                return Task.FromResult("ignoré : plugin en arrêt");
            }

            if (!this._gestes.TryAdd((scene.Temps, touche, () =>
            {
                try
                {
                    switch (touche)
                    {
                        case Capture:
                            // L'écran tel que l'utilisateur le voit (capturé avant de changer d'appli).
                            this.CollerDansIa("Capture", () => Ecran.Capturer() is { } capture
                                && this._observateur.Telephone.CopierImage(capture, Path.ChangeExtension(capture, null) + "-ia", "Capture"),
                                () => this._captureCollee = this._horloge.Elapsed.TotalSeconds);
                            break;
                        case Photo when this._observateur.Telephone.Derniere is null:
                            // Sans téléphone, la touche est une horloge : notifications et calendrier de Windows (Win+N).
                            Gestes.Lancer("ms-actioncenter:");
                            break;
                        case Photo:
                            this.CollerDansIa("Photo", this._observateur.Telephone.Copier, () => this._photoCopiee = this._horloge.Elapsed.TotalSeconds);
                            break;
                        case Dictee:
                            if (Gestes.Dicter() is var ia and >= 0 and < 3)
                            {
                                this._observateur.SuivreDictee(ia); // touche rouge seulement pour une dictée lancée, dans son appli
                            }

                            break;
                        case < 3:
                            // Ligne du haut : la conversation qui clignote, sinon l'appli de l'IA ; appli déjà devant, la
                            // conversation suivante (urgentes d'abord, puis de la plus récente à la plus ancienne).
                            var c = this.Cible(touche, scene.Instantane);
                            if (c is not null)
                            {
                                // Vue seulement si elle s'est vraiment ouverte ; sinon l'alerte reste, et les gestes déjà
                                // pressés (Photo, Capture) ne partent pas dans la conversation d'avant.
                                if (!Gestes.Ouvrir(touche, c))
                                {
                                    this._teteRatee = this._horloge.Elapsed.TotalSeconds;
                                    PluginLog.Warning($"tête {touche + 1} : la conversation « {c.Titre} » ne s'est pas ouverte");
                                }
                                else if (this.Devant(touche))
                                {
                                    this._observateur.MarquerVu(c.Agent, c.Id); // ouverte et devant : vue
                                    this._ouverte[touche] = c.Id;
                                    this.Descendre(touche, appui);
                                }
                            }
                            else
                            {
                                Gestes.Premier(touche);
                                if (this.Devant(touche))
                                {
                                    this.Descendre(touche, appui); // appli ramenée devant : sa conversation aussi, jusqu'au bout
                                }
                            }

                            break;
                        case >= VolumeMoins:
                            this.Page2(touche, site);
                            break;
                        default:
                            // Ligne des jauges : le détail des quotas de l'IA ; appli pas encore prête, elle est seulement
                            // lancée ou ramenée (un appui rejoué plus tard pouvait reprendre le premier plan après un geste
                            // plus récent) : l'appui suivant ouvre le détail.
                            if (!Gestes.Utilisation(touche % 3))
                            {
                                PluginLog.Info($"Utilisation : appli de la touche {touche + 1} pas encore prête, seulement lancée");
                            }

                            break;
                    }
                }
                catch (Exception e)
                {
                    PluginLog.Error(e, $"touche {touche + 1}");
                    if (touche < Capture)
                    {
                        this._teteRatee = this._horloge.Elapsed.TotalSeconds; // tête en échec : la suite viserait l'appli d'avant
                    }
                }
            }, issue)))
            {
                PluginLog.Warning($"touche {touche + 1} ignorée : plugin en cours d'arrêt");
                return Task.FromResult("ignoré : plugin en arrêt");
            }

            return issue.Task;
        }

        // Une lecture à la fois : une lecture lente (page Models d'Antigravity, port muet) ne doit ni se doubler ni écraser une plus récente.
        private void Lire()
        {
            if (this._arret || Interlocked.Exchange(ref this._lectureOccupee, 1) == 1)
            {
                return;
            }

            try
            {
                var avant = this._instantane;
#if POSTE
                // Édition PC Dashboard : ni agents ni quotas à lire, seulement l'écran éteint.
                this._instantane = avant with { Eteint = this._observateur.Eteint };
#else
                this._instantane = this._observateur.Lire();
                if (this._instantane.Devant >= 0)
                {
                    this._derniereIa = this._instantane.Devant;
                }

                var resume = String.Join(" | ", Enumerable.Range(0, 3).Select(i =>
                    $"{Observateur.Agents[i]} {this._instantane.Ouvertes[i]} conv. : {this._instantane.Premiere(i)?.Titre ?? "—"}={this._instantane.Premiere(i)?.Etat}{(this._instantane.Premiere(i)?.Alerte == true ? "·alerte" : "")}" +
                    $" ; quota 5 h {this._instantane.Quotas[i]?.Cinq * 100:0} % semaine {this._instantane.Quotas[i]?.Semaine * 100:0} %"));
                if (resume != this._resume)
                {
                    this._resume = resume;
                    PluginLog.Info($"conversations : {resume}");
                }
#endif
                if (avant.Eteint != this._instantane.Eteint)
                {
                    PluginLog.Info(this._instantane.Eteint ? "écran éteint : le pad n'est plus dessiné" : "écran rallumé : le pad est redessiné");
                    this.Tout();
                }
            }
            catch (Exception e)
            {
                PluginLog.Error(e, "lecture de l'état");
            }
            finally
            {
                Volatile.Write(ref this._lectureOccupee, 0);
            }
        }

        // Une lecture à la fois : le rapatriement d'une photo par le Wi-Fi peut dépasser les 3 s entre deux tours. Une
        // demande arrivée pendant une lecture (le téléphone vient d'écrire une image) en relance une juste après.
        private void Telephone()
        {
            if (this._arret)
            {
                return;
            }

            Volatile.Write(ref this._telephoneEncore, 1);
            // Après avoir rendu la main, une demande arrivée juste avant est reprise : sinon elle attendait le tour suivant.
            while (Volatile.Read(ref this._telephoneEncore) == 1 && Interlocked.Exchange(ref this._telephoneOccupe, 1) == 0)
            {
                try
                {
                    while (Interlocked.Exchange(ref this._telephoneEncore, 0) == 1)
                    {
                        var avant = this._observateur.Telephone.Derniere;
                        this._observateur.Telephone.Rafraichir();
                        if (!ReferenceEquals(avant, this._observateur.Telephone.Derniere))
                        {
                            this.Envoyer(Photo);
                        }
                    }
                }
                catch (Exception e)
                {
                    PluginLog.Error(e, "photo du téléphone");
                }
                finally
                {
                    Volatile.Write(ref this._telephoneOccupe, 0);
                }
            }
        }

        private void Animer()
        {
            if (this._arret || Interlocked.Exchange(ref this._animationOccupee, 1) == 1)
            {
                return; // le tour précédent dessine encore : celui-ci est sauté
            }

            try
            {
                this._tic++;
                if (this._instantane.Eteint)
                {
                    return; // écran éteint, pad éteint avec lui : rien à dessiner
                }

                // Seule la page affichée est relue et dessinée. Page 1 : têtes qui travaillent ou appellent l'utilisateur 8 fois
                // par seconde, le reste 4 fois (sauf la photo ; sans téléphone, la touche est une horloge animée). Page 2 :
                // le poste relu 8 fois par seconde (quelques appels locaux), le son à chaque tour, le reste 4 fois. Seules les
                // images qui ont changé partent au pad, chacune coûte cher au service Logi.
                if (this._page == 2)
                {
                    // Édition PC Dashboard : ses touches peuvent n'être sur aucune page affichée du pad, et le service ne dit
                    // pas quelle page il montre. Une touche affichée dont l'image ne change pas (Son coupé, volume stable,
                    // silence) n'est plus redemandée : l'absence de demande ne prouve pas que la page est cachée (relecture
                    // de Codex, 25/10). Sans demande depuis 3 s, seul un relevé léger continue, une fois par seconde (son,
                    // processeur, mémoire ; ni Chrome ni le téléphone) : une image qui change part au service, qui la
                    // redemande si sa page est affichée, et tout reprend.
                    var sansDemande = Premiere >= 9 && this._horloge.Elapsed.TotalSeconds - this._demande > 3;
                    if (sansDemande && this._tic % 8 != 0)
                    {
                        return;
                    }

                    if (sansDemande)
                    {
                        this._poste = Releve.Lire() with { Mails = this._poste?.Mails, Messages = this._poste?.Messages };
                    }
                    else
                    {
                        this.LireNotifications();
                        this._poste = this.LirePoste();
                    }

                    for (var t = VolumeMoins; t < Touches; t++)
                    {
                        if (t == Muet || this._tic % 2 == 0)
                        {
                            this.Envoyer(t);
                        }
                    }

                    return;
                }

                var i = this._instantane;
                var horloge = this._observateur.Telephone.Derniere is null;
                for (var t = 0; t < 9; t++)
                {
                    var vive = t < 3 && (i.EtatIA(t) is Etat.Travaille or Etat.Attend || i.Premiere(t)?.Alerte == true || i.PourClaude(1) || i.PourClaude(2));
                    if ((t != Photo || horloge) && (vive || this._tic % 2 == 0))
                    {
                        this.Envoyer(t);
                    }
                }

                // La photo ne bouge pas : redessinée chaque seconde, ou juste après un appui (coche).
                var temps = this._horloge.Elapsed.TotalSeconds;
                if (this._tic % 8 == 0 || temps - this._photoCopiee < 2 || temps - this._appuis[Photo] < 0.4)
                {
                    this.Envoyer(Photo);
                }
            }
            catch (Exception e)
            {
                PluginLog.Error(e, "animation");
            }
            finally
            {
                Volatile.Write(ref this._animationOccupee, 0);
            }
        }

        // Écran rallumé : les 9 touches de la première page proposée redessinées.
        private void Tout()
        {
            for (var t = 0; t < 9; t++)
            {
                this.Envoyer(Disposition[Premiere + t]);
            }
        }

        // Geste abandonné (file bloquée plus de 5 s). Une tête abandonnée invalide aussi les gestes pressés après elle.
        private void Abandonner(Int32 touche)
        {
            PluginLog.Warning($"touche {touche + 1} abandonnée : elle attendait depuis plus de 5 s");
            if (touche < Capture)
            {
                this._teteRatee = this._horloge.Elapsed.TotalSeconds;
            }
        }

        // Conversation qu'ouvre une tête : celle qui clignote, si elle n'est pas déjà affichée ; appli devant, la suivante
        // de la liste (chats du relais compris : ils ne clignotent pas, mais l'utilisateur peut les lire), en boucle. Null : l'appli
        // seule (fermée, derrière sans alerte, ou une seule conversation, déjà affichée).
        // Claude n'est reconnue qu'à son titre : deux du même titre, c'est celle que le pad vient d'ouvrir (sinon le tour
        // restait bloqué sur la seconde, prise pour la première).
        private Conversation Cible(Int32 agent, Instantane instantane)
        {
            var liste = instantane.Conversations[agent];
            var devant = Observateur.PremierPlan() == Observateur.Processus[agent];
            var affichee = devant ? this._observateur.ConversationAffichee(agent) : null;
            Boolean Montree(Conversation x) => (agent == 0 ? x.Titre : x.Id) == affichee;
            var ici = Array.FindIndex(liste, x => Montree(x) && x.Id == this._ouverte[agent]);
            ici = ici >= 0 ? ici : Array.FindIndex(liste, Montree);
            PluginLog.Info($"tête {agent + 1} : {liste.Length} conversation(s), devant={devant}, affichée={affichee ?? "?"} (rang {ici})");
            if (instantane.Premiere(agent) is { Alerte: true } alerte && (ici < 0 || liste[ici] != alerte))
            {
                return alerte;
            }

            if (!devant || liste.Length == 0 || (liste.Length == 1 && ici == 0))
            {
                return null;
            }

            return liste[(ici + 1) % liste.Length];
        }

        // Descente hors de la file des gestes : elle ne prend ni le focus ni le clavier (défilement UI Automation ou du
        // DOM), et ses ~4 s (changement de vue de Claude, trois passes) faisaient abandonner les gestes pressés derrière.
        // Caduque dès qu'une touche pressée après son appui amène une autre fenêtre, ou que le plugin s'arrête.
        private void Descendre(Int32 agent, Int32 appui)
        {
            Task.Run(() =>
            {
                try
                {
                    Gestes.Descendre(agent, () => !this._arret && Volatile.Read(ref this._appuiFenetre) == appui);
                }
                catch (Exception e)
                {
                    PluginLog.Error(e, $"descente de la conversation, tête {agent + 1}");
                }
            });
        }

        // Capture ou photo : l'image copiée puis collée (sans envoyer) dans la conversation affichée de l'IA devant, sinon de
        // la dernière IA que l'utilisateur a utilisée, ramenée devant (la photo exigeait une IA devant : depuis une autre appli,
        // l'appui ne faisait rien, 24/10).
        private void CollerDansIa(String geste, Func<Boolean> copier, Action collee)
        {
            var devant = Array.IndexOf(Observateur.Processus, Observateur.PremierPlan());
            var cible = devant >= 0 ? devant : this._derniereIa;
            if (cible < 0)
            {
                PluginLog.Warning($"{geste} : aucune IA utilisée depuis le démarrage, rien n'a été copié");
                return;
            }

            if (!copier() || this._arret)
            {
                return;
            }

            if (devant < 0)
            {
                Gestes.Premier(cible);
                if (!this.Devant(cible))
                {
                    return;
                }
            }

            // Collé seulement dans l'IA visée au moment de l'appui : si l'utilisateur est passé sur une autre appli pendant
            // la copie, rien n'est collé (relecture de Codex, 25/10).
            if (Gestes.Coller(geste, cible))
            {
                collee();
            }
        }

        // Page 2 : gestes immédiats sur le poste.
        private void Page2(Int32 touche, Signet site)
        {
            switch (touche)
            {
                case VolumeMoins: Releve.Media(Releve.VolumeMoins); break;
                case Muet: Releve.Basculer(0); break;
                case VolumePlus: Releve.Media(Releve.VolumePlus); break;
                case Gmail: AppliWeb.Gmail.Ouvrir(); break;
                case Agenda: AppliWeb.Agenda.Ouvrir(); break;
                case Messages: AppliWeb.Messages.Ouvrir(); break;
                case Sante: Gestes.GestionnaireDesTaches(); break;
                case MisesAJour:
                    // Tout mettre à jour, hors de la file des gestes (jusqu'à 2 min et demie pour Windows Update) et sans rien
                    // amener au premier plan ; la touche s'anime jusqu'à la fin. Un appui pendant ce temps ne relance rien.
                    if (this._miseAJourFinie >= this._miseAJour)
                    {
                        this._miseAJour = this._horloge.Elapsed.TotalSeconds;
                        this._miseAJourTache = Task.Run(() =>
                        {
                            var (store, windows) = Releve.MettreAJour(this._arretMiseAJour.Token);
                            this._miseAJourReussie = store is Releve.IssueMiseAJour.Lancee or Releve.IssueMiseAJour.AJour
                                && windows is Releve.IssueMiseAJour.Lancee or Releve.IssueMiseAJour.AJour;
                            this._miseAJourFinie = this._horloge.Elapsed.TotalSeconds;
                        });
                    }

                    break;
                default: AppliWeb.Site(site ?? Signet.Reglage)?.Ouvrir(); break;
            }

            this._poste = this.LirePoste();
            this.Envoyer(touche);
        }

        // Poste et non-lus de Gmail et Messages : le titre de leur fenêtre ou de leur ONGLET de Chrome sur le PC fait foi
        // (vrais non-lus ; Gmail ouvert dans un onglet n'avait pas de pastille, 25/10), sinon les notifications du
        // téléphone (Messages), sinon pas de pastille. Titres relus toutes les 3 s au plus, hors du fil d'animation (la
        // barre d'onglets de Chrome se lit par UI Automation).
        private Poste LirePoste()
        {
            var maintenant = this._horloge.Elapsed.TotalSeconds;
            if (maintenant - this._titresLus >= 3 && Interlocked.Exchange(ref this._titresEnCours, 1) == 0)
            {
                this._titresLus = maintenant;
                Task.Run(() =>
                {
                    try
                    {
                        // NexusBrowser d'abord, indépendamment de Chrome ; la barre d'onglets (UI Automation, coûteuse pour
                        // Chrome) n'est lue que si elle sert : Gmail sans NexusBrowser, ou Messages sans téléphone.
                        this._gmailNexusBrowser = GmailNexusBrowser();
                        var fenetres = Uia.Fenetres("chrome").Select(Uia.Titre);
                        var onglets = this._gmailNexusBrowser is null || this._notifications is null;
                        this._titres = (onglets ? fenetres.Concat(Uia.OngletsChrome()) : fenetres).ToArray();
                    }
                    catch (Exception e)
                    {
                        PluginLog.Error(e, "titres de Chrome");
                    }
                    finally
                    {
                        Volatile.Write(ref this._titresEnCours, 0);
                    }
                });
            }

            var titres = this._titres;
            return Releve.Lire() with
            {
                // Gmail : sa fenêtre ou son onglet sur le PC, sinon NexusBrowser (case « Mails non lus de Gmail pour le pad »
                // cochée : même Gmail fermé) ; jamais le téléphone, dont les notifications restent après une lecture sur le
                // PC (2 en attente, boîte vide, 25/10).
                Mails = AppliWeb.Gmail.NonLus(titres) ?? NonPerime(this._gmailNexusBrowser),
                Messages = AppliWeb.Messages.NonLus(titres) ?? this._notifications?.Item2,
            };
        }

        // Notifications du téléphone (mails, messages) : relues au plus toutes les 60 s, page 2 affichée, hors du fil
        // d'animation (adb par Wi-Fi peut prendre quelques secondes).
        private void LireNotifications()
        {
            var maintenant = this._horloge.Elapsed.TotalSeconds;
            if (maintenant - this._notificationsLues < 60 || Interlocked.Exchange(ref this._notificationsEnCours, 1) == 1)
            {
                return;
            }

            this._notificationsLues = maintenant;
            Task.Run(() =>
            {
                try
                {
                    this._notifications = this._observateur.Telephone.Notifications() is var (mails, messages) ? Tuple.Create(mails, messages) : null;
                }
                catch (Exception e)
                {
                    PluginLog.Error(e, "notifications du téléphone");
                }
                finally
                {
                    Volatile.Write(ref this._notificationsEnCours, 0);
                }
            });
        }

        // Après une tête : son appli doit être devant avant le geste suivant ; sinon les gestes déjà pressés sont abandonnés.
        private Boolean Devant(Int32 agent)
        {
            if (Gestes.AttendreDevant(agent))
            {
                return true;
            }

            this._teteRatee = this._horloge.Elapsed.TotalSeconds;
            PluginLog.Warning($"tête {agent + 1} : l'appli n'est pas venue devant en 3 s");
            return false;
        }

        public void Dispose()
        {
            // Plus aucun geste après l'arrêt : ceux en file sont abandonnés, celui en cours a 2 s pour finir.
            this._arret = true;
            this._arretMiseAJour.Cancel(); // une mise à jour en cours s'interrompt avant sa prochaine action (5 s au plus)
            try
            {
                this._miseAJourTache.Wait(5000);
            }
            catch (AggregateException e)
            {
                PluginLog.Error(e, "mises à jour à l'arrêt"); // jamais d'exception sur le fil de déchargement du service
            }
            this._gestes.CompleteAdding();
            this._filGestes?.Join(2000);
            this._pilote?.Dispose();
            foreach (var minuteur in new[] { this._lecture, this._animation, this._telephone })
            {
                // Attend le tour en cours (une lecture peut ouvrir la page Models d'Antigravity) : 5 s au plus. L'événement
                // n'est détruit que signalé : détruit avant, le minuteur le signalerait ensuite et ferait tomber le service.
                var fini = new ManualResetEvent(false);
                if (minuteur is not null && minuteur.Dispose(fini) && fini.WaitOne(5000))
                {
                    fini.Dispose();
                }
            }

            this._observateur.Telephone.Dispose();
        }
    }
}

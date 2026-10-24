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

        // Touche affichée à chaque place du pad (de gauche à droite, de haut en bas ; page 1 puis page 2) : Claude au
        // milieu, entre Codex à gauche et Antigravity à droite, qu'il fait travailler par le relais ; ses quotas dessous.
        public static readonly Int32[] Disposition = [1, 0, 2, 4, 3, 5, Capture, Photo, Dictee, 9, 10, 11, 12, 13, 14, 15, 16, 17];

        public static Int32 Place(Int32 touche) => Array.IndexOf(Disposition, touche);

        private readonly Observateur _observateur = new();
        private readonly Stopwatch _horloge = Stopwatch.StartNew();
        private readonly Double[] _appuis = Enumerable.Repeat(-9.0, Touches).ToArray();
        private readonly Double[] _demandes = Enumerable.Repeat(-9.0, Touches).ToArray(); // dernière image demandée par le service
        private readonly Double[] _avisees = Enumerable.Repeat(-9.0, Touches).ToArray(); // dernier avis de changement envoyé
        private Poste _poste;
        private volatile Tuple<Int32, Int32> _notifications; // mails, messages : objet entier, jamais lu à moitié écrit
        private Double _notificationsLues = -99;
        private Int32 _notificationsEnCours;
        private Double _miseAJour = -99; // dernier appui sur la touche des mises à jour (Windows Update et Store)
        private volatile String[] _titres = []; // titres des fenêtres de Chrome (non-lus de Gmail et Messages)
        private Double _titresLus = -99;
        private readonly Action<Int32> _redessiner;
        private readonly Object _envoi = new();
        private readonly BitmapImage[] _images = new BitmapImage[Touches];
        private readonly Byte[][] _pixels = new Byte[Touches][];
        private Timer _lecture;
        private Timer _animation;
        private Timer _telephone;
        private Instantane _instantane = Instantane.Vide;
        private Double _photoCopiee = -9;
        private String _resume;
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
        private readonly Int32[] _descentes = new Int32[3]; // génération de la dernière descente lancée par chaque tête

        public Moteur(Action<Int32> redessiner) => this._redessiner = redessiner;

        // Dernière image envoyée d'une touche (116 px), que le service redemande juste après l'avis de changement.
        public BitmapImage Image(Int32 touche) => this._images[touche];

        // Le service demande l'image d'une touche : elle est affichée.
        public void Demande(Int32 touche) => this._demandes[touche] = this._horloge.Elapsed.TotalSeconds;

        // Touche de la page 2 affichée : le service a redemandé son image après le dernier avis (1,5 s de grâce). Une page
        // cachée n'est ni relue ni dessinée ; affichée, le service demande ses images et elle repart.
        private Boolean Affichee(Int32 touche) => this._avisees[touche] <= this._demandes[touche] + 1.5;

        // Relevé forcé du quota d'Antigravity (pilote) : rend ce qui a été lu.
        public String ReleverAntigravity()
        {
            var q = this._observateur.ReleverAntigravity();
            return q is null ? "rien lu (fenêtre d'Antigravity fermée, ou autre dialogue ouvert)" : $"5 h {q.Cinq * 100:0} % utilisés, semaine {q.Semaine * 100:0} % utilisés";
        }

        // Résumé de la dernière lecture (journal et pilote).
        public String Resume => this._resume;

        // Les 9 touches telles qu'envoyées au pad, en grille 3 × 3 (PNG) : ce que l'utilisateur voit, pour les essais.
        public Byte[] Planche()
        {
            const Int32 Ecart = 12;
            using var b = new BitmapBuilder((3 * Rendu.T) + (4 * Ecart), (3 * Rendu.T) + (4 * Ecart));
            b.Clear(new BitmapColor(200, 198, 192));
            for (var t = 0; t < 9; t++)
            {
                var image = this._images[Disposition[t]] ?? Rendu.Image(Disposition[t], this.Scene).Image;
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
                if (this._avisees[touche] <= this._demandes[touche])
                {
                    this._avisees[touche] = this._horloge.Elapsed.TotalSeconds; // premier avis sans réponse : le délai court
                }
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
            this._telephone = new Timer(_ => this.Telephone(), null, 1000, 3000);
            this._observateur.Telephone.Veiller(() => Task.Run(this.Telephone));
            Task.Run(this._observateur.Telephone.PreparerCopie);
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
        };

        // Un appui : action hors du fil du service (ouvrir une appli ou taper prend du temps).
        // Rend l'issue du geste (« fait », ou la raison de l'abandon), une fois passé dans la file : le pilote l'attend.
        public Task<String> Appuyer(Int32 touche)
        {
            var scene = this.Scene;
            this._appuis[touche] = scene.Temps;
            this._observateur.NoterGeste();
            this.Envoyer(touche);

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
                            Releve.Lancer("ms-actioncenter:");
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
                                    Descendre(touche);
                                }
                            }
                            else
                            {
                                Gestes.Premier(touche);
                                if (this.Devant(touche))
                                {
                                    Descendre(touche); // appli ramenée devant : sa conversation aussi, jusqu'au bout
                                }
                            }

                            break;
                        case >= VolumeMoins:
                            Page2(touche);
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

                // Têtes qui travaillent ou appellent l'utilisateur 8 fois par seconde, le reste 4 fois (sauf la photo ; sans
                // téléphone, la touche est une horloge animée) : seules les images qui ont changé partent au pad, chacune
                // coûte cher au service Logi.
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

                // Page 2, seulement affichée : le poste relu 8 fois par seconde (quelques appels locaux), les touches
                // qui suivent le son à chaque tour, les autres 4 fois par seconde.
                var page2 = Enumerable.Range(VolumeMoins, Touches - VolumeMoins).Where(this.Affichee).ToList();
                if (page2.Count > 0)
                {
                    this.LireNotifications();
                    this._poste = this.LirePoste();
                    foreach (var t in page2.Where(t => t is Muet || this._tic % 2 == 0))
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

        private void Tout()
        {
            for (var t = 0; t < 9; t++)
            {
                this.Envoyer(t);
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
        // Caduque dès qu'une nouvelle tête de la même IA en lance une autre, ou que le plugin s'arrête.
        private void Descendre(Int32 agent)
        {
            var generation = Interlocked.Increment(ref this._descentes[agent]);
            Task.Run(() =>
            {
                try
                {
                    Gestes.Descendre(agent, () => !this._arret && Volatile.Read(ref this._descentes[agent]) == generation);
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

            collee();
            Gestes.Coller(geste);
        }

        // Page 2 : gestes immédiats sur le poste.
        private void Page2(Int32 touche)
        {
            switch (touche)
            {
                case VolumeMoins: Releve.Media(Releve.VolumeMoins); break;
                case Muet: Releve.Basculer(0); break;
                case VolumePlus: Releve.Media(Releve.VolumePlus); break;
                case Gmail: AppliWeb.Gmail.Ouvrir(); break;
                case Agenda: AppliWeb.Agenda.Ouvrir(); break;
                case Messages: AppliWeb.Messages.Ouvrir(); break;
                case Sante: Releve.Lancer("taskmgr.exe"); break;
                case MisesAJour:
                    // Les deux recherches, hors de la file des gestes (le Store met quelques secondes à s'ouvrir) et sans
                    // rien amener au premier plan ; la touche s'anime pendant ce temps.
                    this._miseAJour = this._horloge.Elapsed.TotalSeconds;
                    Task.Run(Releve.RechercherMisesAJour);
                    break;
                default: AppliWeb.Site.Ouvrir(); break;
            }

            this._poste = this.LirePoste();
            this.Envoyer(touche);
        }

        // Poste et non-lus de Gmail et Messages : le titre de leur fenêtre sur le PC fait foi (vrais non-lus), sinon les
        // notifications du téléphone, sinon pas de pastille. Titres relus toutes les 2 s au plus (la recherche des
        // fenêtres de Chrome énumère les processus).
        private Poste LirePoste()
        {
            var maintenant = this._horloge.Elapsed.TotalSeconds;
            if (maintenant - this._titresLus >= 2)
            {
                this._titresLus = maintenant;
                this._titres = Uia.Fenetres("chrome").Select(Uia.Titre).ToArray();
            }

            var titres = this._titres;
            return Releve.Lire() with
            {
                Mails = AppliWeb.Gmail.NonLus(titres) ?? this._notifications?.Item1,
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

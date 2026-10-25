namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Threading;

    // Ce que font les touches : ouvrir une IA ou sa conversation qui clignote, et agir dans l'appli au premier plan.
    public static class Gestes
    {
        private static readonly String Local = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";

        // Dernière conversation de Claude ouverte par le pad : Claude change de vue pendant ~2 s ; un geste dans son champ
        // pendant ce temps (Dictée, Photo, Capture juste après la tête) se perdait. Il attend la fin du changement.
        private static Int64 _claudeOuvert = Int64.MinValue / 2;

        public static void AttendreClaude()
        {
            var reste = 2500 - (Environment.TickCount64 - _claudeOuvert);
            if (reste > 0)
            {
                Thread.Sleep((Int32)reste);
            }
        }

        // Codex passe par le lanceur de « Codex LB » s'il est installé (NexusLB d'abord, sinon aucun modèle ne répond chez
        // qui s'en sert) ; sans lui, l'appli Codex officielle du Store, activée avec son port de débogage (9223) comme le
        // fait ce lanceur : sans port, ni ouverture de conversation ni dictée dans Codex.
        private static readonly String LanceurCodexLb = Path.Combine(Local, "ChatGPTLBLauncher", "ChatGPTLbLauncher.exe");

        private const String CodexOfficiel = "OpenAI.Codex_2p2nqsd0c76g0!App";

        private static readonly String[] Lancement =
        [
            @"shell:AppsFolder\AnthropicPBC.Claude_fnn82j28hfe8t!Claude",
            File.Exists(LanceurCodexLb) ? LanceurCodexLb : CodexOfficiel,
            @"shell:AppsFolder\electron.app.Antigravity",
        ];

        // Ouvre la conversation dans son appli : lien claude:// pour Claude, clic sur sa ligne par le port pour les deux autres.
        // Vrai si elle est ouverte : titre affiché par Claude, clic fait chez Codex, ligne ou route chez Antigravity.
        public static Boolean Ouvrir(Int32 agent, Conversation c)
        {
            switch (agent)
            {
                case 0:
                    if (Uia.FenetrePrincipale(Observateur.Processus[0]) == IntPtr.Zero)
                    {
                        Lances[0] = Environment.TickCount64; // Claude fermé : le lien le lance, Premier ne le relance pas
                    }

                    Lancer($"claude://code/continue?session={Uri.EscapeDataString(c.Id)}&source=desktop_action");
                    _claudeOuvert = Environment.TickCount64;
                    Premier(agent); // réduite, le lien seul ne la ramène pas toujours devant
                    for (var i = 0; i < 30; i++)
                    {
                        Thread.Sleep(100);
                        if (Uia.TitreClaudeAffiche() == c.Titre)
                        {
                            return true;
                        }
                    }

                    return false;
                case 1:
                    var clic = Cdp.Evaluer(Cdp.PortCodex, Js.OuvrirCodex(c.Id)) is { ValueKind: System.Text.Json.JsonValueKind.True };
                    Premier(agent);
                    return clic && Attendre(() => Cdp.Evaluer(Cdp.PortCodex, Js.SelectionCodex, attente: 1)?.GetString() == c.Id, 2000);
                default:
                    // Fenêtre fermée (dans la zone de notification) : pas de page sur le port tant qu'elle n'est pas rouverte. Sa
                    // ligne est cherchée 5 s (la barre latérale arrive après la fenêtre), puis sa route en dernier recours.
                    Premier(agent);
                    var ouverte = false;
                    for (var essai = 0; essai < 10 && !ouverte; essai++)
                    {
                        // 2 s par essai au plus (appli figée : 25 s, plus une minute de file bloquée).
                        ouverte = Cdp.Evaluer(Cdp.PortAntigravity, Js.OuvrirAntigravity(c.Id), attente: 2) is { ValueKind: System.Text.Json.JsonValueKind.True };

                        if (!ouverte)
                        {
                            Thread.Sleep(500);
                        }
                    }

                    if (!ouverte && Cdp.Evaluer(Cdp.PortAntigravity, Js.OuvrirAntigravity(c.Id, route: true)) is not { ValueKind: System.Text.Json.JsonValueKind.True })
                    {
                        return false;
                    }

                    // Ouverte pour de bon : son adresse /c/<id> (la route recharge la page : 4 s au plus).
                    return Attendre(() => Cdp.Evaluer(Cdp.PortAntigravity, "location.pathname", attente: 1)?.GetString() == "/c/" + c.Id, 4000);
            }
        }

        // Conversation ouverte par le pad : affichée jusqu'au dernier message (l'appli la rouvrait là où l'utilisateur l'avait
        // laissée). Trois passes, après le changement de vue de Claude : le fil se charge par morceaux et s'allonge. Sans
        // zone qui défile (conversation courte), rien à faire. encore : faux dès que la descente est caduque (nouvelle tête,
        // plugin arrêté), vérifié avant chaque passe.
        public static void Descendre(Int32 agent, Func<Boolean> encore)
        {
            if (agent == 0)
            {
                AttendreClaude();
            }

            for (var passe = 0; passe < 3 && encore(); passe++)
            {
                if (agent == 0)
                {
                    Uia.DescendreClaude();
                }
                else
                {
                    Cdp.Evaluer(agent == 1 ? Cdp.PortCodex : Cdp.PortAntigravity, Js.Descendre, attente: 1);
                }

                Thread.Sleep(400);
            }
        }

        // Ligne des jauges : le détail des quotas de l'IA, sous la même forme pour les trois. Claude : sa boîte
        // « Utilisation » ; Antigravity : sa boîte « View Usage » (menu des modèles) ; Codex : l'appli web installée Codex
        // LB (ses comptes cumulés, ceux de la jauge), ramenée devant si elle est ouverte. Un deuxième appui referme le
        // détail (boîte refermée, Codex LB réduite). Faux si l'appli n'était pas prête : elle est seulement lancée (ou
        // ramenée) ; l'appui suivant ouvre le détail.
        public static Boolean Utilisation(Int32 agent)
        {
            if (agent == 1)
            {
                CodexLb();
                return true;
            }

            // Boîte déjà ouverte : refermée, l'appli reste où elle est.
            if (agent == 0 ? Uia.FermerUtilisationClaude() : FermerBoiteAntigravity())
            {
                return true;
            }

            var prete = UtilisationPrete(agent);
            Premier(agent);
            if (!prete)
            {
                return false;
            }

            AttendreClaude();
            var probleme = agent == 0
                ? Uia.OuvrirUtilisationClaude() ? null : "bouton « Utilisation » de Claude introuvable"
                : Saisie(Cdp.PortAntigravity, Js.BoiteUtilisationAntigravity);
            if (probleme is not null)
            {
                PluginLog.Warning($"Utilisation : {probleme}");
            }

            return true;
        }

        // Le détail peut s'ouvrir : bouton « Utilisation » de Claude, sélecteur de modèle d'Antigravity (lus sans rien cliquer).
        private static Boolean UtilisationPrete(Int32 agent) => agent switch
        {
            0 => Uia.BoutonUtilisationClaude(),
            1 => true,
            _ => Cdp.Evaluer(Cdp.PortAntigravity, Js.SelecteurAntigravity, attente: 1) is { ValueKind: System.Text.Json.JsonValueKind.True },
        };

        // Menu des modèles d'Antigravity ouvert : refermé d'un clic de confiance sur son sélecteur (vrai s'il l'était).
        private static Boolean FermerBoiteAntigravity()
        {
            if (Cdp.Evaluer(Cdp.PortAntigravity, Js.MenuModelesAntigravity, attente: 1) is not { ValueKind: System.Text.Json.JsonValueKind.Array } point)
            {
                return false;
            }

            Cdp.Cliquer(Cdp.PortAntigravity, point[0].GetDouble(), point[1].GetDouble());
            return true;
        }

        // Codex LB, l'appli web installée dans Chrome (sa page des comptes) : sa fenêtre ramenée devant en plein écran si
        // elle est ouverte (relancée, Chrome en ouvrait une deuxième), sinon lancée par son raccourci d'appli ; sans
        // appli installée, la page d'usage officielle de Codex dans le navigateur.
        private static void CodexLb()
        {
            const String Titre = "Codex LB";
            var fenetre = Uia.FenetreTitreDebut("chrome", Titre);
            if (FermerSiDevant(fenetre))
            {
                return; // déjà devant : le deuxième appui la ferme
            }

            if (fenetre != IntPtr.Zero)
            {
                Devant(fenetre);
                return;
            }

            var chrome = Path.Combine(Local, "Google", "Chrome", "User Data");
            var raccourci = Directory.Exists(chrome)
                ? Directory.EnumerateDirectories(chrome).Select(p => Path.Combine(p, "Web Applications"))
                    .Where(Directory.Exists)
                    .SelectMany(w => Directory.EnumerateFiles(w, Titre + ".lnk", SearchOption.AllDirectories))
                    .FirstOrDefault()
                : null;
            if (raccourci is null)
            {
                Lancer("https://chatgpt.com/codex/settings/usage");
                return;
            }

            Lancer(raccourci);
            _ = System.Threading.Tasks.Task.Run(() => AgrandirALOuverture(() => Uia.FenetreTitreDebut("chrome", Titre)));
        }

        // Échéance réelle (une lecture lente compte dans le budget : la file des gestes reste sous le verrou pendant ce temps).
        private static Boolean Attendre(Func<Boolean> condition, Int32 ms)
        {
            var fin = Environment.TickCount64 + ms;
            while (Environment.TickCount64 < fin)
            {
                try
                {
                    if (condition())
                    {
                        return true;
                    }
                }
                catch (Exception)
                {
                    // page en train de changer
                }

                Thread.Sleep(200);
            }

            return false;
        }

        // Photo ou capture d'écran : déjà dans le presse-papiers, collée (Ctrl+V, sans envoyer) dans le champ de l'IA au
        // premier plan, à la suite de ce que l'utilisateur a pu écrire.
        public static void Coller(String geste)
        {
            var agent = Array.IndexOf(Observateur.Processus, Observateur.PremierPlan());
            if (agent >= 0 && Uia.Fenetres(Observateur.Processus[agent]).Count > 1)
            {
                PluginLog.Warning($"{geste} : plusieurs fenêtres de l'appli ouvertes, rien n'a été collé");
                return;
            }

            AttendreClaude();
            if (Preparer(geste, vide: false) is IntPtr fenetre && Toujours(fenetre, geste))
            {
                Touches(VK_CONTROL, 0x56);
            }
        }

        // Dictée : une seule à la fois. S'il y en a une en cours n'importe où (dictée ou chat vocal de Codex, dictée de
        // Claude, mémo vocal d'Antigravity), cet appui l'arrête, même si l'utilisateur est passé sur une autre appli entre-temps
        // (le texte reste dans son champ, sans envoi) ; l'appui suivant en ouvre une sur l'appli affichée, par son propre
        // bouton micro. Aucune IA devant : la dictée Windows (Win+H) là où est le curseur.
        // Rend l'IA où une dictée vient d'être lancée (0 à 2), 3 pour la dictée Windows, -1 si rien n'a été lancé (une dictée
        // arrêtée, ou une appli dont on ne sait pas si elle dicte encore : jamais deux micros ouverts).
        public static Int32 Dicter()
        {
            switch (ArreterDicteeOuverte())
            {
                case true:
                    return -1;
                case null:
                    PluginLog.Warning("Dictée : une appli d'IA ne répond pas (dictée peut-être en cours), rien n'a été lancé");
                    return -1;
            }

            var fenetre = GetForegroundWindow();
            var agent = Array.IndexOf(Observateur.Processus, Observateur.PremierPlan());
            var probleme = agent switch
            {
                0 => AttendreEt(() => Uia.DicterClaude(fenetre)),
                1 => Saisie(Cdp.PortCodex, Js.Cliquer(Js.MicroCodex)),
                2 => Uia.Cliquer(Observateur.Processus[2], Js.MicroAntigravity) ? null : "bouton de dictée d'Antigravity introuvable",
                _ => DicteeWindows(),
            };
            if (probleme is not null)
            {
                PluginLog.Warning($"Dictée : {probleme}");
                return -1;
            }

            return agent < 0 ? 3 : agent;
        }

        // Après une tête : l'appli demandée est vraiment devant avant le geste suivant de la file (3 s au plus). Sans
        // cela, un geste juste après la tête de Claude partait dans l'appli d'avant (relecture du 23/10).
        public static Boolean AttendreDevant(Int32 agent)
        {
            for (var i = 0; i < 30; i++)
            {
                if (Observateur.PremierPlan() == Observateur.Processus[agent])
                {
                    return true;
                }

                Thread.Sleep(100);
            }

            return false;
        }

        private static String AttendreEt(Func<String> geste)
        {
            AttendreClaude();
            return geste();
        }

        private static String DicteeWindows()
        {
            Thread.Sleep(80);
            Touches(VK_LWIN, 0x48);
            return null;
        }

        // Focus sur le champ de l'IA au premier plan ; sa fenêtre si c'est prêt, sinon null (raison au journal).
        private static IntPtr? Preparer(String geste, Boolean vide)
        {
            var fenetre = GetForegroundWindow();
            var probleme = Array.IndexOf(Observateur.Processus, Observateur.PremierPlan()) switch
            {
                0 => AttendreEt(() => Uia.PreparerSaisieClaude(fenetre, vide)),
                1 => Saisie(Cdp.PortCodex, Js.PreparerSaisie(vide)),
                2 => Saisie(Cdp.PortAntigravity, Js.PreparerSaisie(vide)),
                _ => "aucune appli d'IA au premier plan",
            };
            if (probleme is null)
            {
                return fenetre;
            }

            PluginLog.Warning($"{geste} : {probleme}, rien n'a été tapé");
            return null;
        }

        // Le focus posé, la fenêtre doit encore être devant avant la moindre touche.
        private static Boolean Toujours(IntPtr fenetre, String geste)
        {
            Thread.Sleep(80);
            if (GetForegroundWindow() == fenetre)
            {
                return true;
            }

            PluginLog.Warning($"{geste} : la fenêtre a changé, rien n'a été tapé");
            return false;
        }

        // Arrête la dictée en cours où qu'elle soit : vrai si une a été arrêtée, faux s'il n'y en avait aucune, null si une
        // appli lancée ne répond pas (on ne sait pas). Codex : sa bulle de chat vocal, sinon sa page principale.
        private static Boolean? ArreterDicteeOuverte()
        {
            Boolean Lancee(Int32 agent) => Process.GetProcessesByName(Observateur.Processus[agent]).Length > 0;
            Boolean? Clic(String url = null) => Cdp.Evaluer(Cdp.PortCodex, Js.Cliquer(Js.Arrets), url)?.GetString() is { } r ? r == "ok" : null;
            Boolean? codex = !Lancee(1) ? false
                : Cdp.PageOuverte(Cdp.PortCodex, Cdp.BulleVocaleCodex) switch
                {
                    null => null,
                    true when Clic(Cdp.BulleVocaleCodex) is var bulle && bulle != false => bulle,
                    _ => Clic(),
                };
            if (codex == true)
            {
                return true;
            }

            if ((Lancee(2) && Uia.Cliquer(Observateur.Processus[2], Js.Arrets)) || (Lancee(0) && Uia.ArreterDicteeClaude()))
            {
                return true;
            }

            return codex; // false : aucune dictée ; null : Codex muet, on ne sait pas
        }

        private static String Saisie(Int32 port, String js, String url = null)
        {
            var r = Cdp.Evaluer(port, js, url);
            return r?.GetString() is "ok" ? null : r?.GetString() ?? "port de débogage muet";
        }

        // Met l'appli au premier plan, en plein écran, ou la lance si elle est fermée.
        private static readonly Int64[] Lances = [Int64.MinValue / 2, Int64.MinValue / 2, Int64.MinValue / 2];

        public static void Premier(Int32 agent)
        {
            // Sa fenêtre visible avec un titre (la « fenêtre principale » de Windows est parfois vide, chez Antigravity).
            var fenetre = Uia.FenetrePrincipale(Observateur.Processus[agent]);
            if (fenetre == IntPtr.Zero)
            {
                // Rouverte par son lanceur (Codex : celui de Codex LB), Windows refusait le premier plan à ce lanceur :
                // la fenêtre reparaissait derrière l'appli d'avant (Claude, 24/10). Appli déjà lancée, cachée dans la zone
                // de notification : sa fenêtre revient en moins de 3 s et le pad la passe devant lui-même.
                // Un lancement de moins de 20 s (appui répété pendant un démarrage à froid, lien claude:// d'Ouvrir) n'est
                // pas doublé : sa fenêtre est seulement attendue.
                if (Environment.TickCount64 - Lances[agent] > 20000)
                {
                    Lancer(Lancement[agent]);
                    Lances[agent] = Environment.TickCount64;
                }

                if (Attendre(() => (fenetre = Uia.FenetrePrincipale(Observateur.Processus[agent])) != IntPtr.Zero, 3000))
                {
                    Devant(fenetre);
                    return;
                }

                _ = System.Threading.Tasks.Task.Run(() => AgrandirALOuverture(() => Uia.FenetrePrincipale(Observateur.Processus[agent])));
                return;
            }

            Devant(fenetre);
        }

        // Une fenêtre devant, en plein écran.
        // Une fenêtre réduite dans la barre des tâches, sans prendre le premier plan.
        public static void Reduire(IntPtr fenetre) => ShowWindow(fenetre, SW_MINIMIZE);

        // Fenêtre réaffichée sans être activée, tout en dessous des autres : ses boutons redeviennent lisibles (réduite, une
        // appli UWP n'en publie plus aucun), sans rien cacher à Julien.
        public static void DerriereSansActiver(IntPtr fenetre)
        {
            if (IsIconic(fenetre))
            {
                ShowWindow(fenetre, 4); // SW_SHOWNOACTIVATE
            }

            SetWindowPos(fenetre, new IntPtr(1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // HWND_BOTTOM ; NOSIZE | NOMOVE | NOACTIVATE
            System.Threading.Thread.Sleep(800); // le temps qu'elle redessine ses boutons
        }

        [DllImport("user32.dll")]
        private static extern Boolean IsIconic(IntPtr h);

        [DllImport("user32.dll")]
        private static extern Boolean SetWindowPos(IntPtr h, IntPtr apres, Int32 x, Int32 y, Int32 l, Int32 ht, UInt32 drapeaux);

        // Deuxième appui d'une touche qui ouvre une fenêtre : elle est devant, elle se ferme (vrai si c'est fait).
        public static Boolean FermerSiDevant(IntPtr fenetre)
        {
            if (fenetre == IntPtr.Zero || fenetre != GetForegroundWindow())
            {
                return false;
            }

            PostMessage(fenetre, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
            return true;
        }

        [DllImport("user32.dll")]
        private static extern Boolean PostMessage(IntPtr h, UInt32 message, IntPtr w, IntPtr l);

        // Gestionnaire des tâches (santé du PC), sans élévation : lancé en administrateur (son manifeste le demande), le
        // pad ne pouvait pas le fermer. Un deuxième appui le ferme : il refuse le message de fermeture même sans élévation
        // (accès refusé, 25/10), il est donc terminé (il ne garde aucun travail ouvert ; élevé, rien n'est possible).
        public static void GestionnaireDesTaches()
        {
            var fenetre = Uia.FenetrePrincipale("Taskmgr");
            if (fenetre != IntPtr.Zero && fenetre == GetForegroundWindow())
            {
                foreach (var processus in Process.GetProcessesByName("Taskmgr"))
                {
                    try
                    {
                        processus.Kill();
                    }
                    catch (Exception)
                    {
                        // lancé en administrateur par quelqu'un d'autre : Windows le protège
                    }
                    finally
                    {
                        processus.Dispose();
                    }
                }

                return;
            }

            if (fenetre == IntPtr.Zero)
            {
                try
                {
                    var info = new ProcessStartInfo("taskmgr.exe") { UseShellExecute = false };
                    info.Environment["__COMPAT_LAYER"] = "RunAsInvoker";
                    Process.Start(info)?.Dispose();
                }
                catch (Exception e)
                {
                    PluginLog.Error(e, "Gestionnaire des tâches");
                    return;
                }

                Attendre(() => (fenetre = Uia.FenetrePrincipale("Taskmgr")) != IntPtr.Zero, 4000);
            }

            if (fenetre != IntPtr.Zero)
            {
                Devant(fenetre);
            }
        }

        public static void Devant(IntPtr fenetre)
        {
            // Restaurée ou relancée, l'appli reprenait parfois une petite fenêtre (Codex, 24/10) : toujours agrandie.
            if (!IsZoomed(fenetre))
            {
                ShowWindow(fenetre, SW_SHOWMAXIMIZED);
            }

            // Windows ne cède le premier plan qu'à l'appli qui a reçu la dernière entrée : le fil du pad est rattaché un
            // instant à celui de la fenêtre de devant, qui peut le céder. Plus d'Alt neutre (il partait à l'appli de
            // devant, Claude souvent, qui gardait ainsi le droit de reprendre le premier plan, à la fin d'une réponse par
            // exemple : « Claude repasse devant », 24/10).
            for (var essai = 0; essai < 3 && GetForegroundWindow() != fenetre; essai++)
            {
                var moi = GetCurrentThreadId();
                var lui = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
                var rattache = lui != 0 && lui != moi && AttachThreadInput(moi, lui, true);
                try
                {
                    BringWindowToTop(fenetre);
                    SetForegroundWindow(fenetre);
                }
                finally
                {
                    if (rattache)
                    {
                        AttachThreadInput(moi, lui, false);
                    }
                }

                Thread.Sleep(150);
            }

            // Comme un clic de l'utilisateur : la dernière entrée revient à l'appli amenée devant (F24, touche sans effet), et
            // plus à celle d'avant, qui ne peut alors plus lui reprendre le premier plan.
            if (GetForegroundWindow() == fenetre)
            {
                Touches(VK_F24);
            }
        }

        // Appli lancée par le pad : sa fenêtre arrive après quelques secondes (Codex passe par le lanceur de Codex LB),
        // dans la taille qu'elle avait gardée ; agrandie dès qu'elle paraît, hors de la file des gestes (30 s au plus).
        private static void AgrandirALOuverture(Func<IntPtr> trouver)
        {
            for (var i = 0; i < 120; i++)
            {
                Thread.Sleep(250);
                var fenetre = trouver();
                if (fenetre != IntPtr.Zero)
                {
                    // Agrandie seulement si elle a pris le premier plan d'elle-même : arrivée tard (démarrage à froid),
                    // elle ne le vole pas à ce que l'utilisateur fait entre-temps (relecture d'Antigravity, 24/10).
                    Thread.Sleep(500); // laisse l'appli poser sa propre taille, puis l'agrandit
                    if (!IsZoomed(fenetre) && GetForegroundWindow() == fenetre)
                    {
                        ShowWindow(fenetre, SW_SHOWMAXIMIZED);
                    }

                    return;
                }
            }
        }

        [DllImport("kernel32.dll")]
        private static extern UInt32 GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern UInt32 GetWindowThreadProcessId(IntPtr hWnd, IntPtr pid);

        [DllImport("user32.dll")]
        private static extern Boolean AttachThreadInput(UInt32 idAttach, UInt32 idAttachTo, Boolean fAttach);

        [DllImport("user32.dll")]
        private static extern Boolean BringWindowToTop(IntPtr hWnd);

        private static void Lancer(String cible)
        {
            if (cible == CodexOfficiel)
            {
                ActiverCodex();
                return;
            }

            try
            {
                var shell = cible.StartsWith("shell:", StringComparison.Ordinal);
                Process.Start(new ProcessStartInfo(shell ? "explorer.exe" : cible, shell ? cible : "") { UseShellExecute = true });
            }
            catch (Exception e)
            {
                PluginLog.Error(e, $"lancement de {cible}");
            }
        }

        // Appli Codex du Store, lancée avec son port de débogage (une appli empaquetée ne prend ses arguments que par
        // l'activation de Windows) ; sur un fil STA, comme l'attend ce service COM.
        private static void ActiverCodex()
        {
            var fil = new Thread(() =>
            {
                try
                {
                    var r = ((IApplicationActivationManager)new ApplicationActivationManager())
                        .ActivateApplication(CodexOfficiel, $"--remote-debugging-port={Cdp.PortCodex}", 0, out _);
                    if (r != 0)
                    {
                        PluginLog.Warning($"lancement de Codex refusé (0x{r:X8}) : appli Codex absente ?");
                    }
                }
                catch (Exception e)
                {
                    PluginLog.Error(e, "lancement de Codex");
                }
            })
            { IsBackground = true, Name = "lancement de Codex" };
            fil.SetApartmentState(ApartmentState.STA);
            fil.Start();
            fil.Join(10000);
        }

        [ComImport]
        [Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
        private class ApplicationActivationManager
        {
        }

        [ComImport]
        [Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IApplicationActivationManager
        {
            [PreserveSig]
            Int32 ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] String appUserModelId, [MarshalAs(UnmanagedType.LPWStr)] String arguments, UInt32 options, out UInt32 processId);
        }

        private static void Touches(params Byte[] touches)
        {
            foreach (var k in touches)
            {
                keybd_event(k, 0, 0, UIntPtr.Zero);
            }

            foreach (var k in touches.Reverse())
            {
                keybd_event(k, 0, 2, UIntPtr.Zero);
            }
        }

        // Saisie Unicode, indépendante de la disposition du clavier (AZERTY) ; vrai si toutes les touches sont parties.
        private static Boolean Taper(String texte)
        {
            var entrees = texte.SelectMany(c => new[] { Unicode(c, 0), Unicode(c, KEYEVENTF_KEYUP) }).ToArray();
            return SendInput((UInt32)entrees.Length, entrees, Marshal.SizeOf<INPUT>()) == entrees.Length;
        }

        private static INPUT Unicode(Char c, UInt32 drapeaux) => new()
        {
            type = 1,
            ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | drapeaux },
        };

        private const Byte VK_LWIN = 0x5B;
        private const Byte VK_F24 = 0x87;
        private const Byte VK_RETURN = 0x0D;
        private const Byte VK_CONTROL = 0x11;
        private const UInt32 KEYEVENTF_KEYUP = 0x0002;
        private const UInt32 KEYEVENTF_UNICODE = 0x0004;

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public UInt16 wVk;
            public UInt16 wScan;
            public UInt32 dwFlags;
            public UInt32 time;
            public IntPtr dwExtraInfo;
        }

        // INPUT : union clavier / souris ; la taille doit couvrir MOUSEINPUT (la plus grande) pour SendInput.
        [StructLayout(LayoutKind.Explicit, Size = 40)]
        private struct INPUT
        {
            [FieldOffset(0)]
            public UInt32 type;

            [FieldOffset(8)]
            public KEYBDINPUT ki;
        }

        [DllImport("user32.dll")]
        private static extern UInt32 SendInput(UInt32 nInputs, INPUT[] pInputs, Int32 cbSize);

        [DllImport("user32.dll")]
        private static extern void keybd_event(Byte bVk, Byte bScan, UInt32 dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern Boolean SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern Boolean ShowWindow(IntPtr hWnd, Int32 nCmdShow);

        [DllImport("user32.dll")]
        private static extern Boolean IsZoomed(IntPtr hWnd);

        private const Int32 SW_SHOWMAXIMIZED = 3;
        private const Int32 SW_MINIMIZE = 6;
    }
}

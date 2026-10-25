// Le plugin, comme le service Logi qui le charge, ne tourne que sous Windows (UI Automation, user32, COM).
[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text.RegularExpressions;

    // Fenêtre de Claude (appli de bureau sans port de débogage) par UI Automation : champ « Prompt », bouton de dictée,
    // bouton « Utilisation » (quota de la semaine) de sa barre d'état.
    public static class Uia
    {
        private const Int32 TreeScopeDescendants = 4;
        private const Int32 NomPropriete = 30005; // UIA_NamePropertyId
        private const Int32 TypePropriete = 30003; // UIA_ControlTypePropertyId
        private const Int32 ValeurPropriete = 30045; // UIA_ValueValuePropertyId
        private const Int32 FocusClavier = 30008; // UIA_HasKeyboardFocusPropertyId
        private const Int32 TypeEdit = 50004; // UIA_EditControlTypeId
        private const Int32 TypeBouton = 50000; // UIA_ButtonControlTypeId
        private const Int32 MotifInvoke = 10000;
        private const Int32 MotifToggle = 10015;
        private const Int32 MotifDefilement = 10004; // UIA_ScrollPatternId
        private const Int32 MotifDeplier = 10005; // UIA_ExpandCollapsePatternId
        private const Int32 Rectangle = 30001; // UIA_BoundingRectanglePropertyId : gauche, haut, largeur, hauteur
        private const Int32 DefilementPossible = 30034; // UIA_IsScrollPatternAvailablePropertyId
        private const Int32 DefilementVertical = 30058; // UIA_ScrollVerticallyScrollablePropertyId (30057 : horizontal)
        private const Int32 SousChaine = 2; // PropertyConditionFlags_MatchSubstring

        private static IUIAutomation Nouveau() =>
            (IUIAutomation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("ff48dba4-60ef-4201-aa87-54103eef594e")));

        // Donne le focus au champ de la fenêtre de Claude ; renvoie null si c'est prêt, sinon la raison.
        // vide : refusé si l'utilisateur a déjà commencé à écrire ; sinon le texte s'ajoute au sien (Photo, Capture).
        public static String PreparerSaisieClaude(IntPtr fenetre, Boolean vide)
        {
            var uia = Nouveau();
            var champs = uia.ElementFromHandle(fenetre)?.FindAll(TreeScopeDescendants,
                uia.CreateAndCondition(uia.CreatePropertyCondition(TypePropriete, TypeEdit), uia.CreatePropertyCondition(NomPropriete, "Prompt")));
            if (champs is null || champs.Length != 1)
            {
                return champs is null || champs.Length == 0 ? "champ de saisie de Claude introuvable" : "plusieurs champs de saisie dans Claude";
            }

            var champ = champs.GetElement(0);
            if (vide)
            {
                var texte = champ.GetCurrentPropertyValue(ValeurPropriete) as String;
                if (texte is null)
                {
                    return "contenu du champ de Claude illisible";
                }

                if (texte.Trim().Length > 0)
                {
                    return "le champ de Claude contient déjà un brouillon";
                }
            }

            champ.SetFocus();
            for (var i = 0; i < 6; i++) // Chromium l'annonce un instant après
            {
                if (champ.GetCurrentPropertyValue(FocusClavier) is true)
                {
                    return null;
                }

                System.Threading.Thread.Sleep(50);
            }

            return "le champ de Claude n'a pas pris le focus";
        }

        // Clic natif (UI Automation, comme le lecteur d'écran) sur le premier bouton visible au nom donné, dans les
        // fenêtres d'un processus ; vrai s'il a été cliqué. Antigravity n'ouvre son micro que sur un clic de ce genre
        // (un element.click() ou un clic simulé par son port de débogage restent sans effet, 23/10).
        public static Boolean Cliquer(String processus, params String[] noms) => Fenetres(processus).Any(f => CliquerDans(f, noms));

        // Un élément de la fenêtre dont le nom contient un de ces textes (« Vous êtes à jour »…), rien n'est cliqué.
        public static Boolean Affiche(IntPtr fenetre, params String[] textes)
        {
            var uia = Nouveau();
            var racine = uia.ElementFromHandle(fenetre);
            return textes.Any(t => racine?.FindFirst(TreeScopeDescendants, uia.CreatePropertyConditionEx(NomPropriete, t, SousChaine)) is not null);
        }

        // Même clic, dans une seule fenêtre (le Store, derrière : Invoke n'a pas besoin du premier plan).
        public static Boolean CliquerDans(IntPtr fenetre, params String[] noms)
        {
            var uia = Nouveau();
            var racine = uia.ElementFromHandle(fenetre);
            foreach (var nom in noms)
            {
                var bouton = racine?.FindFirst(TreeScopeDescendants,
                    uia.CreateAndCondition(uia.CreatePropertyCondition(TypePropriete, TypeBouton), uia.CreatePropertyCondition(NomPropriete, nom)));
                if (bouton?.GetCurrentPattern(MotifInvoke) is IUIAutomationInvokePattern appui)
                {
                    appui.Invoke();
                    return true;
                }
            }

            return false;
        }

        // Titre d'une fenêtre (vide si elle n'en a pas).
        public static String Titre(IntPtr h)
        {
            var texte = new System.Text.StringBuilder(GetWindowTextLength(h) + 1);
            return GetWindowText(h, texte, texte.Capacity) > 0 ? texte.ToString() : "";
        }

        // Fenêtres visibles d'un processus (toutes, pas seulement « MainWindowHandle » : chez Antigravity, c'est une fenêtre
        // vide, sans titre ni boutons, 23/10).
        // Sa fenêtre principale : la plus grande fenêtre visible titrée (pas une bulle, comme celle du chat vocal de Codex).
        public static IntPtr FenetrePrincipale(String processus) =>
            Fenetres(processus).OrderByDescending(h => GetWindowRect(h, out var r) ? (Int64)(r.Droite - r.Gauche) * (r.Bas - r.Haut) : 0).FirstOrDefault();

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public Int32 Gauche, Haut, Droite, Bas;
        }

        [DllImport("user32.dll")]
        private static extern Boolean GetWindowRect(IntPtr h, out RECT r);

        // Fenêtre visible d'un processus au titre exact (une appli web installée : « Codex LB » dans chrome) ; zéro si aucune.
        public static IntPtr FenetreTitree(String processus, String titre) => Fenetres(processus).FirstOrDefault(h => Titre(h) == titre);

        // Fenêtre dont le titre commence par ce texte (Codex LB : « Codex LB - 127.0.0.1 » depuis le 25/10).
        public static IntPtr FenetreTitreDebut(String processus, String debut) =>
            Fenetres(processus).FirstOrDefault(h => Titre(h).StartsWith(debut, StringComparison.Ordinal));

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern Int32 GetWindowText(IntPtr h, System.Text.StringBuilder texte, Int32 max);

        public static System.Collections.Generic.List<IntPtr> Fenetres(String processus)
        {
            var pids = Process.GetProcessesByName(processus).Select(p => (UInt32)p.Id).ToHashSet();
            var fenetres = new System.Collections.Generic.List<IntPtr>();
            EnumWindows((h, _) =>
            {
                GetWindowThreadProcessId(h, out var pid);
                if (pids.Contains(pid) && IsWindowVisible(h) && GetWindowTextLength(h) > 0)
                {
                    fenetres.Add(h);
                }

                return true;
            }, IntPtr.Zero);
            return fenetres;
        }

        private delegate Boolean RappelFenetre(IntPtr h, IntPtr l);

        [DllImport("user32.dll")]
        private static extern Boolean EnumWindows(RappelFenetre rappel, IntPtr l);

        [DllImport("user32.dll")]
        private static extern Boolean IsWindowVisible(IntPtr h);

        [DllImport("user32.dll")]
        private static extern UInt32 GetWindowThreadProcessId(IntPtr hWnd, out UInt32 processId);

        [DllImport("user32.dll")]
        private static extern Int32 GetWindowTextLength(IntPtr h);

        // Titre de la conversation affichée dans Claude : son en-tête « <titre>, renommer la session » ; null si illisible.
        public static String TitreClaudeAffiche()
        {
            const String Suffixe = ", renommer la session";
            var uia = Nouveau();
            foreach (var fenetre in Fenetres("claude"))
            {
                var bouton = uia.ElementFromHandle(fenetre)?.FindFirst(TreeScopeDescendants,
                    uia.CreateAndCondition(uia.CreatePropertyCondition(TypePropriete, TypeBouton), uia.CreatePropertyConditionEx(NomPropriete, Suffixe, SousChaine)));
                if (bouton?.GetCurrentPropertyValue(NomPropriete) is String nom && nom.EndsWith(Suffixe, StringComparison.Ordinal))
                {
                    return nom[..^Suffixe.Length];
                }
            }

            return null;
        }

        // Une dictée de Claude est en cours : son bouton « Arrêter la dictée » est là (rien n'est cliqué).
        public static Boolean DicteeClaude()
        {
            var uia = Nouveau();
            return Fenetres("claude")
                .Any(h => uia.ElementFromHandle(h)?.FindFirst(TreeScopeDescendants,
                    uia.CreateAndCondition(uia.CreatePropertyCondition(TypePropriete, TypeBouton), uia.CreatePropertyCondition(NomPropriete, "Arrêter la dictée"))) is not null);
        }

        // Arrête une dictée de Claude en cours, dans n'importe laquelle de ses fenêtres ; vrai s'il y en avait une.
        public static Boolean ArreterDicteeClaude()
        {
            foreach (var fenetre in Fenetres("claude"))
            {
                var uia = Nouveau();
                var bouton = uia.ElementFromHandle(fenetre)?.FindFirst(TreeScopeDescendants,
                    uia.CreateAndCondition(uia.CreatePropertyCondition(TypePropriete, TypeBouton), uia.CreatePropertyCondition(NomPropriete, "Arrêter la dictée")));
                if (bouton?.GetCurrentPattern(MotifToggle) is IUIAutomationTogglePattern bascule)
                {
                    bascule.Toggle();
                    return true;
                }

                if (bouton?.GetCurrentPattern(MotifInvoke) is IUIAutomationInvokePattern appui)
                {
                    appui.Invoke();
                    return true;
                }
            }

            return false;
        }

        // Lance la dictée de Claude (l'arrêt passe par ArreterDicteeClaude, avant) : son micro « Maintenez appuyé pour
        // enregistrer », basculé comme un clic, attendu 2 s au plus s'il n'est pas encore là (vue qui change).
        public static String DicterClaude(IntPtr fenetre)
        {
            var uia = Nouveau();
            IUIAutomationElement micro = null;
            for (var i = 0; i < 7 && (micro = uia.ElementFromHandle(fenetre)?.FindFirst(TreeScopeDescendants,
                uia.CreateAndCondition(uia.CreatePropertyCondition(TypePropriete, TypeBouton), uia.CreatePropertyCondition(NomPropriete, "Maintenez appuyé pour enregistrer")))) is null; i++)
            {
                System.Threading.Thread.Sleep(300);
            }

            if (micro is null)
            {
                return "bouton de dictée de Claude introuvable";
            }

            if (micro.GetCurrentPattern(MotifToggle) is IUIAutomationTogglePattern bascule)
            {
                bascule.Toggle();
            }
            else if (micro.GetCurrentPattern(MotifInvoke) is IUIAutomationInvokePattern appui)
            {
                appui.Invoke();
            }
            else
            {
                return "bouton de dictée de Claude inactif";
            }

            return null;
        }

        // Conversation de Claude descendue jusqu'au dernier message : sa plus grande zone qui défile verticalement (le fil ;
        // la barre latérale et le panneau de droite sont plus petits), mise à 100 %. Vrai si une zone a été descendue.
        public static Boolean DescendreClaude()
        {
            var uia = Nouveau();
            foreach (var fenetre in Fenetres("claude"))
            {
                var zones = uia.ElementFromHandle(fenetre)?.FindAll(TreeScopeDescendants,
                    uia.CreateAndCondition(uia.CreatePropertyCondition(DefilementPossible, true), uia.CreatePropertyCondition(DefilementVertical, true)));
                IUIAutomationElement fil = null;
                var aire = 0.0;
                for (var i = 0; i < (zones?.Length ?? 0); i++)
                {
                    var zone = zones.GetElement(i);
                    if (zone.GetCurrentPropertyValue(Rectangle) is Double[] { Length: 4 } r && r[2] * r[3] > aire)
                    {
                        (fil, aire) = (zone, r[2] * r[3]);
                    }
                }

                if (fil?.GetCurrentPattern(MotifDefilement) is IUIAutomationScrollPattern defilement)
                {
                    defilement.SetScrollPercent(-1, 100); // -1 : UIA_ScrollPatternNoScroll, l'horizontale ne bouge pas
                    return true;
                }
            }

            return false;
        }

        // Le bouton « Utilisation » de la barre d'état de Claude est là (rien n'est cliqué).
        public static Boolean BoutonUtilisationClaude() => BoutonsUtilisation().Any(b => b is not null);

        private static System.Collections.Generic.IEnumerable<IUIAutomationElement> BoutonsUtilisation()
        {
            var uia = Nouveau();
            return Fenetres("claude").Select(fenetre => uia.ElementFromHandle(fenetre)?.FindFirst(TreeScopeDescendants,
                uia.CreateAndCondition(uia.CreatePropertyCondition(TypePropriete, TypeBouton), uia.CreatePropertyConditionEx(NomPropriete, "Utilisation :", SousChaine))));
        }

        // Referme la boîte « Utilisation » de Claude si elle est dépliée ; vrai si elle l'était.
        public static Boolean FermerUtilisationClaude()
        {
            foreach (var bouton in BoutonsUtilisation())
            {
                if (bouton?.GetCurrentPattern(MotifDeplier) is IUIAutomationExpandCollapsePattern menu
                    && menu.CurrentExpandCollapseState == 1)
                {
                    menu.Collapse();
                    return true;
                }
            }

            return false;
        }

        // Ouvre le menu « Utilisation » de la barre d'état de Claude (quotas en détail), à la demande de l'utilisateur.
        public static Boolean OuvrirUtilisationClaude()
        {
            foreach (var bouton in BoutonsUtilisation())
            {
                if (bouton?.GetCurrentPattern(MotifDeplier) is IUIAutomationExpandCollapsePattern menu)
                {
                    menu.Expand();
                    return true;
                }

                if (bouton?.GetCurrentPattern(MotifInvoke) is IUIAutomationInvokePattern appui)
                {
                    appui.Invoke();
                    return true;
                }
            }

            return false;
        }

        // Secours, sans le plugin Nexus Pad de Claude : le nom de son bouton « Utilisation », jamais ouvert (déplié, il
        // prenait le clavier). Il ne donne que la fenêtre la plus entamée (« Utilisation : Hebdomadaire · tous les
        // modèles : 68% et … ») ; l'autre reste inconnue.
        public static Quota QuotaClaude()
        {
            var uia = Nouveau();
            foreach (var fenetre in Fenetres("claude"))
            {
                var bouton = uia.ElementFromHandle(fenetre)?.FindFirst(TreeScopeDescendants,
                    uia.CreateAndCondition(uia.CreatePropertyCondition(TypePropriete, TypeBouton), uia.CreatePropertyConditionEx(NomPropriete, "Utilisation :", SousChaine)));
                if (bouton?.GetCurrentPropertyValue(NomPropriete) is not String nom || Regex.Match(nom, @"(\d+(?:[.,]\d+)?)\s*%") is not { Success: true } part)
                {
                    continue;
                }

                var valeur = Double.Parse(part.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture) / 100;
                return nom.Contains("session", StringComparison.OrdinalIgnoreCase) ? new(valeur, null) : new(null, valeur);
            }

            return null;
        }

        // Interfaces COM d'UIAutomationCore : seules les entrées utilisées sont typées, les autres tiennent leur place dans la vtable.
        [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IUIAutomation
        {
            void CompareElements();
            void CompareRuntimeIds();
            void GetRootElement();
            IUIAutomationElement ElementFromHandle(IntPtr hwnd);
            void ElementFromPoint();
            void GetFocusedElement();
            void GetRootElementBuildCache();
            void ElementFromHandleBuildCache();
            void ElementFromPointBuildCache();
            void GetFocusedElementBuildCache();
            void CreateTreeWalker();
            void ControlViewWalker();
            void ContentViewWalker();
            void RawViewWalker();
            void RawViewCondition();
            void ControlViewCondition();
            void ContentViewCondition();
            void CreateCacheRequest();
            void CreateTrueCondition();
            void CreateFalseCondition();
            IUIAutomationCondition CreatePropertyCondition(Int32 propriete, [MarshalAs(UnmanagedType.Struct)] Object valeur);
            IUIAutomationCondition CreatePropertyConditionEx(Int32 propriete, [MarshalAs(UnmanagedType.Struct)] Object valeur, Int32 drapeaux);
            IUIAutomationCondition CreateAndCondition(IUIAutomationCondition a, IUIAutomationCondition b);
        }

        [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IUIAutomationElement
        {
            void SetFocus();
            void GetRuntimeId();
            IUIAutomationElement FindFirst(Int32 portee, IUIAutomationCondition condition);
            IUIAutomationElementArray FindAll(Int32 portee, IUIAutomationCondition condition);
            void FindFirstBuildCache();
            void FindAllBuildCache();
            void BuildUpdatedCache();
            [return: MarshalAs(UnmanagedType.Struct)]
            Object GetCurrentPropertyValue(Int32 propriete);
            void GetCurrentPropertyValueEx();
            void GetCachedPropertyValue();
            void GetCachedPropertyValueEx();
            void GetCurrentPatternAs();
            void GetCachedPatternAs();
            [return: MarshalAs(UnmanagedType.IUnknown)]
            Object GetCurrentPattern(Int32 motif);
        }

        [ComImport, Guid("352ffba8-0973-437c-a61f-f64cafd81df9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IUIAutomationCondition
        {
        }

        [ComImport, Guid("14314595-b4bc-4055-95f2-58f2e42c9855"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IUIAutomationElementArray
        {
            Int32 Length { get; }

            IUIAutomationElement GetElement(Int32 index);
        }

        [ComImport, Guid("fb377fbe-8ea6-46d5-9c73-6499642d3059"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IUIAutomationInvokePattern
        {
            void Invoke();
        }

        [ComImport, Guid("94cf8058-9b8d-4ab9-8bfd-4cd0a33c8c70"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IUIAutomationTogglePattern
        {
            void Toggle();
        }

        [ComImport, Guid("88f4d42a-e881-459d-a77c-73bbbb7e02dc"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IUIAutomationScrollPattern
        {
            void Scroll();

            void SetScrollPercent(Double horizontal, Double vertical);
        }

        [ComImport, Guid("619be086-1f4e-4ee4-bafa-210128738730"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IUIAutomationExpandCollapsePattern
        {
            void Expand();

            void Collapse();

            Int32 CurrentExpandCollapseState { get; } // 1 : dépliée
        }
    }
}

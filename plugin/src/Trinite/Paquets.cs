namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;

    // Applis du Store (UWP) suspendues par Windows : réduites ou cachées, Windows les suspend (gestion du cycle de vie des
    // applications) et leur arbre UI Automation tombe à rien ; aucun bouton à cliquer. IPackageDebugSettings.Resume,
    // l'interface documentée des débogueurs, les remet en cours sans premier plan ni réglage persistant (repris de
    // NexusWindows, packages.py). Le Store réduit refusait ainsi la touche des mises à jour (25/10).
    public static class Paquets
    {
        private const Int32 Suspension = 2, Suspendu = 3; // PACKAGE_EXECUTION_STATE

        // Reprend l'appli de cette fenêtre (cadre ApplicationFrameHost) si elle est suspendue ; vrai si elle l'était.
        public static Boolean Reprendre(IntPtr cadre)
        {
            try
            {
                var noms = Processus(cadre).Select(NomDuPaquet).Where(n => n.Length > 0).Distinct().ToList();
                if (noms.Count == 0)
                {
                    return false;
                }

                var reglages = (IPackageDebugSettings)new PackageDebugSettings();
                var repris = false;
                foreach (var nom in noms)
                {
                    if (reglages.GetPackageExecutionState(nom, out var etat) == 0 && etat is Suspension or Suspendu && reglages.Resume(nom) == 0)
                    {
                        repris = true;
                    }
                }

                Marshal.ReleaseComObject(reglages);
                return repris;
            }
            catch (Exception e)
            {
                PluginLog.Warning($"reprise d'une appli suspendue : {e.Message}");
                return false;
            }
        }

        // Processus de l'appli d'un cadre UWP : ses fenêtres enfants d'un autre processus, et la CoreWindow de même titre
        // sortie du cadre (appli réduite).
        private static HashSet<UInt32> Processus(IntPtr cadre)
        {
            GetWindowThreadProcessId(cadre, out var hote);
            var pids = new HashSet<UInt32>();
            EnumChildWindows(cadre, (h, _) =>
            {
                GetWindowThreadProcessId(h, out var pid);
                if (pid != hote && pid != 0)
                {
                    pids.Add(pid);
                }

                return true;
            }, IntPtr.Zero);
            var titre = Uia.Titre(cadre);
            EnumWindows((h, _) =>
            {
                var classe = new StringBuilder(64);
                if (GetClassName(h, classe, classe.Capacity) > 0 && classe.ToString() == "Windows.UI.Core.CoreWindow" && Uia.Titre(h) == titre)
                {
                    GetWindowThreadProcessId(h, out var pid);
                    if (pid != 0)
                    {
                        pids.Add(pid);
                    }
                }

                return true;
            }, IntPtr.Zero);
            return pids;
        }

        private static String NomDuPaquet(UInt32 pid)
        {
            var processus = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
            if (processus == IntPtr.Zero)
            {
                return "";
            }

            try
            {
                var taille = 0U;
                if (GetPackageFullName(processus, ref taille, null) != 122) // ERROR_INSUFFICIENT_BUFFER ; sinon : pas de paquet
                {
                    return "";
                }

                var nom = new StringBuilder((Int32)taille);
                return GetPackageFullName(processus, ref taille, nom) == 0 ? nom.ToString() : "";
            }
            finally
            {
                CloseHandle(processus);
            }
        }

        private delegate Boolean Rappel(IntPtr h, IntPtr l);

        [DllImport("user32.dll")]
        private static extern Boolean EnumChildWindows(IntPtr parent, Rappel rappel, IntPtr l);

        [DllImport("user32.dll")]
        private static extern Boolean EnumWindows(Rappel rappel, IntPtr l);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern Int32 GetClassName(IntPtr h, StringBuilder classe, Int32 max);

        [DllImport("user32.dll")]
        private static extern UInt32 GetWindowThreadProcessId(IntPtr h, out UInt32 pid);

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(UInt32 acces, Boolean herite, UInt32 pid);

        [DllImport("kernel32.dll")]
        private static extern Boolean CloseHandle(IntPtr h);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern Int32 GetPackageFullName(IntPtr processus, ref UInt32 taille, StringBuilder nom);

        [ComImport, Guid("B1AEC16F-2383-4852-B0E9-8F0B1DC66B4D")]
        private class PackageDebugSettings
        {
        }

        [ComImport, Guid("F27C3930-8029-4AD1-94E3-3DBA417810C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPackageDebugSettings
        {
            [PreserveSig] Int32 EnableDebugging([MarshalAs(UnmanagedType.LPWStr)] String nom, [MarshalAs(UnmanagedType.LPWStr)] String debogueur, IntPtr environnement);
            [PreserveSig] Int32 DisableDebugging([MarshalAs(UnmanagedType.LPWStr)] String nom);
            [PreserveSig] Int32 Suspend([MarshalAs(UnmanagedType.LPWStr)] String nom);
            [PreserveSig] Int32 Resume([MarshalAs(UnmanagedType.LPWStr)] String nom);
            [PreserveSig] Int32 TerminateAllProcesses([MarshalAs(UnmanagedType.LPWStr)] String nom);
            [PreserveSig] Int32 SetTargetSessionId(UInt32 session);
            [PreserveSig] Int32 EnumerateBackgroundTasks([MarshalAs(UnmanagedType.LPWStr)] String nom, out UInt32 nombre, out IntPtr ids, out IntPtr noms);
            [PreserveSig] Int32 ActivateBackgroundTask(IntPtr id);
            [PreserveSig] Int32 StartServicing([MarshalAs(UnmanagedType.LPWStr)] String nom);
            [PreserveSig] Int32 StopServicing([MarshalAs(UnmanagedType.LPWStr)] String nom);
            [PreserveSig] Int32 StartSessionRedirection([MarshalAs(UnmanagedType.LPWStr)] String nom, UInt32 session);
            [PreserveSig] Int32 StopSessionRedirection([MarshalAs(UnmanagedType.LPWStr)] String nom);
            [PreserveSig] Int32 GetPackageExecutionState([MarshalAs(UnmanagedType.LPWStr)] String nom, out Int32 etat);
        }
    }
}

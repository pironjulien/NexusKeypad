namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.Diagnostics;
    using System.Linq;
    using System.Runtime.InteropServices;

    // Page 2 du pad : l'état du poste, lu sur place (aucun réseau, aucun processus lancé) et seulement quand la page est
    // affichée. Volume par l'API audio de Windows, processeur et mémoire par le noyau.
    // Volume : niveau réglé (0 à 1) ; Son : crête du signal en ce moment (0 à 1) ; null : pas de périphérique.
    // Mails, Messages : notifications en attente sur le téléphone (null : pas de téléphone).
    public sealed record Poste(Double? Volume, Boolean SonCoupe, Double Son, Double Processeur, Double Memoire)
    {
        public Int32? Mails { get; init; }
        public Int32? Messages { get; init; }
    }

    public static class Releve
    {
        private static readonly Object Verrou = new(); // animation et gestes relisent le poste
        private static (Int64 Repos, Int64 Total) _temps;
        private static Double _processeur;
        private static Int64 _lu;

        // Points audio (volume et crête) de la sortie par défaut, gardés : relus 8 fois par seconde, ils ne sont recréés
        // que toutes les 2 s (le périphérique par défaut peut changer). Une case par flux ; seul le son (0) sert depuis
        // que la touche du micro est retirée de la page 2.
        private static readonly (IAudioEndpointVolume Volume, IAudioMeterInformation Crete)?[] Points = new (IAudioEndpointVolume, IAudioMeterInformation)?[1];
        private static Int64 _points = Int64.MinValue / 2;

        // Un relevé complet : quelques appels COM et noyau, sans attente.
        public static Poste Lire()
        {
            lock (Verrou)
            {
                if (Environment.TickCount64 - _points > 2000)
                {
                    if (Points[0] is var (ancien, ancienneCrete))
                    {
                        Marshal.ReleaseComObject(ancien);
                        Marshal.ReleaseComObject(ancienneCrete);
                    }

                    Points[0] = Ouvrir(0);

                    _points = Environment.TickCount64;
                }

                var (volume, coupe, son) = Audio(0);
                return new(volume, coupe, son, Processeur(), Memoire());
            }
        }

        // Coupe ou rétablit le son (flux 0, la sortie par défaut) ; vrai si c'est fait.
        public static Boolean Basculer(Int32 flux)
        {
            try
            {
                lock (Verrou)
                {
                    // Les points gardés, ouverts au besoin (page 2 jamais affichée) : rien de neuf à libérer.
                    Points[flux] ??= Ouvrir(flux);
                    if (Points[flux] is not { Volume: var volume })
                    {
                        return false;
                    }

                    Marshal.ThrowExceptionForHR(volume.GetMute(out var coupe));
                    var contexte = Guid.Empty;
                    Marshal.ThrowExceptionForHR(volume.SetMute(!coupe, ref contexte));
                    return true;
                }
            }
            catch (Exception e)
            {
                PluginLog.Error(e, "son coupé ou rétabli");
                return false;
            }
        }

        // Volume et crête d'un flux par défaut (0 : sortie), par ses points gardés. Pas de périphérique :
        // (null, false, 0).
        private static (Double?, Boolean, Double) Audio(Int32 flux)
        {
            try
            {
                if (Points[flux] is not var (volume, crete))
                {
                    return (null, false, 0);
                }

                Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out var niveau));
                Marshal.ThrowExceptionForHR(volume.GetMute(out var coupe));
                Marshal.ThrowExceptionForHR(crete.GetPeakValue(out var pic));
                return (niveau, coupe, pic);
            }
            catch (Exception)
            {
                return (null, false, 0); // périphérique débranché ou en train de changer : rouvert au plus dans 2 s
            }
        }

        // Points audio du périphérique par défaut d'un flux ; null s'il n'y en a pas.
        private static (IAudioEndpointVolume Volume, IAudioMeterInformation Crete)? Ouvrir(Int32 flux)
        {
            IMMDeviceEnumerator enumerateur = null;
            IMMDevice peripherique = null;
            try
            {
                enumerateur = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                if (enumerateur.GetDefaultAudioEndpoint(flux, 0, out peripherique) != 0) // 0 : eConsole
                {
                    return null;
                }

                var idVolume = typeof(IAudioEndpointVolume).GUID;
                var idCrete = typeof(IAudioMeterInformation).GUID;
                Marshal.ThrowExceptionForHR(peripherique.Activate(ref idVolume, 1, IntPtr.Zero, out var v)); // 1 : CLSCTX_INPROC_SERVER
                Marshal.ThrowExceptionForHR(peripherique.Activate(ref idCrete, 1, IntPtr.Zero, out var m));
                return ((IAudioEndpointVolume)v, (IAudioMeterInformation)m);
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                // Seuls les points servent ensuite : énumérateur et périphérique libérés aussitôt (relecture d'Antigravity).
                if (peripherique is not null)
                {
                    Marshal.ReleaseComObject(peripherique);
                }

                if (enumerateur is not null)
                {
                    Marshal.ReleaseComObject(enumerateur);
                }
            }
        }

        // Part du processeur occupée depuis le relevé précédent (au plus toutes les 0,5 s, sinon la dernière valeur).
        private static Double Processeur()
        {
            if (Environment.TickCount64 - _lu < 500 || !GetSystemTimes(out var repos, out var noyau, out var utilisateur))
            {
                return _processeur;
            }

            var total = noyau + utilisateur; // le temps noyau compte déjà le repos
            var (dRepos, dTotal) = (repos - _temps.Repos, total - _temps.Total);
            if (_temps.Total != 0 && dTotal > 0)
            {
                _processeur = Math.Clamp(1 - (dRepos / (Double)dTotal), 0, 1);
            }

            _temps = (repos, total);
            _lu = Environment.TickCount64;
            return _processeur;
        }

        private static Double Memoire()
        {
            var etat = new MEMORYSTATUSEX { dwLength = (UInt32)Marshal.SizeOf<MEMORYSTATUSEX>() };
            return GlobalMemoryStatusEx(ref etat) ? etat.dwMemoryLoad / 100.0 : 0;
        }

        // Touches multimédia de Windows (volume moins, plus) : Windows règle le volume et l'affiche.
        public static void Media(Byte touche)
        {
            keybd_event(touche, 0, 1, UIntPtr.Zero); // 1 : KEYEVENTF_EXTENDEDKEY
            keybd_event(touche, 0, 3, UIntPtr.Zero); // + KEYEVENTF_KEYUP
        }

        public const Byte VolumeMoins = 0xAE;
        public const Byte VolumePlus = 0xAF;

        public static void Lancer(String cible)
        {
            try
            {
                Process.Start(new ProcessStartInfo(cible) { UseShellExecute = true });
            }
            catch (Exception e)
            {
                PluginLog.Error(e, $"lancement de {cible}");
            }
        }

        // Touche des mises à jour : les deux recherches d'un appui, sans rien amener au premier plan.
        // Windows Update : UsoClient StartInteractiveScan (sans droits d'administrateur). Microsoft Store : son API de
        // recherche est refusée aux applis de bureau (E_ACCESSDENIED) et winget ne voit pas ses applis ; sa page
        // « Téléchargements et mises à jour » est ouverte (elle s'ouvre derrière), son bouton cliqué par UI Automation
        // (Invoke, fenêtre derrière), puis la fenêtre réduite.
        public static void RechercherMisesAJour()
        {
            try
            {
                using var uso = Process.Start(new ProcessStartInfo("UsoClient.exe", "StartInteractiveScan") { UseShellExecute = false, CreateNoWindow = true });
            }
            catch (Exception e)
            {
                PluginLog.Error(e, "recherche Windows Update");
            }

            Lancer("ms-windows-store://downloadsandupdates");
            String[] boutons = ["Rechercher les mises à jour", "Obtenir les mises à jour", "Get updates", "Check for updates"];
            for (var i = 0; i < 40; i++) // la page arrive en quelques secondes, son bouton après elle
            {
                System.Threading.Thread.Sleep(500);
                var store = Uia.Fenetres("ApplicationFrameHost").FirstOrDefault(h => Uia.Titre(h) == "Microsoft Store");
                if (store != IntPtr.Zero && Uia.CliquerDans(store, boutons))
                {
                    System.Threading.Thread.Sleep(1500); // la recherche part avant que la fenêtre ne soit rangée
                    Gestes.Reduire(store);
                    PluginLog.Info("mises à jour : Windows Update et Store lancés");
                    return;
                }
            }

            PluginLog.Warning("mises à jour : bouton du Store introuvable en 20 s (Windows Update lancé)");
        }

        [DllImport("user32.dll")]
        private static extern void keybd_event(Byte bVk, Byte bScan, UInt32 dwFlags, UIntPtr dwExtraInfo);

        [DllImport("kernel32.dll")]
        private static extern Boolean GetSystemTimes(out Int64 repos, out Int64 noyau, out Int64 utilisateur);

        [DllImport("kernel32.dll")]
        private static extern Boolean GlobalMemoryStatusEx(ref MEMORYSTATUSEX etat);

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public UInt32 dwLength;
            public UInt32 dwMemoryLoad;
            public UInt64 ullTotalPhys;
            public UInt64 ullAvailPhys;
            public UInt64 ullTotalPageFile;
            public UInt64 ullAvailPageFile;
            public UInt64 ullTotalVirtual;
            public UInt64 ullAvailVirtual;
            public UInt64 ullAvailExtendedVirtual;
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumerator
        {
        }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            Int32 EnumAudioEndpoints(Int32 flux, Int32 etats, out IntPtr liste);

            [PreserveSig]
            Int32 GetDefaultAudioEndpoint(Int32 flux, Int32 role, out IMMDevice peripherique);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig]
            Int32 Activate(ref Guid id, Int32 contexte, IntPtr parametres, [MarshalAs(UnmanagedType.IUnknown)] out Object interfaceObtenue);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            [PreserveSig] Int32 RegisterControlChangeNotify(IntPtr rappel);
            [PreserveSig] Int32 UnregisterControlChangeNotify(IntPtr rappel);
            [PreserveSig] Int32 GetChannelCount(out UInt32 canaux);
            [PreserveSig] Int32 SetMasterVolumeLevel(Single niveau, ref Guid contexte);
            [PreserveSig] Int32 SetMasterVolumeLevelScalar(Single niveau, ref Guid contexte);
            [PreserveSig] Int32 GetMasterVolumeLevel(out Single niveau);
            [PreserveSig] Int32 GetMasterVolumeLevelScalar(out Single niveau);
            [PreserveSig] Int32 SetChannelVolumeLevel(UInt32 canal, Single niveau, ref Guid contexte);
            [PreserveSig] Int32 SetChannelVolumeLevelScalar(UInt32 canal, Single niveau, ref Guid contexte);
            [PreserveSig] Int32 GetChannelVolumeLevel(UInt32 canal, out Single niveau);
            [PreserveSig] Int32 GetChannelVolumeLevelScalar(UInt32 canal, out Single niveau);
            [PreserveSig] Int32 SetMute([MarshalAs(UnmanagedType.Bool)] Boolean coupe, ref Guid contexte);
            [PreserveSig] Int32 GetMute([MarshalAs(UnmanagedType.Bool)] out Boolean coupe);
        }

        [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioMeterInformation
        {
            [PreserveSig] Int32 GetPeakValue(out Single crete);
        }
    }
}

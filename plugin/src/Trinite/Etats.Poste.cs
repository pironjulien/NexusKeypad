namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    // Le poste : écran éteint, inactivité de l'utilisateur, appli au premier plan.
    public sealed partial class Observateur
    {
        // Écran éteint par Windows (inactif depuis son délai d'extinction ; jamais si ce délai est « jamais ») : le pad
        // s'éteint avec lui (aucun réglage Logi ne l'empêche, 24/10) ; rien n'est plus dessiné pour lui.
        private Boolean EcranEteint() => Inactif() > this.DelaiEcran();

        // Poste verrouillé : le bureau de saisie n'est plus celui de l'utilisateur, il ne peut ni être ouvert ni activé.
        // (Chercher le processus LogonUI énumérait tous les processus, une vingtaine de ms chaque seconde.)
        private static Boolean Verrouille()
        {
            var bureau = OpenInputDesktop(0, false, DESKTOP_SWITCHDESKTOP);
            if (bureau == IntPtr.Zero)
            {
                return true;
            }

            try
            {
                return !SwitchDesktop(bureau);
            }
            finally
            {
                CloseDesktop(bureau);
            }
        }

        // Appli d'une IA lancée : le processus déjà trouvé vit encore, sinon une recherche par nom (le plus ancien, celui
        // de l'appli, pas d'un onglet), au plus toutes les 3 s quand elle est fermée. Chercher par nom énumère tous les
        // processus : 70 ms chaque seconde pour les trois quand c'était fait à chaque tour.
        // Lancée, ou vue il y a moins de 3 s : un processus d'Antigravity qui se relance ne la fait plus passer « fermée »
        // (tête endormie une seconde, vu le 23/10).
        private Boolean Lancee(Int32 agent)
        {
            if (this.LanceeMaintenant(agent))
            {
                this._vueLancee[agent] = DateTime.UtcNow;
                return true;
            }

            return DateTime.UtcNow - this._vueLancee[agent] < TimeSpan.FromSeconds(3);
        }

        private Boolean LanceeMaintenant(Int32 agent)
        {
            if (this._lancees[agent] is { } connu)
            {
                try
                {
                    if (!connu.HasExited)
                    {
                        return true;
                    }
                }
                catch (Exception)
                {
                    // accès refusé : recherché à nouveau
                }

                connu.Dispose();
                this._lancees[agent] = null;
            }
            else if (DateTime.UtcNow - this._recherche[agent] < TimeSpan.FromSeconds(3))
            {
                return false;
            }

            this._recherche[agent] = DateTime.UtcNow;
            var trouves = Process.GetProcessesByName(Processus[agent]);
            Process aine = null;
            foreach (var p in trouves)
            {
                try
                {
                    if (aine is null || p.StartTime < aine.StartTime)
                    {
                        (aine, var lache) = (p, aine);
                        lache?.Dispose();
                        continue;
                    }
                }
                catch (Exception)
                {
                    // processus sorti entre-temps
                }

                p.Dispose();
            }

            this._lancees[agent] = aine;
            return aine is not null;
        }

        // Temps depuis la dernière touche ou le dernier mouvement de souris de l'utilisateur.
        private static TimeSpan Inactif()
        {
            var info = new LASTINPUTINFO { cbSize = (UInt32)Marshal.SizeOf<LASTINPUTINFO>() };
            return GetLastInputInfo(ref info) ? TimeSpan.FromMilliseconds((UInt32)Environment.TickCount - info.dwTime) : TimeSpan.Zero;
        }

        // Délai d'extinction de l'écran du mode d'alimentation actif (secteur, ou batterie si le PC est sur batterie), lu
        // auprès de Windows ; 0 = jamais. Relu chaque minute : l'utilisateur peut le changer. (La sortie de powercfg, traduite,
        // ne disait pas « secteur » mais « courant alternatif » : le pad prenait 10 min par défaut, écran jamais éteint.)
        private TimeSpan DelaiEcran()
        {
            if (DateTime.UtcNow - this._delaiLu < TimeSpan.FromMinutes(1))
            {
                return this._delaiEcran;
            }

            this._delaiLu = DateTime.UtcNow;
            try
            {
                if (PowerGetActiveScheme(IntPtr.Zero, out var schema) == 0)
                {
                    try
                    {
                        var sousGroupe = SousGroupeVideo;
                        var reglage = ExtinctionVideo;
                        var batterie = GetSystemPowerStatus(out var etat) && etat.ACLineStatus == 0;
                        var lu = batterie
                            ? PowerReadDCValueIndex(IntPtr.Zero, schema, ref sousGroupe, ref reglage, out var secondes)
                            : PowerReadACValueIndex(IntPtr.Zero, schema, ref sousGroupe, ref reglage, out secondes);
                        if (lu == 0)
                        {
                            this._delaiEcran = secondes == 0 ? TimeSpan.MaxValue : TimeSpan.FromSeconds(secondes);
                        }
                    }
                    finally
                    {
                        LocalFree(schema);
                    }
                }
            }
            catch (Exception e)
            {
                PluginLog.Error(e, "délai d'extinction de l'écran");
            }

            return this._delaiEcran;
        }

        private static Guid SousGroupeVideo = new("7516b95f-f776-4464-8c53-06167f40cc99"); // GUID_VIDEO_SUBGROUP
        private static Guid ExtinctionVideo = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e"); // GUID_VIDEO_POWERDOWN_TIMEOUT

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public Byte ACLineStatus;
            public Byte BatteryFlag;
            public Byte BatteryLifePercent;
            public Byte SystemStatusFlag;
            public UInt32 BatteryLifeTime;
            public UInt32 BatteryFullLifeTime;
        }

        [DllImport("powrprof.dll")]
        private static extern UInt32 PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

        [DllImport("powrprof.dll")]
        private static extern UInt32 PowerReadACValueIndex(IntPtr rootPowerKey, IntPtr schemeGuid, ref Guid subGroupOfPowerSettingsGuid, ref Guid powerSettingGuid, out UInt32 acValueIndex);

        [DllImport("powrprof.dll")]
        private static extern UInt32 PowerReadDCValueIndex(IntPtr rootPowerKey, IntPtr schemeGuid, ref Guid subGroupOfPowerSettingsGuid, ref Guid powerSettingGuid, out UInt32 dcValueIndex);

        [DllImport("kernel32.dll")]
        private static extern Boolean GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr mem);

        public static String PremierPlan()
        {
            try
            {
                GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
                return pid == 0 ? "" : Process.GetProcessById((Int32)pid).ProcessName;
            }
            catch (Exception)
            {
                return "";
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public UInt32 cbSize;
            public UInt32 dwTime;
        }

        [DllImport("user32.dll")]
        private static extern Boolean GetLastInputInfo(ref LASTINPUTINFO plii);

        private const UInt32 DESKTOP_SWITCHDESKTOP = 0x0100;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr OpenInputDesktop(UInt32 dwFlags, Boolean fInherit, UInt32 dwDesiredAccess);

        [DllImport("user32.dll")]
        private static extern Boolean SwitchDesktop(IntPtr hDesktop);

        [DllImport("user32.dll")]
        private static extern Boolean CloseDesktop(IntPtr hDesktop);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern UInt32 GetWindowThreadProcessId(IntPtr hWnd, out UInt32 processId);
    }
}

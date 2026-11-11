namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.Runtime.InteropServices;

    // Ligne de commande d'un processus et règle commune « ce processus n'est pas l'appli elle-même ». Partagée par
    // l'observateur (tête éveillée) et les lanceurs (état de la connexion) : seuls les lanceurs comptaient encore les
    // auxiliaires Node d'Antigravity, et le pad montrait « Relancer » sur Antigravity fermé (11/11).
    internal static class LigneDeCommande
    {
        // Un onglet ou un utilitaire d'Electron (« --type= »), ou un auxiliaire lancé avec l'exécutable de l'appli en
        // mode Node (« --import= » : compose-preview, device-hub et le sidecar du relais d'Antigravity, qui survivent à la
        // fermeture de sa fenêtre ; tête éveillée appli fermée le 27/10).
        internal static Boolean Auxiliaire(String ligne) =>
            ligne.Contains(" --type=", StringComparison.Ordinal) || ligne.Contains(" --import=", StringComparison.Ordinal);

        internal static Boolean Auxiliaire(Int32 pid) => Auxiliaire(Lire(pid));

        // Lue par Windows (sans WMI) ; "" si illisible.
        internal static String Lire(Int32 pid)
        {
            var processus = OpenProcess(0x1000, false, (UInt32)pid); // PROCESS_QUERY_LIMITED_INFORMATION
            if (processus == IntPtr.Zero)
            {
                return "";
            }

            try
            {
                _ = NtQueryInformationProcess(processus, ProcessCommandLineInformation, IntPtr.Zero, 0, out var taille); // taille seule
                if (taille == 0)
                {
                    return "";
                }

                var tampon = Marshal.AllocHGlobal((Int32)taille);
                try
                {
                    if (NtQueryInformationProcess(processus, ProcessCommandLineInformation, tampon, taille, out _) != 0)
                    {
                        return "";
                    }

                    // UNICODE_STRING : longueur en octets, puis l'adresse du texte (alignée sur un pointeur).
                    return Marshal.PtrToStringUni(Marshal.ReadIntPtr(tampon, IntPtr.Size), (UInt16)Marshal.ReadInt16(tampon) / 2) ?? "";
                }
                finally
                {
                    Marshal.FreeHGlobal(tampon);
                }
            }
            finally
            {
                CloseHandle(processus);
            }
        }

        private const Int32 ProcessCommandLineInformation = 60;

        [DllImport("ntdll.dll")]
        private static extern Int32 NtQueryInformationProcess(IntPtr processus, Int32 classe, IntPtr information, UInt32 taille, out UInt32 tailleRendue);

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(UInt32 acces, Boolean herite, UInt32 pid);

        [DllImport("kernel32.dll")]
        private static extern Boolean CloseHandle(IntPtr h);
    }
}

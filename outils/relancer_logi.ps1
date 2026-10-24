# Relance l'agent Logi Options+ (pad Trinité et raccourcis du clavier Logitech), détaché de l'appli qui lance ce script.
#
# Pourquoi détaché : lancé par Start-Process depuis une session de Claude, l'agent entre dans l'objet tâche (job) de
# l'appli Claude et meurt avec elle (23/10 : pad sur le logo Logi et raccourcis du clavier perdus à la fermeture de
# Claude). CREATE_BREAKAWAY_FROM_JOB l'en sort.
# Le plugin est installé en paquet (Plugins\TrinitePad) : il se charge quel que soit le lanceur (avec l'ancien lien .link,
# l'agent lancé par WMI ou une tâche planifiée démarrait sans charger le plugin).
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$agent = 'C:\Program Files\LogiOptionsPlus\logioptionsplus_agent.exe'

Add-Type @'
using System; using System.Runtime.InteropServices;
public static class LanceurDetache {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO { public int cb; public string r, d, t; public int x, y, w, h, xc, yc, fa, fl; public short sw, cb2; public IntPtr r2, i, o, e; }
    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION { public IntPtr hp, ht; public int pid, tid; }
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcess(string app, string cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr h);
    // CREATE_BREAKAWAY_FROM_JOB | CREATE_NEW_PROCESS_GROUP | DETACHED_PROCESS
    public static int Lancer(string ligne, string dossier) {
        var si = new STARTUPINFO(); si.cb = Marshal.SizeOf(si);
        PROCESS_INFORMATION pi;
        // Sortie du job refusée : 0, le script passe par WMI.
        if (!CreateProcess(null, ligne, IntPtr.Zero, IntPtr.Zero, false, 0x01000000 | 0x00000200 | 0x00000008, IntPtr.Zero, dossier, ref si, out pi))
            return 0;
        CloseHandle(pi.hp); CloseHandle(pi.ht);
        return pi.pid;
    }
}
'@

Get-Process LogiPluginService, LogiPluginServiceExt, logioptionsplus_agent -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
$debut = Get-Date
$processus = [LanceurDetache]::Lancer("`"$agent`" /minimized", (Split-Path $agent))
if ($processus) {
    Write-Host "Agent Logi relancé (pid $processus), détaché."
} else {
    # Le job de l'appli lanceuse interdit d'en sortir : WMI crée le processus lui-même, hors de ce job.
    $r = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
        CommandLine = "`"$agent`" /minimized"; CurrentDirectory = (Split-Path $agent) }
    if ($r.ReturnValue -ne 0) { throw "Relance de Logi par WMI refusée (code $($r.ReturnValue))." }
    Write-Host "Agent Logi relancé par WMI (pid $($r.ProcessId)), hors du job de l'appli lanceuse."
}

$journal = Join-Path $env:LOCALAPPDATA 'Logi\LogiPluginService\Logs\plugin_logs\TrinitePad.log'
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 2
    if ((Test-Path $journal) -and (Get-Item $journal).LastWriteTime -gt $debut) {
        Write-Host 'Plugin du pad chargé.'
        exit 0
    }
}
Write-Warning 'Plugin du pad pas chargé en 60 s (raccourcis du clavier rétablis, pad sur le logo Logi).'
exit 1

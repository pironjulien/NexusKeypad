# Relance l'agent Logi Options+ (pad Trinité et raccourcis du clavier Logitech), détaché de l'appli qui lance ce script.
#
# Pourquoi WMI : lancé depuis une session de Claude, l'agent appartient à l'appli Claude (objet tâche et paquet MSIX)
# et meurt avec elle (23/10, puis 30/10 malgré CREATE_BREAKAWAY_FROM_JOB : pad noir au redémarrage de Claude).
# Win32_Process.Create fait créer le processus par le service WMI, hors de tout job et de tout paquet de l'appelant.
# Le plugin est installé en paquet (Plugins\NexusKeypad) : il se charge quel que soit le lanceur.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$agent = 'C:\Program Files\LogiOptionsPlus\logioptionsplus_agent.exe'

Get-Process LogiPluginService, LogiPluginServiceExt, logioptionsplus_agent -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
$debut = Get-Date
$r = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
    CommandLine = "`"$agent`" /minimized"; CurrentDirectory = (Split-Path $agent) }
if ($r.ReturnValue -ne 0) { throw "Relance de Logi par WMI refusée (code $($r.ReturnValue))." }
Write-Host "Agent Logi relancé par WMI (pid $($r.ProcessId)), indépendant de l'appli lanceuse."

$journal = Join-Path $env:LOCALAPPDATA 'Logi\LogiPluginService\Logs\plugin_logs\NexusKeypad.log'
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 2
    if ((Test-Path $journal) -and (Get-Item $journal).LastWriteTime -gt $debut) {
        Write-Host 'Plugin du pad chargé.'
        exit 0
    }
}
Write-Warning 'Plugin du pad pas chargé en 60 s (raccourcis du clavier rétablis, pad sur le logo Logi).'
exit 1

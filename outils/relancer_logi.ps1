# Relance uniquement le service de plugins Logi par son outil officiel, sans arrêter l'agent clavier/souris.
#
# Pourquoi WMI : lancé depuis une session de Claude, un processus enfant appartient à l'appli Claude (objet tâche et paquet MSIX)
# et meurt avec elle (23/10, puis 30/10 malgré CREATE_BREAKAWAY_FROM_JOB : pad noir au redémarrage de Claude).
# get.ps1 fait créer le service de plugins par WMI, hors de tout job et de tout paquet de l'appelant.
# Le plugin est installé en paquet (Plugins\NexusKeypad) : il se charge quel que soit le lanceur.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$installeur = Join-Path (Split-Path $PSScriptRoot) 'get.ps1'
& $installeur -LogiService stop
$debut = Get-Date
& $installeur -LogiService start
Write-Host 'Service de plugins Logi relancé par son outil officiel, détaché par WMI ; agent clavier/souris préservé.'

$journal = Join-Path $env:LOCALAPPDATA 'Logi\LogiPluginService\Logs\plugin_logs\NexusKeypad.log'
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 2
    if ((Test-Path $journal) -and (Get-Item $journal).LastWriteTime -gt $debut) {
        Write-Host 'Plugin du pad chargé.'
        exit 0
    }
}
Write-Warning 'Plugin du pad pas chargé en 60 s ; agent clavier/souris inchangé.'
exit 1

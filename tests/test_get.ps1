# Essai de get.ps1 (installeur de la version complète) dans un dossier de test : jamais le service Logi ni les réglages
# réels. Paquet fait de la compilation de l'édition Nexus, profil général minimal à une page ; installation non
# interactive (NEXUS_PACK), pose des 18 touches avec sauvegarde, réinstallation, retrait. Appelé par test.ps1.
param([Parameter(Mandatory)] [string]$Compilation, [Parameter(Mandatory)] [string]$Travail)
$ErrorActionPreference = 'Stop'
$racine = Split-Path $PSScriptRoot
if (Test-Path $Travail) { Remove-Item $Travail -Recurse -Force }
$logi = Join-Path $Travail 'logi'
$profils = Join-Path $logi 'Applications\Loupedeck70\@_defaultwin'
New-Item -ItemType Directory -Force (Join-Path $profils 'Profiles\ESSAI') | Out-Null
Set-Content (Join-Path $profils 'ApplicationInfo.json') '{"name": "@_defaultwin", "defaultProfileName": "ESSAI"}' -Encoding UTF8
$profil = Join-Path $profils 'Profiles\ESSAI\ProfileInfo.json'
$controles = (0..8 | ForEach-Object { "{""controlId"": $_, ""pressAction"": null, ""rotateAction"": null}" }) -join ', '
Set-Content $profil "{""name"": ""ESSAI"", ""macroCommands"": [{""name"": ""garde""}], ""layout"": {""layoutModes"": [{""workspaces"": [{""pressPages"": [{""name"": ""P1"", ""displayName"": ""Page (1)"", ""controls"": [$controles]}]}]}]}}" -Encoding UTF8
$paquet = Join-Path $Travail 'NexusKeypad_essai.lplug4'
Compress-Archive -Path (Join-Path $Compilation '*') -DestinationPath $paquet

$env:NEXUS_KEYPAD_LOGI = $logi
$env:NEXUS_KEYPAD_SOURCE = $paquet
$shell = (Get-Process -Id $PID).Path
function Lancer([string[]]$Arguments) {
    $sortie = & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $racine 'get.ps1') @Arguments 2>&1
    if ($LASTEXITCODE) { throw "get.ps1 $Arguments : code $LASTEXITCODE`n$($sortie -join "`n")" }
    return $sortie
}
function Actions() {
    $j = Get-Content $profil -Raw | ConvertFrom-Json
    return @($j.layout.layoutModes[0].workspaces[0].pressPages | ForEach-Object { $_.controls } | ForEach-Object { $_.pressAction } | Where-Object { $_ })
}
try {
    # Garde-fou : un paquet de test sans dossier de test est refusé (incident du 25/10 : un essai sans dossier de test a
    # installé dans le vrai dossier des plugins). Paquet inexistant : même un garde-fou cassé n'installerait rien.
    Remove-Item Env:NEXUS_KEYPAD_LOGI
    $env:NEXUS_KEYPAD_SOURCE = Join-Path $Travail 'absent.lplug4'
    $sortie = & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $racine 'get.ps1') 2>&1
    if ($LASTEXITCODE -ne 1 -or -not ($sortie -match 'refusing to touch the real Logi folder')) { throw "garde-fou : code $LASTEXITCODE`n$($sortie -join "`n")" }
    $env:NEXUS_KEYPAD_LOGI = $logi
    $env:NEXUS_KEYPAD_SOURCE = $paquet

    $env:NEXUS_PACK = '1'
    $sortie = Lancer @()
    Remove-Item Env:NEXUS_PACK
    if ($sortie[-1] -ne 'NEXUS_PACK_RESULT Plugin=installed; Keys=guided; Live states=asked by the plugin') { throw "pack : $($sortie[-1])" }
    if (-not (Test-Path (Join-Path $logi 'Plugins\NexusKeypad\bin\NexusKeypadPlugin.dll'))) { throw 'plugin absent du dossier des plugins' }
    if ((Actions).Count) { throw 'pack : des touches ont été posées sans question' }

    $null = Lancer @('-Keys', 'yes')
    $actions = Actions
    if ($actions.Count -ne 18 -or $actions[17] -ne '$NexusKeypad___Loupedeck.TrinitePlugin.ToucheCommand___t18') { throw "touches : $($actions -join ', ')" }
    $sauvegarde = Join-Path (Split-Path $profil) 'ProfileInfo.before-nexuskeypad.json'
    if ((Get-Content $sauvegarde -Raw) -match 'NexusKeypad') { throw 'la sauvegarde contient déjà les touches' }
    if ((Get-Content $profil -Raw | ConvertFrom-Json).macroCommands[0].name -ne 'garde') { throw 'le reste du profil a changé' }
    $null = Lancer @('-Keys', 'yes')  # mise à jour : sauvegarde gardée telle quelle
    if ((Get-Content $sauvegarde -Raw) -match 'NexusKeypad') { throw 'sauvegarde écrasée à la réinstallation' }

    $null = Lancer @('-Remove')
    if (Test-Path (Join-Path $logi 'Plugins\NexusKeypad')) { throw 'plugin encore là après le retrait' }
    if ((Actions).Count) { throw 'touches encore là après le retrait' }
} finally {
    Remove-Item Env:NEXUS_KEYPAD_LOGI, Env:NEXUS_KEYPAD_SOURCE, Env:NEXUS_PACK -ErrorAction SilentlyContinue
    Remove-Item $Travail -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host 'get.ps1 : installation, touches et retrait tiennent.'

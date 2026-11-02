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
Set-Content $profil "{""name"": ""ESSAI"", ""displayName"": ""Default General Profile"", ""description"": """", ""macroCommands"": [{""name"": ""garde""}], ""layout"": {""layoutModes"": [{""workspaces"": [{""pressPages"": [{""name"": ""P1"", ""displayName"": ""Page (1)"", ""controls"": [$controles]}]}]}]}}" -Encoding UTF8
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
function Actif() { (Get-Content (Join-Path $profils 'ApplicationInfo.json') -Raw | ConvertFrom-Json).defaultProfileName }
function Nexus() {
    @(Get-ChildItem (Join-Path $profils 'Profiles') -Directory | Where-Object {
        (Get-Content (Join-Path $_.FullName 'ProfileInfo.json') -Raw | ConvertFrom-Json).displayName -ceq 'NexusKeypad' })
}
function Actions([string]$Fichier = $profil) {
    $j = Get-Content $Fichier -Raw | ConvertFrom-Json
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
    if ($sortie[-1] -ne 'NEXUS_PACK_RESULT Plugin=installed; Keys=guided; Live states=asked by the plugin; AI connections=left (test folder)') { throw "pack : $($sortie[-1])" }
    if (-not (Test-Path (Join-Path $logi 'Plugins\NexusKeypad\bin\NexusKeypadPlugin.dll'))) { throw 'plugin absent du dossier des plugins' }
    if ((Actions).Count) { throw 'pack : des touches ont été posées sans question' }

    # Touches sur un profil « NexusKeypad » à part, rendu actif ; le profil de l'utilisateur reste tel quel.
    $origine = Get-Content $profil -Raw
    $null = Lancer @('-Keys', 'yes')
    $nexus = Nexus
    if ($nexus.Count -ne 1 -or (Actif) -cne $nexus[0].Name) { throw "profil NexusKeypad absent ou inactif ($(Actif))" }
    $fichierNexus = Join-Path $nexus[0].FullName 'ProfileInfo.json'
    $actions = Actions $fichierNexus
    if ($actions.Count -ne 18 -or $actions[17] -ne '$NexusKeypad___Loupedeck.TrinitePlugin.ToucheCommand___t18') { throw "touches : $($actions -join ', ')" }
    if ((Get-Content $fichierNexus -Raw | ConvertFrom-Json).macroCommands[0].name -ne 'garde') { throw 'le reste du profil copié a changé' }
    if ((Get-Content $profil -Raw) -cne $origine) { throw 'le profil de l''utilisateur a été modifié' }
    $null = Lancer @('-Keys', 'yes')  # mise à jour : le même profil, pas un second
    if ((Nexus).Count -ne 1 -or (Get-Content $profil -Raw) -cne $origine) { throw 'réinstallation : second profil ou profil d''origine modifié' }
    # Mise à jour par NexusSystem, touches déjà posées : ni question ni consigne de placement.
    $env:NEXUS_PACK = '1'
    $sortie = Lancer @()
    Remove-Item Env:NEXUS_PACK
    if ($sortie[-1] -notmatch 'Keys=already placed' -or ($sortie -match 'drag Key 1')) { throw "mise à jour : $($sortie -join "`n")" }
    if ((Actions $fichierNexus).Count -ne 18) { throw 'mise à jour : touches modifiées' }

    $null = Lancer @('-Remove')
    if (Test-Path (Join-Path $logi 'Plugins\NexusKeypad')) { throw 'plugin encore là après le retrait' }
    if ((Nexus).Count -or (Actif) -cne 'ESSAI' -or (Get-Content $profil -Raw) -cne $origine) { throw 'retrait : profil NexusKeypad restant ou profil d''origine pas rendu' }

    # Ancienne version : touches posées sur le profil de l'utilisateur, avec sa sauvegarde unique. Rendu tel qu'avant.
    $null = Lancer @('-Keys', 'no')
    $sauvegarde = Join-Path (Split-Path $profil) 'ProfileInfo.before-nexuskeypad.json'
    Copy-Item $profil $sauvegarde
    & (Join-Path $logi 'Plugins\NexusKeypad\bin\nexuskeypad-hook.exe') keys $profil NexusKeypad | Out-Null
    if ((Actions).Count -ne 18) { throw 'ancienne pose impossible à reproduire' }
    $null = Lancer @('-Keys', 'yes')
    if ((Get-Content $profil -Raw) -cne $origine -or (Test-Path $sauvegarde)) { throw 'ancienne pose : profil d''origine pas rendu depuis sa sauvegarde' }
    if ((Actions (Join-Path (Nexus)[0].FullName 'ProfileInfo.json')).Count -ne 18) { throw 'ancienne pose : touches absentes du profil NexusKeypad' }
    $null = Lancer @('-Remove')
} finally {
    Remove-Item Env:NEXUS_KEYPAD_LOGI, Env:NEXUS_KEYPAD_SOURCE, Env:NEXUS_PACK -ErrorAction SilentlyContinue
    Remove-Item $Travail -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host 'get.ps1 : installation, touches et retrait tiennent.'

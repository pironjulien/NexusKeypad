# Ready profile (.lp5) for a Logi Marketplace edition, shared beside the plugin (GitHub release), never inside it:
# Logitech's marketplace team (2026-11-04) confirmed that a universal plugin cannot ship a default profile, and suggested
# sharing it separately. Users import it in Logi Options+ (keypad's general profile bar, "+", import a profile): the keys
# are already in place, no dragging. The file mirrors the structure of Logi's own built-in .lp5 profiles (zip:
# ApplicationInfo.json, ProfileInfo.json, metadata/), with the keypad's general application (@_defaultwin) and only this
# plugin's keys; nothing else of the user's setup is touched.
#   -Edition Marketplace : AI Coding Agents, page 1 = Key 1..9.
#   -Edition Poste       : PC Dashboard, page 1 = Key 1..8; the last key is left for the Website action (its address is
#                          typed in Options+, there is no default site).
#   -Edition Complete    : AI Coding Agents + PC, page 1 = Key 1..9, page 2 = Key 10..17, last key left for Website.
# Output: -Sortie (a .lp5 path). Deterministic: the same edition and version always give the same bytes.
#Requires -Version 7
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('Marketplace', 'Poste', 'Complete')] [string] $Edition,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Sortie
)
$ErrorActionPreference = 'Stop'

$definition = @{
    Marketplace = @{ Plugin = 'TrinitePad'; Nom = 'NexusKeypad – AI Coding Agents'; Pages = @(, (1..9)) }
    Poste       = @{ Plugin = 'PcDashboard'; Nom = 'NexusKeypad – PC Dashboard'; Pages = @(, (1..8)) }
    Complete    = @{ Plugin = 'AiAgentsPc'; Nom = 'NexusKeypad – AI Coding Agents + PC'; Pages = @((1..9), (10..17)) }
}[$Edition]
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version invalide : $Version" }

# Stable identifiers derived from the edition: importing a newer version is recognized as the same profile.
function Get-Id([string] $Graine) {
    $hash = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes("NexusKeypad/$Edition/$Graine"))
    return [Convert]::ToHexString($hash, 0, 16)
}
$profil = Get-Id 'profile'
$espace = Get-Id 'workspace'

$controle = 'Loupedeck.Service.Devices.Loupedeck7Devices.ProfileLayoutControl7, LoupedeckService'
$pages = for ($p = 0; $p -lt $definition.Pages.Count; $p++) {
    $touches = @($definition.Pages[$p])
    [ordered]@{
        '$type'       = 'Loupedeck.Service.Devices.Loupedeck7Devices.ProfileLayoutPage7, LoupedeckService'
        name          = Get-Id "page$p"
        displayName   = "Page ($($p + 1))"
        description   = $null
        controls      = @(for ($c = 0; $c -lt $touches.Count; $c++) {
                [ordered]@{
                    '$type'      = $controle
                    controlId    = $c
                    pressAction  = '${0}___Loupedeck.TrinitePlugin.ToucheCommand___t{1}' -f $definition.Plugin, $touches[$c]
                    rotateAction = $null
                }
            })
    }
}

$info = [ordered]@{
    '$type'                     = 'Loupedeck.Service.ApplicationProfile, LoupedeckService'
    name                        = $profil
    profileFlags                = 'None'
    displayName                 = $definition.Nom
    description                 = "Ready layout for the $($definition.Nom) plugin (version $Version)."
    deviceType                  = 'Loupedeck70'
    applicationName             = '@_defaultwin'
    nativePluginName            = 'DefaultWin'
    hasNativePlugin             = $true
    additionalNativePluginNames = @($definition.Plugin)
    profileSettings             = [ordered]@{ '$type' = 'Loupedeck.DictionaryNoCase`1[[System.String, System.Private.CoreLib]], PluginApi' }
    actionImages90              = $null
    actionImages60              = $null
    wheelImages                 = $null
    actionColors                = $null
    layout                      = [ordered]@{
        '$type'      = 'Loupedeck.Service.Devices.Loupedeck7Devices.ProfileLayout7, LoupedeckService'
        deviceType   = 'Loupedeck70'
        profileFlags = 'None'
        layoutModes  = @([ordered]@{
                '$type'             = 'Loupedeck.Service.Devices.Loupedeck7Devices.ProfileLayoutMode7, LoupedeckService'
                deviceType          = 'Loupedeck70'
                modeName            = 'System'
                parentModeName      = $null
                actions             = $null
                dynamicButtonPages  = $null
                dynamicEncoderPages = $null
                workspaces          = @([ordered]@{
                        '$type'     = 'Loupedeck.Service.Devices.Loupedeck7Devices.ProfileLayoutWorkspace7, LoupedeckService'
                        name        = $espace
                        displayName = 'Workspace 1'
                        description = $null
                        pressPages  = @($pages)
                        rotatePages = @()
                    })
                homeWorkspaceName   = $espace
            })
        folderPages  = @()
    }
    macroCommands               = @()
    macroAdjustments            = @()
    profileCommands             = @()
    profileAdjustments          = @()
    packageName                 = $profil
    packageVersion              = "$Version.0"
    profileActions              = @()
}

$application = [ordered]@{
    '$type'             = 'Loupedeck.Service.SupportedApplicationInfo, LoupedeckService'
    name                = '@_defaultwin'
    displayName         = 'System plugin'
    description         = $null
    deviceType          = 'Loupedeck70'
    nativePluginName    = 'DefaultWin'
    hasNativePlugin     = $true
    processOrBundleName = $null
    modes               = @([ordered]@{
            '$type'        = 'Loupedeck.Service.ApplicationMode, LoupedeckService'
            name           = 'System'
            parentModeName = $null
            displayName    = 'System'
        })
    defaultProfileName  = $profil
    isEnabled           = $true
}

$entrees = [ordered]@{
    'ApplicationInfo.json'          = $application | ConvertTo-Json -Depth 20
    'ProfileInfo.json'              = $info | ConvertTo-Json -Depth 20
    'metadata/AdvancedInfo.json'    = [ordered]@{ additionalPluginNames = @($definition.Plugin) } | ConvertTo-Json
    'metadata/LoupedeckPackage.yaml' = "type: Profile5`nname: $profil`ndisplayName: $($definition.Nom)`nversion: $Version.0`n"
}

$dossier = Split-Path -Parent ([IO.Path]::GetFullPath($Sortie))
New-Item -ItemType Directory -Force $dossier | Out-Null
if (Test-Path -LiteralPath $Sortie) { Remove-Item -LiteralPath $Sortie -Force }
$utf8 = [Text.UTF8Encoding]::new($false)
$date = [DateTimeOffset]::new(2024, 1, 1, 0, 0, 0, [TimeSpan]::Zero)  # fixed: reproducible archive
$flux = [IO.File]::Create($Sortie)
try {
    $zip = [IO.Compression.ZipArchive]::new($flux, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($nom in $entrees.Keys) {
            $entree = $zip.CreateEntry($nom, [IO.Compression.CompressionLevel]::Optimal)
            $entree.LastWriteTime = $date
            $ecriture = [IO.StreamWriter]::new($entree.Open(), $utf8)
            try { $ecriture.Write(($entrees[$nom] -replace "`r`n", "`n")) } finally { $ecriture.Dispose() }
        }
    } finally { $zip.Dispose() }
} finally { $flux.Dispose() }
Write-Host "Profil prêt : $Sortie"

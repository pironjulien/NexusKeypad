# Paquets du Logi Marketplace, compilés en Release hors de OneDrive, emballés puis vérifiés par le Plugin Tool officiel
# (logiplugintool, outil .NET global : dotnet tool install -g LogiPluginTool). Rien n'est installé ni rechargé.
#   -Edition Marketplace (défaut) : AI Coding Agents, la page 1 seule (la Trinité, sans le poste ni le tube d'essais).
#   -Edition Poste : PC Dashboard, second plugin, la page 2 seule (le poste) et la touche du site à régler.
#   -Edition Complete : AI Coding Agents + PC, troisième plugin, les deux pages comme la version de Julien.
# Sortie : %LOCALAPPDATA%\NexusKeypad\marketplace\<nom>_<version>.lplug4 (nom conseillé par Logitech : nom_version).
#Requires -Version 7
[CmdletBinding()]
param([ValidateSet('Marketplace', 'Poste', 'Complete')] [string] $Edition = 'Marketplace')
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$racine = Join-Path $env:LOCALAPPDATA 'NexusKeypad\marketplace'
$dossier = @{ Marketplace = 'marketplace'; Poste = 'poste'; Complete = 'complete' }[$Edition]
$entree = Join-Path $env:LOCALAPPDATA "NexusKeypad\$dossier\bin\Release"
# Repartir de zéro : aucun reste d'une compilation précédente dans le paquet.
if (Test-Path $entree) { Remove-Item $entree -Recurse -Force }

dotnet build (Join-Path $PSScriptRoot '..\plugin\src\TrinitePadPlugin.csproj') -c Release -nologo -v q -p:NexusTest=true -p:Edition=$Edition
if ($LASTEXITCODE) { throw 'Le plugin ne compile pas.' }

# Aucun chemin du poste dans ce qui part chez les utilisateurs (dossier de l'utilisateur, en UTF-8 ou UTF-16).
$motif = "\Users\$env:USERNAME\"
$latin = [Text.Encoding]::Latin1
$motifs = [Text.Encoding]::UTF8, [Text.Encoding]::Unicode | ForEach-Object { $latin.GetString($_.GetBytes($motif)) }
$fuite = Get-ChildItem $entree -Recurse -File | Where-Object {
    $contenu = $latin.GetString([IO.File]::ReadAllBytes($_.FullName))
    $motifs | Where-Object { $contenu.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -ge 0 }
}
if ($fuite) { throw "Nom du compte Windows trouvé dans : $($fuite.FullName -join ', ')" }

$yaml = Get-Content (Join-Path $entree 'metadata\LoupedeckPackage.yaml')
$nom = ($yaml | Select-String '^name:\s*(\S+)').Matches[0].Groups[1].Value
$version = ($yaml | Select-String '^version:\s*(\S+)').Matches[0].Groups[1].Value
$paquet = Join-Path $racine ('{0}_{1}.lplug4' -f $nom, ($version -replace '\.', '_'))
New-Item -ItemType Directory -Force $racine | Out-Null
if (Test-Path $paquet) { Remove-Item $paquet -Force }
logiplugintool pack $entree $paquet
if ($LASTEXITCODE) { throw 'Emballage refusé par le Plugin Tool.' }
logiplugintool verify $paquet
if ($LASTEXITCODE) { throw 'Paquet refusé par le Plugin Tool.' }
Write-Host "Paquet du Marketplace prêt : $paquet"

# Contrôle complet du projet, hors ligne et sans effet de bord (convention Nexus : test.ps1 à la racine).
# Compile le plugin à part (NexusTest : ni lien posé, ni rechargement du plugin en service), puis l'aperçu, qui
# compile les mêmes sources et dessine les 9 touches dans chaque situation ; les règles de la logique (verifier) ;
# enfin la syntaxe des scripts Python et les règles des hooks (tests).
# Tout ce qui est produit va dans %LOCALAPPDATA%\NexusKeypad\test, rien dans OneDrive.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$logiDir = 'C:\Program Files\Logi\LogiPluginService'
if (Test-Path $logiDir) { $env:PATH = "$logiDir;$env:PATH" }
$sortie = Join-Path $env:LOCALAPPDATA 'NexusKeypad\test\'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:PYTHONDONTWRITEBYTECODE = '1'  # aucun __pycache__ dans OneDrive (python -B ci-dessous aussi)

dotnet build (Join-Path $PSScriptRoot 'plugin\src\TrinitePadPlugin.csproj') -c Debug -nologo -v q -p:NexusTest=true -p:NexusBuild=$sortie
if ($LASTEXITCODE) { throw 'Le plugin ne compile pas.' }

$planches = Join-Path $sortie 'planches'
if (Test-Path $planches) { Remove-Item $planches -Recurse -Force }
dotnet run --project (Join-Path $PSScriptRoot 'apercu') -v q -- $planches
if ($LASTEXITCODE) { throw "L'aperçu échoue." }
foreach ($nom in 'actif', 'calme', 'tetes') {
    $png = Join-Path $planches "$nom.png"
    if (-not (Test-Path $png) -or (Get-Item $png).Length -lt 2KB) { throw "Planche $nom absente ou vide." }
}

# Règles de la logique (états, ordre, quotas, cache d'images), hors du pad.
dotnet run --project (Join-Path $PSScriptRoot 'verifier') -v q
if ($LASTEXITCODE) { throw 'Une règle du pad ne tient plus.' }

$python = (Get-Command python -ErrorAction Stop).Source
$scripts = @(Get-ChildItem (Join-Path $PSScriptRoot 'etat'), (Join-Path $PSScriptRoot 'outils') -Filter *.py | ForEach-Object FullName)
& $python -I -B -c "import ast, sys; [ast.parse(open(f, encoding='utf-8').read(), f) for f in sys.argv[1:]]" @scripts
if ($LASTEXITCODE) { throw 'Un script Python ne se lit pas.' }
# -B : pas de __pycache__ dans OneDrive.
& $python -I -B -m unittest discover -s (Join-Path $PSScriptRoot 'tests') -q
if ($LASTEXITCODE) { throw 'Une règle des hooks ne tient plus.' }

Write-Host 'NexusKeypad : contrôles réussis.'

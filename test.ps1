# Contrôle complet du projet, hors ligne et sans effet de bord (convention Nexus : test.ps1 à la racine).
# Compile le plugin à part (NexusTest : ni lien posé, ni rechargement du plugin en service), puis l'aperçu, qui
# compile les mêmes sources et dessine les 9 touches dans chaque situation ; les règles de la logique (verifier) ;
# enfin la syntaxe des scripts Python et les règles des hooks (tests), etat.py et le hook natif livré dans le paquet.
# Tout ce qui est produit va dans %LOCALAPPDATA%\NexusKeypad\test, rien dans OneDrive.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$logiDir = 'C:\Program Files\Logi\LogiPluginService'
if (Test-Path $logiDir) { $env:PATH = "$logiDir;$env:PATH" }
$sortie = Join-Path $env:LOCALAPPDATA 'NexusKeypad\test\'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
# Sans effet sur les python -I ci-dessous (-I ignore les variables PYTHON*) : c'est leur -B qui évite tout
# __pycache__ dans OneDrive. Gardé pour un python lancé sans -I.
$env:PYTHONDONTWRITEBYTECODE = '1'

dotnet build (Join-Path $PSScriptRoot 'plugin\src\TrinitePadPlugin.csproj') -c Debug -nologo -v q -p:NexusTest=true -p:NexusBuild=$sortie
if ($LASTEXITCODE) { throw 'Le plugin ne compile pas.' }
# Édition du Logi Marketplace (page 1 seule, sans tube d'essais) : elle doit compiler aussi.
dotnet build (Join-Path $PSScriptRoot 'plugin\src\TrinitePadPlugin.csproj') -c Release -nologo -v q -p:NexusTest=true -p:Edition=Marketplace -p:NexusBuild=$sortie
if ($LASTEXITCODE) { throw "L'édition du Marketplace ne compile pas." }
# Édition PC Dashboard (page 2 seule, second plugin du Marketplace) : elle doit compiler aussi.
dotnet build (Join-Path $PSScriptRoot 'plugin\src\TrinitePadPlugin.csproj') -c Release -nologo -v q -p:NexusTest=true -p:Edition=Poste -p:NexusBuild=$sortie
if ($LASTEXITCODE) { throw "L'édition PC Dashboard ne compile pas." }
# Et elle ne contient rien des agents (audit du 26/10) : ni nom, ni chaîne, ni fonction qui lise ou pilote leurs applis.
$octets = [IO.File]::ReadAllBytes((Join-Path $sortie 'poste\bin\Release\bin\PcDashboardPlugin.dll'))
$texte = [Text.Encoding]::ASCII.GetString($octets) + [Text.Encoding]::Unicode.GetString($octets)
if ($texte -match '(?i)claude|codex|antigravity|gemini|anthropic|openai') { throw "PC Dashboard contient du code des agents : « $($Matches[0]) »." }
# Édition AI Coding Agents + PC (les deux pages, troisième plugin du Marketplace) : elle doit compiler aussi.
dotnet build (Join-Path $PSScriptRoot 'plugin\src\TrinitePadPlugin.csproj') -c Release -nologo -v q -p:NexusTest=true -p:Edition=Complete -p:NexusBuild=$sortie
if ($LASTEXITCODE) { throw "L'édition AI Coding Agents + PC ne compile pas." }
# Version complète hors Marketplace (NexusKeypad) : elle compile, et get.ps1 l'installe, pose les touches et la retire
# dans un dossier de test (jamais le service Logi ni les réglages réels).
dotnet build (Join-Path $PSScriptRoot 'plugin\src\TrinitePadPlugin.csproj') -c Release -nologo -v q -p:NexusTest=true -p:Edition=Nexus -p:NexusBuild=$sortie
if ($LASTEXITCODE) { throw "La version complète (NexusKeypad) ne compile pas." }
& (Join-Path $PSScriptRoot 'tests\test_get.ps1') -Compilation (Join-Path $sortie 'nexus\bin\Release') -Travail (Join-Path $env:TEMP 'nexuskeypad-test-get')

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
# -B : pas de __pycache__ dans OneDrive. NEXUSKEYPAD_HOOK : l'exécutable natif compilé avec le plugin, comparé à etat.py.
$env:NEXUSKEYPAD_HOOK = Join-Path $sortie 'hook-build\publish\nexuskeypad-hook.exe'
if (-not (Test-Path $env:NEXUSKEYPAD_HOOK)) { throw "Le hook natif n'a pas été compilé." }
& $python -I -B -m unittest discover -s (Join-Path $PSScriptRoot 'tests') -q
if ($LASTEXITCODE) { throw 'Une règle des hooks ne tient plus.' }

Write-Host 'NexusKeypad : contrôles réussis.'

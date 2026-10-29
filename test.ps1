# Contrôle complet du projet, hors ligne et sans effet de bord (convention Nexus : test.ps1 à la racine).
# Compile le plugin à part (NexusTest : ni lien posé, ni rechargement du plugin en service), puis l'aperçu, qui
# compile les mêmes sources et dessine les 9 touches dans chaque situation ; les règles de la logique (verifier) ;
# enfin la syntaxe des scripts Python et les règles du hook natif livré dans le paquet (tests).
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

# Plugin nexus-keypad (privé, catalogue nexus de NexusClaude) : son module de quota est la copie à l'identique du mod
# public claude-quota\nexuskeypad-quota (un seul code pour le quota, deux paquets).
$quotaPrive = Join-Path $PSScriptRoot 'nexus-keypad\hooks\quota.ts'
if (Test-Path $quotaPrive) {
    $quotaPublic = Join-Path $PSScriptRoot 'claude-quota\nexuskeypad-quota\hooks\register.ts'
    if (([IO.File]::ReadAllText($quotaPrive) -replace "`r`n", "`n") -cne ([IO.File]::ReadAllText($quotaPublic) -replace "`r`n", "`n")) {
        throw 'nexus-keypad\hooks\quota.ts diffère de claude-quota\nexuskeypad-quota\hooks\register.ts : recopier.'
    }
}

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
& (Join-Path $PSScriptRoot 'tests\test_get_signature.ps1')
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'tests\test_get_signature.ps1')
if ($LASTEXITCODE) { throw 'Installer signature verification failed under Windows PowerShell 5.1.' }
# Stream Deck : privé (ni streamdeck\ ni son test dans l'export public), essayé seulement là où il existe.
if (Test-Path (Join-Path $PSScriptRoot 'streamdeck')) {
    & (Join-Path $PSScriptRoot 'tests\test_get_streamdeck.ps1') -Travail (Join-Path $env:TEMP 'nexuskeypad-test-get-streamdeck')
}

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

dotnet run --project (Join-Path $PSScriptRoot 'tests\connecteurs') -v q -- $PSScriptRoot
if ($LASTEXITCODE) { throw 'Une règle de connexion ou de préservation des réglages ne tient plus.' }
dotnet run --project (Join-Path $PSScriptRoot 'tests\hook-son') -v q
if ($LASTEXITCODE) { throw 'Une règle de fin de tour ou de son du connecteur ne tient plus.' }
dotnet run --project (Join-Path $PSScriptRoot 'tests\hook-codex') -v q
if ($LASTEXITCODE) { throw 'Une règle des événements Codex ou de leur isolation ne tient plus.' }
dotnet run --project (Join-Path $PSScriptRoot 'tests\parite-uia') -v q
if ($LASTEXITCODE) { throw 'Une preuve de conversation ou de quota ne tient plus.' }
dotnet run --project (Join-Path $PSScriptRoot 'tests\claude-quota') -v q
if ($LASTEXITCODE) { throw 'Le périmètre du quota Claude ne tient plus.' }
dotnet run --project (Join-Path $PSScriptRoot 'tests\quota-antigravity') -v q
if ($LASTEXITCODE) { throw 'La lecture des anciens et nouveaux quotas Antigravity ne tient plus.' }
dotnet run --project (Join-Path $PSScriptRoot 'tests\snapshot') -v q
if ($LASTEXITCODE) { throw 'Le contrat du snapshot du pad ne tient plus.' }
dotnet run --project (Join-Path $PSScriptRoot 'tests\lanceurs') -v q
if ($LASTEXITCODE) { throw 'Une règle de lancement des applications officielles ne tient plus.' }

# Node ne sert qu'à éprouver le JavaScript UI du pad, sans npm ni application réelle.
$node = (Get-Command node -ErrorAction Stop).Source
& $node --test (Join-Path $PSScriptRoot 'tests\test_usage_antigravity.cjs')
if ($LASTEXITCODE) { throw 'La preuve du panneau de quotas Antigravity ne tient plus.' }
& $node --test (Join-Path $PSScriptRoot 'tests\test_quota_antigravity.cjs')
if ($LASTEXITCODE) { throw 'La lecture passive des quotas visibles Antigravity ne tient plus.' }

$python = (Get-Command python -ErrorAction Stop).Source
# sons\ est privé (absent de l'export public) : seuls les dossiers présents sont lus.
$dossiers = @('outils', 'sons' | ForEach-Object { Join-Path $PSScriptRoot $_ } | Where-Object { Test-Path $_ })
$scripts = @(Get-ChildItem $dossiers -Filter *.py | ForEach-Object FullName)
& $python -I -B -c "import ast, sys; [ast.parse(open(f, encoding='utf-8').read(), f) for f in sys.argv[1:]]" @scripts
if ($LASTEXITCODE) { throw 'Un script Python ne se lit pas.' }
# -B : pas de __pycache__ dans OneDrive. NEXUSKEYPAD_HOOK : l'exécutable natif compilé avec le plugin.
$env:NEXUSKEYPAD_HOOK = Join-Path $sortie 'hook-build\publish\nexuskeypad-hook.exe'
if (-not (Test-Path $env:NEXUSKEYPAD_HOOK)) { throw "Le hook natif n'a pas été compilé." }
& $python -I -B -m unittest discover -s (Join-Path $PSScriptRoot 'tests') -q
if ($LASTEXITCODE) { throw 'Une règle des hooks ne tient plus.' }

Write-Host 'NexusKeypad : contrôles réussis.'

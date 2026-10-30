# Full offline check of the project, without side effects (Nexus convention: test.ps1 at the root). The CI runs this file.
# 1. The plugin's five editions compile (NexusTest: no link set, no reload of the running plugin); PC Dashboard holds
#    nothing of the agents; get.ps1 installs, places the keys and removes the complete edition in a test folder.
# 2. dotnet test: the C# tests (xUnit v3, tests\*\*.Tests.csproj), from the root where global.json selects the runner.
# 3. node --test: the pad's JavaScript (Antigravity's quota panel and navigation), without npm nor a real app.
# Everything built goes to ~\.nexus\build\nexuskeypad\test, nothing into OneDrive.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$logi = 'C:\Program Files\Logi\LogiPluginService'
if (Test-Path $logi) { $env:PATH = "$logi;$env:PATH" } # the Logi service's native libraries, as when the plugin runs there
$sortie = Join-Path $HOME '.nexus\build\nexuskeypad\test\'
$plugin = Join-Path $PSScriptRoot 'plugin\src\TrinitePadPlugin.csproj'

foreach ($edition in '', 'Marketplace', 'Poste', 'Complete', 'Nexus') {
    $configuration = if ($edition) { 'Release' } else { 'Debug' }
    dotnet build $plugin -c $configuration -nologo -v q -p:NexusTest=true -p:Edition=$edition -p:NexusBuild=$sortie
    if ($LASTEXITCODE) { throw "The plugin does not compile (edition: $(if ($edition) { $edition } else { 'debug' }))." }
}
# PC Dashboard (second Marketplace plugin) contains nothing of the agents (audit of 26/10): no name, string or function
# that reads or drives their apps.
$octets = [IO.File]::ReadAllBytes((Join-Path $sortie 'poste\bin\Release\bin\PcDashboardPlugin.dll'))
$texte = [Text.Encoding]::ASCII.GetString($octets) + [Text.Encoding]::Unicode.GetString($octets)
if ($texte -match '(?i)claude|codex|antigravity|gemini|anthropic|openai') { throw "PC Dashboard contains agent code: « $($Matches[0]) »." }

& (Join-Path $PSScriptRoot 'tests\test_get.ps1') -Compilation (Join-Path $sortie 'nexus\bin\Release') -Travail (Join-Path $env:TEMP 'nexuskeypad-test-get')
& (Join-Path $PSScriptRoot 'tests\test_get_signature.ps1')
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'tests\test_get_signature.ps1')
if ($LASTEXITCODE) { throw 'Installer signature verification failed under Windows PowerShell 5.1.' }
# Stream Deck: private (neither streamdeck\ nor its test in the public export), checked only where it exists.
if (Test-Path (Join-Path $PSScriptRoot 'streamdeck')) {
    & (Join-Path $PSScriptRoot 'tests\test_get_streamdeck.ps1') -Travail (Join-Path $env:TEMP 'nexuskeypad-test-get-streamdeck')
}

Push-Location $PSScriptRoot
try { dotnet test --solution NexusKeypad.Tests.slnx -p:NexusBuild=$sortie --results-directory (Join-Path $sortie 'TestResults') }
finally { Pop-Location }
if ($LASTEXITCODE) { throw 'A C# test failed.' }

& (Get-Command node -ErrorAction Stop).Source --test @(Get-ChildItem (Join-Path $PSScriptRoot 'tests') -Filter *.cjs | ForEach-Object FullName)
if ($LASTEXITCODE) { throw 'A JavaScript test failed.' }

Write-Host 'NexusKeypad: all checks passed.'

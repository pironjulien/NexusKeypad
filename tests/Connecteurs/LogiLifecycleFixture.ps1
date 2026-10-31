param([Parameter(Mandatory)] [string]$Installer, [Parameter(Mandatory)] [string]$Root,
    [Parameter(Mandatory)] [string]$Case)
$ErrorActionPreference = 'Stop'
$source = [IO.File]::ReadAllText($Installer)
. ([scriptblock]::Create($source.Substring(0, $source.IndexOf('$Plugin =', [StringComparison]::Ordinal))))
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($Installer, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'Installer does not parse.' }
foreach ($name in 'Get-LogiPluginRuntime', 'Get-LogiPluginProcesses', 'Wait-LogiPluginService', 'Stop-Logi', 'Start-Logi', 'Get-Profile', 'Install-NexusKeypad') {
    $definition = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $true) | Where-Object Name -eq $name)
    if ($definition.Count -ne 1) { throw "Expected one function $name." }
    . ([scriptblock]::Create($definition[0].Extent.Text))
}
function Assert([bool]$Value, [string]$Message) { if (-not $Value) { throw $Message } }
New-Item -ItemType Directory -Force -Path $Root | Out-Null
$env:TEMP = $Root
$env:TMP = $Root
$env:ProgramFiles = Join-Path $Root 'program-files'
$serviceDirectory = Join-Path $env:ProgramFiles 'Logi\LogiPluginService'
New-Item -ItemType Directory -Force -Path $serviceDirectory | Out-Null
foreach ($file in 'LogiPluginServiceTool.exe', 'LogiPluginService.exe', 'LogiPluginServiceExt.exe') {
    if ($Case -ne 'missing-tool' -or $file -ne 'LogiPluginServiceTool.exe') {
        [IO.File]::WriteAllText((Join-Path $serviceDirectory $file), 'isolated identity fixture, never executed')
    }
}
$script:running = $true
$script:started = $false
$script:events = [Collections.Generic.List[string]]::new()
$script:signatureChecks = [Collections.Generic.List[string]]::new()
function robocopy.exe {
    param([string]$From, [string]$Destination, [Parameter(ValueFromRemainingArguments = $true)] [string[]]$Options)
    Assert (-not $script:running) 'A package copy/rollback must never run while the plugin service is active.'
    if (($Case -like 'copy-failed-*' -or $Case -eq 'rollback-failed') -and $From -notlike '*previous-plugin') {
        New-Item -ItemType Directory -Force -Path (Join-Path $Destination 'bin') | Out-Null
        [IO.File]::WriteAllText((Join-Path $Destination 'bin\NexusKeypadPlugin.dll'), 'partial copy')
        $script:events.Add('copy-failed')
        $global:LASTEXITCODE = 8
        return
    }
    if ($Case -eq 'rollback-failed' -and $From -like '*previous-plugin') {
        $script:events.Add('rollback-failed')
        $global:LASTEXITCODE = 8
        return
    }
    if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Recurse -Force }
    Copy-Item -LiteralPath $From -Destination $Destination -Recurse
    $script:events.Add($(if ($From -like '*previous-plugin') { 'rollback' } else { 'copy' }))
    $global:LASTEXITCODE = 0
}
function Get-AuthenticodeSignature([string]$LiteralPath) {
    $script:signatureChecks.Add($LiteralPath)
    return [pscustomobject]@{ Status = $(if ($Case -eq 'bad-signature') { 'NotTrusted' } else { 'Valid' });
        SignerCertificate = [pscustomobject]@{ Subject = 'CN=Logitech Inc, O=Logitech Inc, C=US' } }
}
function Get-CimInstance([string]$ClassName, [string]$Filter) {
    Assert ($ClassName -eq 'Win32_Process' -and $Filter -notmatch 'agent|optionsplus') 'Only plugin service processes may be inspected.'
    if ($script:running) {
        foreach ($name in 'LogiPluginService.exe', 'LogiPluginServiceExt.exe') {
            [pscustomobject]@{ Name = $name; ProcessId = $(if ($script:started) { 4236 } elseif ($name -eq 'LogiPluginService.exe') { 1618 } else { 2618 });
                ExecutablePath = $(if ($Case -eq 'foreign-process') { Join-Path $Root $name } else { Join-Path $serviceDirectory $name }) }
        }
    }
}
function Stop-Process { throw 'No process termination is allowed.' }
function Start-Process([string]$FilePath, [string]$ArgumentList, [string]$WindowStyle, [switch]$PassThru) {
    Assert ($FilePath -ceq (Join-Path $serviceDirectory 'LogiPluginServiceTool.exe')) 'Only the official plugin control tool may run.'
    Assert ($ArgumentList -ceq 'stop' -and $WindowStyle -ceq 'Hidden' -and $PassThru) 'Stop must be cooperative and hidden.'
    $script:events.Add('stop')
    if ($Case -notin 'stop-failed', 'stop-unconfirmed') { $script:running = $false }
    $process = [pscustomobject]@{ ExitCode = $(if ($Case -in 'stop-failed', 'stop-failed-after-stop') { 1 } else { 0 }); Completed = ($Case -ne 'stop-control-timeout') }
    $process | Add-Member -MemberType ScriptMethod -Name WaitForExit -Value { param($Milliseconds) return $Milliseconds -eq 30000 -and $this.Completed }
    $process | Add-Member -MemberType ScriptMethod -Name Dispose -Value { }
    return $process
}
function Invoke-CimMethod([string]$ClassName, [string]$MethodName, [hashtable]$Arguments) {
    Assert ($ClassName -ceq 'Win32_Process' -and $MethodName -ceq 'Create') 'Restart must be detached by WMI.'
    $expected = '"' + (Join-Path $env:SystemRoot 'System32\conhost.exe') + '" --headless "' + (Join-Path $serviceDirectory 'LogiPluginServiceTool.exe') + '" start'
    Assert ($Arguments.CommandLine -ceq $expected -and $Arguments.CurrentDirectory -ceq $serviceDirectory) 'Restart must target the official plugin tool in a headless console.'
    Assert ($Case -in 'stop-failed-after-stop', 'copy-failed-new' -or (Test-Path -LiteralPath (Join-Path $Target 'bin\NexusKeypadPlugin.dll'))) 'Restart must happen after copy or recovery.'
    if ($Case -eq 'copy-failed-existing') { Assert ([IO.File]::ReadAllText((Join-Path $Target 'bin\NexusKeypadPlugin.dll')) -ceq 'previous complete package') 'Restart must wait for rollback.' }
    if ($Case -eq 'copy-failed-new') { Assert (-not (Test-Path -LiteralPath $Target)) 'Partial first installation must be removed before restart.' }
    $script:events.Add('start')
    if ($Case -notin 'start-refused', 'start-unconfirmed') { $script:running = $true; $script:started = $true }
    return [pscustomobject]@{ ReturnValue = $(if ($Case -eq 'start-refused') { 5 } else { 0 }); ProcessId = 4236 }
}
function Say([string]$Text, [string]$Color = 'Gray') { }
function Set-Connectors([string]$Hook) { return 'isolated fixture' }
# These wrappers only reduce the assertion wait to zero in the negative fixtures. The production functions above
# remain unchanged; no fixture can wait for or control a real process because every provider is isolated here.
$stop = ${function:Stop-Logi}
$start = ${function:Start-Logi}
function Stop-Logi { & $stop -TimeoutSeconds 0 }
function Start-Logi { & $start -TimeoutSeconds 0 }
$Plugin = 'NexusKeypad'
$Sandbox = $false
$Pack = $true
$Keys = 'no'
$Logi = Join-Path $Root 'logi'
$Target = Join-Path $Logi 'Plugins\NexusKeypad'
if ($Case -in 'copy-failed-existing', 'rollback-failed') {
    New-Item -ItemType Directory -Force -Path (Join-Path $Target 'bin') | Out-Null
    [IO.File]::WriteAllText((Join-Path $Target 'bin\NexusKeypadPlugin.dll'), 'previous complete package')
}
$Results = [ordered]@{}
$fixture = Join-Path $Root 'fixture\bin'
New-Item -ItemType Directory -Force -Path $fixture | Out-Null
[IO.File]::WriteAllText((Join-Path $fixture 'NexusKeypadPlugin.dll'), 'isolated package, never loaded')
$env:NEXUS_KEYPAD_SOURCE = Join-Path $Root 'fixture.lplug4'
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory((Split-Path $fixture), $env:NEXUS_KEYPAD_SOURCE)
$work = Join-Path ([IO.Path]::GetTempPath()) "nexuskeypad-$PID"
Assert ($work.StartsWith([IO.Path]::GetFullPath($Root) + '\', [StringComparison]::OrdinalIgnoreCase)) 'The installer work folder must remain inside this fixture.'
$failure = $null
try { $null = Install-NexusKeypad } catch { $failure = $_.Exception.Message }
if ($Case -eq 'success') {
    Assert (-not $failure) "Isolated successful installation failed: $failure"
    Assert (Test-Path -LiteralPath (Join-Path $Target 'bin\NexusKeypadPlugin.dll')) 'Successful copy is absent.'
    Assert (($script:events -join ',') -ceq 'stop,copy,start') 'One cooperative stop must precede copy and one detached start.'
} else {
    $pattern = switch ($Case) {
        'bad-signature' { 'no valid Logitech signature' }
        'missing-tool' { 'component is missing' }
        'foreign-process' { 'unexpected identity' }
        'stop-failed' { 'stop command failed' }
        'stop-failed-after-stop' { 'stop command failed' }
        'stop-control-timeout' { 'still running; its outcome is unknown' }
        'stop-unconfirmed' { 'did not stop' }
        'start-refused' { 'could not start independently' }
        'start-unconfirmed' { 'did not start' }
        'copy-failed-existing' { 'Copy to .* failed' }
        'copy-failed-new' { 'Copy to .* failed' }
        'rollback-failed' { 'Plugin recovery failed' }
    }
    Assert ($failure -match $pattern) "Expected refusal was not reported: $failure"
    if ($Case -eq 'rollback-failed') {
        Assert (-not $script:running -and -not $script:events.Contains('start')) 'Failed recovery must keep the plugin service stopped.'
        Assert (Test-Path -LiteralPath (Join-Path $work 'previous-plugin\bin\NexusKeypadPlugin.dll')) 'Failed recovery must preserve the backup.'
    } elseif ($Case -like 'copy-failed-*') {
        Assert ($script:events[$script:events.Count - 1] -ceq 'start') 'A failed copy must recover the plugin service.'
        if ($Case -eq 'copy-failed-existing') { Assert ($script:events.Contains('rollback')) 'The previous package must be restored.' }
    } elseif ($Case -notlike 'start-*') {
        Assert (-not (Test-Path -LiteralPath $Target)) 'A failed stop/identity check must refuse copying.'
        if ($Case -eq 'stop-failed-after-stop') {
            Assert (($script:events -join ',') -ceq 'stop,start' -and $script:running) 'A completed stop that failed after stopping must recover cooperatively, retain failure and refuse copy.'
        } else {
            Assert (-not $script:events.Contains('start')) 'A rejected or in-flight stop must not replay or restart anything.'
        }
    }
}
Assert (-not ($script:signatureChecks | Where-Object { $_ -notlike "$serviceDirectory\*" })) 'Signature checks escaped the plugin runtime.'
[ordered]@{ case = $Case; events = @($script:events); copied = (Test-Path -LiteralPath $Target) } | ConvertTo-Json -Compress

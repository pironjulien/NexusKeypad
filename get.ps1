# NexusKeypad complete edition, outside the Logi Marketplace, for Windows 10/11 (Windows PowerShell 5.1 or PowerShell 7),
# no administrator rights. Everything the author runs on his own keypad. This file is attached to every release, listed in
# its signed SHA256SUMS.txt and attested: download it, check it, then run it as a file (never piped into iex):
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\get.ps1            (-Remove removes the plugin, live states and
#                                                                              keys; Logi Options+ itself is left alone)
# The NexusSystem app (Microsoft Store) runs it the same way.
#
# It downloads the latest release's NexusKeypad package (its hash and the offline ECDSA signature of SHA256SUMS.txt
# are verified before extraction or execution; its build
# provenance can be checked with `gh attestation verify <file> --repo pironjulien/NexusKeypad`), copies it into Logi
# Options+'s plugin folder (%LOCALAPPDATA%\Logi\LogiPluginService\Plugins\NexusKeypad), where the Logi Plugin Service
# loads it, then offers to place the 18 keys on the keypad's general profile (backed up first; the plugin service restarts
# for a few seconds). It then sets up the NexusKeypad connector of each AI app found (Claude, Codex, Antigravity: plugin and
# hooks with the installed helper's real path); an unplugged character on the keypad sets one up again.
# Run this installer again to update.
#   -Keys yes|no   place the keys without asking (yes) or leave them to drag in Logi Options+ (no)
# NEXUS_PACK=1 (the Nexus pack, get-nexus.ps1): no question, no wait, no window, keys left to place by hand; exit code
# 0 on success; a last "NEXUS_PACK_RESULT ..." line.
# For tests only: NEXUS_KEYPAD_SOURCE (a local .lplug4 instead of the latest release) and NEXUS_KEYPAD_LOGI (another
# Logi Plugin Service data folder; the real service is then never stopped or started). A local package aimed at the real
# Logi folder is refused (a test that lost its test folder must not install for real) unless -Real is given.
[CmdletBinding()]
param([switch]$Remove, [ValidateSet('ask', 'yes', 'no')] [string]$Keys = 'ask', [switch]$Real,
    [ValidateSet('stop', 'start')] [string]$LogiService)

$ErrorActionPreference = 'Stop'
# Windows PowerShell started by PowerShell 7 inherits Core module folders before its own standard modules.
# Prefer this host's modules (Get-FileHash, Unblock-File), while retaining every user and third-party module path.
if ($PSVersionTable.PSEdition -eq 'Desktop') {
    $desktopModules = Join-Path $PSHOME 'Modules'
    $env:PSModulePath = (@($desktopModules) + @($env:PSModulePath -split ';' | Where-Object {
        $_ -and $_.TrimEnd('\') -ine $desktopModules.TrimEnd('\')
    })) -join ';'
}
$ProgressPreference = 'SilentlyContinue'  # Invoke-WebRequest is many times slower with its progress bar
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$Plugin = 'NexusKeypad'
$Pack = $env:NEXUS_PACK -eq '1'
$Sandbox = -not $LogiService -and [bool]$env:NEXUS_KEYPAD_LOGI
$Logi = if ($Sandbox) { $env:NEXUS_KEYPAD_LOGI } else { Join-Path $env:LOCALAPPDATA 'Logi\LogiPluginService' }
$Target = Join-Path $Logi "Plugins\$Plugin"
$Results = [ordered]@{}
# Same SPKI public keys as MiseAJour.Cles. No private key or newly installed runtime is needed.
$ReleaseKeys = @('MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEwSIk2pqKmyLcWfNgwY6uXVzS7qq6kj14BxckZIvSvE2R3tPqi4yITye4c8czlGrdEMYM1PrnIN8o3ba2APvbJQ==')

function Say([string]$Text, [string]$Color = 'Gray') { Write-Host $Text -ForegroundColor $Color }

function Test-ReleaseSignature([byte[]]$Content, [string]$Signature, [string[]]$PublicKeys = $ReleaseKeys) {
    try { $signatureBytes = [Convert]::FromBase64String($Signature.Trim()) } catch { return $false }
    # ECDSA P-256 / SHA-256, fixed-width IEEE P1363 (r || s), as written by signer_release.ps1.
    if ($signatureBytes.Length -ne 64) { return $false }
    # .NET Framework in Windows PowerShell 5.1 has no ImportSubjectPublicKeyInfo. Accept only this exact
    # P-256 SPKI prefix, then import its public X/Y coordinates into Windows CNG (also available in PS7).
    $prefix = '30-59-30-13-06-07-2A-86-48-CE-3D-02-01-06-08-2A-86-48-CE-3D-03-01-07-03-42-00-04'
    foreach ($publicKey in $PublicKeys) {
        $key = $null
        $ecdsa = $null
        try {
            $spki = [Convert]::FromBase64String($publicKey)
            if ($spki.Length -ne 91 -or [BitConverter]::ToString($spki, 0, 27) -cne $prefix) { continue }
            # BCRYPT_ECDSA_PUBLIC_P256_MAGIC ('ECS1'), cbKey=32, followed by X and Y (big-endian each).
            $blob = New-Object byte[] 72
            [Array]::Copy([byte[]](0x45, 0x43, 0x53, 0x31, 0x20, 0, 0, 0), 0, $blob, 0, 8)
            [Array]::Copy($spki, 27, $blob, 8, 64)
            $key = [Security.Cryptography.CngKey]::Import($blob, [Security.Cryptography.CngKeyBlobFormat]::EccPublicBlob)
            $ecdsa = [Security.Cryptography.ECDsaCng]::new($key)
            if ($ecdsa.VerifyData($Content, $signatureBytes, [Security.Cryptography.HashAlgorithmName]::SHA256)) { return $true }
        } catch {
            # Invalid/unsupported keys never turn a failed signature into success; another configured key may match.
        } finally {
            if ($ecdsa) { $ecdsa.Dispose() }
            if ($key) { $key.Dispose() }
        }
    }
    return $false
}

function Get-VerifiedPackageHash([byte[]]$Manifest, [string]$Signature, [string]$PackageName) {
    if (-not (Test-ReleaseSignature $Manifest $Signature)) { throw 'SHA256SUMS.txt has no valid release signature: installation refused.' }
    # The signature covers the original bytes, including line endings. Decode only after verifying them.
    $text = [Text.UTF8Encoding]::new($false, $true).GetString($Manifest).TrimStart([char]0xfeff)
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $expected = $null
    foreach ($line in ($text -split '\r?\n' | Where-Object { $_.Trim() })) {
        if ($line -cnotmatch '^([0-9a-f]{64})\s+\*?(\S+)\s*$') { throw 'SHA256SUMS.txt has an invalid entry: installation refused.' }
        $hash, $name = $Matches[1], $Matches[2]
        if (-not $seen.Add($name)) { throw 'SHA256SUMS.txt lists a file more than once: installation refused.' }
        if ($name -ceq $PackageName) { $expected = $hash }
    }
    if (-not $expected) { throw "$PackageName is absent from the signed manifest: installation refused." }
    return $expected
}

function Get-LatestTag {
    # The latest release's tag from the redirect of github.com/<repo>/releases/latest (to .../releases/tag/vX.Y.Z): no
    # GitHub API, whose anonymous limit (60 requests an hour per IP) breaks on shared IPs (CI runners, offices). The API
    # stays as a fallback.
    try {
        $request = [Net.HttpWebRequest]::Create('https://github.com/pironjulien/NexusKeypad/releases/latest')
        $request.AllowAutoRedirect = $false
        $request.UserAgent = 'NexusKeypad-get'
        $response = $request.GetResponse()
        try { $location = $response.Headers['Location'] } finally { $response.Close() }
        if ($location -match '/releases/tag/(v[0-9][^/?#]*)$') { return $Matches[1] }
    } catch {
        Write-Verbose "Redirect lookup failed: $($_.Exception.Message)"
    }
    return (Invoke-RestMethod -UseBasicParsing 'https://api.github.com/repos/pironjulien/NexusKeypad/releases/latest').tag_name
}

function Get-Profile {
    # The keypad's general profile: Loupedeck70 (MX Creative Keypad), application @_defaultwin, its default profile.
    $app = Join-Path $Logi 'Applications\Loupedeck70\@_defaultwin'
    $info = Join-Path $app 'ApplicationInfo.json'
    if (-not (Test-Path -LiteralPath $info)) { return $null }
    $name = (Get-Content -LiteralPath $info -Raw -Encoding UTF8 | ConvertFrom-Json).defaultProfileName
    $file = Join-Path $app "Profiles\$name\ProfileInfo.json"
    if (Test-Path -LiteralPath $file) { return $file } else { return $null }
}

function Get-LogiPluginRuntime {
    $directory = Join-Path $env:ProgramFiles 'Logi\LogiPluginService'
    $files = @('LogiPluginServiceTool.exe', 'LogiPluginService.exe', 'LogiPluginServiceExt.exe')
    foreach ($file in $files) {
        $path = Join-Path $directory $file
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Logi plugin service component is missing: $path. Installation refused." }
        $signature = Get-AuthenticodeSignature -LiteralPath $path
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(^|,\s*)O=Logitech Inc(,|$)') {
            throw "Logi plugin service component has no valid Logitech signature: $path. Installation refused."
        }
    }
    return [pscustomobject]@{ Directory = $directory; Tool = (Join-Path $directory $files[0]); Service = (Join-Path $directory $files[1]) }
}

function Get-LogiPluginProcesses($Runtime) {
    $processes = @(Get-CimInstance Win32_Process -Filter "Name='LogiPluginService.exe' OR Name='LogiPluginServiceExt.exe'")
    foreach ($process in $processes) {
        $expected = Join-Path $Runtime.Directory $process.Name
        if ($process.Name -notin 'LogiPluginService.exe', 'LogiPluginServiceExt.exe' -or $process.ExecutablePath -ine $expected) {
            throw 'A process with a Logi plugin service name has an unexpected identity. Installation refused.'
        }
    }
    return $processes
}

function Wait-LogiPluginService($Runtime, [bool]$Stopped, [int]$TimeoutSeconds = 30, [int[]]$PreviousProcessIds = @()) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $processes = @(Get-LogiPluginProcesses $Runtime)
        if ($Stopped -and $processes.Count -eq 0) { return }
        if (-not $Stopped -and @($processes | Where-Object Name -eq 'LogiPluginService.exe').Count -gt 0 -and
            @($processes | Where-Object { $_.ProcessId -in $PreviousProcessIds }).Count -eq 0) { return }
        if ([DateTime]::UtcNow -ge $deadline) { throw "Logi plugin service did not $(if ($Stopped) { 'stop' } else { 'start' }) in $TimeoutSeconds seconds. Installation refused." }
        Start-Sleep -Milliseconds 100
    } while ($true)
}

function Stop-Logi([int]$TimeoutSeconds = 30) {
    if ($Sandbox) { return }
    $runtime = Get-LogiPluginRuntime
    $before = @(Get-LogiPluginProcesses $runtime)
    if ($before.Count -eq 0) { return }
    # The official control bus stops only the plugin service cooperatively. Never stop the keyboard/mouse agent.
    $control = Start-Process -FilePath $runtime.Tool -ArgumentList 'stop' -WindowStyle Hidden -PassThru
    $completed = $false
    try {
        $completed = $control.WaitForExit(30000)
        if (-not $completed) { throw 'The official Logi stop command is still running; its outcome is unknown. Installation refused.' }
        if ($control.ExitCode -ne 0) { throw 'The official Logi plugin service stop command failed. Installation refused.' }
        Wait-LogiPluginService $runtime $true $TimeoutSeconds
        $script:LogiStoppedProcessIds = @($before.ProcessId)
    } catch {
        $stopFailure = $_
        # A completed command can fail after it stopped the service. Restore only a verified fully stopped runtime;
        # never race an in-flight command or start another service beside a leftover extension process.
        if ($completed) {
            try { if (@(Get-LogiPluginProcesses $runtime).Count -eq 0) { Start-Logi } }
            catch { Write-Warning "Cooperative recovery of the plugin service was refused: $($_.Exception.Message)" }
        }
        throw $stopFailure
    } finally { $control.Dispose() }
}

function Start-Logi([int]$TimeoutSeconds = 30) {
    if ($Sandbox) { return }
    $runtime = Get-LogiPluginRuntime
    $processes = @(Get-LogiPluginProcesses $runtime)
    if (@($processes | Where-Object Name -eq 'LogiPluginService.exe').Count -gt 0) { return }
    if ($processes.Count -gt 0) { throw 'A Logi extension is still running without its service. Refusing a concurrent restart.' }
    # WMI + headless console: the official tool and its plugin service live outside the calling AI's job/MSIX package.
    $console = Join-Path $env:SystemRoot 'System32\conhost.exe'
    $wmi = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
        CommandLine = "`"$console`" --headless `"$($runtime.Tool)`" start"; CurrentDirectory = $runtime.Directory }
    if ($wmi.ReturnValue -ne 0) { throw "Logi plugin service could not start independently (WMI code $($wmi.ReturnValue))." }
    Wait-LogiPluginService $runtime $false $TimeoutSeconds $script:LogiStoppedProcessIds
}

function Set-Keys([string]$Hook, [switch]$Clear) {
    # Places (or clears) the plugin's 18 keys on the general profile, Logi stopped, after a one-time backup.
    $file = Get-Profile
    if (-not $file) { Say 'No MX Keypad profile found: connect the keypad and open Logi Options+ once, then place the keys by hand.' Yellow; return 'guided' }
    $backup = Join-Path (Split-Path $file) 'ProfileInfo.before-nexuskeypad.json'
    if (-not $Clear -and -not (Test-Path -LiteralPath $backup)) { Copy-Item -LiteralPath $file -Destination $backup }
    Stop-Logi
    try {
        $arguments = @('keys', $file, $Plugin)
        if ($Clear) { $arguments += '--remove' }
        & $Hook @arguments | Out-Host
        $code = $LASTEXITCODE
    } finally {
        Start-Logi
    }
    if ($code) { Say 'The keys could not be placed: place them by hand in Logi Options+ (the profile is unchanged).' Yellow; return 'failed' }
    if ($Clear) { return 'removed' } else { return 'placed' }
}

function Set-Connectors([string]$Hook) {
    # Declares the NexusKeypad plugin and its hooks in each AI app found (Claude, Codex, Antigravity), with the real path of
    # the installed nexuskeypad-hook.exe: Codex hooks cannot come from a catalog, they need this path. The helper keeps
    # third-party settings, turns off a nexus-keypad copy from the Nexus catalogs instead of doubling its hooks, and
    # reports an absent app as Absent. A failure only leaves that key unplugged.
    if ($Sandbox) { return 'left (test folder)' }  # the real AI profiles are not the test's
    $states = foreach ($agent in 'claude', 'codex', 'antigravity') {
        $state = try { ((& $Hook connecteur $agent enable 2>$null) | ConvertFrom-Json).state } catch { $null }
        "$agent=$(if ($state) { $state } else { 'failed' })"
    }
    return $states -join ', '
}

function Install-NexusKeypad {
    if (-not $Sandbox -and -not (Test-Path -LiteralPath (Join-Path $env:ProgramFiles 'Logi\LogiPluginService'))) {
        Say 'Logi Options+ is not installed: get it from https://www.logitech.com/software/logi-options-plus.html, then run this again.' Red
        $Results['Plugin'] = 'no Logi Options+'
        return 1
    }
    $work = Join-Path ([IO.Path]::GetTempPath()) "nexuskeypad-$PID"
    $preserveRecovery = $false
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    try {
        if ($env:NEXUS_KEYPAD_SOURCE) {
            $package = (Resolve-Path -LiteralPath $env:NEXUS_KEYPAD_SOURCE).Path
            Say "Installing NexusKeypad from $package..."
        } else {
            $tag = Get-LatestTag
            $name = '{0}_{1}.lplug4' -f $Plugin, ($tag.TrimStart('v') -replace '\.', '_')
            $base = "https://github.com/pironjulien/NexusKeypad/releases/download/$tag"
            Say "Downloading NexusKeypad $tag..."
            $package = Join-Path $work $name
            Invoke-WebRequest -UseBasicParsing "$base/$name" -OutFile $package
            $manifest = (Invoke-WebRequest -UseBasicParsing "$base/SHA256SUMS.txt").RawContentStream.ToArray()
            $signature = [Text.Encoding]::UTF8.GetString((Invoke-WebRequest -UseBasicParsing "$base/SHA256SUMS.txt.sig").RawContentStream.ToArray())
            $expected = Get-VerifiedPackageHash $manifest $signature $name
            $actual = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($expected -ne $actual) { throw "SHA-256 mismatch for $($name): the download is not the published package." }
        }
        $unpacked = Join-Path $work 'package'
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::ExtractToDirectory($package, $unpacked)
        if (-not (Test-Path -LiteralPath (Join-Path $unpacked "bin\$($Plugin)Plugin.dll"))) { throw 'Not a NexusKeypad package.' }
        $backup = Join-Path $work 'previous-plugin'
        $previous = Test-Path -LiteralPath $Target
        if ($previous) { Copy-Item -LiteralPath $Target -Destination $backup -Recurse }
        # Stop before copying: a live reload of a partial package can quarantine the plugin. No keyboard/mouse agent stop.
        Stop-Logi
        $restartSafe = $true
        try {
            New-Item -ItemType Directory -Force -Path $Target | Out-Null
            & robocopy.exe $unpacked $Target /MIR /NFL /NDL /NJH /NJS /NP /R:5 /W:1 | Out-Null
            if ($LASTEXITCODE -ge 8) { throw "Copy to $Target failed (robocopy $LASTEXITCODE)." }
            Get-ChildItem -LiteralPath $Target -Recurse -File | Unblock-File
        } catch {
            $copyFailure = $_
            try {
                if ($previous) {
                    & robocopy.exe $backup $Target /MIR /NFL /NDL /NJH /NJS /NP /R:5 /W:1 | Out-Null
                    if ($LASTEXITCODE -ge 8) { throw "Restoring the previous package failed (robocopy $LASTEXITCODE)." }
                } elseif (Test-Path -LiteralPath $Target) {
                    Remove-Item -LiteralPath $Target -Recurse -Force
                }
            } catch {
                $restartSafe = $false
                $preserveRecovery = $true
                throw "Plugin recovery failed; the plugin service stays stopped and recovery files stay in $work. $($_.Exception.Message)"
            }
            throw $copyFailure
        } finally { if ($restartSafe) { Start-Logi } }
        $Results['Plugin'] = 'installed'
        Say "NexusKeypad is in $Target." Green
    } finally {
        if (-not $preserveRecovery) { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
    }

    $hook = Join-Path $Target 'bin\nexuskeypad-hook.exe'
    $place = $Keys
    if ($Pack) { $place = 'no' }
    if ($place -eq 'ask') {
        Say ''
        $answer = Read-Host 'Place the 18 keys on the keypad now? The plugin service restarts briefly and your current profile is backed up first (y/N)'
        if ($answer -match '^(y|yes|o|oui)$') { $place = 'yes' } else { $place = 'no' }
    }
    if ($place -eq 'yes') { $Results['Keys'] = Set-Keys $hook } else { $Results['Keys'] = 'guided' }
    $Results['Live states'] = 'asked by the plugin'
    $Results['AI connections'] = Set-Connectors $hook
    Say ''
    if ($Results['Keys'] -ne 'placed') {
        Say 'Keys: in Logi Options+, open the MX Keypad, Customize keys, and drag Key 1 to Key 9 of the "AI agents" group onto page 1, then Key 10 to Key 18 onto page 2.'
    }
    Say "AI connections: $($Results['AI connections']). Press an unplugged character to set up its connector again. A restart arrow means the app connection needs a restart; no shortcut editing is needed."
    return 0
}

function Remove-NexusKeypad {
    $hook = Join-Path $Target 'bin\nexuskeypad-hook.exe'
    if (Test-Path -LiteralPath $hook) {
        if ($Sandbox) {
            $Results['Live states'] = 'left (test folder)'  # the real Claude and Antigravity settings are not the test's
        } else {
            & $hook --remove | Out-Host
            $Results['Live states'] = if ($LASTEXITCODE) { 'failed' } else { 'removed' }
        }
        $file = Get-Profile
        if ($file -and (Select-String -LiteralPath $file -SimpleMatch "`$$($Plugin)___" -Quiet)) { $Results['Keys'] = Set-Keys $hook -Clear }
    }
    if (Test-Path -LiteralPath $Target) {
        Remove-Item -LiteralPath $Target -Recurse -Force
        $Results['Plugin'] = 'removed'
    } else {
        $Results['Plugin'] = 'absent'
    }
    $data = Join-Path $env:LOCALAPPDATA 'NexusKeypad'
    if (-not $Sandbox) {
        foreach ($item in 'hook', 'claude-marketplace', 'etats-en-direct.json') {
            Remove-Item -LiteralPath (Join-Path $data $item) -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    Say 'NexusKeypad removed. Your keypad profile backup, if any, stays next to it (ProfileInfo.before-nexuskeypad.json).' Green
    return 0
}

if ($LogiService) {
    if ($LogiService -eq 'stop') { Stop-Logi } else { Start-Logi }
    return
}

$code = 1
try {
    if ($env:NEXUS_KEYPAD_SOURCE -and -not $Sandbox -and -not $Real) {
        throw 'NEXUS_KEYPAD_SOURCE (a test package) is set but NEXUS_KEYPAD_LOGI is not: refusing to touch the real Logi folder. Set NEXUS_KEYPAD_LOGI to a test folder, or pass -Real.'
    }
    if ($Remove) { $code = Remove-NexusKeypad } else { $code = Install-NexusKeypad }
} catch {
    Say "NexusKeypad: $($_.Exception.Message)" Red
    if (-not $Results.Contains('Plugin')) { $Results['Plugin'] = 'failed' }
    $code = 1
}
if ($Pack) {
    Write-Output ('NEXUS_PACK_RESULT ' + (($Results.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join '; '))
}
# Run as a file (-File, as documented): its exit code. Loaded into an existing session instead: never `exit`, which
# would close the user's window; a failure is an error instead.
if ($PSCommandPath) { exit $code }
if ($code) { throw 'NexusKeypad: not installed (see above).' }

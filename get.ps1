# NexusKeypad complete edition, outside the Logi Marketplace, for Windows 10/11 (Windows PowerShell 5.1 or PowerShell 7),
# no administrator rights. Everything the author runs on his own keypad. This file is attached to every release, listed in
# its SHA256SUMS.txt and attested: download it, check it, then run it as a file (never piped into iex):
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\get.ps1            (-Remove removes the plugin, live states and
#                                                                              keys; Logi Options+ itself is left alone)
# The NexusSystem app (Microsoft Store) runs it the same way.
#
# It downloads the latest release's NexusKeypad package (its hash is checked against the release's SHA256SUMS.txt
# before extraction or execution; its build
# provenance can be checked with `gh attestation verify <file> --repo pironjulien/NexusKeypad`), copies it into Logi
# Options+'s plugin folder (%LOCALAPPDATA%\Logi\LogiPluginService\Plugins\NexusKeypad), where the Logi Plugin Service
# loads it, then offers to place the 18 keys on their own "NexusKeypad" profile, made active (your own profile is left as
# it is: switch back to it in Logi Options+ at any time; the plugin service restarts for a few seconds). It then sets up the NexusKeypad connector of each AI app found (Claude, Codex, Antigravity: plugin and
# hooks with the installed helper's real path); an unplugged character on the keypad sets one up again.
# Run this installer again to update.
#   -Keys yes|no   place the keys without asking (yes) or leave them to drag in Logi Options+ (no)
#   -KeysOnly      only place the keys of the plugin already installed (nothing downloaded); nothing if already placed
# NEXUS_PACK=1 (the Nexus pack, get-nexus.ps1): no question, no wait, no window, keys left to place by hand; exit code
# 0 on success; a last "NEXUS_PACK_RESULT ..." line.
# For tests only: NEXUS_KEYPAD_SOURCE (a local .lplug4 instead of the latest release) and NEXUS_KEYPAD_LOGI (another
# Logi Plugin Service data folder; the real service is then never stopped or started). A local package aimed at the real
# Logi folder is refused (a test that lost its test folder must not install for real) unless -Real is given.
[CmdletBinding()]
param([switch]$Remove, [ValidateSet('ask', 'yes', 'no')] [string]$Keys = 'ask', [switch]$Real,
    [ValidateSet('stop', 'start')] [string]$LogiService, [switch]$KeysOnly)

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

function Say([string]$Text, [string]$Color = 'Gray') { Write-Host $Text -ForegroundColor $Color }

function Get-PackageHash([byte[]]$Manifest, [string]$PackageName) {
    $text = [Text.UTF8Encoding]::new($false, $true).GetString($Manifest).TrimStart([char]0xfeff)
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $expected = $null
    foreach ($line in ($text -split '\r?\n' | Where-Object { $_.Trim() })) {
        if ($line -cnotmatch '^([0-9a-f]{64})\s+\*?(\S+)\s*$') { throw 'SHA256SUMS.txt has an invalid entry: installation refused.' }
        $hash, $name = $Matches[1], $Matches[2]
        if (-not $seen.Add($name)) { throw 'SHA256SUMS.txt lists a file more than once: installation refused.' }
        if ($name -ceq $PackageName) { $expected = $hash }
    }
    if (-not $expected) { throw "$PackageName is absent from SHA256SUMS.txt: installation refused." }
    return $expected
}

function Get-LatestTag {
    # NEXUS_RELEASE_TAG (set by NexusSystem for its development channel): that release instead, a stable vX.Y.Z or a
    # development vX.Y.Z-dev.N build, checked against its own SHA256SUMS.txt like any other.
    if ($env:NEXUS_RELEASE_TAG) {
        if ($env:NEXUS_RELEASE_TAG -notmatch '^v\d+\.\d+\.\d+(-dev\.\d+)?$') { throw "NEXUS_RELEASE_TAG is not a release tag: $env:NEXUS_RELEASE_TAG" }
        return $env:NEXUS_RELEASE_TAG
    }
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

# The keys live in their own "NexusKeypad" profile of the keypad's general application, made active; the user's own
# profile is never edited (switch back to it in Logi Options+ at any time). Its description names that original profile.
$ProfileMark = 'NexusKeypad keys; original profile: '
function Get-KeypadProfiles {
    $app = Join-Path $Logi 'Applications\Loupedeck70\@_defaultwin'
    $info = Join-Path $app 'ApplicationInfo.json'
    if (-not (Test-Path -LiteralPath $info)) { return $null }
    $nexus = $null; $origin = $null
    foreach ($folder in @(Get-ChildItem -LiteralPath (Join-Path $app 'Profiles') -Directory -ErrorAction SilentlyContinue)) {
        $file = Join-Path $folder.FullName 'ProfileInfo.json'
        if (-not (Test-Path -LiteralPath $file)) { continue }
        $data = Get-Content -LiteralPath $file -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($data.displayName -ceq $Plugin -and "$($data.description)".StartsWith($ProfileMark)) {
            $nexus = $folder.Name; $origin = "$($data.description)".Substring($ProfileMark.Length)
        }
    }
    [pscustomobject]@{
        App = $app; Info = $info; Nexus = $nexus; Origin = $origin
        Active = (Get-Content -LiteralPath $info -Raw -Encoding UTF8 | ConvertFrom-Json).defaultProfileName
    }
}

function Set-ActiveProfile($Profiles, [string]$Name) {
    $text = [IO.File]::ReadAllText($Profiles.Info)
    $text = ([regex]'("defaultProfileName"\s*:\s*")[^"]*(")').Replace($text, "`${1}$Name`${2}", 1)
    [IO.File]::WriteAllText($Profiles.Info, $text, [Text.UTF8Encoding]::new($false))
}

function New-KeypadProfile($Profiles) {
    # A copy of the active profile (its other keys and pages stay), renamed, before the keys are placed on it.
    $origin = $Profiles.Active
    $source = Join-Path $Profiles.App "Profiles\$origin"
    $name = [guid]::NewGuid().ToString('N').ToUpperInvariant()
    $target = Join-Path $Profiles.App "Profiles\$name"
    Copy-Item -LiteralPath $source -Destination $target -Recurse
    $file = Join-Path $target 'ProfileInfo.json'
    # Earlier versions placed the keys on the original profile itself, after this one-time backup: start from it.
    $legacy = Join-Path $target 'ProfileInfo.before-nexuskeypad.json'
    if (Test-Path -LiteralPath $legacy) { Move-Item -LiteralPath $legacy -Destination $file -Force }
    $text = [IO.File]::ReadAllText($file)
    # Logi writes name, displayName and description first, before the layout: anything else is an unknown format.
    if ($text -notmatch '^\s*\{\s*("\$type"\s*:\s*"[^"]*"\s*,\s*)?"name"\s*:\s*"[^"]*"\s*,\s*("profileFlags"\s*:\s*"[^"]*"\s*,\s*)?"displayName"\s*:\s*"[^"]*"\s*,\s*"description"\s*:\s*(null|"[^"]*")') {
        Remove-Item -LiteralPath $target -Recurse -Force
        throw 'Unknown Logi profile format: keys left to place by hand.'
    }
    $text = ([regex]'("name"\s*:\s*")[^"]*(")').Replace($text, "`${1}$name`${2}", 1)
    $text = ([regex]'("displayName"\s*:\s*")[^"]*(")').Replace($text, "`${1}$Plugin`${2}", 1)
    $text = ([regex]'("description"\s*:\s*)(null|"[^"]*")').Replace($text, "`${1}`"$ProfileMark$origin`"", 1)
    [IO.File]::WriteAllText($file, $text, [Text.UTF8Encoding]::new($false))
    $yaml = Join-Path $target 'metadata\LoupedeckPackage.yaml'
    if (Test-Path -LiteralPath $yaml) {
        $lines = @(Get-Content -LiteralPath $yaml -Encoding UTF8 | ForEach-Object { if ($_ -match '^displayName:') { "displayName: $Plugin" } else { $_ } })
        [IO.File]::WriteAllLines($yaml, $lines, [Text.UTF8Encoding]::new($false))
    }
    return $name
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
    # A process that is exiting (during a stop) no longer reports its path: once gone, it is not an impostor.
    $processes = @($processes | Where-Object {
        if ($_.ExecutablePath) { return $true }
        Start-Sleep -Milliseconds 300
        [bool](Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue)
    })
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

function Stop-Logi([int]$TimeoutSeconds = 15, [int]$QuietSeconds = 5, [int]$Attempts = 6) {
    if ($Sandbox) { return }
    $runtime = Get-LogiPluginRuntime
    $before = @(Get-LogiPluginProcesses $runtime)
    if ($before.Count -eq 0) { return }
    # The official control bus stops only the plugin service cooperatively. Never stop the keyboard/mouse agent.
    # The Logi Options+ agent restarts a just-stopped plugin service about a second later, sometimes twice (the
    # restarted service can exit by itself some 15 s later and come back again). A restarted service that is still
    # starting may also ignore a stop: its processes outlive the wait, and the stop is sent again (09/11/2026: two
    # maintenance runs refused 1.9.4 with "did not stop in 30 seconds"). Stopped means no process during $QuietSeconds.
    $seen = @($before.ProcessId)
    $completed = $false
    $control = $null
    try {
        for ($attempt = 1; ; $attempt++) {
            if ($control) { $control.Dispose() }
            $completed = $false
            $control = Start-Process -FilePath $runtime.Tool -ArgumentList 'stop' -WindowStyle Hidden -PassThru
            $completed = $control.WaitForExit(30000)
            if (-not $completed) { throw 'The official Logi stop command is still running; its outcome is unknown. Installation refused.' }
            if ($control.ExitCode -ne 0) { throw 'The official Logi plugin service stop command failed. Installation refused.' }
            # The stopped processes exit (a restarted service may already run beside them).
            $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
            $ignored = $false
            while (@(Get-LogiPluginProcesses $runtime | Where-Object { $_.ProcessId -in $seen }).Count -gt 0) {
                if ([DateTime]::UtcNow -ge $deadline) { $ignored = $true; break }
                Start-Sleep -Milliseconds 100
            }
            if ($ignored) {
                if ($attempt -ge $Attempts) { throw "Logi plugin service did not stop after $attempt stop commands. Installation refused." }
                $seen += @(Get-LogiPluginProcesses $runtime | ForEach-Object ProcessId)
                continue
            }
            # Then the agent gets time to restart it.
            $back = @()
            $quiet = [DateTime]::UtcNow.AddSeconds($QuietSeconds)
            do {
                $back = @(Get-LogiPluginProcesses $runtime)
                if ($back.Count -gt 0) { break }
                Start-Sleep -Milliseconds 250
            } while ([DateTime]::UtcNow -lt $quiet)
            if ($back.Count -eq 0) { break }
            if ($attempt -ge $Attempts) { throw 'Logi Options+ keeps restarting the plugin service. Installation refused.' }
            $seen += @($back.ProcessId)
            # Let the restarted service finish starting (its extension process appears last) before stopping it.
            $ready = [DateTime]::UtcNow.AddSeconds($QuietSeconds)
            while (@($back | Where-Object Name -eq 'LogiPluginServiceExt.exe').Count -eq 0 -and [DateTime]::UtcNow -lt $ready) {
                Start-Sleep -Milliseconds 250
                $back = @(Get-LogiPluginProcesses $runtime)
                $seen += @($back.ProcessId)
            }
        }
        $script:LogiStoppedProcessIds = $seen
    } catch {
        $stopFailure = $_
        # A completed command can fail after it stopped the service. Restore only a verified fully stopped runtime;
        # never race an in-flight command or start another service beside a leftover extension process.
        if ($completed) {
            try { if (@(Get-LogiPluginProcesses $runtime).Count -eq 0) { Start-Logi } }
            catch { Write-Warning "Cooperative recovery of the plugin service was refused: $($_.Exception.Message)" }
        }
        throw $stopFailure
    } finally { if ($control) { $control.Dispose() } }
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
    # Places the plugin's 18 keys on its own "NexusKeypad" profile and makes it active (or, with -Clear, makes the
    # original profile active again and deletes it), Logi stopped. The user's own profile is left as it is, except a
    # profile edited by an earlier version, restored from its one-time backup.
    $profiles = Get-KeypadProfiles
    if (-not $profiles) { Say 'No MX Keypad profile found: connect the keypad and open Logi Options+ once, then place the keys by hand.' Yellow; return 'guided' }
    $legacy = $null
    $restore = if ($profiles.Nexus) { $profiles.Origin } else { $profiles.Active }
    $legacyBackup = Join-Path $profiles.App "Profiles\$restore\ProfileInfo.before-nexuskeypad.json"
    if (Test-Path -LiteralPath $legacyBackup) { $legacy = $legacyBackup }
    if ($Clear -and -not $profiles.Nexus -and -not $legacy) { return 'absent' }
    Stop-Logi
    $code = 0
    try {
        if ($Clear) {
            if ($profiles.Nexus) {
                if ($profiles.Active -ceq $profiles.Nexus -and (Test-Path -LiteralPath (Join-Path $profiles.App "Profiles\$($profiles.Origin)"))) {
                    Set-ActiveProfile $profiles $profiles.Origin
                }
                Remove-Item -LiteralPath (Join-Path $profiles.App "Profiles\$($profiles.Nexus)") -Recurse -Force
            }
        } else {
            $name = if ($profiles.Nexus) { $profiles.Nexus } else {
                try { New-KeypadProfile $profiles } catch { Say $_.Exception.Message Yellow; $null }
            }
            if ($name) {
                & $Hook keys (Join-Path $profiles.App "Profiles\$name\ProfileInfo.json") $Plugin | Out-Host
                $code = $LASTEXITCODE
            } else { $code = 1 }
            if (-not $code) { Set-ActiveProfile $profiles $name }
        }
        if ($legacy -and -not $code) { Move-Item -LiteralPath $legacy -Destination (Join-Path (Split-Path $legacy) 'ProfileInfo.json') -Force }
    } finally {
        Start-Logi
    }
    if ($code) { Say 'The keys could not be placed: place them by hand in Logi Options+ (your profile is unchanged).' Yellow; return 'failed' }
    if ($Clear) { return 'removed' } else { return 'placed' }
}

function Set-Connectors([string]$Hook, [switch]$Update) {
    # Connects each AI app found (Claude, Codex, Antigravity) to the installed nexuskeypad-hook.exe: the NexusKeypad plugin
    # of NexusSystem's catalog when that app has it installed, else the same plugin installed locally. The helper keeps
    # third-party settings, never leaves two nexus-keypad copies enabled, and reports an absent app as Absent. A failure
    # only leaves that key unplugged.
    # On an update, only the connections already enabled are refreshed (a new package carries a new connector, which
    # leaves the old one to repair); a connection the user turned off or never set up stays as it is.
    if ($Sandbox) { return 'left (test folder)' }  # the real AI profiles are not the test's
    $states = foreach ($agent in 'claude', 'codex', 'antigravity') {
        $state = $null
        if ($Update) {
            $state = try { ((& $Hook connecteur $agent status 2>$null) | ConvertFrom-Json).state } catch { $null }
            if ($state -and $state -ne 'Reparer') { "$agent=$state"; continue }
        }
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
            # A development build vX.Y.Z-dev.N carries the X.Y.Z package (NexusKeypad_X_Y_Z.lplug4).
            $name = '{0}_{1}.lplug4' -f $Plugin, (($tag.TrimStart('v') -replace '-dev\.\d+$', '') -replace '\.', '_')
            $base = "https://github.com/pironjulien/NexusKeypad/releases/download/$tag"
            Say "Downloading NexusKeypad $tag..."
            $package = Join-Path $work $name
            Invoke-WebRequest -UseBasicParsing "$base/$name" -OutFile $package
            $manifest = (Invoke-WebRequest -UseBasicParsing "$base/SHA256SUMS.txt").RawContentStream.ToArray()
            $expected = Get-PackageHash $manifest $name
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
    # Keys already on the active NexusKeypad profile (an update): nothing to ask nor to explain.
    $profiles = Get-KeypadProfiles
    $placed = $profiles -and $profiles.Nexus -and $profiles.Active -ceq $profiles.Nexus -and
        (Select-String -LiteralPath (Join-Path $profiles.App "Profiles\$($profiles.Nexus)\ProfileInfo.json") -SimpleMatch "`$$($Plugin)___" -Quiet)
    if ($placed -and $place -ne 'yes') { $place = 'already placed' }
    if ($place -eq 'ask') {
        Say ''
        $answer = Read-Host 'Place the 18 keys on the keypad now? The plugin service restarts briefly and your current profile is backed up first (y/N)'
        if ($answer -match '^(y|yes|o|oui)$') { $place = 'yes' } else { $place = 'no' }
    }
    $Results['Keys'] = switch ($place) { 'yes' { Set-Keys $hook } 'already placed' { 'already placed' } default { 'guided' } }
    $Results['Live states'] = 'asked by the plugin'
    $Results['AI connections'] = Set-Connectors $hook -Update:$previous
    Say ''
    if ($Results['Keys'] -notin 'placed', 'already placed') {
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
        $Results['Keys'] = Set-Keys $hook -Clear
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
    Say 'NexusKeypad removed. Your own keypad profile is active again.' Green
    return 0
}

if ($LogiService) {
    if ($LogiService -eq 'stop') { Stop-Logi } else { Start-Logi }
    return
}
if ($KeysOnly) {
    $profiles = Get-KeypadProfiles
    if ($profiles -and $profiles.Nexus -and $profiles.Active -ceq $profiles.Nexus -and
        (Select-String -LiteralPath (Join-Path $profiles.App "Profiles\$($profiles.Nexus)\ProfileInfo.json") -SimpleMatch "`$$($Plugin)___" -Quiet)) {
        Say 'Keys: already placed.'; exit 0
    }
    $result = Set-Keys (Join-Path $Target 'bin\nexuskeypad-hook.exe')
    Say "Keys: $result"
    exit $(if ($result -in 'placed', 'guided') { 0 } else { 1 })
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

# NexusKeypad complete edition, outside the Logi Marketplace, for Windows 10/11 (Windows PowerShell 5.1 or PowerShell 7),
# no administrator rights. Everything the author runs on his own keypad, in one line:
#   irm https://raw.githubusercontent.com/pironjulien/NexusKeypad/main/get.ps1 | iex
# Remove it (plugin, live states, keys; Logi Options+ itself is left alone):
#   & ([scriptblock]::Create((irm https://raw.githubusercontent.com/pironjulien/NexusKeypad/main/get.ps1))) -Remove
#
# It downloads the latest release's NexusKeypad package (checked against the release's SHA256SUMS.txt; its build
# provenance can be checked with `gh attestation verify <file> --repo pironjulien/NexusKeypad`), copies it into Logi
# Options+'s plugin folder (%LOCALAPPDATA%\Logi\LogiPluginService\Plugins\NexusKeypad), where the Logi Plugin Service
# loads it, then offers to place the 18 keys on the keypad's general profile (backed up first; Logi Options+ restarts
# for a few seconds). The plugin itself asks, at its first start, whether to turn on Claude's and Antigravity's live
# states. Run it again to update.
#   -Keys yes|no   place the keys without asking (yes) or leave them to drag in Logi Options+ (no)
# NEXUS_PACK=1 (the Nexus pack, get-nexus.ps1): no question, no wait, no window, keys left to place by hand; exit code
# 0 on success; a last "NEXUS_PACK_RESULT ..." line.
# For tests only: NEXUS_KEYPAD_SOURCE (a local .lplug4 instead of the latest release) and NEXUS_KEYPAD_LOGI (another
# Logi Plugin Service data folder; the real service is then never stopped or started). A local package aimed at the real
# Logi folder is refused (a test that lost its test folder must not install for real) unless -Real is given.
[CmdletBinding()]
param([switch]$Remove, [ValidateSet('ask', 'yes', 'no')] [string]$Keys = 'ask', [switch]$Real)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'  # Invoke-WebRequest is many times slower with its progress bar
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$Plugin = 'NexusKeypad'
$Pack = $env:NEXUS_PACK -eq '1'
$Sandbox = [bool]$env:NEXUS_KEYPAD_LOGI
$Logi = if ($Sandbox) { $env:NEXUS_KEYPAD_LOGI } else { Join-Path $env:LOCALAPPDATA 'Logi\LogiPluginService' }
$Target = Join-Path $Logi "Plugins\$Plugin"
$Agent = Join-Path $env:ProgramFiles 'LogiOptionsPlus\logioptionsplus_agent.exe'
$Results = [ordered]@{}

function Say([string]$Text, [string]$Color = 'Gray') { Write-Host $Text -ForegroundColor $Color }

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

function Stop-Logi {
    if ($Sandbox) { return }
    Get-Process LogiPluginService, LogiPluginServiceExt, logioptionsplus_agent -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
}

function Start-Logi {
    if ($Sandbox -or -not (Test-Path -LiteralPath $Agent)) { return }
    # Detached and out of the caller's job object: started from an app (an AI agent's shell), the agent would otherwise
    # die with that app.
    if (-not ('NexusKeypadLauncher' -as [type])) {
        Add-Type @'
using System; using System.Runtime.InteropServices;
public static class NexusKeypadLauncher {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO { public int cb; public string r, d, t; public int x, y, w, h, xc, yc, fa, fl; public short sw, cb2; public IntPtr r2, i, o, e; }
    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION { public IntPtr hp, ht; public int pid, tid; }
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcess(string app, string cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr h);
    public static bool Start(string line, string dir) {
        var si = new STARTUPINFO(); si.cb = Marshal.SizeOf(si);
        PROCESS_INFORMATION pi;
        // CREATE_BREAKAWAY_FROM_JOB | CREATE_NEW_PROCESS_GROUP | DETACHED_PROCESS
        if (!CreateProcess(null, line, IntPtr.Zero, IntPtr.Zero, false, 0x01000000 | 0x00000200 | 0x00000008, IntPtr.Zero, dir, ref si, out pi)) return false;
        CloseHandle(pi.hp); CloseHandle(pi.ht);
        return true;
    }
}
'@
    }
    if (-not [NexusKeypadLauncher]::Start("`"$Agent`" /minimized", (Split-Path $Agent))) {
        Start-Process -FilePath $Agent -ArgumentList '/minimized'
    }
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

function Install-NexusKeypad {
    if (-not $Sandbox -and -not (Test-Path -LiteralPath (Join-Path $env:ProgramFiles 'Logi\LogiPluginService'))) {
        Say 'Logi Options+ is not installed: get it from https://www.logitech.com/software/logi-options-plus.html, then run this again.' Red
        $Results['Plugin'] = 'no Logi Options+'
        return 1
    }
    $work = Join-Path ([IO.Path]::GetTempPath()) "nexuskeypad-$PID"
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
            $expected = ([Text.Encoding]::UTF8.GetString((Invoke-WebRequest -UseBasicParsing "$base/SHA256SUMS.txt").RawContentStream.ToArray()) -split "`n" |
                Where-Object { $_ -match "^([0-9a-f]{64})\s+\*?$([regex]::Escape($name))\s*$" } | ForEach-Object { $Matches[1] }) | Select-Object -First 1
            $actual = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($expected -ne $actual) { throw "SHA-256 mismatch for $($name): the download is not the published package." }
        }
        $unpacked = Join-Path $work 'package'
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::ExtractToDirectory($package, $unpacked)
        if (-not (Test-Path -LiteralPath (Join-Path $unpacked "bin\$($Plugin)Plugin.dll"))) { throw 'Not a NexusKeypad package.' }
        # Mirror into the plugin folder: the Logi Plugin Service watches it and (re)loads the plugin by itself.
        New-Item -ItemType Directory -Force -Path $Target | Out-Null
        & robocopy.exe $unpacked $Target /MIR /NFL /NDL /NJH /NJS /NP /R:5 /W:1 | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "Copy to $Target failed (robocopy $LASTEXITCODE)." }
        Get-ChildItem -LiteralPath $Target -Recurse -File | Unblock-File
        $Results['Plugin'] = 'installed'
        Say "NexusKeypad is in $Target." Green
    } finally {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }

    $hook = Join-Path $Target 'bin\nexuskeypad-hook.exe'
    $place = $Keys
    if ($Pack) { $place = 'no' }
    if ($place -eq 'ask') {
        Say ''
        $answer = Read-Host 'Place the 18 keys on the keypad now? Logi Options+ restarts for a few seconds and your current profile is backed up first (y/N)'
        if ($answer -match '^(y|yes|o|oui)$') { $place = 'yes' } else { $place = 'no' }
    }
    if ($place -eq 'yes') { $Results['Keys'] = Set-Keys $hook } else { $Results['Keys'] = 'guided' }
    $Results['Live states'] = 'asked by the plugin'
    Say ''
    if ($Results['Keys'] -ne 'placed') {
        Say 'Keys: in Logi Options+, open the MX Keypad, Customize keys, and drag Key 1 to Key 9 of the "AI agents" group onto page 1, then Key 10 to Key 18 onto page 2.'
    }
    Say 'Live states: at its first start the plugin asks whether to show what Claude and Antigravity are doing as it happens (answer Yes).'
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
# Run as a file (the Nexus pack, -File): its exit code. Run by `irm | iex` or a script block: never `exit`, which would
# close the user's window; a failure is an error instead.
if ($PSCommandPath) { exit $code }
if ($code) { throw 'NexusKeypad: not installed (see above).' }

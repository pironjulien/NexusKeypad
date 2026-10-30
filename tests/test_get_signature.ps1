# Signature and downloaded-install path of get.ps1, using disposable keys and a fake Logi folder only.
# Compatible with Windows PowerShell 5.1 and PowerShell 7; no service, profile or real signing key is touched.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$installer = Join-Path $repo 'get.ps1'
$source = [IO.File]::ReadAllText($installer)
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($installer, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw "get.ps1 syntax errors: $($errors -join ', ')" }
# Import only the tested functions; never dot-source the installer's top-level execution.
foreach ($name in 'Test-ReleaseSignature', 'Get-VerifiedPackageHash', 'Set-Connectors', 'Install-NexusKeypad') {
    $definition = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $true) | Where-Object Name -eq $name)
    if ($definition.Count -ne 1) { throw "Expected exactly one function $name." }
    . ([scriptblock]::Create($definition[0].Extent.Text))
}
$checks = 0
function Assert([bool]$Condition, [string]$Message) {
    $script:checks++
    if (-not $Condition) { throw $Message }
}
function Refuses([scriptblock]$Action, [string]$Reason) {
    try { $null = & $Action } catch { return $_.Exception.Message -match $Reason }
    return $false
}

# The installer's trust roots must stay identical to those used by already installed automatic updaters.
$installerKeys = [regex]::Match($source, '(?s)\$ReleaseKeys\s*=\s*@\((.*?)\)').Groups[1].Value
$updaterKeys = [regex]::Match([IO.File]::ReadAllText((Join-Path $repo 'plugin\src\Trinite\MiseAJour.cs')), 'String\[\] Cles =\s*\[([^\]]+)\]').Groups[1].Value
$fromInstaller = @([regex]::Matches($installerKeys, "'([A-Za-z0-9+/=]+)'") | ForEach-Object { $_.Groups[1].Value })
$fromUpdater = @([regex]::Matches($updaterKeys, '"([A-Za-z0-9+/=]+)"') | ForEach-Object { $_.Groups[1].Value })
Assert ($fromInstaller.Count -gt 0 -and -not (Compare-Object $fromInstaller $fromUpdater -CaseSensitive)) 'Installer and updater public keys differ.'

$key = [Security.Cryptography.ECDsaCng]::new(256)
$other = [Security.Cryptography.ECDsaCng]::new(256)
$root = Join-Path $HOME ('.nexus\build\nexuskeypad\test\installer-signature\' + [guid]::NewGuid().ToString('N'))
$root = [IO.Path]::GetFullPath($root)
$allowed = [IO.Path]::GetFullPath((Join-Path $HOME '.nexus\build\nexuskeypad\test\installer-signature')) + '\'
if (-not $root.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid isolated test path.' }
$oldSource = [Environment]::GetEnvironmentVariable('NEXUS_KEYPAD_SOURCE', 'Process')
function PublicKey([Security.Cryptography.ECDsaCng]$Value) {
    $blob = $Value.Key.Export([Security.Cryptography.CngKeyBlobFormat]::EccPublicBlob)
    $prefix = [byte[]]('30-59-30-13-06-07-2A-86-48-CE-3D-02-01-06-08-2A-86-48-CE-3D-03-01-07-03-42-00-04'.Split('-') | ForEach-Object { [Convert]::ToByte($_, 16) })
    $spki = New-Object byte[] 91
    [Array]::Copy($prefix, 0, $spki, 0, 27)
    [Array]::Copy($blob, 8, $spki, 27, 64)
    return [Convert]::ToBase64String($spki)
}
function Signature([byte[]]$Content, [Security.Cryptography.ECDsaCng]$Signer = $key) {
    return [Convert]::ToBase64String($Signer.SignData($Content, [Security.Cryptography.HashAlgorithmName]::SHA256))
}
try {
    $public = PublicKey $key
    $ReleaseKeys = @($public)
    $packageName = 'NexusKeypad_9_8_7.lplug4'
    $hash = 'a' * 64
    $manifest = [Text.Encoding]::UTF8.GetBytes("$hash  $packageName`n")
    $signature = Signature $manifest
    Assert (Test-ReleaseSignature $manifest $signature) 'Disposable-key P1363 signature should pass.'
    Assert ((Get-VerifiedPackageHash $manifest $signature $packageName) -ceq $hash) 'Signed package hash should be returned.'
    Assert (-not (Test-ReleaseSignature $manifest $signature $fromInstaller)) 'Disposable signing key must not match production trust roots.'
    Assert (-not (Test-ReleaseSignature $manifest (Signature $manifest $other))) 'Unknown key should be refused.'
    foreach ($invalid in @('', 'not-base64', [Convert]::ToBase64String((New-Object byte[] 63)), [Convert]::ToBase64String((New-Object byte[] 64)))) {
        Assert (-not (Test-ReleaseSignature $manifest $invalid)) 'Missing, malformed or false signature should be refused.'
    }
    $modified = [Text.Encoding]::UTF8.GetBytes("$('b' * 64)  $packageName`n")
    Assert (-not (Test-ReleaseSignature $modified $signature)) 'Manifest modification must invalidate its signature.'
    Assert (Refuses { Get-VerifiedPackageHash $manifest '' $packageName } 'no valid release signature') 'Unsigned manifest must fail closed.'
    foreach ($text in @("$hash  other.lplug4`n", "$hash  $packageName`n$hash  $packageName`n", "not a checksum`n")) {
        $bytes = [Text.Encoding]::UTF8.GetBytes($text)
        Assert (Refuses { Get-VerifiedPackageHash $bytes (Signature $bytes) $packageName } 'absent|more than once|invalid entry') 'Signed but ambiguous/invalid manifest must be refused.'
    }
    $withBom = [byte[]](@(0xef, 0xbb, 0xbf) + @($manifest))
    Assert ((Get-VerifiedPackageHash $withBom (Signature $withBom) $packageName) -ceq $hash) 'Signed UTF-8 BOM must be verified before decoding.'
    Assert (-not (Test-ReleaseSignature $manifest $signature @('invalid key'))) 'Malformed trust root must not allow installation.'

    # A valid ZIP is deliberately used in negative download tests. A missing signature must fail before it is
    # extracted or copied, even when the package and checksum have both been replaced consistently.
    New-Item -ItemType Directory -Force (Join-Path $root 'fixture\bin') | Out-Null
    [IO.File]::WriteAllText((Join-Path $root 'fixture\bin\NexusKeypadPlugin.dll'), 'isolated fixture, never loaded')
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $package = Join-Path $root $packageName
    [IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $root 'fixture'), $package)
    $packageBytes = [IO.File]::ReadAllBytes($package)
    $packageHash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
    $signedManifest = [Text.Encoding]::UTF8.GetBytes("$packageHash  $packageName`n")
    $wrongHashManifest = [Text.Encoding]::UTF8.GetBytes("$hash  $packageName`n")
    $goodSignature = Signature $signedManifest
    $Plugin = 'NexusKeypad'
    $Sandbox = $true
    $Pack = $true
    $Keys = 'no'
    $script:downloadCase = ''
    $script:downloads = @()
    function Say([string]$Text, [string]$Color = 'Gray') { }
    function Get-LatestTag { return 'v9.8.7' }
    function Invoke-WebRequest([string]$Uri, [switch]$UseBasicParsing, [string]$OutFile) {
        $script:downloads += $Uri
        if ($Uri.EndsWith('/' + $packageName)) {
            [IO.File]::WriteAllBytes($OutFile, $packageBytes)
            return
        }
        if ($Uri.EndsWith('/SHA256SUMS.txt.sig')) {
            if ($script:downloadCase -eq 'missing-signature') { throw 'fixture signature unavailable' }
            $sig = if ($script:downloadCase -eq 'wrong-key') { Signature $signedManifest $other }
                elseif ($script:downloadCase -eq 'wrong-hash') { Signature $wrongHashManifest }
                else { $goodSignature }
            return [pscustomobject]@{ RawContentStream = [IO.MemoryStream]::new([Text.Encoding]::UTF8.GetBytes($sig)) }
        }
        if ($Uri.EndsWith('/SHA256SUMS.txt')) {
            $bytes = if ($script:downloadCase -in 'modified-manifest', 'wrong-hash') { $wrongHashManifest } else { $signedManifest }
            return [pscustomobject]@{ RawContentStream = [IO.MemoryStream]::new($bytes) }
        }
        throw "Unexpected network operation in isolated test: $Uri"
    }
    [Environment]::SetEnvironmentVariable('NEXUS_KEYPAD_SOURCE', $null, 'Process')
    $installerWork = Join-Path ([IO.Path]::GetTempPath()) "nexuskeypad-$PID"
    if (Test-Path -LiteralPath $installerWork) { throw 'The installer test work folder already exists; refusing to reuse it.' }
    foreach ($case in 'missing-signature', 'wrong-key', 'modified-manifest', 'wrong-hash') {
        $script:downloadCase = $case
        $script:downloads = @()
        $Logi = Join-Path $root $case
        $Target = Join-Path $Logi 'Plugins\NexusKeypad'
        $Results = [ordered]@{}
        Assert (Refuses { Install-NexusKeypad } 'signature unavailable|no valid release signature|SHA-256 mismatch') "Download $case must be refused."
        Assert (-not (Test-Path -LiteralPath $Target)) "Download $case must not create the target plugin folder."
        Assert ($script:downloads.Count -eq 3) "Download $case must request the detached signature."
    }
    # Successful signed download uses the exact same extraction/copy function, still only into our fake Logi folder.
    $script:downloadCase = 'valid'
    $Logi = Join-Path $root 'valid'
    $Target = Join-Path $Logi 'Plugins\NexusKeypad'
    $Results = [ordered]@{}
    Assert ((Install-NexusKeypad) -eq 0) 'Valid signed package should install in the fake Logi folder.'
    Assert (Test-Path -LiteralPath (Join-Path $Target 'bin\NexusKeypadPlugin.dll')) 'Signed fixture should be copied only after verification.'
    Assert ($Results['Plugin'] -eq 'installed' -and $Results['Keys'] -eq 'guided' -and $Results['AI connections'] -eq 'left (test folder)') 'Signed fixture installation should not place keys or run a helper.'
} finally {
    [Environment]::SetEnvironmentVariable('NEXUS_KEYPAD_SOURCE', $oldSource, 'Process')
    $key.Dispose()
    $other.Dispose()
    if ((Test-Path -LiteralPath $root) -and $root.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
Write-Host "get.ps1 signature: $checks isolated checks passed (PowerShell $($PSVersionTable.PSVersion))."

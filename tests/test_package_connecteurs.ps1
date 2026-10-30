# Execute the actual helper shipped inside a new .lplug4, using disposable profiles only.
# No Logi process, host application, user plugin/profile, real hook or signing key is used.
[CmdletBinding()]
param([Parameter(Mandatory)] [string]$PackagePath)
$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath $PackagePath).Path
$root = [IO.Path]::GetFullPath((Join-Path $HOME ('.nexus\build\nexuskeypad\test\package-connecteurs\' + [guid]::NewGuid().ToString('N'))))
$allowed = [IO.Path]::GetFullPath((Join-Path $HOME '.nexus\build\nexuskeypad\test\package-connecteurs')) + '\'
if (-not $root.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid isolated test path.' }
$extracted = Join-Path $root 'package'
# Spaces are deliberate: the actual Windows hook path rendering is part of this package test.
$user = Join-Path $root 'user with space'
$local = Join-Path $root 'local appdata'
$checks = 0
$utf8 = [Text.UTF8Encoding]::new($false)
function Assert([bool]$Condition, [string]$Message) {
    $script:checks++
    if (-not $Condition) { throw $Message }
}
function WriteText([string]$Path, [string]$Value) {
    New-Item -ItemType Directory -Force (Split-Path $Path) | Out-Null
    [IO.File]::WriteAllText($Path, $Value, $utf8)
}
function InvokeConnector([string]$Agent, [string]$Action) {
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = Join-Path $extracted 'bin\nexuskeypad-hook.exe'
    $info.Arguments = "connecteur $Agent $Action" # fixed values supplied by this test only
    $info.WorkingDirectory = $root
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.EnvironmentVariables['USERPROFILE'] = $user
    $info.EnvironmentVariables['LOCALAPPDATA'] = $local
    $info.EnvironmentVariables['APPDATA'] = Join-Path $user 'AppData\Roaming'
    $info.EnvironmentVariables['CLAUDE_CONFIG_DIR'] = Join-Path $user '.claude'
    $info.EnvironmentVariables['NEXUSKEYPAD_FIXTURE_ROOT'] = $root
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    try {
        if (-not $process.Start()) { throw 'Isolated helper did not start.' }
        $process.StandardInput.Close()
        $output = $process.StandardOutput.ReadToEndAsync()
        $errors = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Isolated helper exceeded 30 seconds.' }
        $text = $output.GetAwaiter().GetResult()
        $errorText = $errors.GetAwaiter().GetResult()
        if ($process.ExitCode -or $errorText) { throw "Isolated connector $Agent $Action failed ($($process.ExitCode)): $text $errorText" }
        return $text | ConvertFrom-Json
    } finally { $process.Dispose() }
}
try {
    New-Item -ItemType Directory -Force $root, $user, $local | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($package, $extracted)
    Assert (Test-Path -LiteralPath (Join-Path $extracted 'bin\nexuskeypad-hook.exe')) 'Native helper is absent from the package.'
    Assert (Test-Path -LiteralPath (Join-Path $extracted 'bin\NexusKeypadPlugin.dll')) 'Complete NexusKeypad assembly is absent from the package.'
    $version = (Get-Content (Join-Path $extracted 'bin\connecteurs\codex\.codex-plugin\plugin.json') -Raw | ConvertFrom-Json).version
    foreach ($agent in 'claude', 'codex', 'antigravity') {
        Assert ((InvokeConnector $agent 'status').state -eq 'Absent') "Fresh profile should report absent $agent before first host setup."
    }
    WriteText (Join-Path $user '.claude\settings.json') '{"env":{"KEEP":"untouched"},"enabledPlugins":{"third-party@market":false}}'
    WriteText (Join-Path $user '.codex\config.toml') "model = 'keep'`nnotify = ['one', 'two']`n[plugins.'third-party@personal']`nenabled = false`n"
    WriteText (Join-Path $user '.gemini\config\config.json') '{"plugins":{"third-party":{"enabled":false}},"keep":"untouched"}'
    WriteText (Join-Path $user '.gemini\config\hooks.json') '{"third-party":{"Stop":[]}}'
    # No Claude installation exists on Windows CI. Build a strict plugin-management fixture in the disposable
    # Claude-Data tree only. The production helper discovers and starts it as a separate process; there is no
    # runtime test flag, alternate registry writer or trust bypass in the distributed connector.
    $cliSource = Join-Path $root 'cli-fixture'
    $cliOutput = Join-Path $local 'Claude-Data\fixture\claude-code\0.0.0\fixture'
    WriteText (Join-Path $cliSource 'claude.csproj') '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><AssemblyName>claude</AssemblyName><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>'
    WriteText (Join-Path $cliSource 'Program.cs') @'
using System.Text.Json.Nodes;
var root = Path.GetFullPath(Environment.GetEnvironmentVariable("NEXUSKEYPAD_FIXTURE_ROOT") ?? throw new Exception("No isolated fixture root.")) + Path.DirectorySeparatorChar;
string Bound(string value) { var p = Path.GetFullPath(value); if (!p.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new Exception("Fixture path outside test root."); return p; }
var profile = Bound(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? "");
var local = Bound(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "");
var market = Bound(Path.Combine(local, "NexusKeypad", "connecteurs", "claude"));
var plugins = Path.Combine(profile, "plugins");
Directory.CreateDirectory(plugins);
void Write(string file, JsonObject value) { Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, value.ToJsonString()); }
if (args.Length == 7 && args.Take(3).SequenceEqual(new[]{"plugin","marketplace","add"}) && Bound(args[3]) == market && args[4] == "--scope" && args[5] == "user" && args[6] == "--json")
    Write(Path.Combine(plugins,"known_marketplaces.json"), new JsonObject { ["nexuskeypad"] = new JsonObject { ["source"] = new JsonObject { ["source"]="directory", ["path"]=market }, ["installLocation"]=market } });
else if (args.Length == 6 && args[0] == "plugin" && args[2] == "nexus-keypad@nexuskeypad" && args[3] == "--scope" && args[4] == "user" && args[5] == "--json" && args[1] is "install" or "update" or "enable" or "uninstall")
{
    if (args[1] == "uninstall") File.Delete(Path.Combine(plugins,"installed_plugins.json"));
    if (args[1] is "install" or "update")
    {
        var source = Path.Combine(market,"nexus-keypad");
        var version = JsonNode.Parse(File.ReadAllText(Path.Combine(source,".claude-plugin","plugin.json")))!["version"]!.GetValue<string>();
        var cache = Path.Combine(plugins,"cache","nexuskeypad","nexus-keypad",version);
        foreach (var file in Directory.EnumerateFiles(source,"*",SearchOption.AllDirectories)) { var copy=Path.Combine(cache,Path.GetRelativePath(source,file)); Directory.CreateDirectory(Path.GetDirectoryName(copy)!); File.Copy(file,copy,true); }
        Write(Path.Combine(plugins,"installed_plugins.json"),new JsonObject { ["version"]=2,["plugins"]=new JsonObject { ["nexus-keypad@nexuskeypad"]=new JsonArray(new JsonObject { ["scope"]="user",["version"]=version,["installPath"]=cache }) } });
    }
}
else throw new Exception("Fixture only permits the exact official plugin-management commands.");
File.AppendAllText(Path.Combine(profile,"fixture-cli-commands.log"),string.Join("|",args)+"\n");
Console.WriteLine("{\"outcome\":\"ok\"}");
'@
    & dotnet build (Join-Path $cliSource 'claude.csproj') -c Release -nologo -v q -o $cliOutput
    Assert ($LASTEXITCODE -eq 0) 'The isolated CLI process fixture did not compile.'
    foreach ($agent in 'claude', 'codex', 'antigravity') {
        Assert ((InvokeConnector $agent 'status').state -eq 'Activer') "Fresh host profile should offer setup for $agent."
        Assert ((InvokeConnector $agent 'enable').state -eq 'Pret') "Package-native setup should prepare $agent."
        Assert ((InvokeConnector $agent 'status').state -eq 'Pret') "Package-native status should attest $agent files and activation."
        $receiptFile = Join-Path $local "NexusKeypad\connecteurs\etats\$agent.json"
        $receiptBefore = [IO.File]::ReadAllText($receiptFile)
        $receipt = $receiptBefore | ConvertFrom-Json
        Assert ($receipt.version -eq $version -and $receipt.active) "Receipt version/activation differs for $agent."
        foreach ($property in $receipt.files.PSObject.Properties) {
            $path = [IO.Path]::GetFullPath($property.Name)
            Assert ($path.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) 'Receipt unexpectedly points outside the isolated root.'
            Assert ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ceq $property.Value) "Package file hash differs for $agent."
        }
        $runtimeFile = Join-Path $local 'NexusKeypad\connecteurs\etats\runtime.json'
        $runtimeBefore = [IO.File]::ReadAllText($runtimeFile)
        $runtime = $runtimeBefore | ConvertFrom-Json
        Assert ($receipt.sharedRuntime -and $runtime.version -eq $version) "Shared runtime receipt differs for $agent."
        foreach ($property in $runtime.files.PSObject.Properties) {
            $path = [IO.Path]::GetFullPath($property.Name)
            Assert ($path.StartsWith((Join-Path $local 'NexusKeypad\hook\'), [StringComparison]::OrdinalIgnoreCase)) 'Shared receipt points outside the installed helper.'
            Assert ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ceq $property.Value) "Shared runtime hash differs for $agent."
        }
        Assert ((InvokeConnector $agent 'enable').state -eq 'Pret') "Idempotent setup failed for $agent."
        Assert ([IO.File]::ReadAllText($receiptFile) -ceq $receiptBefore) "Idempotent setup changed receipt/evidence for $agent."
        Assert ([IO.File]::ReadAllText($runtimeFile) -ceq $runtimeBefore) "Idempotent setup changed the shared runtime receipt for $agent."
        Assert ((InvokeConnector $agent 'disable').state -eq 'Activer') "Disable failed for $agent."
        Assert ((InvokeConnector $agent 'status').state -eq 'Activer') "Host disablement is ignored for $agent."
        Assert ((InvokeConnector $agent 'enable').state -eq 'Pret') "Re-enable failed for $agent."
    }
    $claudePlugin = Join-Path $local 'NexusKeypad\connecteurs\claude\nexus-keypad'
    $codexPlugin = Join-Path $user '.codex\plugins\nexus-keypad'
    $agyPlugin = Join-Path $user '.gemini\config\plugins\nexus-keypad'
    foreach ($plugin in $claudePlugin, $codexPlugin, $agyPlugin) {
        Assert (Test-Path -LiteralPath (Join-Path $plugin 'skills\nexus-keypad\SKILL.md')) 'Installed connector is missing its diagnostic skill.'
    }
    Assert (Test-Path -LiteralPath (Join-Path $claudePlugin 'hooks\quota.ts')) 'Claude quota module is absent from the installed public connector.'
    $claudeRegistry = Get-Content (Join-Path $user '.claude\plugins\installed_plugins.json') -Raw | ConvertFrom-Json
    Assert ($claudeRegistry.plugins.'nexus-keypad@nexuskeypad'[0].version -eq $version) 'Public Claude plugin was not registered by its separate plugin-manager process.'
    $commands = Get-Content (Join-Path $user '.claude\fixture-cli-commands.log')
    Assert (@($commands | Where-Object { $_ -like 'plugin|marketplace|add|*' }).Count -gt 0) 'Official marketplace add was not invoked.'
    Assert (@($commands | Where-Object { $_ -like 'plugin|install|nexus-keypad@nexuskeypad|*' }).Count -gt 0) 'Official plugin install was not invoked.'
    Assert ((Get-Content (Join-Path $user '.claude\settings.json') -Raw | ConvertFrom-Json).env.KEEP -eq 'untouched') 'Claude unrelated settings changed.'
    Assert ((Get-Content (Join-Path $user '.gemini\config\config.json') -Raw | ConvertFrom-Json).keep -eq 'untouched') 'Antigravity unrelated settings changed.'
    Assert ($null -ne (Get-Content (Join-Path $user '.gemini\config\hooks.json') -Raw | ConvertFrom-Json).'third-party') 'Antigravity third-party hook was removed.'
    $codexConfig = [IO.File]::ReadAllText((Join-Path $user '.codex\config.toml'))
    Assert ($codexConfig.Contains("model = 'keep'") -and $codexConfig.Contains("notify = ['one', 'two']") -and $codexConfig.Contains('third-party@personal')) 'Codex unrelated settings changed.'
    Assert (-not (Test-Path -LiteralPath (Join-Path $user '.codex\hooks.json'))) 'Unexpected global Codex hooks.'
    Assert (-not ($codexConfig -match 'trusted_hash|trusted_content_hash|bypass.hook.trust')) 'A native trust approval was fabricated.'
    $codexHooks = [IO.File]::ReadAllText((Join-Path $codexPlugin 'hooks\hooks.json'))
    Assert (-not $codexHooks.Contains('__NEXUS_KEYPAD_HOOK__')) 'Codex hook template was not rendered.'
    Assert ((($codexHooks | ConvertFrom-Json).hooks.PSObject.Properties | Measure-Object).Count -eq 10) 'Codex lifecycle events are incomplete.'
    Assert (-not ([IO.File]::ReadAllText((Join-Path $agyPlugin 'hooks.json')).Contains('__NEXUS_KEYPAD_HOOK__'))) 'Antigravity hook template was not rendered.'
    Assert (Test-Path -LiteralPath (Join-Path $user ".codex\plugins\cache\personal\nexus-keypad\$version\skills\nexus-keypad\SKILL.md")) 'Codex installed cache is missing.'
    Write-Host "Native package connectors: $checks isolated checks passed, connector version $version. No UI or host process was used."
} finally {
    if ((Test-Path -LiteralPath $root) -and $root.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}

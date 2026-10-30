using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace NexusKeypad.Tests;

public sealed class InstallerTests
{
    [Fact]
    public async Task WindowsPowerShellUsesItsOwnModulesWhenStartedByPowerShell7()
    {
        var depot = typeof(InstallerTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "NexusKeypad.Repository").Value;
        var build = typeof(InstallerTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "NexusKeypad.Build").Value;
        var dossier = Path.Combine(build, "installer-modules", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dossier);
        try
        {
            var source = File.ReadAllText(Path.Combine(depot, "get.ps1"));
            var prefixe = source[..source.IndexOf("$Plugin =", StringComparison.Ordinal)];
            var entree = Path.Combine(dossier, "input.txt");
            File.WriteAllText(entree, "isolated installer checksum input");
            File.WriteAllText(entree + ":Zone.Identifier", "[ZoneTransfer]\nZoneId=3\n");
            var programme = Path.Combine(dossier, "modules.ps1");
            File.WriteAllText(programme, prefixe + "\n" + """
                $hash = (Get-FileHash -LiteralPath $env:NEXUS_TEST_INPUT -Algorithm SHA256).Hash
                Unblock-File -LiteralPath $env:NEXUS_TEST_INPUT
                [ordered]@{
                    hash = $hash
                    hashModule = (Get-Command Get-FileHash).Module.Path
                    unblockModule = (Get-Command Unblock-File).Module.Path
                    modulePaths = @($env:PSModulePath -split ';')
                } | ConvertTo-Json -Compress
                """);
            var desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0");
            var desktopModules = Path.Combine(desktop, "Modules");
            var coreModules = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "Modules");
            Assert.True(Directory.Exists(coreModules), "PowerShell 7 modules must exist for this inherited-module regression.");
            var customModules = Path.Combine(dossier, "custom-modules");
            Directory.CreateDirectory(customModules);
            var info = new ProcessStartInfo(Path.Combine(desktop, "powershell.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", programme })
            {
                info.ArgumentList.Add(argument);
            }

            info.Environment["PSModulePath"] = coreModules + ";" + desktopModules + ";" + customModules;
            info.Environment["NEXUS_TEST_INPUT"] = entree;
            using var processus = Process.Start(info)!;
            var sortie = processus.StandardOutput.ReadToEndAsync();
            var erreur = processus.StandardError.ReadToEndAsync();
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            limite.CancelAfter(TimeSpan.FromSeconds(30));
            try { await processus.WaitForExitAsync(limite.Token); }
            finally { if (!processus.HasExited) { processus.Kill(entireProcessTree: true); } }
            Assert.True(processus.ExitCode == 0, await erreur);
            var resultat = JsonNode.Parse(await sortie)!;
            Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(entree))), resultat["hash"]!.GetValue<String>());
            Assert.StartsWith(desktopModules + Path.DirectorySeparatorChar, resultat["hashModule"]!.GetValue<String>(), StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(desktopModules + Path.DirectorySeparatorChar, resultat["unblockModule"]!.GetValue<String>(), StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(entree + ":Zone.Identifier"), "The Desktop Unblock-File must remove the isolated file's download marker.");
            var chemins = resultat["modulePaths"]!.AsArray().Select(n => n!.GetValue<String>()).ToArray();
            Assert.Equal(desktopModules, chemins[0], ignoreCase: true);
            Assert.Single(chemins.Where(p => String.Equals(p.TrimEnd('\\'), desktopModules, StringComparison.OrdinalIgnoreCase)));
            Assert.Contains(coreModules, chemins);
            Assert.Contains(customModules, chemins);
        }
        finally { Directory.Delete(dossier, recursive: true); }
    }
}

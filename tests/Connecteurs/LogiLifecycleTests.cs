using System.Diagnostics;
using System.Reflection;

namespace NexusKeypad.Tests;

public sealed class LogiLifecycleTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("bad-signature")]
    [InlineData("missing-tool")]
    [InlineData("foreign-process")]
    [InlineData("stop-failed")]
    [InlineData("stop-failed-after-stop")]
    [InlineData("stop-control-timeout")]
    [InlineData("stop-unconfirmed")]
    [InlineData("agent-restart")]
    [InlineData("agent-restart-forever")]
    [InlineData("start-refused")]
    [InlineData("start-unconfirmed")]
    [InlineData("copy-failed-existing")]
    [InlineData("copy-failed-new")]
    [InlineData("rollback-failed")]
    public async Task OfficialPluginServiceLifecycleNeverTouchesTheKeyboardAgent(String scenario)
    {
        String Metadata(String key) => typeof(LogiLifecycleTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == key).Value;
        var depot = Metadata("NexusKeypad.Repository");
        var racine = Path.Combine(Metadata("NexusKeypad.Build"), "lifecycle", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(racine);
        try
        {
            var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
                Path.Combine(depot, "tests", "Connecteurs", "LogiLifecycleFixture.ps1"), "-Installer", Path.Combine(depot, "get.ps1"), "-Root", racine, "-Case", scenario })
            {
                info.ArgumentList.Add(argument);
            }
            using var processus = Process.Start(info)!;
            var sortie = processus.StandardOutput.ReadToEndAsync();
            var erreur = processus.StandardError.ReadToEndAsync();
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            limite.CancelAfter(TimeSpan.FromSeconds(30));
            try { await processus.WaitForExitAsync(limite.Token); }
            finally { if (!processus.HasExited) { processus.Kill(entireProcessTree: true); } }
            Assert.True(processus.ExitCode == 0, await sortie + await erreur);
        }
        finally { Directory.Delete(racine, recursive: true); }
    }
}

using System.Reflection;

namespace NexusKeypad.Tests;

// xUnit's own entry point, written by hand because the compiled hook\Program.cs has a Main too.
internal static class Entree
{
    public static Int32 Main(String[] args) => args.Any(a => a is "--server" or "--internal-msbuild-node")
        ? Xunit.MicrosoftTestingPlatform.TestPlatformTestFramework.RunAsync(args, SelfRegisteredExtensions.AddSelfRegisteredExtensions).GetAwaiter().GetResult()
        : Xunit.Runner.InProc.SystemConsole.ConsoleRunner.Run(args).GetAwaiter().GetResult();

    public static String Metadonnee(String cle) => typeof(Entree).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == cle).Value;

    public static String Hook => Metadonnee("NexusKeypad.Hook");

    public static String Depot => Metadonnee("NexusKeypad.Repository");
}

// The sound rules redirect USERPROFILE and LOCALAPPDATA for the whole process: they run alone, after the other tests.
[CollectionDefinition(nameof(ProfilRedirige), DisableParallelization = true)]
public sealed class ProfilRedirige;
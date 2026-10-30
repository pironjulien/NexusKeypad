using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace NexusKeypad.Tests;

// Shared by the test classes: the Logi service's own assemblies, and the paths baked in at build time.
internal static class Commun
{
    private const String Service = @"C:\Program Files\Logi\LogiPluginService\";

    // On the author's PC the plugin's dependencies are read from the Logi service, as when the plugin runs there.
    [ModuleInitializer]
    internal static void Initialiser() => AssemblyLoadContext.Default.Resolving += (_, nom) =>
    {
        var chemin = Path.Combine(Service, nom.Name + ".dll");
        return File.Exists(chemin) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(chemin) : null;
    };

    public static String Metadonnee(String cle) => typeof(Commun).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == cle).Value;

    public static String Depot => Metadonnee("NexusKeypad.Repository");

    public static void Vrai(Boolean condition, String regle) => Assert.True(condition, regle);
}

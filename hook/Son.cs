// Notes distinctes de Claude et d'Antigravity, livrées dans le même exécutable public, sans Python ni chemin privé.
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace NexusKeypad.Hook;

internal static class Son
{
    internal static bool AppartientAuPlugin(string agent)
    {
        if (agent is not ("claude" or "antigravity")) { return false; }
        try
        {
            var local = Environment.GetEnvironmentVariable("LOCALAPPDATA")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var recu = Path.Combine(local, "NexusKeypad", "connecteurs", "etats", agent + ".json");
            return JsonNode.Parse(File.ReadAllText(recu))?["hookOwner"]?.GetValue<string>() == "plugin"
                && Loupedeck.TrinitePlugin.Connecteurs.EvenementsActifs(agent);
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static void Demander(string agent)
    {
        if (!AppartientAuPlugin(agent)) { return; }
        // Les flux redirigés empêchent le fils de retenir les tubes JSON du hook parent.
        var commande = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        commande.ArgumentList.Add("--play-note");
        commande.ArgumentList.Add(agent);
        using var enfant = Process.Start(commande);
        enfant?.StandardInput.Close();
    }

    internal static void Jouer(string agent)
    {
        // Un arrêt/désactivation entre le Stop et le démarrage du fils reste silencieux.
        if (!AppartientAuPlugin(agent)) { return; }
        using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream($"NexusKeypad.Hook.{agent}.wav");
        if (source is null) { return; }
        using var octets = new MemoryStream();
        source.CopyTo(octets);
        // SND_MEMORY | SND_NODEFAULT, synchrone dans ce processus détaché. Aucun bip en cas d'échec.
        _ = PlaySound(octets.ToArray(), IntPtr.Zero, 0x0004 | 0x0002);
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(byte[] son, IntPtr module, uint flags);
}

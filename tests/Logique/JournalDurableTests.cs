using Loupedeck.TrinitePlugin;

namespace NexusKeypad.Tests;

// Logi recreates the plugin's own log on every load attempt (service start, update, and the refused second load right
// after): the pad keeps its history in a log of its own, appended and capped.
[Collection(nameof(JournalDurableTests))]
public sealed class JournalDurableTests : IDisposable
{
    private readonly String _dossier = Path.Combine(Path.GetTempPath(), "nexuskeypad-journal-" + Guid.NewGuid().ToString("N"));

    public JournalDurableTests()
    {
        JournalDurable.Chemin = Path.Combine(this._dossier, "logs", "NexusKeypad.log");
        JournalDurable.Actif = true;
    }

    public void Dispose()
    {
        JournalDurable.Actif = false;
        JournalDurable.Chemin = null;
        if (Directory.Exists(this._dossier))
        {
            Directory.Delete(this._dossier, recursive: true);
        }
    }

    [Fact]
    public void Les_lignes_s_ajoutent_sans_effacer_les_precedentes()
    {
        JournalDurable.Ecrire("INFO", "photo 20261115_122016.jpg sur la touche", null);
        JournalDurable.Ecrire("ERROR", "photo du téléphone", new IOException("liaison coupée"));
        JournalDurable.Ecrire("INFO", "après un rechargement du plugin", null);

        var texte = File.ReadAllText(JournalDurable.Chemin!);
        Assert.Contains("| INFO  | photo 20261115_122016.jpg sur la touche", texte, StringComparison.Ordinal);
        Assert.Contains("| ERROR | photo du téléphone", texte, StringComparison.Ordinal);
        Assert.Contains("liaison coupée", texte, StringComparison.Ordinal);
        Assert.Contains("après un rechargement du plugin", texte, StringComparison.Ordinal);
    }

    [Fact]
    public void Au_dela_de_2_Mo_le_journal_passe_en_point_1()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(JournalDurable.Chemin)!);
        File.WriteAllText(JournalDurable.Chemin!, new String('x', (Int32)JournalDurable.TailleMax));

        JournalDurable.Ecrire("INFO", "ligne neuve", null);

        Assert.Equal(JournalDurable.TailleMax, new FileInfo(Path.Combine(this._dossier, "logs", "NexusKeypad.1.log")).Length);
        Assert.Contains("ligne neuve", File.ReadAllText(JournalDurable.Chemin!), StringComparison.Ordinal);
        Assert.True(new FileInfo(JournalDurable.Chemin!).Length < JournalDurable.TailleMax);
    }

    [Fact]
    public void Inactif_hors_de_Logi_rien_n_est_ecrit()
    {
        JournalDurable.Actif = false;
        JournalDurable.Ecrire("INFO", "code lancé par un test", null);
        Assert.False(File.Exists(JournalDurable.Chemin));
    }
}

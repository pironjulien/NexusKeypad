using Loupedeck.TrinitePlugin;

namespace NexusKeypad.Tests;

// Quand le plugin relance le service Logi pour garder le pad allumé écran éteint (GardeLogi) : le 27/11, deviné
// « allumé » au chargement puis annoncé « éteint » par Windows, il relançait le service toutes les 22 s, la limite d'une
// relance par minute repartant de zéro avec chaque service. Horloge simulée, fichier temporaire ; rien n'est relancé.
public sealed class GardeLogiTests : IDisposable
{
    private readonly String _dossier = Directory.CreateTempSubdirectory("nexuskeypad-garde-").FullName;

    private DateTime _heure = new(2026, 11, 27, 8, 53, 10, DateTimeKind.Utc);

    private String Fichier => Path.Combine(this._dossier, "NexusKeypad", "logi-garde.txt");

    public void Dispose() => Directory.Delete(this._dossier, recursive: true);

    // Un plugin fraîchement chargé (un service relancé) : sa mémoire repart de zéro, le fichier reste.
    private GardeLogi Charger() => new(this.Fichier, () => this._heure);

    [Fact]
    public void Ecran_devine_allume_puis_annonce_eteint_pas_de_relance()
    {
        var garde = this.Charger();
        Assert.False(garde.Relancer(connu: false, eteint: false)); // Windows n'a pas encore répondu : deviné
        Assert.False(garde.Relancer(connu: true, eteint: true)); // éteint depuis avant le chargement
        Assert.False(File.Exists(this.Fichier));
    }

    [Fact]
    public void Ecran_deja_eteint_au_chargement_pas_de_relance()
    {
        var garde = this.Charger();
        Assert.False(garde.Relancer(connu: true, eteint: true));
        Assert.False(garde.Relancer(connu: true, eteint: true));
    }

    [Fact]
    public void Ecran_vu_allume_puis_eteint_une_seule_relance()
    {
        var garde = this.Charger();
        Assert.False(garde.Relancer(connu: true, eteint: false));
        Assert.True(garde.Relancer(connu: true, eteint: true));
        this._heure += TimeSpan.FromSeconds(1);
        Assert.False(garde.Relancer(connu: true, eteint: true)); // toujours éteint : pas de deuxième
        Assert.True(File.Exists(this.Fichier));
    }

    [Fact]
    public void Relance_de_moins_d_une_minute_dans_le_fichier_aucune()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(this.Fichier)!);
        File.WriteAllText(this.Fichier, (this._heure - TimeSpan.FromSeconds(22)).ToString("O"));
        var garde = this.Charger(); // le service relancé il y a 22 s : nouveau plugin, même fichier
        Assert.False(garde.Relancer(connu: true, eteint: false));
        Assert.False(garde.Relancer(connu: true, eteint: true));
    }

    [Fact]
    public void Limite_tenue_d_un_chargement_a_l_autre_puis_relance_apres_une_minute()
    {
        var premier = this.Charger();
        Assert.False(premier.Relancer(connu: true, eteint: false));
        Assert.True(premier.Relancer(connu: true, eteint: true));

        this._heure += TimeSpan.FromSeconds(30);
        var second = this.Charger();
        Assert.False(second.Relancer(connu: true, eteint: false));
        Assert.False(second.Relancer(connu: true, eteint: true));

        this._heure += TimeSpan.FromSeconds(31);
        Assert.False(second.Relancer(connu: true, eteint: false));
        Assert.True(second.Relancer(connu: true, eteint: true));
    }

    [Fact]
    public void Ligne_du_journal_ne_change_pas_pour_un_point_de_quota()
    {
        Instantane Avec(Double cinq) => Instantane.Vide with { Quotas = [new Quota(cinq, 0.31), null, new Quota(0.1, null)] };
        Assert.Equal(Moteur.ResumeJournal(Avec(0.52)), Moteur.ResumeJournal(Avec(0.53)));
        Assert.Equal(Moteur.ResumeJournal(Avec(0.10)), Moteur.ResumeJournal(Avec(0.49)));
        Assert.NotEqual(Moteur.ResumeJournal(Avec(0.79)), Moteur.ResumeJournal(Avec(0.80)));
        Assert.NotEqual(Moteur.ResumeJournal(Avec(0.98)), Moteur.ResumeJournal(Avec(1.0)));
        Assert.Contains("quota 5 h ≥ 50 % semaine < 50 %", Moteur.ResumeJournal(Avec(0.52)), StringComparison.Ordinal);
    }
}

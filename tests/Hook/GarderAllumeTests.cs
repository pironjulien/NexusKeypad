using NexusKeypad.Hook;

namespace NexusKeypad.Tests;

// Relance du service Logi qui garde le pad allumé écran éteint (hook\GarderAllume.cs) : horloge, outil officiel et
// processus simulés ; seul le journal est un vrai fichier (temporaire). Aucun processus Logi n'est touché.
public sealed class GarderAllumeTests : IDisposable
{
    private readonly String _dossier = Directory.CreateTempSubdirectory("nexuskeypad-garder-").FullName;

    private String Journal => Path.Combine(this._dossier, "logs", "NexusKeypad.log");

    public void Dispose() => Directory.Delete(this._dossier, recursive: true);

    // Un poste simulé : l'horloge avance à chaque pause ; « vivants » rend les processus selon l'heure écoulée.
    private sealed class Poste(Func<Double, List<String>, List<(String, Int32)>> vivants, Boolean stopAccepte = true)
    {
        public DateTime Heure { get; private set; } = new(2026, 11, 25, 9, 0, 0, DateTimeKind.Utc);

        private readonly DateTime _debut = new(2026, 11, 25, 9, 0, 0, DateTimeKind.Utc);

        public List<String> Ordres { get; } = [];

        public Int32 Lancer(String journal) => GarderAllume.Executer(journal,
            pause: d => this.Heure += d,
            outil: o => { this.Ordres.Add(o); return o != "stop" || stopAccepte; },
            processus: () => vivants((this.Heure - this._debut).TotalSeconds, this.Ordres),
            maintenant: () => this.Heure);
    }

    private String Lignes() => File.Exists(this.Journal) ? File.ReadAllText(this.Journal) : "";

    [Fact]
    public void Agent_qui_relance_le_service()
    {
        // Ancien service (id 1) jusqu'à l'arrêt, puis l'agent en relance un (id 2) une seconde après.
        var poste = new Poste((t, ordres) => !ordres.Contains("stop") ? [(GarderAllume.Service, 1)]
            : t < 4.5 ? [(GarderAllume.Service, 1)] : [(GarderAllume.Service, 2), (GarderAllume.Extension, 3)]);
        Assert.Equal(0, poste.Lancer(this.Journal));
        Assert.Equal(["stop"], poste.Ordres);
        Assert.Contains("relancé par son agent", this.Lignes(), StringComparison.Ordinal);
    }

    [Fact]
    public void Service_jamais_relance_par_l_agent_demarre_par_l_outil()
    {
        var poste = new Poste((t, ordres) => ordres.Contains("start") ? [(GarderAllume.Service, 5)]
            : ordres.Contains("stop") ? [] : [(GarderAllume.Service, 1)]);
        Assert.Equal(0, poste.Lancer(this.Journal));
        Assert.Equal(["stop", "start"], poste.Ordres);
        Assert.Contains("démarré par l'outil (1 fois)", this.Lignes(), StringComparison.Ordinal);
    }

    [Fact]
    public void Service_relance_qui_repart_de_lui_meme_est_redemarre()
    {
        // Le cas du 25/11 : relancé par l'agent, le service disparaît 20 s plus tard sans que personne ne le relance.
        var poste = new Poste((t, ordres) => ordres.Contains("start") ? [(GarderAllume.Service, 7)]
            : !ordres.Contains("stop") ? [(GarderAllume.Service, 1)]
            : t is > 4 and < 24 ? [(GarderAllume.Service, 2)] : []);
        Assert.Equal(0, poste.Lancer(this.Journal));
        Assert.Equal(["stop", "start"], poste.Ordres);
    }

    [Fact]
    public void Service_qui_ne_repart_pas_est_signale()
    {
        var poste = new Poste((t, ordres) => ordres.Contains("stop") ? [] : [(GarderAllume.Service, 1)]);
        Assert.Equal(1, poste.Lancer(this.Journal));
        Assert.Equal(["stop", "start", "start", "start"], poste.Ordres);
        Assert.Contains("ERROR | pad gardé allumé : le service Logi ne repart pas après 3 démarrages", this.Lignes(), StringComparison.Ordinal);
    }

    [Fact]
    public void Extension_seule_pas_de_demarrage_a_cote()
    {
        // Une extension restée sans son service : jamais de second service à côté d'elle, et l'échec est noté.
        var poste = new Poste((t, ordres) => ordres.Contains("stop") ? [(GarderAllume.Extension, 9)] : [(GarderAllume.Service, 1)]);
        Assert.Equal(1, poste.Lancer(this.Journal));
        Assert.Equal(["stop"], poste.Ordres);
        Assert.Contains("absent après 1 min", this.Lignes(), StringComparison.Ordinal);
    }

    [Fact]
    public void Arret_refuse_service_qui_continue()
    {
        // Arrêt refusé et ancien service toujours là : rien n'est démarré à côté, le journal le dit sans parler d'absence.
        var poste = new Poste((t, ordres) => [(GarderAllume.Service, 1)], stopAccepte: false);
        Assert.Equal(1, poste.Lancer(this.Journal));
        Assert.Equal(["stop"], poste.Ordres);
        Assert.Contains("arrêt du service refusé", this.Lignes(), StringComparison.Ordinal);
        Assert.Contains("WARN  | pad gardé allumé : le service Logi n'a pas pris l'arrêt", this.Lignes(), StringComparison.Ordinal);
    }

    [Fact]
    public void Arret_annonce_en_echec_mais_service_arrete_est_redemarre()
    {
        // L'outil rend une erreur alors que le service s'est arrêté : la surveillance le démarre quand même.
        var poste = new Poste((t, ordres) => ordres.Contains("start") ? [(GarderAllume.Service, 5)]
            : ordres.Contains("stop") ? [] : [(GarderAllume.Service, 1)], stopAccepte: false);
        Assert.Equal(0, poste.Lancer(this.Journal));
        Assert.Equal(["stop", "start"], poste.Ordres);
    }
}

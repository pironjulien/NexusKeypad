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

        // Un verrou propre à chaque test : ni les tests en parallèle ni une vraie relance du poste ne se gênent.
        public String Verrou { get; init; } = $@"Global\NexusKeypad.Test.{Guid.NewGuid():N}";

        public Action? PremierePause { get; init; }

        public Int32 Lancer(String journal) => GarderAllume.Executer(journal,
            pause: d => { if (this.Heure == this._debut) { this.PremierePause?.Invoke(); } this.Heure += d; },
            outil: o => { lock (this.Ordres) { this.Ordres.Add(o); } return o != "stop" || stopAccepte; },
            processus: () => vivants((this.Heure - this._debut).TotalSeconds, this.Ordres),
            maintenant: () => this.Heure,
            verrou: this.Verrou);
    }

    [Fact]
    public void Deux_relances_en_meme_temps_une_seule_touche_au_service()
    {
        // Le cas du 27/11 : une seconde relance pendant la surveillance de la première. Elle s'arrête aussitôt, sans
        // appeler l'outil de Logi ; la première fait son travail seule.
        var verrou = $@"Global\NexusKeypad.Test.{Guid.NewGuid():N}";
        Int32? seconde = null;
        Poste Nouveau(Action? premiere) => new((t, o) => !o.Contains("stop") ? [(GarderAllume.Service, 1)]
            : t < 4.5 ? [(GarderAllume.Service, 1)] : [(GarderAllume.Service, 2)]) { Verrou = verrou, PremierePause = premiere };
        var autre = Nouveau(null);
        var premiere = Nouveau(() =>
        {
            // La première tient le verrou : la seconde démarre sur un autre fil pendant ce temps et doit finir seule.
            var fil = new Thread(() => seconde = autre.Lancer(this.Journal));
            fil.Start();
            Assert.True(fil.Join(TimeSpan.FromSeconds(30)));
        });
        Assert.Equal(0, premiere.Lancer(this.Journal));
        Assert.Equal(0, seconde);
        Assert.Equal(["stop"], premiere.Ordres);
        Assert.Empty(autre.Ordres);
        Assert.Contains("une relance est déjà en cours", this.Lignes(), StringComparison.Ordinal);

        // Verrou rendu : une relance suivante passe.
        var suivante = Nouveau(null);
        Assert.Equal(0, suivante.Lancer(this.Journal));
        Assert.Equal(["stop"], suivante.Ordres);
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

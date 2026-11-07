using Loupedeck.TrinitePlugin;

namespace NexusKeypad.Tests;

// Codex quota read from Codex LB's /api/accounts: what Codex can really still use. /api/usage/summary counted the 5-hour
// window of a Pro Lite account (weekly limit only) as full: 83% left announced on 07/11 while every account was blocked.
public sealed class QuotaCodexTests
{
    private static String Compte(String plan, String statut, Double? cinq, Double? cinqReste, Double semaine, Double? semaineReste) =>
        $$"""{"planType":"{{plan}}","status":"{{statut}}","capacityCreditsPrimary":{{N(cinq)}},"remainingCreditsPrimary":{{N(cinqReste)}},"capacityCreditsSecondary":{{N(semaine)}},"remainingCreditsSecondary":{{N(semaineReste)}}}""";

    private static String N(Double? v) => v is { } d ? d.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null";

    private static Quota? Lire(params String[] comptes) => Observateur.QuotaCodexLb($$"""{"accounts":[{{String.Join(',', comptes)}}]}""");

    [Fact]
    public void Releve_du_07_11_pro_lite_plafonne_par_sa_semaine()
    {
        var q = Lire(
            Compte("team", "rate_limited", 225, 0, 7560, 2646),
            Compte("prolite", "active", 1125, null, 37800, 378),
            Compte("team", "quota_exceeded", 225, 225, 7560, 0),
            Compte("team", "quota_exceeded", 225, 225, 7560, 0));
        Assert.NotNull(q);
        Assert.Equal(1 - 378.0 / 1350, q!.Cinq!.Value, 6); // skynet bloqué, le Pro Lite limité à sa semaine
        Assert.Equal(1 - 3024.0 / 60480, q.Semaine!.Value, 6);
    }

    [Fact]
    public void Pro_lite_plein_ne_bloque_pas_les_cinq_heures()
    {
        var q = Lire(Compte("team", "rate_limited", 225, 0, 7560, 2646), Compte("prolite", "active", 1125, null, 37800, 37800));
        Assert.Equal(1 - 1125.0 / 1350, q!.Cinq!.Value, 6);
    }

    [Fact]
    public void Compte_actif_sans_fenetre_rapportee_compte_plein_plafonne_par_la_semaine()
    {
        Assert.Equal(0, Lire(Compte("team", "active", 225, null, 7560, 7000))!.Cinq!.Value, 6);
        Assert.Equal(1 - 100.0 / 225, Lire(Compte("team", "active", 225, null, 7560, 100))!.Cinq!.Value, 6);
    }

    [Fact]
    public void Compte_bloque_ou_en_pause()
    {
        var q = Lire(Compte("team", "rate_limited", 225, 225, 7560, 7560), Compte("team", "paused", 225, 225, 7560, 7560));
        Assert.Equal(1, q!.Cinq!.Value, 6);
        Assert.Equal(0, q.Semaine!.Value, 6);
    }
}

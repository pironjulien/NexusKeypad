using Loupedeck.TrinitePlugin;

namespace NexusKeypad.Tests;

// Finding the phone again over wireless debugging. When the phone's Wi-Fi drops, Android restarts wireless debugging on a
// new port and announces it over mDNS. On 2026-11-15 the pad waited out its 2-minute pause after a failed try, missed
// the port announced at 12:20:10 and showed the photo 77 s after it was taken.
public sealed class TelephoneTests
{
    private const String Serie = "ZX1TEST0001";

    private const String Mdns = """
        List of discovered mdns services
        adb-ZX1TEST0001-abc123	_adb-tls-connect._tcp	10.0.0.42:43333
        adb-ZX1TEST0001-abc123	_adb-tls-pairing._tcp	10.0.0.42:44444
        adb-9999000011112	_adb._tcp	10.0.0.77:5555
        """;

    [Fact]
    public void Annonces_du_seul_telephone_note_pour_la_connexion() =>
        Assert.Equal(["10.0.0.42:43333"], Telephone.Annonces(Mdns, Serie));

    [Fact]
    public void Hors_attente_l_adresse_notee_puis_les_annonces() =>
        Assert.Equal(["10.0.0.42:41111", "10.0.0.42:43333"],
            Telephone.AEssayer("10.0.0.42:41111", ["10.0.0.42:43333", "10.0.0.42:41111"], new HashSet<String>(), enAttente: false));

    [Fact]
    public void En_attente_un_port_nouvellement_annonce_est_essaye_tout_de_suite()
    {
        var echouees = new HashSet<String> { "10.0.0.42:41111", "10.0.0.42:42222" };
        Assert.Equal(["10.0.0.42:43333"], Telephone.AEssayer("10.0.0.42:41111", ["10.0.0.42:43333"], echouees, enAttente: true));
    }

    [Fact]
    public void En_attente_ni_l_adresse_notee_ni_un_port_deja_essaye_en_vain()
    {
        var echouees = new HashSet<String> { "10.0.0.42:42222" };
        Assert.Empty(Telephone.AEssayer("10.0.0.42:41111", ["10.0.0.42:42222"], echouees, enAttente: true));
        Assert.Empty(Telephone.AEssayer("10.0.0.42:41111", [], echouees, enAttente: true));
    }
}

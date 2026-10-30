using System.Diagnostics;
using Loupedeck;
using Loupedeck.TrinitePlugin;
using static NexusKeypad.Tests.Commun;

namespace NexusKeypad.Tests;

// Image cache: an identical scene gives identical pixels; the photo does not move from one instant to the next; without a
// phone, the Photo key is an animated clock. And the previews (planches) of the 9 keys in every situation.
public sealed class RenduTests
{
    private static readonly DateTime Midi = new(2026, 10, 24, 12, 34, 56);

    private static Scene S(Double t, Instantane i = null) => new()
    {
        Instantane = i ?? EtatsTests.Instantane(), Temps = t, Appuis = Enumerable.Repeat(-9.0, Moteur.Touches).ToArray(),
        PhotoCopiee = -9, CaptureCollee = -9, Maintenant = Midi,
    };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Meme_scene_memes_pixels(Int32 k) =>
        Vrai(Rendu.Image(k, S(1.0)).Pixels.AsSpan().SequenceEqual(Rendu.Image(k, S(1.0)).Pixels), $"touche {k + 1} : même scène, mêmes pixels");

    [Fact]
    public void Photo_immobile()
    {
        using var carre = new BitmapBuilder(Rendu.T, Rendu.T);
        carre.Clear(new BitmapColor(200, 120, 40));
        var avecPhoto = EtatsTests.Instantane() with { Photo = new("/sdcard/DCIM/Camera/x.jpg", "x.jpg", BitmapImage.FromArray(carre.ToArray(BitmapImageFormat.Png))) };
        Vrai(Rendu.Image(Moteur.Photo, S(0.1, avecPhoto)).Pixels.AsSpan().SequenceEqual(Rendu.Image(Moteur.Photo, S(3.7, avecPhoto)).Pixels), "photo immobile");
    }

    [Fact]
    public void Animations()
    {
        var instantane = EtatsTests.Instantane();
        Vrai(!Rendu.Image(Moteur.Photo, S(0.1, instantane with { Photo = null })).Pixels.AsSpan().SequenceEqual(Rendu.Image(Moteur.Photo, S(0.6, instantane with { Photo = null })).Pixels), "sans téléphone : horloge animée");
        Vrai(!Rendu.Image(0, S(0.1)).Pixels.AsSpan().SequenceEqual(Rendu.Image(0, S(0.6)).Pixels), "tête qui travaille animée");
        Vrai(Rendu.Image(5, S(0.1)).Pixels.AsSpan().SequenceEqual(Rendu.Image(5, S(0.6)).Pixels), "appli fermée : touche d'infos éteinte et immobile");
    }

    [Fact]
    public void Planches_de_l_apercu()
    {
        // apercu (built with these tests) compiles the same sources and draws the 9 keys in every situation.
        var planches = Path.Combine(Metadonnee("NexusKeypad.Build"), "planches");
        if (Directory.Exists(planches))
        {
            Directory.Delete(planches, true);
        }

        var info = new ProcessStartInfo(Metadonnee("NexusKeypad.Apercu")) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(planches);
        using var apercu = Process.Start(info)!;
        var sortie = apercu.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var erreurs = apercu.StandardError.ReadToEnd();
        apercu.WaitForExit();
        Assert.True(apercu.ExitCode == 0, $"L'aperçu échoue : {sortie.Result}{erreurs}");
        foreach (var nom in new[] { "actif", "calme", "tetes" })
        {
            var png = new FileInfo(Path.Combine(planches, nom + ".png"));
            Assert.True(png.Exists && png.Length >= 2048, $"Planche {nom} absente ou vide.");
        }
    }
}

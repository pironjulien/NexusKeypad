using System.Runtime.InteropServices;
using System.Text;
using Loupedeck.TrinitePlugin;
using SkiaSharp;
using static NexusKeypad.Tests.Commun;

namespace NexusKeypad.Tests;

// Image pasted into an AI (shared Nexus rule, NexusShared\images.md, ceiling 3 MB since 2026-11-06): lossless WebP while
// it fits 3 MB; above it, lossless with 2 then 3 low bits dropped, then lossy 90/80/70; never above 3 MB.
public sealed class ImageIaTests : IDisposable
{
    private readonly String _dossier = Path.Combine(Path.GetTempPath(), "nexuskeypad-imageia-" + Guid.NewGuid().ToString("N"));

    public ImageIaTests() => Directory.CreateDirectory(this._dossier);

    public void Dispose() => Directory.Delete(this._dossier, recursive: true);

    [Fact]
    public void Plafond_de_3_Mo() => Assert.Equal(3 * 1024 * 1024, ImageIa.PoidsMax);

    [Fact]
    public void Capture_ordinaire_sans_perte()
    {
        using var image = Bruit(400, 300, 8, 1);
        var (format, octets, rendue) = this.Ecrire(image);
        using (rendue)
        {
            Assert.Equal("VP8L", format);
            Vrai(octets <= ImageIa.PoidsMax, "sous le plafond");
            Vrai(MemesPixels(image, rendue, 0), "pixels identiques");
        }
    }

    // A noisy 4-bit image: lossless is about 3.6 MB, above the ceiling; 2 bits dropped fit.
    [Fact]
    public void Au_dela_du_plafond_deux_bits_retires()
    {
        using var image = Bruit(2000, 1200, 4, 2);
        var (format, octets, rendue) = this.Ecrire(image);
        using (rendue)
        {
            Assert.Equal("VP8L", format);
            Vrai(octets <= ImageIa.PoidsMax, "sous le plafond");
            Vrai(MemesPixels(image, rendue, 2), "seuls les 2 bits de poids faible retirés");
            Vrai(!MemesPixels(image, rendue, 3), "pas 3 bits quand 2 suffisent");
        }
    }

    // A noisy 5-bit image: 2 bits dropped still weigh more than 3 MB; 3 bits dropped fit.
    [Fact]
    public void Puis_trois_bits_retires()
    {
        using var image = Bruit(2000, 1280, 5, 3);
        var (format, octets, rendue) = this.Ecrire(image);
        using (rendue)
        {
            Assert.Equal("VP8L", format);
            Vrai(octets <= ImageIa.PoidsMax, "sous le plafond");
            Vrai(MemesPixels(image, rendue, 3), "3 bits de poids faible retirés, jamais plus");
            Vrai(!MemesPixels(image, rendue, 2), "2 bits ne suffisaient pas");
        }
    }

    // Full 8-bit noise: even 3 bits dropped weigh about 4.5 MB; lossy at the same size.
    [Fact]
    public void Puis_avec_perte_a_la_meme_taille()
    {
        using var image = Bruit(2000, 1200, 8, 4);
        var (format, octets, rendue) = this.Ecrire(image);
        using (rendue)
        {
            Assert.Equal("VP8 ", format);
            Vrai(octets <= ImageIa.PoidsMax, "sous le plafond");
            Assert.Equal((2000, 1200), (rendue.Width, rendue.Height));
        }
    }

    // A JPEG photo now goes through lossless too: reduced to 2.56 Mpx it fits 3 MB.
    [Fact]
    public void Photo_jpeg_sans_perte_reduite()
    {
        using var photo = Degrade(4000, 3000);
        var source = Path.Combine(this._dossier, "photo.jpg");
        using (var donnees = photo.Encode(SKEncodedImageFormat.Jpeg, 90))
        {
            File.WriteAllBytes(source, donnees.ToArray());
        }

        var fichier = ImageIa.Ecrire(source, Path.Combine(this._dossier, "photo"));
        Assert.NotNull(fichier);
        var octets = File.ReadAllBytes(fichier);
        Assert.Equal("VP8L", Format(octets));
        Vrai(octets.Length <= ImageIa.PoidsMax, "sous le plafond");
        using var rendue = SKBitmap.Decode(octets);
        Vrai(Math.Max(rendue.Width, rendue.Height) <= 2000 && (Double)rendue.Width * rendue.Height <= 2_560_000, "≤ 2000 px et ≤ 2,56 Mpx");
    }

    private (String Format, Int32 Octets, SKBitmap Rendue) Ecrire(SKBitmap image)
    {
        var fichier = ImageIa.Ecrire(image, Path.Combine(this._dossier, Guid.NewGuid().ToString("N")));
        Assert.NotNull(fichier);
        var octets = File.ReadAllBytes(fichier);
        return (Format(octets), octets.Length, SKBitmap.Decode(octets));
    }

    // Bitstream chunk of the WebP: VP8L (lossless) or "VP8 " (lossy), after an optional VP8X header.
    private static String Format(Byte[] webp)
    {
        var entete = Encoding.ASCII.GetString(webp, 0, Math.Min(webp.Length, 64));
        return entete.Contains("VP8L", StringComparison.Ordinal) ? "VP8L" : entete.Contains("VP8 ", StringComparison.Ordinal) ? "VP8 " : "?";
    }

    // Opaque random image whose colour channels use only their `bits` low bits.
    private static SKBitmap Bruit(Int32 largeur, Int32 hauteur, Int32 bits, Int32 graine)
    {
        var image = new SKBitmap(new SKImageInfo(largeur, hauteur, SKColorType.Bgra8888, SKAlphaType.Opaque));
        var octets = new Byte[image.ByteCount];
        new Random(graine).NextBytes(octets);
        var masque = (Byte)((1 << bits) - 1);
        for (var i = 0; i < octets.Length; i++)
        {
            octets[i] = (i & 3) == 3 ? (Byte)255 : (Byte)(octets[i] & masque);
        }

        Marshal.Copy(octets, 0, image.GetPixels(), octets.Length);
        image.NotifyPixelsChanged();
        return image;
    }

    private static SKBitmap Degrade(Int32 largeur, Int32 hauteur)
    {
        var image = new SKBitmap(largeur, hauteur);
        using var toile = new SKCanvas(image);
        using var peinture = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(largeur, hauteur), [SKColors.SkyBlue, SKColors.DarkGreen], SKShaderTileMode.Clamp) };
        toile.DrawRect(0, 0, largeur, hauteur, peinture);
        return image;
    }

    // The rendered pixels equal the source's with their `bits` low bits set to zero.
    private static Boolean MemesPixels(SKBitmap source, SKBitmap rendue, Int32 bits)
    {
        using var attendue = ImageIa.SansBitsFaibles(source, bits);
        using var lue = rendue.Copy(SKColorType.Bgra8888);
        if (attendue.Width != lue.Width || attendue.Height != lue.Height)
        {
            return false;
        }

        return attendue.GetPixelSpan().SequenceEqual(lue.GetPixelSpan());
    }
}

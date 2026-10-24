namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.IO;

    using SkiaSharp;

    // Image collée dans une IA, selon la règle commune des produits Nexus : côté long
    // ≤ 1568 px et ≤ 1,15 Mpx, WebP sans perte, repli WebP qualité 85 puis 70 au-delà de 200 Ko, jamais plus de 500 Ko
    // (au-delà, Antigravity répond 400). SkiaSharp est celui du service Logi (aucune dépendance ajoutée au paquet).
    public static class ImageIa
    {
        private const Int32 CoteMax = 1568;
        private const Double PixelsMax = 1_150_000;

        // Écrit <base>.webp (pour l'IA) et <base>.png (image du presse-papiers, que Windows sait lire) ; rend le WebP, ou
        // null si l'image est illisible ou ne tient pas sous 500 Ko.
        public static String Ecrire(String source, String baseSortie)
        {
            using var codec = SKCodec.Create(source);
            if (codec is null)
            {
                return null;
            }

            using var brute = SKBitmap.Decode(codec);
            using var droite = Redresser(brute, codec.EncodedOrigin);
            var k = Math.Min(1.0, Math.Min(CoteMax / (Double)Math.Max(droite.Width, droite.Height), Math.Sqrt(PixelsMax / ((Double)droite.Width * droite.Height))));
            using var reduite = k < 1
                ? droite.Resize(new SKImageInfo(Math.Max(1, (Int32)(droite.Width * k)), Math.Max(1, (Int32)(droite.Height * k))), SKFilterQuality.High)
                : droite.Copy();
            using var pixels = reduite.PeekPixels();

            SKData webp = null;
            foreach (var (compression, qualite) in new[] { (SKWebpEncoderCompression.Lossless, 100f), (SKWebpEncoderCompression.Lossy, 85f), (SKWebpEncoderCompression.Lossy, 70f) })
            {
                webp?.Dispose();
                webp = pixels.Encode(new SKWebpEncoderOptions(compression, qualite));
                if (webp is not null && webp.Size <= (compression == SKWebpEncoderCompression.Lossless ? 200 * 1024 : 500 * 1024))
                {
                    break;
                }
            }

            using (webp)
            {
                if (webp is null || webp.Size > 500 * 1024)
                {
                    return null;
                }

                var fichier = baseSortie + ".webp";
                File.WriteAllBytes(fichier, webp.ToArray());
                using var png = pixels.Encode(SKPngEncoderOptions.Default);
                File.WriteAllBytes(baseSortie + ".png", png.ToArray());
                return fichier;
            }
        }

        // Une photo d'appareil est souvent stockée couchée, avec son sens dans l'EXIF.
        private static SKBitmap Redresser(SKBitmap source, SKEncodedOrigin sens)
        {
            if (sens is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default)
            {
                return source.Copy();
            }

            var couche = sens is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
            var sortie = new SKBitmap(couche ? source.Height : source.Width, couche ? source.Width : source.Height);
            using var toile = new SKCanvas(sortie);
            switch (sens)
            {
                case SKEncodedOrigin.BottomRight:
                    toile.RotateDegrees(180, source.Width / 2f, source.Height / 2f);
                    break;
                case SKEncodedOrigin.RightTop:
                    toile.Translate(sortie.Width, 0);
                    toile.RotateDegrees(90);
                    break;
                case SKEncodedOrigin.LeftBottom:
                    toile.Translate(0, sortie.Height);
                    toile.RotateDegrees(270);
                    break;
                default:
                    break; // miroirs : très rares sur un téléphone, laissés tels quels
            }

            toile.DrawBitmap(source, 0, 0);
            return sortie;
        }
    }
}

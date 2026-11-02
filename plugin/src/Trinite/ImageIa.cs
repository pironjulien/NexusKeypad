namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.IO;

    using SkiaSharp;

    // Image collée dans une IA, selon la règle commune des produits Nexus (27/10) : côté long
    // ≤ 2000 px et ≤ 2,56 Mpx, WebP sans perte tant qu'il tient 500 Ko, sinon qualité 90, 80 puis 70 ; jamais plus de
    // 500 Ko (plafond gardé pour Antigravity). SkiaSharp est celui du service Logi (aucune dépendance ajoutée au paquet).
    // Un seul fichier, le WebP : plus de PNG à côté (02/11 : sur un écran 5120×1440, le PNG pleine taille, le WebP sans
    // perte à l'effort maximal et le PNG réduit prenaient 1,3 s pour une capture et 2,6 s pour une photo avant le Ctrl+V).
    public static class ImageIa
    {
        private const Int32 CoteMax = 2000;
        private const Double PixelsMax = 2_560_000;
        private const Int32 PoidsMax = 500 * 1024;

        // Effort du WebP sans perte (0 à 100) : 25 pour 4 à 5 fois moins de temps que 100, pour 2 à 5 % d'octets en plus
        // (mesuré le 02/11 : capture 5120×1440 réduite, 34 ms au lieu de 167 ms).
        private const Single EffortSansPerte = 25;

        // Photo ou fichier image : redressée selon son EXIF puis écrite en <base>.webp ; rend le WebP, ou null si l'image
        // est illisible ou ne tient pas sous 500 Ko. Un JPEG est déjà avec perte : le sans perte n'y tiendrait jamais
        // 500 Ko (1,8 Mo pour une photo de 12 Mpx), il n'est pas essayé.
        public static String Ecrire(String source, String baseSortie)
        {
            using var codec = SKCodec.Create(source);
            if (codec is null)
            {
                return null;
            }

            using var brute = SKBitmap.Decode(codec);
            using var droite = Redresser(brute, codec.EncodedOrigin);
            return Ecrire(droite, baseSortie, sansPerte: codec.EncodedFormat != SKEncodedImageFormat.Jpeg);
        }

        // Image déjà en mémoire (capture d'écran) : réduite puis écrite en <base>.webp.
        public static String Ecrire(SKBitmap image, String baseSortie, Boolean sansPerte = true)
        {
            var k = Math.Min(1.0, Math.Min(CoteMax / (Double)Math.Max(image.Width, image.Height), Math.Sqrt(PixelsMax / ((Double)image.Width * image.Height))));
            using var reduite = k < 1
                ? image.Resize(new SKImageInfo(Math.Max(1, (Int32)(image.Width * k)), Math.Max(1, (Int32)(image.Height * k))), SKFilterQuality.High)
                : image.Copy();
            using var pixels = reduite.PeekPixels();

            SKData webp = null;
            foreach (var (compression, qualite) in new[] { (SKWebpEncoderCompression.Lossless, EffortSansPerte), (SKWebpEncoderCompression.Lossy, 90f), (SKWebpEncoderCompression.Lossy, 80f), (SKWebpEncoderCompression.Lossy, 70f) })
            {
                if (compression == SKWebpEncoderCompression.Lossless && !sansPerte)
                {
                    continue;
                }

                webp?.Dispose();
                webp = pixels.Encode(new SKWebpEncoderOptions(compression, qualite));
                if (webp is not null && webp.Size <= PoidsMax)
                {
                    break;
                }
            }

            using (webp)
            {
                if (webp is null || webp.Size > PoidsMax)
                {
                    return null;
                }

                // Écrit à part puis renommé : un appui pendant la préparation d'une photo ne lit jamais un fichier à moitié écrit.
                var fichier = baseSortie + ".webp";
                var partiel = $"{fichier}.{Guid.NewGuid():N}.part";
                File.WriteAllBytes(partiel, webp.ToArray());
                File.Move(partiel, fichier, overwrite: true);
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

namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;

    using SkiaSharp;

    // Image collée dans une IA, selon la règle commune des produits Nexus (NexusShared\images.md, 27/10, plafond relevé
    // le 06/11) : côté long ≤ 2000 px et ≤ 2,56 Mpx, WebP sans perte tant qu'il tient 3 Mo ; sinon sans perte en retirant
    // 2 puis 3 bits de poids faible par couleur, puis avec perte qualité 90, 80 puis 70 ; jamais plus de 3 Mo (aucune
    // des trois IA ne refuse moins de 6,8 Mo). SkiaSharp est celui du service Logi (aucune dépendance ajoutée au paquet).
    // Un seul fichier, le WebP : plus de PNG à côté (02/11 : sur un écran 5120×1440, le PNG pleine taille, le WebP sans
    // perte à l'effort maximal et le PNG réduit prenaient 1,3 s pour une capture et 2,6 s pour une photo avant le Ctrl+V).
    public static class ImageIa
    {
        private const Int32 CoteMax = 2000;
        private const Double PixelsMax = 2_560_000;
        internal const Int32 PoidsMax = 3 * 1024 * 1024;

        // Repli au-delà du plafond : jamais plus de 3 bits retirés (4 bits perdent des mots, mesure du 06/11).
        private static readonly Int32[] BitsRetires = { 2, 3 };
        private static readonly Single[] Qualites = { 90, 80, 70 };

        // Effort du WebP sans perte (0 à 100) : 25 pour 4 à 5 fois moins de temps que 100, pour 2 à 5 % d'octets en plus
        // (mesuré le 02/11 : capture 5120×1440 réduite, 34 ms au lieu de 167 ms).
        private const Single EffortSansPerte = 25;

        // Photo ou fichier image : redressée selon son EXIF puis écrite en <base>.webp ; rend le WebP, ou null si l'image
        // est illisible ou ne tient pas sous 3 Mo. Un JPEG passe aussi par le sans perte : réduit à 2,56 Mpx, il tient
        // 3 Mo (1,8 Mo pour une photo de 12 Mpx), l'IA reçoit alors les pixels du JPEG sans seconde perte.
        public static String? Ecrire(String source, String baseSortie)
        {
            using var codec = SKCodec.Create(source);
            if (codec is null)
            {
                return null;
            }

            using var brute = SKBitmap.Decode(codec);
            using var droite = Redresser(brute, codec.EncodedOrigin);
            return Ecrire(droite, baseSortie);
        }

        // Image déjà en mémoire (capture d'écran) : réduite puis écrite en <base>.webp.
        public static String? Ecrire(SKBitmap image, String baseSortie)
        {
            var k = Math.Min(1.0, Math.Min(CoteMax / (Double)Math.Max(image.Width, image.Height), Math.Sqrt(PixelsMax / ((Double)image.Width * image.Height))));
            using var reduite = k < 1
                ? image.Resize(new SKImageInfo(Math.Max(1, (Int32)(image.Width * k)), Math.Max(1, (Int32)(image.Height * k))), SKFilterQuality.High)
                : image.Copy();

            using var webp = Encoder(reduite);
            if (webp is null)
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

        // Le premier encodage qui tient le plafond, dans l'ordre de la règle ; null si aucun.
        private static SKData? Encoder(SKBitmap reduite)
        {
            using (var pixels = reduite.PeekPixels())
            {
                var sansPerte = Tient(pixels.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless, EffortSansPerte)));
                if (sansPerte is not null)
                {
                    return sansPerte;
                }
            }

            foreach (var bits in BitsRetires)
            {
                using var allegee = SansBitsFaibles(reduite, bits);
                using var pixels = allegee.PeekPixels();
                var webp = Tient(pixels.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless, EffortSansPerte)));
                if (webp is not null)
                {
                    return webp;
                }
            }

            using (var pixels = reduite.PeekPixels())
            {
                foreach (var qualite in Qualites)
                {
                    var webp = Tient(pixels.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, qualite)));
                    if (webp is not null)
                    {
                        return webp;
                    }
                }
            }

            return null;
        }

        private static SKData? Tient(SKData webp)
        {
            if (webp is not null && webp.Size <= PoidsMax)
            {
                return webp;
            }

            webp?.Dispose();
            return null;
        }

        // Copie BGRA dont les `bits` de poids faible de chaque couleur sont à zéro (écart ≤ 2^bits − 1 sur 255) ; alpha
        // intact. Comme NexusWindows (ImageEncoding.DropLowBits).
        internal static SKBitmap SansBitsFaibles(SKBitmap source, Int32 bits)
        {
            var copie = source.Copy(SKColorType.Bgra8888);
            var octets = new Byte[copie.ByteCount];
            Marshal.Copy(copie.GetPixels(), octets, 0, octets.Length);
            var masque = (Byte)(0xFF << bits);
            for (var i = 0; i < octets.Length; i++)
            {
                if ((i & 3) != 3)
                {
                    octets[i] &= masque;
                }
            }

            Marshal.Copy(octets, 0, copie.GetPixels(), octets.Length);
            copie.NotifyPixelsChanged();
            return copie;
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

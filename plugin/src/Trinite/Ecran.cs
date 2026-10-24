namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;

    using SkiaSharp;

    // Capture de l'écran où se trouve la fenêtre de devant, en pixels réels (le service Logi n'est pas forcément « DPI
    // aware » : sans cela, une capture à 125 % ne prenait qu'un coin de l'écran), écrite en PNG.
    public static class Ecran
    {
        private static readonly String Dossier = Path.Combine(
            Environment.GetEnvironmentVariable("USERPROFILE") ?? "", ".nexus", "trinite", "captures");

        // Rend le chemin du PNG de la capture, null si elle a échoué. Garde les 5 dernières captures.
        public static String Capturer()
        {
            var avant = SetThreadDpiAwarenessContext(new IntPtr(-4)); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
            var ecran = IntPtr.Zero;
            var memoire = IntPtr.Zero;
            var image = IntPtr.Zero;
            try
            {
                var infos = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (!GetMonitorInfo(MonitorFromWindow(GetForegroundWindow(), 2), ref infos)) // 2 : MONITOR_DEFAULTTONEAREST
                {
                    return null;
                }

                var (x, y) = (infos.rcMonitor.Gauche, infos.rcMonitor.Haut);
                var (l, h) = (infos.rcMonitor.Droite - x, infos.rcMonitor.Bas - y);
                ecran = GetDC(IntPtr.Zero);
                memoire = CreateCompatibleDC(ecran);
                image = CreateCompatibleBitmap(ecran, l, h);
                var ancienne = SelectObject(memoire, image);
                var copie = BitBlt(memoire, 0, 0, l, h, ecran, x, y, 0x00CC0020 | 0x40000000); // SRCCOPY | CAPTUREBLT
                SelectObject(memoire, ancienne);
                if (!copie)
                {
                    return null;
                }

                // Pixels BGRA, de haut en bas (hauteur négative).
                var entete = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = l, biHeight = -h, biPlanes = 1, biBitCount = 32 };
                var pixels = new Byte[l * h * 4];
                if (GetDIBits(memoire, image, 0, (UInt32)h, pixels, ref entete, 0) != h)
                {
                    return null;
                }

                for (var i = 3; i < pixels.Length; i += 4)
                {
                    pixels[i] = 255; // GDI laisse l'alpha à 0
                }

                using var bitmap = new SKBitmap(new SKImageInfo(l, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
                Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
                Directory.CreateDirectory(Dossier);
                var fichier = Path.Combine(Dossier, $"ecran-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
                using (var flux = File.Create(fichier))
                {
                    bitmap.Encode(flux, SKEncodedImageFormat.Png, 100);
                }

                foreach (var vieille in new DirectoryInfo(Dossier).EnumerateFiles("ecran-*").OrderByDescending(f => f.Name).Skip(15))
                {
                    vieille.Delete(); // 5 captures, chacune avec sa copie WebP et PNG pour l'IA
                }

                return fichier;
            }
            catch (Exception e)
            {
                PluginLog.Error(e, "capture d'écran");
                return null;
            }
            finally
            {
                if (image != IntPtr.Zero)
                {
                    DeleteObject(image);
                }

                if (memoire != IntPtr.Zero)
                {
                    DeleteDC(memoire);
                }

                if (ecran != IntPtr.Zero)
                {
                    ReleaseDC(IntPtr.Zero, ecran);
                }

                SetThreadDpiAwarenessContext(avant);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public Int32 Gauche, Haut, Droite, Bas;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public Int32 cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public UInt32 dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public Int32 biSize;
            public Int32 biWidth;
            public Int32 biHeight;
            public UInt16 biPlanes;
            public UInt16 biBitCount;
            public UInt32 biCompression;
            public UInt32 biSizeImage;
            public Int32 biXPelsPerMeter;
            public Int32 biYPelsPerMeter;
            public UInt32 biClrUsed;
            public UInt32 biClrImportant;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr contexte);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr h, UInt32 drapeaux);

        [DllImport("user32.dll")]
        private static extern Boolean GetMonitorInfo(IntPtr moniteur, ref MONITORINFO infos);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr h);

        [DllImport("user32.dll")]
        private static extern Int32 ReleaseDC(IntPtr h, IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, Int32 l, Int32 h);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr dc, IntPtr objet);

        [DllImport("gdi32.dll")]
        private static extern Boolean BitBlt(IntPtr dest, Int32 x, Int32 y, Int32 l, Int32 h, IntPtr source, Int32 xs, Int32 ys, UInt32 operation);

        [DllImport("gdi32.dll")]
        private static extern Int32 GetDIBits(IntPtr dc, IntPtr image, UInt32 debut, UInt32 lignes, Byte[] pixels, ref BITMAPINFOHEADER infos, UInt32 usage);

        [DllImport("gdi32.dll")]
        private static extern Boolean DeleteObject(IntPtr objet);

        [DllImport("gdi32.dll")]
        private static extern Boolean DeleteDC(IntPtr dc);
    }
}

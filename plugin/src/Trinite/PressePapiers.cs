namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading;

    using SkiaSharp;

    // Image et fichier dans le presse-papiers, par l'API Win32 du presse-papiers, sans processus : « PNG » (lu en premier
    // par Chromium et Electron), CF_DIB (les autres applis) et CF_HDROP (le fichier WebP, joint par son nom par les IA).
    // Remplace un PowerShell en STA gardé ouvert (60 à 80 Mo, relecture d'Antigravity, 25/10). Les données sont copiées
    // dans la mémoire globale du presse-papiers : elles restent après la fermeture de la fenêtre propriétaire.
    public static class PressePapiers
    {
        private const UInt32 CfDib = 8, CfHdrop = 15;
        private static readonly UInt32 CfPng = RegisterClipboardFormat("PNG");

        // png : l'image ; fichier : le fichier joint. Vrai si les trois formats sont posés.
        public static Boolean Poser(String png, String fichier)
        {
            var octetsPng = File.ReadAllBytes(png);
            Byte[] dib;
            using (var image = SKBitmap.Decode(octetsPng))
            {
                if (image is null)
                {
                    return false;
                }

                dib = Dib(image);
            }

            var chemin = Encoding.Unicode.GetBytes(Path.GetFullPath(fichier) + "\0\0");
            var depot = new Byte[20 + chemin.Length]; // DROPFILES : pFiles, pt, fNC, fWide
            BitConverter.GetBytes(20).CopyTo(depot, 0);
            BitConverter.GetBytes(1).CopyTo(depot, 16);
            chemin.CopyTo(depot, 20);

            // Fenêtre de message propre : propriétaire du presse-papiers (avec une fenêtre nulle, SetClipboardData peut échouer).
            var fenetre = CreateWindowEx(0, "STATIC", "", 0, 0, 0, 0, 0, new IntPtr(-3), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            try
            {
                // Tenu par une autre appli un instant (gestionnaire d'historique, copie en cours) : 20 essais en 1 s.
                var ouvert = false;
                for (var essai = 0; essai < 20 && !(ouvert = OpenClipboard(fenetre)); essai++)
                {
                    Thread.Sleep(50);
                }

                if (!ouvert)
                {
                    PluginLog.Warning("presse-papiers tenu par une autre appli");
                    return false;
                }

                try
                {
                    return EmptyClipboard() && Poser(CfPng, octetsPng) && Poser(CfDib, dib) && Poser(CfHdrop, depot);
                }
                finally
                {
                    CloseClipboard();
                }
            }
            finally
            {
                if (fenetre != IntPtr.Zero)
                {
                    DestroyWindow(fenetre);
                }
            }
        }

        // Un format : copié en mémoire globale, cédée au presse-papiers s'il l'accepte (libérée sinon).
        private static Boolean Poser(UInt32 format, Byte[] octets)
        {
            var memoire = GlobalAlloc(0x0002, (UIntPtr)octets.Length); // GMEM_MOVEABLE
            if (memoire == IntPtr.Zero)
            {
                return false;
            }

            var adresse = GlobalLock(memoire);
            if (adresse == IntPtr.Zero)
            {
                GlobalFree(memoire);
                return false;
            }

            Marshal.Copy(octets, 0, adresse, octets.Length);
            GlobalUnlock(memoire);
            if (SetClipboardData(format, memoire) == IntPtr.Zero)
            {
                GlobalFree(memoire);
                return false;
            }

            return true;
        }

        // CF_DIB : BITMAPINFOHEADER puis les pixels BGRA 32 bits, lignes de bas en haut.
        private static Byte[] Dib(SKBitmap image)
        {
            using var bgra = image.Copy(SKColorType.Bgra8888);
            var (largeur, hauteur) = (bgra.Width, bgra.Height);
            var ligne = largeur * 4;
            var dib = new Byte[40 + (ligne * hauteur)];
            BitConverter.GetBytes(40).CopyTo(dib, 0);
            BitConverter.GetBytes(largeur).CopyTo(dib, 4);
            BitConverter.GetBytes(hauteur).CopyTo(dib, 8);
            BitConverter.GetBytes((Int16)1).CopyTo(dib, 12);
            BitConverter.GetBytes((Int16)32).CopyTo(dib, 14);
            BitConverter.GetBytes(ligne * hauteur).CopyTo(dib, 20);
            var pixels = bgra.Bytes;
            for (var y = 0; y < hauteur; y++)
            {
                Buffer.BlockCopy(pixels, y * bgra.RowBytes, dib, 40 + ((hauteur - 1 - y) * ligne), ligne);
            }

            return dib;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern UInt32 RegisterClipboardFormat(String nom);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(UInt32 styleEx, String classe, String titre, UInt32 style, Int32 x, Int32 y, Int32 l, Int32 h,
            IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll")]
        private static extern Boolean DestroyWindow(IntPtr h);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern Boolean OpenClipboard(IntPtr h);

        [DllImport("user32.dll")]
        private static extern Boolean EmptyClipboard();

        [DllImport("user32.dll")]
        private static extern Boolean CloseClipboard();

        [DllImport("user32.dll")]
        private static extern IntPtr SetClipboardData(UInt32 format, IntPtr memoire);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalAlloc(UInt32 drapeaux, UIntPtr taille);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalLock(IntPtr memoire);

        [DllImport("kernel32.dll")]
        private static extern Boolean GlobalUnlock(IntPtr memoire);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalFree(IntPtr memoire);
    }
}

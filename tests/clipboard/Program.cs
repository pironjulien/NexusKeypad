using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Loupedeck.TrinitePlugin;

// Essai matériel opt-in : image de test, jamais une photo personnelle, aucune saisie ou application IA.
if (!args.SequenceEqual(new[] { "--real-clipboard" }))
{
    Console.WriteLine("Essai réel opt-in : --real-clipboard (presse-papiers sauvegardé puis restauré).");
    return 0;
}
Exception echec = null;
var thread = new Thread(() =>
{
    var original = Clipboard.GetDataObject();
    uint sequence = 0;
    try
    {
        var dossier = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusKeypad", "test", "clipboard", "fixture");
        Directory.CreateDirectory(dossier);
        var source = Path.Combine(dossier, "checker.png");
        using (var image = new Bitmap(32, 32))
        {
            using var dessin = Graphics.FromImage(image);
            dessin.Clear(Color.White);
            dessin.FillRectangle(Brushes.Black, 0, 0, 16, 16);
            dessin.FillRectangle(Brushes.Black, 16, 16, 16, 16);
            image.Save(source, ImageFormat.Png);
        }
        using var telephone = new Telephone();
        var copie = telephone.CopierImage(source, Path.Combine(dossier, "prepared"), "Fixture locale");
        sequence = Natif.GetClipboardSequenceNumber();
        if (!copie || !Clipboard.ContainsImage() || !Clipboard.ContainsFileDropList())
            throw new InvalidOperationException("Le pipeline du pad n'a pas publié l'image et son fichier.");
        var fichiers = Clipboard.GetFileDropList();
        if (fichiers.Count != 1 || !String.Equals(fichiers[0], Path.Combine(dossier, "prepared.webp"), StringComparison.OrdinalIgnoreCase)
            || new FileInfo(fichiers[0]).Length is <= 0 or > 512000)
            throw new InvalidOperationException("Le WebP publié est absent, inattendu ou trop lourd.");
        Console.WriteLine("Photo/capture : PNG, image Windows et fichier WebP de test publiés par le vrai pipeline ; aucune IA ni réseau sollicités.");
    }
    catch (Exception erreur) { echec = erreur; }
    finally
    {
        // Si Julien a repris son presse-papiers pendant l'essai, conserver sa nouvelle valeur.
        if (sequence != 0 && Natif.GetClipboardSequenceNumber() == sequence)
        {
            if (original is null) Clipboard.Clear(); else Clipboard.SetDataObject(original, true);
            Console.WriteLine("Presse-papiers initial restauré.");
        }
    }
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
if (echec is not null) throw echec;
return 0;

static class Natif
{
    [DllImport("user32.dll")]
    public static extern uint GetClipboardSequenceNumber();
}

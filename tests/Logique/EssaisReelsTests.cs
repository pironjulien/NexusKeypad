using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using Loupedeck.TrinitePlugin;
using static NexusKeypad.Tests.Commun;

namespace NexusKeypad.Tests;

// Opt-in checks on the real PC (explicit tests: never run by test.ps1 or the CI). They read the real apps or use the real
// clipboard, so they run only when asked by name, for instance:
//   dotnet test --project tests\Logique -- --filter-method "*Presse_papiers_reel*" --explicit only
// None of them presses a key, types into an app or starts or stops an app.
public sealed class EssaisReelsTests
{
    private static void Ecrire(String texte) => TestContext.Current.SendDiagnosticMessage(texte);

    [Fact(Explicit = true)]
    public void Presse_papiers_reel_trois_formats()
    {
        // Overwrites the clipboard's content (see Pipeline_photo_reel for the saved and restored version).
        var dossier = Directory.CreateTempSubdirectory("nexus-presse").FullName;
        try
        {
            var png = Path.Combine(dossier, "essai.png");
            var webp = Path.Combine(dossier, "essai.webp");
            using (var image = new SkiaSharp.SKBitmap(7, 3))
            {
                image.Erase(SkiaSharp.SKColors.Red);
                File.WriteAllBytes(png, image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100).ToArray());
                File.WriteAllBytes(webp, image.Encode(SkiaSharp.SKEncodedImageFormat.Webp, 100).ToArray());
            }

            Vrai(PressePapiers.Poser(png, webp), "presse-papiers : trois formats posés");
            Vrai(LecturePressePapiers.Formats().SetEquals([8u, 15u, LecturePressePapiers.Png]), "presse-papiers : PNG, CF_DIB et CF_HDROP lisibles");
            Vrai(LecturePressePapiers.Fichier() == webp, "presse-papiers : fichier joint = le WebP");
            Vrai(LecturePressePapiers.Dib() == (7, 3, 32), "presse-papiers : DIB 7×3 en 32 bits");
        }
        finally
        {
            Directory.Delete(dossier, true);
        }
    }

    [Fact(Explicit = true)]
    public void Pipeline_photo_reel_presse_papiers_restaure()
    {
        // Test image only, never a personal photo; no AI app or network. The user's clipboard is saved, then restored unless
        // they changed it during the check.
        Exception echec = null;
        var thread = new Thread(() =>
        {
            var original = System.Windows.Forms.Clipboard.GetDataObject();
            UInt32 sequence = 0;
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
                sequence = LecturePressePapiers.GetClipboardSequenceNumber();
                if (!copie || !System.Windows.Forms.Clipboard.ContainsImage() || !System.Windows.Forms.Clipboard.ContainsFileDropList())
                {
                    throw new InvalidOperationException("Le pipeline du pad n'a pas publié l'image et son fichier.");
                }

                var fichiers = System.Windows.Forms.Clipboard.GetFileDropList();
                if (fichiers.Count != 1 || !String.Equals(fichiers[0], Path.Combine(dossier, "prepared.webp"), StringComparison.OrdinalIgnoreCase)
                    || new FileInfo(fichiers[0]).Length is <= 0 or > 512000)
                {
                    throw new InvalidOperationException("Le WebP publié est absent, inattendu ou trop lourd.");
                }
            }
            catch (Exception erreur)
            {
                echec = erreur;
            }
            finally
            {
                if (sequence != 0 && LecturePressePapiers.GetClipboardSequenceNumber() == sequence)
                {
                    if (original is null)
                    {
                        System.Windows.Forms.Clipboard.Clear();
                    }
                    else
                    {
                        System.Windows.Forms.Clipboard.SetDataObject(original, true);
                    }
                }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(echec);
    }

    [Fact(Explicit = true)]
    public void Connexions_des_trois_IA_en_lecture_seule()
    {
        for (var agent = 0; agent < 3; agent++)
        {
            var lancement = Lanceurs.Preparer(agent);
            var connexion = ConnexionPad.Lire(forcer: true)[agent];
            Ecrire($"{Observateur.Agents[agent]}: {connexion.Etat}; app={lancement.Etat}; {connexion.Detail}");
        }
    }

    [Fact(Explicit = true)]
    public void Application_officielle_Antigravity_prete()
    {
        var preparation = Lanceurs.Preparer(2);
        Ecrire(JsonSerializer.Serialize(new { state = preparation.Etat.ToString(), preparation.Detail }));
        Assert.Equal(EtatLancement.Pret, preparation.Etat);
    }

    [Fact(Explicit = true)]
    public void Profil_Codex_reel_change_cible_sans_ecriture()
    {
        var profil = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "config.toml");
        Connecteurs.ActiverToml(File.ReadAllText(profil), "nexus-keypad@personal", true);
    }

    [Fact(Explicit = true)]
    public void Fenetre_Claude_usage_et_dictee() =>
        Ecrire(JsonSerializer.Serialize(new { window = Uia.FenetrePrincipale("claude").ToInt64(), usage = Uia.UtilisationClaudeOuverte(), recording = Uia.DicteeClaudeObservee() }));

    [Fact(Explicit = true)]
    public void Page_Antigravity_lue_passivement()
    {
        var lu = Cdp.Evaluer(Cdp.PortAntigravity, """
            (() => {
              const root = document.querySelector('#root'), css = root && getComputedStyle(root), rect = root?.getBoundingClientRect();
              return JSON.stringify({title:document.title,route:location.pathname,ready:document.readyState,visibility:document.visibilityState,
                rows:[...document.querySelectorAll('[data-testid=conversation-row-sidebar]')].map(r=>r.getAttribute('data-cascade-id')),
                model:document.querySelector('[data-testid=model-selector-trigger]')?.getAttribute('aria-label'),
                root:!!root,rootChildren:root?.childElementCount,rootStyle:css && {display:css.display,visibility:css.visibility,opacity:css.opacity},
                rootRect:rect && {width:rect.width,height:rect.height},textLength:document.body?.innerText.length,
                controls:{buttons:document.querySelectorAll('button').length,inputs:document.querySelectorAll('textarea,[contenteditable=true]').length,
                  scripts:document.querySelectorAll('script').length,frames:document.querySelectorAll('iframe').length},
                restore:[...document.querySelectorAll('button')].some(e=>(e.innerText||'').trim()==='Restore'),
                labels:[...document.querySelectorAll('button[aria-label]')].map(e=>e.getAttribute('aria-label')).filter(x=>/conversation|model|archive|record|send|error|retry|back|close/i.test(x)),
                testIds:[...new Set([...document.querySelectorAll('[data-testid]')].map(e=>e.getAttribute('data-testid')))].slice(0,70),
                fault:['TypeError','ReferenceError','SyntaxError','Unexpected Application Error'].filter(x=>(document.body?.innerText||'').includes(x))});
            })()
            """, attente: 2);
        Assert.True(lu is { ValueKind: JsonValueKind.String }, "Antigravity page unavailable.");
        Ecrire(lu.Value.GetString());
    }

    // Antigravity conversation (GUID) for the two checks below, given by NEXUSKEYPAD_ANTIGRAVITY_CONVERSATION.
    private static String Conversation()
    {
        var cible = Environment.GetEnvironmentVariable("NEXUSKEYPAD_ANTIGRAVITY_CONVERSATION");
        Assert.True(Guid.TryParse(cible, out _), "Set NEXUSKEYPAD_ANTIGRAVITY_CONVERSATION to the conversation's GUID.");
        return cible;
    }

    [Fact(Explicit = true)]
    public void Navigation_Antigravity_lue_passivement()
    {
        var etat = Cdp.Evaluer(Cdp.PortAntigravity, Js.NavigationAntigravity(Conversation()), attente: 2);
        Assert.True(etat is { ValueKind: JsonValueKind.String }, "Antigravity page unavailable.");
        Ecrire(etat.Value.GetString());
    }

    [Fact(Explicit = true)]
    public void Navigation_Antigravity_reelle()
    {
        // Opens the given conversation in the real Antigravity app (one gesture, never replayed).
        var cible = Conversation();
        var horloge = System.Diagnostics.Stopwatch.StartNew();
        var ouvert = NavigationAntigravity.Ouvrir(cible);
        Ecrire(JsonSerializer.Serialize(new { verified = ouvert, elapsed_ms = horloge.ElapsedMilliseconds, target = cible }));
        Assert.True(ouvert);
    }
}

// Reads the clipboard back for the real checks (Win32 API, as the apps that paste do).
internal static class LecturePressePapiers
{
    public static readonly UInt32 Png = RegisterClipboardFormat("PNG");

    public static HashSet<UInt32> Formats()
    {
        var formats = new HashSet<UInt32>();
        if (OpenClipboard(IntPtr.Zero))
        {
            for (var f = EnumClipboardFormats(0); f != 0; f = EnumClipboardFormats(f))
            {
                formats.Add(f);
            }

            CloseClipboard();
        }

        formats.IntersectWith([8u, 15u, Png]);
        return formats;
    }

    public static String Fichier()
    {
        if (!OpenClipboard(IntPtr.Zero))
        {
            return null;
        }

        try
        {
            var depot = GetClipboardData(15);
            var nom = new System.Text.StringBuilder(1024);
            return DragQueryFile(depot, 0, nom, nom.Capacity) > 0 ? nom.ToString() : null;
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static (Int32, Int32, Int16) Dib()
    {
        if (!OpenClipboard(IntPtr.Zero))
        {
            return default;
        }

        try
        {
            var adresse = GlobalLock(GetClipboardData(8));
            var resultat = (Marshal.ReadInt32(adresse, 4), Marshal.ReadInt32(adresse, 8), Marshal.ReadInt16(adresse, 14));
            GlobalUnlock(GetClipboardData(8));
            return resultat;
        }
        finally
        {
            CloseClipboard();
        }
    }

    [DllImport("user32.dll")] public static extern UInt32 GetClipboardSequenceNumber();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern UInt32 RegisterClipboardFormat(String nom);
    [DllImport("user32.dll")] private static extern Boolean OpenClipboard(IntPtr h);
    [DllImport("user32.dll")] private static extern Boolean CloseClipboard();
    [DllImport("user32.dll")] private static extern UInt32 EnumClipboardFormats(UInt32 format);
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(UInt32 format);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern UInt32 DragQueryFile(IntPtr depot, UInt32 i, System.Text.StringBuilder nom, Int32 max);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr m);
    [DllImport("kernel32.dll")] private static extern Boolean GlobalUnlock(IntPtr m);
}

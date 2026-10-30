#if !POSTE
namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Text.Json;
    using System.Threading;

    public enum EtatLancement { Pret, Fermee, Relancer, Absent, Erreur, Masquee }

    public sealed record PreparationLancement(EtatLancement Etat, String Detail);

    // Un seul lancement par IA. Les applications restent officielles ; aucune option de leurs raccourcis n'est modifiée.
    // Les ports CDP ne sont considérés prêts qu'avec un processus propriétaire et une page de l'application attendue.
    public static class Lanceurs
    {
        private const String ClaudeOfficiel = "AnthropicPBC.Claude_fnn82j28hfe8t!Claude";
        private const String CodexOfficiel = "OpenAI.Codex_2p2nqsd0c76g0!App";
        private static readonly String Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        private static readonly String[] Processus = ["claude", "ChatGPT", "Antigravity"];
        private static readonly Object[] Verrous = [new(), new(), new()];
        private static readonly String[] Erreurs = ["", "", ""];
        private static readonly HttpClient Http = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(1) };

        public static String DerniereErreur(Int32 agent) => agent is >= 0 and < 3 ? Volatile.Read(ref Erreurs[agent]) : "unknown app";

        private static String CodexLb => Path.Combine(Local, "ChatGPTLBLauncher", "ChatGPTLbLauncher.exe");

        // Lecture seulement : aucun processus démarré ou arrêté, aucun fichier écrit.
        public static PreparationLancement Preparer(Int32 agent)
        {
            try
            {
                if (agent is < 0 or > 2)
                {
                    return new(EtatLancement.Erreur, "unknown app");
                }

                var processus = LireProcessus(agent);
                if (processus.Count > 0)
                {
                    return agent == 0 ? new(EtatLancement.Pret, "app connected") : VerifierConnexion(agent, processus);
                }

                var presente = agent switch
                {
                    0 => PaquetPresent(ClaudeOfficiel),
                    1 => File.Exists(CodexLb) || PaquetPresent(CodexOfficiel),
                    _ => AntigravityExecutable() is not null,
                };
                return presente ? new(EtatLancement.Fermee, "app is closed") : new(EtatLancement.Absent, "official app not found");
            }
            catch (Exception ex)
            {
                return new(EtatLancement.Erreur, ex.GetType().Name);
            }
        }

        // N'arrête jamais une application. Une application ouverte sans son port reste à RELANCER.
        public static Boolean Demarrer(Int32 agent)
        {
            if (agent is < 0 or > 2)
            {
                return false;
            }

            lock (Verrous[agent])
            {
                try
                {
                    return DemarrerSansVerrou(agent);
                }
                catch (Exception ex)
                {
                    Erreurs[agent] = ex.GetType().Name;
                    return false;
                }
            }
        }

        private static Boolean DemarrerSansVerrou(Int32 agent)
        {
            var avant = Preparer(agent);
            var resultat = ExecuterDemarrage(avant,
                () => avant.Etat == EtatLancement.Masquee ? Rappeler(agent) : LancerFermee(agent),
                () => Preparer(agent).Etat == EtatLancement.Pret, Thread.Sleep);
            Erreurs[agent] = resultat ? "" : avant.Etat is EtatLancement.Relancer or EtatLancement.Erreur
                ? avant.Detail : "app launch or connection not verified";
            return resultat;
        }

        // Appel seulement après la confirmation de l'utilisateur et le contrôle des conversations/dictées côté pad.
        // WM_CLOSE laisse l'application afficher sa confirmation ou son dialogue d'enregistrement. Aucun kill, même en cas d'échec.
        public static Boolean Relancer(Int32 agent)
        {
            if (agent is < 0 or > 2)
            {
                return false;
            }

            lock (Verrous[agent])
            {
                try
                {
                    var avant = Preparer(agent);
                    if (avant.Etat is EtatLancement.Pret or EtatLancement.Masquee or EtatLancement.Fermee)
                    {
                        // La liaison peut être revenue pendant la confirmation, ou la fenêtre seulement masquée.
                        // Dans ces cas, aucune fermeture n'est demandée.
                        return DemarrerSansVerrou(agent);
                    }

                    if (avant.Etat != EtatLancement.Relancer)
                    {
                        Erreurs[agent] = avant.Detail;
                        return false; // Une preuve devenue indécidable n'autorise jamais une demande de fermeture.
                    }

                    if (LireProcessus(agent) is { Count: > 0 } processus)
                    {
                        var ids = processus.ToHashSet();
                        var fenetres = new List<IntPtr>();
                        EnumWindows((fenetre, _) =>
                        {
                            GetWindowThreadProcessId(fenetre, out var id);
                            if (ids.Contains((Int32)id) && IsWindowVisible(fenetre) && GetWindowTextLength(fenetre) > 0)
                            {
                                fenetres.Add(fenetre);
                            }

                            return true;
                        }, IntPtr.Zero);
                        if (fenetres.Count == 0 || fenetres.Any(f => !PostMessage(f, 0x0010, IntPtr.Zero, IntPtr.Zero)))
                        {
                            Erreurs[agent] = "app did not accept a normal close";
                            return false;
                        }

                        for (var i = 0; i < 75 && LireProcessus(agent).Count > 0; i++)
                        {
                            Thread.Sleep(200);
                        }

                        if (LireProcessus(agent).Count > 0)
                        {
                            Erreurs[agent] = "app is still open; no forced close was attempted";
                            return false;
                        }
                    }

                    return DemarrerSansVerrou(agent);
                }
                catch (Exception ex)
                {
                    Erreurs[agent] = ex.GetType().Name;
                    return false;
                }
            }
        }

        // Séparable pour vérifier hors ligne qu'une application déjà ouverte ne reçoit jamais un second lancement.
        internal static Boolean ExecuterDemarrage(PreparationLancement avant, Func<Boolean> lancer, Func<Boolean> connecte, Action<Int32> attendre)
        {
            if (avant.Etat == EtatLancement.Pret)
            {
                return true;
            }

            if (avant.Etat is not (EtatLancement.Fermee or EtatLancement.Masquee) || !lancer())
            {
                return false;
            }

            for (var i = 0; i < 50; i++)
            {
                if (connecte())
                {
                    return true;
                }

                attendre(200);
            }

            return false;
        }

        private static Boolean LancerFermee(Int32 agent)
        {
            // Relu au dernier moment : une application ouverte depuis le premier relevé ne doit jamais être redémarrée.
            if (LireProcessus(agent).Count > 0)
            {
                return false;
            }

            return LancerApplication(agent);
        }

        // Activation normale d'une application existante, même si elle n'a pas son port. Aucun argument de connexion
        // ajouté, aucune fermeture : son état reste RELANCER jusqu'à une vraie préparation ultérieure.
        public static Boolean Rappeler(Int32 agent)
        {
            if (agent is < 0 or > 2 || Preparer(agent).Etat is not (EtatLancement.Pret or EtatLancement.Masquee or EtatLancement.Relancer))
            {
                return false;
            }

            try
            {
                return LancerApplication(agent, dejaOuverte: true);
            }
            catch (Exception ex)
            {
                Erreurs[agent] = ex.GetType().Name;
                return false;
            }
        }

        private static Boolean LancerApplication(Int32 agent, Boolean dejaOuverte = false)
        {
            if (agent == 0)
            {
                return ActiverPaquet(ClaudeOfficiel, "");
            }

            if (agent == 1)
            {
                if (!dejaOuverte && File.Exists(CodexLb))
                {
                    return LancerUtilisateur(new ProcessStartInfo(CodexLb));
                }

                return ActiverPaquet(CodexOfficiel, dejaOuverte ? "" : $"--remote-debugging-port={Cdp.PortCodex} --remote-debugging-address=127.0.0.1");
            }

            var exe = AntigravityExecutable();
            return exe is not null && LancerUtilisateur(CommandeAntigravity(exe, dejaOuverte));
        }

        // Le shell interactif possède l'application : elle survit au service du pad et à son éventuel job Windows.
        internal static Boolean LancerUtilisateur(ProcessStartInfo info, Int32 afficher = 1)
        {
            UserApplication.Start(info, () => { }, afficher);
            return true;
        }

        internal static ProcessStartInfo CommandeAntigravity(String exe, Boolean dejaOuverte = false)
        {
            var info = new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe) };
            if (!dejaOuverte)
            {
                info.Arguments = $"--remote-debugging-port={Cdp.PortAntigravity} --remote-debugging-address=127.0.0.1";
            }

            return info;
        }

        // Le raccourci créé par l'installation officielle est la source principale, sans reprendre ses arguments.
        // Les exécutables trouvés hors raccourci doivent aussi avoir une signature Google valide.
        public static String AntigravityExecutable()
        {
            foreach (var dossier in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms,
                Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory })
            {
                var racine = Environment.GetFolderPath(dossier);
                if (String.IsNullOrEmpty(racine) || !Directory.Exists(racine))
                {
                    continue;
                }

                IEnumerable<String> raccourcis;
                try
                {
                    raccourcis = Directory.GetFiles(racine, "Antigravity.lnk", SearchOption.AllDirectories);
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var raccourci in raccourcis)
                {
                    var exe = CibleRaccourci(raccourci);
                    if (ExecutableAntigravity(exe))
                    {
                        return Path.GetFullPath(exe);
                    }
                }
            }

            foreach (var racine in new[] { Path.Combine(Local, "Programs"), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
            {
                var exe = Path.Combine(racine, "Antigravity", "Antigravity.exe");
                if (ExecutableAntigravity(exe) && SignatureGoogle(exe))
                {
                    return exe;
                }
            }

            return null;
        }

        internal static Boolean IdentiteAntigravity(String nom, String produit, String societe) =>
            String.Equals(nom, "Antigravity.exe", StringComparison.OrdinalIgnoreCase)
            && String.Equals(produit, "Antigravity", StringComparison.OrdinalIgnoreCase)
            && (String.Equals(societe, "Google", StringComparison.OrdinalIgnoreCase) || String.Equals(societe, "Google LLC", StringComparison.OrdinalIgnoreCase));

        private static Boolean ExecutableAntigravity(String exe)
        {
            try
            {
                if (String.IsNullOrEmpty(exe) || !Path.IsPathFullyQualified(exe) || !File.Exists(exe))
                {
                    return false;
                }

                var version = FileVersionInfo.GetVersionInfo(exe);
                return IdentiteAntigravity(Path.GetFileName(exe), version.ProductName, version.CompanyName);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static String CibleRaccourci(String chemin)
        {
            Object shell = null, lien = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                lien = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [chemin]);
                return lien.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, lien, null) as String;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                if (lien is not null) { Marshal.FinalReleaseComObject(lien); }
                if (shell is not null) { Marshal.FinalReleaseComObject(shell); }
            }
        }

        private static Boolean SignatureGoogle(String exe)
        {
            try
            {
                // A fixed, readable command (never -EncodedCommand, which antivirus heuristics flag); the path travels in an
                // environment variable, so nothing is ever quoted into the script.
                const String commande = "$s=Get-AuthenticodeSignature -LiteralPath $env:NEXUSKEYPAD_EXE; "
                    + "if ($s.Status -eq 'Valid' -and $s.SignerCertificate.Subject -match '(^|, )CN=Google (LLC|Inc\\.?)(,|$)') { exit 0 }; exit 1";
                var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "WindowsPowerShell", "v1.0", "powershell.exe")) { UseShellExecute = false, CreateNoWindow = true };
                foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", commande })
                {
                    info.ArgumentList.Add(arg);
                }

                info.Environment["NEXUSKEYPAD_EXE"] = exe;

                using var verification = Process.Start(info);
                return verification is not null && verification.WaitForExit(5000) && verification.ExitCode == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Le propriétaire TCP est contrôlé avant de lire la liste CDP : une autre appli sur le même port ne vaut jamais « prêt ».
        public static Boolean Connecte(Int32 agent)
        {
            if (agent is not (1 or 2))
            {
                return agent == 0 && LireProcessus(0).Count > 0;
            }

            try
            {
                return VerifierConnexion(agent, LireProcessus(agent)).Etat == EtatLancement.Pret;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static PreparationLancement VerifierConnexion(Int32 agent, List<Int32> processus)
        {
            var port = agent == 1 ? Cdp.PortCodex : Cdp.PortAntigravity;
            var proprietaire = Proprietaire(port);
            if (proprietaire is not Int32 id || !processus.Contains(id))
            {
                return new(EtatLancement.Relancer, "app is open without its own verified local debugging port");
            }

            try
            {
                var liste = Http.GetStringAsync($"http://127.0.0.1:{port}/json/list").GetAwaiter().GetResult();
                return EtatPage(agent, port, liste, FenetreVisible(processus));
            }
            catch (Exception)
            {
                // Une lecture muette n'est pas une preuve d'absence du port : pas de demande de redémarrage sur timeout.
                return new(EtatLancement.Erreur, "app owns its local port, but its connection could not be read; try again");
            }
        }

        private static Boolean FenetreVisible(List<Int32> processus)
        {
            var trouvee = false;
            EnumWindows((fenetre, _) =>
            {
                GetWindowThreadProcessId(fenetre, out var id);
                if (processus.Contains((Int32)id) && IsWindowVisible(fenetre) && GetWindowTextLength(fenetre) > 0)
                {
                    trouvee = true;
                    return false;
                }

                return true;
            }, IntPtr.Zero);
            return trouvee;
        }

        internal static PreparationLancement EtatPage(Int32 agent, Int32 port, String liste, Boolean fenetreVisible)
        {
            if (PageAttendue(agent, port, liste)) { return new(EtatLancement.Pret, "app connected"); }
            try
            {
                using var doc = JsonDocument.Parse(liste);
                if (!fenetreVisible && doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() == 0)
                {
                    return new(EtatLancement.Masquee, "local port verified; reopen the app window without restarting");
                }

                return new(EtatLancement.Relancer, "app owns its local port, but its expected page is missing");
            }
            catch (JsonException)
            {
                return new(EtatLancement.Erreur, "app connection returned an unreadable page list; try again");
            }
        }

        internal static Boolean PageAttendue(Int32 agent, Int32 port, String liste)
        {
            try
            {
                using var doc = JsonDocument.Parse(liste);
                return doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.EnumerateArray().Any(page =>
                    page.TryGetProperty("type", out var type) && type.GetString() == "page"
                    && page.TryGetProperty("webSocketDebuggerUrl", out var socket) && Uri.TryCreate(socket.GetString(), UriKind.Absolute, out var ws)
                    && ws.Scheme == "ws" && ws.IsLoopback && ws.Port == port
                    && page.TryGetProperty("url", out var url) && (agent == 1 ? url.GetString() == "app://-/index.html"
                        : agent == 2 && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.IsLoopback));
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static List<Int32> LireProcessus(Int32 agent)
        {
            var resultat = new List<Int32>();
            foreach (var processus in Process.GetProcessesByName(Processus[agent]))
            {
                using (processus)
                {
                    if (ProcessusAttendu(agent, processus))
                    {
                        resultat.Add(processus.Id);
                    }
                }
            }

            return resultat;
        }

        private static Boolean ProcessusAttendu(Int32 agent, Process p)
        {
            try
            {
                var exe = p.MainModule?.FileName;
                return agent switch
                {
                    0 => p.ProcessName.Equals("claude", StringComparison.OrdinalIgnoreCase),
                    1 => exe is not null && Path.GetFileName(exe).Equals("ChatGPT.exe", StringComparison.OrdinalIgnoreCase)
                        && Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(exe))).StartsWith("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase),
                    2 => ExecutableAntigravity(exe),
                    _ => false,
                };
            }
            catch (Exception)
            {
                return false;
            }
        }

        // table IPv4 propriétaire (AF_INET, TCP_TABLE_OWNER_PID_LISTENER). Une écoute non locale est refusée.
        private static Int32? Proprietaire(Int32 port)
        {
            var taille = 0;
            _ = GetExtendedTcpTable(IntPtr.Zero, ref taille, false, 2, 3, 0);
            var memoire = Marshal.AllocHGlobal(taille);
            try
            {
                if (GetExtendedTcpTable(memoire, ref taille, false, 2, 3, 0) != 0)
                {
                    return null;
                }

                var nombre = Marshal.ReadInt32(memoire);
                for (var i = 0; i < nombre; i++)
                {
                    var ligne = IntPtr.Add(memoire, 4 + (i * 24));
                    var numero = (UInt16)IPAddress.NetworkToHostOrder((Int16)Marshal.ReadInt32(ligne, 8));
                    if (numero == port && (UInt32)Marshal.ReadInt32(ligne, 4) == 0x0100007f)
                    {
                        return Marshal.ReadInt32(ligne, 20);
                    }
                }

                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(memoire);
            }
        }

        private static Boolean PaquetPresent(String identifiant)
        {
            var nombre = 0u;
            var taille = 0u;
            var code = GetPackagesByPackageFamily(identifiant.Split('!')[0], ref nombre, IntPtr.Zero, ref taille, IntPtr.Zero);
            return nombre > 0 && code is 0 or 122;
        }

        // Contrat Windows.Launch documenté : arguments applicatifs transmis à l'appli du Store, sans shell construit.
        private static Boolean ActiverPaquet(String identifiant, String arguments)
        {
            var reussi = false;
            var fil = new Thread(() =>
            {
                try
                {
                    reussi = ((IApplicationActivationManager)new ApplicationActivationManager())
                        .ActivateApplication(identifiant, arguments, 0, out _) == 0;
                }
                catch (Exception ex)
                {
                    PluginLog.Warning(ex, "lancement officiel");
                }
            }) { IsBackground = true, Name = "NexusKeypad launch" };
            fil.SetApartmentState(ApartmentState.STA);
            fil.Start();
            return fil.Join(10000) && reussi;
        }

        [DllImport("iphlpapi.dll")]
        private static extern Int32 GetExtendedTcpTable(IntPtr table, ref Int32 taille, Boolean ordre, Int32 famille, Int32 classe, UInt32 reserve);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern Int32 GetPackagesByPackageFamily(String famille, ref UInt32 nombre, IntPtr noms, ref UInt32 taille, IntPtr tampon);

        private delegate Boolean EnumWindowsProc(IntPtr fenetre, IntPtr argument);

        [DllImport("user32.dll")]
        private static extern Boolean EnumWindows(EnumWindowsProc callback, IntPtr argument);

        [DllImport("user32.dll")]
        private static extern UInt32 GetWindowThreadProcessId(IntPtr fenetre, out UInt32 processus);

        [DllImport("user32.dll")]
        private static extern Boolean IsWindowVisible(IntPtr fenetre);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern Int32 GetWindowTextLength(IntPtr fenetre);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern Boolean PostMessage(IntPtr fenetre, UInt32 message, IntPtr wParam, IntPtr lParam);

        [ComImport]
        [Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
        private class ApplicationActivationManager { }

        [ComImport]
        [Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IApplicationActivationManager
        {
            [PreserveSig]
            Int32 ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] String appUserModelId, [MarshalAs(UnmanagedType.LPWStr)] String arguments, UInt32 options, out UInt32 processId);
        }
    }
}
#endif

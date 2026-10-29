#if !POSTE
namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text.Json;
    using System.Threading;

    // Le même parcours pour les trois IA : préparer le connecteur, lancer l'appli officielle, puis relire la liaison.
    public static class ConnexionPad
    {
        private static readonly String[] Noms = ["Claude", "Codex", "Antigravity"];
        private static readonly Object Verrou = new();
        private static Connexion[] _etats = [new(EtatConnexion.EnCours, "Vérification"), new(EtatConnexion.EnCours, "Vérification"), new(EtatConnexion.EnCours, "Vérification")];
        private static DateTime _lecture = DateTime.MinValue;
        private static Int32 _question;

        public static Connexion[] Lire(Boolean forcer = false)
        {
            lock (Verrou)
            {
                if (!forcer && DateTime.UtcNow - _lecture < TimeSpan.FromSeconds(5))
                {
                    return (Connexion[])_etats.Clone();
                }

                _etats = Enumerable.Range(0, 3).Select(Verifier).ToArray();
                _lecture = DateTime.UtcNow;
                return (Connexion[])_etats.Clone();
            }
        }

        public static void Invalider()
        {
            lock (Verrou) { _lecture = DateTime.MinValue; }
        }

        public static void Basculer(Int32 agent)
        {
            new Thread(() =>
            {
                if (Connecteurs.LireConfiguration(agent).Etat != EtatConnexion.Pret)
                {
                    Traiter(agent, null);
                    return;
                }

                if (Oui($"Disconnect {Noms[agent]} from NexusKeypad?\n\nIts activity and controls will be disconnected from the keypad. Other plugins and settings will be preserved."))
                {
                    var resultat = Connecteurs.Desactiver(agent);
                    Invalider();
                    if (resultat.Etat != EtatConnexion.Activer) { Informer("NexusKeypad could not finish disconnecting this app. Its connection remains pending."); }
                }
            }) { IsBackground = true, Name = "NexusKeypad connexion" }.Start();
        }

        private static Connexion Verifier(Int32 agent)
        {
            var configuration = Connecteurs.LireConfiguration(agent);
            var lancement = Lanceurs.Preparer(agent);
            if (lancement.Etat == EtatLancement.Absent)
            {
                return configuration with { Etat = EtatConnexion.Absent, Detail = "Application officielle introuvable." };
            }

            // Une application installée sans profil a besoin d'un premier lancement avant son connecteur.
            if (configuration.Etat == EtatConnexion.Absent && lancement.Etat is EtatLancement.Pret or EtatLancement.Fermee)
            {
                return configuration with { Etat = EtatConnexion.Activer, Detail = "Ouvrir une première fois l’application officielle pour préparer son profil." };
            }

            if (configuration.Etat is not EtatConnexion.Pret)
            {
                return configuration;
            }

            if (lancement.Etat == EtatLancement.Erreur)
            {
                // Un échec de lecture de l'application ne prouve pas un connecteur endommagé : réessayer au prochain relevé.
                return configuration with { Etat = EtatConnexion.EnCours, Detail = "The app connection could not be checked yet; normal app use remains available." };
            }

            if (lancement.Etat == EtatLancement.Relancer)
            {
                return configuration with { Etat = EtatConnexion.Relancer, Detail = "Relancer l’application depuis le pad pour terminer sa connexion." };
            }

            if (lancement.Etat == EtatLancement.Masquee)
            {
                // Port et propriétaire déjà vérifiés : une fenêtre fermée dans la zone de notification n'est pas
                // une opération en cours. Comme pour une appli arrêtée, le connecteur reste prêt à l'ouvrir.
                // Lanceurs.Connecte reste faux jusqu'à la preuve de la page après l'ouverture.
                return configuration with { Detail = "Connector ready; the app window is closed. Press its character to reopen it." };
            }

            if (agent == 0 && lancement.Etat == EtatLancement.Pret && configuration.PrepareLe is DateTime prepare)
            {
                // Un futur callback n'est pas une opération en cours. La configuration reste disponible au repos ;
                // le diagnostic indique séparément si un véritable événement a été observé depuis sa préparation.
                return AvecObservationClaude(configuration, EvenementClaudeDepuis(prepare));
            }

            return configuration with { Detail = lancement.Etat == EtatLancement.Fermee ? "Connecteur préparé ; le pad lancera l’application avec sa connexion." : "Connecteur et liaison avec l’application vérifiés." };
        }

        public static Connexion AvecObservationClaude(Connexion configuration, Boolean evenementObserve) => configuration.Etat != EtatConnexion.Pret
            ? configuration
            : configuration with
            {
                PremierEvenementObserve = evenementObserve,
                Detail = evenementObserve ? "Connector ready; a real Claude activity event has been observed since setup."
                    : "Connector ready; the first real Claude activity event has not been observed yet. Normal app use remains available.",
            };

        private static Boolean EvenementClaudeDepuis(DateTime prepare)
        {
            var dossier = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nexus", "trinite", "claude");
            try
            {
                if (!Directory.Exists(dossier)) { return false; }
                foreach (var fichier in Directory.EnumerateFiles(dossier, "*.json"))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(fichier));
                        var r = doc.RootElement;
                        if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("at", out var at)
                            && at.ValueKind == JsonValueKind.Number && at.TryGetDouble(out var secondes)
                            && secondes >= new DateTimeOffset(prepare.ToUniversalTime()).ToUnixTimeMilliseconds() / 1000.0
                            && secondes <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                            && r.TryGetProperty("event", out var evenement) && evenement.ValueKind == JsonValueKind.String
                            && EtatsEnDirect.EvenementsClaude.Contains(evenement.GetString())
                            && r.TryGetProperty("state", out var etat) && etat.ValueKind == JsonValueKind.String)
                        {
                            return true;
                        }
                    }
                    catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
                }

                return false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        // Vrai : l'appui a servi à la configuration ; il ne doit jamais ouvrir une ancienne conversation ensuite.
        public static Boolean Traiter(Int32 agent, Instantane instantane, Func<Instantane> actuel = null)
        {
            var connexion = Lire(forcer: true)[agent];
            // Une connexion préparée mais encore partielle ne transforme pas chaque appui en dialogue de relance.
            // L'application reste accessible normalement ; la proposition n'apparaît qu'après l'installation ci-dessous.
            if (connexion.Etat is EtatConnexion.Pret or EtatConnexion.Relancer or EtatConnexion.EnCours) { return false; }
            if (Interlocked.Exchange(ref _question, 1) == 1) { return true; }
            try
            {
                if (connexion.Etat == EtatConnexion.Absent)
                {
                    Informer($"Install the official {Noms[agent]} app first, then press its key again to connect NexusKeypad.");
                    return true;
                }

                if (connexion.Etat is EtatConnexion.Activer or EtatConnexion.Reparer or EtatConnexion.Erreur)
                {
                    var demande = $"Connect {Noms[agent]} to NexusKeypad?\n\nThis installs its connector so the keypad can show its activity and operate the official app locally. Other settings will be preserved. The app will be started if closed.";
                    if (!Oui(demande)) { return true; }
                    if (Connecteurs.LireConfiguration(agent).Etat == EtatConnexion.Absent)
                    {
                        Lanceurs.Demarrer(agent);
                        Informer("Complete the official app’s first launch, then press its key again to connect NexusKeypad.");
                        Invalider();
                        return true;
                    }
                    lock (Verrou) { _etats[agent] = new(EtatConnexion.EnCours, "Activation"); }
                    var resultat = Connecteurs.Activer(agent);
                    Invalider();
                    if (resultat.Etat != EtatConnexion.Pret)
                    {
                        Informer("NexusKeypad setup did not finish. This connection remains pending; press the character again to retry.");
                        return true;
                    }

                    if (Lanceurs.Preparer(agent).Etat == EtatLancement.Fermee)
                    {
                        Lanceurs.Demarrer(agent);
                    }
                }

                var maintenant = Lire(forcer: true)[agent];
                if (maintenant.Etat == EtatConnexion.Relancer)
                {
                    Boolean Occupe() => RelanceOccupee(agent, actuel?.Invoke() ?? instantane);
                    var occupe = Occupe();
                    if (occupe)
                    {
                        Informer($"{Noms[agent]} is in use and will stay open. Its connector is installed; the remaining connection will be checked when the app next starts from the keypad.");
                    }
                    else if (Oui($"Enable the keypad controls for {Noms[agent]}?\n\nThe app needs to reopen with its local keypad connection. Save your work first. NexusKeypad will request a normal quit and reopen; cancellation will be respected."))
                    {
                        // Le dialogue peut rester ouvert : un tour lancé pendant ce temps prime sur l'ancien relevé.
                        if (Occupe())
                        {
                            Informer("A conversation or microphone has become active. Reopening this app remains pending.");
                        }
                        else if (!Lanceurs.Relancer(agent))
                        {
                            Lanceurs.Rappeler(agent);
                            Informer($"{Noms[agent]} kept running. Its connector is installed and basic keypad controls remain available. The remaining connection will be checked when the app next starts from the keypad.");
                        }
                    }
                }

                Invalider();
                return true;
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "connexion NexusKeypad");
                Informer("NexusKeypad could not finish setup. This connection remains pending.");
                Invalider();
                return true;
            }
            finally { Volatile.Write(ref _question, 0); }
        }

        internal static Boolean RelanceOccupee(Int32 agent, Instantane instantane) => instantane is null
            || instantane.DicteeIa == agent || instantane.Conversations[agent].Any(c => c.Etat is Etat.Travaille or Etat.Autorisation or Etat.Attend);

        private static Boolean Oui(String texte) => MessageBoxW(IntPtr.Zero, texte, "NexusKeypad", 0x4 | 0x20 | 0x100 | 0x40000) == 6;
        private static void Informer(String texte) => MessageBoxW(IntPtr.Zero, texte, "NexusKeypad", 0x40 | 0x40000);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern Int32 MessageBoxW(IntPtr fenetre, String texte, String titre, UInt32 type);
    }
}
#endif

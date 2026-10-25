namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.IO;
    using System.IO.Pipes;
    using System.Threading;
    using System.Threading.Tasks;

    // Pilotage local du pad pour les essais (outils/pad.py) : un tube nommé réservé à l'utilisateur de la session, une
    // commande par connexion, une ligne de réponse.
    //   capture       → image PNG des 9 touches telles qu'envoyées au pad ; répond son chemin
    //   appui <1-18>  → appui simulé sur la touche à cette place du pad (même chemin qu'un vrai appui) ; répond son issue
    //   etat          → résumé de la dernière lecture (conversations, états, quotas)
    //   releve        → relevé du quota d'Antigravity sur sa page Settings › Models, sans attendre l'inactivité
    //   update        → (édition complète) mise à jour vers la dernière release ; l'appli Nexus la déclenche ainsi
    //   etats-en-direct → la question de l'action « Live states » (poser ou retirer les hooks), comme un appui sur elle
    public sealed class Pilote : IDisposable
    {
        public const String Nom = "nexus-trinite-pad";

        private static readonly String Captures = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "", "NexusKeypad", "captures");

        private readonly Moteur _moteur;
        private readonly CancellationTokenSource _fin = new();
        private Boolean _tubeOccupe;

        public Pilote(Moteur moteur) => this._moteur = moteur;

#if MARKETPLACE || POSTE || COMPLETE || STREAMDECK
        // Éditions du Logi Marketplace (AI Coding Agents, PC Dashboard, AI Coding Agents + PC) et Stream Deck (le tube est
        // celui du plugin Logi) : pas d'outils d'essai, donc pas de tube ouvert.
        public void Demarrer()
        {
        }
#else
        public void Demarrer() => Task.Run(this.Ecouter);
#endif

        private async Task Ecouter()
        {
            while (!this._fin.IsCancellationRequested)
            {
                try
                {
                    await using var tube = new NamedPipeServerStream(Nom, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    this._tubeOccupe = false;
                    await tube.WaitForConnectionAsync(this._fin.Token);
                    using var lecteur = new StreamReader(tube, leaveOpen: true);
                    await using var ecrivain = new StreamWriter(tube, leaveOpen: true) { AutoFlush = true };
                    using var delai = CancellationTokenSource.CreateLinkedTokenSource(this._fin.Token);
                    delai.CancelAfter(TimeSpan.FromSeconds(5)); // un client muet ne bloque pas le pilote
                    String commande;
                    try
                    {
                        commande = (await lecteur.ReadLineAsync(delai.Token) ?? "").Trim();
                    }
                    catch (OperationCanceledException) when (!this._fin.IsCancellationRequested)
                    {
                        continue;
                    }

                    await ecrivain.WriteLineAsync(this.Executer(commande));
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException) when (!this._fin.IsCancellationRequested)
                {
                    // Tube déjà tenu (autre instance du plugin pendant un rechargement) : réessai discret.
                    if (!this._tubeOccupe)
                    {
                        this._tubeOccupe = true;
                        PluginLog.Warning("pilote du pad : tube déjà tenu, nouvel essai toutes les 2 s");
                    }

                    await Task.Delay(2000);
                }
                catch (Exception e)
                {
                    PluginLog.Error(e, "pilote du pad");
                    await Task.Delay(500);
                }
            }
        }

        private String Executer(String commande)
        {
            var mots = commande.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (mots.Length > 0 ? mots[0] : "")
            {
                case "capture":
                    Directory.CreateDirectory(Captures);
                    var chemin = Path.Combine(Captures, $"pad-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
                    File.WriteAllBytes(chemin, this._moteur.Planche(mots.Length == 2 && mots[1] == "2" ? 2 : 1));
                    return chemin;
                case "appui" when mots.Length == 2 && Int32.TryParse(mots[1], out var touche) && touche is >= 1 and <= Moteur.Touches:
                    var issue = this._moteur.Appuyer(Moteur.Disposition[touche - 1]); // place du pad ; l'issue réelle du geste
                    return issue.Wait(TimeSpan.FromSeconds(20)) ? issue.Result : "en cours depuis plus de 20 s";
                case "etat":
                    return this._moteur.Resume ?? "";
                case "releve":
                    return this._moteur.ReleverAntigravity();
#if NEXUS
                case "update":
                    return MiseAJour.Lancer(this._moteur, demande: true) ? "started" : "already running";
#endif
#if !POSTE
                case "etats-en-direct":
                    EtatsEnDirect.Basculer();
                    return EtatsEnDirect.Poses() ? "question posée : retirer les états en direct" : "question posée : poser les états en direct";
#endif
                default:
                    return "commandes : capture | appui <1-18> | etat | releve | etats-en-direct";
            }
        }

        public void Dispose()
        {
            this._fin.Cancel();
            this._fin.Dispose();
        }
    }
}

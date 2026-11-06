namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.Linq;

    // Autorisation : attend l'accord de l'utilisateur pour une action (commande, fichier) ; Attend : une question.
    // Erreur : le tour s'est arrêté sur une erreur ; Limite : limite d'usage atteinte, l'IA ne peut plus travailler.
    public enum Etat { Ferme, Repos, Travaille, Attend, Fini, Autorisation, Erreur, Limite }

    // Un seul à la fois : un geste du pad, ou un relevé qui ouvre une interface (page Models d'Antigravity). Une interface
    // ouverte pendant un geste lui prenait le clavier (menu Utilisation de Claude pendant un geste, 23/10).
    public static class Interface
    {
        public static readonly Object Verrou = new();
    }

    // Une conversation d'une IA : Id = ce qui sert à l'ouvrir (Claude local_…, Codex conversationId, Antigravity cascade).
    // Vu : l'utilisateur l'a regardée depuis son dernier changement d'état (le clignotement s'arrête).
    // Relais : ouverte par NexusRelay, pas par l'utilisateur ; elle ne le prévient jamais.
    // Source : IA qui a demandé le travail (0 Claude, 1 Codex, 2 Antigravity) ; -1 si non précisée.
    public sealed record Conversation(Int32 Agent, String Id, String Titre, Etat Etat, DateTime Depuis, DateTime Activite, Boolean Vu, Boolean Relais = false, Int32 Source = -1)
    {
        // Clignote pour l'utilisateur : une question, une autorisation, une erreur ou une fin qu'il n'a pas encore
        // regardée, hors relais. La limite d'usage ne clignote pas : il n'y peut rien.
        public Boolean Alerte => !this.Relais && !this.Vu && this.Etat is Etat.Attend or Etat.Fini or Etat.Autorisation or Etat.Erreur;

        // Elle attend l'utilisateur (question ou autorisation).
        public Boolean Appelle => this.Etat is Etat.Attend or Etat.Autorisation;
    }

    // Quota d'une IA : part utilisée (0 à 1) de la fenêtre de 5 h et de la semaine ; null quand elle n'a pas encore
    // été lue. Groupes (Antigravity) : le détail par groupe de modèles et par fenêtre, pour recalculer la jauge quand une
    // fenêtre repart à zéro.
    public sealed record Quota(Double? Cinq, Double? Semaine)
    {
        public System.Collections.Generic.IReadOnlyList<GroupeQuota> Groupes { get; init; } = [];

        // Dernière famille réellement sélectionnée dans l'interface ; null si aucune sélection n'est prouvée.
        public String GroupeSelectionne { get; init; }
    }

    // Une fenêtre d'un groupe de modèles : 5 h (Cinq) ou semaine, part utilisée, heure (UTC) de sa remise à zéro si l'IA
    // la donne.
    public sealed record GroupeQuota(Boolean Cinq, Double Utilise, DateTime? Fin, Boolean Principal = false, String Groupe = null);

    // Conversations : la plus pressante d'abord. Ouvertes : nombre de conversations actives de chaque IA.
    // Devant : l'IA dont l'appli est au premier plan (celle que Capture, Photo et Dictée visent), -1 si aucune.
    // Dictee : une dictée lancée par le pad est en cours (Claude, Codex ou Antigravity) : la touche passe en rouge.
    public sealed record Instantane(Conversation[][] Conversations, Int32[] Ouvertes, Quota[] Quotas, Boolean[] Ouverte, Boolean Eteint, Photo Photo, Int32 Devant)
    {
        public Boolean Dictee { get; init; }

        // L'IA où tourne la dictée lancée par le pad (0 à 2), -1 sinon : sa tête écoute.
        public Int32 DicteeIa { get; init; } = -1;

#if !POSTE
        // Null dans les aperçus historiques ; une liaison réellement vérifiée pour chaque IA sur le pad.
        public Connexion[] Connexions { get; init; }
#endif

        public static readonly Instantane Vide = new([[], [], []], [0, 0, 0], [null, null, null], [false, false, false], false, null, -1);

        public Conversation Premiere(Int32 agent) => this.Conversations[agent].Length > 0 ? this.Conversations[agent][0] : null;

        // Conversation qui appelle l'utilisateur sur la touche de l'IA (anneau, cadence vive) : aucune appli fermée. Les
        // fiches de session restent sur disque après la fermeture : une fin non regardée la veille entourait Claude
        // d'un anneau au démarrage du PC, appli pas encore lancée (06/11).
        public Conversation Alerte(Int32 agent) => this.Ouverte[agent] && this.Premiere(agent) is { Alerte: true } c ? c : null;

#if !POSTE
        // Couples (source, ouvrier) où un ouvrier travaille pour une source par le relais.
        public System.Collections.Generic.IReadOnlyList<(Int32 Source, Int32 Ouvrier)> LiaisonsRelais()
        {
            var paires = new System.Collections.Generic.List<(Int32, Int32)>();
            for (var w = 0; w < 3; w++)
            {
                foreach (var c in this.Conversations[w])
                {
                    if (c is { Relais: true, Etat: Etat.Travaille })
                    {
                        var s = c.Source >= 0 && c.Source < 3 && c.Source != w ? c.Source : (w == 0 ? 1 : 0);
                        if (!paires.Contains((s, w)))
                        {
                            paires.Add((s, w));
                        }
                    }
                }
            }
            return paires;
        }

        // Pour compatibilité : Codex ou Antigravity travaille pour Claude par le relais.
        public Boolean PourClaude(Int32 agent) => this.Conversations[agent].Any(c => c is { Relais: true, Etat: Etat.Travaille } && (c.Source == 0 || c.Source < 0));
#endif

        // État d'ensemble d'une IA : sa conversation la plus pressante.
        public Etat EtatIA(Int32 agent) =>
            !this.Ouverte[agent] ? Etat.Ferme : this.Premiere(agent)?.Etat ?? Etat.Repos;
    }

    // Dernière photo du téléphone : chemin sur le téléphone, copie locale, miniature prête pour la touche.
    public sealed record Photo(String SurTelephone, String Fichier, BitmapImage Miniature);
}

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
    // Relais : ouverte par Nexus Relay pour Claude, pas par l'utilisateur ; elle ne le prévient jamais.
    public sealed record Conversation(Int32 Agent, String Id, String Titre, Etat Etat, DateTime Depuis, DateTime Activite, Boolean Vu, Boolean Relais = false)
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
    }

    // Une fenêtre d'un groupe de modèles : 5 h (Cinq) ou semaine, part utilisée, heure (UTC) de sa remise à zéro si l'IA
    // la donne.
    public sealed record GroupeQuota(Boolean Cinq, Double Utilise, DateTime? Fin);

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

#if !POSTE
        // Codex ou Antigravity travaille pour Claude par le relais (au moins un de ses chats du relais tourne).
        public Boolean PourClaude(Int32 agent) => this.Conversations[agent].Any(c => c is { Relais: true, Etat: Etat.Travaille });
#endif

        // État d'ensemble d'une IA : sa conversation la plus pressante.
        public Etat EtatIA(Int32 agent) =>
            !this.Ouverte[agent] ? Etat.Ferme : this.Premiere(agent)?.Etat ?? Etat.Repos;
    }

    // Dernière photo du téléphone : chemin sur le téléphone, copie locale, miniature prête pour la touche.
    public sealed record Photo(String SurTelephone, String Fichier, BitmapImage Miniature);
}

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
    // FinCinq, FinSemaine : heure (UTC) de remise à zéro de chaque fenêtre quand l'IA la donne, pour le rythme.
    public sealed record Quota(Double? Cinq, Double? Semaine)
    {
        public System.Collections.Generic.IReadOnlyList<GroupeQuota> Groupes { get; init; } = [];

        public DateTime? FinCinq { get; init; }

        public DateTime? FinSemaine { get; init; }

        // Comptes (Codex LB) : chaque compte du cumul, avec sa propre fenêtre ; le cumul n'a pas de fenêtre commune.
        public System.Collections.Generic.IReadOnlyList<CompteQuota> Comptes { get; init; } = [];

        // Charge d'une fenêtre (5 h ou semaine) : part utilisée projetée à sa remise à zéro au rythme actuel (1 : vide pile
        // à la remise à zéro) ; null, pas jugeable. Par groupe (Antigravity) : le groupe de modèles le plus chargé ; par
        // compte (Codex LB) : le pool.
        public Double? Charge(Boolean cinq, DateTime maintenant) =>
            this.Comptes.Any(c => c.Cinq == cinq) ? Rythme.ChargePool(this.Comptes.Where(c => c.Cinq == cinq), cinq, maintenant)
            : this.Groupes.Count > 0 ? this.Groupes.Where(g => g.Cinq == cinq).Select(g => Rythme.Projection(g.Utilise, g.Fin, cinq, maintenant)).Max()
            : Rythme.Projection(cinq ? this.Cinq : this.Semaine, cinq ? this.FinCinq : this.FinSemaine, cinq, maintenant);

        // Allure de l'IA, signe à droite du nombre de conversations : celle de sa fenêtre la plus chargée ; null quand
        // aucune ne se juge (aucun signe).
        public Allure? Niveau(DateTime maintenant) =>
            new[] { this.Charge(true, maintenant), this.Charge(false, maintenant) }.Max() is Double charge ? Rythme.Niveau(charge) : null;

        public Boolean Rapide(Boolean cinq, DateTime maintenant) => this.Charge(cinq, maintenant) > Rythme.Plein + 1e-9;
    }

    // Allure du quota, par charge projetée à la remise à zéro : Large (< 80 %, pouce vert), Juste (80 à 100 %, coche
    // bleue), Tendu (100 à 125 % : vide un peu avant, sablier orange), Critique (> 125 %, triangle rouge).
    public enum Allure { Large, Juste, Tendu, Critique }

    // Rythme de consommation d'une fenêtre de quota : la part utilisée comparée à la part de la fenêtre déjà écoulée.
    // Projection à la remise à zéro = utilisé ÷ écoulé ; au-delà de 100 %, le quota sera vide avant (seuil : Plein).
    // Aucun signal plutôt qu'une fausse alerte : sans heure de remise à zéro, fenêtre déjà remise à zéro ou incohérente,
    // quota déjà plein (la tête le montre), ou fenêtre trop jeune (moins de Jeune écoulé : 30 min sur 5 h, 16 h 48 sur
    // une semaine), où quelques messages suffisent à fausser la projection.
    public static class Rythme
    {
        public const Double Jeune = 0.10;
        public const Double Plein = 1.0;
        public const Double Large = 0.8;  // en dessous : allure large
        public const Double Tendu = 1.25; // au-dessus : critique

        // Tolérance : pile au seuil ne change pas de niveau par un arrondi flottant.
        public static Allure Niveau(Double charge) =>
            charge < Large - 1e-9 ? Allure.Large : charge <= Plein + 1e-9 ? Allure.Juste : charge <= Tendu + 1e-9 ? Allure.Tendu : Allure.Critique;

        public static TimeSpan Duree(Boolean cinq) => cinq ? TimeSpan.FromHours(5) : TimeSpan.FromDays(7);

        // Part utilisée projetée à la remise à zéro ; null quand le rythme ne se juge pas encore.
        public static Double? Projection(Double? utilise, DateTime? fin, Boolean cinq, DateTime maintenant)
        {
            if (utilise is not Double u || fin is not DateTime f || u >= 0.995)
            {
                return null;
            }

            var duree = Duree(cinq);
            var reste = f - maintenant;
            if (reste <= TimeSpan.Zero || reste > duree)
            {
                return null;
            }

            var ecoule = 1 - (reste / duree);
            return ecoule < Jeune ? null : u / ecoule;
        }

        // Tolérance : pile au rythme (20 % en 1 h sur 5 h) ne doit pas passer en alerte par un arrondi flottant.
        public static Boolean Trop(Double? utilise, DateTime? fin, Boolean cinq, DateTime maintenant) =>
            Projection(utilise, fin, cinq, maintenant) > Plein + 1e-9;

        // Pool de comptes aux fenêtres décalées (Codex LB) : débit de chaque compte = ses crédits utilisés ÷ temps écoulé
        // dans sa fenêtre (comptes trop jeunes, sans heure ou à l'heure incohérente : aucun débit connu) ; alerte quand le
        // solde du pool, à la somme de ces débits, est vide avant la première remise à zéro à venir d'un compte. Pour un
        // seul compte, c'est la règle de Trop. Un compte remis à zéro depuis la lecture compte plein et sans débit. Aucun
        // signal si un compte a une part inconnue (Codex LB masque une fenêtre expirée : sa réserve est inconnue) ou si le
        // pool est déjà plein (moins de 0,5 % de sa capacité restant).
        // Charge du pool : crédits utilisés plus ceux que les débits consommeront d'ici la première remise à zéro, sur sa
        // capacité (un seul compte : la Projection).
        public static Boolean Pool(System.Collections.Generic.IEnumerable<CompteQuota> comptes, Boolean cinq, DateTime maintenant) =>
            ChargePool(comptes, cinq, maintenant) > Plein + 1e-9;

        public static Double? ChargePool(System.Collections.Generic.IEnumerable<CompteQuota> comptes, Boolean cinq, DateTime maintenant)
        {
            var duree = Duree(cinq);
            Double solde = 0, debit = 0, capacite = 0; // crédits, crédits par heure
            var mesure = false; // au moins un compte assez avancé dans sa fenêtre pour donner son débit (même nul)
            DateTime? prochaine = null;
            foreach (var c in comptes)
            {
                if (c.Utilise is not Double part)
                {
                    return null;
                }

                capacite += c.Capacite;
                var utilise = Math.Clamp(part, 0, 1);
                var reste = c.Fin - maintenant;
                if (reste is not TimeSpan r || r > duree)
                {
                    solde += c.Capacite * (1 - utilise);
                    continue;
                }

                if (r <= TimeSpan.Zero)
                {
                    solde += c.Capacite;
                    continue;
                }

                solde += c.Capacite * (1 - utilise);
                prochaine = prochaine is DateTime p && p < c.Fin ? p : c.Fin;
                var ecoule = 1 - (r / duree);
                if (ecoule >= Jeune)
                {
                    mesure = true;
                    debit += c.Capacite * utilise / (ecoule * duree.TotalHours);
                }
            }

            return mesure && solde / capacite > 0.005 + 1e-9 && prochaine is DateTime premiere
                ? (capacite - solde + (debit * (premiere - maintenant).TotalHours)) / capacite
                : null;
        }
    }

    // Un compte d'un pool (Codex LB) pour une fenêtre : capacité en crédits, part utilisée (null : inconnue), heure (UTC)
    // de sa remise à zéro.
    public sealed record CompteQuota(Boolean Cinq, Double Capacite, Double? Utilise, DateTime? Fin);

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

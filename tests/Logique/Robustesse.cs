// Robustesse des lecteurs de fichiers écrits par d'autres applis (journaux de Codex, page de quota d'Antigravity) :
// tests de propriétés FsCheck, sur des entrées quelconques et sur des lignes réelles abîmées au hasard. Un plantage y
// coupait la lecture des états du pad ; une valeur hors de 0 à 1 faussait une jauge.
using FsCheck;
using FsCheck.Fluent;
using Loupedeck.TrinitePlugin;

public static class Robustesse
{
    private static readonly Config Essais = Config.QuickThrowOnFailure.WithMaxTest(2000).WithQuietOnSuccess(true);

    public static void Tout(Action<Boolean, String> vrai, String[] modeles, String page)
    {
        var maintenant = new DateTime(2026, 10, 27, 21, 0, 0, DateTimeKind.Utc);

        // Une ligne réelle abîmée : un caractère quelconque inséré, ou un morceau retiré, à une place quelconque.
        String Abimer(String ligne, Int32 place, Int32 longueur, Char c, Boolean retirer)
        {
            var i = place % (ligne.Length + 1);
            return retirer ? ligne.Remove(i, Math.Min(longueur % 8, ligne.Length - i)) : ligne.Insert(i, c.ToString());
        }

        Essayer(vrai, "journal Codex quelconque : jamais d'exception", () =>
            Prop.ForAll<String[]>(lignes =>
            {
                Observateur.EtatJournalCodex((lignes ?? []).Select(l => l ?? ""), false, maintenant);
                Observateur.EtatJournalCodex((lignes ?? []).Select(l => l ?? ""), true, maintenant);
            }));

        Essayer(vrai, "journal Codex réel abîmé : jamais d'exception", () =>
            Prop.ForAll<NonNegativeInt[], Char, Boolean>((n, c, retirer) =>
            {
                var lignes = modeles.ToArray();
                var k = Nombre(n, 0) % lignes.Length;
                // Deux fois sur trois, un caractère qui casse un JSON (« \ », « " », « { »).
                var insere = (Nombre(n, 3) % 3) switch { 0 => '\\', 1 => '"', _ => Nombre(n, 4) % 2 == 0 ? '{' : c };
                lignes[k] = Abimer(lignes[k], Nombre(n, 1), Nombre(n, 2), insere, retirer);
                Observateur.EtatJournalCodex(lignes, false, maintenant);
            }));

        // Cas trouvé à la main : un échappement que JSON refuse dans le dernier message d'un tour.
        Essayer(vrai, "dernier message au mauvais échappement : jamais d'exception", () =>
            Prop.ForAll<Char>(c =>
            {
                var ligne = $$$"""{"timestamp":"2026-10-27T20:51:12.000Z","type":"event_msg","payload":{"type":"task_complete","last_agent_message":"Re\{{{c}}}lecture ?"}}""";
                Observateur.EtatJournalCodex([ligne], false, maintenant);
            }));

        Essayer(vrai, "page de quota d'Antigravity quelconque ou abîmée : jamais d'exception, parts de 0 à 1", () =>
            Prop.ForAll<String, NonNegativeInt[], Char>((texte, n, c) =>
            {
                // Un caractère sur deux inséré est un chiffre (ASCII, arabe-indien ou pleine chasse : \d les accepte tous) :
                // les pourcentages sont la partie sensible de la page.
                var zero = (Nombre(n, 7) % 3) switch { 0 => '0', 1 => '\u0660', _ => '\uFF10' };
                var insere = Nombre(n, 3) % 2 == 0 ? (Char)(zero + (Nombre(n, 4) % 10)) : c;
                // Une fois sur deux, juste avant un « % » de la page (k-ième, 0 à 3 caractères plus tôt).
                var signes = Enumerable.Range(0, page.Length).Where(i => page[i] == '%').ToArray();
                var place = Nombre(n, 5) % 2 == 0 && signes.Length > 0
                    ? Math.Max(0, signes[Nombre(n, 0) % signes.Length] - (Nombre(n, 6) % 4)) : Nombre(n, 0);
                foreach (var entree in new[] { texte ?? "", Abimer(page, place, Nombre(n, 1), insere, Nombre(n, 2) % 3 == 0) })
                {
                    var q = QuotaAntigravity.Analyser(entree);
                    var parts = new[] { q?.Cinq, q?.Semaine }.Concat(q?.Groupes.Select(g => (Double?)g.Utilise) ?? []);
                    if (parts.Any(p => p is < 0 or > 1 || p is Double d && Double.IsNaN(d)))
                    {
                        throw new InvalidOperationException($"part hors de 0 à 1 pour « {entree} »");
                    }
                }
            }));

        Essayer(vrai, "historique de quota Claude quelconque : jamais d'exception, parts de 0 à 1", () =>
            Prop.ForAll<String>(texte =>
            {
                var q = Observateur.QuotaHistoriqueClaude(texte, maintenant);
                if (q?.Cinq is Double c && (c < 0 || c > 1)) throw new InvalidOperationException($"cinq {c}");
                if (q?.Semaine is Double s && (s < 0 || s > 1)) throw new InvalidOperationException($"semaine {s}");
            }));

        Essayer(vrai, "historique de quota Claude abîmé : jamais d'exception", () =>
            Prop.ForAll<NonNegativeInt[], Char, Boolean>((n, c, retirer) =>
            {
                var modele = """{"version":2,"samples":[{"t":1793221096102,"org":"e35395a4","u":{"fh":9,"sd":69}}]}""";
                var modifie = Abimer(modele, Nombre(n, 0), Nombre(n, 1), c, retirer);
                Observateur.QuotaHistoriqueClaude(modifie, maintenant);
            }));

        Essayer(vrai, "conversation Claude active : jamais d'exception sur entrées arbitraires", () =>
            Prop.ForAll<Tuple<String, String, String, String>, DateTime, Int32>((textes, date, etatInt) =>
            {
                var (titre, cwd, affiche, cwdActif) = textes;
                var etat = (Etat)(Math.Abs(etatInt) % 6);
                var conv = new Conversation(0, "id", titre ?? "", etat, date, date, true);
                Observateur.ConversationClaudeActive(conv, cwd, affiche, cwdActif, maintenant);
            }));
    }

    // Délais de remise à zéro, d'une à quatre composantes aux bornes (jusqu'au-delà d'Int64) : jamais d'exception ; un
    // délai jusqu'à 8 jours donne sa remise à zéro exacte, au-delà (une composante ou la somme) aucune ; la part lue
    // reste toujours (33 % restants = 0,67).
    public static void Delais(Action<Boolean, String> vrai)
    {
        (String Nom, Int64 Secondes)[] unites = [("days", 86400), ("hour", 3600), ("minutes", 60), ("second", 1)];
        Essayer(vrai, "délai de remise à zéro quelconque : exact jusqu'à 8 jours, ignoré au-delà, part gardée", () =>
            Prop.ForAll<NonNegativeInt[], Boolean, Boolean>((tirage, enorme, tresEnorme) =>
            {
                var morceaux = (tirage ?? []).Take(4).Select((t, k) =>
                {
                    var n = (System.Numerics.BigInteger)t.Get * (k == 0 && enorme ? 1000 : 1) + (k == 0 && tresEnorme ? Int64.MaxValue : 0);
                    return (Nombre: n, Unite: unites[(t.Get + k) % unites.Length]);
                }).ToList();
                if (morceaux.Count == 0)
                {
                    return;
                }

                var texte = $"Gemini Models\nFive Hour Limit Remaining\nYou have used some, it will fully refresh in {String.Join(", ", morceaux.Select(m => $"{m.Nombre} {m.Unite.Nom}"))}.\n33%";
                var avant = DateTime.UtcNow;
                var q = QuotaAntigravity.Analyser(texte);
                var total = morceaux.Aggregate(System.Numerics.BigInteger.Zero, (a, m) => a + (m.Nombre * m.Unite.Secondes));
                var g = q?.Groupes.SingleOrDefault() ?? throw new InvalidOperationException("part perdue");
                if (Math.Abs(g.Utilise - 0.67) > 1e-9)
                {
                    throw new InvalidOperationException($"part {g.Utilise}");
                }

                if (total > 8 * 86400 ? g.Fin is not null : g.Fin is not DateTime f || Math.Abs((f - avant).TotalSeconds - (Double)total) > 5)
                {
                    throw new InvalidOperationException($"remise à zéro {g.Fin:o} pour {total} s");
                }
            }));
    }

    // n-ième nombre tiré (0 s'il en manque).
    private static Int32 Nombre(NonNegativeInt[] n, Int32 i) => n is not null && i < n.Length ? n[i].Get : 0;

    private static void Essayer(Action<Boolean, String> vrai, String regle, Func<Property> propriete)
    {
        try
        {
            Check.One(Essais, propriete());
            vrai(true, regle);
        }
        catch (Exception e)
        {
            vrai(false, $"{regle} : {e.Message.Trim()}"); // diagnostic entier : contre-exemple réduit et graine
        }
    }
}

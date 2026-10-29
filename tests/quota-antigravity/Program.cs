using Loupedeck.TrinitePlugin;

var controles = 0;
void Vrai(bool condition, string message) { controles++; if (!condition) throw new InvalidOperationException(message); }
bool Proche(double? valeur, double attendu) => valeur is double v && Math.Abs(v - attendu) < 1e-9;
var maintenant = DateTime.UtcNow;
var actuel = QuotaAntigravity.Analyser("""
Usage
Gemini Models
Weekly limit
93% left
Resets in 6d 6h
5h limit
100% left
Claude and GPT models
Weekly limit
100% left
5h limit
100% left
Gemini 4 Argon
Weekly limit
100% left
""");
Vrai(Proche(actuel?.Cinq, 0) && Proche(actuel?.Semaine, .07), "Panneau visible actuel : 0 % utilisés en 5 h et 7 % semaine.");
Vrai(actuel?.Groupes.Count == 5, "Les cinq fenêtres des trois groupes restent séparées.");
Vrai(actuel.Groupes.Count(g => g.Cinq) == 2, "Le groupe Argon hebdomadaire ne fabrique pas de fenêtre 5 h.");
Vrai(actuel.Groupes[0].Fin is DateTime fin && fin >= maintenant.AddDays(6).AddHours(6) && fin < maintenant.AddDays(6).AddHours(6).AddSeconds(2), "Délai après la valeur conservé sur sa fenêtre.");
Vrai(actuel.Groupes.Skip(1).All(g => g.Fin is null), "Aucun délai propagé aux fenêtres ou groupes suivants.");

foreach (var ordre in new[] { "Resets in 42m\n75% left", "75% left\nResets in 42m" })
{
    var q = QuotaAntigravity.Analyser("Gemini Models\n5h limit\n" + ordre);
    Vrai(Proche(q?.Cinq, .25) && q?.Semaine is null, "Valeur et fenêtre courtes préservées dans les deux ordres.");
    Vrai(q.Groupes[0].Fin is DateTime finCourte && finCourte > DateTime.UtcNow.AddMinutes(41) && finCourte <= DateTime.UtcNow.AddMinutes(42), "Délai court avant ou après valeur.");
}
var ancien = QuotaAntigravity.Analyser("Gemini Models\nWeekly Limit Remaining\nYou have used some of your weekly limit, it will fully refresh in 6 days, 9 hours.\n33%\nFive Hour Limit Remaining\n3%");
Vrai(Proche(ancien?.Cinq, .97) && Proche(ancien?.Semaine, .67), "Ancien format et valeurs sans suffixe left conservés.");
Vrai(ancien.Groupes[0].Fin is not null && ancien.Groupes[1].Fin is null, "Délai ancien limité à sa fenêtre.");

var memesLignes = QuotaAntigravity.Analyser("Gemini Models\nWeekly limit 93% left\n5h limit 100% left");
Vrai(Proche(memesLignes?.Cinq, 0) && Proche(memesLignes?.Semaine, .07), "Libellé et valeur sur une même ligne visuelle.");
var fragments = QuotaAntigravity.Analyser("Gemini\nModels\nWeekly\nlimit\n93\n% left\n5h limit\n100%\nleft");
Vrai(Proche(fragments?.Cinq, 0) && Proche(fragments?.Semaine, .07), "Nœuds DOM séparés des libellés et pourcentages.");
var separes = QuotaAntigravity.Analyser("Gemini Models\nWeekly limit\nResets in 3d\nClaude and GPT models\n5h limit\n80% left");
Vrai(separes?.Semaine is null && Proche(separes?.Cinq, .2) && separes.Groupes[0].Fin is null, "Groupe sans valeur ne récupère ni valeur ni délai du groupe suivant.");
var fenetres = QuotaAntigravity.Analyser("Gemini Models\nWeekly limit\nResets in 3d\n5h limit\n80% left");
Vrai(fenetres?.Semaine is null && fenetres?.Groupes[0].Fin is null, "Fenêtre sans valeur ne prend pas celle de la suivante.");
Vrai(QuotaAntigravity.Analyser("Usage\nWeekly limit\n93% left") is null, "Un pourcentage sans groupe identifié ne crée pas de quota.");
Vrai(QuotaAntigravity.Analyser("Gemini Models\nWeekly limit\n200% left") is null, "Pourcentage supérieur à cent ignoré.");
Vrai(QuotaAntigravity.Analyser("Gemini Models\nWeekly limit\n20% left\n30% left") is null, "Plusieurs valeurs pour une fenêtre restent ambiguës.");
Vrai(QuotaAntigravity.Analyser("Gemini Models\nWeekly limit\nAutre panneau\n93% left") is null, "Texte étranger coupe l'association fenêtre-valeur.");
var argon = QuotaAntigravity.Analyser("Gemini4Argon\nWeekly limit\n50% left");
Vrai(Proche(argon?.Semaine, .5) && argon?.Cinq is null, "Nom Argon compact accepté sans fausse fenêtre 5 h.");
foreach (var partArgon in new[] { 100, 50 })
{
    var inverse = QuotaAntigravity.Analyser($"Gemini Models\nWeekly limit\n93% left\n5h limit\n100% left\nClaude and GPT models\nWeekly limit\n100% left\n5h limit\n100% left\nGemini 4 Argon\n{partArgon}\n% left\nWeekly limit");
    Vrai(inverse?.Groupes.Count == 5 && Proche(inverse.Groupes[4].Utilise, 1 - partArgon / 100d), "Argon : valeur avant libellé conservée, 100 % ou 50 % restants.");
    Vrai(inverse.Groupes[4].Cinq == false && inverse.Groupes[4].Fin is null && Proche(inverse.Cinq, 0), "Bloc Argon inversé reste hebdomadaire, sans délai ou quota 5 h emprunté.");
}
Vrai(QuotaAntigravity.Analyser("Gemini Models\n50% left\nGemini 4 Argon\nWeekly limit") is null, "Argon ne récupère pas une valeur d'un autre groupe.");
Vrai(QuotaAntigravity.Analyser("Gemini 4 Argon\n50% left\nClaude and GPT models\nWeekly limit") is null, "Une valeur Argon sans libellé ne fuit pas vers le groupe suivant.");
Vrai(QuotaAntigravity.Analyser("Gemini 4 Argon\n50% left\n5h limit") is null, "Valeur préalable Argon jamais attribuée à une fenêtre 5 h.");
Vrai(QuotaAntigravity.Analyser("Gemini 4 Argon\n50% left\n100% left\nWeekly limit") is null, "Deux valeurs Argon préalables restent ambiguës.");
foreach (var delaiFragmente in new[] { "Resets in\n6d 6h", "Resets in 6d\n6h", "Resets\nin\n6d\n6h" })
{
    var q = QuotaAntigravity.Analyser("Gemini Models\nWeekly limit\n93% left\n" + delaiFragmente);
    Vrai(Proche(q?.Semaine, .07) && q.Groupes[0].Fin is DateTime remise && remise > DateTime.UtcNow.AddHours(149.99) && remise <= DateTime.UtcNow.AddHours(150), "Délai fragmenté recomposé intégralement dans sa fenêtre.");
}
var coupeDelai = QuotaAntigravity.Analyser("Gemini Models\nWeekly limit\n93% left\nResets in 6d\n5h limit\n50% left\n6h");
Vrai(coupeDelai.Groupes[0].Fin is DateTime finSixJours && finSixJours > DateTime.UtcNow.AddHours(143.99) && finSixJours <= DateTime.UtcNow.AddHours(144)
    && coupeDelai.Groupes[1].Fin is null, "Fragments du délai ne traversent jamais une fenêtre.");
var coupeGroupeDelai = QuotaAntigravity.Analyser("Gemini Models\nWeekly limit\n93% left\nResets in\nClaude and GPT models\n5h limit\n50% left\n6h");
Vrai(coupeGroupeDelai.Groupes.Count == 2 && coupeGroupeDelai.Groupes.All(g => g.Fin is null), "Fragments du délai ne traversent jamais un groupe.");

foreach (var (texte, attendu) in new[] { ("Resets in 6d 6h", new TimeSpan(6, 6, 0, 0)), ("Resets in 42m", TimeSpan.FromMinutes(42)),
    ("Resets in 1h42m", TimeSpan.FromMinutes(102)), ("Resets in 10s", TimeSpan.FromSeconds(10)),
    ("it will fully refresh in 4 days, 23 hours.", new TimeSpan(4,23,0,0)), ("Resets in 0m", TimeSpan.Zero) })
    Vrai(QuotaAntigravity.Delai(texte) == attendu, "Délai ancien ou compact : " + texte);
foreach (var texte in new[] { "Resets in 999999999999999999999d", "Resets in 8d 1s", "Resets in 6daylight", "Resets in 2.5 hours", "Weekly limit" })
    Vrai(QuotaAntigravity.Delai(texte) is null, "Délai invalide ignoré : " + texte);

Console.WriteLine($"Quota Antigravity : {controles} contrôles purs réussis, aucun profil ou écran lu.");

// Usage : dotnet run --project verifier   →   code 0 si toutes les règles tiennent, sinon la liste des échecs.
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using Loupedeck;
using Loupedeck.TrinitePlugin;

var service = @"C:\Program Files\Logi\LogiPluginService\";
AssemblyLoadContext.Default.Resolving += (_, nom) =>
{
    var chemin = Path.Combine(service, nom.Name + ".dll");
    return File.Exists(chemin) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(chemin) : null;
};

var echecs = new List<String>();
void Vrai(Boolean condition, String regle)
{
    if (!condition)
    {
        echecs.Add(regle);
    }
}

// Essai réel du presse-papiers (écrase son contenu : jamais dans test.ps1) : dotnet run --project verifier -- presse-papiers
if (args.Contains("presse-papiers"))
{
    var dossier = Directory.CreateTempSubdirectory("nexus-presse").FullName;
    var png = Path.Combine(dossier, "essai.png");
    var webp = Path.Combine(dossier, "essai.webp");
    using (var image = new SkiaSharp.SKBitmap(7, 3))
    {
        image.Erase(SkiaSharp.SKColors.Red);
        File.WriteAllBytes(png, image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100).ToArray());
        File.WriteAllBytes(webp, image.Encode(SkiaSharp.SKEncodedImageFormat.Webp, 100).ToArray());
    }

    Vrai(PressePapiers.Poser(png, webp), "presse-papiers : trois formats posés");
    Vrai(Essai.Formats().SetEquals([8u, 15u, Essai.Png]), "presse-papiers : PNG, CF_DIB et CF_HDROP lisibles");
    Vrai(Essai.Fichier() == webp, "presse-papiers : fichier joint = le WebP");
    Vrai(Essai.Dib() == (7, 3, 32), "presse-papiers : DIB 7×3 en 32 bits");
    Directory.Delete(dossier, true);
    Console.WriteLine(echecs.Count == 0 ? "presse-papiers : essai réussi." : String.Join("\n", echecs));
    return echecs.Count == 0 ? 0 : 1;
}

var maintenant = DateTime.UtcNow;
Conversation C(Int32 a, Etat e, Boolean vu = false, Boolean relais = false) => new(a, "id", "titre", e, maintenant, maintenant, vu, relais);

// États notés par les hooks : une question ou une fin restent 12 h, un travail 20 min (au-delà, l'agent est mort).
Vrai(Observateur.Traduire("wait", maintenant.AddMinutes(-1)) == Etat.Attend, "wait récent → attend");
Vrai(Observateur.Traduire("ask", maintenant.AddMinutes(-1)) == Etat.Travaille, "ask → travaille (boîte de fin pas encore posée)");
Vrai(Observateur.Traduire("run", maintenant.AddMinutes(-30)) == Etat.Travaille, "run de 30 min → travaille (commande longue)");
Vrai(Observateur.Traduire("run", maintenant.AddHours(-3)) == Etat.Repos, "run de 3 h → repos");
Vrai(Observateur.Traduire("done", maintenant.AddHours(-1)) == Etat.Fini, "done d'1 h → fini");
Vrai(Observateur.Traduire("done", maintenant.AddHours(-13)) == Etat.Repos, "done de 13 h → repos");

// Clignotement : jamais pour le relais, jamais une fois vu, seulement question ou fin.
Vrai(C(1, Etat.Attend).Alerte, "question non vue → alerte");
Vrai(C(1, Etat.Fini).Alerte, "fin non vue → alerte");
Vrai(!C(1, Etat.Attend, relais: true).Alerte, "question du relais → pas d'alerte");
Vrai(!C(1, Etat.Fini, vu: true).Alerte, "fin vue → pas d'alerte");
Vrai(!C(1, Etat.Travaille).Alerte, "travail → pas d'alerte");

// Ordre d'une touche : question, fin, travail pour l'utilisateur, travail du relais, le reste.
Vrai(Observateur.Rang(C(0, Etat.Attend)) > Observateur.Rang(C(0, Etat.Fini)), "question avant fin");
Vrai(Observateur.Rang(C(0, Etat.Fini)) > Observateur.Rang(C(0, Etat.Travaille)), "fin avant travail");
Vrai(Observateur.Rang(C(0, Etat.Travaille)) > Observateur.Rang(C(0, Etat.Travaille, relais: true)), "travail de l'utilisateur avant relais");

// État d'ensemble d'une IA : appli fermée → fermé ; ouverte sans conversation → repos.
Quota[] sansQuota = [null, null, null];
var instantane = new Instantane([[C(0, Etat.Travaille)], [], []], [1, 0, 0], sansQuota, [true, true, false], false, null, -1);
Vrai(instantane.EtatIA(0) == Etat.Travaille, "conversation qui travaille → travaille");
Vrai(instantane.EtatIA(1) == Etat.Repos, "appli ouverte sans conversation → repos");
Vrai(instantane.EtatIA(2) == Etat.Ferme, "appli fermée → fermé");

// Quotas d'Antigravity lus sur sa page Settings › Models (relevé réel du 20/10) : groupe le plus entamé par fenêtre.
var page = """
Models & Usage
Plan
Your Plan: Google AI Pro
Gemini Models
Weekly Limit Remaining
You have used some of your weekly limit, it will fully refresh in 6 days, 9 hours.
33%
Five Hour Limit Remaining
You have used some of your 5-hour limit, it will fully refresh in 14 minutes.
3%
Claude and GPT models
Weekly Limit Remaining
100%
Five Hour Limit Remaining
100%
""";
var qa = QuotaAntigravity.Analyser(page);
Vrai(Math.Abs((qa?.Cinq ?? -1) - 0.97) < 1e-9, "Antigravity 5 h : 3 % restants = 97 % utilisés");
Vrai(Math.Abs((qa?.Semaine ?? -1) - 0.67) < 1e-9, "Antigravity semaine : 33 % restants = 67 % utilisés");
Vrai(QuotaAntigravity.Analyser("autre dialogue") is null, "page illisible → rien");
var finCinq = (qa?.Groupes.Single(g => g.Cinq && g.Utilise > 0.9).Fin ?? DateTime.MinValue) - DateTime.UtcNow;
Vrai(finCinq > TimeSpan.FromMinutes(13) && finCinq <= TimeSpan.FromMinutes(14), "Antigravity 5 h : remise à zéro dans 14 min");
var finSemaine = (qa?.Groupes.Single(g => !g.Cinq && g.Utilise > 0.6).Fin ?? DateTime.MinValue) - DateTime.UtcNow;
Vrai(finSemaine > new TimeSpan(6, 8, 59, 0) && finSemaine <= new TimeSpan(6, 9, 0, 0), "Antigravity semaine : remise à zéro dans 6 j 9 h");
// Groupe le plus entamé remis à zéro : le suivant prend la jauge (Gemini 80 % fini, Claude et GPT 50 % encore 4 h).
var instant = DateTime.UtcNow;
var groupes = new Quota(null, null)
{
    Groupes = [new(true, 0.8, instant.AddMinutes(-1)), new(true, 0.5, instant.AddHours(4)), new(false, 0.6, null)],
};
var actuel = QuotaAntigravity.Actuel(groupes, instant.AddDays(-8));
Vrai(actuel.Cinq == 0.5 && actuel.Semaine == 0, "remise à zéro par groupe ; sans heure, au bout de sa durée");
Vrai(QuotaAntigravity.Actuel(groupes, instant).Semaine == 0.6, "semaine sans heure, lue à l'instant : gardée");
// Ancien cache (sans groupes) avec ses heures de remise à zéro : elles restent appliquées.
var cache = Path.Combine(Directory.CreateTempSubdirectory("nexus-quota").FullName, "quota-antigravity.json");
var vraiCache = QuotaAntigravity.Fichier;
QuotaAntigravity.Fichier = cache; // fichier temporaire : le vrai cache du plugin en service n'est jamais touché
try
{
    File.WriteAllText(cache, JsonSerializer.Serialize(new
    {
        cinq = 0.97, semaine = 0.4, finCinq = instant.AddMinutes(-1), finSemaine = instant.AddDays(3), lu = instant.AddMinutes(-15),
    }));
    var garde = QuotaAntigravity.Garde(out _);
    Vrai(garde?.Cinq == 0 && garde?.Semaine == 0.4, "ancien cache : heure de remise à zéro gardée appliquée");
}
finally
{
    QuotaAntigravity.Fichier = vraiCache;
    Directory.Delete(Path.GetDirectoryName(cache), true);
}
// Questions de Codex sans attente (request_user_input_async, format relevé le 27/10) : le chat passe en question jusqu'à
// la réponse (<send_user_message_question_reply> qui cite le call_id), même si le tour est fini ; jamais pour le relais.
String L(String h, String charge) => $$"""{"timestamp":"2026-10-27T{{h}}Z",{{charge}}}""";
var debut = L("20:48:34.000", "\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}");
var question = L("20:49:06.000", "\"type\":\"response_item\",\"payload\":{\"type\":\"function_call\",\"status\":\"completed\",\"name\":\"request_user_input_async\",\"arguments\":\"{\\\"questions\\\":[{\\\"title\\\":\\\"Courts ou détaillés ?\\\"}]}\",\"call_id\":\"call_dfc1744cb4404887847e77d2911650c0\"}");
var fin = L("20:51:12.000", "\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"last_agent_message\":\"Relecture faite.\"}");
var reponse = L("20:55:00.000", "\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"role\":\"user\",\"content\":[{\"type\":\"input_text\",\"text\":\"<send_user_message_question_reply>\\n[{\\\"questionItemId\\\":\\\"[\\\\\\\"request_user_input_async\\\\\\\",\\\\\\\"call_dfc1744cb4404887847e77d2911650c0\\\\\\\"]\\\"}]\"}]}");
var ordinaire = L("20:56:00.000", "\"type\":\"event_msg\",\"payload\":{\"type\":\"user_message\",\"message\":\"Autre chose\"}");
var citee = L("20:57:00.000", "\"type\":\"response_item\",\"payload\":{\"type\":\"function_call_output\",\"call_id\":\"call_autre\",\"output\":\"<send_user_message_question_reply> call_dfc1744cb4404887847e77d2911650c0\"}");
var reprise = L("20:58:00.000", "\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}");
var instantJournal = new DateTime(2026, 10, 27, 21, 0, 0, DateTimeKind.Utc);
String Multi() => L("20:49:30.000", $$"""
"type":"response_item","payload":{"type":"function_call","name":"request_user_input_async","arguments":"{\"questions\":[{\"title\":\"q0\"},{\"title\":\"q1\"}]}","call_id":"call_multi123"}
""".Trim());
String Reponse(String rangs) => L("20:59:00.000", $$"""
"type":"response_item","payload":{"type":"message","role":"user","content":[{"type":"input_text","text":"<send_user_message_question_reply>\n[{{rangs}}]"}]}
""".Trim());
String Item(Int32 r) => $$"""{\"questionItemId\":\"[\\\"request_user_input_async\\\",\\\"call_multi123\\\",{{r}}]\"}""";
Vrai(Observateur.EtatJournalCodex([debut, question], false, instantJournal).Item1 == Etat.Attend, "question async pendant le tour → question");
Vrai(Observateur.EtatJournalCodex([debut, question, fin], false, instantJournal) is (Etat.Attend, var q) && q.Minute == 49, "tour fini, question sans réponse → question (à son heure)");
Vrai(Observateur.EtatJournalCodex([debut, question, fin, reponse], false, instantJournal).Item1 == Etat.Fini, "réponse qui cite le call_id → la question se ferme");
Vrai(Observateur.EtatJournalCodex([debut, question, fin, ordinaire], false, instantJournal).Item1 == Etat.Attend, "message ordinaire : pas une réponse, la carte reste");
Vrai(Observateur.EtatJournalCodex([debut, question, fin, citee], false, instantJournal).Item1 == Etat.Attend, "citation dans une sortie d'outil → ne ferme rien");
Vrai(Observateur.EtatJournalCodex([debut, question, fin, reprise], false, instantJournal.AddHours(13)).Item1 == Etat.Travaille,
    "question de plus de 12 h : effacée, le travail en cours se voit");
Vrai(Observateur.EtatJournalCodex([debut, question, fin], true, instantJournal).Item1 == Etat.Fini, "chat du relais → jamais de question");
Vrai(Observateur.EtatJournalCodex([debut, Multi(), fin, Reponse(Item(0))], false, instantJournal).Item1 == Etat.Attend, "deux questions, une seule répondue → question");
Vrai(Observateur.EtatJournalCodex([debut, Multi(), fin, Reponse(Item(0)), Reponse(Item(1))], false, instantJournal).Item1 == Etat.Fini, "les deux répondues l'une après l'autre → fermé");
Vrai(Observateur.EtatJournalCodex([debut, Multi(), fin, Reponse(Item(0) + "," + Item(1))], false, instantJournal).Item1 == Etat.Fini, "réponses groupées → fermé");
Vrai(Observateur.EtatJournalCodex([debut, fin], false, instantJournal).Item1 == Etat.Fini, "sans question async → inchangé");

Robustesse.Delais(Vrai);
Robustesse.Tout(Vrai, [debut, question, fin, reponse, Multi(), Reponse(Item(0) + "," + Item(1)), ordinaire, citee], page);

Vrai(QuotaAntigravity.Delai("it will fully refresh in 4 days, 23 hours.") == new TimeSpan(4, 23, 0, 0)
    && QuotaAntigravity.Delai("Weekly Limit Remaining") is null, "délai de remise à zéro lu en jours, heures, minutes");
// Relevé du quota : la fenêtre des réglages (onglet Models), jamais le premier [role=dialog] venu (une notification
// « Conversation Archived … Undo » en est un aussi et bloquait le relevé, 26/10).
foreach (var script in new[] { "Passif", "Actif" })
{
    var code = (String)typeof(QuotaAntigravity).GetField(script, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
    Vrai(!code.Contains("document.querySelector('[role=dialog]')") && code.Contains("=== 'Models'"), $"quota d'Antigravity ({script}) : vise la fenêtre des réglages");
}

// Ouverture d'une conversation d'Antigravity : l'identifiant reste une chaîne JSON (pas d'injection dans le script).
var js = Js.OuvrirAntigravity("abc'); alert(1); ('", route: true);
Vrai(js.Contains("location.assign", StringComparison.Ordinal), "route /c/<id> si la ligne manque");
Vrai(!js.Contains("'); alert(1)", StringComparison.Ordinal) && js.Contains("abc" + (Char)92 + "u0027", StringComparison.Ordinal), "identifiant passé en chaîne JSON échappée");

// Non-lus de Gmail et Messages : le nombre entre parenthèses du titre de leur fenêtre ; un onglet de Chrome ne compte pas.
Vrai(AppliWeb.Gmail.NonLus(["Codex LB", "Boîte de réception (12) - a@example.com - Gmail"]) == 12, "Gmail : 12 non-lus dans le titre");
Vrai(AppliWeb.Gmail.NonLus(["Boîte de réception - a@example.com - Gmail"]) == 0, "Gmail sans nombre : 0");
Vrai(AppliWeb.Gmail.NonLus(["Boîte de réception (3) - Gmail - Google Chrome"]) is null, "onglet Gmail de Chrome : pas une fenêtre d'appli");
Vrai(AppliWeb.Messages.NonLus(["Messages - Google Messages pour le Web : conversations"]) == 0, "Messages : titre réel à espaces insécables reconnu");
Vrai(AppliWeb.Messages.NonLus(["(2) Google Messages pour le Web"]) == 2 && AppliWeb.Messages.NonLus([]) is null, "Messages : titre, sinon rien");
// Touche du site sans titre : sa fenêtre est celle dont la page est sous l'adresse (origine, puis chemin au segment près).
Boolean Sous(String page, String adresse) => AppliWeb.Sous(new Uri(page), new Uri(adresse));
Vrai(Sous("https://github.com/pironjulien/NexusKeypad/issues", "https://github.com/pironjulien/NexusKeypad")
    && Sous("https://www.example.org/a", "https://example.org/") && Sous("https://example.org/", "https://example.org"),
    "site : page sous l'adresse, www ignoré, racine");
Vrai(!Sous("http://localhost:5173/", "http://localhost:3000/") && !Sous("http://example.org/", "https://example.org/"),
    "site : port ou schéma différents → autre site");
Vrai(!Sous("https://github.com/pironjulien/NexusBrowser", "https://github.com/pironjulien/NexusKeypad")
    && !Sous("https://github.com/pironjulien/NexusKeypad-fork", "https://github.com/pironjulien/NexusKeypad"),
    "site : autre dépôt GitHub, ou chemin qui ne s'arrête pas au segment → autre site");
Vrai(Sous("https://example.org/view?id=alpha", "https://example.org/view?id=alpha") && !Sous("https://example.org/view?id=beta", "https://example.org/view?id=alpha")
    && !Sous("https://example.org/view", "https://example.org/view?id=alpha") && Sous("https://example.org/view?id=beta", "https://example.org/view"),
    "site : requête de l'adresse exigée à l'identique (aucune requête donnée : toutes acceptées)");
Vrai(Sous("https://example.org/app#/document/alpha", "https://example.org/app#/document/alpha")
    && !Sous("https://example.org/app#/document/beta", "https://example.org/app#/document/alpha"),
    "site : fragment de l'adresse exigé à l'identique");
Vrai(AppliWeb.Gmail.NonLus(["(2) Issues · pironjulien/nexus-gmail", "Questions sur l'API Gmail (4)"]) is null
    && AppliWeb.Messages.NonLus(["(3) Messages pour le Web - aide"]) is null, "onglets qui citent Gmail ou Messages : pas de fausse pastille");

// Cache d'images : une scène identique donne des pixels identiques ; la photo ne bouge pas d'un instant à l'autre ;
// sans téléphone, la touche Photo est une horloge animée.
var midi = new DateTime(2026, 10, 24, 12, 34, 56);
Scene S(Double t, Instantane i = null) => new() { Instantane = i ?? instantane, Temps = t, Appuis = Enumerable.Repeat(-9.0, Moteur.Touches).ToArray(), PhotoCopiee = -9, CaptureCollee = -9, Maintenant = midi };
for (var k = 0; k < 9; k++)
{
    Vrai(Rendu.Image(k, S(1.0)).Pixels.AsSpan().SequenceEqual(Rendu.Image(k, S(1.0)).Pixels), $"touche {k + 1} : même scène, mêmes pixels");
}

using (var carre = new BitmapBuilder(Rendu.T, Rendu.T))
{
    carre.Clear(new BitmapColor(200, 120, 40));
    var avecPhoto = instantane with { Photo = new("/sdcard/DCIM/Camera/x.jpg", "x.jpg", BitmapImage.FromArray(carre.ToArray(BitmapImageFormat.Png))) };
    Vrai(Rendu.Image(Moteur.Photo, S(0.1, avecPhoto)).Pixels.AsSpan().SequenceEqual(Rendu.Image(Moteur.Photo, S(3.7, avecPhoto)).Pixels), "photo immobile");
}

Vrai(!Rendu.Image(Moteur.Photo, S(0.1, instantane with { Photo = null })).Pixels.AsSpan().SequenceEqual(Rendu.Image(Moteur.Photo, S(0.6, instantane with { Photo = null })).Pixels), "sans téléphone : horloge animée");
Vrai(!Rendu.Image(0, S(0.1)).Pixels.AsSpan().SequenceEqual(Rendu.Image(0, S(0.6)).Pixels), "tête qui travaille animée");

// Mise à jour de l'édition complète : versions comparées, réglage, état pour l'appli Nexus, paquet contrôlé (SHA-256 de
// la release, plugin et version attendus) et déballé dans un dossier de travail. Dossiers temporaires seulement.
{
    Vrai(MiseAJour.PlusRecente("1.10.0", "1.9.3") && MiseAJour.PlusRecente("v1.3.1", "1.3.0") && !MiseAJour.PlusRecente("1.3.0", "1.3.0")
        && !MiseAJour.PlusRecente("1.2.9", "1.3.0"), "mise à jour : versions comparées nombre par nombre");
    var dossier = Directory.CreateTempSubdirectory("nexus-maj").FullName;
    var reglages = Path.Combine(dossier, "reglages.json");
    Vrai(MiseAJour.Auto(reglages), "mise à jour : automatique sans réglage");
    File.WriteAllText(reglages, """{"miseAJourAuto": false}""");
    Vrai(!MiseAJour.Auto(reglages), "mise à jour : coupée par le réglage");
    var etat = Path.Combine(dossier, "status", "keypad.json");
    MiseAJour.Ecrire("1.3.0", "1.3.1", "idle", "update available", etat);
    var lu = JsonDocument.Parse(File.ReadAllText(etat)).RootElement;
    Vrai(lu.GetProperty("product").GetString() == "keypad" && lu.GetProperty("version").GetString() == "1.3.0" && lu.GetProperty("latest").GetString() == "1.3.1"
        && lu.GetProperty("state").GetString() == "idle" && lu.TryGetProperty("checked_at", out _), "mise à jour : état au contrat de l'appli Nexus");

    // Faux paquet 1.3.1 : bin/NexusKeypadPlugin.dll et metadata/LoupedeckPackage.yaml.
    var source = Path.Combine(dossier, "source");
    Directory.CreateDirectory(Path.Combine(source, "bin"));
    Directory.CreateDirectory(Path.Combine(source, "metadata"));
    File.WriteAllText(Path.Combine(source, "bin", "NexusKeypadPlugin.dll"), "dll");
    File.WriteAllText(Path.Combine(source, "metadata", "LoupedeckPackage.yaml"), "name: NexusKeypad\nversion: 1.3.1\n");
    var paquet = Path.Combine(dossier, MiseAJour.NomPaquet("1.3.1"));
    System.IO.Compression.ZipFile.CreateFromDirectory(source, paquet);
    var empreinte = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(paquet))).ToLowerInvariant();
    var sommes = $"{new String('0', 64)}  autre.lplug4\n{empreinte}  {MiseAJour.NomPaquet("1.3.1")}\n";
    var travail = Path.Combine(dossier, "maj");
    Directory.CreateDirectory(travail);
    var pret = MiseAJour.Preparer(paquet, sommes, "1.3.1", travail);
    Vrai(File.Exists(Path.Combine(pret, "bin", "NexusKeypadPlugin.dll")) && MiseAJour.Version(pret) == "1.3.1", "mise à jour : paquet contrôlé et déballé");
    Boolean Refuse(Action a) { try { a(); return false; } catch (InvalidDataException) { return true; } }
    Vrai(Refuse(() => MiseAJour.Preparer(paquet, sommes.Replace(empreinte, new String('1', 64)), "1.3.1", travail)), "mise à jour : empreinte différente refusée");
    Vrai(Refuse(() => MiseAJour.Preparer(paquet, "", "1.3.1", travail)), "mise à jour : paquet absent de SHA256SUMS refusé");
    Vrai(Refuse(() => MiseAJour.Preparer(paquet, sommes.Replace(MiseAJour.NomPaquet("1.3.1"), MiseAJour.NomPaquet("1.3.2")), "1.3.2", travail)), "mise à jour : autre version refusée");

    // SHA256SUMS.txt signé (clé d'essai) : accepté ; modifié, signé par une autre clé, sans signature : refusé. La vraie
    // clé embarquée se lit.
    using (var cle = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256))
    using (var autreCle = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256))
    {
        var publique = new[] { Convert.ToBase64String(cle.ExportSubjectPublicKeyInfo()) };
        var contenu = System.Text.Encoding.UTF8.GetBytes(sommes);
        String Signe(System.Security.Cryptography.ECDsa k, Byte[] c) => Convert.ToBase64String(k.SignData(c, System.Security.Cryptography.HashAlgorithmName.SHA256));
        Vrai(MiseAJour.Sommes(contenu, Signe(cle, contenu) + "\n", publique) == sommes, "mise à jour : SHA256SUMS signé accepté");
        var modifie = System.Text.Encoding.UTF8.GetBytes(sommes.Replace(empreinte, new String('1', 64)));
        Vrai(Refuse(() => MiseAJour.Sommes(modifie, Signe(cle, contenu), publique)), "mise à jour : SHA256SUMS modifié refusé");
        Vrai(Refuse(() => MiseAJour.Sommes(contenu, Signe(autreCle, contenu), publique)), "mise à jour : signé par une autre clé refusé");
        Vrai(Refuse(() => MiseAJour.Sommes(contenu, "", publique)) && Refuse(() => MiseAJour.Sommes(contenu, "pas du base64 !", publique)), "mise à jour : sans signature refusé");
        Vrai(Refuse(() => MiseAJour.Sommes(contenu, Signe(cle, contenu))), "mise à jour : clé d'essai refusée par les clés embarquées");
    }

    // Rechargement après un échange : « done » de cette version gardé ; sinon « idle » avec la dernière version connue.
    MiseAJour.Ecrire("1.3.1", "1.3.1", "done", "updated", etat);
    MiseAJour.Recharge("1.3.1", etat);
    Vrai(JsonDocument.Parse(File.ReadAllText(etat)).RootElement.GetProperty("state").GetString() == "done", "mise à jour : « done » gardé au rechargement");
    MiseAJour.Recharge("1.3.2", etat);
    lu = JsonDocument.Parse(File.ReadAllText(etat)).RootElement;
    Vrai(lu.GetProperty("state").GetString() == "idle" && lu.GetProperty("version").GetString() == "1.3.2" && lu.GetProperty("latest").GetString() == "1.3.1",
        "mise à jour : rechargement d'une autre version en « idle », dernière version gardée");
    Directory.Delete(dossier, true);
}

// États en direct : pose et retrait des hooks par le plugin (ceux des autres outils gardés, idempotent), format
// d'Antigravity, et rien de modifié quand la pose échoue.
{
    System.Text.Json.Nodes.JsonObject J(String s) => (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(s);
    var autre = """{"type": "command", "command": "& share-session.ps1", "timeout": 30}""";
    var ancien = """{"type": "command", "command": "python -I -B (Join-Path $env:OneDrive 'Documents\\NexusHarness\\NexusKeypad\\etat\\etat.py') claude SessionEnd"}""";
    var reglages = J("{\"model\": \"x\", \"hooks\": {\"SessionEnd\": [{\"hooks\": [" + ancien + ", " + autre + "]}], \"Stop\": [{\"hooks\": [" + autre + "]}]}}");
    EtatsEnDirect.ArrangerClaude(reglages, pose: true);
    EtatsEnDirect.ArrangerClaude(reglages, pose: true);
    var commandes = reglages["hooks"]!.AsObject().SelectMany(p => p.Value!.AsArray()).SelectMany(g => g!["hooks"]!.AsArray())
        .Select(h => h!["command"]!.GetValue<String>()).ToList();
    Vrai(commandes.Count(c => c == "& share-session.ps1") == 2 && commandes.Count(c => c.Contains("nexuskeypad-hook")) == EtatsEnDirect.EvenementsClaude.Length
        && !commandes.Any(c => c.Contains("etat.py")) && reglages["model"]!.GetValue<String>() == "x",
        "états en direct : un hook du pad par événement, etat.py remplacé, ceux des autres gardés, idempotent");
    Vrai(EtatsEnDirect.ClaudeAvecPad(reglages) && reglages["hooks"]!["Stop"]!.AsArray().Last()!["hooks"]![0]!["async"]!.GetValue<Boolean>(),
        "états en direct : hooks de Claude en tâche de fond");
    EtatsEnDirect.ArrangerClaude(reglages, pose: false);
    Vrai(!EtatsEnDirect.ClaudeAvecPad(reglages) && reglages["hooks"]!.AsObject().Count == 2
        && reglages["hooks"]!.AsObject().All(p => p.Value!.AsArray().Count == 1),
        "états en direct : retrait = seulement ceux du pad, événements vidés retirés");
    Vrai(!EtatsEnDirect.DuPad(J("""{"command": "pwsh -File audit.ps1 -Project NexusKeypad"}""")), "états en direct : un outil qui cite le projet n'est pas du pad");
    var agy = J("""{"autre-outil": {}}""");
    EtatsEnDirect.ArrangerAntigravity(agy, @"C:\X\nexuskeypad-hook.exe");
    var entree = agy[EtatsEnDirect.CleAntigravity]!;
    Vrai(agy.ContainsKey("autre-outil") && entree["PreToolUse"]![0]!["matcher"]!.GetValue<String>() == "*"
        && entree["Stop"]![0]!["command"]!.GetValue<String>() == @"C:\X\nexuskeypad-hook.exe antigravity Stop",
        "états en direct : format d'Antigravity (outils en enveloppe { matcher, hooks }, sinon gestionnaires directs)");

    var dossier = Directory.CreateTempSubdirectory("nexus-etats").FullName;
    var claude = Path.Combine(dossier, ".claude", "settings.json");
    var gemini = Path.Combine(dossier, ".gemini", "config", "hooks.json");
    Directory.CreateDirectory(Path.GetDirectoryName(claude));
    Directory.CreateDirectory(Path.GetDirectoryName(gemini));
    File.WriteAllText(claude, """{"hooks": {}}""");
    var paquet = Path.Combine(dossier, "paquet", "nexuskeypad-hook.exe");
    var exe = Path.Combine(dossier, "local avec espace", "nexuskeypad-hook.exe");
    var catalogue = Path.Combine(dossier, "local avec espace", "claude-marketplace");
    File.WriteAllText(claude, """{"hooks": {}, "enabledPlugins": {"autre@x": true}}""");
    var reglagesAvant = File.ReadAllText(claude);
    Vrai(!EtatsEnDirect.Appliquer(paquet, true, claude, gemini, exe, catalogue) && File.ReadAllText(claude) == reglagesAvant && !File.Exists(gemini),
        "états en direct : pose échouée (exécutable absent) → rien de modifié");
    Directory.CreateDirectory(Path.GetDirectoryName(paquet));
    File.WriteAllText(paquet, "exe");
    Vrai(!EtatsEnDirect.Appliquer(paquet, true, claude, gemini, exe, catalogue) && File.ReadAllText(claude) == reglagesAvant && !File.Exists(gemini),
        "états en direct : pose échouée (mod du quota absent du paquet) → rien de modifié");
    var mod = Path.Combine(dossier, "paquet", EtatsEnDirect.DossierModsPaquet, ".claude-plugin", "marketplace.json");
    Directory.CreateDirectory(Path.GetDirectoryName(mod));
    File.WriteAllText(mod, "{}");
    Vrai(EtatsEnDirect.Appliquer(paquet, true, claude, gemini, exe, catalogue) && File.Exists(exe)
        && EtatsEnDirect.ClaudeAvecPad(J(File.ReadAllText(claude))) && !J(File.ReadAllText(gemini))[EtatsEnDirect.CleAntigravity]!.ToJsonString().Contains("avec espace"),
        "états en direct : pose (exécutable copié, Claude, Antigravity par un chemin sans espace)");
    var pose = J(File.ReadAllText(claude));
    Vrai(File.Exists(Path.Combine(catalogue, ".claude-plugin", "marketplace.json"))
        && pose["extraKnownMarketplaces"]![EtatsEnDirect.Catalogue]!["source"]!["path"]!.GetValue<String>() == catalogue
        && pose["extraKnownMarketplaces"]![EtatsEnDirect.Catalogue]!["source"]!["source"]!.GetValue<String>() == "directory"
        && pose["enabledPlugins"]![EtatsEnDirect.ModQuota]!.GetValue<Boolean>() && pose["enabledPlugins"]!["autre@x"]!.GetValue<Boolean>(),
        "états en direct : mod du quota (catalogue local copié, déclaré et activé ; autres plugins gardés)");
    Vrai(EtatsEnDirect.Appliquer(paquet, false, claude, gemini, exe, catalogue) && !EtatsEnDirect.ClaudeAvecPad(J(File.ReadAllText(claude)))
        && !J(File.ReadAllText(gemini)).ContainsKey(EtatsEnDirect.CleAntigravity),
        "états en direct : retrait chez les deux");
    var retire = J(File.ReadAllText(claude));
    Vrai(!retire.ContainsKey("extraKnownMarketplaces") && retire["enabledPlugins"]!.AsObject().Count == 1 && !Directory.Exists(catalogue),
        "états en direct : retrait du mod du quota (clés et catalogue), autres plugins gardés");

    // Plugin Claude complet déclaré (nexus-keypad@nexus) : il porte hooks et quota ; le pad l'active ou le coupe, sans
    // jamais poser ses hooks ni l'ancien mod de quota en double.
    File.WriteAllText(claude, new System.Text.Json.Nodes.JsonObject { ["hooks"] = new System.Text.Json.Nodes.JsonObject(), ["enabledPlugins"] = new System.Text.Json.Nodes.JsonObject { [EtatsEnDirect.PluginClaude] = false } }.ToJsonString());
    Vrai(!EtatsEnDirect.ClaudeAvecPad(J(File.ReadAllText(claude))), "états en direct : plugin complet coupé → pas posé");
    var complet = EtatsEnDirect.Appliquer(paquet, true, claude, gemini, exe, catalogue) ? J(File.ReadAllText(claude)) : null;
    Vrai(complet is not null && EtatsEnDirect.ClaudeAvecPad(complet) && complet["hooks"]!.AsObject().Count == 0
        && complet["enabledPlugins"]![EtatsEnDirect.PluginClaude]!.GetValue<Boolean>()
        && complet["enabledPlugins"]![EtatsEnDirect.ModQuota] is null && !complet.ContainsKey("extraKnownMarketplaces"),
        "états en direct : pose avec le plugin complet (activé, aucun hook ni mod de quota en double)");
    Vrai(EtatsEnDirect.Appliquer(paquet, false, claude, gemini, exe, catalogue) && !EtatsEnDirect.ClaudeAvecPad(J(File.ReadAllText(claude)))
        && !J(File.ReadAllText(claude))["enabledPlugins"]![EtatsEnDirect.PluginClaude]!.GetValue<Boolean>(),
        "états en direct : retrait avec le plugin complet (coupé)");
    Directory.Delete(dossier, true);
}

foreach (var e in echecs)
{
    Console.WriteLine($"ÉCHEC : {e}");
}

Console.WriteLine(echecs.Count == 0 ? "verifier : toutes les règles tiennent." : $"verifier : {echecs.Count} échec(s).");
return echecs.Count == 0 ? 0 : 1;

// Relecture du presse-papiers pour l'essai réel (API Win32, comme les applis qui collent).
static class Essai
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern UInt32 RegisterClipboardFormat(String nom);
    [DllImport("user32.dll")] private static extern Boolean OpenClipboard(IntPtr h);
    [DllImport("user32.dll")] private static extern Boolean CloseClipboard();
    [DllImport("user32.dll")] private static extern UInt32 EnumClipboardFormats(UInt32 format);
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(UInt32 format);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern UInt32 DragQueryFile(IntPtr depot, UInt32 i, System.Text.StringBuilder nom, Int32 max);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr m);
    [DllImport("kernel32.dll")] private static extern Boolean GlobalUnlock(IntPtr m);
}

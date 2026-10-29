using System.Text.Json.Nodes;
using Loupedeck.TrinitePlugin;

if (args is ["check-profile", var profil])
{
    try
    {
        Connecteurs.ActiverToml(File.ReadAllText(profil), "nexus-keypad@personal", true);
        Console.WriteLine("Profil Codex réel : changement ciblé validé sans écriture.");
        return 0;
    }
    catch (InvalidDataException erreur)
    {
        Console.WriteLine(erreur.Message);
        return 1;
    }
}

var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "does-not-exist"));
if (args.Length == 1) { source = Path.GetFullPath(args[0]); }
var racine = Directory.CreateTempSubdirectory("nexuskeypad-connecteurs-").FullName;
var maison = Path.Combine(racine, "user");
var local = Path.Combine(racine, "local");
var paquet = Path.Combine(racine, "package");
Directory.CreateDirectory(maison);
Directory.CreateDirectory(local);
Directory.CreateDirectory(paquet);
var executable = Path.Combine(paquet, "nexuskeypad-hook.exe");
File.WriteAllText(executable, "isolated fixture, never executed");
foreach (var fichier in Directory.EnumerateFiles(Path.Combine(source, "connecteurs"), "*", SearchOption.AllDirectories))
{
    var cible = Path.Combine(paquet, "connecteurs", Path.GetRelativePath(Path.Combine(source, "connecteurs"), fichier));
    Directory.CreateDirectory(Path.GetDirectoryName(cible));
    File.Copy(fichier, cible);
}
var quota = Path.Combine(paquet, "claude-quota", "nexuskeypad-quota", "hooks", "register.ts");
Directory.CreateDirectory(Path.GetDirectoryName(quota));
File.Copy(Path.Combine(source, "claude-quota", "nexuskeypad-quota", "hooks", "register.ts"), quota);
Connecteurs.DossiersEssai(maison, local, executable);
var appelsClaude = new List<String>();
var echecClaude = false;
var fauxSuccesClaude = false;
var miseAJourClaudeInchangee = false;
(Int32, String) GestionnaireClaude(String[] arguments)
{
    appelsClaude.Add(String.Join("|", arguments));
    if (echecClaude) { return (1, "{\"outcome\":\"failed\"}"); }
    if (fauxSuccesClaude) { return (0, "{\"outcome\":\"ok\"}"); }
    if (miseAJourClaudeInchangee && arguments[1] == "update") { return (0, "{\"outcome\":\"ok\",\"updateOutcome\":\"up_to_date\"}"); }
    var catalogue = Path.Combine(local, "NexusKeypad", "connecteurs", "claude");
    var registre = Path.Combine(maison, ".claude", "plugins");
    Directory.CreateDirectory(registre);
    if (arguments.Take(3).SequenceEqual(new[] { "plugin", "marketplace", "add" }))
    {
        File.WriteAllText(Path.Combine(registre, "known_marketplaces.json"), new JsonObject { ["nexuskeypad"] = new JsonObject {
            ["source"] = new JsonObject { ["source"] = "directory", ["path"] = catalogue }, ["installLocation"] = catalogue } }.ToJsonString());
    }
    else if (arguments[1] == "uninstall")
    {
        File.Delete(Path.Combine(registre, "installed_plugins.json"));
    }
    else if (arguments[1] is "install" or "update")
    {
        var cible = Path.Combine(registre, "cache", "nexuskeypad", "nexus-keypad", Connecteurs.Version);
        foreach (var fichier in Directory.EnumerateFiles(Path.Combine(catalogue, "nexus-keypad"), "*", SearchOption.AllDirectories))
        {
            var copie = Path.Combine(cible, Path.GetRelativePath(Path.Combine(catalogue, "nexus-keypad"), fichier));
            Directory.CreateDirectory(Path.GetDirectoryName(copie)); File.Copy(fichier, copie, true);
        }
        File.WriteAllText(Path.Combine(registre, "installed_plugins.json"), new JsonObject { ["version"] = 2, ["plugins"] = new JsonObject {
            ["nexus-keypad@nexuskeypad"] = new JsonArray(new JsonObject { ["scope"] = "user", ["version"] = Connecteurs.Version, ["installPath"] = cible }) } }.ToJsonString());
    }
    else if (arguments[1] != "enable") { throw new InvalidOperationException("Unexpected plugin-management command."); }
    return (0, "{\"outcome\":\"ok\"}");
}
Connecteurs.CommandeClaudeEssai(GestionnaireClaude);

// Les profils réels contiennent des tableaux TOML (TomlArray n'implémente pas forcément IList non générique).
var tableauxProfil = "model = 'keep'\nnotify = ['one', 'two']\n[plugins.'other@personal']\nenabled = false\n";
var tableauxActives = Connecteurs.ActiverToml(tableauxProfil, "nexus-keypad@personal", true);
if (!tableauxActives.Contains("notify = ['one', 'two']") || !tableauxActives.Contains("other@personal"))
{
    throw new InvalidDataException("Les tableaux et plugins tiers du profil doivent être conservés.");
}
var echecs = new List<String>();
void Vrai(Boolean condition, String nom) { if (!condition) { echecs.Add(nom); } }
String Recu(Int32 agent) => Path.Combine(local, "NexusKeypad", "connecteurs", "etats", new[] { "claude", "codex", "antigravity" }[agent] + ".json");
DateTime VieillirPreparation(Int32 agent)
{
    var date = DateTime.UtcNow.AddDays(-1);
    var recu = JsonNode.Parse(File.ReadAllText(Recu(agent)));
    recu["preparedAt"] = date.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
    File.WriteAllText(Recu(agent), recu.ToJsonString() + "\n");
    return date;
}

foreach (var texte in new[]
{
    "# retained\nmodel = \"a\"\n[plugins.'nexus-keypad@personal']\nenabled = false # keep this\nother = 2\n",
    "[plugins]\n'nexus-keypad@personal' = { enabled = false, keep = 'x' }\n",
    "plugins = { 'nexus-keypad@personal' = { enabled = false, keep = 'x' }, other = { enabled = true } }\n",
    "[plugins.'nexus-keypad@personal']\nkeep = \"\"\"\n[pretend.table]\nenabled = false\n\"\"\"\n",
    "[plugins]\nother = { enabled = true }\n",
    "model = 'x'\n",
})
{
    try
    {
        var actif = Connecteurs.ActiverToml(texte, "nexus-keypad@personal", true);
        Vrai(actif.Contains("enabled = true"), "enable TOML syntax variant");
        var inactif = Connecteurs.ActiverToml(actif, "nexus-keypad@personal", false);
        Vrai(inactif.Contains("enabled = false"), "disable TOML syntax variant");
        if (texte.Contains("# retained")) { Vrai(actif.Contains("# retained") && actif.Contains("# keep this"), "TOML comments retained"); }
    }
    catch (Exception e) { echecs.Add("TOML variant " + texte.Split('\n')[0] + ": " + e.GetType().Name + " " + e.Message); }
}

for (var agent = 0; agent < 3; agent++)
{
    Vrai(Connecteurs.LireConfiguration(agent).Etat == EtatConnexion.Absent, "absent app " + agent);
    var dossier = agent switch { 0 => Path.Combine(maison, ".claude"), 1 => Path.Combine(maison, ".codex"), _ => Path.Combine(maison, ".gemini", "config") };
    Directory.CreateDirectory(dossier);
    Vrai(Connecteurs.LireConfiguration(agent).Etat == EtatConnexion.Activer, "not configured " + agent);
    if (agent == 1) { File.WriteAllText(Path.Combine(dossier, "config.toml"), "model = 'keep'\n[plugins.'third-party@personal']\nenabled = false\n"); }
    if (agent == 2) { File.WriteAllText(Path.Combine(dossier, "hooks.json"), "{\"third-party\":{\"Stop\":[]}}"); }
    var resultat = Connecteurs.Activer(agent);
    Vrai(resultat.Etat == EtatConnexion.Pret, "install verified " + agent + ": " + resultat.Detail);
    Vrai(Connecteurs.EvenementsActifs(new[] { "claude", "codex", "antigravity" }[agent]), "events enabled " + agent);
    Vrai(Connecteurs.Lire(agent, true, false).Etat == (agent == 0 ? EtatConnexion.Pret : EtatConnexion.Relancer), "port absent prevents ready " + agent);
    var anciennePreparation = VieillirPreparation(agent);
    var recuAvant = File.ReadAllBytes(Recu(agent));
    Vrai(Connecteurs.Activer(agent).PrepareLe == anciennePreparation, "identical enable preserves setup date " + agent);
    Vrai(File.ReadAllBytes(Recu(agent)).AsSpan().SequenceEqual(recuAvant), "identical enable leaves receipt unchanged " + agent);
    Vrai(Connecteurs.Desactiver(agent).Etat == EtatConnexion.Activer, "disable " + agent);
    Vrai(!Connecteurs.EvenementsActifs(new[] { "claude", "codex", "antigravity" }[agent]), "events disabled " + agent);
    if (agent == 1) { Vrai(File.ReadAllText(Path.Combine(dossier, "config.toml")).Contains("third-party@personal") && File.ReadAllText(Path.Combine(dossier, "config.toml")).Contains("model = 'keep'"), "Codex third party retained"); }
    if (agent == 2) { Vrai(JsonNode.Parse(File.ReadAllText(Path.Combine(dossier, "hooks.json")))["third-party"] is not null, "AGY third party hook retained"); }
    var reactive = Connecteurs.Activer(agent);
    Vrai(reactive.Etat == EtatConnexion.Pret, "reactivate " + agent);
    Vrai(reactive.PrepareLe > anciennePreparation, "real reactivation starts a new setup date " + agent);
    anciennePreparation = VieillirPreparation(agent);
    var guide = Path.Combine(agent switch { 0 => Path.Combine(local, "NexusKeypad", "connecteurs", "claude", "nexus-keypad"), 1 => Path.Combine(maison, ".codex", "plugins", "nexus-keypad"), _ => Path.Combine(maison, ".gemini", "config", "plugins", "nexus-keypad") }, "skills", "nexus-keypad", "SKILL.md");
    File.AppendAllText(guide, "changed");
    Vrai(Connecteurs.LireConfiguration(agent).Etat == EtatConnexion.Reparer, "changed file detected " + agent);
    var repare = Connecteurs.Activer(agent);
    Vrai(repare.Etat == EtatConnexion.Pret, "repair " + agent);
    Vrai(repare.PrepareLe > anciennePreparation, "repair never reuses prior setup evidence " + agent);
}

// A changed package must start a fresh observation interval even while the previous installed copy is valid.
var guidePaquet = Path.Combine(paquet, "connecteurs", "codex", "skills", "nexus-keypad", "SKILL.md");
var guideOriginal = File.ReadAllBytes(guidePaquet);
var avantMiseAJour = VieillirPreparation(1);
File.AppendAllText(guidePaquet, "\nchanged package fixture\n");
Vrai(Connecteurs.LireConfiguration(1).Etat == EtatConnexion.Pret, "old installed package is still internally valid before update");
var actualise = Connecteurs.Activer(1);
Vrai(actualise.Etat == EtatConnexion.Pret && actualise.PrepareLe > avantMiseAJour, "new package does not inherit old callback interval");
File.WriteAllBytes(guidePaquet, guideOriginal);
Vrai(Connecteurs.Activer(1).Etat == EtatConnexion.Pret, "package fixture restored through normal installation");
File.WriteAllText(Recu(1), "{broken");
Vrai(Connecteurs.LireConfiguration(1).Etat == EtatConnexion.Reparer && Connecteurs.Activer(1).Etat == EtatConnexion.Pret,
    "malformed receipt can be repaired without inheriting any old setup evidence");

// Codex hooks are rendered in both the marketplace source and the installed cache, without trusting them.
var codexPlugin = Path.Combine(maison, ".codex", "plugins", "nexus-keypad");
var codexCache = Path.Combine(maison, ".codex", "plugins", "cache", "personal", "nexus-keypad", Connecteurs.Version);
var codexHooksText = File.ReadAllText(Path.Combine(codexPlugin, "hooks", "hooks.json"));
var codexHooks = JsonNode.Parse(codexHooksText)["hooks"].AsObject();
Vrai(codexHooks.Count == 10, "Codex declared lifecycle hooks");
Vrai(!codexHooksText.Contains("__NEXUS_KEYPAD_HOOK__") && !codexHooksText.Contains("OneDrive") && !codexHooksText.Contains("python"), "Codex portable native hooks");
Vrai(codexHooksText == File.ReadAllText(Path.Combine(codexCache, "hooks", "hooks.json")), "Codex cache hooks rendered too");
Vrai(!File.Exists(Path.Combine(maison, ".codex", "hooks.json")), "Codex no global hook duplication");
Vrai(!Directory.EnumerateFiles(Path.Combine(maison, ".codex"), "*trust*", SearchOption.AllDirectories).Any(), "Codex installer never creates hook trust");
foreach (var evenement in codexHooks)
{
    var commande = evenement.Value[0]["hooks"][0];
    Vrai(commande["async"].GetValue<Boolean>() == false, "Codex native event ordering " + evenement.Key);
    Vrai(commande["command"].GetValue<String>().EndsWith(" codex " + evenement.Key), "Codex exact event command " + evenement.Key);
    if (evenement.Key is "Interrupt" or "SessionEnd") { Vrai(commande["timeout"].GetValue<Int32>() <= 3, "Codex short terminal timeout " + evenement.Key); }
}

// An external UI toggle is authoritative too, including callbacks already loaded by a running app.
var agyConfig = Path.Combine(maison, ".gemini", "config", "config.json");
var avantBasculeExterne = VieillirPreparation(2);
var agySettings = JsonNode.Parse(File.ReadAllText(agyConfig));
agySettings["plugins"]["nexus-keypad"]["enabled"] = false;
File.WriteAllText(agyConfig, agySettings.ToJsonString());
Vrai(!Connecteurs.EvenementsActifs("antigravity") && Connecteurs.LireConfiguration(2).Etat == EtatConnexion.Activer, "external AGY off switch respected");
var agyReactive = Connecteurs.Activer(2);
Vrai(agyReactive.Etat == EtatConnexion.Pret, "AGY explicit reactivate");
Vrai(agyReactive.PrepareLe > avantBasculeExterne, "external off switch invalidates earlier setup interval");

// Hooks belong to the plugin, and enabling twice never recreates a global registration.
Connecteurs.Activer(2);
var agyHooks = JsonNode.Parse(File.ReadAllText(Path.Combine(maison, ".gemini", "config", "hooks.json")));
Vrai(agyHooks[EtatsEnDirect.CleAntigravity] is null, "AGY no global activity hook");
var pluginHooksPath = Path.Combine(maison, ".gemini", "config", "plugins", "nexus-keypad", "hooks.json");
var pluginHooks = JsonNode.Parse(File.ReadAllText(pluginHooksPath));
Vrai(pluginHooks["nexus-keypad"]?["Stop"] is JsonArray { Count: 1 }, "AGY plugin owns Stop");
Vrai(!File.ReadAllText(pluginHooksPath).Contains("__NEXUS_KEYPAD_HOOK__"), "AGY command template rendered");
Vrai(!File.ReadAllText(pluginHooksPath).Contains("OneDrive") && !File.ReadAllText(pluginHooksPath).Contains("python"), "AGY standalone native hooks");

// Migrate our former handlers while preserving third-party handlers inside the same group.
var oldHooks = new JsonObject();
EtatsEnDirect.ArrangerAntigravity(oldHooks, "old/nexuskeypad-hook.exe");
((JsonArray)oldHooks[EtatsEnDirect.CleAntigravity]["Stop"]).Add(new JsonObject { ["command"] = "third-party.exe" });
oldHooks["nexus-keypad-sons"] = JsonNode.Parse("{\"Stop\":[{\"command\":\"python -I %OneDrive%/Documents/NexusHarness/NexusKeypad/sons/jouer.py antigravity --hook\"}]}");
oldHooks["son-fin-de-tache"] = JsonNode.Parse("{\"Stop\":[{\"command\":\"python -I %OneDrive%/Documents/NexusHarness/NexusKeypad/sons/jouer.py antigravity --hook\"},{\"command\":\"other-sound.exe\"}]}");
File.WriteAllText(Path.Combine(maison, ".gemini", "config", "hooks.json"), oldHooks.ToJsonString());
Vrai(Connecteurs.Activer(2).Etat == EtatConnexion.Pret, "AGY legacy migration");
var migrated = JsonNode.Parse(File.ReadAllText(Path.Combine(maison, ".gemini", "config", "hooks.json")));
Vrai(migrated["nexus-keypad-sons"] is null, "AGY legacy sound removed");
Vrai(migrated[EtatsEnDirect.CleAntigravity]["Stop"].AsArray().Count == 1 && migrated[EtatsEnDirect.CleAntigravity]["Stop"][0]["command"].GetValue<String>() == "third-party.exe", "AGY shared legacy group retained");
Vrai(migrated["son-fin-de-tache"]["Stop"].AsArray().Count == 1, "AGY shared legacy sound group retained");
var receipt = JsonNode.Parse(File.ReadAllText(Path.Combine(local, "NexusKeypad", "connecteurs", "etats", "antigravity.json")));
Vrai(receipt["hookOwner"].GetValue<String>() == "plugin", "AGY sound migration receipt");
File.WriteAllText(pluginHooksPath, "{}");
Vrai(Connecteurs.LireConfiguration(2).Etat == EtatConnexion.Reparer, "AGY missing bundled hooks detected");
Vrai(Connecteurs.Activer(2).Etat == EtatConnexion.Pret, "AGY bundled hooks repaired");

// The installed/public Claude identity must be attested by the official manager, not just settings and file presence.
Vrai(appelsClaude.Any(x => x.StartsWith("plugin|marketplace|add|", StringComparison.Ordinal))
    && appelsClaude.Any(x => x.StartsWith("plugin|install|nexus-keypad@nexuskeypad|", StringComparison.Ordinal)), "public Claude uses official marketplace add then install");
var appelsAvant = appelsClaude.Count;
Vrai(Connecteurs.Activer(0).Etat == EtatConnexion.Pret && appelsClaude.Count == appelsAvant, "unchanged ready Claude never reruns manager");
var registrePublic = Path.Combine(maison, ".claude", "plugins", "installed_plugins.json");
File.Delete(registrePublic);
Vrai(Connecteurs.LireConfiguration(0).Etat == EtatConnexion.Reparer, "public Claude settings alone never prove installation");
Connecteurs.CommandeClaudeEssai(null);
Vrai(Connecteurs.Activer(0).Etat == EtatConnexion.Erreur && Connecteurs.LireConfiguration(0).Etat != EtatConnexion.Pret, "missing official CLI never reports ready");
Connecteurs.CommandeClaudeEssai(GestionnaireClaude);
echecClaude = true;
Vrai(Connecteurs.Activer(0).Etat == EtatConnexion.Erreur && Connecteurs.LireConfiguration(0).Etat != EtatConnexion.Pret && !Connecteurs.EvenementsActifs("claude"), "failed native install keeps pending connector inactive");
echecClaude = false;
fauxSuccesClaude = true;
Vrai(Connecteurs.Activer(0).Etat == EtatConnexion.Erreur && Connecteurs.LireConfiguration(0).Etat != EtatConnexion.Pret, "exit zero without official registry never proves installation");
fauxSuccesClaude = false;
Vrai(Connecteurs.Activer(0).Etat == EtatConnexion.Pret, "official install repairs interrupted public setup");
var cachePublicClaude = JsonNode.Parse(File.ReadAllText(registrePublic))["plugins"]["nexus-keypad@nexuskeypad"][0]["installPath"].GetValue<String>();
File.AppendAllText(Path.Combine(cachePublicClaude, "skills", "nexus-keypad", "SKILL.md"), "damaged same-version cache");
miseAJourClaudeInchangee = true;
Vrai(Connecteurs.Activer(0).Etat == EtatConnexion.Pret && appelsClaude.Any(x => x.StartsWith("plugin|uninstall|nexus-keypad@nexuskeypad|", StringComparison.Ordinal)), "same-version damaged cache repaired by official reinstall after up_to_date");
miseAJourClaudeInchangee = false;
var reglagesPublics = Path.Combine(maison, ".claude", "settings.json");
var cataloguesPublics = Path.Combine(maison, ".claude", "plugins", "known_marketplaces.json");
foreach (var fichierConflit in new[] { reglagesPublics, cataloguesPublics })
{
    var original = File.ReadAllBytes(fichierConflit);
    var contenu = JsonNode.Parse(original);
    var entree = fichierConflit == reglagesPublics ? contenu["extraKnownMarketplaces"]["nexuskeypad"] : contenu["nexuskeypad"];
    entree["source"]["path"] = Path.Combine(maison, "different-marketplace");
    File.WriteAllText(fichierConflit, contenu.ToJsonString());
    var avantConflit = File.ReadAllBytes(fichierConflit);
    var avantRecu = File.ReadAllBytes(Recu(0));
    var avantAppels = appelsClaude.Count;
    Vrai(Connecteurs.Activer(0).Etat == EtatConnexion.Erreur, "foreign Claude marketplace is refused before setup");
    Vrai(File.ReadAllBytes(fichierConflit).AsSpan().SequenceEqual(avantConflit) && File.ReadAllBytes(Recu(0)).AsSpan().SequenceEqual(avantRecu)
        && appelsClaude.Count == avantAppels, "foreign Claude catalogue/receipt is unchanged and manager not invoked");
    File.WriteAllBytes(fichierConflit, original);
}

// Preserve the existing private Claude connector rather than install a competing public identity.
var privateRoot = Path.Combine(maison, ".claude", "plugins", "cache", "nexus", "nexus-keypad", "2.1.0");
Directory.CreateDirectory(Path.Combine(privateRoot, ".claude-plugin"));
Directory.CreateDirectory(Path.Combine(privateRoot, "hooks"));
File.WriteAllText(Path.Combine(privateRoot, ".claude-plugin", "plugin.json"), "{\"name\":\"nexus-keypad\",\"version\":\"2.1.0\"}");
File.WriteAllText(Path.Combine(privateRoot, "hooks", "hooks.json"), "{\"hooks\":{}}");
Directory.CreateDirectory(Path.Combine(privateRoot, "skills", "nexus-keypad"));
File.Copy(Path.Combine(source, "connecteurs", "claude", "skills", "nexus-keypad", "SKILL.md"), Path.Combine(privateRoot, "skills", "nexus-keypad", "SKILL.md"));
var privateSettingsPath = Path.Combine(maison, ".claude", "settings.json");
File.WriteAllText(privateSettingsPath, "{\"other\":true,\"enabledPlugins\":{\"nexus-keypad@nexus\":false,\"third-party@other\":true}}");
var installed = new JsonObject { ["version"] = 2, ["plugins"] = new JsonObject { ["nexus-keypad@nexus"] = new JsonArray(new JsonObject { ["scope"] = "user", ["installPath"] = privateRoot }) } };
File.WriteAllText(Path.Combine(maison, ".claude", "plugins", "installed_plugins.json"), installed.ToJsonString());
Vrai(Connecteurs.Activer(0).Etat == EtatConnexion.Pret, "private Claude connector activated");
var privateSettings = JsonNode.Parse(File.ReadAllText(privateSettingsPath));
Vrai(privateSettings["enabledPlugins"]["nexus-keypad@nexus"].GetValue<Boolean>() && privateSettings["enabledPlugins"]["nexus-keypad@nexuskeypad"] is null, "private Claude identity preserved without duplicate");
Vrai(privateSettings["other"].GetValue<Boolean>() && privateSettings["enabledPlugins"]["third-party@other"].GetValue<Boolean>(), "private Claude third parties preserved");
var avantPrive = VieillirPreparation(0);
Vrai(Connecteurs.Activer(0).PrepareLe == avantPrive, "private Claude identical enable preserves setup date");
File.WriteAllText(Path.Combine(privateRoot, ".claude-plugin", "plugin.json"), "{\"name\":\"nexus-keypad\",\"version\":\"2.1.1\"}");
var priveActualise = Connecteurs.Activer(0);
Vrai(priveActualise.Etat == EtatConnexion.Pret && priveActualise.PrepareLe > avantPrive, "private Claude package change starts a new observation interval");

var codexConfig = Path.Combine(maison, ".codex", "config.toml");
File.WriteAllText(codexConfig, "[broken =");
var before = File.ReadAllText(codexConfig);
Vrai(Connecteurs.Activer(1).Etat == EtatConnexion.Erreur, "malformed TOML fails");
Vrai(File.ReadAllText(codexConfig) == before, "malformed TOML profile unchanged");
var hooksAgy = Path.Combine(maison, ".gemini", "config", "hooks.json");
File.WriteAllText(hooksAgy, "{broken");
Vrai(Connecteurs.Activer(2).Etat == EtatConnexion.Erreur && File.ReadAllText(hooksAgy) == "{broken", "malformed AGY preserved");

var rollbackA = Path.Combine(local, "NexusKeypad", "rollback-a.json");
var rollbackB = Path.Combine(local, "NexusKeypad", "rollback-b.json");
File.WriteAllText(rollbackA, "original bytes");
var transaction = new Connecteurs.Transaction();
transaction.Poser(rollbackA, System.Text.Encoding.UTF8.GetBytes("new bytes"));
transaction.Poser(rollbackB, System.Text.Encoding.UTF8.GetBytes("created bytes"));
try { transaction.Executer(() => false); echecs.Add("rollback must fail verification"); } catch (IOException) { }
Vrai(File.ReadAllText(rollbackA) == "original bytes" && !File.Exists(rollbackB), "failed verification restores all original bytes");
var drift = new Connecteurs.Transaction();
drift.Poser(rollbackA, System.Text.Encoding.UTF8.GetBytes("planned bytes"));
File.WriteAllText(rollbackA, "outside writer");
try { drift.Executer(() => true); echecs.Add("profile drift must abort"); } catch (IOException) { }
Vrai(File.ReadAllText(rollbackA) == "outside writer", "concurrent profile writer preserved");
Console.WriteLine(echecs.Count == 0 ? "Connecteurs: all isolated checks passed." : String.Join("\n", echecs));
return echecs.Count == 0 ? 0 : 1;

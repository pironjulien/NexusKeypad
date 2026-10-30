using System.Text.Json;
using Loupedeck.TrinitePlugin;
using System.Diagnostics;
using System.Runtime.InteropServices;

// Fixture headless opt-in : aucune application de Julien. Le parent attend son rattachement au job de test.
if (args is ["--lifetime-parent", var gate, var oracle, var stop, var receipt])
{
    var limit = DateTime.UtcNow.AddSeconds(10);
    while (!File.Exists(gate) && DateTime.UtcNow < limit) Thread.Sleep(50);
    if (!File.Exists(gate)) return 3;
    var info = new ProcessStartInfo(Environment.ProcessPath)
    {
        Arguments = UserApplication.ArgumentString("--lifetime-child", oracle, stop, "a b", "", "quote\"value", @"end\", "é & % $(literal)"),
        WorkingDirectory = AppContext.BaseDirectory,
    };
    var launched = Lanceurs.LancerUtilisateur(info, 0);
    File.WriteAllText(receipt, JsonSerializer.Serialize(new { launched }));
    Thread.Sleep(Timeout.Infinite);
    return 0;
}
if (args is ["--lifetime-child", var childOracle, var childStop, .. var values])
{
    File.WriteAllText(childOracle, JsonSerializer.Serialize(new { pid = Environment.ProcessId, executable = Environment.ProcessPath, values }));
    var limit = DateTime.UtcNow.AddSeconds(30);
    while (!File.Exists(childStop) && DateTime.UtcNow < limit) Thread.Sleep(50);
    return 0;
}

// Diagnostic opt-in, en lecture seule, de l'application officielle Antigravity : aucun démarrage/arrêt, aucune saisie.
if (args.SequenceEqual(new[] { "--probe-antigravity" }))
{
    var preparation = Lanceurs.Preparer(2);
    Console.WriteLine(JsonSerializer.Serialize(new { state = preparation.Etat.ToString(), preparation.Detail }));
    return preparation.Etat == EtatLancement.Pret ? 0 : 1;
}

var echecs = new List<String>();
var controles = 0;
void Vrai(Boolean condition, String regle)
{
    controles++;
    if (!condition) { echecs.Add(regle); }
}

// Aucun appel aux chemins réels de démarrage, aux processus du poste ou à son réseau.
foreach (var etat in new[] { EtatLancement.Relancer, EtatLancement.Absent, EtatLancement.Erreur })
{
    var appels = 0;
    var resultat = Lanceurs.ExecuterDemarrage(new(etat, "test"), () => { appels++; return true; }, () => true, _ => { });
    Vrai(!resultat && appels == 0, $"{etat} : aucun lancement");
}

Vrai(Lanceurs.ExecuterDemarrage(new(EtatLancement.Pret, "test"), () => throw new Exception("unexpected launch"),
    () => throw new Exception("unexpected probe"), _ => { }), "application prête : rien à lancer");
var lancement = 0;
var lectures = 0;
Vrai(Lanceurs.ExecuterDemarrage(new(EtatLancement.Fermee, "test"), () => { lancement++; return true; },
    () => ++lectures == 4, _ => { }) && lancement == 1 && lectures == 4, "démarrage unique puis preuve retardée");
lancement = 0;
Vrai(!Lanceurs.ExecuterDemarrage(new(EtatLancement.Fermee, "test"), () => { lancement++; return true; },
    () => false, _ => { }) && lancement == 1, "port muet : échec sans deuxième lancement");
Vrai(!Lanceurs.ExecuterDemarrage(new(EtatLancement.Fermee, "test"), () => false,
    () => throw new Exception("unexpected probe"), _ => { }), "lancement refusé : pas de preuve inventée");
lancement = 0;
lectures = 0;
Vrai(Lanceurs.ExecuterDemarrage(new(EtatLancement.Masquee, "test"), () => { lancement++; return true; },
    () => ++lectures == 3, _ => { }) && lancement == 1 && lectures == 3, "fenêtre masquée : réactivation unique puis page vérifiée");

String Page(String url, String socket, String type = "page") => JsonSerializer.Serialize(new[] { new { url, type, webSocketDebuggerUrl = socket } });
Vrai(Lanceurs.PageAttendue(1, 9223, Page("app://-/index.html", "ws://127.0.0.1:9223/devtools/page/1")), "page principale Codex reconnue");
Vrai(!Lanceurs.PageAttendue(1, 9223, Page("app://-/index.html?initialRoute=%2Favatar-overlay", "ws://127.0.0.1:9223/devtools/page/1")), "bulle Codex refusée");
Vrai(!Lanceurs.PageAttendue(1, 9223, Page("https://example.com", "ws://127.0.0.1:9223/devtools/page/1")), "navigateur quelconque refusé");
Vrai(!Lanceurs.PageAttendue(1, 9223, Page("app://-/index.html", "ws://example.com:9223/devtools/page/1")), "socket distant refusé");
Vrai(!Lanceurs.PageAttendue(1, 9223, Page("app://-/index.html", "ws://127.0.0.1:9227/devtools/page/1")), "autre port refusé");
Vrai(!Lanceurs.PageAttendue(1, 9223, Page("app://-/index.html", "ws://127.0.0.1:9223/devtools/page/1", "worker")), "worker refusé");
Vrai(Lanceurs.PageAttendue(2, 9227, Page("http://localhost:12345/", "ws://127.0.0.1:9227/devtools/page/1")), "page locale Antigravity reconnue");
Vrai(Lanceurs.PageAttendue(2, 9227, Page("https://127.0.0.1:56416/c/example", "ws://127.0.0.1:9227/devtools/page/1")), "page HTTPS locale Antigravity reconnue");
Vrai(!Lanceurs.PageAttendue(2, 9227, Page("data:text/html,waiting", "ws://127.0.0.1:9227/devtools/page/1")), "page provisoire Antigravity refusée");
Vrai(!Lanceurs.PageAttendue(2, 9227, Page("https://antigravity.google/", "ws://127.0.0.1:9227/devtools/page/1")), "site web Antigravity refusé");
Vrai(!Lanceurs.PageAttendue(2, 9227, "{}") && !Lanceurs.PageAttendue(2, 9227, "invalid"), "réponse inattendue refusée");
Vrai(Lanceurs.EtatPage(2, 9227, "[ ]", false).Etat == EtatLancement.Masquee, "port connu sans fenêtre ni cible : rouvrir sans relance");
Vrai(Lanceurs.EtatPage(2, 9227, "[ ]", true).Etat != EtatLancement.Masquee, "fenêtre visible sans cible : jamais prête ni simplement masquée");
Vrai(Lanceurs.EtatPage(2, 9227, "{}", false).Etat != EtatLancement.Masquee, "réponse non liste : jamais prise pour une fenêtre fermée");
Vrai(Lanceurs.EtatPage(2, 9227, "broken", false).Etat == EtatLancement.Erreur, "liste illisible : erreur de lecture sans faux diagnostic de relance");
Vrai(Lanceurs.EtatPage(2, 9227, Page("https://example.com", "ws://127.0.0.1:9227/devtools/page/1"), false).Etat == EtatLancement.Relancer,
    "page d'une autre origine : pas une simple fenêtre masquée");
Vrai(Lanceurs.EtatPage(2, 9227, Page("https://127.0.0.1:56416/", "ws://127.0.0.1:9223/devtools/page/1"), false).Etat == EtatLancement.Relancer,
    "socket d'un autre port : pas une connexion masquée valide");

var exe = @"C:\Program Files\Antigravity\Antigravity.exe";
var commande = Lanceurs.CommandeAntigravity(exe);
Vrai(commande.FileName == exe && commande.ArgumentList.Count == 0 && commande.Arguments ==
    "--remote-debugging-port=9227 --remote-debugging-address=127.0.0.1", "chemin à espaces isolé et port local");
var rappel = Lanceurs.CommandeAntigravity(exe, dejaOuverte: true);
Vrai(rappel.FileName == exe && rappel.Arguments.Length == 0 && rappel.ArgumentList.Count == 0, "application existante : activation sans nouveaux arguments de connexion");
var originaux = new[] { "", "a b", "quote\"value", @"end\", @"spaces\ and\", "é & % $(literal)", "line\nbreak" };
var natif = ArgumentsWindows.Decoder("fixture.exe " + UserApplication.ArgumentString(originaux));
Vrai(natif.Skip(1).SequenceEqual(originaux), "Le parseur Windows relit chaque argument exact, sans interprétation shell ni perte des barres finales.");
Vrai(Lanceurs.IdentiteAntigravity("Antigravity.exe", "Antigravity", "Google"), "identité officielle acceptée");
Vrai(!Lanceurs.IdentiteAntigravity("other.exe", "Antigravity", "Google")
    && !Lanceurs.IdentiteAntigravity("Antigravity.exe", "Other", "Google")
    && !Lanceurs.IdentiteAntigravity("Antigravity.exe", "Antigravity", "Other"), "homonymes non officiels refusés");

Console.WriteLine(echecs.Count == 0 ? $"Lanceurs : {controles} contrôles hors ligne réussis." : String.Join("\n", echecs));
return echecs.Count == 0 ? 0 : 1;

static class ArgumentsWindows
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CommandLineToArgvW(String line, out Int32 count);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
    public static String[] Decoder(String line)
    {
        var memory = CommandLineToArgvW(line, out var count);
        if (memory == IntPtr.Zero) throw new InvalidOperationException("Arguments Windows illisibles.");
        try { return Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(memory, i * IntPtr.Size))).ToArray(); }
        finally { LocalFree(memory); }
    }
}

namespace Loupedeck.TrinitePlugin
{
    public static class Cdp
    {
        public const Int32 PortCodex = 9223;
        public const Int32 PortAntigravity = 9227;
    }

    public static class PluginLog
    {
        public static void Warning(Exception erreur, String message) => throw new Exception("Les essais ne doivent pas lancer une application.");
    }
}

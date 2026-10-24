namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.Linq;
    using System.Net.Http;
    using System.Net.WebSockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;

    // Client minimal du port de débogage de Codex (9223) et d'Antigravity (9227), comme le relais : un Runtime.evaluate.
    public static class Cdp
    {
        public const Int32 PortCodex = 9223;
        public const Int32 PortAntigravity = 9227;

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(1.5) };

        // Bulle du chat vocal de Codex : page à part, qui porte « Arrêter le chat vocal » pendant un chat.
        public const String BulleVocaleCodex = "app://-/index.html?initialRoute=%2Favatar-overlay";

        // url : une page précise (sinon la page principale de l'appli).
        // attente : secondes au plus pour la réponse (un script qui ouvre une page et attend son rendu en prend plus).
        public static JsonElement? Evaluer(Int32 port, String js, String url = null, Int32 attente = 4)
        {
            try
            {
                using var session = Session.Ouvrir(port, url, attente);
                return session?.Envoyer("Runtime.evaluate", new { expression = js, returnByValue = true, awaitPromise = true }) is { } r
                    && r.TryGetProperty("result", out var res) && res.TryGetProperty("value", out var v) ? v.Clone() : null;
            }
            catch (Exception)
            {
                return null; // appli fermée ou port muet : la touche garde la dernière liste
            }
        }

        // Clic de souris « de confiance » (Input.dispatchMouseEvent) au point (x, y) de la page principale, appli devant ou
        // non : un menu d'Antigravity ne se referme pas sur un clic simulé en JavaScript. Vrai si le clic est parti.
        public static Boolean Cliquer(Int32 port, Double x, Double y)
        {
            try
            {
                using var session = Session.Ouvrir(port, null, 2);
                if (session is null)
                {
                    return false;
                }

                foreach (var type in new[] { "mousePressed", "mouseReleased" })
                {
                    session.Envoyer("Input.dispatchMouseEvent", new { type, x, y, button = "left", clickCount = 1 });
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // La page d'adresse url est-elle ouverte ? Null : port muet (appli fermée ou occupée).
        public static Boolean? PageOuverte(Int32 port, String url)
        {
            try
            {
                using var liste = JsonDocument.Parse(Http.GetStringAsync($"http://127.0.0.1:{port}/json/list").GetAwaiter().GetResult());
                return liste.RootElement.EnumerateArray().Any(t => t.GetProperty("type").GetString() == "page" && t.GetProperty("url").GetString() == url);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Une liaison avec une page : commandes numérotées, réponse attendue pour chacune.
        private sealed class Session : IDisposable
        {
            private readonly ClientWebSocket _ws = new();
            private readonly CancellationTokenSource _annulation;
            private Int32 _numero;

            private Session(Int32 attente) => this._annulation = new CancellationTokenSource(TimeSpan.FromSeconds(attente));

            public static Session Ouvrir(Int32 port, String url, Int32 attente)
            {
                using var liste = JsonDocument.Parse(Http.GetStringAsync($"http://127.0.0.1:{port}/json/list").GetAwaiter().GetResult());
                // Page principale : Codex sert app://-/index.html ; Antigravity garde parfois une page d'attente data:
                var page = liste.RootElement.EnumerateArray().FirstOrDefault(t =>
                    t.GetProperty("type").GetString() == "page"
                    && (url is not null ? t.GetProperty("url").GetString() == url
                        : port == PortCodex ? t.GetProperty("url").GetString() == "app://-/index.html"
                        : !t.GetProperty("url").GetString().StartsWith("data:", StringComparison.Ordinal)));
                if (page.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                var session = new Session(attente);
                try
                {
                    session._ws.ConnectAsync(new Uri(page.GetProperty("webSocketDebuggerUrl").GetString()), session._annulation.Token).GetAwaiter().GetResult();
                    return session;
                }
                catch (Exception)
                {
                    session.Dispose();
                    throw;
                }
            }

            // Envoie une commande et rend son « result » (null si la réponse n'en a pas).
            public JsonElement? Envoyer(String methode, Object parametres)
            {
                var numero = ++this._numero;
                var requete = JsonSerializer.Serialize(new { id = numero, method = methode, @params = parametres });
                this._ws.SendAsync(Encoding.UTF8.GetBytes(requete), WebSocketMessageType.Text, true, this._annulation.Token).GetAwaiter().GetResult();
                var tampon = new Byte[1 << 16];
                while (true)
                {
                    using var message = new System.IO.MemoryStream();
                    WebSocketReceiveResult r;
                    do
                    {
                        r = this._ws.ReceiveAsync(tampon, this._annulation.Token).GetAwaiter().GetResult();
                        message.Write(tampon, 0, r.Count); // décodé une fois entier : un caractère peut être coupé en deux
                    }
                    while (!r.EndOfMessage);

                    using var reponse = JsonDocument.Parse(message.ToArray());
                    if (reponse.RootElement.TryGetProperty("id", out var id) && id.GetInt32() == numero)
                    {
                        return reponse.RootElement.TryGetProperty("result", out var res) ? res.Clone() : null;
                    }
                }
            }

            public void Dispose()
            {
                this._ws.Dispose();
                this._annulation.Dispose();
            }
        }
    }
}

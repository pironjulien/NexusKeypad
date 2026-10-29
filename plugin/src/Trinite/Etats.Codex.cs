namespace Loupedeck.TrinitePlugin
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    // Codex : journaux de session, chats du relais, quotas par Codex LB.
    public sealed partial class Observateur
    {
        // Codex : index des noms (session_index.jsonl) + journaux de session ; chats du relais seulement s'ils sont actifs.
        private IEnumerable<Conversation> Codex()
        {
            var index = Path.Combine(DossierCodex, "session_index.jsonl");
            if (File.Exists(index) && File.GetLastWriteTimeUtc(index) != this._indexCodex.Ecrit)
            {
                var noms = new Dictionary<String, String>();
                foreach (var ligne in Lignes(index, 512 * 1024))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(ligne);
                        noms[doc.RootElement.GetProperty("id").GetString()] = doc.RootElement.GetProperty("thread_name").GetString();
                    }
                    catch (Exception)
                    {
                        // ligne coupée
                    }
                }

                this._indexCodex = (File.GetLastWriteTimeUtc(index), noms);
            }

            foreach (var f in this.JournauxCodex())
            {
                {
                    var id = f.Name.Length > 42 ? f.Name[^42..^6] : null;
                    if (id is null || DateTime.UtcNow - f.LastWriteTimeUtc > Fenetre)
                    {
                        continue;
                    }

                    var entete = Debut(f.FullName, 3000);
                    if (entete.Contains("\"thread_source\":\"subagent\"", StringComparison.Ordinal))
                    {
                        continue; // sous-agent d'un chat : l'appli ne le montre pas à part
                    }

                    if (entete.Contains("\"originator\":\"codex_exec\"", StringComparison.Ordinal))
                    {
                        // `codex exec` lancé par un outil (sonde, script) : absent de la barre latérale de l'appli, son alerte
                        // « Chat Codex » clignotait sans jamais pouvoir s'ouvrir (25/10).
                        continue;
                    }

                    var relais = entete.Contains(@".nexus\\relay", StringComparison.OrdinalIgnoreCase);
                    var (etat, depuis) = this.EtatCodexGarde(f, relais, id);
                    if (relais && etat is not (Etat.Travaille or Etat.Attend or Etat.Autorisation) && DateTime.UtcNow - f.LastWriteTimeUtc > TimeSpan.FromHours(12))
                    {
                        continue; // un réveil du relais ne reste « actif » que 12 h après son dernier message
                    }

                    var vu = this.Vu(1, id, depuis);
                    var titre = relais ? "Relais · " + Sujet(f) : this._indexCodex.Noms.GetValueOrDefault(id) ?? "Chat Codex";
                    yield return new(1, id, titre, etat == Etat.Fini && (vu || relais) ? Etat.Repos : etat, depuis, f.LastWriteTimeUtc, vu, relais);
                }
            }
        }

        // Journaux de Codex écrits dans la fenêtre. Un chat repris reste dans le dossier de son jour de création : tous les
        // dossiers sont parcourus (quelques dizaines de fichiers), toutes les 10 s ; le dossier du jour, à chaque tour.
        private List<FileInfo> JournauxCodex()
        {
            var sessions = Path.Combine(DossierCodex, "sessions");
            if (!Directory.Exists(sessions))
            {
                return [];
            }

            if (DateTime.UtcNow - this._journauxCodex.Quand > TimeSpan.FromSeconds(10))
            {
                this._journauxCodex = (DateTime.UtcNow, new DirectoryInfo(sessions).EnumerateFiles("rollout-*.jsonl", SearchOption.AllDirectories)
                    .Where(f => DateTime.UtcNow - f.LastWriteTimeUtc <= Fenetre).Select(f => f.FullName).ToList());
            }

            var jour = Path.Combine(sessions, DateTime.Now.ToString("yyyy"), DateTime.Now.ToString("MM"), DateTime.Now.ToString("dd"));
            var noms = Directory.Exists(jour) ? this._journauxCodex.Chemins.Union(Directory.EnumerateFiles(jour, "rollout-*.jsonl")) : this._journauxCodex.Chemins;
            return noms.Select(n => new FileInfo(n)).Where(f => f.Exists).ToList();
        }

        private (DateTime Quand, List<String> Chemins) _journauxCodex = (DateTime.MinValue, []);

        // Sujet d'un réveil du relais : son texte cite read(id="…") ; le message <id>.<de>.<vers>.<sujet>.json le nomme.
        private String Sujet(FileInfo f)
        {
            if (this._sujets.TryGetValue(f.FullName, out var connu))
            {
                return connu;
            }

            var m = Regex.Match(Debut(f.FullName, 1 << 20), @"read\(id=\\?""([0-9A-Za-z-]+)");
            var messages = Path.Combine(Maison, ".nexus", "relay", "v1", "messages");
            var fichier = m.Success && Directory.Exists(messages) ? Directory.EnumerateFiles(messages, m.Groups[1].Value + ".*.json").FirstOrDefault() : null;
            var morceaux = fichier is null ? [] : Path.GetFileNameWithoutExtension(fichier).Split('.');
            var sujet = morceaux.Length >= 4 ? String.Join('.', morceaux[3..]) : "Nexus Relay";
            if (m.Success)
            {
                this._sujets[f.FullName] = sujet; // le sujet d'un chat ne change plus
            }

            return sujet;
        }

        // Quota de Codex : celui de Codex LB, cumulé sur tous ses comptes (/api/usage/summary) ; sans LB, le dernier
        // « token_count » des journaux, qui ne reflète que le compte qui a servi la dernière réponse.
        private Quota QuotaCodex()
        {
            try
            {
                using var doc = JsonDocument.Parse(CodexLb.GetStringAsync("http://127.0.0.1:2455/api/usage/summary").GetAwaiter().GetResult());
                Double? Part(String fenetre) =>
                    doc.RootElement.TryGetProperty(fenetre, out var f) && f.ValueKind == JsonValueKind.Object
                    && f.TryGetProperty("remainingPercent", out var r) && r.ValueKind == JsonValueKind.Number
                        ? 1 - (r.GetDouble() / 100)
                        : null;
                return new(Part("primaryWindow"), Part("secondaryWindow"));
            }
            catch (Exception)
            {
                return this.QuotaCodexJournaux(); // Codex LB arrêté
            }
        }

        // Sans Codex LB (Codex tel qu'installé) : les journaux les plus récents, chats archivés compris (l'archivage les sort
        // de sessions\) et sans limite d'âge (une semaine entamée vaut encore ; une fenêtre passée se lit à 0 %).
        private Quota QuotaCodexJournaux()
        {
            var fichiers = new[] { "sessions", "archived_sessions" }.Select(d => Path.Combine(DossierCodex, d)).Where(Directory.Exists)
                .SelectMany(d => new DirectoryInfo(d).EnumerateFiles("rollout-*.jsonl", SearchOption.AllDirectories))
                .OrderByDescending(f => f.LastWriteTimeUtc).Take(6).Select(f => f.FullName);
            (DateTime Quand, JsonElement Limites)? meilleur = null;
            foreach (var f in fichiers)
            {
                var ligne = Lignes(f, 256 * 1024).Reverse().FirstOrDefault(l => l.Contains("\"token_count\"", StringComparison.Ordinal)
                    && l.Contains("\"rate_limits\":{", StringComparison.Ordinal)); // rate_limits peut valoir null
                if (ligne is null || Horodatage(ligne) is not DateTime quand || (meilleur is { } m && m.Quand >= quand))
                {
                    continue;
                }

                try
                {
                    using var doc = JsonDocument.Parse(ligne);
                    meilleur = (quand, doc.RootElement.GetProperty("payload").GetProperty("rate_limits").Clone());
                }
                catch (Exception)
                {
                    // ligne d'un autre format
                }
            }

            if (meilleur is not { } lu)
            {
                return null;
            }

            // Chaque fenêtre reconnue à sa durée (window_minutes) : « primary » n'est pas toujours celle de 5 h (vue seule, d'une
            // semaine, 25/10). Sans part lue : inconnue, jamais 0 %.
            static Double? Fenetre(JsonElement limites, String nom, Int32 minutes, Boolean seule)
            {
                if (!limites.TryGetProperty(nom, out var f) || f.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                var duree = f.TryGetProperty("window_minutes", out var w) && w.ValueKind == JsonValueKind.Number ? w.GetInt32() : (Int32?)null;
                if (duree is { } d ? d != minutes : !seule)
                {
                    return null;
                }

                var remise = f.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.Number ? DateTimeOffset.FromUnixTimeSeconds(r.GetInt64()).UtcDateTime : (DateTime?)null;
                var part = f.TryGetProperty("used_percent", out var u) && u.ValueKind == JsonValueKind.Number ? u.GetDouble() / 100 : (Double?)null;
                return part is null ? null : remise < DateTime.UtcNow ? 0 : part; // fenêtre passée depuis : remise à zéro
            }

            Double? Duree(Int32 minutes, String parDefaut) =>
                Fenetre(lu.Limites, "primary", minutes, parDefaut == "primary") ?? Fenetre(lu.Limites, "secondary", minutes, parDefaut == "secondary");

            return new(Duree(300, "primary"), Duree(10080, "secondary"));
        }

        // État d'un chat Codex, suivi pas à pas dans son journal : seules les lignes ajoutées depuis la lecture précédente sont
        // lues (un tour dépasse souvent 256 Ko : 57 sur 92 le 24/10, jusqu'à 20 Mo ; lire seulement la fin le donnait au
        // repos en plein travail). Le temps fait aussi passer au repos : travail sans écriture depuis 2 h (Codex fermé en
        // plein tour), fin ou question de plus de 12 h.
        private (Etat, DateTime) EtatCodexGarde(FileInfo f, Boolean relais, String session)
        {
            this._etatsCodex.TryGetValue(f.FullName, out var suivi);
            if (suivi is null || f.Length < suivi.Position)
            {
                suivi = new SuiviCodex(); // nouveau, ou journal réécrit : relu depuis le début
                this._etatsCodex[f.FullName] = suivi;
            }

            // Windows met la date d'écriture à jour en retard (vu 27 min) : la croissance du fichier fait foi (sa taille, pas
            // la position lue, qui s'arrête avant une ligne inachevée).
            if (f.Length != suivi.Taille)
            {
                suivi.Grandi = suivi.Taille == 0 ? f.LastWriteTimeUtc : DateTime.UtcNow;
                suivi.Taille = f.Length;
            }

            if (f.Length > suivi.Position)
            {
                try
                {
                    // Par morceaux de 1 Mo : un journal fait jusqu'à 80 Mo (premier passage après un redémarrage).
                    using var flux = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    flux.Position = suivi.Position;
                    var octets = new Byte[1 << 20];
                    var garde = 0; // début d'une ligne coupée par la fin du morceau précédent
                    Int32 lus;
                    while ((lus = flux.Read(octets, garde, octets.Length - garde)) > 0)
                    {
                        var plein = garde + lus;
                        var fin = Array.LastIndexOf(octets, (Byte)'\n', plein - 1); // ligne en cours d'écriture : relue ensuite
                        if (fin < 0)
                        {
                            if (plein == octets.Length)
                            {
                                Array.Resize(ref octets, octets.Length * 2); // ligne de plus de 1 Mo
                            }

                            garde = plein;
                            continue;
                        }

                        foreach (var ligne in Encoding.UTF8.GetString(octets, 0, fin).Split('\n'))
                        {
                            suivi.Lire(ligne, f.LastWriteTimeUtc);
                        }

                        suivi.Position += fin + 1;
                        garde = plein - (fin + 1);
                        Array.Copy(octets, fin + 1, octets, 0, garde);
                    }
                }
                catch (IOException)
                {
                    // journal pris : relu au tour suivant
                }
            }

            var hook = LireHookCodex(session);
            var (etat, depuis) = suivi.Effectif(relais, DateTime.UtcNow, hook);
            var silence = DateTime.UtcNow - suivi.Grandi;
            var age = DateTime.UtcNow - depuis;
            return etat switch
            {
                Etat.Travaille when silence > TimeSpan.FromHours(2) => (Etat.Repos, depuis),
                Etat.Fini or Etat.Attend or Etat.Autorisation when age > TimeSpan.FromHours(12) => (Etat.Repos, depuis),
                _ => (etat, depuis),
            };
        }

        private readonly Dictionary<String, SuiviCodex> _etatsCodex = [];

        // Le paquet de hooks garde une preuve d'origine : les événements des enfants ont le même session_id que
        // leur parent. Un ancien fichier générique ou une origine ambiguë ne prend donc jamais le pas sur le journal.
        private static JsonElement? LireHookCodex(String session)
        {
            try
            {
                var fichier = Path.Combine(DossierEtats, "codex", session + ".json");
                using var flux = new FileStream(fichier, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var doc = JsonDocument.Parse(flux);
                return doc.RootElement.Clone();
            }
            catch (Exception)
            {
                return null; // absent, non approuvé, illisible ou en cours de remplacement : journaux seuls
            }
        }

        // État d'un journal Codex lu d'un bloc, sans l'usure du temps (vérificateur).
        public static (Etat, DateTime) EtatJournalCodex(IEnumerable<String> lignes, Boolean relais, DateTime maintenant)
        {
            var suivi = new SuiviCodex();
            foreach (var ligne in lignes)
            {
                suivi.Lire(ligne, DateTime.MinValue);
            }

            return suivi.Effectif(relais, maintenant);
        }

        // Même lecteur et même fusion que le pad, sans lire ni écrire le profil Windows (vérificateur).
        public static (Etat, DateTime) EtatJournalCodexAvecHook(IEnumerable<String> lignes, Boolean relais, DateTime maintenant, String hook)
        {
            var suivi = new SuiviCodex();
            foreach (var ligne in lignes) { suivi.Lire(ligne, DateTime.MinValue); }
            try
            {
                using var doc = JsonDocument.Parse(hook);
                return suivi.Effectif(relais, maintenant, doc.RootElement);
            }
            catch (Exception)
            {
                return suivi.Effectif(relais, maintenant);
            }
        }

        private sealed partial class SuiviCodex
        {
            public Int64 Position;
            public Int64 Taille;
            public DateTime Grandi;
            public Etat Etat = Etat.Repos;
            public DateTime Depuis = DateTime.MinValue;
            private Boolean _accord; // Attend un accord pendant le tour (pas une question de fin)
            private DateTime _debutTour = DateTime.MinValue;
            private String _tour;
            private DateTime _dernierEvenement = DateTime.MinValue;
            private Boolean _tourTermine;

            // Questions posées par request_user_input_async (call_id → heure) : Codex continue son tour sans attendre, la
            // réponse arrive plus tard, souvent après la fin du tour, dans un message de l'utilisateur
            // <send_user_message_question_reply> dont chaque questionItemId nomme un call_id répondu. Un message ordinaire
            // n'en ferme aucune : l'appli garde la carte tant qu'elle n'a pas sa réponse (relecture de Codex, 27/10).
            // Clé : call_id et rang de la question dans l'appel (un appel peut en poser plusieurs, l'appli permet d'en
            // répondre une seule à la fois).
            private readonly Dictionary<(String Appel, Int32 Rang), DateTime> _questions = [];

            // Une question async ne bloque pas le travail. Après la fin du tour, ses questions restent visibles,
            // pas celles d'un tour précédent.
            // Les anciennes cartes restent dans Codex et peuvent recevoir une réponse tardive ; elles ne bloquent plus
            // l'état d'une nouvelle demande. Un tour interrompu reste au repos, sans fermer ses cartes.
            // Chats du relais : jamais de question à l'utilisateur.
            public (Etat, DateTime) Effectif(Boolean relais, DateTime maintenant, JsonElement? hook = null)
            {
                var (etat, depuis) = this.FusionnerHook(hook, maintenant);
                var ouvertes = this._questions.Values.Where(q => q >= this._debutTour && maintenant - q <= TimeSpan.FromHours(12)).ToList();
                return !relais && ouvertes.Count > 0 && etat is not (Etat.Autorisation or Etat.Travaille or Etat.Repos)
                    ? (Etat.Attend, ouvertes.Max())
                    : (etat, depuis);
            }

            private (Etat, DateTime) FusionnerHook(JsonElement? hook, DateTime maintenant)
            {
                var journal = (this.Etat, this.Depuis);
                try
                {
                    // Les fichiers de journal conservent les questions terminales et l'annulation ; un hook en retard
                    // ne ressuscite ni ce tour ni un ancien tour. Sans identité du tour, le journal reste le secours.
                    if (this._tourTermine || String.IsNullOrEmpty(this._tour) || hook is not { ValueKind: JsonValueKind.Object } h
                        || h.GetProperty("schema").GetInt32() != 1 || h.GetProperty("scope").GetString() != "main"
                        || h.GetProperty("turn_id").GetString() != this._tour)
                    {
                        return journal;
                    }

                    var quand = DateTimeOffset.FromUnixTimeMilliseconds((Int64)(h.GetProperty("at").GetDouble() * 1000)).UtcDateTime;
                    if (quand < this._dernierEvenement || quand > maintenant) { return journal; }
                    var evenement = h.GetProperty("event").GetString();
                    if (evenement is not ("UserPromptSubmit" or "PreToolUse" or "PostToolUse" or "PermissionRequest"
                        or "SubagentStart" or "SubagentStop" or "Stop" or "Interrupt" or "SessionStart" or "SessionEnd"))
                    {
                        return journal;
                    }

                    var age = maintenant - quand;
                    Etat? etat = h.GetProperty("state").GetString() switch
                    {
                        "run" when age < TimeSpan.FromHours(2) => Etat.Travaille,
                        "perm" when age < TimeSpan.FromHours(12) => Etat.Autorisation,
                        "wait" when age < TimeSpan.FromHours(12) => Etat.Attend,
                        "done" when age < TimeSpan.FromHours(12) => Etat.Fini,
                        "repos" or "end" => Etat.Repos,
                        _ => null,
                    };
                    return etat is { } connu ? (connu, quand) : journal;
                }
                catch (Exception)
                {
                    return journal; // schéma ancien, champ manquant ou type imprévu : aucune supposition
                }
            }

            // Signes que le travail a repris après un accord.
            private static readonly String[] Reprise = ["\"type\":\"exec_command_begin\"", "\"type\":\"patch_apply_begin\"",
                "\"type\":\"mcp_tool_call_begin\"", "\"type\":\"agent_message\"", "\"type\":\"agent_reasoning\""];

            public void Lire(String ligne, DateTime parDefaut)
            {
                var quand = Horodatage(ligne) ?? parDefaut;
                if (quand > this._dernierEvenement) { this._dernierEvenement = quand; }
                if (ligne.Contains("\"response_item\"", StringComparison.Ordinal))
                {
                    this.LireQuestion(ligne, parDefaut);
                    return;
                }

                if (!ligne.Contains("\"event_msg\"", StringComparison.Ordinal))
                {
                    return;
                }

                if (ligne.Contains("\"type\":\"task_started\"", StringComparison.Ordinal))
                {
                    this._debutTour = quand;
                    this._tour = Champ(ligne, "turn_id");
                    this._tourTermine = false;
                    (this.Etat, this.Depuis, this._accord) = (Etat.Travaille, quand, false);
                }
                else if (ligne.Contains("_approval_request\"", StringComparison.Ordinal))
                {
                    (this.Etat, this.Depuis, this._accord) = (Etat.Autorisation, quand, true); // une commande ou un fichier à accorder
                }
                else if (ligne.Contains("\"type\":\"request_user_input\"", StringComparison.Ordinal))
                {
                    (this.Etat, this.Depuis, this._accord) = (Etat.Attend, quand, true);
                }
                else if (ligne.Contains("\"type\":\"turn_aborted\"", StringComparison.Ordinal))
                {
                    this._tourTermine = true;
                    // Tour interrompu : ni réussite ni question, il retourne au repos (une annulation affichait la coche de
                    // fin, relecture de Codex, 25/10).
                    (this.Etat, this.Depuis, this._accord) = (Etat.Repos, quand, false);
                }
                else if (ligne.Contains("\"type\":\"task_complete\"", StringComparison.Ordinal))
                {
                    this._tourTermine = true;
                    // Un tour qui finit sur une question attend l'utilisateur.
                    var question = Regex.Match(ligne, "\"last_agent_message\":\"((?:[^\"\\\\]|\\\\.)*)\"");
                    var texte = question.Success ? ChaineJson(question.Groups[1].Value).TrimEnd('*', '_', ' ', ')', '»', '"', '\n') : "";
                    (this.Etat, this.Depuis, this._accord) = (texte.EndsWith('?') ? Etat.Attend : Etat.Fini, quand, false);
                }
                else if (this._accord && this.Etat is Etat.Attend or Etat.Autorisation && quand >= this.Depuis
                    && Reprise.Any(r => ligne.Contains(r, StringComparison.Ordinal)))
                {
                    // Accord donné pendant le tour : le travail a repris (commande lancée, réponse…).
                    (this.Etat, this._accord) = (Etat.Travaille, false);
                }
            }

            private void LireQuestion(String ligne, DateTime parDefaut)
            {
                if (ligne.Contains("\"type\":\"function_call\"", StringComparison.Ordinal)
                    && ligne.Contains("\"name\":\"request_user_input_async\"", StringComparison.Ordinal)
                    && AppelId().Match(ligne) is { Success: true } appel)
                {
                    var quand = Horodatage(ligne) ?? parDefaut;
                    for (var rang = 0; rang < NombreQuestions(ligne); rang++)
                    {
                        this._questions[(appel.Groups[1].Value, rang)] = quand;
                    }
                }
                else if (this._questions.Count > 0 && ligne.Contains(MarqueReponse, StringComparison.Ordinal))
                {
                    foreach (var (repondu, rang) in Repondues(ligne))
                    {
                        // Sans rang lu : tout l'appel.
                        foreach (var cle in this._questions.Keys.Where(k => k.Appel == repondu && (rang is null || k.Rang == rang)).ToList())
                        {
                            this._questions.Remove(cle);
                        }
                    }
                }
            }

            // Nombre de questions d'un appel (arguments.questions) ; 1 si illisible.
            private static Int32 NombreQuestions(String ligne)
            {
                try
                {
                    using var doc = JsonDocument.Parse(ligne);
                    using var arguments = JsonDocument.Parse(doc.RootElement.GetProperty("payload").GetProperty("arguments").GetString());
                    return arguments.RootElement.TryGetProperty("questions", out var q) && q.ValueKind == JsonValueKind.Array
                        ? Math.Max(1, q.GetArrayLength()) : 1;
                }
                catch (Exception)
                {
                    return 1;
                }
            }

            private const String MarqueReponse = "<send_user_message_question_reply>";

            // call_id répondus par une ligne : seulement un message de l'utilisateur dont le texte commence par la balise
            // de réponse, et seulement les questionItemId de son tableau JSON (["request_user_input_async", call_id, rang]).
            // Une citation ailleurs (sortie d'outil, message de l'assistant, journal relu) ne ferme rien.
            private static List<(String Appel, Int32? Rang)> Repondues(String ligne)
            {
                var appels = new List<(String, Int32?)>();
                try
                {
                    using var doc = JsonDocument.Parse(ligne);
                    if (!doc.RootElement.TryGetProperty("payload", out var p) || p.ValueKind != JsonValueKind.Object
                        || !p.TryGetProperty("type", out var t) || t.GetString() != "message"
                        || !p.TryGetProperty("role", out var r) || r.GetString() != "user"
                        || !p.TryGetProperty("content", out var contenu) || contenu.ValueKind != JsonValueKind.Array)
                    {
                        return appels;
                    }

                    foreach (var c in contenu.EnumerateArray())
                    {
                        var texte = c.ValueKind == JsonValueKind.Object && c.TryGetProperty("text", out var x) && x.ValueKind == JsonValueKind.String
                            ? x.GetString().TrimStart() : "";
                        if (!texte.StartsWith(MarqueReponse, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var corps = texte[MarqueReponse.Length..];
                        var finBalise = corps.IndexOf("</send_user_message_question_reply>", StringComparison.Ordinal);
                        using var reponses = JsonDocument.Parse(finBalise >= 0 ? corps[..finBalise] : corps);
                        foreach (var item in reponses.RootElement.EnumerateArray())
                        {
                            if (item.TryGetProperty("questionItemId", out var id) && id.ValueKind == JsonValueKind.String)
                            {
                                using var cle = JsonDocument.Parse(id.GetString());
                                if (cle.RootElement.ValueKind == JsonValueKind.Array && cle.RootElement.GetArrayLength() >= 2
                                    && cle.RootElement[0].GetString() == "request_user_input_async" && cle.RootElement[1].GetString() is String appel)
                                {
                                    appels.Add((appel, cle.RootElement.GetArrayLength() >= 3 && cle.RootElement[2].ValueKind == JsonValueKind.Number
                                        ? cle.RootElement[2].GetInt32() : null));
                                }
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // ligne d'un autre format : rien de répondu
                }

                return appels;
            }

            [GeneratedRegex("\"call_id\":\"(call_[A-Za-z0-9]+)\"")]
            private static partial Regex AppelId();

        }
    }
}

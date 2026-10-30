using System.Text.Json;
using System.Text.RegularExpressions;
using Loupedeck.TrinitePlugin;
using static NexusKeypad.Tests.Commun;

namespace NexusKeypad.Tests;

// Antigravity navigation, offline: one bounded passive wait before a single gesture, never replayed.
public sealed class NavigationTests
{
    private Int64 _temps;
    private Int32 _gestes;
    private Int32 _lectures;

    private Boolean Ouvrir()
    {
        this._gestes++;
        return true;
    }

    private Boolean Executer(Func<String> lire, Func<Boolean> geste = null) =>
        NavigationAntigravity.Executer(lire, geste ?? this.Ouvrir, () => this._temps, ms => this._temps += ms);

    [Fact]
    public void Conversation_deja_affichee_aucun_geste() =>
        Vrai(this.Executer(() => "open") && this._gestes == 0, "Conversation déjà affichée : aucun clic ni rechargement.");

    [Fact]
    public void Chargement_retarde_un_geste_unique() =>
        Vrai(this.Executer(() => ++this._lectures < 4 ? null : this._gestes == 0 ? "ready" : "open") && this._gestes == 1,
            "Chargement retardé : lectures seules avant un geste unique et ID vérifié.");

    [Fact]
    public void Port_muet_delai_borne_sans_geste() =>
        Vrai(!this.Executer(() => null) && this._gestes == 0 && this._temps == 5000, "Port muet : délai borné sans geste.");

    [Fact]
    public void Transport_inconnu_jamais_de_second_clic() =>
        Vrai(!this.Executer(() => "ready", () => { this._gestes++; return false; }) && this._gestes == 1 && this._temps == 0,
            "Résultat de transport inconnu : jamais de deuxième clic.");

    [Fact]
    public void Geste_sans_changement_d_ID_jamais_rejoue() =>
        Vrai(!this.Executer(() => "ready") && this._gestes == 1 && this._temps == 4000,
            "Geste parti sans changement d'ID : échec borné, jamais rejoué.");

    [Fact]
    public void Lectures_lentes_comptees_dans_le_delai() =>
        Vrai(!this.Executer(() => { this._temps += 1000; return null; }) && this._gestes == 0 && this._temps <= 6000,
            "Lectures lentes : durée réelle incluse dans le délai.");

    [Fact]
    public void ID_encode_en_chaine_JSON()
    {
        var idEncode = Regex.Match(Js.OuvrirAntigravity("an\"id", true), "const id = (\"(?:\\\\.|[^\"])*\");").Groups[1].Value;
        Vrai(JsonSerializer.Deserialize<String>(idEncode) == "an\"id", "ID encodé comme une chaîne JSON, sans injection.");
    }
}

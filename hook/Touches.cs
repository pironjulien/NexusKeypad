// Place les 18 touches du plugin sur le profil général du MX Keypad (Loupedeck70, @_defaultwin), ou les retire : pour
// get.ps1 (sans Python). Page 1 : « Touche 1 » à « Touche 9 » ; page 2 :
// « Touche 10 » à « Touche 18 » (créée sur le gabarit de la page 1 si le profil n'en a qu'une). Les deux boutons du bas
// passent d'une page à l'autre. Le service Logi doit être arrêté (sinon il réécrit le profil) ; la sauvegarde est faite
// par get.ps1.
// Usage : nexuskeypad-hook keys <ProfileInfo.json> <plugin> [--remove]
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NexusKeypad.Hook;

public static class Touches
{
    private const string Controle = "Loupedeck.Service.Devices.Loupedeck7Devices.ProfileLayoutControl7, LoupedeckService";

    public static string Action(string plugin, int n) => $"${plugin}___Loupedeck.TrinitePlugin.ToucheCommand___t{n}";

    // Renvoie le nombre de touches placées (ou retirées) ; lève si le profil n'a pas la forme attendue.
    public static int Arranger(JsonObject profil, string plugin, bool retirer)
    {
        var espace = profil["layout"]?["layoutModes"]?[0]?["workspaces"]?[0] as JsonObject
            ?? throw new InvalidDataException("profil sans espace de travail");
        var pages = espace["pressPages"] as JsonArray ?? throw new InvalidDataException("profil sans pages");
        if (pages.Count == 0 || pages[0] is not JsonObject premiere)
        {
            throw new InvalidDataException("profil sans première page");
        }

        if (retirer)
        {
            var retirees = 0;
            foreach (var controle in pages.OfType<JsonObject>().SelectMany(p => (p["controls"] as JsonArray ?? []).OfType<JsonObject>()))
            {
                if (controle["pressAction"]?.GetValue<string>() is { } a && a.StartsWith($"${plugin}___", StringComparison.Ordinal))
                {
                    controle["pressAction"] = null;
                    retirees++;
                }
            }

            return retirees;
        }

        if (pages.Count == 1)
        {
            var seconde = premiere.DeepClone().AsObject();
            seconde["name"] = Guid.NewGuid().ToString("N").ToUpperInvariant();
            seconde["displayName"] = "Page (2)";
            pages.Add((JsonNode)seconde);
        }

        var placees = 0;
        for (var p = 0; p < 2; p++)
        {
            var page = pages[p]!.AsObject();
            var controles = page["controls"] as JsonArray;
            if (controles is null)
            {
                page["controls"] = controles = new JsonArray();
            }

            for (var c = 0; c < 9; c++)
            {
                var controle = controles.OfType<JsonObject>().FirstOrDefault(x => x["controlId"]?.GetValue<int>() == c);
                if (controle is null)
                {
                    controle = new JsonObject { ["$type"] = Controle, ["controlId"] = c, ["rotateAction"] = null };
                    controles.Add((JsonNode)controle);
                }

                controle["pressAction"] = Action(plugin, (p * 9) + c + 1);
                placees++;
            }
        }

        return placees;
    }

    public static int Executer(string fichier, string plugin, bool retirer)
    {
        try
        {
            var texte = File.ReadAllText(fichier, Encoding.UTF8).TrimStart('﻿');
            var profil = JsonNode.Parse(texte) as JsonObject ?? throw new InvalidDataException("profil illisible");
            var n = Arranger(profil, plugin, retirer);
            var temporaire = fichier + ".nexuskeypad.tmp";
            File.WriteAllText(temporaire, profil.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(temporaire, fichier, overwrite: true);
            Console.Out.WriteLine($"{n} keys {(retirer ? "removed" : "placed")}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"keys: {ex.Message}");
            return 1;
        }
    }
}

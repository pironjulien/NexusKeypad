using System.Security.Cryptography;
using System.Text.Json;
using Loupedeck.TrinitePlugin;
using static NexusKeypad.Tests.Commun;

namespace NexusKeypad.Tests;

// Update of the complete edition: versions compared, setting, state for the Nexus app, package checked (the release's
// SHA-256, expected plugin and version) and unpacked into a work folder. Temporary folders only.
public sealed class MiseAJourTests
{
    private static Boolean Refuse(Action a)
    {
        try
        {
            a();
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
    }

    [Fact]
    public void Versions_comparees_nombre_par_nombre() =>
        Vrai(MiseAJour.PlusRecente("1.10.0", "1.9.3") && MiseAJour.PlusRecente("v1.3.1", "1.3.0") && !MiseAJour.PlusRecente("1.3.0", "1.3.0")
            && !MiseAJour.PlusRecente("1.2.9", "1.3.0"), "mise à jour : versions comparées nombre par nombre");

    [Fact]
    public void Reglage_etat_paquet_signature_et_rechargement()
    {
        var dossier = Directory.CreateTempSubdirectory("nexus-maj").FullName;
        try
        {
            var reglages = Path.Combine(dossier, "reglages.json");
            Vrai(MiseAJour.Auto(reglages), "mise à jour : automatique sans réglage");
            File.WriteAllText(reglages, """{"miseAJourAuto": false}""");
            Vrai(!MiseAJour.Auto(reglages), "mise à jour : coupée par le réglage");
            var etat = Path.Combine(dossier, "status", "keypad.json");
            MiseAJour.Ecrire("1.3.0", "1.3.1", "idle", "update available", etat);
            var lu = JsonDocument.Parse(File.ReadAllText(etat)).RootElement;
            Vrai(lu.GetProperty("product").GetString() == "keypad" && lu.GetProperty("version").GetString() == "1.3.0" && lu.GetProperty("latest").GetString() == "1.3.1"
                && lu.GetProperty("state").GetString() == "idle" && lu.TryGetProperty("checked_at", out _), "mise à jour : état au contrat de l'appli Nexus");

            // Fake 1.3.1 package: bin/NexusKeypadPlugin.dll and metadata/LoupedeckPackage.yaml.
            var source = Path.Combine(dossier, "source");
            Directory.CreateDirectory(Path.Combine(source, "bin"));
            Directory.CreateDirectory(Path.Combine(source, "metadata"));
            File.WriteAllText(Path.Combine(source, "bin", "NexusKeypadPlugin.dll"), "dll");
            File.WriteAllText(Path.Combine(source, "metadata", "LoupedeckPackage.yaml"), "name: NexusKeypad\nversion: 1.3.1\n");
            var paquet = Path.Combine(dossier, MiseAJour.NomPaquet("1.3.1"));
            System.IO.Compression.ZipFile.CreateFromDirectory(source, paquet);
            var empreinte = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(paquet))).ToLowerInvariant();
            var sommes = $"{new String('0', 64)}  autre.lplug4\n{empreinte}  {MiseAJour.NomPaquet("1.3.1")}\n";
            var travail = Path.Combine(dossier, "maj");
            Directory.CreateDirectory(travail);
            var pret = MiseAJour.Preparer(paquet, sommes, "1.3.1", travail);
            Vrai(File.Exists(Path.Combine(pret, "bin", "NexusKeypadPlugin.dll")) && MiseAJour.Version(pret) == "1.3.1", "mise à jour : paquet contrôlé et déballé");
            Vrai(Refuse(() => MiseAJour.Preparer(paquet, sommes.Replace(empreinte, new String('1', 64)), "1.3.1", travail)), "mise à jour : empreinte différente refusée");
            Vrai(Refuse(() => MiseAJour.Preparer(paquet, "", "1.3.1", travail)), "mise à jour : paquet absent de SHA256SUMS refusé");
            Vrai(Refuse(() => MiseAJour.Preparer(paquet, sommes.Replace(MiseAJour.NomPaquet("1.3.1"), MiseAJour.NomPaquet("1.3.2")), "1.3.2", travail)), "mise à jour : autre version refusée");

            // SHA256SUMS.txt signed (test key): accepted; modified, signed by another key, unsigned: refused. The real
            // embedded key is read.
            using (var cle = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            using (var autreCle = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                var publique = new[] { Convert.ToBase64String(cle.ExportSubjectPublicKeyInfo()) };
                var contenu = System.Text.Encoding.UTF8.GetBytes(sommes);
                String Signe(ECDsa k, Byte[] c) => Convert.ToBase64String(k.SignData(c, HashAlgorithmName.SHA256));
                Vrai(MiseAJour.Sommes(contenu, Signe(cle, contenu) + "\n", publique) == sommes, "mise à jour : SHA256SUMS signé accepté");
                var modifie = System.Text.Encoding.UTF8.GetBytes(sommes.Replace(empreinte, new String('1', 64)));
                Vrai(Refuse(() => MiseAJour.Sommes(modifie, Signe(cle, contenu), publique)), "mise à jour : SHA256SUMS modifié refusé");
                Vrai(Refuse(() => MiseAJour.Sommes(contenu, Signe(autreCle, contenu), publique)), "mise à jour : signé par une autre clé refusé");
                Vrai(Refuse(() => MiseAJour.Sommes(contenu, "", publique)) && Refuse(() => MiseAJour.Sommes(contenu, "pas du base64 !", publique)), "mise à jour : sans signature refusé");
                Vrai(Refuse(() => MiseAJour.Sommes(contenu, Signe(cle, contenu))), "mise à jour : clé d'essai refusée par les clés embarquées");
            }

            // Reload after a swap: "done" of this version kept; otherwise "idle" with the latest known version.
            MiseAJour.Ecrire("1.3.1", "1.3.1", "done", "updated", etat);
            MiseAJour.Recharge("1.3.1", etat);
            Vrai(JsonDocument.Parse(File.ReadAllText(etat)).RootElement.GetProperty("state").GetString() == "done", "mise à jour : « done » gardé au rechargement");
            MiseAJour.Recharge("1.3.2", etat);
            lu = JsonDocument.Parse(File.ReadAllText(etat)).RootElement;
            Vrai(lu.GetProperty("state").GetString() == "idle" && lu.GetProperty("version").GetString() == "1.3.2" && lu.GetProperty("latest").GetString() == "1.3.1",
                "mise à jour : rechargement d'une autre version en « idle », dernière version gardée");
        }
        finally
        {
            Directory.Delete(dossier, true);
        }
    }
}

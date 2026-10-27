# Signe une release GitHub de NexusKeypad, hors ligne, après son workflow Release (qui la crée en brouillon), puis la publie :
# la mise à jour automatique de l'édition complète refuse toute release sans SHA256SUMS.txt.sig valide (MiseAJour.Sommes).
#
# Avant de signer : chaque fichier de la release est contrôlé contre SHA256SUMS.txt et contre son attestation de
# provenance (gh attestation verify : construit par le workflow Release de pironjulien/NexusKeypad). Puis SHA256SUMS.txt
# est signé par la clé privée de NexusSecure (ECDSA P-256, SHA-256, IEEE P1363 en base64), la signature relue avec la clé
# publique embarquée dans le plugin (Cles de MiseAJour.cs), envoyée, téléchargée de nouveau et revérifiée.
# La clé privée ne quitte jamais le poste (jamais de secret de CI) : un dépôt compromis ne peut pas signer.
#   pwsh -NoProfile -File outils\signer_release.ps1 -Tag v1.6.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidatePattern('^v\d+\.\d+\.\d+$')] [string]$Tag,
    [string]$Cle = (Join-Path $env:OneDrive 'Documents\NexusSecure\projects\nexuskeypad\release-signing-p256.pem')
)
$ErrorActionPreference = 'Stop'
$depot = 'pironjulien/NexusKeypad'
$travail = Join-Path $env:LOCALAPPDATA "NexusKeypad\signature\$Tag"

# Clés publiques du plugin, lues dans sa source : la signature doit passer là où elle sera contrôlée.
$source = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot '..\plugin\src\Trinite\MiseAJour.cs')
if ($source -notmatch 'String\[\] Cles = \[([^\]]+)\]') { throw 'Clés publiques introuvables dans MiseAJour.cs.' }
$publiques = [regex]::Matches($Matches[1], '"([A-Za-z0-9+/=]+)"') | ForEach-Object { $_.Groups[1].Value }

function Test-Signature([byte[]]$Contenu, [string]$Signature) {
    foreach ($p in $publiques) {
        $ec = [Security.Cryptography.ECDsa]::Create()
        try {
            $lus = 0
            $ec.ImportSubjectPublicKeyInfo([Convert]::FromBase64String($p), [ref]$lus)
            if ($ec.VerifyData($Contenu, [Convert]::FromBase64String($Signature.Trim()), [Security.Cryptography.HashAlgorithmName]::SHA256)) { return $true }
        } finally { $ec.Dispose() }
    }
    return $false
}

if (Test-Path $travail) { Remove-Item $travail -Recurse -Force }
New-Item -ItemType Directory -Force $travail | Out-Null
try {
    gh release download $Tag -R $depot -D $travail
    if ($LASTEXITCODE) { throw "Release $Tag introuvable." }
    $sommes = Join-Path $travail 'SHA256SUMS.txt'
    $lignes = @(Get-Content -LiteralPath $sommes | Where-Object { $_.Trim() })
    $fichiers = @(Get-ChildItem $travail -File | Where-Object Name -notin 'SHA256SUMS.txt', 'SHA256SUMS.txt.sig')
    if ($lignes.Count -ne $fichiers.Count) { throw "SHA256SUMS.txt liste $($lignes.Count) fichiers, la release en a $($fichiers.Count)." }
    foreach ($ligne in $lignes) {
        if ($ligne -notmatch '^([0-9a-f]{64})\s+\*?(\S+)$') { throw "Ligne illisible : $ligne" }
        $fichier = Join-Path $travail $Matches[2]
        if ((Get-FileHash -LiteralPath $fichier -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Matches[1]) { throw "Empreinte différente : $($Matches[2])" }
        gh attestation verify $fichier --repo $depot --signer-workflow "$depot/.github/workflows/release.yml" | Out-Null
        if ($LASTEXITCODE) { throw "Attestation de provenance refusée : $($Matches[2])" }
        Write-Host "contrôlé : $($Matches[2])"
    }

    $contenu = [IO.File]::ReadAllBytes($sommes)
    $ec = [Security.Cryptography.ECDsa]::Create()
    try {
        $ec.ImportFromPem((Get-Content -Raw -LiteralPath $Cle))
        $signature = [Convert]::ToBase64String($ec.SignData($contenu, [Security.Cryptography.HashAlgorithmName]::SHA256))
    } finally { $ec.Dispose() }
    if (-not (Test-Signature $contenu $signature)) { throw 'La clé de NexusSecure ne correspond à aucune clé publique du plugin.' }
    $sig = Join-Path $travail 'SHA256SUMS.txt.sig'
    [IO.File]::WriteAllText($sig, $signature + "`n", [Text.UTF8Encoding]::new($false))
    gh release upload $Tag $sig -R $depot --clobber
    if ($LASTEXITCODE) { throw 'Envoi de la signature refusé.' }

    # Relue telle que le plugin la verra.
    $relue = Join-Path $travail 'relue'
    gh release download $Tag -R $depot -p 'SHA256SUMS.txt*' -D $relue
    if ($LASTEXITCODE -or -not (Test-Signature ([IO.File]::ReadAllBytes((Join-Path $relue 'SHA256SUMS.txt'))) (Get-Content -Raw (Join-Path $relue 'SHA256SUMS.txt.sig')))) {
        throw 'Signature publiée invalide.'
    }
    # Le workflow Release crée la release en brouillon : publiée seulement maintenant, signée.
    if ((gh release view $Tag -R $depot --json isDraft -q .isDraft) -eq 'true') {
        gh release edit $Tag -R $depot --draft=false --latest | Out-Null
        if ($LASTEXITCODE) { throw 'Publication du brouillon refusée.' }
    }
    Write-Host "$Tag signée et publiée : SHA256SUMS.txt.sig vérifiée." -ForegroundColor Green
} finally {
    Remove-Item $travail -Recurse -Force -ErrorAction SilentlyContinue
}

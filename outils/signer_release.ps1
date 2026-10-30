# Signe une release GitHub de NexusKeypad, hors ligne, après son workflow Release (qui la crée en brouillon), puis la publie :
# la mise à jour automatique de l'édition complète refuse toute release sans SHA256SUMS.txt.sig valide (MiseAJour.Sommes).
#
# Avant de signer : chaque fichier de la release est contrôlé contre SHA256SUMS.txt et contre son attestation de
# provenance (gh attestation verify : construit par le workflow Release de pironjulien/NexusKeypad). Puis SHA256SUMS.txt
# est signé (ECDSA P-256, SHA-256, IEEE P1363 en base64) par la clé privée de NexusSecure (-Cle : une autre, pour un
# changement de clé). La signature doit passer avec les clés du plugin de cette release (Cles de MiseAJour.cs) ET avec
# celles de la release publiée avant, que les plugins en place utilisent pour la contrôler : un changement de clé ne coupe
# jamais leurs mises à jour. Puis elle est envoyée, téléchargée de nouveau et revérifiée.
# La clé privée ne quitte jamais le poste (jamais de secret de CI) : un dépôt compromis ne peut pas signer.
#   pwsh -NoProfile -File outils\signer_release.ps1 -Tag v1.6.3
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidatePattern('^v\d+\.\d+\.\d+$')] [string]$Tag,
    [string]$Cle = (Join-Path $env:OneDrive 'Documents\NexusSecure\projects\nexuskeypad\release-signing-p256.pem')
)
$ErrorActionPreference = 'Stop'
$depot = 'pironjulien/NexusKeypad'
$travail = Join-Path $HOME ".nexus\build\nexuskeypad\signature\$Tag"

# Clés publiques d'un MiseAJour.cs (Cles).
function Get-Cles([string]$Source) {
    if ($Source -notmatch 'String\[\] Cles =\s*\[([^\]]+)\]') { throw 'Clés publiques introuvables dans MiseAJour.cs.' }
    return @([regex]::Matches($Matches[1], '"([A-Za-z0-9+/=]+)"') | ForEach-Object { $_.Groups[1].Value })
}

# Celles du plugin de cette release et celles de la dernière release publiée (les plugins en place), lues sur GitHub.
function Get-ClesDe([string]$Ref) {
    $source = gh api -H 'Accept: application/vnd.github.raw' "repos/$depot/contents/plugin/src/Trinite/MiseAJour.cs?ref=$Ref" | Out-String
    if ($LASTEXITCODE) { throw "MiseAJour.cs introuvable à $Ref." }
    return Get-Cles $source
}
$nouvelles = Get-ClesDe $Tag
$avant = gh release view -R $depot --json tagName -q .tagName
if ($LASTEXITCODE) { throw 'Dernière release publiée introuvable.' }
$enPlace = @()
if ($avant -ne $Tag) {
    $enPlace = Get-ClesDe $avant
}

function Test-Signature([byte[]]$Contenu, [string]$Signature, [string[]]$Cles) {
    foreach ($p in $Cles) {
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
    # La provenance jointe (*.intoto.jsonl, bundle Sigstore de l'attestation) n'est pas dans SHA256SUMS.txt : elle est
    # contrôlée à part, chaque fichier devant être vérifié par elle aussi.
    $provenance = @(Get-ChildItem $travail -File -Filter '*.intoto.jsonl')
    if ($provenance.Count -ne 1) { throw "Provenance jointe attendue (un *.intoto.jsonl), trouvé $($provenance.Count)." }
    $fichiers = @(Get-ChildItem $travail -File | Where-Object { $_.Name -notin 'SHA256SUMS.txt', 'SHA256SUMS.txt.sig' -and $_.Name -notlike '*.intoto.jsonl' })
    # SHA256SUMS.txt doit nommer chaque fichier de la release une fois et une seule, ni plus ni moins (une ligne répétée
    # laissait un fichier sans contrôle ; relecture de Codex, 27/10).
    $listes = @(foreach ($ligne in $lignes) {
        if ($ligne -notmatch '^([0-9a-f]{64})\s+\*?(\S+)$') { throw "Ligne illisible : $ligne" }
        [pscustomobject]@{ Empreinte = $Matches[1]; Nom = $Matches[2] }
    })
    $noms = @($listes.Nom | Sort-Object -Unique)
    if ($noms.Count -ne $listes.Count) { throw 'SHA256SUMS.txt nomme un fichier plusieurs fois.' }
    if (Compare-Object $noms @($fichiers.Name | Sort-Object) -CaseSensitive) { throw 'SHA256SUMS.txt ne nomme pas exactement les fichiers de la release.' }
    # Provenance liée à CETTE release : construite par le workflow Release depuis l'étiquette $Tag, à son commit (un ancien
    # ensemble authentique du même workflow passait aussi).
    $commit = gh api "repos/$depot/commits/$Tag" -q .sha
    if ($LASTEXITCODE -or $commit -notmatch '^[0-9a-f]{40}$') { throw "Commit de $Tag introuvable." }
    $source = @('--repo', $depot, '--signer-workflow', "$depot/.github/workflows/release.yml", '--source-ref', "refs/tags/$Tag", '--source-digest', $commit)
    foreach ($l in @($listes) + [pscustomobject]@{ Empreinte = $null; Nom = 'SHA256SUMS.txt' }) {
        $fichier = Join-Path $travail $l.Nom
        if ($l.Empreinte -and (Get-FileHash -LiteralPath $fichier -Algorithm SHA256).Hash.ToLowerInvariant() -ne $l.Empreinte) { throw "Empreinte différente : $($l.Nom)" }
        gh attestation verify $fichier @source | Out-Null
        if ($LASTEXITCODE) { throw "Attestation de provenance refusée : $($l.Nom)" }
        gh attestation verify $fichier @source --bundle $provenance[0].FullName | Out-Null
        if ($LASTEXITCODE) { throw "Provenance jointe refusée pour : $($l.Nom)" }
        Write-Host "contrôlé : $($l.Nom)"
    }

    $contenu = [IO.File]::ReadAllBytes($sommes)
    $ec = [Security.Cryptography.ECDsa]::Create()
    try {
        $ec.ImportFromPem((Get-Content -Raw -LiteralPath $Cle))
        $signature = [Convert]::ToBase64String($ec.SignData($contenu, [Security.Cryptography.HashAlgorithmName]::SHA256))
    } finally { $ec.Dispose() }
    if (-not (Test-Signature $contenu $signature $nouvelles)) { throw "La clé de signature n'est pas dans Cles du plugin $Tag." }
    if ($enPlace -and -not (Test-Signature $contenu $signature $enPlace)) {
        throw "La clé de signature n'est pas connue des plugins en place ($avant) : signer avec une clé de leurs Cles (-Cle)."
    }
    $sig = Join-Path $travail 'SHA256SUMS.txt.sig'
    [IO.File]::WriteAllText($sig, $signature + "`n", [Text.UTF8Encoding]::new($false))
    gh release upload $Tag $sig -R $depot --clobber
    if ($LASTEXITCODE) { throw 'Envoi de la signature refusé.' }

    # Relue telle que le plugin la verra.
    $relue = Join-Path $travail 'relue'
    gh release download $Tag -R $depot -p 'SHA256SUMS.txt*' -D $relue
    if ($LASTEXITCODE -or -not (Test-Signature ([IO.File]::ReadAllBytes((Join-Path $relue 'SHA256SUMS.txt'))) (Get-Content -Raw (Join-Path $relue 'SHA256SUMS.txt.sig')) $nouvelles)) {
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

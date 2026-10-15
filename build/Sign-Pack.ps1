#requires -Version 7.0
<#
.SYNOPSIS
    Signe tous les fichiers signables d'un pack ADReplic avec Azure Artifact
    Signing (certificat Public Trust SCOPI), puis verifie chaque signature.

.DESCRIPTION
    Appele par build\publish.ps1 entre l'assemblage du pack et le zip, sous
    pwsh (le module TrustedSigning ne fonctionne pas sous Windows PowerShell).

    Parametres publics du compte : build\signing.json (versionne, sans secret).
    Identite : variables d'environnement AZURE_TENANT_ID / AZURE_CLIENT_ID /
    AZURE_CLIENT_SECRET (portee utilisateur, jamais versionnees). Elles sont
    recopiees dans le processus car un shell lance par un service peut avoir
    herite d'un environnement anterieur a leur creation : sans elles, le module
    bascule en authentification interactive et le build reste bloque.

    Une seule operation Invoke-TrustedSigning pour tous les fichiers (une
    signature par fichier est decomptee sur le SKU Basic : ~8 par release).

.EXAMPLE
    pwsh -NoProfile -File build\Sign-Pack.ps1 -PackRoot dist\ADReplic-0.7.1
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackRoot,
    [string]$ConfigPath = (Join-Path $PSScriptRoot 'signing.json')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PackRoot)) { throw "Dossier de pack introuvable : $PackRoot" }
if (-not (Test-Path -LiteralPath $ConfigPath)) { throw "Configuration de signature introuvable : $ConfigPath" }

foreach ($name in 'AZURE_TENANT_ID', 'AZURE_CLIENT_ID', 'AZURE_CLIENT_SECRET') {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name, 'Process'))) {
        $userValue = [Environment]::GetEnvironmentVariable($name, 'User')
        if ([string]::IsNullOrWhiteSpace($userValue)) {
            throw "Variable $name absente (portee utilisateur) : identite Artifact Signing non configuree sur ce poste."
        }
        [Environment]::SetEnvironmentVariable($name, $userValue, 'Process')
    }
}

$cfg = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
# Nom long canonique : %TEMP% peut etre en 8.3 (NHAULT~1) alors que Get-ChildItem
# renvoie des noms longs, ce qui fausserait les chemins relatifs affiches.
$packResolved = (Get-Item -LiteralPath $PackRoot).FullName

$files = @(Get-ChildItem -LiteralPath $packResolved -Recurse -File |
    Where-Object { $cfg.signableExtensions -contains $_.Extension.ToLowerInvariant() } |
    Select-Object -ExpandProperty FullName)

if ($files.Count -eq 0) { throw "Aucun fichier signable dans $packResolved" }

Write-Host ("==> Signature Artifact Signing de {0} fichier(s) ({1} / {2})" -f $files.Count, $cfg.codeSigningAccountName, $cfg.certificateProfileName)
foreach ($f in $files) { Write-Host ("    " + [IO.Path]::GetRelativePath($packResolved, $f)) }

# -Files n'accepte qu'un seul chemin ; pour un lot, le module attend un dossier
# et un filtre d'extensions (sans point, separees par des virgules).
$filter = ($cfg.signableExtensions | ForEach-Object { $_.TrimStart('.') }) -join ','

Import-Module TrustedSigning -ErrorAction Stop
Invoke-TrustedSigning `
    -Endpoint $cfg.endpoint `
    -CodeSigningAccountName $cfg.codeSigningAccountName `
    -CertificateProfileName $cfg.certificateProfileName `
    -FilesFolder $packResolved `
    -FilesFolderRecurse `
    -FilesFolderFilter $filter `
    -FileDigest $cfg.fileDigest `
    -TimeStampRfc3161 $cfg.timestampRfc3161 `
    -TimeStampDigest $cfg.timestampDigest | Out-Null

# Verification : chaque fichier doit etre Valid ET horodate (certificats a
# courte duree de vie : sans horodatage, la signature expirerait avec eux).
$failures = 0
foreach ($f in $files) {
    $sig = Get-AuthenticodeSignature -LiteralPath $f
    $timestamped = $null -ne $sig.TimeStamperCertificate
    if ($sig.Status -ne 'Valid' -or -not $timestamped) {
        $failures++
        Write-Host ("    [ECHEC] {0} : {1} (horodate : {2})" -f [IO.Path]::GetRelativePath($packResolved, $f), $sig.Status, $timestamped) -ForegroundColor Red
    }
}
if ($failures -gt 0) { throw "$failures fichier(s) avec une signature non valide ou sans horodatage." }

$first = Get-AuthenticodeSignature -LiteralPath $files[0]
Write-Host ("    OK - signe par " + $first.SignerCertificate.Subject + ", expire le " + $first.SignerCertificate.NotAfter.ToString('dd/MM/yyyy') + " (horodate)")

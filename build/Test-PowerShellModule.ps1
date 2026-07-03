# ============================================================================
# Test-PowerShellModule.ps1
# ----------------------------------------------------------------------------
# Test manuel autonome du module ADReplic PowerShell (lot v0.6.0 :
# Compare-ADRAudit, parametre -DcName, garde-fou Credential).
#
# Concu pour etre lance tel quel sur la machine de test, sans preparation :
#   - importe le module lui-meme (bin de build par defaut, ou -ModulePath
#     vers le psd1 du pack portable copie sur la machine)
#   - decouvre automatiquement un DC cible si -DcName n'est pas fourni
#   - cree son propre dossier de sortie dans %TEMP%
#   - genere ses propres JSON pour tester Compare-ADRAudit
#
# IMPORTANT : fichier en ASCII pur (pas d'accents), convention projet.
# Hote requis : Windows PowerShell 5.1 (module net48).
# Le module binaire verrouille sa DLL : fermez cette console avant de rebuilder.
#
# Usage :
#   .\Test-PowerShellModule.ps1
#   .\Test-PowerShellModule.ps1 -ModulePath "D:\Pack\Module\ADReplic\ADReplic.psd1"
#   .\Test-PowerShellModule.ps1 -DcName srv-dc-01.exemple.local
# ============================================================================

[CmdletBinding()]
param(
    # Vide par defaut : resolu automatiquement selon l'emplacement du script
    # (racine du pack portable, ou repo de dev). Voir Resolve-DefaultModulePath.
    [string]$ModulePath,
    [string]$DcName
)

$ErrorActionPreference = 'Stop'
$script:failures = 0

function Resolve-DefaultModulePath {
    # Ordre de recherche :
    #   1. Pack portable : le script est copie a la racine du pack par publish.ps1,
    #      le module vit dans .\Module\ADReplic\ (circuit serveur de test).
    #   2. Repo de dev, build Release puis Debug (circuit poste de dev).
    $candidates = @(
        (Join-Path $PSScriptRoot 'Module\ADReplic\ADReplic.psd1'),
        (Join-Path $PSScriptRoot '..\src\ADReplic.PowerShell\bin\Release\net48\ADReplic.psd1'),
        (Join-Path $PSScriptRoot '..\src\ADReplic.PowerShell\bin\Debug\net48\ADReplic.psd1')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }
    throw ("Aucun ADReplic.psd1 trouve. Emplacements testes :`n  " + ($candidates -join "`n  ") + "`nPassez -ModulePath explicitement.")
}

function Write-Result {
    param([bool]$Ok, [string]$Label)
    if ($Ok) { Write-Host "[OK]     $Label" -ForegroundColor Green }
    else     { Write-Host "[ECHEC]  $Label" -ForegroundColor Red; $script:failures++ }
}

if ($PSVersionTable.PSEdition -eq 'Core') {
    Write-Warning "Module net48 : lancez ce script sous Windows PowerShell 5.1 (powershell.exe), pas pwsh."
}

# --- 1. Import du module ----------------------------------------------------
Write-Host "`n== 1. Import du module =="
if (-not $ModulePath) { $ModulePath = Resolve-DefaultModulePath }
if (-not (Test-Path $ModulePath)) {
    throw "Module introuvable : $ModulePath."
}
Write-Host "         Module : $ModulePath"
Import-Module $ModulePath -Force
$commands = @((Get-Command -Module ADReplic).Name)
Write-Result ($commands -contains 'Compare-ADRAudit') "Compare-ADRAudit exporte"
Write-Result ($commands.Count -ge 9) "Au moins 9 commandes exportees ($($commands.Count) trouvees)"

# --- 2. Inventaire et choix du DC cible --------------------------------------
Write-Host "`n== 2. Inventaire =="
$dcs = @(Get-ADRDcInventory)
Write-Result ($dcs.Count -ge 1) "Inventaire : $($dcs.Count) DC decouverts"
if (-not $DcName) {
    $DcName = $dcs[0].HostName
    Write-Host "         DC cible choisi automatiquement : $DcName"
}

# --- 3. Mode DC seul (-DcName) ------------------------------------------------
Write-Host "`n== 3. Mode DC seul =="
$links = @(Get-ADRReplicationStatus -DcName $DcName)
$foreign = @($links | Where-Object { $_.DestinationDc -ne $DcName })
Write-Result ($foreign.Count -eq 0) "Get-ADRReplicationStatus -DcName : $($links.Count) lien(s), tous vers le DC cible"

$issues = @(Get-ADRIssue -DcName $DcName)
$falsePositives = @($issues | Where-Object { $_.Code -in 'DC_ISOLATED', 'DOMAIN_SINGLE_DC' })
Write-Result ($falsePositives.Count -eq 0) "Get-ADRIssue -DcName : pas de faux positif DC_ISOLATED / DOMAIN_SINGLE_DC ($($issues.Count) anomalie(s) au total)"

# --- 4. Export avec creation automatique du dossier --------------------------
Write-Host "`n== 4. Export =="
$outDir = Join-Path $env:TEMP ("ADReplic-Test-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
# Le dossier n'est volontairement PAS cree ici : c'est la cmdlet qui doit le creer.
$htmlPath = Export-ADRAudit -Path (Join-Path $outDir 'audit-dc.html') -DcName $DcName -SkipHealthProbes
Write-Result ((Test-Path $htmlPath)) "Export-ADRAudit -DcName : rapport cree, dossier de sortie cree automatiquement"
Write-Host "         Rapport : $htmlPath"

# --- 5. Compare-ADRAudit ------------------------------------------------------
# Deux exports JSON generes a la volee : le diff doit exister et etre sans
# degradation significative (memes donnees a quelques secondes d'intervalle).
Write-Host "`n== 5. Compare-ADRAudit =="
$jsonRef = Export-ADRAudit -Path (Join-Path $outDir 'audit-ref.json') -Format JSON -SkipHealthProbes
Start-Sleep -Seconds 2
$jsonCur = Export-ADRAudit -Path (Join-Path $outDir 'audit-cur.json') -Format JSON -SkipHealthProbes

$diff = Compare-ADRAudit -ReferencePath $jsonRef -CurrentPath $jsonCur
Write-Result ($null -ne $diff) "Compare-ADRAudit : objet AuditDiff retourne"

$significant = @(Compare-ADRAudit -ReferencePath $jsonRef -CurrentPath $jsonCur -SignificantOnly)
Write-Result ($significant.Count -eq 0) "Compare-ADRAudit -SignificantOnly : aucune degradation entre deux audits identiques"
Write-Host "         Pour un vrai diff : relancez avec deux snapshots de $env:APPDATA\ADReplic\snapshots (un pendant panne, un apres)."

# --- 6. Garde-fou Credential ---------------------------------------------------
# Deux couches de defense peuvent rejeter un -Credential vide, et les deux sont
# des issues correctes :
#   - ParameterArgumentValidationError : l'attribut [ValidateNotNull] bloque le
#     $null au binding, avant le code de la cmdlet (comportement normal en 5.1).
#   - CredentialProvidedButEmpty : le garde-fou applicatif (AuditContextBuilder)
#     attrape le cas ou la transformation [Credential] a laisse passer une
#     valeur vide (Get-Credential annule ou muet).
Write-Host "`n== 6. Garde-fou Credential =="
$guardTriggered = $false
$guardLayer = ''
try {
    Get-ADRDcInventory -Credential $null | Out-Null
}
catch {
    switch -Wildcard ($_.FullyQualifiedErrorId) {
        'CredentialProvidedButEmpty*'      { $guardTriggered = $true; $guardLayer = 'garde-fou applicatif' }
        'ParameterArgumentValidationError*' { $guardTriggered = $true; $guardLayer = 'validation d''attribut [ValidateNotNull]' }
        default { Write-Host "         Erreur inattendue : $($_.FullyQualifiedErrorId)" }
    }
}
if ($guardLayer) { Write-Host "         Rejete par : $guardLayer" }
Write-Result $guardTriggered "-Credential vide fourni explicitement : appel rejete"

# --- Bilan ---------------------------------------------------------------------
Write-Host ""
if ($script:failures -eq 0) {
    Write-Host "Tous les tests sont passes." -ForegroundColor Green
    Write-Host "Rappel : fermez cette console avant tout dotnet build (la DLL du module est verrouillee)."
}
else {
    Write-Host "$($script:failures) test(s) en echec." -ForegroundColor Red
    exit 1
}

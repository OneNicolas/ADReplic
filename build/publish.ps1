#requires -Version 5.1
<#
.SYNOPSIS
    Compile la solution et assemble un pack ADReplic portable sur clé USB.

.DESCRIPTION
    Produit :
      dist/
        ADReplic-{version}/
          ADReplic.exe               GUI WPF, executable unique (Core fusionne)
          ADReplic.exe.config
          Module/ADReplic/           Module PowerShell binaire (cmdlets ADR*)
          Module/ADForestAudit/      Module historique (cmdlets ADFA*)
          Lancer-PowerShell.cmd      Console PS avec modules pre-charges
          README.txt
        ADReplic-{version}.zip       Archive prête à transporter

.EXAMPLE
    .\publish.ps1
    .\publish.ps1 -Configuration Debug -SkipZip
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Release',

    [switch]$SkipZip,

    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

# ============================================================
# Fonctions (déclarées avant utilisation)
# ============================================================

function Get-VersionFromBuildProps {
    param([string]$RepoRoot)
    $propsPath = Join-Path $RepoRoot 'Directory.Build.props'
    if (-not (Test-Path $propsPath)) { return '0.0.0' }
    $xml = [xml](Get-Content $propsPath -Raw)
    $node = $xml.SelectSingleNode("//Version")
    if ($node) { return $node.InnerText.Trim() }
    return '0.0.0'
}

function Remove-IfExists {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return }
    try {
        Remove-Item $Path -Recurse -Force -ErrorAction Stop
    } catch {
        Start-Sleep -Milliseconds 500
        Remove-Item $Path -Recurse -Force
    }
}

function Write-LauncherShell {
    param([string]$Path)
    # On positionne PSModulePath pour rendre ADReplic et ADForestAudit
    # importables par leur nom court (Import-Module ADReplic plutôt qu'un chemin).
    $content = @'
@echo off
REM Ouvre une session PowerShell avec les modules ADReplic et ADForestAudit charges.
setlocal
cd /d "%~dp0"
powershell.exe -NoExit -ExecutionPolicy Bypass -Command "$env:PSModulePath = (Resolve-Path '.\Module').Path + ';' + $env:PSModulePath; Import-Module ADReplic -Force; Import-Module ADForestAudit -Force; Write-Host 'Modules charges :' -ForegroundColor Cyan; Write-Host '  ADReplic (binaire)' -ForegroundColor Gray; Get-Command -Module ADReplic | ForEach-Object { Write-Host ('    ' + $_.Name) -ForegroundColor Gray }; Write-Host '  ADForestAudit (surcouche)' -ForegroundColor Gray; Get-Command -Module ADForestAudit | ForEach-Object { Write-Host ('    ' + $_.Name) -ForegroundColor Gray }"
endlocal
'@
    Set-Content -Path $Path -Value $content -Encoding ASCII
}

function Write-Readme {
    param([string]$Path, [string]$Version)
    $content = @"
ADReplic $Version
================

Outil d'audit de la replication Active Directory.
Remplacement portable d'Active Directory Replication Status Tool (ARST).

PREREQUIS
---------
- Windows 10 / 11 ou Windows Server 2016+
- .NET Framework 4.8 (installe nativement sur tous les OS ci-dessus)
- Compte ayant les droits de lecture AD (Domain Users suffit dans la plupart
  des cas pour interroger les metadonnees de replication)

UTILISATION
-----------
1) Interface graphique :
   Double-clic sur ADReplic.exe a la racine du pack.
   C'est un executable unique et autonome : il ne depend d'aucun autre
   fichier du pack et peut etre copie seul (avec ADReplic.exe.config)
   sur un poste ou un serveur.

   Si Windows affiche un avertissement SmartScreen au premier lancement
   (fichier telecharge depuis Internet), cliquez sur "Informations
   complementaires" puis "Executer quand meme".

2) Console PowerShell :
   Double-clic sur Lancer-PowerShell.cmd

   Deux modules sont charges automatiquement :

   ADReplic (API moderne)
     Get-ADRDcInventory          Inventaire des controleurs de domaine
     Get-ADRReplicationStatus    Matrice complete des liens de replication
     Get-ADRReplicationFailure   Echecs de replication en cours
     Get-ADRTopology             Sites, sous-reseaux, liens inter-sites
     Get-ADRDnsHealth            Sante DNS (SRV + A records des DC)
     Get-ADRPortHealth           Sante reseau (tests de ports TCP par DC)
     Export-ADRAudit             Rapport complet (CSV / JSON / HTML)
     Compare-ADRAudit            Compare deux audits JSON (degradations)
     Get-ADRErrorExplanation     Explique un code d'erreur de replication (hors ligne)
     Register-ADRScheduledAudit  Planifie un audit recurrent (Daily/Weekly/Monthly)

   ADForestAudit (API historique, retrocompatibilite scripts existants)
     Get-ADFAReplicationStatus   Statut de replication par DC
     Get-ADFAReplicationErrors   Liste des erreurs en cours
     Get-ADFAReplicationTopology Sites avec leurs DC
     Get-ADFAInterSiteReplication Liens inter-sites
     Get-ADFADCMetadata          Metadata par paire DC/partenaire
     Get-ADFAReplicationLatency  Latence des liens
     Get-ADFANetworkPortsTest    Test des ports AD (53, 88, 135...)
     Get-ADFADNSHealth           Verification SRV _ldap / _kerberos
     Get-ADFAHealthScore         Score pondere 0-100
     Invoke-ADForestAudit        Audit complet (oriente rapport HTML Bootstrap)

EXEMPLES
--------
   # API moderne
   Export-ADRAudit -Path C:\Temp\audit.html -Format HTML
   Get-ADRReplicationFailure | Format-Table DestinationDc, SourceDc, LastErrorMessage
   Get-ADRDnsHealth | Where-Object Status -ne 'Ok'
   Get-ADRPortHealth -DCHostName dc01.exemple.local -Port 389,636

   # Planification d'un audit hebdomadaire
   Register-ADRScheduledAudit -TaskName "ADReplic-Weekly" \`
       -OutputFolder "\\fs01\reports\AD" \`
       -Frequency Weekly -At "07:00" \`
       -Credential (Get-Credential)

   # API historique
   Invoke-ADForestAudit                              # Score + rapport
   Get-ADFAReplicationStatus | Where-Object Statut -eq 'Echec'

REMARQUES
---------
- L'outil ne necessite PAS RSAT (Remote Server Administration Tools).
- Il peut etre execute depuis un poste admin, un DC, ou meme une cle USB.
- Aucune ecriture dans le registre, aucune installation.
- Le module ADForestAudit s'appuie sur ADReplic en interne :
  les anciennes fonctions sont plus rapides et plus fiables qu'en v1.
- Les proprietes francaises (Controleur, Statut...) sont conservees,
  les noms anglais (HostName, Status...) sont ajoutes comme alias.

PROJET INTERNE - SCOPI
"@
    Set-Content -Path $Path -Value $content -Encoding UTF8
}

# ============================================================
# Script principal
# ============================================================

$root      = Split-Path -Parent $PSScriptRoot
$slnPath   = Join-Path $root 'ADReplic.sln'
$distRoot  = Join-Path $root 'dist'
$version   = Get-VersionFromBuildProps -RepoRoot $root
$packName  = "ADReplic-$version"
$packRoot  = Join-Path $distRoot $packName
$modBinOut = Join-Path $packRoot 'Module\ADReplic'
$modPsOut  = Join-Path $packRoot 'Module\ADForestAudit'
$zipPath   = Join-Path $distRoot "$packName.zip"

if (-not $SkipBuild) {
    Write-Host "==> Build ($Configuration)" -ForegroundColor Cyan
    & dotnet build $slnPath -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Build echoue (code $LASTEXITCODE)."
    }
}

Write-Host "==> Preparation $packName" -ForegroundColor Cyan
Remove-IfExists -Path $packRoot
Remove-IfExists -Path $zipPath
New-Item -ItemType Directory -Path $modBinOut -Force | Out-Null
New-Item -ItemType Directory -Path $modPsOut  -Force | Out-Null

Write-Host "==> Pack GUI (executable unique a la racine)" -ForegroundColor Cyan
$appBin    = Join-Path $root "src\ADReplic.App\bin\$Configuration\net48"
$singleBin = Join-Path $appBin 'single'
if (Test-Path (Join-Path $singleBin 'ADReplic.exe')) {
    # Release : ILRepack.targets a fusionne Core.dll dans l'exe.
    Copy-Item (Join-Path $singleBin 'ADReplic.exe')        -Destination $packRoot -Force
    Copy-Item (Join-Path $singleBin 'ADReplic.exe.config') -Destination $packRoot -Force
} elseif (Test-Path (Join-Path $appBin 'ADReplic.exe')) {
    # Debug : pas de fusion, on livre l'exe et sa dependance cote a cote.
    Write-Warning "Exe fusionne absent ($singleBin) : pack avec Core.dll separee (build $Configuration)."
    foreach ($file in 'ADReplic.exe','ADReplic.exe.config','ADReplic.Core.dll') {
        Copy-Item (Join-Path $appBin $file) -Destination $packRoot -Force
    }
} else {
    throw "ADReplic.exe introuvable dans $appBin. Lance le build d'abord."
}

Write-Host "==> Pack module ADReplic (binaire)" -ForegroundColor Cyan
$psBin = Join-Path $root "src\ADReplic.PowerShell\bin\$Configuration\net48"
$psSrc = Join-Path $root "src\ADReplic.PowerShell"
$binaryModuleFiles = @(
    'ADReplic.psd1',
    'ADReplic.PowerShell.dll',
    'ADReplic.Core.dll'
)
foreach ($file in $binaryModuleFiles) {
    $src = Join-Path $psBin $file
    if (-not (Test-Path $src)) { throw "Fichier manquant : $src" }
    Copy-Item $src -Destination $modBinOut -Force
}
# Module imbrique (script) : non copie par dotnet build, on le prend a la source.
$nestedModule = Join-Path $psSrc 'ADReplic.Scheduling.psm1'
if (-not (Test-Path $nestedModule)) { throw "Module imbrique manquant : $nestedModule" }
Copy-Item $nestedModule -Destination $modBinOut -Force

Write-Host "==> Pack module ADForestAudit (surcouche)" -ForegroundColor Cyan
$adfaSrc = Join-Path $root 'src\ADForestAudit'
Copy-Item (Join-Path $adfaSrc 'ADForestAudit.psd1') -Destination $modPsOut -Force
Copy-Item (Join-Path $adfaSrc 'ADForestAudit.psm1') -Destination $modPsOut -Force

Write-Host "==> Launcher PowerShell et README" -ForegroundColor Cyan
Write-LauncherShell -Path (Join-Path $packRoot 'Lancer-PowerShell.cmd')
Write-Readme        -Path (Join-Path $packRoot 'README.txt') -Version $version

# Script de test manuel du module : copie a la racine du pack pour que la
# validation sur le serveur de test soit une seule commande, sans dependre
# du repo de dev. Le script trouve le module tout seul (.\Module\ADReplic\).
Copy-Item (Join-Path $PSScriptRoot 'Test-PowerShellModule.ps1') -Destination $packRoot -Force

# Plus d'ADReplic.ico a la racine : l'exe etant lui-meme a la racine, Windows
# en extrait l'icone directement pour les raccourcis.

if (-not $SkipZip) {
    Write-Host "==> Creation de l'archive" -ForegroundColor Cyan
    Compress-Archive -Path "$packRoot\*" -DestinationPath $zipPath -CompressionLevel Optimal
}

$packSize = (Get-ChildItem $packRoot -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ""
Write-Host "Pack OK : $packName" -ForegroundColor Green
Write-Host ("  Dossier : {0} ({1:N1} Mo)" -f $packRoot, $packSize)
if (-not $SkipZip) {
    $zipSize = (Get-Item $zipPath).Length / 1MB
    Write-Host ("  Archive : {0} ({1:N1} Mo)" -f $zipPath, $zipSize)
}
Write-Host ""
Write-Host "Pour tester : `"$packRoot\ADReplic.exe`""

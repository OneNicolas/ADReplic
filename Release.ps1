#requires -Version 5.1
<#
.SYNOPSIS
    Orchestre une release complete d'ADReplic : bump version, build, test,
    pack portable, et tag git local.

.DESCRIPTION
    Le script ne touche jamais a GitHub : il prepare la release en local et
    affiche les commandes a executer manuellement pour pousser le tag et creer
    la release GitHub avec le ZIP en asset.

    Etapes :
      1. Met a jour la version dans Directory.Build.props
      2. dotnet restore + build (Release)
      3. dotnet test (les ~65 tests xUnit doivent passer)
      4. Appel a build\publish.ps1 qui produit dist\ADReplic-{version}\ + zip
      5. Cree un tag git local vX.Y.Z (annote)
      6. Affiche les instructions pour finaliser la release sur GitHub

.PARAMETER Version
    Version SemVer (ex : "0.1.0", "0.2.0-beta.1"). Obligatoire.

.PARAMETER SkipTests
    Saute l'execution des tests. A reserver aux cas d'urgence.

.PARAMETER SkipTag
    Ne cree pas le tag git. Utile pour produire un pack sans engager de release.

.EXAMPLE
    .\Release.ps1 -Version "0.1.0"

.EXAMPLE
    .\Release.ps1 -Version "0.2.0" -SkipTests
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$Version,

    [switch]$SkipTests,

    [switch]$SkipTag
)

$ErrorActionPreference = 'Stop'

# ============================================================
# Configuration
# ============================================================

$Root         = $PSScriptRoot
$SolutionPath = Join-Path $Root 'ADReplic.sln'
$BuildProps   = Join-Path $Root 'Directory.Build.props'
$PublishScript= Join-Path $Root 'build\publish.ps1'
$DistDir      = Join-Path $Root 'dist'
$TagName      = "v$Version"
$ZipName      = "ADReplic-$Version.zip"
$ZipPath      = Join-Path $DistDir $ZipName

# ============================================================
# Helpers
# ============================================================

function Write-Step {
    param([int]$Number, [int]$Total, [string]$Message)
    Write-Host ""
    Write-Host "[$Number/$Total] $Message" -ForegroundColor Cyan
}

function Test-VersionFormat {
    param([string]$Value)
    # SemVer simple : X.Y.Z avec suffixe optionnel (-beta.N, -alpha.N, -rc.N)
    return $Value -match '^\d+\.\d+\.\d+(-(?:alpha|beta|rc)\.\d+)?$'
}

function Get-CurrentVersion {
    [xml]$xml = Get-Content $BuildProps -Encoding UTF8
    return $xml.Project.PropertyGroup.Version
}

function Set-CurrentVersion {
    param([string]$NewVersion)
    [xml]$xml = Get-Content $BuildProps -Encoding UTF8
    $xml.Project.PropertyGroup.Version = $NewVersion
    # PreserveWhitespace est faux par defaut : on garde l'indentation existante.
    $xml.Save($BuildProps)
}

function Assert-WorkingTreeClean {
    $status = git status --porcelain 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "Git n'est pas disponible ou ce dossier n'est pas un depot git."
    }
    if ($status) {
        Write-Host "ATTENTION : l'arbre de travail contient des modifications non commitees :" -ForegroundColor Yellow
        Write-Host $status -ForegroundColor Yellow
        $answer = Read-Host "Continuer quand meme ? (o/N)"
        if ($answer -ne 'o' -and $answer -ne 'O') {
            throw "Release annulee par l'utilisateur."
        }
    }
}

function Assert-TagNotExists {
    param([string]$Tag)
    $existing = git tag --list $Tag 2>$null
    if ($existing) {
        throw "Le tag $Tag existe deja localement. Supprimez-le avec ``git tag -d $Tag`` ou choisissez une autre version."
    }
}

# ============================================================
# Script principal
# ============================================================

if (-not (Test-VersionFormat $Version)) {
    throw "Format de version invalide : '$Version'. Attendu : X.Y.Z ou X.Y.Z-(alpha|beta|rc).N"
}

$totalSteps = if ($SkipTag) { 5 } else { 6 }

Write-Host ""
Write-Host "Release ADReplic v$Version" -ForegroundColor White
Write-Host "================================" -ForegroundColor DarkGray

# Verifications prealables Git (seulement si on va creer un tag)
if (-not $SkipTag) {
    Assert-WorkingTreeClean
    Assert-TagNotExists $TagName
}

# ─── Etape 1 : Bump version ─────────────────────────────────

Write-Step 1 $totalSteps "Mise a jour de la version dans Directory.Build.props"

$currentVersion = Get-CurrentVersion
if ($currentVersion -eq $Version) {
    Write-Host "      Version deja a $Version, pas de bump necessaire." -ForegroundColor Gray
} else {
    Set-CurrentVersion $Version
    Write-Host "      $currentVersion -> $Version" -ForegroundColor Green
}

# ─── Etape 2 : Build ────────────────────────────────────────

Write-Step 2 $totalSteps "Build de la solution (Release)"

& dotnet build $SolutionPath -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) {
    throw "Build echoue (code $LASTEXITCODE)."
}
Write-Host "      Build OK" -ForegroundColor Green

# ─── Etape 3 : Tests ────────────────────────────────────────

if ($SkipTests) {
    Write-Step 3 $totalSteps "Tests : SAUTES (-SkipTests)"
    Write-Host "      ATTENTION : tests ignores, a utiliser uniquement en cas d'urgence." -ForegroundColor Yellow
} else {
    Write-Step 3 $totalSteps "Execution des tests xUnit"
    & dotnet test $SolutionPath -c Release --no-build --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        throw "Tests en echec (code $LASTEXITCODE). Corrigez avant de releaser."
    }
    Write-Host "      Tests OK" -ForegroundColor Green
}

# ─── Etape 4 : Pack portable ────────────────────────────────

Write-Step 4 $totalSteps "Creation du pack portable (build\publish.ps1)"

# On passe -SkipBuild car on vient deja de builder en Release juste avant.
& $PublishScript -Configuration Release -SkipBuild
if ($LASTEXITCODE -ne 0) {
    throw "Pack portable echoue (code $LASTEXITCODE)."
}

if (-not (Test-Path $ZipPath)) {
    throw "Le ZIP attendu est introuvable : $ZipPath"
}

$zipSizeKb = [math]::Round((Get-Item $ZipPath).Length / 1KB, 0)
$zipHash   = (Get-FileHash $ZipPath -Algorithm SHA256).Hash
Write-Host "      ZIP : $ZipPath ($zipSizeKb Ko)" -ForegroundColor Green
Write-Host "      SHA256 : $zipHash" -ForegroundColor Gray

# ─── Etape 5 : Commit du bump version ───────────────────────

Write-Step 5 $totalSteps "Commit du bump version"

if ($currentVersion -eq $Version) {
    Write-Host "      Pas de bump a commiter." -ForegroundColor Gray
} else {
    & git add $BuildProps
    & git commit -m "chore(release): bump version to v$Version" | Out-Null
    Write-Host "      Commit cree." -ForegroundColor Green
}

# ─── Etape 6 : Tag git local ────────────────────────────────

if (-not $SkipTag) {
    Write-Step 6 $totalSteps "Creation du tag git local"
    & git tag -a $TagName -m "Release $TagName"
    Write-Host "      Tag $TagName cree localement." -ForegroundColor Green
}

# ============================================================
# Resume + instructions de push
# ============================================================

Write-Host ""
Write-Host "================================" -ForegroundColor DarkGray
Write-Host "  Release v$Version prete !" -ForegroundColor White
Write-Host "================================" -ForegroundColor DarkGray
Write-Host ""
Write-Host "Artefact :" -ForegroundColor White
Write-Host "  $ZipPath ($zipSizeKb Ko)"
Write-Host "  SHA256 : $zipHash"
Write-Host ""
Write-Host "Etapes restantes (a executer manuellement) :" -ForegroundColor White
Write-Host ""
Write-Host "  1. Pousser le commit et le tag :" -ForegroundColor Cyan
Write-Host "       git push" -ForegroundColor Gray
Write-Host "       git push --tags" -ForegroundColor Gray
Write-Host ""
Write-Host "  2. Creer la GitHub Release :" -ForegroundColor Cyan
Write-Host "       https://github.com/OneNicolas/ADReplic/releases/new?tag=$TagName" -ForegroundColor Gray
Write-Host "       - Titre : ADReplic $TagName"
Write-Host "       - Description : copier la section de version du CHANGELOG / README"
Write-Host "       - Glisser-deposer le ZIP comme asset : $ZipName"
Write-Host "       - Publier"
Write-Host ""

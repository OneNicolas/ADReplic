#requires -Version 5.1
<#
.SYNOPSIS
    Genere src\ADReplic.App\Resources\ADReplic.ico a partir du DrawingImage
    vectoriel defini dans App.xaml.

.DESCRIPTION
    L'icone WPF de l'application est decrite en XAML (App.xaml, ressource
    "AppIcon"). Windows Explorer ne sait pas lire ca : il lui faut un vrai
    fichier .ico binaire pour afficher l'icone sur l'exe, dans la barre des
    taches Windows ou dans un raccourci.

    Ce script rend le meme dessin vectoriel a plusieurs resolutions
    (16, 24, 32, 48, 64, 128, 256) et combine le tout dans un .ico
    multi-resolution unique. La source de verite reste App.xaml : si on
    retouche le design plus tard, on relance le script.

    Aucune dependance externe : utilise uniquement WPF (RenderTargetBitmap)
    et les types .NET pour ecrire le format ICO directement.

.EXAMPLE
    .\Generate-Icon.ps1
    # Produit src\ADReplic.App\Resources\ADReplic.ico

.NOTES
    A executer une fois apres modification de l'icone XAML, puis commit du .ico.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

# ============================================================
# WPF requiert un thread STA (Single-Threaded Apartment) pour
# RenderTargetBitmap. PowerShell 5.1 est en STA par defaut ;
# PowerShell 7 (pwsh) est en MTA par defaut et lance une erreur
# tres obscure ("Parameter 'index'") au premier render WPF.
# On detecte la situation et on se relance en STA automatiquement.
# ============================================================
if ([System.Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    Write-Host "Apartment MTA detecte : relance du script en STA..." -ForegroundColor Yellow
    $hostExe = if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh' } else { 'powershell' }
    & $hostExe -NoProfile -STA -File $PSCommandPath @args
    exit $LASTEXITCODE
}

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase, System.Xaml

# ============================================================
# 1. Charge le DrawingImage depuis App.xaml
# ============================================================

$repoRoot  = Split-Path -Parent $PSScriptRoot
$appXaml   = Join-Path $repoRoot 'src\ADReplic.App\App.xaml'
$outDir    = Join-Path $repoRoot 'src\ADReplic.App\Resources'
$outIco    = Join-Path $outDir 'ADReplic.ico'

if (-not (Test-Path $appXaml)) {
    throw "Introuvable : $appXaml"
}

Write-Host "Lecture de l'icone vectorielle depuis App.xaml..." -ForegroundColor Cyan

# Le XAML embarque la geometrie dans App.xaml comme ressource. Plutot que de
# parser le fichier entier (qui contient ResourceDictionary, themes, styles...),
# on extrait juste le bloc <DrawingImage x:Key="AppIcon">...</DrawingImage>
# et on le reparse comme XAML autonome.
$content = Get-Content $appXaml -Raw

$pattern = '(?s)<DrawingImage x:Key="AppIcon">.*?</DrawingImage>'
$match = [regex]::Match($content, $pattern)
if (-not $match.Success) {
    throw "Bloc <DrawingImage x:Key=`"AppIcon`"> introuvable dans App.xaml."
}

# On enrobe avec un namespace par defaut pour que XamlReader le parse comme arbre
# autonome (sans x:Key, qui n'a de sens que dans un ResourceDictionary).
$snippet = $match.Value -replace ' x:Key="AppIcon"', ''
$xamlStandalone = @"
<DrawingImage xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
              xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" />
"@
# Remplace le tag racine vide par la version chargee
$xamlStandalone = $snippet -replace '<DrawingImage>',
    '<DrawingImage xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">'

$drawing = [Windows.Markup.XamlReader]::Parse($xamlStandalone)
if (-not $drawing -or -not $drawing.Drawing) {
    throw "Echec de l'extraction du DrawingImage."
}

# ============================================================
# 2. Rendu multi-resolution en PNG
# ============================================================

# Tailles standards Windows pour une icone : couvrent du 16x16 (small icon
# Explorer) au 256x256 (jumbo icons / Windows 10+ start menu).
$sizes = @(16, 24, 32, 48, 64, 128, 256)

# Container pour les PNG en memoire, indexes par taille pour ecriture ICO.
# Hashtable simple (pas [ordered]@{}) : avec OrderedDictionary, $dict[$int] est
# ambigu et peut etre interprete comme acces par position au lieu d'acces par cle,
# ce qui jette ArgumentOutOfRangeException. L'ordre d'iteration n'a pas d'importance
# ici, on parcourt toujours via foreach ($size in $sizes).
$pngBlobs = @{}

foreach ($size in $sizes) {
    Write-Host "  Rendu ${size}x${size}..." -ForegroundColor Gray
    try {
        # On utilise DrawingVisual au lieu d'un Image Control : pas de pipeline
        # de layout WPF (Measure/Arrange), juste un dessin offscreen direct.
        # Plus rapide, plus fiable, c'est le pattern documente par Microsoft pour
        # le rendu programmatique d'icones.
        $rect = New-Object System.Windows.Rect 0.0, 0.0, ([double]$size), ([double]$size)

        $visual = New-Object System.Windows.Media.DrawingVisual
        $context = $visual.RenderOpen()
        $context.DrawImage($drawing, $rect)
        $context.Close()

        $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap(
            $size, $size, 96, 96,
            [System.Windows.Media.PixelFormats]::Pbgra32)
        $rtb.Render($visual)

        $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
        $frame = [System.Windows.Media.Imaging.BitmapFrame]::Create($rtb)
        $encoder.Frames.Add($frame)

        $ms = New-Object System.IO.MemoryStream
        $encoder.Save($ms)
        $pngBlobs[$size] = $ms.ToArray()
        $ms.Dispose()
    }
    catch {
        Write-Host "  ECHEC ${size}x${size}: $($_.Exception.GetType().FullName) - $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "  Stack: $($_.ScriptStackTrace)" -ForegroundColor DarkGray
        throw
    }
}

# ============================================================
# 3. Ecriture du conteneur ICO
# ============================================================
#
# Format ICO (https://en.wikipedia.org/wiki/ICO_(file_format)) :
#
#   ICONDIR (6 bytes)
#     Reserved : UInt16 = 0
#     Type     : UInt16 = 1 (ICO ; 2 = CUR)
#     Count    : UInt16 = nombre d'images
#
#   ICONDIRENTRY (16 bytes par image)
#     Width      : Byte   (0 = 256)
#     Height     : Byte   (0 = 256)
#     ColorCount : Byte   = 0 (pas de palette indexee)
#     Reserved   : Byte   = 0
#     Planes     : UInt16 = 1
#     BitCount   : UInt16 = 32
#     BytesInRes : UInt32 = taille du blob PNG
#     Offset     : UInt32 = position absolue du blob dans le fichier
#
#   Puis pour chaque image : le PNG brut (pas de DIB, pas de header BMP).
#   Windows >= XP accepte les ICO contenant des PNG.

Write-Host "Assemblage du .ico..." -ForegroundColor Cyan

$stream = New-Object System.IO.FileStream($outIco, [System.IO.FileMode]::Create)
$writer = New-Object System.IO.BinaryWriter($stream)

try {
    # ICONDIR
    $writer.Write([UInt16]0)              # Reserved
    $writer.Write([UInt16]1)              # Type = ICO
    $writer.Write([UInt16]$sizes.Count)   # Count

    # Premier offset : juste apres ICONDIR + toutes les ICONDIRENTRY
    $offset = 6 + (16 * $sizes.Count)

    foreach ($size in $sizes) {
        $blob = $pngBlobs[$size]
        $byteW = if ($size -ge 256) { [byte]0 } else { [byte]$size }
        $byteH = if ($size -ge 256) { [byte]0 } else { [byte]$size }

        $writer.Write($byteW)            # Width
        $writer.Write($byteH)            # Height
        $writer.Write([byte]0)           # ColorCount
        $writer.Write([byte]0)           # Reserved
        $writer.Write([UInt16]1)         # Planes
        $writer.Write([UInt16]32)        # BitCount
        $writer.Write([UInt32]$blob.Length)
        $writer.Write([UInt32]$offset)

        $offset += $blob.Length
    }

    # Blobs PNG dans le meme ordre que les entries
    foreach ($size in $sizes) {
        $writer.Write($pngBlobs[$size])
    }
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}

$sizeKb = (Get-Item $outIco).Length / 1KB
Write-Host ""
Write-Host "OK : $outIco ($([math]::Round($sizeKb, 1)) Ko, $($sizes.Count) resolutions)" -ForegroundColor Green
Write-Host ""
Write-Host "Etapes suivantes :" -ForegroundColor Cyan
Write-Host "  1. Verifier l'apparence du .ico (clic droit dans Explorer)"
Write-Host "  2. Commit src\ADReplic.App\Resources\ADReplic.ico"
Write-Host "  3. dotnet build : l'exe portera l'icone via <ApplicationIcon>"

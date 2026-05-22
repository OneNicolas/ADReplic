#requires -Version 5.1
<#
.SYNOPSIS
    Lance l'audit complet de la forêt AD et produit un rapport HTML Bootstrap.

.DESCRIPTION
    Script identique en sortie à la v1, mais s'appuie maintenant sur le module
    ADForestAudit v2 qui délègue les sondes lourdes au module binaire ADReplic.
#>
param(
    [switch]$Silent
)

Import-Module ADForestAudit -Force -ErrorAction Stop

$timestamp  = Get-Date -Format 'yyyyMMdd_HHmmss'
$reportPath = "$env:USERPROFILE\Desktop\Rapport_Audit_AD_$timestamp.html"

if (-not $Silent) {
    Write-Host "Execution de l'audit complet de la foret Active Directory..." -ForegroundColor Cyan
}

$allResults, $null = Invoke-ADForestAudit -OutputPath $reportPath -Silent:$Silent

$score       = $allResults['Score global']
$scoreDetail = $allResults['Score detaille']
$anomalies   = $allResults['Anomalies detectees']

function New-SectionHtml {
    param([string]$Title, [object]$Data, [string]$Id)
    if (-not $Data -or $Data.Count -eq 0) {
        return "<div class='card mb-3'><div class='card-header'><a class='btn btn-link' data-bs-toggle='collapse' href='#$Id'>$Title</a></div><div class='collapse show' id='$Id'><div class='card-body'><p>Aucune donnee disponible.</p></div></div></div>"
    }
    $fragment = $Data | ConvertTo-Html -Fragment
    return "<div class='card mb-3'><div class='card-header'><a class='btn btn-link' data-bs-toggle='collapse' href='#$Id'>$Title</a></div><div class='collapse show' id='$Id'><div class='card-body table-responsive'>$fragment</div></div></div>"
}

$body = @()
foreach ($key in $allResults.Keys) {
    if ($key -in @('Score global','Score detaille','Anomalies detectees')) { continue }
    $safeId = ($key -replace '[^a-zA-Z0-9]','')
    $body += New-SectionHtml -Title $key -Data $allResults[$key] -Id $safeId
}

$scoreRows = ""
foreach ($cat in $scoreDetail.Keys) {
    $s = $scoreDetail[$cat]
    $anCount = if ($s.Anomalies) { $s.Anomalies.Count } else { 0 }
    $scoreRows += "<tr><td>$cat</td><td>$($s.Score)/100</td><td>$($s.Statut)</td><td>$anCount</td></tr>`n"
}

$scoreDetailHtml = @"
<div class='card mb-4'>
  <div class='card-header'>Score detaille par categorie</div>
  <div class='card-body'>
    <table class='table table-sm'>
      <thead><tr><th>Categorie</th><th>Score</th><th>Statut</th><th>Anomalies</th></tr></thead>
      <tbody>
        $scoreRows
      </tbody>
    </table>
  </div>
</div>
"@

$anomaliesHtml = if (-not $anomalies -or $anomalies.Count -eq 0) {
    "<p>Aucune anomalie detectee.</p>"
} else {
    "<ul>" + (($anomalies | ForEach-Object { "<li>$_</li>" }) -join "") + "</ul>"
}
$anomaliesSection = "<div class='card mb-4'><div class='card-header'>Anomalies detectees</div><div class='card-body'>$anomaliesHtml</div></div>"

$scoreColor = if ($score -ge 80) { "bg-success" } elseif ($score -ge 50) { "bg-warning text-dark" } else { "bg-danger" }
$scoreSection = "<div class='card mb-4'><div class='card-header'>Score global de sante AD</div><div class='card-body'><span class='badge $scoreColor' style='font-size:1.2rem;'>$score / 100</span></div></div>"

$html = @"
<!DOCTYPE html>
<html lang='fr'>
<head>
<meta charset='UTF-8'>
<title>Rapport d'audit Active Directory</title>
<link href='https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css' rel='stylesheet'>
<style>body{font-family:Segoe UI,Arial,sans-serif}.meta{font-size:0.9rem;color:#555}table{font-size:0.85rem}th{background:#f3f3f3}</style>
</head>
<body class='bg-light'>
<div class='container my-4'>
  <div class='card mb-4'><div class='card-body'><h1 class='card-title mb-3'>Rapport d'audit Active Directory</h1><p class='meta mb-2'>Genere le : $(Get-Date)<br/>Hote : $env:COMPUTERNAME<br/>Utilisateur : $env:USERNAME</p></div></div>
  $scoreSection
  $scoreDetailHtml
  $anomaliesSection
  $($body -join "`n")
</div>
<script src='https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/js/bootstrap.bundle.min.js'></script>
</body>
</html>
"@

$html | Out-File -FilePath $reportPath -Encoding utf8

if (-not $Silent) { Write-Host "Rapport genere : $reportPath" -ForegroundColor Green }

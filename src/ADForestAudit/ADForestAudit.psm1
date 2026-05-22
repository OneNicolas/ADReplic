#requires -Version 5.1

<#
.SYNOPSIS
    ADForestAudit v2.1 : surcouche au-dessus du module binaire ADReplic.

.DESCRIPTION
    Maintient l'API publique de la v1.4 (mêmes noms de fonctions et propriétés
    en français) pour ne casser aucun script existant. Les sondes de réplication,
    topologie et erreurs délèguent au module binaire ADReplic ; les sondes
    réseau/DNS et le scoring restent locaux.

    Changements v2.1 :
    - Sonde KCC retirée : repadmin /showkcc n'est plus fiable sur Server 2022+
      et faisait du parsing texte fragile. La vraie source de vérité pour les
      erreurs de réplication (qui incluent les erreurs KCC) est désormais
      Get-ADRReplicationFailure.
    - Scoring rééquilibré sans la catégorie KCC : Réplication 50 %, DNS 25 %,
      Ports 25 %.
#>

Import-Module ADReplic -ErrorAction Stop

# =============================================================================
# Helpers internes
# =============================================================================

# Ajoute des propriétés EN comme alias non-destructifs des propriétés FR.
function Add-EnglishAliases {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true)]
        [PSObject]$Object,

        [Parameter(Mandatory = $true)]
        [hashtable]$AliasMap
    )
    process {
        if ($null -eq $Object) { return }
        foreach ($frenchName in $AliasMap.Keys) {
            $englishName = $AliasMap[$frenchName]
            if ($Object.PSObject.Properties[$frenchName]) {
                $value = $Object.$frenchName
                Add-Member -InputObject $Object -NotePropertyName $englishName -NotePropertyValue $value -Force
            }
        }
        $Object
    }
}

# =============================================================================
# Réplication — délégation à ADReplic
# =============================================================================

function Get-ADFAReplicationStatus {
    [CmdletBinding()]
    param()

    $links = Get-ADRReplicationStatus

    $links | Group-Object DestinationDc | ForEach-Object {
        $dcLinks = $_.Group
        $lastSuccess = $null
        $withSuccess = $dcLinks | Where-Object { $_.LastSuccess } | Sort-Object LastSuccess -Descending
        if ($withSuccess) { $lastSuccess = $withSuccess[0].LastSuccess }

        $hasError = ($dcLinks | Where-Object { $_.LastResultCode -ne 0 }).Count -gt 0

        [PSCustomObject]@{
            Controleur          = $_.Name
            DerniereReplication = $lastSuccess
            Statut              = if ($hasError) { 'Echec' } else { 'Reussite' }
        }
    } | Add-EnglishAliases -AliasMap @{
        Controleur          = 'HostName'
        DerniereReplication = 'LastReplication'
        Statut              = 'Status'
    }
}

function Get-ADFAReplicationErrors {
    [CmdletBinding()]
    param()

    Get-ADRReplicationFailure | ForEach-Object {
        [PSCustomObject]@{
            Controleur = $_.DestinationDc
            Erreur     = $_.LastErrorMessage
        }
    } | Add-EnglishAliases -AliasMap @{
        Controleur = 'HostName'
        Erreur     = 'Error'
    }
}

function Get-ADFAReplicationTopology {
    [CmdletBinding()]
    param()

    (Get-ADRTopology).Sites | ForEach-Object {
        [PSCustomObject]@{
            Site        = $_.Name
            Controleurs = ($_.DomainControllers -join ', ')
            Description = $_.Location
        }
    } | Add-EnglishAliases -AliasMap @{
        Site        = 'SiteName'
        Controleurs = 'DomainControllers'
        Description = 'Location'
    }
}

function Get-ADFAInterSiteReplication {
    [CmdletBinding()]
    param()

    (Get-ADRTopology).SiteLinks | ForEach-Object {
        [PSCustomObject]@{
            Lien        = $_.Name
            Sites       = ($_.Sites -join ', ')
            Intervalle  = [int]$_.ReplicationInterval.TotalMinutes
            Cout        = $_.Cost
        }
    } | Add-EnglishAliases -AliasMap @{
        Lien        = 'Name'
        Sites       = 'SitesLinked'
        Intervalle  = 'IntervalMinutes'
        Cout        = 'Cost'
    }
}

function Get-ADFADCMetadata {
    [CmdletBinding()]
    param()

    $links = Get-ADRReplicationStatus

    $links |
        Group-Object DestinationDc, SourceDc |
        ForEach-Object {
            $mostRecent = $_.Group | Sort-Object LastAttempt -Descending | Select-Object -First 1
            [PSCustomObject]@{
                Controleur        = $mostRecent.DestinationDc
                Partenaire        = $mostRecent.SourceDc
                DerniereTentative = $mostRecent.LastAttempt
                DernierResultat   = $mostRecent.LastResultCode
                DernierSucces     = $mostRecent.LastSuccess
            }
        } | Add-EnglishAliases -AliasMap @{
            Controleur        = 'HostName'
            Partenaire        = 'Partner'
            DerniereTentative = 'LastAttempt'
            DernierResultat   = 'LastResultCode'
            DernierSucces     = 'LastSuccess'
        }
}

function Get-ADFAReplicationLatency {
    [CmdletBinding()]
    param()

    Get-ADRReplicationStatus | ForEach-Object {
        $latencyMinutes = $null
        if ($_.Latency) {
            $latencyMinutes = [math]::Round($_.Latency.TotalMinutes, 2)
        }
        [PSCustomObject]@{
            Source            = $_.DestinationDc
            Partenaire        = $_.SourceDc
            DerniereTentative = $_.LastAttempt
            DernierSucces     = $_.LastSuccess
            LatenceMinutes    = $latencyMinutes
        }
    } | Add-EnglishAliases -AliasMap @{
        Source            = 'DestinationDc'
        Partenaire        = 'SourceDc'
        DerniereTentative = 'LastAttempt'
        DernierSucces     = 'LastSuccess'
        LatenceMinutes    = 'LatencyMinutes'
    }
}

# =============================================================================
# Sondes réseau et DNS — conservées telles quelles
# =============================================================================

function Get-ADFANetworkPortsTest {
    [CmdletBinding()]
    param(
        [int[]]$Ports = @(53, 88, 135, 389, 445, 3268, 3269)
    )
    $results = @()
    $dcs = Get-ADRDcInventory
    foreach ($dc in $dcs) {
        foreach ($port in $Ports) {
            $status = 'Inconnu'
            $tcp = $null
            try {
                $tcp = New-Object System.Net.Sockets.TcpClient
                $iar = $tcp.BeginConnect($dc.HostName, $port, $null, $null)
                $ok = $iar.AsyncWaitHandle.WaitOne(2000, $false)
                $status = if ($ok -and $tcp.Connected) { 'Ouvert' } else { 'Ferme' }
            } catch {
                $status = 'Erreur'
            } finally {
                if ($tcp) { $tcp.Close() }
            }
            $results += [PSCustomObject]@{
                Controleur = $dc.HostName
                Port       = $port
                Statut     = $status
            }
        }
    }

    $results | Add-EnglishAliases -AliasMap @{
        Controleur = 'HostName'
        Statut     = 'Status'
    }
}

function Get-ADFADNSHealth {
    [CmdletBinding()]
    param()

    $firstDc = Get-ADRDcInventory | Select-Object -First 1
    if (-not $firstDc) {
        Write-Warning "Aucun contrôleur trouvé pour vérifier la santé DNS."
        return
    }

    $domain = $firstDc.Domain
    $records = @("_ldap._tcp.dc._msdcs.$domain", "_kerberos._tcp.$domain")
    $results = @()

    foreach ($rec in $records) {
        try {
            $query = Resolve-DnsName -Name $rec -Type SRV -ErrorAction Stop
            foreach ($q in $query) {
                $results += [PSCustomObject]@{
                    Enregistrement = $rec
                    Cible          = $q.NameTarget
                    Port           = $q.Port
                    Priorite       = $q.Priority
                    Poids          = $q.Weight
                    Statut         = 'OK'
                }
            }
        } catch {
            $results += [PSCustomObject]@{
                Enregistrement = $rec
                Cible          = $null
                Port           = $null
                Priorite       = $null
                Poids          = $null
                Statut         = 'Echec'
            }
        }
    }

    $results | Add-EnglishAliases -AliasMap @{
        Enregistrement = 'Record'
        Cible          = 'Target'
        Priorite       = 'Priority'
        Poids          = 'Weight'
        Statut         = 'Status'
    }
}

# =============================================================================
# Scoring — sans KCC, poids rééquilibrés
# =============================================================================

function Get-ADFAHealthScore {
    [CmdletBinding()]
    param([hashtable]$Data)

    $scores = @{
        Replication = @{ Score=100; Anomalies=@(); Statut='OK' }
        DNS         = @{ Score=100; Anomalies=@(); Statut='OK' }
        Ports       = @{ Score=100; Anomalies=@(); Statut='OK' }
    }

    $rep = $Data['Statut de replication']
    if ($rep) {
        $failed = ($rep | Where-Object { $_.Statut -ne 'Reussite' }).Count
        if ($failed -gt 0) {
            $penalty = [math]::Min(100, $failed * 25)
            $scores.Replication.Score -= $penalty
            $scores.Replication.Anomalies += "$failed DC en echec de replication"
            $scores.Replication.Statut = if ($failed -ge 2) { 'Critique' } else { 'Avertissement' }
        }
        $lat = $Data['Latence de replication']
        if ($lat) {
            $high = ($lat | Where-Object { $_.LatenceMinutes -gt 30 }).Count
            if ($high -gt 0) {
                $scores.Replication.Score -= ($high * 5)
                $scores.Replication.Anomalies += "$high liens avec latence > 30min"
                if ($scores.Replication.Score -lt 60) { $scores.Replication.Statut = 'Avertissement' }
            }
        }
    }

    $dns = $Data['Sante DNS AD']
    if ($dns) {
        $ko = ($dns | Where-Object { $_.Statut -ne 'OK' }).Count
        if ($ko -gt 0) {
            $scores.DNS.Score -= ($ko * 20)
            $scores.DNS.Anomalies += "$ko enregistrements DNS en echec"
            $scores.DNS.Statut = if ($ko -ge 2) { 'Critique' } else { 'Avertissement' }
        }
    }

    $ports = $Data['Tests de ports reseau']
    if ($ports) {
        $ko = ($ports | Where-Object { $_.Statut -ne 'Ouvert' }).Count
        if ($ko -gt 0) {
            $scores.Ports.Score -= ($ko * 5)
            $scores.Ports.Anomalies += "$ko ports non ouverts"
            if ($ko -ge 5) { $scores.Ports.Statut = 'Critique' } elseif ($ko -ge 1) { $scores.Ports.Statut = 'Avertissement' }
        }
    }

    foreach ($k in @($scores.Keys)) {
        if ($scores[$k].Score -lt 0) { $scores[$k].Score = 0 }
    }

    # Pondération sans KCC : Réplication 50%, DNS 25%, Ports 25%.
    $global = [math]::Round(
        ($scores.Replication.Score * 0.5) +
        ($scores.DNS.Score         * 0.25) +
        ($scores.Ports.Score       * 0.25)
    )

    $anomalies = @()
    foreach ($k in $scores.Keys) { $anomalies += $scores[$k].Anomalies }
    $anomalies = $anomalies | Where-Object { $_ }

    return @{
        Score     = $global
        Detail    = $scores
        Anomalies = $anomalies
    }
}

# =============================================================================
# Orchestrateur
# =============================================================================

function Invoke-ADForestAudit {
    [CmdletBinding()]
    param(
        [string]$OutputPath,
        [switch]$Silent
    )

    $all = [ordered]@{}
    $all['Statut de replication']     = Get-ADFAReplicationStatus
    $all['Erreurs de replication']    = Get-ADFAReplicationErrors
    $all['Topologie de replication']  = Get-ADFAReplicationTopology
    $all['Replication inter-sites']   = Get-ADFAInterSiteReplication
    $all['Metadata DC']               = Get-ADFADCMetadata
    $all['Latence de replication']    = Get-ADFAReplicationLatency
    $all['Tests de ports reseau']     = Get-ADFANetworkPortsTest
    $all['Sante DNS AD']              = Get-ADFADNSHealth

    $health = Get-ADFAHealthScore -Data $all
    $all['Score global']        = $health.Score
    $all['Score detaille']      = $health.Detail
    $all['Anomalies detectees'] = $health.Anomalies

    if (-not $Silent) {
        Write-Host "Audit AD termine. Score global : $($health.Score)/100" -ForegroundColor Cyan
    }

    return $all, $OutputPath
}

Export-ModuleMember -Function 'Get-ADFA*', 'Invoke-ADForestAudit'

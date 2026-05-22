@{
    RootModule        = 'ADForestAudit.psm1'
    ModuleVersion     = '2.1.0'
    GUID              = 'd2a6f8b1-7c3e-4f2a-9b8e-5c1a2d3e4f66'
    Author            = 'Nicolas Haultcoeur'
    CompanyName       = 'Scopi'
    Description       = "Module d'audit Active Directory v2.1 : surcouche au-dessus du module binaire ADReplic. Sonde KCC retiree au profit de Get-ADRReplicationFailure."

    PowerShellVersion = '5.1'
    RequiredModules   = @('ADReplic')

    FunctionsToExport = @(
        'Get-ADFAReplicationStatus',
        'Get-ADFAReplicationErrors',
        'Get-ADFAReplicationTopology',
        'Get-ADFAInterSiteReplication',
        'Get-ADFADCMetadata',
        'Get-ADFAReplicationLatency',
        'Get-ADFANetworkPortsTest',
        'Get-ADFADNSHealth',
        'Get-ADFAHealthScore',
        'Invoke-ADForestAudit'
    )
    CmdletsToExport   = @()
    AliasesToExport   = @()
    VariablesToExport = @()

    PrivateData = @{
        PSData = @{
            Tags = @('ActiveDirectory','Replication','Audit','Scopi')
        }
    }
}

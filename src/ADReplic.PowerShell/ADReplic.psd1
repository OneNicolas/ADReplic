@{
    RootModule        = 'ADReplic.PowerShell.dll'
    ModuleVersion     = '0.4.0'
    GUID              = '8c4b3f12-1d6a-4e9f-b2a7-3f4d5e6c7a8b'
    Author            = 'Nicolas Haultcoeur'
    CompanyName       = 'Scopi'
    Description       = 'Module binaire ADReplic : sondes AD (réplication, topologie, diagnostics) au-dessus du noyau ADReplic.Core.'
    PowerShellVersion = '5.1'
    DotNetFrameworkVersion = '4.8'

    # Module imbriqué : extensions script (planification) ajoutées au module binaire.
    NestedModules     = @('ADReplic.Scheduling.psm1')

    CmdletsToExport   = @(
        'Get-ADRDcInventory',
        'Get-ADRReplicationStatus',
        'Get-ADRReplicationFailure',
        'Get-ADRTopology',
        'Get-ADRIssue',
        'Get-ADRDnsHealth',
        'Get-ADRPortHealth',
        'Export-ADRAudit'
    )
    FunctionsToExport = @(
        'Register-ADRScheduledAudit'
    )
    AliasesToExport   = @()
    VariablesToExport = @()
    PrivateData = @{
        PSData = @{
            Tags = @('ActiveDirectory','Replication','Audit','Scopi')
            ProjectUri = 'https://github.com/OneNicolas/ADReplic'
        }
    }
}

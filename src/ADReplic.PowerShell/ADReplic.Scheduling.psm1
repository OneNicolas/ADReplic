# ============================================================================
# ADReplic.Scheduling.psm1
# ----------------------------------------------------------------------------
# Surcouche script du module ADReplic.PowerShell qui ajoute les fonctions de
# planification d'audits via le Task Scheduler natif Windows.
#
# Pourquoi en .psm1 plutot qu'en cmdlet C# ?
#   La fonction Register-ADRScheduledAudit est essentiellement un orchestrateur
#   autour de Register-ScheduledTask, New-ScheduledTaskTrigger, etc. — toutes
#   natives Windows. Refaire ca en C# avec PowerShell.Create() serait plus
#   verbeux sans benefice : on gagne plus de transparence (l'admin peut lire
#   le script avant d'executer une cmdlet privilegiee) que de typage fort.
# ============================================================================

Set-StrictMode -Version 3.0

function Register-ADRScheduledAudit {
<#
.SYNOPSIS
    Enregistre une tache planifiee Windows qui execute Export-ADRAudit
    periodiquement et depose le rapport dans un dossier de sortie.

.DESCRIPTION
    Cree une tache Task Scheduler qui :
      1. Importe le module ADReplic (chemin capture au moment de la creation)
      2. Calcule un nom de fichier horodate dans OutputFolder
      3. Execute Export-ADRAudit avec les parametres fournis

    Le compte d'execution doit etre precise explicitement via l'un des trois
    parametres mutuellement exclusifs :
      -RunAsSystem  : compte SYSTEM (NT AUTHORITY\SYSTEM) — la machine doit
                      etre jointe au domaine et son compte doit avoir les
                      droits de lecture AD (cas rare en pratique)
      -Credential   : compte utilisateur AD (le mot de passe est stocke par
                      Task Scheduler sous forme chiffree, mais visible dans
                      schtasks /query /xml)
      -GMSA         : Group Managed Service Account (recommande en prod ;
                      requiert une infra AD avec KDS Root Key configuree)

.PARAMETER TaskName
    Nom de la tache planifiee (visible dans Task Scheduler).

.PARAMETER OutputFolder
    Dossier ou seront deposes les rapports. Doit etre accessible en ecriture
    par le compte d'execution. Les chemins UNC (\\server\share) sont supportes.

.PARAMETER Frequency
    Periodicite d'execution : Daily, Weekly ou Monthly.
    Weekly = lundi par defaut. Monthly = 1er du mois.

.PARAMETER At
    Heure d'execution au format HH:mm (ex: "07:00", "23:30").

.PARAMETER Format
    Format du rapport produit : CSV, JSON ou HTML (defaut HTML).

.PARAMETER ForestName
    Foret AD a auditer. Si vide, audit la foret courante de la machine.

.PARAMETER RunAsSystem
    Execute la tache sous le compte SYSTEM (machine doit avoir les droits AD).

.PARAMETER Credential
    Identifiants AD utilises pour executer la tache.

.PARAMETER GMSA
    Nom du Group Managed Service Account (ex: "DOMAINE\svc_adreplic$").

.PARAMETER SkipHealthProbes
    Desactive les sondes DNS et reseau (comportement equivalent v0.3.0).

.PARAMETER Force
    Remplace une tache existante de meme nom sans confirmation.

.EXAMPLE
    Register-ADRScheduledAudit -TaskName "ADReplic-Weekly" `
        -OutputFolder "\\fs01\reports\AD" `
        -Frequency Weekly -At "07:00" `
        -Credential (Get-Credential "EXEMPLE\svc_audit")

    Tache hebdomadaire (lundi 07:00) qui produit un HTML horodate sur le
    partage reseau, executee sous un compte de service AD.

.EXAMPLE
    Register-ADRScheduledAudit -TaskName "ADReplic-Quotidien" `
        -OutputFolder "C:\Reports\AD" `
        -Frequency Daily -At "06:00" -Format JSON `
        -GMSA "EXEMPLE\svc_adreplic$" -WhatIf

    Verifie ce qui serait cree sans rien enregistrer (mode WhatIf).

.OUTPUTS
    Microsoft.Management.Infrastructure.CimInstance (ScheduledTask)
#>
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium',
                   DefaultParameterSetName = 'RunAsSystem')]
    [OutputType([Microsoft.Management.Infrastructure.CimInstance])]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$TaskName,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$OutputFolder,

        [Parameter(Mandatory = $true)]
        [ValidateSet('Daily', 'Weekly', 'Monthly')]
        [string]$Frequency,

        [Parameter(Mandatory = $true)]
        [ValidatePattern('^([01]\d|2[0-3]):[0-5]\d$')]
        [string]$At,

        [ValidateSet('CSV', 'JSON', 'HTML')]
        [string]$Format = 'HTML',

        [string]$ForestName,

        [Parameter(ParameterSetName = 'RunAsSystem')]
        [switch]$RunAsSystem,

        [Parameter(Mandatory = $true, ParameterSetName = 'Credential')]
        [PSCredential]$Credential,

        [Parameter(Mandatory = $true, ParameterSetName = 'GMSA')]
        [ValidateNotNullOrEmpty()]
        [string]$GMSA,

        [switch]$SkipHealthProbes,
        [switch]$Force
    )

    # --- 1. Validations precoces ---------------------------------------------

    # Le module ADReplic doit etre importe (sinon on ne sait pas ou il vit).
    $module = Get-Module -Name ADReplic -ErrorAction SilentlyContinue
    if (-not $module) {
        throw "Le module ADReplic doit etre importe avant d'appeler cette fonction. Executez 'Import-Module ADReplic'."
    }
    $modulePath = Join-Path $module.ModuleBase 'ADReplic.psd1'
    if (-not (Test-Path $modulePath)) {
        throw "Manifest ADReplic.psd1 introuvable a $modulePath."
    }

    # Si la tache existe deja, demander -Force ou refuser.
    $existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($existing -and -not $Force) {
        throw "Une tache nommee '$TaskName' existe deja. Utilisez -Force pour la remplacer."
    }

    # OutputFolder : on n'oblige pas a ce qu'il existe au moment de la creation
    # (le compte d'execution peut etre different de l'utilisateur courant),
    # mais on previent si on detecte qu'il est inaccessible.
    if (-not (Test-Path $OutputFolder)) {
        Write-Warning "OutputFolder '$OutputFolder' n'existe pas ou n'est pas accessible depuis cette session. Verifiez que le compte d'execution y a acces en ecriture."
    }

    # --- 2. Construction de l'argument PowerShell qui sera lance par la tache ---

    # L'argument est entoure de simples cotes en bash-like ; on echappe les
    # chemins potentiellement problematiques (apostrophes dans OutputFolder
    # ou ForestName, rare mais possible).
    function Quote-PsLiteral([string]$s) { return "'" + ($s -replace "'", "''") + "'" }

    $extension = switch ($Format) {
        'CSV'  { '.csv' }
        'JSON' { '.json' }
        'HTML' { '.html' }
    }

    $exportArgs = @(
        "-Path `$path",
        "-Format $Format"
    )
    if ($ForestName)            { $exportArgs += "-ForestName $(Quote-PsLiteral $ForestName)" }
    if ($SkipHealthProbes)      { $exportArgs += "-SkipHealthProbes" }
    $exportArgsLine = $exportArgs -join ' '

    # Le command est volontairement compact et lisible (le sysadmin peut
    # l'inspecter dans schtasks /query /xml). Aucune logique metier ici :
    # on delegue tout a Export-ADRAudit qui est deja testee.
    $command = @"
Import-Module $(Quote-PsLiteral $modulePath) -Force;
`$ts = Get-Date -Format 'yyyy-MM-dd_HHmm';
`$path = Join-Path $(Quote-PsLiteral $OutputFolder) ('ADReplic-' + `$ts + '$extension');
Export-ADRAudit $exportArgsLine
"@

    $argument = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command `"& { $command }`""

    # --- 3. Construction de l'action, du trigger, du principal ----------------

    $action = New-ScheduledTaskAction `
        -Execute 'powershell.exe' `
        -Argument $argument

    $trigger = New-AdrTaskTrigger -Frequency $Frequency -At $At

    $principal = New-AdrTaskPrincipal -ParameterSetName $PSCmdlet.ParameterSetName `
        -Credential $Credential -GMSA $GMSA

    # Settings : timeout 1h (un audit ne doit pas durer plus longtemps),
    # rattrapage si machine eteinte, pas de retry (la prochaine occurrence
    # prendra le relais — un retry sur une transient AD est rarement utile).
    $settings = New-ScheduledTaskSettingsSet `
        -ExecutionTimeLimit (New-TimeSpan -Hours 1) `
        -StartWhenAvailable `
        -MultipleInstances IgnoreNew

    # --- 4. Resume pour ShouldProcess ----------------------------------------

    $whatIfSummary = "Tache '$TaskName' : $Frequency a $At, sortie $Format dans '$OutputFolder', execution $($PSCmdlet.ParameterSetName)"
    if (-not $PSCmdlet.ShouldProcess($whatIfSummary, "Enregistrer la tache planifiee")) {
        return
    }

    # --- 5. Enregistrement ---------------------------------------------------

    if ($existing) {
        Write-Verbose "Suppression de la tache existante '$TaskName'..."
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    }

    $registerParams = @{
        TaskName  = $TaskName
        Action    = $action
        Trigger   = $trigger
        Settings  = $settings
        Principal = $principal
    }

    # Pour le mode Credential, Register-ScheduledTask demande User+Password
    # passes directement (pas via Principal), car le password doit etre chiffre
    # par le service au moment de l'enregistrement.
    if ($PSCmdlet.ParameterSetName -eq 'Credential') {
        $registerParams.Remove('Principal')
        $registerParams.User     = $Credential.UserName
        $registerParams.Password = $Credential.GetNetworkCredential().Password
    }

    $task = Register-ScheduledTask @registerParams

    Write-Verbose "Tache '$TaskName' enregistree."
    return $task
}

# ============================================================================
# Helpers internes — non exportes
# ============================================================================

function New-AdrTaskTrigger {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('Daily','Weekly','Monthly')]
        [string]$Frequency,

        [Parameter(Mandatory)]
        [string]$At
    )

    # On utilise la date du jour comme reference, l'heure vient du parametre.
    $start = [datetime]::Today.Add([timespan]$At)
    # Si on est deja apres l'heure aujourd'hui, decale au lendemain pour
    # que le 1er trigger ne soit pas "deja passe".
    if ($start -lt [datetime]::Now) { $start = $start.AddDays(1) }

    switch ($Frequency) {
        'Daily' {
            return New-ScheduledTaskTrigger -Daily -At $start
        }
        'Weekly' {
            # Lundi par defaut. Si on veut customiser plus tard, on exposera
            # un parametre -DayOfWeek sur la fonction publique.
            return New-ScheduledTaskTrigger -Weekly -DaysOfWeek Monday -At $start
        }
        'Monthly' {
            # New-ScheduledTaskTrigger ne supporte pas -Monthly nativement.
            # On construit le trigger via CIM (1er du mois a l'heure dite).
            $class = Get-CimClass -Namespace Root/Microsoft/Windows/TaskScheduler `
                                  -ClassName MSFT_TaskMonthlyTrigger
            return New-CimInstance -CimClass $class -ClientOnly -Property @{
                Enabled        = $true
                DaysOfMonth    = [uint32[]](1)
                MonthsOfYear   = [uint32]4095   # toutes les bits 0..11 = tous les mois
                StartBoundary  = $start.ToString('yyyy-MM-ddTHH:mm:ss')
            }
        }
    }
}

function New-AdrTaskPrincipal {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ParameterSetName,
        [PSCredential]$Credential,
        [string]$GMSA
    )

    switch ($ParameterSetName) {
        'RunAsSystem' {
            return New-ScheduledTaskPrincipal -UserId 'SYSTEM' `
                -LogonType ServiceAccount -RunLevel Highest
        }
        'Credential' {
            # Le principal sera ignore par le caller : Register-ScheduledTask
            # gere User+Password directement. On retourne quelque chose de
            # neutre pour eviter un null dans le splat.
            return New-ScheduledTaskPrincipal -UserId $Credential.UserName `
                -LogonType Password -RunLevel Highest
        }
        'GMSA' {
            # gMSA = pas de mot de passe a fournir, Task Scheduler le recupere
            # via le Group Managed Service Account framework. Nom attendu :
            # "DOMAINE\gMSA_NAME$" (avec le dollar final).
            if ($GMSA -notmatch '\$$') {
                Write-Warning "Le nom gMSA '$GMSA' ne se termine pas par '`$' — c'est inhabituel. Le format usuel est 'DOMAINE\nom_gmsa`$'."
            }
            return New-ScheduledTaskPrincipal -UserId $GMSA `
                -LogonType Password -RunLevel Highest
        }
    }
}

Export-ModuleMember -Function Register-ADRScheduledAudit

# ADReplic — Audit de réplication Active Directory

> **Version 0.4.0** — outil portable de diagnostic AD
>
> Remplaçant moderne de **Active Directory Replication Status Tool (ARST)** de Microsoft, non maintenu depuis 2014. Application WPF (.NET Framework 4.8) qui audite la santé d'une forêt Active Directory (réplication, DNS, réseau), sans dépendance externe ni installation, dans un exécutable d'environ 200 Ko.

## Fonctionnalités

### Audit
- **Inventaire DC** — découverte automatique des contrôleurs de domaine d'une forêt (sites, OS, IP, FSMO, GC, RODC) sans RSAT
- **Mode DC seul** — auditer un DC précis sans énumérer toute la forêt (gain de temps sur les grandes infras)
- **Matrice de réplication** — liens entre DC avec statut Healthy / Warning / Failing / Unreachable, sondage parallélisé
- **Échecs actifs** — détection et classification des erreurs de réplication en cours (sévérité Recent / Sustained / Critical)
- **Topologie inter-sites** — sites, subnets, têtes de pont, site links et coûts
- **Santé DNS** — vérification des 4 SRV critiques (`_ldap`, `_kerberos`, `_gc`, `_kpasswd`) et résolution des enregistrements A de chaque DC, via P/Invoke `DnsQuery_W` (zéro dépendance externe)
- **Santé réseau** — tests de connectivité TCP sur 9 ports AD (53, 88, 135, 389, 445, 464, 636, 3268, 3269), distinction Closed / Timeout pour faciliter le diagnostic firewall vs service down
- **Score de santé tripartite** — note 0/100 pondérée Réplication 50 % / DNS 25 % / Réseau 25 %, avec niveaux Excellent / Warning / Critical et renormalisation automatique des poids si une sonde est absente

### Diagnostics (13 détecteurs)
ADReplic identifie automatiquement les anomalies de configuration et de structure, avec une recommandation de correction pour chacune :

| Code | Sévérité | Détecte |
|---|---|---|
| `DC_ISOLATED` | Critical | Contrôleur de domaine absent de tous les liens de réplication |
| `OS_UNSUPPORTED` | Critical | Windows Server 2003 / 2008 / 2008 R2 (hors support total) |
| `OS_OUT_OF_EXTENDED_SUPPORT` | Warning | Windows Server 2012 / 2012 R2 (hors support étendu depuis octobre 2023) |
| `OS_UNKNOWN` | Info | Version d'OS non reconnue |
| `DOMAIN_SINGLE_DC` | Warning | Domaine avec un seul contrôleur (SPOF) |
| `SITE_NO_BRIDGEHEAD` | Warning | Site avec des DC mais sans serveur tête de pont |
| `SITE_NO_SUBNET` | Warning | Site avec des DC mais sans subnet IP associé |
| `SITE_NO_DC` | Info | Site déclaré sans aucun DC |
| `SITE_LINK_COST_ABERRANT` | Warning | Coût de site link hors plage 1-10000 |
| `DNS_SRV_CRITICAL_MISSING` | Critical | Enregistrement SRV `_ldap` ou `_kerberos` introuvable |
| `DNS_SRV_OPTIONAL_MISSING` | Warning | Enregistrement SRV `_gc` ou `_kpasswd` introuvable |
| `DNS_RESOLUTION_ERROR` | Warning | Erreur d'infrastructure DNS (SERVFAIL, serveur injoignable) |
| `DNS_DC_A_RECORD_MISSING` | Critical | DC sans enregistrement A résolvable |
| `PORT_CRITICAL_CLOSED` | Critical | Port AD critique (389/88/445) fermé ou en timeout |
| `PORT_CLOSED_OR_FILTERED` | Warning | Port AD secondaire fermé ou en timeout |

### Interface et workflow
- **Forêt cible** — interroger une forêt distante depuis un poste hors-domaine
- **Identifiants alternatifs** — authentification UPN ou DOMAINE\compte
- **Thème clair / sombre** — bascule en un clic, préférence persistée dans `%APPDATA%`
- **Exports** — CSV (8 fichiers), JSON (snapshot complet) et HTML autonome avec CSS inline
- **8 onglets** — Contrôleurs, Échecs actifs, Réplication, Sites, Liens inter-sites, Diagnostics, Santé DNS, Santé réseau
- **Menu contextuel** — clic droit sur un DC pour l'auditer en mode ciblé

### Automatisation
- **Module PowerShell binaire** — 8 cmdlets (`Get-ADRDcInventory`, `Get-ADRReplicationStatus`, `Get-ADRReplicationFailure`, `Get-ADRTopology`, `Get-ADRIssue`, `Get-ADRDnsHealth`, `Get-ADRPortHealth`, `Export-ADRAudit`) + 1 fonction (`Register-ADRScheduledAudit` pour planifier des audits récurrents via Task Scheduler)
- **Module legacy `ADForestAudit`** — surcouche PowerShell avec API FR + alias EN (compatibilité scripts existants)
- **Portable** — exécutable single-file ~200 Ko, lançable depuis une clé USB, zéro install

## Prérequis

- Windows 10 / 11 ou Windows Server 2016+
- .NET Framework 4.8 (présent nativement sur Windows 10 1903+ et Windows Server 2019+)
- Compte avec droits de lecture sur l'AD cible (lecture des partitions Configuration et Domain)
- Aucune dépendance RSAT requise

## Stack technique

| Couche | Technologie |
|---|---|
| UI | WPF .NET Framework 4.8, MVVM fait main, sans framework externe |
| AD | `System.DirectoryServices.ActiveDirectory` (zéro dépendance RSAT) |
| DNS | P/Invoke `DnsQuery_W` (dnsapi.dll), zéro dépendance NuGet |
| Réseau | `System.Net.Sockets.TcpClient` natif, parallélisme `SemaphoreSlim` |
| PowerShell | Module binaire .NET Framework 4.8 + module script imbriqué |
| Tests | xUnit (**253 tests verts**) |
| Build | SDK-style csproj, single-file publish |

## Architecture

```
src/
├── ADReplic.Core/                — DLL avec toute la logique métier (testable, sans UI)
│   ├── Abstractions/             — Interfaces (IDcInventoryProvider, IReplicationProbe, IDnsHealthProbe, IPortHealthProbe, ...)
│   ├── Ad/                       — DirectoryContextFactory (création contexte AD)
│   ├── Diagnostics/              — Score tripartite, classifier d'erreurs, sévérité, Win32 messages FR
│   │   └── Issues/               — 13 détecteurs d'anomalies + IssueAggregator
│   ├── Discovery/                — DcInventoryProvider (GetAllAsync + GetSingleAsync)
│   ├── Export/                   — CSV, JSON, HTML exporters + AuditSnapshotBuilder
│   ├── Health/                   — Sondes DNS et réseau
│   │   ├── Dns/                  — Win32DnsResolver (P/Invoke), DnsHealthProbe, DnsSrvNames
│   │   └── Ports/                — TcpPortProber, PortHealthProbe, AdServicePorts
│   ├── Models/                   — DTO (DomainControllerInfo, ReplicationLink, HealthScore, DetectedIssue, DnsCheckResult, PortCheckResult, ...)
│   ├── Replication/              — ReplicationProbe, status evaluator, partition classifier
│   └── Topology/                 — TopologyProvider (sites, subnets, site links)
│
├── ADReplic.App/                 — WPF MVVM (UI uniquement)
│   ├── Mvvm/                     — RelayCommand, AsyncRelayCommand, ViewModelBase
│   ├── Resources/                — ADReplic.ico (multi-résolution, généré depuis le XAML)
│   ├── Themes/                   — LightTheme.xaml, DarkTheme.xaml, ThemeManager
│   ├── ViewModels/               — MainViewModel, Converters
│   └── Views/                    — MainWindow, CredentialsDialog, AboutDialog
│
├── ADReplic.PowerShell/          — Cmdlets binaires + script imbriqué (planification)
│   ├── ADReplic.psd1             — Manifeste module
│   ├── ADReplic.Scheduling.psm1  — Register-ADRScheduledAudit (NestedModules)
│   ├── Cmdlets/                  — 8 cmdlets (DcInventory, ReplicationStatus, Failure, Topology, Issue, DnsHealth, PortHealth, Export)
│   └── Internal/                 — AuditContextBuilder
│
└── ADForestAudit/                — Module legacy refactoré (PowerShell pur)
    ├── ADForestAudit.psd1
    ├── ADForestAudit.psm1        — Surcouche sur ADReplic.PowerShell, ajoute alias EN
    └── Scripts/Invoke-ADForestAudit.ps1

tests/
└── ADReplic.Core.Tests/          — xUnit, 253 tests

build/
├── publish.ps1                   — Produit le pack portable dans dist/
└── Generate-Icon.ps1             — Régénère l'icone .ico multi-résolution depuis le XAML

Release.ps1                       — Orchestrateur de release (bump + build + test + pack + tag local)
```

### Choix techniques notables

- **.NET Framework 4.8** plutôt que .NET 8 : présent nativement sur Windows 10/11 et Server 2019+, zéro install client, exécutable single-file léger.
- **WPF MVVM fait main** : pas de dépendance CommunityToolkit ou MVVM Light, le code est plus simple à auditer et l'exécutable plus léger.
- **`System.DirectoryServices.ActiveDirectory`** : librairie native .NET, pas besoin de RSAT ni d'AD PowerShell module sur le poste qui exécute ADReplic.
- **P/Invoke `DnsQuery_W`** : `System.Net.Dns` ne supporte pas les records SRV, et plutôt qu'embarquer une lib NuGet (DnsClient.NET ~200 Ko), on appelle directement l'API Win32 via P/Invoke avec marshaling propre des structures `DNS_RECORDW` / `DNS_SRV_DATAW`.
- **Single-file publish** : un seul `.exe` à copier sur clé USB, pas de DLL à traîner.
- **Localisation FR de bout en bout** : UI, code, commentaires, exports CSV/JSON/HTML.

## Démarrage rapide

```powershell
# Cloner
git clone https://github.com/OneNicolas/ADReplic.git
cd ADReplic

# Build
dotnet build

# Tester
dotnet test

# Lancer l'app
dotnet run --project src/ADReplic.App
```

## Pack portable USB

```powershell
# Produit dist/ADReplic-{version}/ + zip + launchers + 2 modules PowerShell
.\build\publish.ps1
```

Le dossier `dist/ADReplic-{version}/` contient :
- `App/ADReplic.exe` (single-file, ~200 Ko)
- `Module/ADReplic/` (module PowerShell binaire + script imbriqué)
- `Module/ADForestAudit/` (module legacy)
- `Lancer-GUI.cmd` (raccourci pour ne pas avoir à ouvrir le sous-dossier App)
- `Lancer-PowerShell.cmd` (session PowerShell avec modules pré-chargés)
- `ADReplic.ico` (icone multi-résolution pour création de raccourcis manuels)
- `README.txt` (notice utilisateur final)

## Workflow de release

Pour publier une nouvelle version :

```powershell
# Tout-en-un : bump version, build, tests, pack portable, tag git local
.\Release.ps1 -Version "X.Y.Z"

# Puis pousser
git push
git push --tags

# Et créer la GitHub Release manuellement avec le ZIP en asset
# https://github.com/OneNicolas/ADReplic/releases/new?tag=vX.Y.Z
```

## Module PowerShell

```powershell
# Importer
Import-Module .\Module\ADReplic\ADReplic.psd1

# Inventaire
Get-ADRDcInventory
Get-ADRReplicationStatus -ForestName exemple.local

# Échecs et anomalies
Get-ADRReplicationFailure -Credential (Get-Credential)
Get-ADRIssue                                  # toutes les anomalies
Get-ADRIssue -Severity Critical               # uniquement les critiques
Get-ADRIssue -Code DC_ISOLATED, OS_UNSUPPORTED

# Sondes DNS / Réseau
Get-ADRDnsHealth | Where-Object Status -ne 'Ok'
Get-ADRPortHealth -DCHostName dc01.exemple.local -Port 389,636

# Export complet (sondes activées par défaut)
Export-ADRAudit -Path C:\Temp\audit.html -Format HTML
Export-ADRAudit -Path C:\Temp\audit-fast.html -Format HTML -SkipHealthProbes

# Planification d'audits récurrents (Task Scheduler natif Windows)
Register-ADRScheduledAudit -TaskName "ADReplic-Weekly" `
    -OutputFolder "\\fs01\reports\AD" `
    -Frequency Weekly -At "07:00" `
    -Credential (Get-Credential)
```

## Données locales

Stockées dans `%APPDATA%\ADReplic\` :

| Fichier | Contenu |
|---|---|
| `theme.txt` | Préférence thème (Light / Dark) |

Aucune donnée sensible n'est persistée — les identifiants saisis dans le dialogue sont en mémoire uniquement.

## Testé sur

- Forêt AD multi-DC sous Windows Server 2022 (3 DC, 30 liens de réplication inter-sites)
- Postes Windows 10 et Windows 11 (en domaine et hors-domaine avec UPN)

## Historique des versions

| Version | Date | Apports majeurs |
|---|---|---|
| **0.4.0** | mai 2026 | Sondes DNS et Réseau (4 SRV + A records, 9 ports TCP) avec P/Invoke `DnsQuery_W` et `TcpClient`, scoring tripartite renormalisé, 6 nouveaux détecteurs, 2 onglets GUI, 2 cmdlets PowerShell, planification via `Register-ADRScheduledAudit`, icône `.exe` multi-résolution générée depuis le XAML |
| **0.3.0** | mai 2026 | Mode DC seul : audit ciblé d'un contrôleur précis (champ "DC cible" + menu contextuel), détecteurs adaptés (DC_ISOLATED et DOMAIN_SINGLE_DC désactivés en mode ciblé pour éviter les faux positifs) |
| **0.2.0** | mai 2026 | Moteur de détection d'anomalies (7 détecteurs), onglet GUI "Diagnostics", cmdlet `Get-ADRIssue`, section HTML "Diagnostics", fichier `issues.csv` |
| **0.1.0** | mai 2026 | Socle initial : inventaire, réplication, échecs, topologie, score de santé, 5 onglets GUI, 5 cmdlets, exports CSV/JSON/HTML, thèmes clair/sombre, pack portable |

Détail de chaque version dans les [GitHub Releases](https://github.com/OneNicolas/ADReplic/releases).

## Roadmap

Voir le backlog priorisé interne. Prochaines features prévues :

- **Mode DC seul côté PowerShell** — paramètre `-DcName` sur les cmdlets concernées
- **Comparaison de deux audits** — mode diff entre 2 fichiers JSON pour suivi MCO
- **Vue topologie SVG** — visualisation graphique inter-sites
- **Auto-refresh / mode monitoring** — réactualisation périodique avec notification de dégradation
- **Rotation automatique des rapports planifiés** — paramètre `-MaxKeptReports` sur `Register-ADRScheduledAudit`

## Licence

Voir [LICENSE](LICENSE). Logiciel propriétaire à utilisation gratuite — pas de redistribution sans autorisation.

## Développé par

[Scopi — La SCOP Informatique](https://scopi.fr) — Nicolas Haultcoeur

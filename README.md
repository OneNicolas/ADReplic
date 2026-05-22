# ADReplic — Audit de réplication Active Directory

> **Version 0.3.0** — outil portable de diagnostic AD
>
> Remplaçant moderne de **Active Directory Replication Status Tool (ARST)** de Microsoft, non maintenu depuis 2014. Application WPF (.NET Framework 4.8) qui audite la santé de la réplication d'une forêt Active Directory, sans dépendance externe ni installation, dans un exécutable d'environ 200 Ko.

## Fonctionnalités

### Audit
- **Inventaire DC** — découverte automatique des contrôleurs de domaine d'une forêt (sites, OS, IP, FSMO, GC, RODC) sans RSAT
- **Mode DC seul** — auditer un DC précis sans énumérer toute la forêt (gain de temps sur les grandes infras)
- **Matrice de réplication** — liens entre DC avec statut Healthy / Warning / Failing / Unreachable, sondage parallélisé
- **Échecs actifs** — détection et classification des erreurs de réplication en cours (sévérité Recent / Sustained / Critical)
- **Topologie inter-sites** — sites, subnets, têtes de pont, site links et coûts
- **Score de santé** — note 0/100 avec niveaux Excellent / Warning / Critical

### Diagnostics (7 détecteurs)
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

### Interface et workflow
- **Forêt cible** — interroger une forêt distante depuis un poste hors-domaine
- **Identifiants alternatifs** — authentification UPN ou DOMAINE\compte
- **Thème clair / sombre** — bascule en un clic, préférence persistée dans `%APPDATA%`
- **Exports** — CSV (6 fichiers), JSON (snapshot complet) et HTML autonome avec CSS inline
- **6 onglets** — Contrôleurs, Échecs actifs, Réplication, Sites, Liens inter-sites, Diagnostics
- **Menu contextuel** — clic droit sur un DC pour l'auditer en mode ciblé

### Automatisation
- **Module PowerShell binaire** — 6 cmdlets (`Get-ADRDcInventory`, `Get-ADRReplicationStatus`, `Get-ADRReplicationFailure`, `Get-ADRTopology`, `Get-ADRIssue`, `Export-ADRAudit`)
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
| PowerShell | Module binaire .NET Framework 4.8 |
| Tests | xUnit (**142 tests verts**) |
| Build | SDK-style csproj, single-file publish |

## Architecture

```
src/
├── ADReplic.Core/                — DLL avec toute la logique métier (testable, sans UI)
│   ├── Abstractions/             — Interfaces (IDcInventoryProvider, IReplicationProbe, ...)
│   ├── Ad/                       — DirectoryContextFactory (création contexte AD)
│   ├── Diagnostics/              — Score, classifier d'erreurs, sévérité, Win32 messages FR
│   │   └── Issues/               — 7 détecteurs d'anomalies + IssueAggregator
│   ├── Discovery/                — DcInventoryProvider (GetAllAsync + GetSingleAsync)
│   ├── Export/                   — CSV, JSON, HTML exporters + AuditSnapshotBuilder
│   ├── Models/                   — DTO (DomainControllerInfo, ReplicationLink, HealthScore, DetectedIssue, ...)
│   ├── Replication/              — ReplicationProbe, status evaluator, partition classifier
│   └── Topology/                 — TopologyProvider (sites, subnets, site links)
│
├── ADReplic.App/                 — WPF MVVM (UI uniquement)
│   ├── Mvvm/                     — RelayCommand, AsyncRelayCommand, ViewModelBase
│   ├── Themes/                   — LightTheme.xaml, DarkTheme.xaml, ThemeManager
│   ├── ViewModels/               — MainViewModel, Converters
│   └── Views/                    — MainWindow, CredentialsDialog, AboutDialog
│
├── ADReplic.PowerShell/          — Cmdlets binaires partageant ADReplic.Core
│   ├── ADReplic.psd1             — Manifeste module
│   ├── Cmdlets/                  — 6 cmdlets (DcInventory, ReplicationStatus, Failure, Topology, Issue, Export)
│   └── Internal/                 — AuditContextBuilder
│
└── ADForestAudit/                — Module legacy refactoré (PowerShell pur)
    ├── ADForestAudit.psd1
    ├── ADForestAudit.psm1        — Surcouche sur ADReplic.PowerShell, ajoute alias EN
    └── Scripts/Invoke-ADForestAudit.ps1

tests/
└── ADReplic.Core.Tests/          — xUnit, 142 tests

build/
└── publish.ps1                   — Produit le pack portable dans dist/

Release.ps1                       — Orchestrateur de release (bump + build + test + pack + tag local)
```

### Choix techniques notables

- **.NET Framework 4.8** plutôt que .NET 8 : présent nativement sur Windows 10/11 et Server 2019+, zéro install client, exécutable single-file léger.
- **WPF MVVM fait main** : pas de dépendance CommunityToolkit ou MVVM Light, le code est plus simple à auditer et l'exécutable plus léger.
- **`System.DirectoryServices.ActiveDirectory`** : librairie native .NET, pas besoin de RSAT ni d'AD PowerShell module sur le poste qui exécute ADReplic.
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
- `ADReplic.exe` (single-file, ~200 Ko)
- `Modules/ADReplic/` (module PowerShell binaire)
- `Modules/ADForestAudit/` (module legacy)
- `Launchers/` (raccourcis `.cmd`)
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
Import-Module .\Modules\ADReplic\ADReplic.psd1

# Inventaire
Get-ADRDcInventory
Get-ADRReplicationStatus -ForestName exemple.local

# Échecs et anomalies
Get-ADRReplicationFailure -Credential (Get-Credential)
Get-ADRIssue                                  # toutes les anomalies
Get-ADRIssue -Severity Critical               # uniquement les critiques
Get-ADRIssue -Code DC_ISOLATED, OS_UNSUPPORTED

# Export complet
Export-ADRAudit -Path C:\Temp\audit.html -Format HTML
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
| **0.3.0** | mai 2026 | Mode DC seul : audit ciblé d'un contrôleur précis (champ "DC cible" + menu contextuel), détecteurs adaptés (DC_ISOLATED et DOMAIN_SINGLE_DC désactivés en mode ciblé pour éviter les faux positifs) |
| **0.2.0** | mai 2026 | Moteur de détection d'anomalies (7 détecteurs), onglet GUI "Diagnostics", cmdlet `Get-ADRIssue`, section HTML "Diagnostics", fichier `issues.csv` |
| **0.1.0** | mai 2026 | Socle initial : inventaire, réplication, échecs, topologie, score de santé, 5 onglets GUI, 5 cmdlets, exports CSV/JSON/HTML, thèmes clair/sombre, pack portable |

Détail de chaque version dans les [GitHub Releases](https://github.com/OneNicolas/ADReplic/releases).

## Roadmap

Voir le backlog priorisé interne. Prochaines features prévues :

- **Sondes DNS et Ports dans le Core** — scoring tripartite Réplication 50% / DNS 25% / Ports 25%, alignement avec l'ancien module legacy
- **Mode DC seul côté PowerShell** — paramètre `-DcName` sur les cmdlets concernées
- **Comparaison de deux audits** — mode diff entre 2 fichiers JSON pour suivi MCO
- **Vue topologie SVG** — visualisation graphique inter-sites
- **Auto-refresh / mode monitoring** — réactualisation périodique avec notification de dégradation

## Licence

Voir [LICENSE](LICENSE). Logiciel propriétaire à utilisation gratuite — pas de redistribution sans autorisation.

## Développé par

[Scopi — La SCOP Informatique](https://scopi.fr) — Nicolas Haultcoeur

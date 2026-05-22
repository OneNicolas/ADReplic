# ADReplic — Audit de réplication Active Directory

> **Version 0.1.0** — outil portable de diagnostic AD
>
> Remplaçant moderne de **Active Directory Replication Status Tool (ARST)** de Microsoft, non maintenu depuis 2014. Application WPF (.NET Framework 4.8) qui audite la santé de la réplication d'une forêt Active Directory, sans dépendance externe ni installation, dans un exécutable d'environ 200 Ko.

## Fonctionnalités

- **Inventaire DC** — découverte automatique de tous les contrôleurs de domaine d'une forêt (sites, OS, IP, FSMO, GC, RODC) sans RSAT
- **Matrice de réplication** — tous les liens entre DC avec leur statut (Healthy / Warning / Failing / Unreachable), parallélisé
- **Échecs actifs** — détection et classification des erreurs de réplication en cours (sévérité Critical / Warning / Info)
- **Topologie inter-sites** — sites, subnets, têtes de pont, site links et coûts
- **Score de santé** — note 0/100 avec niveaux Excellent / Warning / Critical
- **Exports** — CSV (5 fichiers), JSON (snapshot complet) et HTML autonome avec CSS inline
- **Identifiants alternatifs** — authentification UPN ou DOMAINE\compte pour les forêts distantes
- **Forêt cible** — interroger une forêt distante depuis un poste hors-domaine
- **Thème clair/sombre** — bascule en un clic, préférence persistée dans `%APPDATA%`
- **Module PowerShell binaire** — 5 cmdlets (`Get-ADRDcInventory`, `Get-ADRReplicationStatus`, `Get-ADRReplicationFailure`, `Get-ADRTopology`, `Export-ADRAudit`)
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
| Tests | xUnit (~65 tests verts) |
| Build | SDK-style csproj, single-file publish |

## Architecture

```
src/
├── ADReplic.Core/                — DLL avec toute la logique métier (testable, sans UI)
│   ├── Abstractions/             — Interfaces (IDcInventoryProvider, IReplicationProbe, ...)
│   ├── Ad/                       — DirectoryContextFactory (création contexte AD)
│   ├── Diagnostics/              — Score, classifier d'erreurs, sévérité, Win32 messages FR
│   ├── Discovery/                — DcInventoryProvider
│   ├── Export/                   — CSV, JSON, HTML exporters + AuditSnapshotBuilder
│   ├── Models/                   — DTO (DomainControllerInfo, ReplicationLink, HealthScore...)
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
│   ├── Cmdlets/                  — 5 cmdlets : DcInventory, ReplicationStatus, ...
│   └── Internal/                 — AuditContextBuilder
│
└── ADForestAudit/                — Module legacy refactoré (PowerShell pur)
    ├── ADForestAudit.psd1
    ├── ADForestAudit.psm1        — Surcouche sur ADReplic.PowerShell, ajoute alias EN
    └── Scripts/Invoke-ADForestAudit.ps1

tests/
└── ADReplic.Core.Tests/          — xUnit, ~65 tests

build/
└── publish.ps1                   — Produit le pack portable dans dist/
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

## Module PowerShell

```powershell
# Importer
Import-Module .\Modules\ADReplic\ADReplic.psd1

# Exemples
Get-ADRDcInventory
Get-ADRReplicationStatus -ForestName lyceerondeau.bsg
Get-ADRReplicationFailure -Credential (Get-Credential)
Export-ADRAudit -Path C:\Temp\audit.html -Format HTML
```

## Données locales

Stockées dans `%APPDATA%\ADReplic\` :

| Fichier | Contenu |
|---|---|
| `theme.txt` | Préférence thème (Light / Dark) |

Aucune donnée sensible n'est persistée — les identifiants saisis dans le dialogue sont en mémoire uniquement.

## Testé sur

- Forêt `lyceerondeau.bsg` (Windows Server 2025, 3 DC SRV-CD-01/02/03, 30 liens de réplication)
- Postes Windows 10 et Windows 11 (en domaine et hors-domaine avec UPN)

## Roadmap

Voir le backlog priorisé. À venir :
- Détection de problèmes connus (DC isolés, OS obsolète, site sans subnet)
- Mode "DC seul" pour audit ciblé
- Sondes DNS et ports critiques dans le Core (scoring tripartite 50/25/25)
- Comparaison de deux audits (mode diff)
- Auto-refresh / mode monitoring

## Licence

Voir [LICENSE](LICENSE). Logiciel propriétaire à utilisation gratuite — pas de redistribution sans autorisation.

## Développé par

[Scopi — La SCOP Informatique](https://scopi.fr) — Nicolas Haultcoeur

using System.Collections.Generic;

namespace ADReplic.Core.Diagnostics.Issues
{
    /// <summary>
    /// Connaissance métier associée à chaque code d'anomalie : pourquoi c'est
    /// important et que faire. Centralisée ici plutôt que dans chaque détecteur
    /// pour qu'un non-expert lise le même vocabulaire partout (GUI, exports,
    /// PowerShell) et pour que la rédaction se relise en un seul endroit.
    ///
    /// Un détecteur peut toujours renseigner lui-même Impact / Recommendation :
    /// <see cref="Enrich"/> ne remplit que les champs laissés vides.
    /// </summary>
    public static class IssueKnowledgeBase
    {
        public sealed class Entry
        {
            public string Impact { get; set; }
            public string Recommendation { get; set; }
            public string DocumentationUrl { get; set; }
        }

        private const string Learn = "https://learn.microsoft.com/";

        private static readonly Dictionary<string, Entry> Entries = BuildEntries();

        /// <summary>Fiche du code, ou null si inconnu.</summary>
        public static Entry Lookup(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            return Entries.TryGetValue(code, out var entry) ? entry : null;
        }

        /// <summary>Complète les champs vides de l'anomalie à partir de la base. Sans effet si le code est inconnu.</summary>
        public static void Enrich(DetectedIssue issue)
        {
            if (issue == null) return;
            var entry = Lookup(issue.Code);
            if (entry == null) return;

            if (string.IsNullOrWhiteSpace(issue.Impact))           issue.Impact           = entry.Impact;
            if (string.IsNullOrWhiteSpace(issue.Recommendation))   issue.Recommendation   = entry.Recommendation;
            if (string.IsNullOrWhiteSpace(issue.DocumentationUrl)) issue.DocumentationUrl = entry.DocumentationUrl;
        }

        private static Entry E(string impact, string recommendation, string docUrl = null)
            => new Entry { Impact = impact, Recommendation = recommendation, DocumentationUrl = docUrl };

        private static Dictionary<string, Entry> BuildEntries()
        {
            return new Dictionary<string, Entry>
            {
                ["DC_UNREACHABLE"] = E(
                    "Les utilisateurs de son site s'authentifient sur un autre DC, souvent à travers le WAN : ouvertures de session lentes, GPO non appliquées. Ses propres changements ne se propagent plus, et plus il reste isolé, plus le risque d'objets persistants grandit.",
                    "1. Vérifier que la machine est allumée et répond au ping.\n2. Vérifier le service AD DS (NTDS) et le pare-feu sur le DC.\n3. Vérifier la résolution DNS de son nom depuis le poste d'audit.\n4. S'il a été retiré définitivement : nettoyer ses métadonnées (ntdsutil metadata cleanup) et ses enregistrements DNS.",
                    Learn + "troubleshoot/windows-server/active-directory/diagnose-replication-failures"),

                ["DC_ISOLATED"] = E(
                    "Un DC qui ne réplique avec personne accumule des divergences : mots de passe changés d'un côté et pas de l'autre, comptes créés invisibles ailleurs. Passé la durée de vie des tombstones (60 ou 180 jours), il devient irrécupérable sans intervention lourde.",
                    "1. Vérifier connectivité réseau, DNS et service NTDS du DC.\n2. Forcer le recalcul de topologie : repadmin /kcc <dc>.\n3. Vérifier qu'il existe des connexions entrantes et sortantes : repadmin /showrepl <dc>.\n4. Si le DC ne doit plus exister : le rétrograder proprement ou nettoyer ses métadonnées.",
                    Learn + "troubleshoot/windows-server/active-directory/diagnose-replication-failures"),

                ["OS_UNSUPPORTED"] = E(
                    "Un DC héberge les secrets de tout le domaine. Sans correctif de sécurité, une seule faille exploitée compromet potentiellement tous les comptes. C'est aussi un point bloquant pour les audits de conformité et les assurances cyber.",
                    "1. Installer un nouveau DC sous Windows Server 2022 ou 2025.\n2. Transférer les rôles FSMO s'il en détient : Move-ADDirectoryServerOperationMasterRole.\n3. Vérifier que plus aucune application ne pointe explicitement vers l'ancien DC (LDAP en dur, DNS clients).\n4. Rétrograder l'ancien DC (dcpromo / Uninstall-ADDSDomainController) puis le retirer.",
                    Learn + "lifecycle/products/?terms=windows%20server"),

                ["OS_OUT_OF_EXTENDED_SUPPORT"] = E(
                    "Sans abonnement ESU, ce DC ne reçoit plus de correctifs de sécurité : même exposition qu'un OS non supporté, avec un sursis payant possible. Chaque mois sans migration augmente le risque.",
                    "1. Vérifier si un abonnement ESU est en place ; sinon considérer le DC comme non protégé.\n2. Planifier la migration vers Windows Server 2022 ou 2025 : nouveau DC, transfert des rôles FSMO, rétrogradation de l'ancien.\n3. Vérifier la compatibilité des applications qui dépendent de ce DC.",
                    Learn + "lifecycle/products/?terms=windows%20server"),

                ["OS_UNKNOWN"] = E(
                    "Impossible d'évaluer l'exposition de ce DC aux failles de sécurité tant que sa version n'est pas connue.",
                    "1. Vérifier la version sur le DC : winver ou systeminfo.\n2. Vérifier l'attribut operatingSystemVersion de son compte machine dans l'annuaire ; s'il est vide, c'est généralement un DC injoignable ou un objet incomplet."),

                ["DOMAIN_SINGLE_DC"] = E(
                    "Si ce DC tombe (panne matérielle, mise à jour ratée, ransomware), plus personne ne peut ouvrir de session, les GPO ne s'appliquent plus et le DNS intégré à AD disparaît. Aucune sauvegarde ne restaure un domaine aussi vite qu'un second DC ne prend le relais.",
                    "1. Installer un second DC, idéalement sur un hôte physique ou un hyperviseur différent.\n2. Le promouvoir en catalogue global et serveur DNS.\n3. Configurer les clients et serveurs avec les deux DC comme serveurs DNS.\n4. Vérifier que les sauvegardes d'état système couvrent les deux DC.",
                    Learn + "windows-server/identity/ad-ds/plan/planning-domain-controller-capacity"),

                ["SITE_NO_BRIDGEHEAD"] = E(
                    "Impact limité tant que l'ISTG choisit automatiquement une tête de pont. Le risque est de ne pas maîtriser quel DC porte la réplication inter-sites : un DC sous-dimensionné peut être choisi et ralentir la propagation vers ce site.",
                    "1. Dans Sites et services AD, vérifier qu'au moins un DC du site est marqué comme tête de pont préférée si le choix doit être contrôlé.\n2. Sinon, accepter la sélection automatique et ignorer cette alerte.\n3. Vérifier que le DC élu (repadmin /bridgeheads) a les ressources nécessaires.",
                    Learn + "windows-server/identity/ad-ds/plan/determining-the-interval"),

                ["SITE_NO_SUBNET"] = E(
                    "Les postes de ce site ne savent pas qu'ils y sont : ils s'authentifient sur n'importe quel DC de la forêt, souvent à travers le WAN. Résultat : ouvertures de session lentes, GPO lentes à s'appliquer, recherches d'imprimantes ou de partages DFS qui pointent vers un autre site.",
                    "1. Lister les plages IP réellement utilisées sur ce site.\n2. Les déclarer comme sous-réseaux dans Sites et services AD et les rattacher au site.\n3. Vérifier qu'aucun poste n'apparaît dans le journal NETLOGON des DC comme NO_CLIENT_SITE.",
                    Learn + "windows-server/identity/ad-ds/plan/creating-a-site-design"),

                ["SITE_NO_DC"] = E(
                    "Aucun impact opérationnel direct. Un site vide complique la lecture de la topologie et peut tromper lors d'un diagnostic.",
                    "1. Si un DC est prévu à court terme sur ce site : conserver.\n2. Sinon : réaffecter ses sous-réseaux à un site existant puis supprimer le site dans Sites et services AD."),

                ["SITE_LINK_COST_ABERRANT"] = E(
                    "Le KCC se sert du coût pour choisir par où passent les données entre sites. Une valeur aberrante peut faire transiter la réplication par un lien lent ou créer des chemins inattendus, et ralentir la propagation des changements.",
                    "1. Ouvrir le site link dans Sites et services AD.\n2. Remettre un coût cohérent (100 par défaut, plus bas pour les liens rapides, plus haut pour les liens lents).\n3. Forcer le recalcul : repadmin /kcc sur un DC de chaque site concerné.",
                    Learn + "windows-server/identity/ad-ds/plan/determining-the-interval"),

                ["DNS_SRV_CRITICAL_MISSING"] = E(
                    "Sans cet enregistrement, les postes et serveurs ne trouvent pas de DC pour s'authentifier ou interroger l'annuaire : échecs d'ouverture de session, GPO non appliquées, applications LDAP en erreur. Les DC eux-mêmes peuvent ne plus se localiser pour répliquer.",
                    "1. Vérifier que la zone DNS du domaine (et _msdcs) est hébergée par le serveur DNS interrogé.\n2. Sur chaque DC : nltest /dsregdns pour republier ses enregistrements, puis ipconfig /registerdns.\n3. Vérifier que le service Netlogon tourne sur les DC.\n4. Contrôler : dcdiag /test:DNS /DnsRecordRegistration.",
                    Learn + "troubleshoot/windows-server/networking/srv-records-missing-after-promotion"),

                ["DNS_SRV_OPTIONAL_MISSING"] = E(
                    "Les fonctions principales marchent, mais certaines opérations peuvent échouer ou être lentes : recherches dans le catalogue global (Outlook, applications multi-domaines), changement de mot de passe Kerberos.",
                    "1. Vérifier que le DC concerné est bien catalogue global s'il doit l'être.\n2. Republier ses enregistrements : nltest /dsregdns.\n3. Contrôler avec dcdiag /test:DNS /DnsRecordRegistration.",
                    Learn + "troubleshoot/windows-server/networking/srv-records-missing-after-promotion"),

                ["DNS_RESOLUTION_ERROR"] = E(
                    "Le serveur DNS interrogé répond mal ou pas du tout : au-delà de cet enregistrement, c'est toute la localisation des DC qui est fragile. Les symptômes sont intermittents et difficiles à relier au DNS.",
                    "1. Vérifier que le serveur DNS configuré sur le poste d'audit est bien un DNS interne hébergeant la zone AD.\n2. Vérifier la santé du serveur DNS : service, forwarders, journal DNS Server.\n3. Tester la résolution depuis un autre poste pour écarter un problème local.",
                    Learn + "troubleshoot/windows-server/networking/dns-troubleshooting-guidance"),

                ["DNS_DC_A_RECORD_MISSING"] = E(
                    "Les enregistrements SRV peuvent pointer vers ce DC, mais sans adresse IP les clients ne peuvent pas l'atteindre : délais d'ouverture de session le temps que le client bascule sur un autre DC, échecs si c'est le seul DC du site.",
                    "1. Vérifier l'enregistrement A du DC dans la zone DNS du domaine.\n2. Sur le DC : ipconfig /registerdns puis redémarrer le service Netlogon.\n3. Vérifier que la mise à jour dynamique est autorisée sur la zone.\n4. Si le DC a été retiré : supprimer ses enregistrements DNS restants.",
                    Learn + "troubleshoot/windows-server/networking/dns-troubleshooting-guidance"),

                ["PORT_CRITICAL_CLOSED"] = E(
                    "Port 389 fermé : plus d'annuaire, les clients basculent sur un autre DC ou échouent. Port 88 fermé : plus d'authentification Kerberos. Port 445 fermé : plus de GPO ni de scripts d'ouverture de session (SYSVOL). Si c'est le seul DC du site, tous les utilisateurs du site sont touchés.",
                    "1. Vérifier le pare-feu Windows du DC et les règles réseau entre le poste d'audit et le DC.\n2. Vérifier que le service correspondant tourne : NTDS pour 389/88, Server (LanmanServer) pour 445.\n3. Tester depuis un autre segment réseau pour isoler un filtrage intermédiaire : Test-NetConnection <dc> -Port <port>.",
                    Learn + "troubleshoot/windows-server/networking/service-overview-and-network-port-requirements"),

                ["PORT_CLOSED_OR_FILTERED"] = E(
                    "Le DC continue d'authentifier et de servir l'annuaire. Les fonctions qui dépendent de ce port sont dégradées : LDAPS (636) pour les applications sécurisées, catalogue global (3268/3269), DNS (53) si le DC fait aussi serveur DNS, RPC (135) pour la réplication et les outils d'administration.",
                    "1. Déterminer si la fonction portée par ce port est utilisée depuis l'endroit où l'audit a été lancé.\n2. Si oui : vérifier pare-feu, routage et service associé.\n3. Si non : documenter le filtrage comme intentionnel pour ne plus être alerté.",
                    Learn + "troubleshoot/windows-server/networking/service-overview-and-network-port-requirements"),

                ["DNS_SRV_POINTS_TO_DEAD_DC"] = E(
                    "Les clients qui tirent ce DC au sort dans le DNS attendent le timeout avant de basculer sur un autre : ouvertures de session et applications LDAP ralenties de façon aléatoire. Un DC fantôme dans le DNS est aussi une source classique d'échecs de réplication persistants.",
                    "1. Déterminer si le DC est en panne temporaire ou retiré définitivement.\n2. Panne temporaire : le remettre en service, les enregistrements redeviendront valides.\n3. Retrait définitif : nettoyer ses métadonnées (ntdsutil metadata cleanup) puis supprimer ses enregistrements SRV, A et CNAME (_msdcs) dans le DNS.",
                    Learn + "troubleshoot/windows-server/active-directory/remove-data-after-ad-ds-installation-fails")
            };
        }
    }
}

using System.Collections.Generic;
using System.Linq;

namespace ADReplic.Core.Diagnostics
{
    /// <summary>
    /// Catalogue des codes d'erreur de réplication les plus fréquents, traduits
    /// en explications lisibles par un non-expert. Couvre les codes documentés
    /// par Microsoft dans "Troubleshoot common Active Directory replication
    /// errors" (KB 3108513) plus le code synthétique -1 émis par ADReplic.
    ///
    /// Les fiches sont immuables et partagées : <see cref="Lookup"/> renvoie
    /// toujours la même instance pour un code donné. Un code absent du catalogue
    /// reçoit une fiche générique (IsKnown = false) construite autour du message
    /// système, pour que l'UI ait toujours quelque chose d'exploitable à afficher.
    /// </summary>
    public static class ReplicationErrorCatalog
    {
        private const string LearnBase = "https://learn.microsoft.com/troubleshoot/windows-server/active-directory/";
        private const string CommonErrorsUrl = LearnBase + "common-active-directory-replication-errors";

        private static readonly Dictionary<int, ReplicationErrorExplanation> Entries = BuildEntries();
        private static readonly object UnknownCacheLock = new object();
        private static readonly Dictionary<int, ReplicationErrorExplanation> UnknownCache = new Dictionary<int, ReplicationErrorExplanation>();

        /// <summary>Codes documentés dans le catalogue.</summary>
        public static IReadOnlyCollection<int> KnownCodes => Entries.Keys.ToList();

        /// <summary>Retourne la fiche du code, ou une fiche générique si inconnu. Jamais null.</summary>
        public static ReplicationErrorExplanation Lookup(int code)
        {
            if (Entries.TryGetValue(code, out var known)) return known;

            lock (UnknownCacheLock)
            {
                if (!UnknownCache.TryGetValue(code, out var generic))
                {
                    generic = BuildGenericEntry(code);
                    UnknownCache[code] = generic;
                }
                return generic;
            }
        }

        private static ReplicationErrorExplanation BuildGenericEntry(int code)
        {
            var systemMessage = Win32ErrorMessage.ResolveOrCode(code) ?? $"Code {code}";
            return new ReplicationErrorExplanation
            {
                Code = code,
                IsKnown = false,
                Title = systemMessage,
                Explanation =
                    "Ce code d'erreur n'est pas documenté dans ADReplic. Le message système ci-dessus " +
                    "est la seule information disponible : la démarche générale de diagnostic reste " +
                    "valable (connectivité réseau, DNS, Kerberos, puis journaux du DC).",
                ProbableCauses = new[]
                {
                    "Problème réseau ou pare-feu entre les deux DC",
                    "Résolution DNS incorrecte du DC source",
                    "Problème d'authentification Kerberos (horloge, SPN, mot de passe machine)"
                },
                Checks = new[]
                {
                    "Consulter le journal Directory Service du DC de destination autour de l'heure de l'échec",
                    "Vérifier que le DC source répond (ping, ports 135/389/88/445)",
                    "Rechercher le code d'erreur sur Microsoft Learn"
                },
                Commands = new[]
                {
                    "repadmin /showrepl <dc-destination> /errorsonly",
                    "dcdiag /s:<dc-destination> /test:Replications"
                },
                DocumentationUrl = CommonErrorsUrl
            };
        }

        private static ReplicationErrorExplanation Entry(
            int code, string title, string explanation,
            string[] causes, string[] checks, string[] commands,
            string docUrl, bool transient = false, bool dataLossRisk = false)
        {
            return new ReplicationErrorExplanation
            {
                Code = code,
                IsKnown = true,
                Title = title,
                Explanation = explanation,
                ProbableCauses = causes,
                Checks = checks,
                Commands = commands,
                DocumentationUrl = docUrl,
                IsTransient = transient,
                IsDataLossRisk = dataLossRisk
            };
        }

        private static Dictionary<int, ReplicationErrorExplanation> BuildEntries()
        {
            var list = new List<ReplicationErrorExplanation>
            {
                Entry(-1,
                    "DC injoignable : ses échecs n'ont pas pu être consultés",
                    "ADReplic n'a pas réussi à interroger ce contrôleur de domaine. Ce n'est pas un code " +
                    "d'erreur Active Directory mais un marqueur interne : la ligne signale que l'état réel " +
                    "de la réplication de ce DC est inconnu.",
                    new[] { "DC éteint ou en cours de redémarrage", "Pare-feu bloquant LDAP (389) ou RPC (135) depuis le poste d'audit", "Nom DNS du DC non résolu depuis le poste d'audit" },
                    new[] { "Vérifier que le DC est allumé et répond au ping", "Vérifier la résolution DNS de son nom depuis le poste d'audit", "Vérifier les ports 135, 389 et 445 (onglet Ports d'ADReplic)", "Relancer l'audit une fois le DC de nouveau joignable" },
                    new[] { "nltest /server:<dc> /sc_query:<domaine>", "Test-NetConnection <dc> -Port 389" },
                    null),

                Entry(5,
                    "Accès refusé",
                    "Le DC de destination a bien contacté la source, mais l'authentification ou l'autorisation " +
                    "a échoué. Les deux DC se parlent, mais ne se font plus confiance.",
                    new[] { "Décalage d'horloge supérieur à 5 minutes entre les DC (Kerberos refuse)", "Mot de passe du compte machine du DC désynchronisé", "Canal sécurisé (secure channel) rompu", "Clé de registre RestrictRemoteClients ou CrashOnAuditFail mal positionnée" },
                    new[] { "Comparer l'heure des deux DC et de leur source de temps", "Tester le canal sécurisé du DC de destination", "Exécuter le test de sécurité dcdiag sur les deux DC", "Réinitialiser le mot de passe machine en dernier recours" },
                    new[] { "w32tm /monitor", "nltest /sc_verify:<domaine>", "dcdiag /s:<dc-destination> /test:CheckSecurityError", "netdom resetpwd /server:<dc-source> /userd:<domaine>\\<admin> /passwordd:*" },
                    LearnBase + "replications-fail-with-error-5"),

                Entry(1127,
                    "Erreur disque sur le DC",
                    "Une opération sur le disque hébergeant la base Active Directory (ntds.dit) a échoué " +
                    "malgré plusieurs tentatives. C'est un problème matériel ou de stockage, pas de réplication.",
                    new[] { "Disque ou contrôleur de stockage défaillant", "Espace disque insuffisant sur le volume NTDS", "Problème de pilote ou de stockage virtualisé" },
                    new[] { "Consulter le journal Système du DC (erreurs disk, ntfs, storahci...)", "Vérifier l'espace libre du volume NTDS", "Vérifier l'intégrité du disque", "Si la base est corrompue : restaurer le DC ou le rétrograder puis le repromouvoir" },
                    new[] { "chkdsk <lecteur-ntds>: /scan", "esentutl /k <chemin>\\ntds.dit" },
                    LearnBase + "replication-error-1127"),

                Entry(1256,
                    "Système distant indisponible (conséquence d'une autre erreur)",
                    "Le DC de destination a annulé ses demandes de réplication vers la source après un " +
                    "échec de connexion RPC. Ce code est un symptôme secondaire : la vraie cause est " +
                    "l'erreur logguée sur la partition Schema ou Configuration du même partenaire.",
                    new[] { "Une autre erreur (souvent 1722 ou 1753) sur le même lien a déclenché l'annulation", "DC source arrêté ou réseau coupé" },
                    new[] { "Chercher, pour le même couple source/destination, l'erreur non 1256 et la traiter en priorité", "Ne pas investiguer le 1256 lui-même" },
                    new[] { "repadmin /showrepl <dc-destination>", "repadmin /showrepl * /csv > replsum.csv" },
                    LearnBase + "active-directory-replication-error-1256"),

                Entry(1396,
                    "Nom du compte cible incorrect (SPN)",
                    "Kerberos n'a pas trouvé le nom de service (SPN) attendu pour le DC source, ou l'a " +
                    "trouvé sur un autre compte. Le DC de destination ne peut donc pas prouver qu'il " +
                    "parle au bon serveur.",
                    new[] { "SPN du DC source absent ou dupliqué sur un autre compte", "Catalogue global interrogé pas à jour (réplication en retard)", "Secret LSA manquant pour le domaine de la source (forêt multi-domaines)" },
                    new[] { "Lister les SPN du compte machine du DC source", "Rechercher un SPN en double dans la forêt", "Vérifier que le GC utilisé par la destination est à jour" },
                    new[] { "setspn -L <dc-source>", "setspn -X -F", "nltest /dsgetdc:<domaine> /gc" },
                    "https://support.microsoft.com/help/2183411"),

                Entry(1722,
                    "Serveur RPC injoignable",
                    "Le DC de destination n'a pas pu ouvrir de connexion RPC vers la source. En pratique : " +
                    "soit le nom ne se résout pas, soit le réseau ne passe pas, soit le service " +
                    "n'écoute pas. C'est l'erreur de réplication la plus fréquente.",
                    new[] { "Résolution DNS du nom du DC source incorrecte ou pointant vers une mauvaise IP", "Pare-feu bloquant le port 135 ou la plage de ports RPC dynamiques (49152-65535)", "DC source arrêté ou service AD DS non démarré", "Cartes réseau multiples : mauvaise interface enregistrée dans le DNS" },
                    new[] { "Résoudre le nom du DC source depuis la destination et comparer avec l'IP réelle", "Tester le port 135 puis un port dynamique RPC depuis la destination", "Vérifier que le service NTDS tourne sur la source", "Contrôler les règles de pare-feu entre les deux sites" },
                    new[] { "nslookup <dc-source>", "Test-NetConnection <dc-source> -Port 135", "portqry -n <dc-source> -e 135", "dcdiag /s:<dc-destination> /test:Connectivity" },
                    LearnBase + "replication-error-1722-rpc-server-unavailable"),

                Entry(1753,
                    "Aucun point de terminaison RPC disponible",
                    "Le port 135 répond, mais le service de réplication AD n'est pas enregistré auprès du " +
                    "mappeur de points de terminaison. Soit la destination contacte la mauvaise machine, " +
                    "soit le service AD DS de la source n'est pas correctement démarré.",
                    new[] { "Enregistrement DNS pointant vers une machine qui n'est pas le DC (IP réattribuée)", "Service AD DS de la source arrêté ou en échec d'initialisation", "DC source rétrogradé mais encore référencé" },
                    new[] { "Vérifier que l'IP résolue pour la source est bien celle du DC", "Vérifier l'état du service NTDS et les journaux Directory Service de la source", "Si le DC a été retiré : nettoyer ses métadonnées" },
                    new[] { "nslookup <dc-source>", "portqry -n <dc-source> -e 135", "Get-Service NTDS -ComputerName <dc-source>" },
                    LearnBase + "replication-error-1753"),

                Entry(1818,
                    "Appel RPC annulé (délai dépassé)",
                    "La connexion RPC s'est établie mais l'opération n'a pas abouti dans le délai imparti. " +
                    "Typique d'une liaison réseau lente ou saturée, ou d'un DC source très chargé.",
                    new[] { "Liaison WAN lente ou saturée entre les sites", "DC source surchargé (CPU, disque)", "Timeout RPC trop court pour la taille des changements à répliquer" },
                    new[] { "Vérifier la charge du DC source", "Mesurer la latence et la bande passante vers la source", "Vérifier si l'erreur disparaît hors heures de pointe" },
                    new[] { "repadmin /replsummary", "Test-NetConnection <dc-source> -Port 389 -InformationLevel Detailed" },
                    CommonErrorsUrl, transient: true),

                Entry(1908,
                    "Aucun serveur d'ouverture de session disponible",
                    "Le DC de destination n'a trouvé aucun contrôleur de domaine pour authentifier sa " +
                    "demande. Presque toujours un problème DNS : les enregistrements de service (SRV) " +
                    "qui permettent de localiser les DC sont absents ou incorrects.",
                    new[] { "Enregistrements SRV (_ldap._tcp, _kerberos._tcp) manquants dans le DNS", "Le DC de destination utilise un serveur DNS qui n'héberge pas la zone AD", "Service Netlogon arrêté sur les DC" },
                    new[] { "Vérifier les enregistrements SRV du domaine (onglet DNS d'ADReplic)", "Vérifier les serveurs DNS configurés sur le DC de destination", "Ré-enregistrer les enregistrements DNS des DC" },
                    new[] { "nltest /dsgetdc:<domaine>", "dcdiag /test:DNS /s:<dc-destination>", "nltest /dsregdns" },
                    CommonErrorsUrl),

                Entry(8418,
                    "Schéma différent entre les deux DC",
                    "La source et la destination n'ont pas la même version du schéma Active Directory. " +
                    "La réplication est bloquée jusqu'à ce que la partition Schema soit synchronisée.",
                    new[] { "Extension de schéma récente (Exchange, SCCM...) pas encore répliquée partout", "Partition Schema elle-même en échec de réplication" },
                    new[] { "Vérifier l'état de réplication de la partition Schema en priorité", "Attendre la fin de la propagation si une extension vient d'être faite", "Forcer la synchronisation de la partition Schema" },
                    new[] { "repadmin /showrepl <dc-destination>", "repadmin /syncall <dc-destination> CN=Schema,CN=Configuration,DC=<domaine>,DC=<tld> /AdeP" },
                    LearnBase + "replication-error-8418", transient: true),

                Entry(8451,
                    "Erreur de base de données sur le DC",
                    "La base Active Directory (ntds.dit) du DC a renvoyé une erreur interne pendant la " +
                    "réplication. Signal d'une corruption ou d'un problème de stockage : à traiter " +
                    "rapidement et avec prudence.",
                    new[] { "Corruption de la base (checksum, index secondaire)", "Matériel, firmware ou pilote de stockage défaillant", "Compteurs internes de la base arrivés en butée" },
                    new[] { "Consulter le journal Directory Service pour l'erreur JET détaillée (événements 1173, 474, 488)", "Ne pas redémarrer le DC à répétition : sauvegarder d'abord", "Vérifier l'intégrité de la base hors ligne", "Si corruption confirmée : rétrograder puis repromouvoir le DC" },
                    new[] { "esentutl /k <chemin>\\ntds.dit", "ntdsutil \"activate instance ntds\" files integrity quit quit" },
                    LearnBase + "active-directory-replication-error-8451", dataLossRisk: true),

                Entry(8452,
                    "Partition en cours de retrait ou non répliquée depuis ce serveur",
                    "La destination demande à la source une partition que celle-ci n'héberge pas (ou plus). " +
                    "La topologie de réplication vue par les deux DC n'est pas cohérente.",
                    new[] { "DC source rétrogradé ou partition applicative retirée, mais liens pas encore recalculés", "Métadonnées d'un ancien DC non nettoyées", "KCC en échec sur la destination" },
                    new[] { "Vérifier que la source héberge bien la partition concernée", "Forcer le recalcul de la topologie sur la destination", "Nettoyer les métadonnées d'un DC supprimé si nécessaire" },
                    new[] { "repadmin /showrepl <dc-source>", "repadmin /kcc <dc-destination>", "ntdsutil \"metadata cleanup\"" },
                    LearnBase + "replication-error-8452"),

                Entry(8453,
                    "Accès à la réplication refusé",
                    "La source a refusé la demande de réplication pour une raison de droits : le compte " +
                    "machine du DC de destination n'a pas les permissions attendues, ou son compte n'est " +
                    "plus reconnu comme un contrôleur de domaine.",
                    new[] { "Attribut UserAccountControl du compte machine du DC modifié (flags SERVER_TRUST_ACCOUNT / TRUSTED_FOR_DELEGATION perdus)", "Permissions manquantes sur la tête de partition (NC head)", "Compte machine du DC déplacé hors de l'OU Domain Controllers", "Réplication lancée avec un compte sans le droit Replicating Directory Changes" },
                    new[] { "Exécuter dcdiag sur les deux DC avec le test de sécurité", "Vérifier la valeur UserAccountControl du compte machine (532480 pour un DC inscriptible)", "Vérifier que le compte machine est bien dans l'OU Domain Controllers", "Vérifier les permissions sur la tête de partition" },
                    new[] { "dcdiag /s:<dc-destination> /test:CheckSecurityError", "dcdiag /s:<dc-destination> /test:NCSecDesc", "dcdiag /s:<dc-destination> /test:MachineAccount" },
                    LearnBase + "replication-error-8453"),

                Entry(8456,
                    "Le DC source refuse les demandes de réplication",
                    "Le DC source a volontairement désactivé sa réplication sortante. Windows le fait " +
                    "automatiquement pour protéger la forêt après un incident grave (restauration non " +
                    "supportée, retour en arrière d'un snapshot, USN rollback).",
                    new[] { "Restauration du DC par snapshot ou image disque (USN rollback)", "Espace disque insuffisant sur le DC source", "Réplication désactivée manuellement par un administrateur" },
                    new[] { "Consulter le journal Directory Service de la source (événements 2095, 2103, 1393)", "Vérifier la valeur de registre DSA not writable", "Si USN rollback : rétrograder puis repromouvoir le DC, ne pas réactiver la réplication à la main", "Si espace disque : libérer de l'espace puis réactiver" },
                    new[] { "repadmin /options <dc-source>", "reg query HKLM\\System\\CurrentControlSet\\Services\\NTDS\\Parameters /v \"DSA not writable\"" },
                    LearnBase + "replication-error-8456-8457", dataLossRisk: true),

                Entry(8457,
                    "Le DC de destination refuse les demandes de réplication",
                    "Le DC de destination a volontairement désactivé sa réplication entrante. Comme pour " +
                    "le 8456, c'est une protection automatique après un incident grave (USN rollback) " +
                    "ou une désactivation manuelle.",
                    new[] { "Restauration du DC par snapshot ou image disque (USN rollback)", "Espace disque insuffisant sur le DC de destination", "Réplication désactivée manuellement par un administrateur" },
                    new[] { "Consulter le journal Directory Service de la destination (événements 2095, 2103, 1393)", "Vérifier la valeur de registre DSA not writable", "Si USN rollback : rétrograder puis repromouvoir le DC, ne pas réactiver la réplication à la main", "Si espace disque : libérer de l'espace puis réactiver" },
                    new[] { "repadmin /options <dc-destination>", "reg query HKLM\\System\\CurrentControlSet\\Services\\NTDS\\Parameters /v \"DSA not writable\"" },
                    LearnBase + "replication-error-8456-8457", dataLossRisk: true),

                Entry(8464,
                    "Synchronisation des attributs partiels en cours",
                    "Un attribut vient d'être ajouté au jeu d'attributs partiels du catalogue global : les GC " +
                    "doivent re-synchroniser cet attribut depuis chaque domaine avant de reprendre. État " +
                    "transitoire, souvent lié à une installation d'Exchange ou d'une application.",
                    new[] { "Extension de schéma ayant ajouté un attribut au catalogue global", "GC devant rejoindre un DC de chaque domaine, dont un est injoignable" },
                    new[] { "Attendre la fin de la synchronisation (peut prendre du temps en multi-domaines)", "Vérifier que chaque domaine de la forêt a un DC joignable depuis le GC" },
                    new[] { "repadmin /showrepl <dc-destination>", "repadmin /replsummary" },
                    LearnBase + "replication-error-8464", transient: true),

                Entry(8477,
                    "Réplication en cours (demande postée, en attente de réponse)",
                    "Ce n'est pas une erreur : la demande de réplication a été transmise et la destination " +
                    "attend les données. Cela persiste seulement si la source est lente ou si une mise à " +
                    "jour de schéma prioritaire bloque temporairement les autres partitions.",
                    new[] { "Volume important de changements à appliquer", "Mise à jour du schéma en cours de propagation", "DC chargé ou liaison réseau lente" },
                    new[] { "Relancer l'audit dans quelques minutes", "Si l'état persiste plusieurs heures : vérifier la charge du DC et l'état de la partition Schema" },
                    new[] { "repadmin /queue <dc-destination>", "repadmin /replsummary" },
                    LearnBase + "replication-error-8477", transient: true),

                Entry(8524,
                    "Échec de résolution DNS",
                    "Le DC de destination n'a pas réussi à résoudre le nom du DC source via DNS. AD utilise " +
                    "un alias CNAME spécial (<GUID>._msdcs.<forêt>) pour localiser chaque DC : s'il est " +
                    "absent ou pointe vers une mauvaise adresse, la réplication échoue.",
                    new[] { "Enregistrement CNAME du DC source absent de la zone _msdcs", "Zone _msdcs non répliquée ou non hébergée par le DNS utilisé par la destination", "Serveurs DNS configurés sur la destination incorrects", "Enregistrement A du DC source obsolète" },
                    new[] { "Vérifier les enregistrements A et CNAME du DC source (onglet DNS d'ADReplic)", "Vérifier les serveurs DNS configurés sur la carte réseau de la destination", "Ré-enregistrer les enregistrements DNS de la source", "Vérifier la réplication de la zone _msdcs" },
                    new[] { "dcdiag /test:DNS /s:<dc-destination> /DnsBasic", "nslookup -type=cname <guid-dsa>._msdcs.<foret>", "ipconfig /registerdns", "nltest /dsregdns" },
                    LearnBase + "replication-error-8524"),

                Entry(8545,
                    "Mise à jour non applicable (objet déplacé entre domaines)",
                    "La source envoie des changements pour un objet récemment déplacé d'un domaine à un " +
                    "autre, alors que la destination le connaît encore dans son ancienne partition.",
                    new[] { "Migration inter-domaines (ADMT, Move-ADObject) pas encore propagée partout" },
                    new[] { "Identifier l'objet concerné dans le journal Directory Service de la destination", "Attendre la propagation ou forcer la réplication de la partition source" },
                    new[] { "repadmin /showrepl <dc-destination>", "repadmin /syncall <dc-destination> /AdeP" },
                    LearnBase + "active-directory-replication-error-8545", transient: true),

                Entry(8606,
                    "Objets persistants détectés (lingering objects)",
                    "La source tente d'envoyer la mise à jour d'un objet que la destination a déjà supprimé " +
                    "et purgé définitivement. La destination, en mode strict, refuse pour ne pas faire " +
                    "réapparaître un objet fantôme. Signe qu'un DC est resté trop longtemps sans répliquer.",
                    new[] { "Un DC n'a pas répliqué pendant plus longtemps que la durée de vie des tombstones (60 ou 180 jours)", "Saut dans le temps système d'un DC", "Restauration d'une sauvegarde trop ancienne" },
                    new[] { "Identifier le DC qui héberge les objets persistants (événement 1988 sur la destination)", "Supprimer les objets persistants depuis un DC de référence sain", "Vérifier qu'aucun DC n'est resté hors réplication pendant plus de la moitié de la durée de vie des tombstones", "Mettre en place une surveillance quotidienne" },
                    new[] { "repadmin /removelingeringobjects <dc-cible> <guid-dc-reference> <dn-partition> /advisory_mode", "repadmin /removelingeringobjects <dc-cible> <guid-dc-reference> <dn-partition>", "repadmin /showrepl * /csv > replsum.csv" },
                    LearnBase + "replication-error-8606", dataLossRisk: true),

                Entry(8614,
                    "Durée de vie des tombstones dépassée",
                    "Ce DC n'a pas répliqué avec la source depuis plus longtemps que la durée de vie des " +
                    "tombstones. AD bloque la réplication pour empêcher la réapparition d'objets supprimés. " +
                    "C'est l'état le plus grave : le DC est potentiellement irrécupérable sans intervention.",
                    new[] { "Panne de réplication ignorée pendant 60 ou 180 jours", "DC resté éteint ou isolé trop longtemps (site distant, VM archivée)", "Saut dans le temps système après la dernière réplication réussie" },
                    new[] { "Vérifier la date de dernière réplication réussie et l'horloge du DC", "Si le DC n'a rien d'unique à conserver : le rétrograder de force, nettoyer ses métadonnées, puis le repromouvoir", "Sinon : supprimer les objets persistants puis autoriser exceptionnellement la réplication (procédure Microsoft)", "Ne jamais réactiver la réplication sans avoir traité les objets persistants" },
                    new[] { "repadmin /showrepl <dc-destination>", "repadmin /showutdvec <dc-destination> <dn-partition>", "repadmin /removelingeringobjects <dc-cible> <guid-dc-reference> <dn-partition> /advisory_mode" },
                    LearnBase + "replication-error-8614", dataLossRisk: true),

                Entry(-2146893022,
                    "Nom principal cible incorrect (Kerberos)",
                    "Kerberos a émis un ticket pour le DC source, mais celui-ci n'a pas pu le déchiffrer : " +
                    "le mot de passe du compte machine ou le nom de service ne correspondent pas. Les " +
                    "deux DC existent bien, mais ne partagent plus le même secret.",
                    new[] { "Mot de passe du compte machine du DC source désynchronisé entre le KDC et la source", "Nom résolu vers une autre machine que le DC source (DNS, hosts)", "Tickets Kerberos obsolètes en cache sur la destination", "Décalage d'horloge entre les DC" },
                    new[] { "Vérifier que le nom du DC source résout vers la bonne IP", "Purger les tickets Kerberos du compte système sur la destination", "Identifier quel KDC la destination utilise", "Réinitialiser le mot de passe machine de la source si le problème persiste" },
                    new[] { "nslookup <dc-source>", "klist -li 0x3e7 purge", "nltest /dsgetdc:<domaine> /kdc", "netdom resetpwd /server:<dc-sain> /userd:<domaine>\\<admin> /passwordd:*" },
                    LearnBase + "replication-error-2146893022")
            };

            return list.ToDictionary(e => e.Code);
        }
    }
}

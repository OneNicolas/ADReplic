using System;

namespace ADReplic.Core.Models
{
    /// <summary>
    /// Résultat d'une vérification DNS unitaire pour la santé AD.
    /// Une instance par couple (nom interrogé, résultat). Un SRV qui retourne
    /// plusieurs cibles produit plusieurs DnsCheckResult ; un échec ou un
    /// "Missing" produit une ligne unique avec Target/Port à null.
    /// </summary>
    public sealed class DnsCheckResult
    {
        /// <summary>Nom DNS interrogé (ex: _ldap._tcp.dc._msdcs.exemple.local).</summary>
        public string RecordName { get; set; }

        /// <summary>Catégorie fonctionnelle du record (pour grouper côté UI).</summary>
        public DnsCheckedRecordType Type { get; set; }

        /// <summary>
        /// Hôte cible : pour un SRV = pNameTarget retourné ; pour un A = nom interrogé
        /// (le RecordName lui-même). Null si Status != Ok.
        /// </summary>
        public string Target { get; set; }

        /// <summary>Port du service (SRV uniquement, null pour A).</summary>
        public int? Port { get; set; }

        /// <summary>Priorité SRV (plus petit = plus prioritaire). Null pour A.</summary>
        public ushort? Priority { get; set; }

        /// <summary>Poids SRV (départage les enregistrements de même priorité). Null pour A.</summary>
        public ushort? Weight { get; set; }

        /// <summary>Adresse IPv4 (A records uniquement). Null pour SRV.</summary>
        public string IpAddress { get; set; }

        /// <summary>Statut consolidé : Ok / Missing (NXDOMAIN, no records) / Error (SERVFAIL, infra).</summary>
        public DnsCheckStatus Status { get; set; }

        /// <summary>Code d'erreur Win32 brut (0 si succès). Utile pour le diagnostic.</summary>
        public int ErrorCode { get; set; }

        /// <summary>Message lisible (localisé OS) pour ErrorCode != 0. Null sinon.</summary>
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Catégorie fonctionnelle du record vérifié.
    /// Évite à l'UI de parser le RecordName pour savoir si c'est un _ldap, _gc, etc.
    /// </summary>
    public enum DnsCheckedRecordType
    {
        SrvLdap = 0,
        SrvKerberos = 1,
        SrvGc = 2,
        SrvKpasswd = 3,
        ARecord = 4
    }

    /// <summary>
    /// Verdict d'une vérification DNS.
    /// - Ok : au moins un record retourné.
    /// - Missing : le nom ou le type de record n'existe pas (NXDOMAIN, NO_RECORDS).
    ///   Conceptuellement c'est un constat, pas une panne d'infra DNS.
    /// - Error : panne de résolution (SERVFAIL, pas de serveur DNS, timeout, etc.).
    /// </summary>
    public enum DnsCheckStatus
    {
        Ok = 0,
        Missing = 1,
        Error = 2
    }
}

using System.Collections.Generic;

namespace ADReplic.Core.Health.Dns
{
    /// <summary>
    /// Abstraction d'un résolveur DNS, isolée de la couche P/Invoke Win32 pour
    /// permettre le mock en tests unitaires. Le contrat est synchrone car
    /// l'API native (DnsQuery_W) l'est ; l'asynchronisme est géré au niveau
    /// supérieur (DnsHealthProbe via Task.Run + SemaphoreSlim).
    /// </summary>
    public interface IDnsResolver
    {
        /// <summary>
        /// Interroge le DNS pour le nom et le type donnés.
        /// Ne lève jamais : un échec se traduit par un DnsQueryResult.Status = Error
        /// avec ErrorCode renseigné, pour que l'appelant puisse classifier sans try/catch.
        /// </summary>
        DnsQueryResult Query(string name, DnsQueryRecordType type);
    }

    /// <summary>Types de records DNS supportés par les sondes ADReplic.</summary>
    public enum DnsQueryRecordType : ushort
    {
        A = 0x0001,
        Srv = 0x0021
    }

    /// <summary>
    /// Statut bas niveau d'une requête DNS, miroir condensé des codes Win32.
    /// Mappé vers DnsCheckStatus côté DnsHealthProbe.
    /// </summary>
    public enum DnsQueryStatus
    {
        Ok = 0,
        Missing = 1,
        Error = 2
    }

    /// <summary>
    /// Résultat d'une requête DNS sous forme neutre (sans types Win32).
    /// </summary>
    public sealed class DnsQueryResult
    {
        public DnsQueryStatus Status { get; set; }

        /// <summary>Code Win32 brut (DNS_ERROR_* / ERROR_SUCCESS). 0 si Status = Ok.</summary>
        public int ErrorCode { get; set; }

        public IReadOnlyList<DnsRawRecord> Records { get; set; }

        public DnsQueryResult()
        {
            Records = System.Array.Empty<DnsRawRecord>();
        }
    }

    /// <summary>
    /// Un record DNS retourné par le résolveur, sous forme neutre.
    /// Les champs non pertinents pour le type courant sont laissés à null.
    /// </summary>
    public sealed class DnsRawRecord
    {
        // SRV
        public string Target { get; set; }
        public int? Port { get; set; }
        public ushort? Priority { get; set; }
        public ushort? Weight { get; set; }

        // A
        public string IpAddress { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ADReplic.Core.Health.Dns
{
    /// <summary>
    /// Résolveur DNS adossé à l'API Win32 DnsQuery_W (dnsapi.dll).
    /// Choix de cette API plutôt qu'un client DNS managé :
    /// - zéro dépendance NuGet, cohérent avec la philosophie portable
    ///   (System.Net.Dns ne supporte pas les records SRV)
    /// - même résolveur que les outils Windows natifs (nslookup, repadmin)
    ///
    /// Cette classe est volontairement la SEULE à manipuler des types Win32.
    /// Toute la nastiness de marshaling et de libération mémoire vit ici ;
    /// le reste du Core consomme l'abstraction IDnsResolver.
    /// </summary>
    public sealed class Win32DnsResolver : IDnsResolver
    {
        // Constantes Win32 (windns.h)
        private const ushort DNS_TYPE_A = 0x0001;
        private const ushort DNS_TYPE_SRV = 0x0021;
        private const uint DNS_QUERY_STANDARD = 0x00000000;

        // Codes de retour qui signifient "le record n'existe pas" (vs vraie panne d'infra)
        private const int ERROR_SUCCESS = 0;
        private const int DNS_ERROR_RCODE_NAME_ERROR = 9003;        // NXDOMAIN
        private const int DNS_INFO_NO_RECORDS = 9501;               // nom OK, pas de record du type
        private const int DNS_ERROR_RECORD_DOES_NOT_EXIST = 9701;

        public DnsQueryResult Query(string name, DnsQueryRecordType type)
        {
            if (string.IsNullOrWhiteSpace(name))
                return ErrorResult(ErrorCodes.Win32InvalidParameter);

            IntPtr queryResultsPtr = IntPtr.Zero;
            try
            {
                var status = DnsQuery_W(
                    pszName: name,
                    wType: (ushort)type,
                    Options: DNS_QUERY_STANDARD,
                    pExtra: IntPtr.Zero,
                    ppQueryResults: out queryResultsPtr,
                    pReserved: IntPtr.Zero);

                if (IsRecordAbsent(status))
                    return new DnsQueryResult { Status = DnsQueryStatus.Missing, ErrorCode = status };

                if (status != ERROR_SUCCESS)
                    return new DnsQueryResult { Status = DnsQueryStatus.Error, ErrorCode = status };

                var records = ParseRecordList(queryResultsPtr, type, name);
                if (records.Count == 0)
                {
                    // Cas théoriquement déjà couvert par DNS_INFO_NO_RECORDS, mais ceinture+bretelles.
                    return new DnsQueryResult { Status = DnsQueryStatus.Missing, ErrorCode = DNS_INFO_NO_RECORDS };
                }

                return new DnsQueryResult { Status = DnsQueryStatus.Ok, ErrorCode = 0, Records = records };
            }
            catch (Exception)
            {
                return ErrorResult(ErrorCodes.GenericFailure);
            }
            finally
            {
                if (queryResultsPtr != IntPtr.Zero)
                    DnsRecordListFree(queryResultsPtr, DnsFreeType.DnsFreeRecordList);
            }
        }

        // -- Parsing de la liste chaînée DNS_RECORD ------------------------------

        private static List<DnsRawRecord> ParseRecordList(IntPtr head, DnsQueryRecordType type, string queryName)
        {
            var result = new List<DnsRawRecord>();
            int headerSize = Marshal.SizeOf(typeof(DNS_RECORDW_Header));

            IntPtr current = head;
            while (current != IntPtr.Zero)
            {
                var header = (DNS_RECORDW_Header)Marshal.PtrToStructure(current, typeof(DNS_RECORDW_Header));

                // Le serveur peut retourner des records additionnels d'un type différent
                // (ex: A pour les SRV targets). On filtre sur le type demandé pour rester déterministe.
                if (header.wType == (ushort)type)
                {
                    var dataPtr = IntPtr.Add(current, headerSize);
                    var record = type == DnsQueryRecordType.Srv
                        ? ReadSrvRecord(dataPtr)
                        : ReadARecord(dataPtr, queryName);

                    if (record != null) result.Add(record);
                }

                current = header.pNext;
            }

            return result;
        }

        private static DnsRawRecord ReadSrvRecord(IntPtr dataPtr)
        {
            var srv = (DNS_SRV_DATAW)Marshal.PtrToStructure(dataPtr, typeof(DNS_SRV_DATAW));
            return new DnsRawRecord
            {
                Target = Marshal.PtrToStringUni(srv.pNameTarget),
                Port = srv.wPort,
                Priority = srv.wPriority,
                Weight = srv.wWeight
            };
        }

        private static DnsRawRecord ReadARecord(IntPtr dataPtr, string queryName)
        {
            var a = (DNS_A_DATA)Marshal.PtrToStructure(dataPtr, typeof(DNS_A_DATA));
            return new DnsRawRecord
            {
                Target = queryName,
                IpAddress = FormatIpv4HostOrder(a.IpAddress)
            };
        }

        /// <summary>
        /// IP4_ADDRESS est un DWORD en host byte order : l'octet de poids fort
        /// est le premier de l'IP affichée (0x7F000001 = 127.0.0.1).
        /// </summary>
        internal static string FormatIpv4HostOrder(uint ip)
        {
            byte a = (byte)((ip >> 24) & 0xFF);
            byte b = (byte)((ip >> 16) & 0xFF);
            byte c = (byte)((ip >> 8) & 0xFF);
            byte d = (byte)(ip & 0xFF);
            return a + "." + b + "." + c + "." + d;
        }

        private static bool IsRecordAbsent(int status)
        {
            return status == DNS_ERROR_RCODE_NAME_ERROR
                || status == DNS_INFO_NO_RECORDS
                || status == DNS_ERROR_RECORD_DOES_NOT_EXIST;
        }

        private static DnsQueryResult ErrorResult(int errorCode)
            => new DnsQueryResult { Status = DnsQueryStatus.Error, ErrorCode = errorCode };

        // Codes "internes" utilisés quand l'erreur ne vient pas de DnsQuery_W
        private static class ErrorCodes
        {
            public const int Win32InvalidParameter = 87;  // ERROR_INVALID_PARAMETER
            public const int GenericFailure = -1;         // distinct des codes Win32
        }

        // -- P/Invoke ------------------------------------------------------------

        [DllImport("Dnsapi.dll", EntryPoint = "DnsQuery_W", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern int DnsQuery_W(
            [MarshalAs(UnmanagedType.LPWStr)] string pszName,
            ushort wType,
            uint Options,
            IntPtr pExtra,
            out IntPtr ppQueryResults,
            IntPtr pReserved);

        [DllImport("Dnsapi.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern void DnsRecordListFree(IntPtr pRecordList, DnsFreeType FreeType);

        private enum DnsFreeType
        {
            DnsFreeFlat = 0,
            DnsFreeRecordList = 1,
            DnsFreeParsedMessageFields = 2
        }

        /// <summary>
        /// Entête de DNS_RECORDW. La portion "Data" qui suit cet entête est
        /// une union dépendante du type, qu'on lit séparément via PtrToStructure
        /// sur l'offset (header_size). Pack=0 => alignement naturel pour matcher Win32.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DNS_RECORDW_Header
        {
            public IntPtr pNext;
            public IntPtr pName;
            public ushort wType;
            public ushort wDataLength;
            public uint Flags;
            public uint dwTtl;
            public uint dwReserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DNS_SRV_DATAW
        {
            public IntPtr pNameTarget;
            public ushort wPriority;
            public ushort wWeight;
            public ushort wPort;
            public ushort Pad;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DNS_A_DATA
        {
            public uint IpAddress;
        }
    }
}

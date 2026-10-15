using System;
using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace ADReplic.Core.Health.Clock
{
    /// <summary>
    /// Lit l'attribut currentTime du RootDSE d'un DC en LDAP simple (port 389).
    /// Choisi plutôt que NetRemoteTOD : même information, mais sur le port LDAP
    /// déjà exigé par tout l'outil, sans ouvrir SMB (445). Le RootDSE est lisible
    /// sans authentification : aucun identifiant n'est nécessaire, même hors domaine.
    /// </summary>
    public sealed class LdapRootDseClockReader : IDcClockReader
    {
        private const int LdapPort = 389;

        public Task<DateTime?> ReadCurrentTimeUtcAsync(string hostName, TimeSpan timeout, CancellationToken cancellationToken)
        {
            // LdapConnection est synchrone : on l'isole sur le pool, le timeout borne l'attente.
            return Task.Run(() => Read(hostName, timeout), cancellationToken);
        }

        private static DateTime? Read(string hostName, TimeSpan timeout)
        {
            using (var connection = new LdapConnection(new LdapDirectoryIdentifier(hostName, LdapPort)))
            {
                connection.AuthType = AuthType.Anonymous;
                connection.Timeout = timeout;
                connection.SessionOptions.ProtocolVersion = 3;

                var request = new SearchRequest(null, "(objectClass=*)", SearchScope.Base, "currentTime");
                var response = (SearchResponse)connection.SendRequest(request, timeout);
                if (response.Entries.Count == 0) return null;

                var values = response.Entries[0].Attributes["currentTime"];
                if (values == null || values.Count == 0) return null;
                return ParseGeneralizedTime(values[0] as string);
            }
        }

        /// <summary>Format GeneralizedTime du RootDSE : "20261015181200.0Z".</summary>
        public static DateTime? ParseGeneralizedTime(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var text = value.Trim();
            var dot = text.IndexOf('.');
            if (dot > 0) text = text.Substring(0, dot) + "Z";
            if (DateTime.TryParseExact(text, "yyyyMMddHHmmss'Z'", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }
            return null;
        }
    }
}

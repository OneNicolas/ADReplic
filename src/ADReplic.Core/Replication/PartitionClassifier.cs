using System;

namespace ADReplic.Core.Replication
{
    /// <summary>
    /// Classification des partitions Active Directory selon leur Distinguished Name.
    /// Exposé publiquement pour testabilité et pour pouvoir le réutiliser ailleurs
    /// (par exemple dans des règles de scoring spécifiques à un type de partition).
    /// </summary>
    public static class PartitionClassifier
    {
        public const string SchemaLabel       = "Schéma";
        public const string ConfigurationLabel = "Configuration";
        public const string DnsAppLabel       = "Application (DNS)";
        public const string DomainLabel       = "Domaine";
        public const string AppLabel          = "Application";
        public const string UnknownLabel      = "Inconnu";

        public static string Classify(string partitionDn)
        {
            if (string.IsNullOrWhiteSpace(partitionDn)) return UnknownLabel;

            // Ordre important : Schema d'abord car son DN commence par CN=Configuration aussi.
            if (partitionDn.StartsWith("CN=Schema,CN=Configuration,", StringComparison.OrdinalIgnoreCase))
                return SchemaLabel;
            if (partitionDn.StartsWith("CN=Configuration,", StringComparison.OrdinalIgnoreCase))
                return ConfigurationLabel;
            if (partitionDn.StartsWith("DC=DomainDnsZones,", StringComparison.OrdinalIgnoreCase) ||
                partitionDn.StartsWith("DC=ForestDnsZones,", StringComparison.OrdinalIgnoreCase))
                return DnsAppLabel;
            if (partitionDn.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
                return DomainLabel;
            return AppLabel;
        }
    }
}

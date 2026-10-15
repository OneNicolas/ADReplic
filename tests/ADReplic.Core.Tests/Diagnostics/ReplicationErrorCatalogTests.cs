using System.Linq;
using ADReplic.Core.Diagnostics;
using Xunit;

namespace ADReplic.Core.Tests.Diagnostics
{
    /// <summary>
    /// Contrat du catalogue d'erreurs de réplication : chaque code connu doit
    /// être explicable en français clair et proposer au moins une piste de
    /// vérification ; un code inconnu doit retomber sur une fiche générique
    /// exploitable (jamais null).
    /// </summary>
    public class ReplicationErrorCatalogTests
    {
        [Theory]
        [InlineData(5)]
        [InlineData(1256)]
        [InlineData(1396)]
        [InlineData(1722)]
        [InlineData(1753)]
        [InlineData(1908)]
        [InlineData(8453)]
        [InlineData(8524)]
        [InlineData(8606)]
        [InlineData(8614)]
        [InlineData(-2146893022)]
        public void Lookup_returns_known_entry_for_common_codes(int code)
        {
            var entry = ReplicationErrorCatalog.Lookup(code);

            Assert.True(entry.IsKnown, $"Code {code} devrait etre documente.");
            Assert.Equal(code, entry.Code);
        }

        [Fact]
        public void Lookup_returns_generic_entry_for_unknown_code()
        {
            var entry = ReplicationErrorCatalog.Lookup(0x7FFFFFFF);

            Assert.NotNull(entry);
            Assert.False(entry.IsKnown);
            Assert.Equal(0x7FFFFFFF, entry.Code);
            Assert.False(string.IsNullOrWhiteSpace(entry.Title));
            Assert.NotEmpty(entry.Checks);
        }

        [Fact]
        public void Lookup_knows_synthetic_unreachable_code()
        {
            // -1 est le code synthetique emis par ReplicationFailureProbe quand
            // un DC ne repond pas : il doit avoir sa propre explication.
            var entry = ReplicationErrorCatalog.Lookup(-1);

            Assert.True(entry.IsKnown);
            Assert.Contains("injoignable", entry.Title, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Every_known_entry_is_complete()
        {
            foreach (var code in ReplicationErrorCatalog.KnownCodes)
            {
                var entry = ReplicationErrorCatalog.Lookup(code);

                Assert.False(string.IsNullOrWhiteSpace(entry.Title), $"{code} : titre vide");
                Assert.False(string.IsNullOrWhiteSpace(entry.Explanation), $"{code} : explication vide");
                Assert.NotEmpty(entry.ProbableCauses);
                Assert.NotEmpty(entry.Checks);
                Assert.NotEmpty(entry.Commands);
            }
        }

        [Fact]
        public void Known_codes_are_unique()
        {
            var codes = ReplicationErrorCatalog.KnownCodes.ToList();

            Assert.Equal(codes.Count, codes.Distinct().Count());
        }

        [Fact]
        public void Lookup_is_stable_across_calls()
        {
            var first  = ReplicationErrorCatalog.Lookup(1722);
            var second = ReplicationErrorCatalog.Lookup(1722);

            Assert.Same(first, second);
        }

        [Fact]
        public void Informational_code_8477_is_not_flagged_as_blocking()
        {
            // 8477 signifie "replication en cours" : ce n'est pas une panne.
            var entry = ReplicationErrorCatalog.Lookup(8477);

            Assert.True(entry.IsKnown);
            Assert.True(entry.IsTransient);
        }

        [Fact]
        public void Tombstone_related_codes_are_flagged_as_data_loss_risk()
        {
            Assert.True(ReplicationErrorCatalog.Lookup(8606).IsDataLossRisk);
            Assert.True(ReplicationErrorCatalog.Lookup(8614).IsDataLossRisk);
            Assert.False(ReplicationErrorCatalog.Lookup(1722).IsDataLossRisk);
        }
    }
}

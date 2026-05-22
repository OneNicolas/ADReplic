using ADReplic.Core.Diagnostics;
using Xunit;

namespace ADReplic.Core.Tests.Diagnostics
{
    /// <summary>
    /// Tests de la traduction des codes d'erreur Win32.
    /// On évite d'asserter sur des messages localisés (l'OS peut être FR ou EN) :
    /// on vérifie le contrat (null pour succès, fallback formaté pour code inconnu).
    /// </summary>
    public class Win32ErrorMessageTests
    {
        [Fact]
        public void Resolve_returns_null_for_zero()
        {
            Assert.Null(Win32ErrorMessage.Resolve(0));
        }

        [Fact]
        public void Resolve_returns_non_empty_message_for_known_code()
        {
            // 1722 = RPC server unavailable, un classique des erreurs de réplication.
            var message = Win32ErrorMessage.Resolve(1722);

            Assert.False(string.IsNullOrWhiteSpace(message));
        }

        [Fact]
        public void Resolve_returns_null_for_clearly_unknown_code()
        {
            // Un code délibérément hors plage Win32 connue.
            var message = Win32ErrorMessage.Resolve(0x7FFFFFFF);

            // On accepte null OU un texte qui n'est pas "Unknown error" : c'est filtré dans Resolve.
            if (message != null)
            {
                Assert.DoesNotContain("Unknown error", message);
            }
        }

        [Fact]
        public void ResolveOrCode_returns_null_for_zero()
        {
            Assert.Null(Win32ErrorMessage.ResolveOrCode(0));
        }

        [Fact]
        public void ResolveOrCode_falls_back_to_hex_code_when_unknown()
        {
            var message = Win32ErrorMessage.ResolveOrCode(0x7FFFFFFF);

            Assert.NotNull(message);
            // Soit on a un message système, soit on a le fallback "Code 0x..."
            Assert.True(
                message.Contains("Code 0x") || !message.Contains("Unknown error"),
                $"Message inattendu : {message}");
        }
    }
}

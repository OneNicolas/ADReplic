using System;
using System.DirectoryServices.ActiveDirectory;
using ADReplic.Core.Diagnostics;
using Xunit;

namespace ADReplic.Core.Tests.Diagnostics
{
    /// <summary>
    /// Tests de la classification des erreurs AD remontées dans le pipeline.
    /// On vérifie que les exceptions typées et les messages bruts sont
    /// correctement catégorisés, peu importe la langue du système.
    /// </summary>
    public class AuditErrorClassifierTests
    {
        [Fact]
        public void Typed_server_down_is_recognized()
        {
            var ex = new ActiveDirectoryServerDownException("server down");
            Assert.Equal(AuditErrorCategory.ServerDown, AuditErrorClassifier.Classify(ex));
        }

        [Fact]
        public void Typed_object_not_found_is_recognized()
        {
            var ex = new ActiveDirectoryObjectNotFoundException("forest missing");
            Assert.Equal(AuditErrorCategory.ForestOrObjectNotFound, AuditErrorClassifier.Classify(ex));
        }

        [Theory]
        [InlineData("Logon failure: unknown user name or bad password")]
        [InlineData("Échec d'ouverture de session : nom d'utilisateur ou mot de passe incorrect")]
        [InlineData("Error 1326: The user name or password is incorrect")]
        public void Authentication_failure_is_recognized_across_locales(string message)
        {
            var ex = new Exception(message);
            Assert.Equal(AuditErrorCategory.AuthenticationFailed, AuditErrorClassifier.Classify(ex));
        }

        [Theory]
        [InlineData("Access is denied")]
        [InlineData("Accès refusé.")]
        public void Access_denied_is_recognized(string message)
        {
            var ex = new Exception(message);
            Assert.Equal(AuditErrorCategory.AccessDenied, AuditErrorClassifier.Classify(ex));
        }

        [Fact]
        public void Rpc_server_unavailable_maps_to_server_down()
        {
            var ex = new Exception("The RPC server is unavailable. (Exception from HRESULT: 0x800706BA)");
            Assert.Equal(AuditErrorCategory.ServerDown, AuditErrorClassifier.Classify(ex));
        }

        [Fact]
        public void Null_returns_unknown()
        {
            Assert.Equal(AuditErrorCategory.Unknown, AuditErrorClassifier.Classify(null));
        }

        [Fact]
        public void Explain_returns_actionable_text_for_known_category()
        {
            var ex = new Exception("Logon failure");
            var explained = AuditErrorClassifier.Explain(ex);

            // Le message doit contenir une indication concrète d'action.
            Assert.Contains("format", explained, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Explain_falls_back_to_raw_message_when_unknown()
        {
            var ex = new Exception("Something completely unexpected");
            Assert.Equal("Something completely unexpected", AuditErrorClassifier.Explain(ex));
        }
    }
}

using ADReplic.Core.Models;

namespace ADReplic.Core.Abstractions
{
    /// <summary>
    /// Sérialise un AuditSnapshot vers un format donné. Une implémentation par format.
    /// </summary>
    public interface IAuditExporter
    {
        string Format { get; }
        string DefaultFileExtension { get; }
        void Export(AuditSnapshot snapshot, string targetPath);
    }
}

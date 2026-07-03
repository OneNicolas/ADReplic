using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ADReplic.Core.Abstractions;
using ADReplic.Core.Export;
using ADReplic.Core.Models;

namespace ADReplic.Core.Monitoring
{
    /// <summary>
    /// Magasin de snapshots persisté sur disque (JSON UTF-8). Utilise par défaut
    /// le dossier <c>%APPDATA%\ADReplic\snapshots\</c> et conserve au maximum
    /// <see cref="DefaultMaxKept"/> fichiers (les plus anciens sont purgés à
    /// chaque Save).
    /// </summary>
    public sealed class SnapshotStore : ISnapshotStore
    {
        public const int DefaultMaxKept = 100;

        private const string FilePrefix = "ADReplic-";
        private const string FileExtension = ".json";
        private const string TimestampFormat = "yyyy-MM-dd_HHmmss-fff";
        private static readonly string SearchPattern = FilePrefix + "*" + FileExtension;

        private readonly string _directory;
        private readonly int _maxKept;
        private readonly IAuditExporter _exporter;
        private readonly IJsonAuditReader _reader;

        public SnapshotStore()
            : this(GetDefaultDirectory(), DefaultMaxKept) { }

        public SnapshotStore(string directory, int maxKept)
            : this(directory, maxKept, new JsonAuditExporter(), new JsonAuditReader()) { }

        public SnapshotStore(string directory, int maxKept, IAuditExporter exporter, IJsonAuditReader reader)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Repertoire requis.", nameof(directory));
            if (maxKept < 1)
                throw new ArgumentOutOfRangeException(nameof(maxKept), "Doit etre superieur ou egal a 1.");

            _directory = directory;
            _maxKept = maxKept;
            _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        }

        public string BaseDirectory => _directory;

        public string Save(AuditSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            EnsureDirectoryExists();

            var capturedAt = snapshot.GeneratedAt == default(DateTime)
                ? DateTime.Now
                : snapshot.GeneratedAt.ToLocalTime();

            var fullPath = BuildUniqueFilePath(capturedAt);
            _exporter.Export(snapshot, fullPath);
            RotateInternal();
            return fullPath;
        }

        public IReadOnlyList<SnapshotFileInfo> List()
        {
            if (!Directory.Exists(_directory)) return Array.Empty<SnapshotFileInfo>();

            return Directory.EnumerateFiles(_directory, SearchPattern)
                .Select(BuildFileInfo)
                .Where(info => info != null)
                .OrderByDescending(info => info.CapturedAt)
                .ToList();
        }

        public AuditSnapshot Load(string fileNameOrPath)
        {
            if (string.IsNullOrWhiteSpace(fileNameOrPath))
                throw new ArgumentException("Nom de fichier requis.", nameof(fileNameOrPath));

            var path = Path.IsPathRooted(fileNameOrPath)
                ? fileNameOrPath
                : Path.Combine(_directory, fileNameOrPath);

            return _reader.Read(path);
        }

        // === Internals ==========================================================

        private void EnsureDirectoryExists()
        {
            if (!Directory.Exists(_directory)) Directory.CreateDirectory(_directory);
        }

        private string BuildUniqueFilePath(DateTime capturedAtLocal)
        {
            var baseStem = FilePrefix + capturedAtLocal.ToString(TimestampFormat, CultureInfo.InvariantCulture);
            var candidate = Path.Combine(_directory, baseStem + FileExtension);
            if (!File.Exists(candidate)) return candidate;

            // Collision sur la milliseconde (extrêmement rare) : on suffixe.
            for (int i = 2; i < 1000; i++)
            {
                candidate = Path.Combine(_directory, $"{baseStem}-{i}{FileExtension}");
                if (!File.Exists(candidate)) return candidate;
            }
            throw new InvalidOperationException(
                "Trop de snapshots en collision sur la meme milliseconde.");
        }

        private void RotateInternal()
        {
            var files = List();
            if (files.Count <= _maxKept) return;

            foreach (var file in files.Skip(_maxKept))
            {
                try { File.Delete(file.FullPath); }
                catch { /* best-effort : un fichier verrouille ne doit pas bloquer la passe */ }
            }
        }

        private static SnapshotFileInfo BuildFileInfo(string fullPath)
        {
            var name = Path.GetFileName(fullPath);
            if (!TryParseTimestamp(name, out var captured)) return null;
            return new SnapshotFileInfo
            {
                FileName = name,
                FullPath = fullPath,
                CapturedAt = captured
            };
        }

        private static bool TryParseTimestamp(string fileName, out DateTime captured)
        {
            captured = default(DateTime);
            if (!fileName.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (!fileName.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase)) return false;

            var stem = fileName.Substring(
                FilePrefix.Length,
                fileName.Length - FilePrefix.Length - FileExtension.Length);

            // Cas nominal : le stem matche directement le format complet
            // (y compris les millisecondes -fff).
            if (DateTime.TryParseExact(
                    stem, TimestampFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal, out captured))
            {
                return true;
            }

            // Cas de collision : un suffixe numerique "-2", "-10" a ete ajoute
            // apres le timestamp. On tronque le dernier segment numerique et on
            // retente le parse.
            var dashIndex = stem.LastIndexOf('-');
            if (dashIndex > 0)
            {
                var afterDash = stem.Substring(dashIndex + 1);
                if (int.TryParse(afterDash, out _))
                {
                    var truncated = stem.Substring(0, dashIndex);
                    return DateTime.TryParseExact(
                        truncated, TimestampFormat, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeLocal, out captured);
                }
            }

            return false;
        }

        private static string GetDefaultDirectory() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ADReplic", "snapshots");
    }
}

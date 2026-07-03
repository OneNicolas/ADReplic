using System;
using System.Collections.Generic;
using System.Linq;

namespace ADReplic.Core.Monitoring
{
    /// <summary>
    /// Tampon circulaire in-memory pour les N dernières captures de score.
    /// Non thread-safe : conçu pour être consommé depuis le thread UI WPF
    /// (DispatcherTimer) qui produit et lit séquentiellement.
    /// </summary>
    public sealed class AuditHistory
    {
        public const int DefaultCapacity = 50;

        private readonly int _capacity;
        private readonly Queue<ScoreHistoryEntry> _entries;

        public AuditHistory() : this(DefaultCapacity) { }

        public AuditHistory(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(
                nameof(capacity), "La capacite doit etre superieure ou egale a 1.");
            _capacity = capacity;
            _entries = new Queue<ScoreHistoryEntry>(capacity);
        }

        /// <summary>Capacité maximale du tampon. Les entrées plus anciennes sont éjectées au-delà.</summary>
        public int Capacity => _capacity;

        /// <summary>Nombre d'entrées actuellement stockées.</summary>
        public int Count => _entries.Count;

        /// <summary>
        /// Vue ordonnée chronologiquement (plus ancienne d'abord) des entrées en mémoire.
        /// Copie défensive : modifier la liste retournée n'affecte pas le buffer.
        /// </summary>
        public IReadOnlyList<ScoreHistoryEntry> Entries => _entries.ToList();

        /// <summary>Dernière entrée ajoutée, ou null si le buffer est vide.</summary>
        public ScoreHistoryEntry Latest => _entries.LastOrDefault();

        /// <summary>
        /// Ajoute une entrée. Si la capacité est atteinte, la plus ancienne
        /// entrée est silencieusement éjectée.
        /// </summary>
        public void Add(ScoreHistoryEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (_entries.Count >= _capacity) _entries.Dequeue();
            _entries.Enqueue(entry);
        }

        public void Clear() => _entries.Clear();
    }
}

using System.Collections.Generic;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Owns everything needed to resolve a Roots &amp; Impact row: the native
    /// <see cref="FlattenedRowStore{TRow}"/>, the special-name side table for synthetic/region rows, and the
    /// snapshot that ownership-row names are resolved from. Refcounted rather than singly owned, because
    /// <see cref="RootsAndImpactModelBuilder.Build(RootsAndImpactModel, System.Collections.Generic.IList{int})"/>
    /// re-wraps a base model's rows into a derived id-filtered model while the table view controller disposes the
    /// model it replaced on every rebuild; single ownership would free the store out from under that derived
    /// model.
    /// </summary>
    sealed class RootsAndImpactRowSource
    {
        readonly FlattenedRowStore<CompactFlatRow> m_Store;
        readonly List<string> m_SpecialNames;
        readonly CachedSnapshot m_Snapshot;

        /// <summary>
        /// Lazily filled, keyed by store index and populated on first access per row (e.g. tree-mode name sort).
        /// Bounded by the number of rows actually resolved rather than the store size, so binding a single page
        /// of a flattened, paged table over a huge store doesn't recreate a full managed array (see
        /// <see cref="GetCachedName"/>).
        /// </summary>
        readonly Dictionary<long, string> m_NameCache = new();

        int m_RefCount = 1;

        public RootsAndImpactRowSource(FlattenedRowStore<CompactFlatRow> store, List<string> specialNames, CachedSnapshot snapshot)
        {
            m_Store = store;
            m_SpecialNames = specialNames;
            m_Snapshot = snapshot;
        }

        public FlattenedRowStore<CompactFlatRow> Store => m_Store;

        /// <summary>
        /// False once the store has been freed (refcount dropped to zero). Set before the native free so any
        /// redirect still holding this source can fail safe instead of reading through a dangling pointer.
        /// </summary>
        public bool RowsAreAlive { get; private set; } = true;

        public void Retain() => m_RefCount++;

        /// <summary>
        /// Releases one reference; disposes the store once the last reference is released. Further releases are
        /// ignored, so a double-dispose of one model cannot free the store out from under another that still
        /// holds a reference.
        /// </summary>
        public void Release()
        {
            if (m_RefCount <= 0 || --m_RefCount > 0)
                return;
            RowsAreAlive = false;
            m_Store.Dispose();
        }

        /// <summary>Appends a name for a synthetic/region/header row and returns its <see cref="CompactFlatRow.SpecialNameId"/>.</summary>
        public int AddSpecialName(string name)
        {
            m_SpecialNames.Add(name);
            return m_SpecialNames.Count - 1;
        }

        /// <summary>Resolves a row's display name: synthetic/region rows from the special-name side table, ownership rows from the snapshot.</summary>
        public string ResolveName(in CompactFlatRow row)
            => row.SpecialNameId >= 0 ? m_SpecialNames[row.SpecialNameId] : row.Source.GetName(m_Snapshot);

        /// <summary>
        /// Resolves the same type-name candidates <see cref="RootsAndImpactModelBuilder.GetTypeNamesForFiltering"/>
        /// gives tree mode's per-node filter check, so flat mode can match a text search against an ownership
        /// row's type as well as its display name. Synthetic/region rows have no meaningful Source, so this
        /// always returns default for them (SpecialNameId &gt;= 0).
        /// </summary>
        public RootsAndImpactModelBuilder.TypeNamesForFiltering GetTypeNamesForFiltering(in CompactFlatRow row)
            => row.SpecialNameId >= 0 ? default : RootsAndImpactModelBuilder.GetTypeNamesForFiltering(m_Snapshot, row.Source);

        /// <summary>
        /// Resolves and caches a row's display name by store index, for repeated lookups such as a name sort
        /// comparer. Tree-mode group/namespace headers resolve their own name while the build is still appending
        /// rows for later buckets, so the cache has to be able to grow rather than assume the store is complete.
        /// </summary>
        public string GetCachedName(long index)
        {
            if (!m_NameCache.TryGetValue(index, out var name))
            {
                name = ResolveName(in m_Store[index]);
                m_NameCache[index] = name;
            }
            return name;
        }
    }
}

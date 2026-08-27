using System;
using System.Collections.Generic;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Owns the native <see cref="FlattenedRowStore{TRow}"/> backing a flattened, paged comparison table, plus the
    /// parallel managed side lists for the parts of a comparison row that <see cref="ComparisonCompactRow"/> can't
    /// carry directly: display name, tree-view id arrays for each side, and the view-specific
    /// <typeparamref name="TModelSpecificData"/> (which may itself carry managed references, e.g.
    /// <c>UnityObjectsComparisonModel.ExtraData.NativeTypeName</c>). Generic only in
    /// <typeparamref name="TModelSpecificData"/> — the row's other managed fields (name, id arrays) are the same
    /// shape for every comparison view.
    /// </summary>
    sealed class ComparisonRowSource<TModelSpecificData> : IDisposable
    {
        readonly List<string> m_Names = new();
        readonly List<int[]> m_IdsA = new();
        readonly List<int[]> m_IdsB = new();
        readonly List<TModelSpecificData> m_Extras = new();

        public ComparisonRowSource(long initialCapacity = 0)
        {
            Store = new FlattenedRowStore<ComparisonCompactRow>(initialCapacity);
        }

        public FlattenedRowStore<ComparisonCompactRow> Store { get; }

        /// <summary>Appends a row's side data, then the row itself, and returns its store index.</summary>
        public long Add(
            int treeId,
            ComparisonSourceIndex source,
            MemorySize totalSizeInA,
            MemorySize totalSizeInB,
            uint countInA,
            uint countInB,
            int childCount,
            string name,
            int[] idsInA,
            int[] idsInB,
            TModelSpecificData extra,
            int statusSortKeyInA,
            int statusSortKeyInB,
            bool hasChanged)
        {
            var nameId = m_Names.Count;
            m_Names.Add(name);

            var idsAId = -1;
            if (idsInA != null)
            {
                idsAId = m_IdsA.Count;
                m_IdsA.Add(idsInA);
            }

            var idsBId = -1;
            if (idsInB != null)
            {
                idsBId = m_IdsB.Count;
                m_IdsB.Add(idsInB);
            }

            var extraId = m_Extras.Count;
            m_Extras.Add(extra);

            var row = new ComparisonCompactRow(
                treeId, source, totalSizeInA, totalSizeInB, countInA, countInB, childCount,
                nameId, idsAId, idsBId, extraId, statusSortKeyInA, statusSortKeyInB, hasChanged);
            return Store.Add(row);
        }

        public string GetName(int nameId) => m_Names[nameId];
        public int[] GetIdsA(int idsAId) => idsAId < 0 ? null : m_IdsA[idsAId];
        public int[] GetIdsB(int idsBId) => idsBId < 0 ? null : m_IdsB[idsBId];
        public TModelSpecificData GetExtra(int extraId) => m_Extras[extraId];

        /// <summary>For <see cref="PagedFlatModel{TRow,TItemData}"/>'s name-sort callback.</summary>
        public string ResolveName(ComparisonCompactRow row) => GetName(row.NameId);

        public void Dispose() => Store.Dispose();
    }
}

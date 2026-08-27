using static Unity.MemoryProfiler.Editor.CachedSnapshot;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Pairs the snapshot-level source handles from each snapshot for a single comparison row.
    // InA is SourceIndex.None when the item is absent from snapshot A (added in B).
    // InB is SourceIndex.None when the item is absent from snapshot B (deleted from A).
    readonly struct ComparisonSourceIndex
    {
        public ComparisonSourceIndex(SourceIndex inA, SourceIndex inB)
        {
            InA = inA;
            InB = inB;
        }

        // The source handle in snapshot A. SourceIndex.None if the item is absent from snapshot A.
        public SourceIndex InA { get; }

        // The source handle in snapshot B. SourceIndex.None if the item is absent from snapshot B.
        public SourceIndex InB { get; }
    }
}

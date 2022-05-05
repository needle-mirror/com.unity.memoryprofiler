namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    // Model builder base class for builders which use captured memory
    // snapshot to build breakdown and table representation of the data.
    /// </summary>
    internal abstract class MemoryBreakdownModelBuilderBase
    {
        public MemoryBreakdownModelBuilderBase(CachedSnapshot snapshotA, CachedSnapshot snapshotB)
        {
            SnapshotA = snapshotA;
            SnapshotB = snapshotB;
        }

        protected CachedSnapshot SnapshotA { get; private set; }
        protected CachedSnapshot SnapshotB { get; private set; }

        public abstract MemoryBreakdownModel Build();
    }
}

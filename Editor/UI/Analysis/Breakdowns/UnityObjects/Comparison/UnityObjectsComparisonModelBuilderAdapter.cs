namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Adapter that implements IComparisonModelBuilder for UnityObjectsComparisonModelBuilder.
    /// This allows the builder to be used with ComparisonModelBuildOrchestrator.
    /// </summary>
    class UnityObjectsComparisonModelBuilderAdapter
        : IComparisonModelBuilder<UnityObjectsComparisonModel, UnityObjectsComparisonModelBuilder.BuildArgs>
    {
        readonly UnityObjectsComparisonModelBuilder m_Builder = new UnityObjectsComparisonModelBuilder();

        public UnityObjectsComparisonModel Build(
            CachedSnapshot snapshotA,
            CachedSnapshot snapshotB,
            UnityObjectsComparisonModelBuilder.BuildArgs args)
        {
            return m_Builder.Build(snapshotA, snapshotB, args);
        }
    }
}

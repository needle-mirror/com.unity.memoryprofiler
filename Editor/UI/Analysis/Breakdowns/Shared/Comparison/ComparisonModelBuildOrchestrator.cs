using System;
using System.Threading;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Orchestrates the building of comparison models (dual-snapshot comparisons).
    /// Handles model building, cancellation, and debug logging in a consistent way across all comparison tables.
    /// </summary>
    class ComparisonModelBuildOrchestrator<TComparisonModel, TBuildArgs>
        where TComparisonModel : class
    {
        readonly CachedSnapshot m_SnapshotA;
        readonly CachedSnapshot m_SnapshotB;
        readonly IComparisonModelBuilder<TComparisonModel, TBuildArgs> m_Builder;
        readonly string m_ModelTypeName;

        public ComparisonModelBuildOrchestrator(
            CachedSnapshot snapshotA,
            CachedSnapshot snapshotB,
            IComparisonModelBuilder<TComparisonModel, TBuildArgs> builder,
            string modelTypeName)
        {
            m_SnapshotA = snapshotA;
            m_SnapshotB = snapshotB;
            m_Builder = builder;
            m_ModelTypeName = modelTypeName;
        }

        /// <summary>
        /// Creates a task function that builds the comparison model.
        /// The task can be executed on a background thread and supports cancellation.
        /// </summary>
        public Func<TComparisonModel> CreateBuildTask(
            TBuildArgs args,
            CancellationToken cancellationToken)
        {
            // Capture variables locally to avoid closure issues
            var snapshotA = m_SnapshotA;
            var snapshotB = m_SnapshotB;
            var builder = m_Builder;
            var modelTypeName = m_ModelTypeName;

            return () =>
            {
                AsyncTaskHelper.DebugLogAsyncStep($"Start Building {modelTypeName}");
                cancellationToken.ThrowIfCancellationRequested();
                AsyncTaskHelper.DebugLogAsyncStep($"Start Building (not canceled) {modelTypeName}");

                // Build the comparison model
                var model = builder.Build(snapshotA, snapshotB, args);

                cancellationToken.ThrowIfCancellationRequested();
                AsyncTaskHelper.DebugLogAsyncStep($"Building Finished {modelTypeName}");
                return model;
            };
        }
    }

    /// <summary>
    /// Interface for comparison model builders.
    /// Implementations build a comparison model from two snapshots and build arguments.
    /// </summary>
    interface IComparisonModelBuilder<TModel, TBuildArgs>
    {
        TModel Build(CachedSnapshot snapshotA, CachedSnapshot snapshotB, TBuildArgs args);
    }
}

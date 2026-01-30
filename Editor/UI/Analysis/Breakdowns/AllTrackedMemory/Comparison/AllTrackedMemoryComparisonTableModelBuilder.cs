using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    class AllTrackedComparisonTableModel : ComparisonTableModel<AllTrackedMemoryModel, AllTrackedMemoryModel.ItemData>
    {
        public AllTrackedComparisonTableModel(
            List<TreeViewItemData<ComparisonData>> rootNodes, AllTrackedMemoryModel baseModel, MemorySize totalSnapshotSizeA,
            AllTrackedMemoryModel comparedModel, MemorySize totalSnapshotSizeB, long largestAbsoluteSizeDelta)
            : base(rootNodes, baseModel, totalSnapshotSizeA, comparedModel, totalSnapshotSizeB, largestAbsoluteSizeDelta) { }
    }

    static class AllTrackedMemoryComparisonTableModelBuilder
    {
        public static AllTrackedComparisonTableModel Build(
            CachedSnapshot snapshotA,
            CachedSnapshot snapshotB,
            AllTrackedMemoryModelBuilder.BuildArgs snapshotModelBuildArgs,
            TreeComparisonBuilder.BuildArgs comparisonModelBuildArgs)
        {
            var builderA = new AllTrackedMemoryModelBuilder();
            var modelA = builderA.Build(snapshotA, snapshotModelBuildArgs);

            var builderB = new AllTrackedMemoryModelBuilder();
            var modelB = builderB.Build(snapshotB, snapshotModelBuildArgs);

            var treeComparisonBuilder = new TreeComparisonBuilder();
            var comparisonTree = treeComparisonBuilder.Build<AllTrackedMemoryModel.ItemData, AllTrackedMemoryModel>(
                modelA.RootNodes,
                modelB.RootNodes,
                comparisonModelBuildArgs,
                out var largestAbsoluteSizeDelta);

            var comparisonModel = new AllTrackedComparisonTableModel(
                comparisonTree,
                modelA,
                modelA.TotalSnapshotMemorySize,
                modelB,
                modelB.TotalSnapshotMemorySize,
                largestAbsoluteSizeDelta);

            return comparisonModel;
        }
    }
}

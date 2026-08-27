using System.Collections.Generic;
using UnityEngine.UIElements;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;

namespace Unity.MemoryProfiler.Editor.UI
{
    class AllTrackedComparisonTableModel : ComparisonTableModel<AllTrackedMemoryModel, AllTrackedMemoryModel.ItemData, AllTrackedComparisonTableModel.ExtraData>
    {
        public readonly struct ExtraData
        {
            public ExtraData(ObjectStatusWarningInfo warningInfoInA = default, ObjectStatusWarningInfo warningInfoInB = default)
            {
                WarningInfoInA = warningInfoInA;
                WarningInfoInB = warningInfoInB;
            }

            /// <summary>Aggregated warning info for all objects in the group from snapshot A.</summary>
            public ObjectStatusWarningInfo WarningInfoInA { get; }
            /// <summary>Aggregated warning info for all objects in the group from snapshot B.</summary>
            public ObjectStatusWarningInfo WarningInfoInB { get; }
        }

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
            var comparisonTree = treeComparisonBuilder.Build<AllTrackedMemoryModel.ItemData, AllTrackedMemoryModel, AllTrackedComparisonTableModel.ExtraData>(
                modelA.RootNodes,
                modelB.RootNodes,
                GenerateExtraData,
                comparisonModelBuildArgs,
                out var largestAbsoluteSizeDelta,
                snapshotA,
                snapshotB,
                comparisonModelBuildArgs.CompareExtraData ? ed => ObjectStatusExtensions.HasStatusChangedBetweenSnapshots(ed.WarningInfoInA.Bitmask, ed.WarningInfoInB.Bitmask) : null,
                aggregateExtraDataFromChildren: (parent, children) =>
                {
                    var infoA = default(ObjectStatusWarningInfo);
                    var infoB = default(ObjectStatusWarningInfo);
                    foreach (var child in children)
                    {
                        infoA |= child.WarningInfoInA;
                        infoB |= child.WarningInfoInB;
                    }
                    return new AllTrackedComparisonTableModel.ExtraData(infoA, infoB);
                });

            var comparisonModel = new AllTrackedComparisonTableModel(
                comparisonTree,
                modelA,
                modelA.TotalSnapshotMemorySize,
                modelB,
                modelB.TotalSnapshotMemorySize,
                largestAbsoluteSizeDelta);

            return comparisonModel;
        }

        static AllTrackedComparisonTableModel.ExtraData GenerateExtraData(
            List<TreeViewItemData<AllTrackedMemoryModel.ItemData>> itemsA,
            List<TreeViewItemData<AllTrackedMemoryModel.ItemData>> itemsB)
        {
            return new AllTrackedComparisonTableModel.ExtraData(
                AggregateWarningInfo(itemsA),
                AggregateWarningInfo(itemsB));
        }

        static ObjectStatusWarningInfo AggregateWarningInfo(List<TreeViewItemData<AllTrackedMemoryModel.ItemData>> items)
        {
            var result = default(ObjectStatusWarningInfo);
            for (int i = 0; i < items.Count; i++)
                result |= items[i].data.WarningInfo;
            return result;
        }
    }
}

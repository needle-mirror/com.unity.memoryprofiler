using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.Extensions;
using UnityEngine.UIElements;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Builds a UnityObjectsComparisonModel.
    class UnityObjectsComparisonModelBuilder
    {
        /// <summary>Rows materialized into the tree view at once in the flattened, paginated view.</summary>
        const int k_FlattenedPageSize = 2500;

        readonly int m_FlattenedPageSize;

        CachedSnapshot m_SnapshotA;
        CachedSnapshot m_SnapshotB;

        public UnityObjectsComparisonModelBuilder()
            : this(k_FlattenedPageSize)
        {
        }

        /// <summary>Test seam: build the flattened model with a small page size so paging can be exercised on small fixtures.</summary>
        internal UnityObjectsComparisonModelBuilder(int flattenedPageSize)
        {
            m_FlattenedPageSize = flattenedPageSize;
        }

        public UnityObjectsComparisonModel Build(
            CachedSnapshot snapshotA,
            CachedSnapshot snapshotB,
            UnityObjectSpecificBuildArgs args,
            TreeComparisonBuilder.BuildArgs comparisonArgs)
        {
            m_SnapshotA = snapshotA;
            m_SnapshotB = snapshotB;
            // modelA/modelB always keep the full grouped hierarchy, regardless of args.FlattenHierarchy: they are
            // both the merge input for TreeComparisonBuilder and BaseModel/ComparedModel for the sub-table id
            // lookups below, which need every object reachable, not just a single flattened, paged model's page 0.
            var singleSnapshotArgs = new UnityObjectsModelBuilder.BuildArgs(
                    args.SearchFilter,
                    unityObjectNameFilter: null,
                    flattenHierarchy: false,
                    disambiguateByInstanceId: args.DisambiguateByInstanceId);
            var baseModelBuilder = new UnityObjectsModelBuilder();

            var modelA = baseModelBuilder.Build(snapshotA, singleSnapshotArgs);
            var modelB = baseModelBuilder.Build(snapshotB, singleSnapshotArgs);

            var treeComparisonBuilder = new TreeComparisonBuilder();
            var comparisonTree = treeComparisonBuilder.Build<UnityObjectsModel.ItemData, UnityObjectsModel, UnityObjectsComparisonModel.ExtraData>(
                modelA.RootNodes,
                modelB.RootNodes,
                GenerateExtraData,
                comparisonArgs,
                out var largestAbsoluteSizeDelta,
                snapshotA,
                snapshotB,
                comparisonArgs.CompareExtraData ? ed => ObjectStatusExtensions.HasStatusChangedBetweenSnapshots(ed.WarningInfoInA.Bitmask, ed.WarningInfoInB.Bitmask) : null,
                aggregateExtraDataFromChildren: (parent, children) =>
                {
                    var infoA = default(ObjectStatusWarningInfo);
                    var infoB = default(ObjectStatusWarningInfo);
                    foreach (var child in children)
                    {
                        infoA |= child.WarningInfoInA;
                        infoB |= child.WarningInfoInB;
                    }
                    // NativeTypeName comes from the source items, not from children — keep parent's.
                    return new UnityObjectsComparisonModel.ExtraData(parent.NativeTypeName, infoA, infoB);
                });

            if (args.FlattenHierarchy)
            {
                var leaves = TreeModelUtility.RetrieveLeafNodesOfTree(comparisonTree);
                var rowSource = new ComparisonRowSource<UnityObjectsComparisonModel.ExtraData>(leaves.Count);
                var totalSizeA = new MemorySize();
                var totalSizeB = new MemorySize();
                foreach (var leaf in leaves)
                {
                    var data = leaf.data;
                    totalSizeA += data.TotalSizeInA;
                    totalSizeB += data.TotalSizeInB;
                    rowSource.Add(
                        leaf.id,
                        data.SourceIndex,
                        data.TotalSizeInA,
                        data.TotalSizeInB,
                        data.CountInA,
                        data.CountInB,
                        data.ChildCount,
                        data.Name,
                        data.TreeViewIdsInA,
                        data.TreeViewIdsInB,
                        data.ExtraData,
                        data.ExtraData.WarningInfoInA.GetStatusSortKey(),
                        data.ExtraData.WarningInfoInB.GetStatusSortKey(),
                        data.HasChanged);
                }

                return new UnityObjectsComparisonModel(
                    modelA, modelB,
                    rowSource,
                    m_FlattenedPageSize,
                    modelA.TotalSnapshotMemorySize,
                    modelB.TotalSnapshotMemorySize,
                    largestAbsoluteSizeDelta,
                    totalSizeA,
                    totalSizeB);
            }

            return new UnityObjectsComparisonModel(
                modelA, modelB,
                comparisonTree,
                modelA.TotalSnapshotMemorySize,
                modelB.TotalSnapshotMemorySize,
                largestAbsoluteSizeDelta);
        }

        UnityObjectsComparisonModel.ExtraData GenerateExtraData(List<TreeViewItemData<UnityObjectsModel.ItemData>> itemsA, List<TreeViewItemData<UnityObjectsModel.ItemData>> itemsB)
        {
            TreeViewItemData<UnityObjectsModel.ItemData> firstItem;
            CachedSnapshot firstItemsSnapshot;
            if (itemsA.Count > 0)
            {
                firstItem = itemsA[0];
                firstItemsSnapshot = m_SnapshotA;
            }
            else
            {
                firstItem = itemsB[0];
                firstItemsSnapshot = m_SnapshotB;
            }
            return new UnityObjectsComparisonModel.ExtraData(
                GetTypeNameForItem(firstItem, firstItemsSnapshot),
                AggregateWarningInfo(itemsA),
                AggregateWarningInfo(itemsB));
        }

        static ObjectStatusWarningInfo AggregateWarningInfo(List<TreeViewItemData<UnityObjectsModel.ItemData>> items)
        {
            var result = default(ObjectStatusWarningInfo);
            for (int i = 0; i < items.Count; i++)
                result |= items[i].data.WarningInfo;
            return result;
        }

        static string GetTypeNameForItem(TreeViewItemData<UnityObjectsModel.ItemData> item, CachedSnapshot snapshot)
        {
            return item.data.Source.Id switch
            {
                SourceIndex.SourceId.NativeType => item.data.Name,
                SourceIndex.SourceId.NativeObject => snapshot.NativeTypes.TypeName[snapshot.NativeObjects.NativeTypeArrayIndex[item.data.Source.Index]],
                _ => item.hasChildren ? GetTypeNameForItemFromChildren(item, snapshot) : null,
            };
        }

        static string GetTypeNameForItemFromChildren(TreeViewItemData<UnityObjectsModel.ItemData> item, CachedSnapshot snapshot)
        {
            var iterator = item.children.GetEnumerator();
            if (!iterator.MoveNext())
                return null;
            var firstChild = iterator.Current;
            return GetTypeNameForItem(firstChild, snapshot);
        }

        public readonly struct UnityObjectSpecificBuildArgs
        {
            public UnityObjectSpecificBuildArgs(
                CompoundSearchFilter searchFilter,
                bool flattenHierarchy,
                bool disambiguateByInstanceId
                )
            {
                SearchFilter = searchFilter;
                FlattenHierarchy = flattenHierarchy;
                DisambiguateByInstanceId = disambiguateByInstanceId && !flattenHierarchy;
            }
            /// <summary>
            /// Only include tree paths that match this filter.
            /// </summary>
            public CompoundSearchFilter SearchFilter { get; }

            // Flatten hierarchy to leaf nodes only (remove all categorization).
            public bool FlattenHierarchy { get; }

            /// <summary>
            /// Use Instance IDs to differentiate objects with the same name
            /// </summary>
            public bool DisambiguateByInstanceId { get; }
        }
    }
}

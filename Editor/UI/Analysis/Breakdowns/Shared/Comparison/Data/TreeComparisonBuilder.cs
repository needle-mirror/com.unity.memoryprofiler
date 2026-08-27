using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.MemoryProfiler.Editor.Extensions;
using UnityEngine.UIElements;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;
using static Unity.MemoryProfiler.Editor.CallstacksUtility;
using static Unity.MemoryProfiler.Editor.UI.TreeModelHelpers;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Builds a comparison tree from two input trees with nodes of type TreeViewItemData<IComparableItemData>.
    class TreeComparisonBuilder
    {
        /// <summary>
        /// Returns a stable merge key for matching type-level nodes across snapshots.
        /// ManagedType nodes (MonoBehaviours, ScriptableObjects) are qualified with assembly
        /// to prevent cross-assembly collisions. NativeType and object-group nodes use name only.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="item"></param>
        /// <param name="snapshot"></param>
        /// <returns></returns>
        static (string, string) GetComparisonKeyWithManagedTypeAssemblyCollisionCheck<T>(T item, CachedSnapshot snapshot)
            where T : IPrivateComparableItemData
        {
            if (item.Source.Id == SourceIndex.SourceId.ManagedType)
            {
                var index = item.Source.Index;
                return (item.Name, snapshot.TypeDescriptions.Assembly[index]);
            }
            return (item.Name, string.Empty);
        }

        /// <summary>
        /// Overload that defaults to using <see cref="GetComparisonKeyWithManagedTypeAssemblyCollisionCheck{T}"/> with a dual string tuple for differentiating managed types.
        /// </summary>
        public List<TreeViewItemData<ComparisonTableModel<TBaseModel, TBaseModelTreeItemData, TModelSpecificData>.ComparisonData>> Build<TBaseModelTreeItemData, TBaseModel, TModelSpecificData>(
            List<TreeViewItemData<TBaseModelTreeItemData>> treeA,
            List<TreeViewItemData<TBaseModelTreeItemData>> treeB,
            Func<List<TreeViewItemData<TBaseModelTreeItemData>>, List<TreeViewItemData<TBaseModelTreeItemData>>, TModelSpecificData> getExtraData,
            BuildArgs args,
            out long largestAbsoluteSizeDelta,
            CachedSnapshot snapshotA,
            CachedSnapshot snapshotB,
            Func<TModelSpecificData, bool> extraDataHasChanged = null,
            Func<TModelSpecificData, IReadOnlyList<TModelSpecificData>, TModelSpecificData> aggregateExtraDataFromChildren = null)

            where TBaseModelTreeItemData : IPrivateComparableItemData
        {
            return Build<TBaseModelTreeItemData, TBaseModel, TModelSpecificData, (string, string)>(
                treeA, treeB, getExtraData, args, out largestAbsoluteSizeDelta,
                snapshotA,
                snapshotB,
                GetComparisonKeyWithManagedTypeAssemblyCollisionCheck,
                Comparer<(string, string)>.Default,
                extraDataHasChanged,
                aggregateExtraDataFromChildren);
        }

        /// <summary>
        /// Overload that accepts a single key function with per-side context, avoiding the need for two separate key lambdas
        /// when the only difference between them is the snapshot they close over.
        /// </summary>
        List<TreeViewItemData<ComparisonTableModel<TBaseModel, TBaseModelTreeItemData, TModelSpecificData>.ComparisonData>> Build<TBaseModelTreeItemData, TBaseModel, TModelSpecificData, TComparableKey>(
            List<TreeViewItemData<TBaseModelTreeItemData>> treeA,
            List<TreeViewItemData<TBaseModelTreeItemData>> treeB,
            Func<List<TreeViewItemData<TBaseModelTreeItemData>>, List<TreeViewItemData<TBaseModelTreeItemData>>, TModelSpecificData> getExtraData,
            BuildArgs args,
            out long largestAbsoluteSizeDelta,
            CachedSnapshot snapshotA,
            CachedSnapshot snapshotB,
            Func<TBaseModelTreeItemData, CachedSnapshot, TComparableKey> getComparisonKey,
            IComparer<TComparableKey> keyComparer = null,
            Func<TModelSpecificData, bool> extraDataHasChanged = null,
            Func<TModelSpecificData, IReadOnlyList<TModelSpecificData>, TModelSpecificData> aggregateExtraDataFromChildren = null)

            where TBaseModelTreeItemData : IPrivateComparableItemData
            where TComparableKey : IComparable
        {
            return Build<TBaseModelTreeItemData, TBaseModel, TModelSpecificData, TComparableKey>(
                treeA, treeB, getExtraData, args, out largestAbsoluteSizeDelta,
                item => getComparisonKey(item, snapshotA),
                item => getComparisonKey(item, snapshotB),
                keyComparer,
                extraDataHasChanged,
                aggregateExtraDataFromChildren);
        }

        public List<TreeViewItemData<ComparisonTableModel<TBaseModel, TBaseModelTreeItemData, TModelSpecificData>.ComparisonData>> Build<TBaseModelTreeItemData, TBaseModel, TModelSpecificData, TComparableKey>(
            List<TreeViewItemData<TBaseModelTreeItemData>> treeA,
            List<TreeViewItemData<TBaseModelTreeItemData>> treeB,
            Func<List<TreeViewItemData<TBaseModelTreeItemData>>, List<TreeViewItemData<TBaseModelTreeItemData>>, TModelSpecificData> getExtraData,
            BuildArgs args,
            out long largestAbsoluteSizeDelta,
            Func<TBaseModelTreeItemData, TComparableKey> getComparisonKeyA = null,
            Func<TBaseModelTreeItemData, TComparableKey> getComparisonKeyB = null,
            IComparer<TComparableKey> keyComparer = null,
            Func<TModelSpecificData, bool> extraDataHasChanged = null,
            Func<TModelSpecificData, IReadOnlyList<TModelSpecificData>, TModelSpecificData> aggregateExtraDataFromChildren = null)

            where TBaseModelTreeItemData : IPrivateComparableItemData
            where TComparableKey : IComparable
        {
            List<Node<TBaseModel, TBaseModelTreeItemData, TModelSpecificData>> intermediateTree;
            if (getComparisonKeyA != null)
            {
                Debug.Assert(getComparisonKeyB != null, "If getComparisonKeyA is provided, getComparisonKeyB must also be provided.");
                intermediateTree = BuildIntermediateTree<TBaseModelTreeItemData, TBaseModel, TModelSpecificData, TComparableKey>(treeA, treeB, getExtraData, getComparisonKeyA, getComparisonKeyB, keyComparer);
            }
            else
            {
                // If no custom key functions are provided, we default to using the item name as the key, with ordinal string comparison.
                // In this case we can skip the overhead of extracting keys by reusing the name field on the item data and comparing directly on it.
                Debug.Assert(getComparisonKeyB == null, "If getComparisonKeyA is not provided, getComparisonKeyB must also be null.");
                Debug.Assert(keyComparer == null, "If getComparisonKeyA is not provided, keyComparer must also be null.");
                intermediateTree = BuildIntermediateTree<TBaseModelTreeItemData, TBaseModel, TModelSpecificData, string>
                    (treeA, treeB, getExtraData, (d) => d.Name, (d) => d.Name, StringComparer.Ordinal);
            }
            var comparisonTree = BuildUitkTreeFromIntermediateTree(intermediateTree, args, extraDataHasChanged, out largestAbsoluteSizeDelta, aggregateExtraDataFromChildren);
            return comparisonTree;
        }

        static List<Node<TBaseModel, TBaseModelTreeItemData, TModelSpecificData>> BuildIntermediateTree<TBaseModelTreeItemData, TBaseModel, TModelSpecificData, TComparableKey>(
            IEnumerable<TreeViewItemData<TBaseModelTreeItemData>> treeA,
            IEnumerable<TreeViewItemData<TBaseModelTreeItemData>> treeB,
            Func<List<TreeViewItemData<TBaseModelTreeItemData>>, List<TreeViewItemData<TBaseModelTreeItemData>>, TModelSpecificData> getExtraData,
            Func<TBaseModelTreeItemData, TComparableKey> getComparisonKeyA = null,
            Func<TBaseModelTreeItemData, TComparableKey> getComparisonKeyB = null,
            IComparer<TComparableKey> keyComparer = null)
            where TBaseModelTreeItemData : IPrivateComparableItemData
            where TComparableKey : IComparable
        {
            var itemStackA = new Stack<TreeViewItemData<TBaseModelTreeItemData>>();
            var rootA = new TreeViewItemData<TBaseModelTreeItemData>(-1, default, new List<TreeViewItemData<TBaseModelTreeItemData>>(treeA));
            itemStackA.Push(rootA);

            var itemStackB = new Stack<TreeViewItemData<TBaseModelTreeItemData>>();
            var rootB = new TreeViewItemData<TBaseModelTreeItemData>(-1, default, new List<TreeViewItemData<TBaseModelTreeItemData>>(treeB));
            itemStackB.Push(rootB);

            var parentOutputNodeStack = new Stack<Node<TBaseModel, TBaseModelTreeItemData, TModelSpecificData>>();
            var rootOutputNode = new Node<TBaseModel, TBaseModelTreeItemData, TModelSpecificData>(null, default, default);
            parentOutputNodeStack.Push(rootOutputNode);

            // When custom key functions are provided, we sort A and B independently with their
            // respective keys, then use a cross-comparator (keyA vs keyB) for MatchSortedItems.
            // The within-A and within-B dedup comparers must use their respective key functions
            // to avoid passing a B-side index to a snapshot-A lookup (and vice-versa).
            var useCustomKeys = getComparisonKeyA != null || getComparisonKeyB != null;

            var sortByNameComparison = new Comparison<TreeViewItemData<TBaseModelTreeItemData>>((x, y) => string.Compare(
                x.data.Name,
                y.data.Name,
                StringComparison.Ordinal));

            var sortByKeyComparisonA = useCustomKeys
                ? new Comparison<TreeViewItemData<TBaseModelTreeItemData>>((x, y) => keyComparer.Compare(getComparisonKeyA(x.data), getComparisonKeyA(y.data)))
                : sortByNameComparison;

            var sortByKeyComparisonB = useCustomKeys
                ? new Comparison<TreeViewItemData<TBaseModelTreeItemData>>((x, y) => keyComparer.Compare(getComparisonKeyB(x.data), getComparisonKeyB(y.data)))
                : sortByNameComparison;

            // Cross-comparator used for MatchSortedItems: compares keyA of x against keyB of y.
            var matchComparison = useCustomKeys
                ? new Comparison<TreeViewItemData<TBaseModelTreeItemData>>((x, y) => keyComparer.Compare(getComparisonKeyA(x.data), getComparisonKeyB(y.data)))
                : sortByNameComparison;

            var cachedMatchingListForItemsWithNameInA = new List<TreeViewItemData<TBaseModelTreeItemData>>();
            var cachedMatchingListForItemsWithNameInB = new List<TreeViewItemData<TBaseModelTreeItemData>>();

            while (itemStackA.Count > 0 && itemStackB.Count > 0)
            {
                var itemA = itemStackA.Pop();
                var itemB = itemStackB.Pop();
                var parentOutputNode = parentOutputNodeStack.Pop();

                var childrenA = (List<TreeViewItemData<TBaseModelTreeItemData>>)itemA.children ?? new List<TreeViewItemData<TBaseModelTreeItemData>>();
                var childrenB = (List<TreeViewItemData<TBaseModelTreeItemData>>)itemB.children ?? new List<TreeViewItemData<TBaseModelTreeItemData>>();
                childrenA.Sort(sortByKeyComparisonA);
                childrenB.Sort(sortByKeyComparisonB);

                MatchSortedItems(
                    childrenA,
                    childrenB,
                    matchComparison,
                    withinAComparison: sortByKeyComparisonA,
                    withinBComparison: sortByKeyComparisonB,
                    (itemsA, itemsB) =>
                    {
                        // We make an assumption that any items with children (non-leaf items) have an exclusive size/count of zero, i.e only leaf nodes have a non-zero exclusive size/count. This means the sizes/counts of non-leaf items are derived entirely from the sum of their children. This allows us to filter the tree later, calculating inclusive size/count per item for display.
                        var exclusiveSizeInA = new MemorySize();
                        var exclusiveCountInA = 0U;
                        var itemCountA = itemsA.Count;
                        var treeNodeIdsA = itemCountA > 0 ? new int[itemCountA] : null;
                        for (int i = 0; i < itemCountA; i++)
                        {
                            var item = itemsA[i];
                            if (!item.hasChildren)
                            {
                                exclusiveSizeInA += item.data.TotalSize;
                                exclusiveCountInA++;
                            }
                            treeNodeIdsA[i] = item.id;
                        }

                        var exclusiveSizeInB = new MemorySize();
                        var exclusiveCountInB = 0U;
                        var itemCountB = itemsB.Count;
                        var treeNodeIdsB = itemCountB > 0 ? new int[itemCountB] : null;
                        for (int i = 0; i < itemCountB; i++)
                        {
                            var item = itemsB[i];
                            if (!item.hasChildren)
                            {
                                exclusiveSizeInB += item.data.TotalSize;
                                exclusiveCountInB++;
                            }
                            treeNodeIdsB[i] = item.id;
                        }

                        var name = (itemCountA > 0) ? itemsA[0].data.Name : itemsB[0].data.Name;

                        // Check if item has special id and retain it
                        var treeId = (itemCountA > 0) ? itemsA[0].id : itemsB[0].id;
                        if (!IAnalysisViewSelectable.IsPredefinedCategory(treeId))
                            treeId = (int)IAnalysisViewSelectable.Category.None;

                        // Build ComparisonSourceIndex from the representative items on each side.
                        var sourceInA = itemCountA > 0 ? itemsA[0].data.Source : default;
                        var sourceInB = itemCountB > 0 ? itemsB[0].data.Source : default;
                        var comparisonSourceIndex = new ComparisonSourceIndex(sourceInA, sourceInB);

                        var data = new NodeData(
                            name,
                            exclusiveSizeInA,
                            exclusiveSizeInB,
                            exclusiveCountInA,
                            exclusiveCountInB,
                            treeNodeIdsA,
                            treeNodeIdsB,
                            treeId,
                            comparisonSourceIndex);
                        var outputNode = new Node<TBaseModel, TBaseModelTreeItemData, TModelSpecificData>(parentOutputNode, data, getExtraData != null ? getExtraData(itemsA, itemsB) : default);
                        parentOutputNode.Children.Add(outputNode);

                        var a = itemsA.FirstOrDefault();
                        var b = itemsB.FirstOrDefault();
                        var hasChildren = a.hasChildren || b.hasChildren;
                        if (hasChildren)
                        {
                            itemStackA.Push(a);
                            itemStackB.Push(b);
                            parentOutputNodeStack.Push(outputNode);
                        }
                    },
                    cachedMatchingListForItemsWithNameInA: cachedMatchingListForItemsWithNameInA,
                    cachedMatchingListForItemsWithNameInB: cachedMatchingListForItemsWithNameInB
                );
            }

            return new List<Node<TBaseModel, TBaseModelTreeItemData, TModelSpecificData>>(rootOutputNode.Children);
        }

        // Matches the items with the same key in both input lists, A and B. The 'match' action will be invoked for every match discovered, including exclusive items, and passed all matched items from both A and B. The input lists must be sorted by their respective key.
        // crossComparison: used to advance the A/B cursors against each other (compares an A item against a B item).
        // withinAComparison: used to group consecutive equal-key items on the A side (defaults to crossComparison).
        // withinBComparison: used to group consecutive equal-key items on the B side (defaults to crossComparison).
        // When different key functions are used for A and B (e.g. qualified names that embed per-snapshot data),
        // withinAComparison and withinBComparison must use the matching per-side key function to avoid applying
        // a B-side index to snapshot-A data or vice-versa.
        internal static void MatchSortedItems<T>(
            List<TreeViewItemData<T>> itemsSortedByNameA,
            List<TreeViewItemData<T>> itemsSortedByNameB,
            Comparison<TreeViewItemData<T>> crossComparison,
            Comparison<TreeViewItemData<T>> withinAComparison,
            Comparison<TreeViewItemData<T>> withinBComparison,
            Action<List<TreeViewItemData<T>>, List<TreeViewItemData<T>>> match,
            List<TreeViewItemData<T>> cachedMatchingListForItemsWithNameInA = null,
            List<TreeViewItemData<T>> cachedMatchingListForItemsWithNameInB = null)
            where T : IPrivateComparableItemData
        {
            cachedMatchingListForItemsWithNameInA ??= new();
            cachedMatchingListForItemsWithNameInB ??= new();

            var indexA = 0;
            var indexB = 0;
            while (indexA < itemsSortedByNameA.Count || indexB < itemsSortedByNameB.Count)
            {
                int comparison;
                if (indexA >= itemsSortedByNameA.Count)
                    comparison = 1;
                else if (indexB >= itemsSortedByNameB.Count)
                    comparison = -1;
                else
                    comparison = crossComparison(itemsSortedByNameA[indexA], itemsSortedByNameB[indexB]);

                cachedMatchingListForItemsWithNameInA.Clear();
                cachedMatchingListForItemsWithNameInB.Clear();
                if (comparison <= 0)
                {
                    // Process all items with this name in list A.
                    TreeViewItemData<T> itemAData = itemsSortedByNameA[indexA];
                    TreeViewItemData<T> nextItemA;
                    do
                    {
                        var itemA = itemsSortedByNameA[indexA];
                        cachedMatchingListForItemsWithNameInA.Add(itemA);

                        indexA++;
                        if (indexA < itemsSortedByNameA.Count)
                            nextItemA = itemsSortedByNameA[indexA];
                        else
                            break;
                    }
                    while (withinAComparison(itemAData, nextItemA) == 0);
                }

                if (comparison >= 0)
                {
                    // Process all items with this name in list B.
                    TreeViewItemData<T> itemBData = itemsSortedByNameB[indexB];
                    TreeViewItemData<T> nextItemB;
                    do
                    {
                        var itemB = itemsSortedByNameB[indexB];
                        cachedMatchingListForItemsWithNameInB.Add(itemB);

                        indexB++;
                        if (indexB < itemsSortedByNameB.Count)
                            nextItemB = itemsSortedByNameB[indexB];
                        else
                            break;
                    }
                    while (withinBComparison(itemBData, nextItemB) == 0);
                }

                match?.Invoke(cachedMatchingListForItemsWithNameInA, cachedMatchingListForItemsWithNameInB);
            }
        }

        static List<TreeViewItemData<ComparisonTableModel<TBaseModel, TBaseModelTreeItemData, TModelSpecificData>.ComparisonData>>
            BuildUitkTreeFromIntermediateTree<TBaseModel, TBaseModelTreeItemData, TModelSpecificData>(
            List<Node<TBaseModel, TBaseModelTreeItemData, TModelSpecificData>> intermediateTree,
            BuildArgs args,
            Func<TModelSpecificData, bool> extraDataHasChanged,
            out long largestAbsoluteSizeDelta,
            Func<TModelSpecificData, IReadOnlyList<TModelSpecificData>, TModelSpecificData> aggregateExtraDataFromChildren = null)
            where TBaseModelTreeItemData : INamedTreeItemData
        {
            // Because UIToolkit TreeViewItemData has immutable children, we must iterate bottom up, so require post-order depth-first traversal.
            // Pre-order depth-first traversal to build post-order traversal stack.
            var stack = new Stack<Node<TBaseModel, TBaseModelTreeItemData, TModelSpecificData>>(intermediateTree);
            var postOrderStack = new Stack<Node<TBaseModel, TBaseModelTreeItemData, TModelSpecificData>>();
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                postOrderStack.Push(node);

                var children = node.Children;
                if (children.Count > 0)
                {
                    foreach (var child in children)
                        stack.Push(child);
                }
            }

            // Post-order depth-first traversal.
            var itemId = (int)IAnalysisViewSelectable.Category.FirstDynamicId;
            largestAbsoluteSizeDelta = 0;
            while (postOrderStack.Count > 0)
            {
                var node = postOrderStack.Pop();

                // Retrieve child tree-view-items and calculate inclusive values.
                var inclusiveSizeInA = node.Data.ExclusiveSizeInA;
                var inclusiveSizeInB = node.Data.ExclusiveSizeInB;
                var inclusiveCountInA = node.Data.ExclusiveCountInA;
                var inclusiveCountInB = node.Data.ExclusiveCountInB;
                var children = node.Children;
                var childItems = new List<TreeViewItemData<ComparisonTableModel<TBaseModel, TBaseModelTreeItemData, TModelSpecificData>.ComparisonData>>(children.Count);
                if (children.Count > 0)
                {
                    foreach (var child in children)
                    {
                        if (child.OutputItem.HasValue)
                        {
                            var childItem = child.OutputItem.Value;
                            inclusiveSizeInA += childItem.data.TotalSizeInA;
                            inclusiveSizeInB += childItem.data.TotalSizeInB;
                            inclusiveCountInA += childItem.data.CountInA;
                            inclusiveCountInB += childItem.data.CountInB;
                            childItems.Add(childItem);
                        }
                    }
                }

                var itemPath = new List<string>();
                var n = node;
                while (n != null)
                {
                    // Ignore the null root node used only for traversal.
                    if (n.Parent == null)
                        break;

                    itemPath.Insert(0, n.Data.Name);
                    n = n.Parent;
                }

                // Compute whether extra data (e.g. status) changed for this node.
                // Uses the original node.ExtraData (full aggregation) for change detection — correct,
                // since we want to detect any status composition change between A and B.
                bool changedByExtraData = args.CompareExtraData && (extraDataHasChanged?.Invoke(node.ExtraData) ?? false);

                // When not including unchanged items, childItems only contains nodes that were
                // themselves marked as changed (they would have been skipped otherwise). So
                // childItems.Count > 0 reliably signals "at least one changed descendant exists",
                // which must bubble up to make this ancestor visible too — otherwise a status-only
                // change in a leaf (no size/count delta) would be hidden because its ancestors'
                // inclusive sizes are unchanged and their own HasChanged would be false.
                //
                // When including unchanged items, the HasChanged guard in the visibility check is
                // always false (if (!HasChanged && !IncludeUnchanged) → if (!HasChanged && false)),
                // so every node is emitted regardless of HasChanged. hasChangedDescendant is
                // irrelevant in that case, and scanning childItems would be wasted O(N) work.
                bool hasChangedDescendant = !args.IncludeUnchanged && childItems.Count > 0;

                // For display, recompute ExtraData bottom-up from visible changed children when a
                // delegate is provided. This prevents warnings from unchanged items (which don't
                // appear in the tree when Show Unchanged is off) from showing on parent nodes.
                TModelSpecificData effectiveExtraData;
                if (aggregateExtraDataFromChildren != null && childItems.Count > 0)
                {
                    var childrenExtraData = new List<TModelSpecificData>(childItems.Count);
                    foreach (var c in childItems)
                        childrenExtraData.Add(c.data.ExtraData);
                    effectiveExtraData = aggregateExtraDataFromChildren(node.ExtraData, childrenExtraData);
                }
                else
                {
                    effectiveExtraData = node.ExtraData;
                }

                // Create UIToolkit node type with children. Store on our node type until full tree is traversed.
                var comparisonData = new ComparisonTableModel<TBaseModel, TBaseModelTreeItemData, TModelSpecificData>.ComparisonData(
                    node.Data.Name,
                    inclusiveSizeInA,
                    inclusiveSizeInB,
                    inclusiveCountInA,
                    inclusiveCountInB,
                    node.Data.TreeNodeIdsA,
                    node.Data.TreeNodeIdsB,
                    itemPath,
                    effectiveExtraData,
                    args.TableMode,
                    childItems.Count,
                    node.Data.ComparisonSourceIndex,
                    changedByExtraData || hasChangedDescendant);

                // Only include unchanged items if requested by build args.
                if (!comparisonData.HasChanged && !args.IncludeUnchanged)
                    continue;

                var nodeItemId = IAnalysisViewSelectable.IsPredefinedCategory(node.Data.TreeNodeId) ? node.Data.TreeNodeId : itemId++;
                var item = new TreeViewItemData<ComparisonTableModel<TBaseModel, TBaseModelTreeItemData, TModelSpecificData>.ComparisonData>(
                    nodeItemId,
                    comparisonData,
                    childItems);
                node.OutputItem = item;

                var absoluteSizeDelta = Math.Abs(comparisonData.SizeDelta);
                largestAbsoluteSizeDelta = Math.Max(absoluteSizeDelta, largestAbsoluteSizeDelta);
            }

            var finalTree = new List<TreeViewItemData<ComparisonTableModel<TBaseModel, TBaseModelTreeItemData, TModelSpecificData>.ComparisonData>>(intermediateTree.Count);
            foreach (var rootNode in intermediateTree)
            {
                if (rootNode.OutputItem.HasValue)
                {
                    var rootItem = rootNode.OutputItem.Value;
                    finalTree.Add(rootItem);
                }
            }

            return finalTree;
        }

        public readonly struct BuildArgs
        {
            public BuildArgs(
                bool includeUnchanged,
                AllTrackedMemoryTableMode tableMode,
                bool compareExtraData = false
                )
            {
                IncludeUnchanged = includeUnchanged;
                TableMode = tableMode;
                CompareExtraData = compareExtraData;
            }

            // Include unchanged items.
            public bool IncludeUnchanged { get; }

            public AllTrackedMemoryTableMode TableMode { get; }

            // When true, callers should pass an extraDataHasChanged delegate to Build()
            // so that extra-data differences (e.g. status) factor into HasChanged.
            public bool CompareExtraData { get; }
        }

        // A node for the intermediate comparison tree.
        class Node<TBaseModel, TBaseModelTreeItemData, TModelSpecificData>
            where TBaseModelTreeItemData : INamedTreeItemData
        {
            public Node(Node<TBaseModel, TBaseModelTreeItemData, TModelSpecificData> parent, NodeData data, TModelSpecificData extraData)
            {
                Parent = parent;
                Children = new List<Node<TBaseModel, TBaseModelTreeItemData, TModelSpecificData>>();
                Data = data;
                ExtraData = extraData;
            }

            public Node<TBaseModel, TBaseModelTreeItemData, TModelSpecificData> Parent { get; }

            public List<Node<TBaseModel, TBaseModelTreeItemData, TModelSpecificData>> Children { get; }

            public NodeData Data { get; }
            public TModelSpecificData ExtraData { get; }
            // Built during final traversal when converting to UIToolkit tree.
            public TreeViewItemData<ComparisonTableModel<TBaseModel, TBaseModelTreeItemData, TModelSpecificData>.ComparisonData>? OutputItem { get; set; }
        }

        readonly struct NodeData
        {
            public NodeData(
                string name,
                MemorySize exclusiveSizeInA,
                MemorySize exclusiveSizeInB,
                uint exclusiveCountInA,
                uint exclusiveCountInB,
                int[] treeNodeIdsA,
                int[] treeNodeIdsB,
                int treeNodeId,
                ComparisonSourceIndex comparisonSourceIndex = default)
            {
                Name = name;
                ExclusiveSizeInA = exclusiveSizeInA;
                ExclusiveSizeInB = exclusiveSizeInB;
                ExclusiveCountInA = exclusiveCountInA;
                ExclusiveCountInB = exclusiveCountInB;
                TreeNodeIdsA = treeNodeIdsA;
                TreeNodeIdsB = treeNodeIdsB;
                TreeNodeId = treeNodeId;
                ComparisonSourceIndex = comparisonSourceIndex;
            }

            // The name of this item.
            public string Name { get; }

            // The size of this item in A excluding children, in bytes.
            public MemorySize ExclusiveSizeInA { get; }

            // The size of this item in B excluding children, in bytes.
            public MemorySize ExclusiveSizeInB { get; }

            // The number of this item in A, excluding children.
            public uint ExclusiveCountInA { get; }

            // The number of this item in B, excluding children.
            public uint ExclusiveCountInB { get; }

            public int TreeNodeId { get; }

            // The tree node Ids in model A.
            public int[] TreeNodeIdsA { get; }

            // The tree node Ids in model B.
            public int[] TreeNodeIdsB { get; }

            // The snapshot-level source handles for this comparison node.
            public ComparisonSourceIndex ComparisonSourceIndex { get; }
        }
    }
}

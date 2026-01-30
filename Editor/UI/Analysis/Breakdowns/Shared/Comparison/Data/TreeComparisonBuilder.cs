using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.Extensions;
using UnityEngine.UIElements;
using static Unity.MemoryProfiler.Editor.UI.TreeModelHelpers;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Builds a comparison tree from two input trees with nodes of type TreeViewItemData<IComparableItemData>.
    class TreeComparisonBuilder
    {
        public List<TreeViewItemData<ComparisonTableModel<TBaseModel, TBaseModelTreeItemData>.ComparisonData>> Build<TBaseModelTreeItemData, TBaseModel>(
            List<TreeViewItemData<TBaseModelTreeItemData>> treeA,
            List<TreeViewItemData<TBaseModelTreeItemData>> treeB,
            BuildArgs args,
            out long largestAbsoluteSizeDelta)
            where TBaseModelTreeItemData : IPrivateComparableItemData
        {
            var intermediateTree = BuildIntermediateTree<TBaseModelTreeItemData, TBaseModel>(treeA, treeB);
            var comparisonTree = BuildUitkTreeFromIntermediateTree(intermediateTree, args, out largestAbsoluteSizeDelta);
            return comparisonTree;
        }

        static List<Node<TBaseModel, TBaseModelTreeItemData>> BuildIntermediateTree<TBaseModelTreeItemData, TBaseModel>(
            IEnumerable<TreeViewItemData<TBaseModelTreeItemData>> treeA,
            IEnumerable<TreeViewItemData<TBaseModelTreeItemData>> treeB)
            where TBaseModelTreeItemData : IPrivateComparableItemData
        {
            var itemStackA = new Stack<TreeViewItemData<TBaseModelTreeItemData>>();
            var rootA = new TreeViewItemData<TBaseModelTreeItemData>(-1, default, new List<TreeViewItemData<TBaseModelTreeItemData>>(treeA));
            itemStackA.Push(rootA);

            var itemStackB = new Stack<TreeViewItemData<TBaseModelTreeItemData>>();
            var rootB = new TreeViewItemData<TBaseModelTreeItemData>(-1, default, new List<TreeViewItemData<TBaseModelTreeItemData>>(treeB));
            itemStackB.Push(rootB);

            var parentOutputNodeStack = new Stack<Node<TBaseModel, TBaseModelTreeItemData>>();
            var rootOutputNode = new Node<TBaseModel, TBaseModelTreeItemData>(null, default);
            parentOutputNodeStack.Push(rootOutputNode);

            var sortByNameComparison = new Comparison<TreeViewItemData<TBaseModelTreeItemData>>((x, y) => string.Compare(
                x.data.Name,
                y.data.Name,
                StringComparison.Ordinal));

            while (itemStackA.Count > 0 && itemStackB.Count > 0)
            {
                var itemA = itemStackA.Pop();
                var itemB = itemStackB.Pop();
                var parentOutputNode = parentOutputNodeStack.Pop();

                var childrenA = (List<TreeViewItemData<TBaseModelTreeItemData>>)itemA.children ?? new List<TreeViewItemData<TBaseModelTreeItemData>>();
                var childrenB = (List<TreeViewItemData<TBaseModelTreeItemData>>)itemB.children ?? new List<TreeViewItemData<TBaseModelTreeItemData>>();
                childrenA.Sort(sortByNameComparison);
                childrenB.Sort(sortByNameComparison);

                MatchSortedItems(
                    childrenA,
                    childrenB,
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

                        var data = new NodeData(
                            name,
                            exclusiveSizeInA,
                            exclusiveSizeInB,
                            exclusiveCountInA,
                            exclusiveCountInB,
                            treeNodeIdsA,
                            treeNodeIdsB,
                            treeId);
                        var outputNode = new Node<TBaseModel, TBaseModelTreeItemData>(parentOutputNode, data);
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
                    }
                );
            }

            return new List<Node<TBaseModel, TBaseModelTreeItemData>>(rootOutputNode.Children);
        }

        // Matches the items with the same name in both input lists, A and B. The 'match' action will be invoked for every match discovered, including exclusive items, and passed all matched items from both A and B. The input lists must be sorted by name.
        internal static void MatchSortedItems<T>(
            List<TreeViewItemData<T>> itemsSortedByNameA,
            List<TreeViewItemData<T>> itemsSortedByNameB,
            Action<List<TreeViewItemData<T>>, List<TreeViewItemData<T>>> match)
            where T : IPrivateComparableItemData
        {
            var indexA = 0;
            var indexB = 0;
            while (indexA < itemsSortedByNameA.Count || indexB < itemsSortedByNameB.Count)
            {
                string nameA = null;
                if (indexA < itemsSortedByNameA.Count)
                    nameA = itemsSortedByNameA[indexA].data.Name;

                string nameB = null;
                if (indexB < itemsSortedByNameB.Count)
                    nameB = itemsSortedByNameB[indexB].data.Name;

                int comparison;
                if (nameA == null)
                    comparison = 1;
                else if (nameB == null)
                    comparison = -1;
                else
                    comparison = string.Compare(nameA, nameB, StringComparison.Ordinal);

                var itemsWithNameInA = new List<TreeViewItemData<T>>();
                var itemsWithNameInB = new List<TreeViewItemData<T>>();
                if (comparison <= 0)
                {
                    // Process all items with this name in list A.
                    string nextNameA;
                    do
                    {
                        var itemA = itemsSortedByNameA[indexA];
                        itemsWithNameInA.Add(itemA);

                        indexA++;
                        nextNameA = (indexA < itemsSortedByNameA.Count) ? itemsSortedByNameA[indexA].data.Name : null;
                    }
                    while (string.Compare(nameA, nextNameA, StringComparison.Ordinal) == 0);
                }

                if (comparison >= 0)
                {
                    // Process all items with this name in list B.
                    string nextNameB;
                    do
                    {
                        var itemB = itemsSortedByNameB[indexB];
                        itemsWithNameInB.Add(itemB);

                        indexB++;
                        nextNameB = (indexB < itemsSortedByNameB.Count) ? itemsSortedByNameB[indexB].data.Name : null;
                    }
                    while (string.Compare(nameB, nextNameB, StringComparison.Ordinal) == 0);
                }

                match?.Invoke(itemsWithNameInA, itemsWithNameInB);
            }
        }

        static List<TreeViewItemData<ComparisonTableModel<TBaseModel, TBaseModelTreeItemData>.ComparisonData>>
            BuildUitkTreeFromIntermediateTree<TBaseModel, TBaseModelTreeItemData>(
            List<Node<TBaseModel, TBaseModelTreeItemData>> intermediateTree,
            BuildArgs args,
            out long largestAbsoluteSizeDelta)
            where TBaseModelTreeItemData : INamedTreeItemData
        {
            // Because UIToolkit TreeViewItemData has immutable children, we must iterate bottom up, so require post-order depth-first traversal.
            // Pre-order depth-first traversal to build post-order traversal stack.
            var stack = new Stack<Node<TBaseModel, TBaseModelTreeItemData>>(intermediateTree);
            var postOrderStack = new Stack<Node<TBaseModel, TBaseModelTreeItemData>>();
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
                var childItems = new List<TreeViewItemData<ComparisonTableModel<TBaseModel, TBaseModelTreeItemData>.ComparisonData>>(children.Count);
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

                // Create UIToolkit node type with children. Store on our node type until full tree is traversed.
                var comparisonData = new ComparisonTableModel<TBaseModel, TBaseModelTreeItemData>.ComparisonData(
                    node.Data.Name,
                    inclusiveSizeInA,
                    inclusiveSizeInB,
                    inclusiveCountInA,
                    inclusiveCountInB,
                    itemPath,
                    args.TableMode);

                // Only include unchanged items if requested by build args.
                if (!comparisonData.HasChanged && !args.IncludeUnchanged)
                    continue;

                var nodeItemId = IAnalysisViewSelectable.IsPredefinedCategory(node.Data.TreeNodeId) ? node.Data.TreeNodeId : itemId++;
                var item = new TreeViewItemData<ComparisonTableModel<TBaseModel, TBaseModelTreeItemData>.ComparisonData>(
                    nodeItemId,
                    comparisonData,
                    childItems);
                node.OutputItem = item;

                var absoluteSizeDelta = Math.Abs(comparisonData.SizeDelta);
                largestAbsoluteSizeDelta = Math.Max(absoluteSizeDelta, largestAbsoluteSizeDelta);
            }

            var finalTree = new List<TreeViewItemData<ComparisonTableModel<TBaseModel, TBaseModelTreeItemData>.ComparisonData>>(intermediateTree.Count);
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
                AllTrackedMemoryTableMode tableMode
                )
            {
                IncludeUnchanged = includeUnchanged;
                TableMode = tableMode;
            }

            // Include unchanged items.
            public bool IncludeUnchanged { get; }

            public AllTrackedMemoryTableMode TableMode { get; }
        }

        // A node for the intermediate comparison tree.
        class Node<TBaseModel, TBaseModelTreeItemData>
            where TBaseModelTreeItemData : INamedTreeItemData
        {
            public Node(Node<TBaseModel, TBaseModelTreeItemData> parent, NodeData data)
            {
                Parent = parent;
                Children = new List<Node<TBaseModel, TBaseModelTreeItemData>>();
                Data = data;
            }

            public Node<TBaseModel, TBaseModelTreeItemData> Parent { get; }

            public List<Node<TBaseModel, TBaseModelTreeItemData>> Children { get; }

            public NodeData Data { get; }

            // Built during final traversal when converting to UIToolkit tree.
            public TreeViewItemData<ComparisonTableModel<TBaseModel, TBaseModelTreeItemData>.ComparisonData>? OutputItem { get; set; }
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
                int treeNodeId)
            {
                Name = name;
                ExclusiveSizeInA = exclusiveSizeInA;
                ExclusiveSizeInB = exclusiveSizeInB;
                ExclusiveCountInA = exclusiveCountInA;
                ExclusiveCountInB = exclusiveCountInB;
                TreeNodeIdsA = treeNodeIdsA;
                TreeNodeIdsB = treeNodeIdsB;
                TreeNodeId = treeNodeId;
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
        }
    }
}

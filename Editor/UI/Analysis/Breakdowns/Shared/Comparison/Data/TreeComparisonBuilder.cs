#if UNITY_2022_1_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Builds a comparison tree from two input trees with nodes of type TreeViewItemData<IComparableItemData>.
    class TreeComparisonBuilder
    {
        public List<TreeViewItemData<ComparisonTableModel.ComparisonData>> Build<T>(
            List<TreeViewItemData<T>> treeA,
            List<TreeViewItemData<T>> treeB,
            BuildArgs args,
            out long largestAbsoluteSizeDelta)
            where T : IPrivateComparableItemData
        {
            var intermediateTree = BuildIntermediateTree(treeA, treeB);
            var comparisonTree = BuildUitkTreeFromIntermediateTree(intermediateTree, args, out largestAbsoluteSizeDelta);
            return comparisonTree;
        }

        static List<Node> BuildIntermediateTree<T>(
            IEnumerable<TreeViewItemData<T>> treeA,
            IEnumerable<TreeViewItemData<T>> treeB)
            where T : IPrivateComparableItemData
        {
            var itemStackA = new Stack<TreeViewItemData<T>>();
            var rootA = new TreeViewItemData<T>(-1, default, new List<TreeViewItemData<T>>(treeA));
            itemStackA.Push(rootA);

            var itemStackB = new Stack<TreeViewItemData<T>>();
            var rootB = new TreeViewItemData<T>(-1, default, new List<TreeViewItemData<T>>(treeB));
            itemStackB.Push(rootB);

            var parentOutputNodeStack = new Stack<Node>();
            var rootOutputNode = new Node(null, default);
            parentOutputNodeStack.Push(rootOutputNode);

            var sortByNameComparison = new Comparison<TreeViewItemData<T>>((x, y) => string.Compare(
                x.data.Name,
                y.data.Name,
                StringComparison.Ordinal));

            while (itemStackA.Count > 0 && itemStackB.Count > 0)
            {
                var itemA = itemStackA.Pop();
                var itemB = itemStackB.Pop();
                var parentOutputNode = parentOutputNodeStack.Pop();

                var childrenA = (List<TreeViewItemData<T>>)itemA.children ?? new List<TreeViewItemData<T>>();
                var childrenB = (List<TreeViewItemData<T>>)itemB.children ?? new List<TreeViewItemData<T>>();
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
                        foreach (var item in itemsA)
                        {
                            if (!item.hasChildren)
                            {
                                exclusiveSizeInA += item.data.Size;
                                exclusiveCountInA++;
                            }
                        }

                        var exclusiveSizeInB = new MemorySize();
                        var exclusiveCountInB = 0U;
                        foreach (var item in itemsB)
                        {
                            if (!item.hasChildren)
                            {
                                exclusiveSizeInB += item.data.Size;
                                exclusiveCountInB++;
                            }
                        }

                        var name = (itemsA.Count > 0) ? itemsA[0].data.Name : itemsB[0].data.Name;

                        // Check if item has special id and retain it
                        var treeId = (itemsA.Count > 0) ? itemsA[0].id : itemsB[0].id;
                        if (!IAnalysisViewSelectable.IsPredefinedCategory(treeId))
                            treeId = (int)IAnalysisViewSelectable.Category.None;

                        var data = new NodeData(
                            name,
                            exclusiveSizeInA.Committed,
                            exclusiveSizeInB.Committed,
                            exclusiveCountInA,
                            exclusiveCountInB,
                            treeId);
                        var outputNode = new Node(parentOutputNode, data);
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

            return new List<Node>(rootOutputNode.Children);
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

        static List<TreeViewItemData<ComparisonTableModel.ComparisonData>> BuildUitkTreeFromIntermediateTree(
            List<Node> intermediateTree,
            BuildArgs args,
            out long largestAbsoluteSizeDelta)
        {
            // Because UIToolkit TreeViewItemData has immutable children, we must iterate bottom up, so require post-order depth-first traversal.
            // Pre-order depth-first traversal to build post-order traversal stack.
            var stack = new Stack<Node>(intermediateTree);
            var postOrderStack = new Stack<Node>();
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
                var childItems = new List<TreeViewItemData<ComparisonTableModel.ComparisonData>>(children.Count);
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
                var comparisonData = new ComparisonTableModel.ComparisonData(
                    node.Data.Name,
                    inclusiveSizeInA,
                    inclusiveSizeInB,
                    inclusiveCountInA,
                    inclusiveCountInB,
                    itemPath);

                // Only include unchanged items if requested by build args.
                if (!comparisonData.HasChanged && !args.IncludeUnchanged)
                    continue;

                var nodeItemId = IAnalysisViewSelectable.IsPredefinedCategory(node.Data.TreeNodeId) ? node.Data.TreeNodeId : itemId++;
                var item = new TreeViewItemData<ComparisonTableModel.ComparisonData>(
                    nodeItemId,
                    comparisonData,
                    childItems);
                node.OutputItem = item;

                var absoluteSizeDelta = Math.Abs(comparisonData.SizeDelta);
                largestAbsoluteSizeDelta = Math.Max(absoluteSizeDelta, largestAbsoluteSizeDelta);
            }

            var finalTree = new List<TreeViewItemData<ComparisonTableModel.ComparisonData>>(intermediateTree.Count);
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
                bool includeUnchanged)
            {
                IncludeUnchanged = includeUnchanged;
            }

            // Include unchanged items.
            public bool IncludeUnchanged { get; }
        }

        // A node for the intermediate comparison tree.
        class Node
        {
            public Node(Node parent, NodeData data)
            {
                Parent = parent;
                Children = new List<Node>();
                Data = data;
            }

            public Node Parent { get; }

            public List<Node> Children { get; }

            public NodeData Data { get; }

            // Built during final traversal when converting to UIToolkit tree.
            public TreeViewItemData<ComparisonTableModel.ComparisonData>? OutputItem { get; set; }
        }

        readonly struct NodeData
        {
            public NodeData(
                string name,
                ulong exclusiveSizeInA,
                ulong exclusiveSizeInB,
                uint exclusiveCountInA,
                uint exclusiveCountInB,
                int treeNodeId)
            {
                Name = name;
                ExclusiveSizeInA = exclusiveSizeInA;
                ExclusiveSizeInB = exclusiveSizeInB;
                ExclusiveCountInA = exclusiveCountInA;
                ExclusiveCountInB = exclusiveCountInB;
                TreeNodeId = treeNodeId;
            }

            // The name of this item.
            public string Name { get; }

            // The size of this item in A excluding children, in bytes.
            public ulong ExclusiveSizeInA { get; }

            // The size of this item in B excluding children, in bytes.
            public ulong ExclusiveSizeInB { get; }

            // The number of this item in A, excluding children.
            public uint ExclusiveCountInA { get; }

            // The number of this item in B, excluding children.
            public uint ExclusiveCountInB { get; }

            public int TreeNodeId { get; }
        }
    }
}
#endif

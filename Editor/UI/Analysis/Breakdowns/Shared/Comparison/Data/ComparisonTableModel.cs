using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using static Unity.MemoryProfiler.Editor.UI.TreeModelHelpers;

namespace Unity.MemoryProfiler.Editor.UI
{
    interface IComparisonTreeModel<TComparisonTreeItemData, TBaseModel> : ITreeModel<TComparisonTreeItemData>
        where TComparisonTreeItemData : INamedTreeItemData
    {
        TBaseModel BaseModel { get; }
        TBaseModel ComparedModel { get; }
        MemorySize TotalSnapshotSizeA { get; }
        MemorySize TotalSnapshotSizeB { get; }
        MemorySize TotalSizeA { get; }
        MemorySize TotalSizeB { get; }
        long LargestAbsoluteSizeDelta { get; }
    }

    // General model for comparison of two tree-based models.
    class ComparisonTableModel<TBaseModel, TBaseModelTreeItemData>
        : TreeModel<ComparisonTableModel<TBaseModel, TBaseModelTreeItemData>.ComparisonData>,
        IComparisonTreeModel<ComparisonTableModel<TBaseModel, TBaseModelTreeItemData>.ComparisonData, TBaseModel>
        where TBaseModelTreeItemData : INamedTreeItemData
    {
        public ComparisonTableModel(
            List<TreeViewItemData<ComparisonData>> rootNodes,
            TBaseModel baseModel,
            MemorySize totalSnapshotSizeA,
            TBaseModel comparedModel,
            MemorySize totalSnapshotSizeB,
            long largestAbsoluteSizeDelta)
            : base(rootNodes)
        {
            var totalSizeA = new MemorySize();
            var totalSizeB = new MemorySize();
            foreach (var rootItem in rootNodes)
            {
                totalSizeA += rootItem.data.TotalSizeInA;
                totalSizeB += rootItem.data.TotalSizeInB;
            }
            BaseModel = baseModel;
            ComparedModel = comparedModel;
            TotalSizeA = totalSizeA;
            TotalSizeB = totalSizeB;
            TotalSnapshotSizeA = totalSnapshotSizeA;
            TotalSnapshotSizeB = totalSnapshotSizeB;
            LargestAbsoluteSizeDelta = largestAbsoluteSizeDelta;
        }

        // The total size of memory in the model from A, in bytes; the sum of all tree items' TotalSizeInA field.
        public MemorySize TotalSizeA { get; }

        // The total size of memory in the model from B, in bytes; the sum of all tree items' TotalSizeInB field.
        public MemorySize TotalSizeB { get; }

        // The size of all memory in A's source snapshot, in bytes.
        public MemorySize TotalSnapshotSizeA { get; }

        // The size of all memory in B's source snapshot, in bytes.
        public MemorySize TotalSnapshotSizeB { get; }

        // The largest absolute size delta of any single item in the model's tree, in bytes.
        public long LargestAbsoluteSizeDelta { get; }

        public TBaseModel BaseModel { get; }
        public TBaseModel ComparedModel { get; }

        // The data associated with each item in the tree.
        public readonly struct ComparisonData : INamedTreeItemData
        {
            public ComparisonData(
                string name,
                MemorySize totalSizeInA,
                MemorySize totalSizeInB,
                uint countInA,
                uint countInB,
                List<string> itemPath,
                AllTrackedMemoryTableMode tableMode)
            {
                Name = name;
                SizeDelta = Convert.ToInt64(totalSizeInB.Committed) - Convert.ToInt64(totalSizeInA.Committed);
                TotalSizeInA = totalSizeInA;
                TotalSizeInB = totalSizeInB;
                CountInA = countInA;
                CountInB = countInB;
                CountDelta = Convert.ToInt32(countInB) - Convert.ToInt32(countInA);
                var sizeChanged = tableMode switch
                {
                    AllTrackedMemoryTableMode.OnlyCommitted => totalSizeInA.Committed != totalSizeInB.Committed,
                    AllTrackedMemoryTableMode.OnlyResident => totalSizeInA.Resident != totalSizeInB.Resident,
                    AllTrackedMemoryTableMode.CommittedAndResident => totalSizeInA != totalSizeInB,
                    _ => throw new NotImplementedException()
                };
                HasChanged = sizeChanged || CountInA != CountInB;
                ItemPath = itemPath;
            }

            // The name of this item.
            public string Name { get; }

            // The difference in size, in bytes, between A and B. Computed as B - A.
            public long SizeDelta { get; }

            // The total size in bytes of this item in A, including its children.
            public MemorySize TotalSizeInA { get; }

            // The total size of this item in B, including its children.
            public MemorySize TotalSizeInB { get; }

            // The number of this item in A.
            public uint CountInA { get; }

            // The number of this item in B.
            public uint CountInB { get; }

            // The difference in count between A and B. Computed as B - A.
            public int CountDelta { get; }

            // Has this item or any of its children changed?
            public bool HasChanged { get; }

            // Item path.
            public List<string> ItemPath { get; }
        }

        // There is currently no need for tree node id based filtering beyond filtering Base and Compared tables based on the tree node ids selected in the comparison view
        // In case we see a use for the faster filtering via tree node ids, this should do it.
        //static public ComparisonTableModel<TBaseModel, TBaseModelTreeItemData> Build(ComparisonTableModel<TBaseModel, TBaseModelTreeItemData> fullModel, IEnumerable<int> treeNodeIds = null)
        //{
        //    Dictionary<int, Tuple<IndexableEnumerable<TreeViewItemData<ComparisonData>>, int>> idToIndex = null;
        //    var tree = new List<TreeViewItemData<ComparisonData>>();

        //    var totalMemoryA = new MemorySize();
        //    var totalMemoryB = new MemorySize();
        //    var largestAbsoluteSizeDelta = 0ul;

        //    if (treeNodeIds != null)
        //    {
        //        var stack = new Stack<TreeViewItemData<ComparisonData>>();
        //        foreach (var id in treeNodeIds)
        //        {
        //            // the only place where the base model has to be assumed as non-null.
        //            var item = fullModel.RootNodes.GetItemById(ref idToIndex, id);
        //            stack.Push(item);
        //        }
        //        while (stack.Count > 0)
        //        {
        //            var treeNode = stack.Pop();
        //            // only add leaf nodes, add children to the stack to be processed
        //            if (treeNode.hasChildren)
        //            {
        //                foreach (var child in treeNode.children)
        //                {
        //                    stack.Push(child);
        //                }
        //            }
        //            else
        //            {
        //                tree.Add(new TreeViewItemData<ComparisonData>(treeNode.id, treeNode.data));
        //                var diff = treeNode.data.TotalSizeInA.Committed > treeNode.data.TotalSizeInB.Committed ?
        //                    treeNode.data.TotalSizeInA.Committed - treeNode.data.TotalSizeInB.Committed
        //                    : treeNode.data.TotalSizeInA.Committed - treeNode.data.TotalSizeInB.Committed;
        //                if(diff > largestAbsoluteSizeDelta)
        //                    largestAbsoluteSizeDelta = diff;
        //                totalMemoryA += treeNode.data.TotalSizeInA;
        //                totalMemoryB += treeNode.data.TotalSizeInB;
        //            }
        //        }
        //    }
        //    // the base model could be null, but in an empty model, there are no selections to be made anyways.
        //    var model = CreateDerivedModel(tree, totalMemoryA, totalMemoryB, fullModel, (long)largestAbsoluteSizeDelta);
        //    return model;
        //}
        //
        ///// <summary>
        ///// Creates a derived model, based on a provided <paramref name="baseModel"/> and some <paramref name="rootNodes"/>.
        ///// The <paramref name="baseModel"/> may be null if <paramref name="rootNodes"/> is an empty list.
        ///// </summary>
        ///// <param name="rootNodes"></param>
        ///// <param name="totalMemory"></param>
        ///// <param name="baseModel"></param>
        ///// <returns></returns>
        //static ComparisonTableModel<TBaseModel, TBaseModelTreeItemData> CreateDerivedModel(List<TreeViewItemData<ComparisonData>> rootNodes, MemorySize totalMemoryA, MemorySize totalMemoryB, ComparisonTableModel<TBaseModel, TBaseModelTreeItemData> baseModel, long largestAbsoluteSizeDelta)
        //{
        //    return new ComparisonTableModel<TBaseModel, TBaseModelTreeItemData>(rootNodes, baseModel.BaseModel, totalMemoryA, baseModel.ComparedModel, totalMemoryB, largestAbsoluteSizeDelta);
        //}
    }
}

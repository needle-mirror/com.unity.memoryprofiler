using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using static Unity.MemoryProfiler.Editor.UI.TreeModelHelpers;

namespace Unity.MemoryProfiler.Editor.UI
{
    static class TreeModelHelpers
    {
        public struct IndexableTreeViewListAndIndex<TEnumerable>
        {
            public IndexableEnumerable<TreeViewItemData<TEnumerable>> IndexableTreeViewItems;
            public int Index;

            public IndexableTreeViewListAndIndex(IndexableEnumerable<TreeViewItemData<TEnumerable>> indexableTreeView, int index)
            {
                IndexableTreeViewItems = indexableTreeView;
                Index = index;
            }
        }

        public struct IndexableEnumerable<TEnumerable>
        {
            public TEnumerable this[int index]
            {
                get
                {
                    // if possible upcast to higherlevel interface so we can index into the list and don't have to iterate over it every time.
                    // TreeViewItemData currently uses an IList for its children so this is the path it will take.
                    if (m_Enumerable is IList<TEnumerable> list)
                    {
                        return list[index];
                    }
                    // otherwise, e.g. when the implementation for TreeViewItemData changes, fall back to using IEnumerable interface to access an element at the right index
                    var enumerator = m_Enumerable.GetEnumerator();
                    if (!enumerator.MoveNext())
                        throw new IndexOutOfRangeException(nameof(index));
                    for (int i = 0; i < index; i++)
                    {
                        if (!enumerator.MoveNext())
                            throw new IndexOutOfRangeException(nameof(index));
                    }
                    return enumerator.Current;
                }
            }

            IEnumerable<TEnumerable> m_Enumerable;

            public IndexableEnumerable(IEnumerable<TEnumerable> enumerable)
            {
                m_Enumerable = enumerable;
            }
        }

        public static TreeViewItemData<TTreeItemData> GetItemById<TTreeItemData>(
            this List<TreeViewItemData<TTreeItemData>> RootNodes,
            ref Dictionary<int, IndexableTreeViewListAndIndex<TTreeItemData>> idToIndex,
            int id)
        {
            if (idToIndex == null)
            {
                idToIndex = new Dictionary<int, IndexableTreeViewListAndIndex<TTreeItemData>>();
                var stack = new Stack<IndexableTreeViewListAndIndex<TTreeItemData>>();
                for (int i = 0; i < RootNodes.Count; i++)
                {
                    stack.Push(new IndexableTreeViewListAndIndex<TTreeItemData>(new(RootNodes), i));
                }
                while (stack.Count > 0)
                {
                    var item = stack.Pop();
                    var treeItem = item.IndexableTreeViewItems[item.Index];
                    idToIndex.Add(treeItem.id, item);
                    if (!treeItem.hasChildren)
                        continue;

                    int i = 0;
                    var children = new IndexableEnumerable<TreeViewItemData<TTreeItemData>>(treeItem.children);
                    foreach (var child in treeItem.children.GetEnumerator())
                    {
                        stack.Push(new IndexableTreeViewListAndIndex<TTreeItemData>(children, i++));
                    }
                }
            }
            if (idToIndex.TryGetValue(id, out var value))
            {
                return value.IndexableTreeViewItems[value.Index];
            }
            else
                return default;
        }
    }

    interface ITreeModel<TTreeItemData>
        where TTreeItemData : INamedTreeItemData
    {
        public List<TreeViewItemData<TTreeItemData>> RootNodes { get; }
        public void Sort(Comparison<TreeViewItemData<TTreeItemData>> sortComparison);
    }

    abstract class TreeModel<T> : ITreeModel<T>
        where T : INamedTreeItemData
    {
        protected TreeModel(List<TreeViewItemData<T>> rootNodes)
        {
            RootNodes = rootNodes;
        }

        // The tree's root nodes.
        public List<TreeViewItemData<T>> RootNodes { get; }

        // Sort the tree's data according to the provided sort comparison.
        public void Sort(Comparison<TreeViewItemData<T>> sortComparison)
        {
            if (sortComparison == null)
                return;

            RootNodes.Sort(sortComparison);
            var stack = new Stack<TreeViewItemData<T>>(RootNodes);
            while (stack.Count > 0)
            {
                var item = stack.Pop();
                if (item.hasChildren)
                {
                    var children = item.children as List<TreeViewItemData<T>>;
                    foreach (var child in children)
                        stack.Push(child);

                    children.Sort(sortComparison);
                }
            }
        }
    }
}

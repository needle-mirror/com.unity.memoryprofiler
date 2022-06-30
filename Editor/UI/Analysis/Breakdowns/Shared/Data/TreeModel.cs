#if UNITY_2022_1_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    abstract class TreeModel<T>
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
#endif

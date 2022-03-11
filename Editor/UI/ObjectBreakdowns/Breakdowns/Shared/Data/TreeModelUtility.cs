#if UNITY_2022_1_OR_NEWER
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    static class TreeModelUtility
    {
        public static List<TreeViewItemData<T>> RetrieveLeafNodesOfTree<T>(List<TreeViewItemData<T>> rootItems)
        {
            var items = new List<TreeViewItemData<T>>();

            // Depth-first search to retrieve leaves.
            var stack = new Stack<TreeViewItemData<T>>(rootItems);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (node.hasChildren)
                {
                    foreach (var child in node.children)
                        stack.Push(child);
                }
                else
                {
                    items.Add(node);
                }
            }

            return items;
        }
    }
}
#endif

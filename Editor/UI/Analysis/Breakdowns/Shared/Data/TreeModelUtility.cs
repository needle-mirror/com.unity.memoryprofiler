using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    static class TreeModelUtility
    {
        internal const int CancellableSortMinCount = 1024;
        const int k_ComparisonsPerCancellationCheck = 1024;

        public static void SortCancellable<T>(List<T> list, Comparison<T> comparison, CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled || list.Count < CancellableSortMinCount)
            {
                list.Sort(comparison);
                return;
            }

            var comparisons = 0;
            try
            {
                list.Sort((x, y) =>
                {
                    if (++comparisons % k_ComparisonsPerCancellationCheck == 0)
                        cancellationToken.ThrowIfCancellationRequested();
                    return comparison(x, y);
                });
            }
            // List.Sort wraps exceptions thrown by the comparison.
            catch (InvalidOperationException e) when (e.InnerException is OperationCanceledException canceled)
            {
                ExceptionDispatchInfo.Capture(canceled).Throw();
            }
        }

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

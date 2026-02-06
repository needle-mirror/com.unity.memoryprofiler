using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Utility class for building sort comparisons from TreeView column sorting state.
    /// Extracts the sort comparison building logic from TreeViewController to make it reusable and testable.
    /// </summary>
    static class TreeViewSortHelper
    {
        /// <summary>
        /// Builds a composite sort comparison function based on the current sorting state of a TreeView.
        /// </summary>
        /// <typeparam name="TItemData">The type of data items in the tree</typeparam>
        /// <param name="treeView">The TreeView to get sorting state from</param>
        /// <param name="sortComparisons">Dictionary mapping column IDs to comparison functions</param>
        /// <returns>A composite comparison function that applies all active sorts in order, or null if no sorting is active</returns>
        public static Comparison<TreeViewItemData<TItemData>> BuildSortComparison<TItemData>(
            MultiColumnTreeView treeView,
            Dictionary<string, Comparison<TreeViewItemData<TItemData>>> sortComparisons)
        {
            var sortedColumns = treeView.sortedColumns;
            if (sortedColumns == null)
                return null;

            var activeComparisons = new List<Comparison<TreeViewItemData<TItemData>>>();

            foreach (var sortedColumnDescription in sortedColumns)
            {
                if (sortedColumnDescription == null)
                    continue;

                if (!sortComparisons.TryGetValue(sortedColumnDescription.columnName, out var sortComparison))
                    continue;

                // Invert the comparison's input arguments depending on the sort direction
                var sortComparisonWithDirection = (sortedColumnDescription.direction == SortDirection.Ascending)
                    ? sortComparison
                    : (x, y) => sortComparison(y, x);

                activeComparisons.Add(sortComparisonWithDirection);
            }

            if (activeComparisons.Count == 0)
                return null;

            // Return a composite comparison that tries each comparison in order until a non-zero result is found
            return (x, y) =>
            {
                var result = 0;
                foreach (var sortComparison in activeComparisons)
                {
                    result = sortComparison.Invoke(x, y);
                    if (result != 0)
                        break;
                }
                return result;
            };
        }
    }
}

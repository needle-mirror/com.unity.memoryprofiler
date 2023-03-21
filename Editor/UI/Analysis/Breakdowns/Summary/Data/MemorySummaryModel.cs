using System;
using System.Collections.Generic;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    // By category memory summary model for breakdown bar and table representation
    /// </summary>
    internal class MemorySummaryModel
    {
        public MemorySummaryModel(string title, string description, bool compareMode, ulong totalA, ulong totalB, List<Row> rows, string residentMemoryWarning)
        {
            Title = title;
            Description = description;
            CompareMode = compareMode;
            TotalA = totalA;
            TotalB = totalB;
            Rows = rows;
            ResidentMemoryWarning = residentMemoryWarning;
        }

        public enum SortableItemDataProperty
        {
            BaseSnapshot,
            ComparedSnapshot,
            Difference
        }

        public enum SortDirection
        {
            Ascending,
            Descending,
        }

        // Table title
        public string Title { get; }

        // Table content description
        public string Description { get; }

        // Indicates that table data is generated for compare mode
        // and contains both sets for SnapshotA and SnapshotB
        public bool CompareMode { get; }

        // Sum of all row items for SnapshotA
        public ulong TotalA { get; }

        // Sum of all row items for SnapshotB
        public ulong TotalB { get; }

        // Table rows
        public List<Row> Rows { get; }

        // Resident memory widget warning message
        public string ResidentMemoryWarning { get; }

        public void Sort(SortableItemDataProperty column, SortDirection direction)
        {
            Rows.Sort(Comparer<Row>.Create((l, r) =>
            {
                if (l.SortPriority != r.SortPriority)
                    return l.SortPriority.CompareTo(r.SortPriority);

                var mod = direction == SortDirection.Ascending ? 1 : -1;
                switch (column)
                {
                    case SortableItemDataProperty.BaseSnapshot:
                        return mod * l.BaseSize.Committed.CompareTo(r.BaseSize.Committed);
                    case SortableItemDataProperty.ComparedSnapshot:
                        return mod * l.ComparedSize.Committed.CompareTo(r.ComparedSize.Committed);
                    case SortableItemDataProperty.Difference:
                    {
                        var diffL = Math.Abs((long)l.BaseSize.Committed - (long)l.ComparedSize.Committed);
                        var diffR = Math.Abs((long)r.BaseSize.Committed - (long)r.ComparedSize.Committed);
                        return mod * diffL.CompareTo(diffR);
                    }
                    default:
                        return 0;
                }
            }));
        }

        /// <summary>
        /// Table row for memory summary views
        /// </summary>
        public struct Row
        {
            public Row(string name, ulong baseTotal, ulong baseInner, ulong comparedTotal, ulong comparedInner, string styleId, string descr, string docsUrl)
            {
                Name = name;
                BaseSize = new MemorySize(baseTotal, baseInner);
                ComparedSize = new MemorySize(comparedTotal, comparedInner);
                StyleId = styleId;
                Description = descr;
                DocumentationUrl = docsUrl;
                CategoryId = IAnalysisViewSelectable.Category.None;
                SortPriority = RowSortPriority.Normal;
                ResidentSizeUnavailable = false;
            }

            public Row(string name, MemorySize baseSize, MemorySize comparedSize, string styleId, string descr, string docsUrl)
            {
                Name = name;
                BaseSize = baseSize;
                ComparedSize = comparedSize;
                StyleId = styleId;
                Description = descr;
                DocumentationUrl = docsUrl;
                CategoryId = IAnalysisViewSelectable.Category.None;
                SortPriority = RowSortPriority.Normal;
                ResidentSizeUnavailable = false;
            }

            /// <summary>
            /// Item name.
            /// </summary>
            public string Name { get; }

            /// <summary>
            /// Base snapshot element memory size
            /// </summary>
            public MemorySize BaseSize { get; }

            /// <summary>
            /// Compared snapshot element memory size
            /// </summary>
            public MemorySize ComparedSize { get; }

            /// <summary>
            /// Style id for associated bar element.
            /// </summary>
            public string StyleId { get; }

            /// <summary>
            /// Item description showed in details panel.
            /// </summary>
            public string Description { get; }

            /// <summary>
            /// Documentation URL link showed in details panel.
            /// </summary>
            public string DocumentationUrl { get; }

            /// <summary>
            /// Associated unique category. Used to select matching items in TreeView.
            /// </summary>
            public IAnalysisViewSelectable.Category CategoryId { get; init; }

            /// <summary>
            /// Indicates that element memory size value is imprecise.
            /// </summary>
            public RowSortPriority SortPriority { get; init; }

            /// <summary>
            /// Don't show resident size for this element us it's unavailable
            /// </summary>
            public bool ResidentSizeUnavailable { get; init; }
        }

        public enum RowSortPriority
        {
            ShowFirst = -1,
            Normal = 0,
            Low = 1,
            ShowLast = 2
        }
    }
}

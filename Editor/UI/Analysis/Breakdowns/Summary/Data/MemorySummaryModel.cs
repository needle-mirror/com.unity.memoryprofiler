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

        /// <summary>Sum of the rows' resident sizes for SnapshotA. Only set by builders whose widgets scale bars against it.</summary>
        public ulong ResidentTotalA { get; init; }

        /// <summary>Sum of the rows' resident sizes for SnapshotB. See <see cref="ResidentTotalA"/>.</summary>
        public ulong ResidentTotalB { get; init; }

        // Table rows
        public List<Row> Rows { get; }

        // Resident memory widget warning message
        public string ResidentMemoryWarning { get; }

        /// <summary>
        /// Sorts rows by <paramref name="column"/>, comparing <see cref="MemorySize.Resident"/> instead of
        /// <see cref="MemorySize.Committed"/> when <paramref name="useResidentSize"/> is set.
        /// </summary>
        public void Sort(SortableItemDataProperty column, SortDirection direction, bool useResidentSize = false)
        {
            Rows.Sort(Comparer<Row>.Create((l, r) =>
            {
                if (l.SortPriority != r.SortPriority)
                    return l.SortPriority.CompareTo(r.SortPriority);

                var mod = direction == SortDirection.Ascending ? 1 : -1;
                switch (column)
                {
                    case SortableItemDataProperty.BaseSnapshot:
                        return mod * ValueOf(l.BaseSize, useResidentSize).CompareTo(ValueOf(r.BaseSize, useResidentSize));
                    case SortableItemDataProperty.ComparedSnapshot:
                        return mod * ValueOf(l.ComparedSize, useResidentSize).CompareTo(ValueOf(r.ComparedSize, useResidentSize));
                    case SortableItemDataProperty.Difference:
                    {
                        var diffL = Math.Abs((long)ValueOf(l.BaseSize, useResidentSize) - (long)ValueOf(l.ComparedSize, useResidentSize));
                        var diffR = Math.Abs((long)ValueOf(r.BaseSize, useResidentSize) - (long)ValueOf(r.ComparedSize, useResidentSize));
                        return mod * diffL.CompareTo(diffR);
                    }
                    default:
                        return 0;
                }
            }));
        }

        static ulong ValueOf(MemorySize size, bool useResidentSize) => useResidentSize ? size.Resident : size.Committed;

        /// <summary>
        /// Table row for memory summary views
        /// </summary>
        public struct Row
        {
            public const long InvalidCount = -1;
            public Row(string name, ulong baseTotal, ulong baseInner, ulong comparedTotal, ulong comparedInner, string styleId, string descr, string docsUrl, long baseCount = InvalidCount, long comparedCount = InvalidCount)
            {
                Name = name;
                BaseSize = new MemorySize(baseTotal, baseInner);
                ComparedSize = new MemorySize(comparedTotal, comparedInner);
                StyleId = styleId;
                Description = descr;
                DocumentationUrl = docsUrl;
                CategoryId = IAnalysisViewSelectable.Category.None;
                StatusMask = 0;
                DeepLinkFlatten = false;
                SortPriority = RowSortPriority.Normal;
                ResidentSizeUnavailable = false;
                BaseCount = baseCount;
                ComparedCount = comparedCount;
            }

            public Row(string name, MemorySize baseSize, MemorySize comparedSize, string styleId, string descr, string docsUrl, long baseCount = InvalidCount, long comparedCount = InvalidCount)
            {
                Name = name;
                BaseSize = baseSize;
                ComparedSize = comparedSize;
                StyleId = styleId;
                Description = descr;
                DocumentationUrl = docsUrl;
                CategoryId = IAnalysisViewSelectable.Category.None;
                StatusMask = 0;
                DeepLinkFlatten = false;
                SortPriority = RowSortPriority.Normal;
                ResidentSizeUnavailable = false;
                BaseCount = baseCount;
                ComparedCount = comparedCount;
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
            /// Base snapshot element instance count
            /// </summary>
            public long BaseCount { get; }

            /// <summary>
            /// Compared snapshot element memory size
            /// </summary>
            public MemorySize ComparedSize { get; }

            /// <summary>
            /// Compared snapshot element instance count
            /// </summary>
            public long ComparedCount { get; }

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
            /// Bitmask (bit N = <see cref="ObjectStatus"/> N) this row aggregates, for deep-linking into a
            /// status-filtered view. 0 means the row is not deep-linkable.
            /// </summary>
            public ulong StatusMask { get; init; }

            /// <summary>Whether a deep-link from this row opens the target view flattened rather than expanded.</summary>
            public bool DeepLinkFlatten { get; init; }

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

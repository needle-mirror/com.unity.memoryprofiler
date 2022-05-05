using System;
using System.Collections.Generic;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    // By category memory breakdown model for
    // breakdown bar and table representation
    /// </summary>
    internal class MemoryBreakdownModel
    {
        public MemoryBreakdownModel(string title, bool compareMode, ulong totalA, ulong totalB, List<Row> rows)
        {
            Title = title;
            CompareMode = compareMode;
            TotalA = totalA;
            TotalB = totalB;
            Rows = rows;
        }

        public enum SortableItemDataProperty
        {
            SnapshotA,
            SnapshotB,
            Difference
        }

        public enum SortDirection
        {
            Ascending,
            Descending,
        }

        public string Title { get; }
        public bool CompareMode { get; }
        public ulong TotalA { get; }
        public ulong TotalB { get; }
        public List<Row> Rows { get; }

        public void Sort(SortableItemDataProperty column, SortDirection direction)
        {
            Rows.Sort(Comparer<Row>.Create((l, r) =>
            {
                var mod = direction == SortDirection.Ascending ? 1 : -1;
                var isUnknownL = l.StyleId == "unknown";
                var isUnknownR = r.StyleId == "unknown";
                if (isUnknownL || isUnknownR)
                    return isUnknownL.CompareTo(isUnknownR);
                switch (column)
                {
                    case SortableItemDataProperty.SnapshotA:
                        return mod * l.TotalA.CompareTo(r.TotalA);
                    case SortableItemDataProperty.SnapshotB:
                        return mod * l.TotalB.CompareTo(r.TotalB);
                    case SortableItemDataProperty.Difference:
                    {
                        var diffL = Math.Abs((long)l.TotalA - (long)l.TotalB);
                        var diffR = Math.Abs((long)r.TotalA - (long)r.TotalB);
                        return mod * diffL.CompareTo(diffR);
                    }
                    default:
                        return 0;
                }
            }));
        }

        public struct Row
        {
            public Row(string name, ulong totalA, ulong usedA, ulong totalB, ulong usedB, string styleId, string descr, string docsUrl)
            {
                Name = name;
                TotalA = totalA;
                UsedA = usedA;
                TotalB = totalB;
                UsedB = usedB;
                StyleId = styleId;
                Description = descr;
                DocumentationUrl = docsUrl;
            }

            public string Name { get; }

            public ulong TotalA { get; }
            public ulong UsedA { get; }

            public ulong TotalB { get; }
            public ulong UsedB { get; }

            public string StyleId { get; }

            public string Description { get; }
            public string DocumentationUrl { get; }
        }
    }
}

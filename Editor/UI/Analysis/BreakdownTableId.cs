using Unity.MemoryProfiler.Editor.UIContentData;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Identifies a breakdown table for per-table UI state (e.g. the References panel rider).
    /// One id per table, not per comparison side: a comparison table's SnapshotA/SnapshotB
    /// selections both map to the same id, since it is one table with two selectable sides.
    /// Deliberately has no default/"Unknown" member so every call site must specify one explicitly.
    /// </summary>
    enum BreakdownTableId
    {
        UnityObjects,
        UnityObjectsComparison,
        RootsAndImpact,
        AllTrackedMemory,
        AllTrackedMemoryComparison,
        MemoryMap,
    }

    // No discard arms: a new BreakdownTableId must fail to compile (CS8509) until every switch over it handles it.
#pragma warning disable CS8524
    static class BreakdownTableIdExtensions
    {
        public static bool IsComparisonTable(this BreakdownTableId table)
        {
            return table switch
            {
                BreakdownTableId.UnityObjectsComparison or BreakdownTableId.AllTrackedMemoryComparison => true,
                BreakdownTableId.UnityObjects or BreakdownTableId.RootsAndImpact or BreakdownTableId.AllTrackedMemory or BreakdownTableId.MemoryMap => false,
            };
        }

        public static string GetDisplayName(this BreakdownTableId table)
        {
            return table switch
            {
                BreakdownTableId.UnityObjects => TextContent.UnityObjectsViewName,
                BreakdownTableId.UnityObjectsComparison => TextContent.GetComparisonViewName(TextContent.UnityObjectsViewName),
                BreakdownTableId.RootsAndImpact => TextContent.RootsAndImpactViewName,
                BreakdownTableId.AllTrackedMemory => TextContent.AllOfMemoryViewName,
                BreakdownTableId.AllTrackedMemoryComparison => TextContent.GetComparisonViewName(TextContent.AllOfMemoryViewName),
                BreakdownTableId.MemoryMap => TextContent.MemoryMapViewName,
            };
        }

        /// <summary>Whether <paramref name="source"/> is a type that <paramref name="table"/> only shows with instances, and <paramref name="snapshot"/> has none.</summary>
        public static bool LacksInstancesOfType(this BreakdownTableId table, CachedSnapshot snapshot, CachedSnapshot.SourceIndex source)
            => table == BreakdownTableId.UnityObjects && !UnityObjectsTableViewController.HasNativeObjectsOfType(snapshot, source);

        /// <summary>
        /// Whether <paramref name="table"/> could ever show <paramref name="source"/>, without requiring the
        /// table to be built: combines each table's kind-only static check (is this <see cref="CachedSnapshot.SourceIndex.SourceId"/>
        /// a shape the table can show at all) with a status check (does the table's status filter dropdown
        /// permanently exclude the item's <see cref="ObjectStatus"/>). Used to populate the "Select In..." menu
        /// for tables that haven't been opened/built yet, where no live <see cref="ITableItemLocator"/> exists.
        /// </summary>
        public static bool CanEverContainSourceId(this BreakdownTableId table, CachedSnapshot snapshot, CachedSnapshot.SourceIndex source)
        {
            var canEverContainKind = table switch
            {
                BreakdownTableId.UnityObjects or BreakdownTableId.UnityObjectsComparison => UnityObjectsTableViewController.CanEverContainSourceId(source.Id),
                BreakdownTableId.RootsAndImpact => RootsAndImpactTableViewController.CanEverContainSourceId(source.Id),
                BreakdownTableId.AllTrackedMemory or BreakdownTableId.AllTrackedMemoryComparison => AllTrackedMemoryTableViewController.CanEverContainSourceId(source.Id),
                BreakdownTableId.MemoryMap => MemoryMapTableViewController.CanEverContainSourceId(source.Id),
            };
            if (!canEverContainKind)
                return false;

            if (LacksInstancesOfType(table, snapshot, source))
                return false;

            // Type rows group whichever objects survive the status filter, so the object status masks don't apply to them.
            if (source.Id is CachedSnapshot.SourceIndex.SourceId.NativeType or CachedSnapshot.SourceIndex.SourceId.ManagedType)
                return true;

            var unavailableStatuses = table.GetUnavailableStatuses();
            if (unavailableStatuses == 0)
                return true;

            var status = snapshot.ObjectStatusComputation.GetStatus(source);
            return (unavailableStatuses & (1ul << (int)status)) == 0;
        }
    }
#pragma warning restore CS8524
}

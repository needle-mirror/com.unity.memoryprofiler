using System;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Locates and reveals a specific object (identified by a <see cref="CachedSnapshot.SourceIndex"/>) inside
    /// one breakdown table, for the Selected Item Details Ping/Select/cross-table reveal buttons.
    /// </summary>
    interface ITableItemLocator
    {
        BreakdownTableId TableId { get; }

        // The optional owner is the snapshot the source index belongs to; it only matters to comparison tables, where the same index can mean different objects in snapshots A and B.

        /// <summary>Whether this table's underlying model could ever contain <paramref name="source"/>, independent of the current filter state.</summary>
        bool CanEverContain(CachedSnapshot.SourceIndex source);

        /// <summary>Whether <paramref name="source"/> is currently shown, and if not, which active filter(s) are hiding it.</summary>
        ItemVisibility GetVisibility(CachedSnapshot.SourceIndex source, CachedSnapshot owner = null);

        /// <summary>Selects, and if <paramref name="frame"/> is true scrolls to, <paramref name="source"/>'s row. Returns false if it can't be found right now.</summary>
        bool TryRevealAndSelect(CachedSnapshot.SourceIndex source, bool frame, CachedSnapshot owner = null);

        /// <summary>
        /// Reveals <paramref name="source"/>'s row (scrolls to it if <paramref name="frame"/> is true, and briefly
        /// highlights it) without changing the table's current selection. Returns false if it can't be found right now.
        /// </summary>
        bool TryReveal(CachedSnapshot.SourceIndex source, bool frame, CachedSnapshot owner = null);

        /// <summary>Whether the table's rows are built and no rebuild is pending, so an item without a row is genuinely absent from it.</summary>
        bool HasSettledRows { get; }

        /// <summary>Requests that <paramref name="source"/> be revealed once the in-flight model rebuild completes. When <paramref name="select"/> is false, the reveal won't change the table's selection.</summary>
        void RevealAfterRebuild(CachedSnapshot.SourceIndex source, bool frame, bool select, CachedSnapshot owner = null);
    }

    /// <summary>Implemented by a table that can tell whether its loaded rows include an item's row, without revealing it.</summary>
    interface IShowsRows
    {
        bool ShowsRow(CachedSnapshot.SourceIndex source);
    }

    /// <summary>Implemented by a comparison table whose Base and Compared tables list the objects of its selected row.</summary>
    interface IComparisonSubTables
    {
        ITableItemLocator BaseTable { get; }
        ITableItemLocator ComparedTable { get; }

        /// <summary>Whether <paramref name="subTable"/>, this table's Base or Compared table, currently lists <paramref name="source"/> of <paramref name="owner"/>.</summary>
        bool SubTableShows(ITableItemLocator subTable, CachedSnapshot.SourceIndex source, CachedSnapshot owner);

        /// <summary>
        /// The same object as <paramref name="source"/> of <paramref name="owner"/> in the comparison's other snapshot, matched by
        /// instance ID or, for a managed object without a native object in both, by address and type, which
        /// <paramref name="matchedByAddress"/> flags as possibly a different object. Only possible when both snapshots were captured in the same session.
        /// </summary>
        bool TryGetCounterpart(CachedSnapshot.SourceIndex source, CachedSnapshot owner, out CachedSnapshot.SourceIndex counterpart, out CachedSnapshot counterpartOwner, out bool matchedByAddress);
    }

    /// <summary>
    /// Implemented by an <see cref="ITableItemLocator"/> that can atomically relax a combination of its own
    /// filter dimensions (see <see cref="ItemVisibility.BlockingFilters"/>) and rebuild.
    /// </summary>
    interface IFilterRelaxationTarget
    {
        void ApplyFilterRelaxation(TableFilterKind filtersToRelax);
    }

    /// <summary>Filter dimensions that can hide an otherwise-representable item from a table.</summary>
    [Flags]
    enum TableFilterKind
    {
        None = 0,
        StatusMask = 1,
        HideExclusiveOnly = 2,
        HideRegionData = 4,
        TextSearch = 8,
        DuplicatesOnly = 16,
        HideUnchanged = 32,
    }

    /// <summary>A request to reveal <see cref="Source"/> in <see cref="Table"/>, which may first need the analysis mode to switch.</summary>
    readonly struct TableRevealRequest
    {
        public readonly BreakdownTableId Table;
        public readonly CachedSnapshot.SourceIndex Source;

        /// <summary>The snapshot <see cref="Source"/> belongs to; see <see cref="ITableItemLocator"/>.</summary>
        public readonly CachedSnapshot Owner;
        public readonly bool Frame;
        public readonly bool Select;

        public TableRevealRequest(BreakdownTableId table, CachedSnapshot.SourceIndex source, CachedSnapshot owner, bool frame, bool select)
        {
            Table = table;
            Source = source;
            Owner = owner;
            Frame = frame;
            Select = select;
        }
    }

    /// <summary>Whether, and why, an item is currently shown in a table.</summary>
    readonly struct ItemVisibility
    {
        public enum State
        {
            Visible,
            HiddenByFilters,
            NotRepresentable,
            /// <summary>The table could show this kind of item but has no row for this one, e.g. a type without instances.</summary>
            NoRow,
            Unknown,
        }

        public readonly State Status;
        public readonly TableFilterKind BlockingFilters;

        public ItemVisibility(State status, TableFilterKind blockingFilters = TableFilterKind.None)
        {
            Status = status;
            BlockingFilters = blockingFilters;
        }

        public static ItemVisibility FromBlockingFilters(TableFilterKind blockingFilters)
            => blockingFilters == TableFilterKind.None
                ? new ItemVisibility(State.Visible)
                : new ItemVisibility(State.HiddenByFilters, blockingFilters);
    }
}

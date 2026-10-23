namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Reveals an item in a table that the current analysis mode (single snapshot or comparison) doesn't show, by
    /// switching to the mode that does. The switch rebuilds the <see cref="AnalysisTabBarController"/>, so the request
    /// is held here and replayed on the new one via <see cref="RevealPending"/>.
    /// </summary>
    class CrossModeRevealProvider
    {
        readonly SnapshotDataService m_SnapshotDataService;
        TableRevealRequest m_Pending;

        public CrossModeRevealProvider(SnapshotDataService snapshotDataService)
        {
            m_SnapshotDataService = snapshotDataService;
        }

        /// <summary>
        /// Whether <paramref name="table"/> could be shown right now by some combination of switching analysis
        /// mode (single/comparison) and/or enabling its view setting.
        /// </summary>
        // CS8524 (the *unnamed* enum value isn't covered) is suppressed: every defined value is handled below, and
        // a runtime value is always a defined enum member. We deliberately keep no discard arm so that adding a new
        // named section type still trips CS8509 (a -warnaserror+ build error) — suppressing CS8524 keeps that gate while
        // not forcing a catch-all that would defeat it.
#pragma warning disable CS8524
        public bool IsReachable(BreakdownTableId table)
        {
            return table switch
            {
                BreakdownTableId.RootsAndImpact or BreakdownTableId.UnityObjects or BreakdownTableId.AllTrackedMemory => true,
                BreakdownTableId.MemoryMap => MemoryProfilerSettings.ShowMemoryMapView,
                BreakdownTableId.UnityObjectsComparison or BreakdownTableId.AllTrackedMemoryComparison => m_SnapshotDataService.Compared != null,
            };
        }
#pragma warning restore CS8524

        /// <summary>Whether switching mode can reveal an item of <paramref name="owner"/> in <paramref name="table"/>.</summary>
        public bool CanSwitchTo(BreakdownTableId table, CachedSnapshot owner)
            => IsReachable(table) && CanShowOwner(table, owner, m_SnapshotDataService.Base);

        /// <summary>
        /// Single snapshot tables only show the base snapshot and their locators ignore the owner, so a request for an
        /// item of another snapshot would match an unrelated base snapshot item sharing its source index.
        /// Internal for testing purposes.
        /// </summary>
        internal static bool CanShowOwner(BreakdownTableId table, CachedSnapshot owner, CachedSnapshot baseSnapshot)
        {
            return table.IsComparisonTable() || owner == null || owner == baseSnapshot;
        }

        public void SwitchModeAndReveal(TableRevealRequest request)
        {
            // Stored before flipping CompareMode: CompareModeChanged fires synchronously, so the analysis view is
            // rebuilt (and calls RevealPending) inside this very call.
            m_Pending = request;
            m_SnapshotDataService.CompareMode = request.Table.IsComparisonTable();
        }

        /// <summary>Replays a request held by <see cref="SwitchModeAndReveal"/> on the rebuilt tab controller.</summary>
        public void RevealPending(AnalysisTabBarController tabController)
        {
            if (!m_Pending.Source.Valid)
                return;

            var pending = m_Pending;
            m_Pending = default;
            tabController.TryRevealInTable(pending.Table, pending.Source, pending.Frame, pending.Select, pending.Owner);
        }
    }
}

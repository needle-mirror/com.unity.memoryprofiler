using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// The reveal plumbing shared by every <see cref="ITableItemLocator"/> table: an instance holds the reveal
    /// deferred until an in-flight model rebuild completes, and the static helpers cover selecting or highlighting
    /// (and optionally framing) a row by tree id, diagnosing/relaxing the
    /// generic filter dimensions (status mask, text search) every table shares, deferring a reveal request
    /// until the in-flight model rebuild it depends on completes, and the "Select In..." reveal
    /// flow itself (prompting to relax filters, or reporting an item can't be shown at all).
    /// </summary>
    class TableRevealHelper
    {
        const string k_RowHighlightUssClass = "memory-profiler-row-highlight";
        const string k_RowHighlightFadeUssClass = "memory-profiler-row-highlight-fade";
        const int k_HighlightFlashDurationMs = 200;
        const int k_HighlightFadeDurationMs = 700;
        const int k_ScrollSettleDelayMs = 50;

        /// <summary>Raised after any table finished a model rebuild, so visibility-dependent UI can re-evaluate.</summary>
        public static event Action AnyTableRebuilt;

        CachedSnapshot.SourceIndex m_PendingSource;
        bool m_PendingFrame;
        bool m_PendingSelect;
        CachedSnapshot m_PendingOwner;
        int m_PendingAttemptsLeft;
        bool m_PendingFollowsPrompt;
        const int k_PendingRevealAttempts = 3;

        public void RevealAfterRebuild(CachedSnapshot.SourceIndex source, bool frame, bool select, CachedSnapshot owner = null)
        {
            m_PendingOwner = owner;
            m_PendingSource = source;
            m_PendingFrame = frame;
            m_PendingSelect = select;
            m_PendingAttemptsLeft = k_PendingRevealAttempts;
            m_PendingFollowsPrompt = false;
        }

        /// <summary>
        /// Finishes a reveal that was deferred via <see cref="RevealAfterRebuild"/>, once the model rebuild it
        /// was waiting on has completed. If the rebuilt table hides the item, this explains why via
        /// <see cref="RevealWithPrompt"/>, but only once, so a filter change that still doesn't reveal it can't loop.
        /// </summary>
        public void ConsumePendingReveal(ITableItemLocator locator)
        {
            ConsumePending(locator);
            AnyTableRebuilt?.Invoke();
        }

        void ConsumePending(ITableItemLocator locator)
        {
            if (!m_PendingSource.Valid)
                return;

            var source = m_PendingSource;
            var frame = m_PendingFrame;
            var select = m_PendingSelect;
            var owner = m_PendingOwner;
            if (select ? locator.TryRevealAndSelect(source, frame, owner) : locator.TryReveal(source, frame, owner))
            {
                m_PendingSource = default;
                return;
            }

            var status = Diagnose(locator, source, owner).Status;
            var hidden = status is ItemVisibility.State.HiddenByFilters or ItemVisibility.State.NotRepresentable or ItemVisibility.State.NoRow;
            if (!hidden && --m_PendingAttemptsLeft > 0)
                return;

            var followsPrompt = m_PendingFollowsPrompt;
            m_PendingSource = default;
            if (!hidden || followsPrompt)
                return;

            RevealWithPrompt(locator, source, frame, select, owner);
            if (m_PendingSource.Valid)
                m_PendingFollowsPrompt = true;
        }

        public static bool TrySelectTreeViewItem(MultiColumnTreeView treeView, int treeId, bool frame)
        {
            if (treeView.viewController.GetIndexForId(treeId) == -1)
                return false;

            treeView.SetSelectionById(treeId);
            if (frame)
            {
                FocusOnceShown(treeView);
                treeView.schedule.Execute(() => FrameTreeViewItem(treeView, treeId));
            }
            return true;
        }

        const int k_FocusAttempts = 10;

        /// <summary>
        /// Focuses <paramref name="view"/> so its selection shows as active rather than grey. A table whose tab was only
        /// just switched to can't take focus until its styles are resolved, and Focus() is silently ignored until then.
        /// </summary>
        static void FocusOnceShown(VisualElement view, int attemptsLeft = k_FocusAttempts)
        {
            if (view.canGrabFocus)
                view.Focus();
            else if (attemptsLeft > 0)
                view.schedule.Execute(() => FocusOnceShown(view, attemptsLeft - 1));
        }

        /// <summary>Scrolls to <paramref name="treeId"/>'s row and then places it about a third of the way down the view, rather than at whichever edge is closest.</summary>
        public static void FrameTreeViewItem(BaseTreeView treeView, int treeId)
        {
            treeView.ScrollToItemById(treeId);
            if (treeView.virtualizationMethod != CollectionVirtualizationMethod.FixedHeight)
                return;

            treeView.schedule.Execute(() =>
            {
                var index = treeView.viewController.GetIndexForId(treeId);
                var scrollView = treeView.Q<ScrollView>();
                if (index < 0 || scrollView == null)
                    return;

                var offset = index * treeView.fixedItemHeight - scrollView.contentViewport.layout.height / 3f;
                scrollView.scrollOffset = new Vector2(scrollView.scrollOffset.x, Mathf.Max(0f, offset));
            });
        }

        /// <summary>
        /// Reveals (and if <paramref name="frame"/>, scrolls to and briefly highlights) the row for <paramref name="treeId"/>
        /// without changing the table's current selection — the Ping counterpart to <see cref="TrySelectTreeViewItem"/>.
        /// </summary>
        public static bool TryHighlightTreeViewItem(MultiColumnTreeView treeView, int treeId, bool frame)
        {
            if (treeView.viewController.GetIndexForId(treeId) == -1)
                return false;

            void ApplyHighlight()
            {
                var element = treeView.GetRootElementForId(treeId);
                if (element == null)
                    return;

                // A brief solid flash, then swap to a class whose only change is a transitioned background-color
                // back to transparent — UI Toolkit animates a property across a class swap the same way it
                // would across any other style change, so this fades rather than snapping off.
                element.AddToClassList(k_RowHighlightUssClass);
                element.schedule.Execute(() =>
                {
                    element.RemoveFromClassList(k_RowHighlightUssClass);
                    element.AddToClassList(k_RowHighlightFadeUssClass);
                    element.schedule.Execute(() => element.RemoveFromClassList(k_RowHighlightFadeUssClass)).StartingIn(k_HighlightFadeDurationMs);
                }).StartingIn(k_HighlightFlashDurationMs);
            }

            if (frame)
            {
                treeView.schedule.Execute(() =>
                {
                    FrameTreeViewItem(treeView, treeId);
                    // Scrolling can (re)virtualize rows asynchronously; give it a moment to settle before
                    // grabbing the row's VisualElement, or ApplyHighlight may find nothing to highlight yet.
                    treeView.schedule.Execute(ApplyHighlight).StartingIn(k_ScrollSettleDelayMs);
                });
            }
            else
            {
                ApplyHighlight();
            }
            return true;
        }

        /// <summary>
        /// Source indices only identify an item within their own snapshot, so with an <paramref name="onlyA"/> / <paramref name="onlyB"/>
        /// owner restriction, an index of the other snapshot must not match.
        /// Internal for testing purposes.
        /// </summary>
        internal static bool MatchesComparisonSide(ComparisonSourceIndex indices, CachedSnapshot.SourceIndex source, bool onlyA, bool onlyB)
            => (!onlyB && indices.InA == source) || (!onlyA && indices.InB == source);

        internal static bool ListsBaseItem(int[] mergedItemIds, int baseItemId)
            => baseItemId >= 0 && mergedItemIds != null && Array.IndexOf(mergedItemIds, baseItemId) >= 0;

        /// <summary>
        /// Resolves <paramref name="path"/> by name, level by level, to the tree item ids along it; false unless every
        /// segment is found, so a partial match can never stand in for the item.
        /// Internal for testing purposes.
        /// </summary>
        internal static bool TryResolveNamePath(IReadOnlyList<string> path, IEnumerable<int> rootIds, Func<int, IEnumerable<int>> getChildIds, Func<int, string> getName, out List<int> idPath)
        {
            idPath = new List<int>();
            if (path.Count == 0 || rootIds == null)
                return false;

            var candidates = rootIds;
            for (int i = 0; i < path.Count; i++)
            {
                var found = false;
                foreach (var id in candidates)
                {
                    if (getName(id) != path[i])
                        continue;

                    found = true;
                    idPath.Add(id);
                    if (i < path.Count - 1)
                    {
                        candidates = getChildIds(id);
                        if (candidates == null)
                            found = false;
                    }
                    break;
                }
                if (!found)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// The status-mask half of filter diagnosis, identical for every table: a table's <see cref="StatusFilterButton"/>
        /// blocks <paramref name="source"/> when its <see cref="ObjectStatus"/> isn't in <paramref name="filter"/>'s
        /// <see cref="CompoundSearchFilter.ExclusiveStatusMask"/>. Text-search matching is left to each table's own
        /// <c>GetVisibility</c>, since which name/type candidates to test differs per table's row shape.
        /// </summary>
        public static bool IsBlockedByStatusMask(CachedSnapshot snapshot, CachedSnapshot.SourceIndex source, CompoundSearchFilter filter)
        {
            if (filter?.ExclusiveStatusMask == null)
                return false;

            var status = snapshot.ObjectStatusComputation.GetStatus(source);
            return (filter.ExclusiveStatusMask.Value & (1ul << (int)status)) == 0;
        }

        /// <summary>
        /// The filter dimensions shared by tables whose rows are only matched by their own name or status: the
        /// status mask, and a text search that neither the item's name nor its status passes.
        /// </summary>
        public static TableFilterKind GetBlockingStatusAndOwnNameFilters(CachedSnapshot snapshot, CachedSnapshot.SourceIndex source, CompoundSearchFilter filter)
        {
            var blocking = TableFilterKind.None;
            if (filter == null)
                return blocking;

            if (IsBlockedByStatusMask(snapshot, source, filter))
                blocking |= TableFilterKind.StatusMask;

            if (filter.SearchStringFilter != null && !filter.PassesTextOrStatusSearch(source.GetName(snapshot), snapshot.ObjectStatusComputation.GetStatus(source)))
                blocking |= TableFilterKind.TextSearch;

            return blocking;
        }

        /// <summary>
        /// Clears the filter dimensions every table shares (status mask, text search) as one step of a
        /// <see cref="IFilterRelaxationTarget.ApplyFilterRelaxation"/> implementation. Callers still trigger
        /// their own single rebuild afterwards; this deliberately uses <see cref="INotifyValueChanged{T}.SetValueWithoutNotify"/>
        /// rather than driving the field's own change event, so relaxing several dimensions at once (e.g. from
        /// a table with both its own dimensions and these) causes exactly one rebuild, not one per dimension.
        /// </summary>
        public static void RelaxStatusAndTextFilters(TableFilterKind filtersToRelax, StatusFilterButton statusFilterButton, ToolbarSearchField searchField, TableFilterManagerBase filterManager)
        {
            if ((filtersToRelax & TableFilterKind.StatusMask) != 0)
                statusFilterButton?.SetMask(null);

            if ((filtersToRelax & TableFilterKind.TextSearch) != 0)
            {
                filterManager.SearchFilter = null;
                searchField?.SetValueWithoutNotify(string.Empty);
            }
        }

        /// <summary>Names the blocking filters by the labels of the controls that set them. Internal for testing purposes.</summary>
        internal static string DescribeFilters(TableFilterKind blocking)
        {
            var parts = new List<string>();
            if ((blocking & TableFilterKind.HideExclusiveOnly) != 0)
                parts.Add(string.Format(TextContent.FilterControlNameFormat, TextContent.RootsAndImpactHideExclusiveToggle));
            if ((blocking & TableFilterKind.HideRegionData) != 0)
                parts.Add(string.Format(TextContent.FilterControlNameFormat, TextContent.RootsAndImpactRegionDataToggle));
            if ((blocking & TableFilterKind.DuplicatesOnly) != 0)
                parts.Add(string.Format(TextContent.FilterControlNameFormat, TextContent.ShowPotentialDuplicatesOnly));
            if ((blocking & TableFilterKind.HideUnchanged) != 0)
                parts.Add(string.Format(TextContent.FilterControlNameFormat, TextContent.ShowUnchanged));
            if ((blocking & TableFilterKind.StatusMask) != 0)
                parts.Add(string.Format(TextContent.FilterControlNameFormat, TextContent.ObjectStatusFilterLabel));
            if ((blocking & TableFilterKind.TextSearch) != 0)
                parts.Add(TextContent.FilterKindTextSearch);

            if (parts.Count <= 1)
                return parts.Count == 0 ? string.Empty : parts[0];

            var last = parts[parts.Count - 1];
            parts.RemoveAt(parts.Count - 1);
            return string.Format(TextContent.ListLastItemFormat, string.Join(", ", parts), last);
        }

        /// <summary>
        /// The single entry point for "Select In..." reveal requests: tries the reveal, and on
        /// failure diagnoses why via <see cref="ITableItemLocator.GetVisibility"/> and reacts accordingly —
        /// an informational dialog for an item the table can never show or a filter it can't diagnose, an
        /// offer to relax the diagnosed filter(s) and retry after the table's own <see cref="IFilterRelaxationTarget"/>,
        /// or a silent <see cref="ITableItemLocator.RevealAfterRebuild"/>
        /// when the item should already be visible but the reveal still failed (e.g. a rebuild triggered
        /// elsewhere, such as toggling a filter directly on the table, hasn't landed yet).
        /// </summary>
        /// <returns>False when the item won't be revealed: the table can't show it, or the user kept the filters that hide it.</returns>
        public static bool RevealWithPrompt(ITableItemLocator locator, CachedSnapshot.SourceIndex source, bool frame, bool select, CachedSnapshot owner = null)
        {
            if (locator == null)
                return false;

            if (select ? locator.TryRevealAndSelect(source, frame, owner) : locator.TryReveal(source, frame, owner))
                return true;

            var visibility = Diagnose(locator, source, owner);
            switch (visibility.Status)
            {
                case ItemVisibility.State.NotRepresentable:
                case ItemVisibility.State.NoRow:
                    var messageFormat = visibility.Status == ItemVisibility.State.NoRow ? TextContent.PingSelectNoRowMessageFormat : TextContent.PingSelectNotRepresentableMessageFormat;
                    EditorUtilityCompatibilityHelper.DisplayAlertDialog(
                        TextContent.PingSelectNotRepresentableDialogTitle,
                        string.Format(messageFormat, locator.TableId.GetDisplayName()),
                        TextContent.PingSelectNotRepresentableDialogOK);
                    return false;
                case ItemVisibility.State.HiddenByFilters:
                    return PromptFilterRelaxation(locator, source, frame, select, visibility.BlockingFilters, owner);
                case ItemVisibility.State.Visible:
                case ItemVisibility.State.Unknown:
                default:
                    // The item should be showable but the reveal still failed — most likely a rebuild that
                    // was triggered independently (e.g. a filter toggled directly on the table) hasn't landed
                    // in the row store yet. Queue it silently rather than leaving the click with no effect.
                    locator.RevealAfterRebuild(source, frame, select, owner);
                    return true;
            }
        }

        /// <summary>
        /// <paramref name="locator"/>'s visibility for an item whose reveal just failed: one it reports as shown, but which has
        /// no row although its rows are settled, is absent from the table rather than waiting on a rebuild.
        /// Internal for testing purposes.
        /// </summary>
        internal static ItemVisibility Diagnose(ITableItemLocator locator, CachedSnapshot.SourceIndex source, CachedSnapshot owner)
        {
            var visibility = locator.GetVisibility(source, owner);
            return visibility.Status is ItemVisibility.State.Visible or ItemVisibility.State.Unknown && locator.HasSettledRows
                ? new ItemVisibility(ItemVisibility.State.NoRow)
                : visibility;
        }

        /// <summary>
        /// The table a detail view should act on for <paramref name="source"/>: the tab currently shown when a tab controller is known,
        /// or its Base or Compared table that created the view while that still lists the item, else the table that created the view.
        /// </summary>
        public static ITableItemLocator ResolveCurrentLocator(AnalysisTabBarController tabController, ITableItemLocator creatingTable, CachedSnapshot.SourceIndex source, CachedSnapshot owner)
            => tabController == null ? creatingTable : ResolveLocatorWithin(tabController.ActiveLocator, creatingTable, source, owner);

        /// <summary>See <see cref="ResolveCurrentLocator"/>, for the shown table <paramref name="activeTable"/>. Internal for testing purposes.</summary>
        internal static ITableItemLocator ResolveLocatorWithin(ITableItemLocator activeTable, ITableItemLocator creatingTable, CachedSnapshot.SourceIndex source, CachedSnapshot owner)
            => activeTable is IComparisonSubTables host && host.SubTableShows(creatingTable, source, owner) ? creatingTable : activeTable;

        /// <summary>The name of <paramref name="locator"/>, qualified by its comparison when it is one of <paramref name="host"/>'s Base or Compared tables.</summary>
        public static string GetTableName(ITableItemLocator locator, IComparisonSubTables host)
        {
            if (host != null && locator != null && host is ITableItemLocator hostTable)
            {
                if (locator == host.BaseTable)
                    return string.Format(TextContent.ComparisonSubTableNameFormat, hostTable.TableId.GetDisplayName(), TextContent.BaseTableTitle);
                if (locator == host.ComparedTable)
                    return string.Format(TextContent.ComparisonSubTableNameFormat, hostTable.TableId.GetDisplayName(), TextContent.ComparedTableTitle);
            }
            return locator?.TableId.GetDisplayName();
        }

        /// <summary>
        /// Like <see cref="RevealWithPrompt"/>, but when the item can't be revealed in <paramref name="locator"/> it offers every way to
        /// get to it — clearing the hiding filters, and switching to any other table that could show it, as a
        /// menu at <paramref name="guiRect"/> (GUI-view coordinates), so no Cancel button is needed.
        /// </summary>
        public static void RevealWithChoice(ITableItemLocator locator, AnalysisTabBarController tabController, CachedSnapshot snapshot,
            CachedSnapshot.SourceIndex source, bool frame, bool select, Rect guiRect)
        {
            if (locator == null && tabController == null)
                return;

            var owner = snapshot;

            ItemVisibility visibility = default;
            if (locator != null)
            {
                if (select ? locator.TryRevealAndSelect(source, frame, owner) : locator.TryReveal(source, frame, owner))
                    return;

                visibility = Diagnose(locator, source, owner);
                if (visibility.Status is ItemVisibility.State.Visible or ItemVisibility.State.Unknown)
                {
                    locator.RevealAfterRebuild(source, frame, select, owner);
                    return;
                }
            }

            var options = new List<(string label, Action action)>();
            var currentName = locator?.TableId.GetDisplayName();
            if (locator is IFilterRelaxationTarget relaxable && visibility.Status == ItemVisibility.State.HiddenByFilters && visibility.BlockingFilters != TableFilterKind.None)
            {
                var blocking = visibility.BlockingFilters;
                options.Add((string.Format(TextContent.PingSelectChangeFiltersAndRevealInFormat, currentName, DescribeFilters(blocking)), () =>
                {
                    relaxable.ApplyFilterRelaxation(blocking);
                    locator.RevealAfterRebuild(source, frame, select, owner);
                }
                ));
            }

            if (tabController != null)
            {
                foreach (BreakdownTableId table in Enum.GetValues(typeof(BreakdownTableId)))
                {
                    if ((locator != null && table == locator.TableId) || !tabController.CanRevealIn(table, owner))
                        continue;

                    var loaded = tabController.TryGetLoadedLocator(table);
                    if (!(loaded != null ? loaded.CanEverContain(source) : table.CanEverContainSourceId(snapshot, source)))
                        continue;

                    // Selected rather than pinged: the ping fades, and after switching tables nothing would show which row it was.
                    var target = table;
                    options.Add((string.Format(TextContent.PingSelectSelectInTableFormat, target.GetDisplayName()), () => tabController.TryRevealInTable(target, source, frame, select: true, owner)));
                }
            }

            if (options.Count == 0)
            {
                RevealWithPrompt(locator, source, frame, select, owner);
                return;
            }

            var menu = new GenericMenu();
            foreach (var option in options)
            {
                var action = option.action;
                menu.AddItem(new GUIContent(SelectInMenuBuilder.EscapeForGenericMenu(option.label)), false, () => action());
            }
            menu.DropDown(guiRect);
        }

        static bool PromptFilterRelaxation(ITableItemLocator locator, CachedSnapshot.SourceIndex source, bool frame, bool select, TableFilterKind blocking, CachedSnapshot owner)
        {
            var tableName = locator.TableId.GetDisplayName();

            if (blocking == TableFilterKind.None || locator is not IFilterRelaxationTarget relaxable)
            {
                EditorUtilityCompatibilityHelper.DisplayAlertDialog(
                    TextContent.PingSelectHiddenByFiltersDialogTitle,
                    string.Format(TextContent.PingSelectHiddenByUnknownFilterMessageFormat, tableName),
                    TextContent.PingSelectNotRepresentableDialogOK);
                return false;
            }

            var message = string.Format(TextContent.PingSelectHiddenByFiltersMessageFormat, tableName, DescribeFilters(blocking));
            if (UnityEngine.Application.isBatchMode || EditorUtilityCompatibilityHelper.DisplayDecisionDialogWithOptOut(
                TextContent.PingSelectHiddenByFiltersDialogTitle, message,
                TextContent.PingSelectHiddenByFiltersDialogOK, TextContent.PingSelectHiddenByFiltersDialogCancel,
                EditorUtilityCompatibilityHelper.DialogOptOutDecisionType.ForThisSession, MemoryProfilerSettings.PingSelectFilterRelaxationOptOutKey))
            {
                relaxable.ApplyFilterRelaxation(blocking);
                locator.RevealAfterRebuild(source, frame, select, owner);
                return true;
            }
            return false;
        }
    }
}

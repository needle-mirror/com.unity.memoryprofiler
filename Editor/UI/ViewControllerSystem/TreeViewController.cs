using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Base Tree view controller class, abstracting shared code used by all Tree View Controllers
    /// </summary>
    abstract class TreeViewController<TModel, TTreeItemData> : ViewController where TModel : TreeModel<TTreeItemData>
    {
        // Model.
        protected TModel m_Model;
        protected AsyncWorker<TModel> m_BuildModelWorker;

        // View.
        protected MultiColumnTreeView m_TreeView;

        readonly string m_IDOfDefaultColumnWithPercentageBasedWidth = null;
        protected abstract ToolbarSearchField SearchField { get; }

        public TreeViewController(string idOfDefaultColumnWithPercentageBasedWidth)
        {
            m_IDOfDefaultColumnWithPercentageBasedWidth = idOfDefaultColumnWithPercentageBasedWidth;
        }

        public event Action<ContextualMenuPopulateEvent> HeaderContextMenuPopulateEvent;

        public void ClearSelection()
        {
            // TreeView doesn't have ClearSelection without notification.
            // We don't need notification as we need notification only
            // on user input
            m_TreeView.SetSelectionWithoutNotify(new int[0]);
        }

        /// <summary>
        /// Selects the first item in the table.
        /// </summary>
        /// <returns>true if an item was selected, false if not.</returns>
        public bool SelectFirstItem()
        {
            if (m_TreeView.GetTreeCount() <= 0)
                return false;
            // force a selection notification by making sure the selected index changed
            if (m_TreeView.selectedIndex == 0)
                ClearSelection();
            m_TreeView.SetSelection(0);
            return true;
        }

        protected virtual void ConfigureTreeView()
        {
            m_TreeView.RegisterCallback<GeometryChangedEvent>(ConfigureInitialTreeViewLayout);

            if (SearchField != null)
            {
                SearchField.RegisterValueChangedCallback(OnSearchValueChanged);
                SearchField.RegisterCallback<FocusOutEvent>(OnSearchFocusLost);
            }

#if UNITY_2022_2_OR_NEWER
            m_TreeView.selectionChanged += OnTreeViewSelectionChanged;
#else
            m_TreeView.onSelectionChange += OnTreeViewSelectionChanged;
#endif
            m_TreeView.columnSortingChanged += OnTreeViewSortingChanged;
            m_TreeView.headerContextMenuPopulateEvent += GenerateContextMenu;
        }

        protected virtual void ConfigureInitialTreeViewLayout(GeometryChangedEvent evt)
        {
            // There is currently no way to set a tree view column's initial width as a percentage from UXML/USS, so we must do it manually once on load.
            if (!string.IsNullOrEmpty(m_IDOfDefaultColumnWithPercentageBasedWidth))
            {
                var column = m_TreeView.columns[m_IDOfDefaultColumnWithPercentageBasedWidth];
                column.width = m_TreeView.layout.width * 0.4f;
            }
            m_TreeView.UnregisterCallback<GeometryChangedEvent>(ConfigureInitialTreeViewLayout);
        }

        // TODO: Consider consolidating all instance of OnTreeViewSelectionChanged, at least to a comon shared part
        protected abstract void OnTreeViewSelectionChanged(IEnumerable<object> items);

        protected virtual void OnTreeViewSortingChanged()
        {
            var sortedColumns = m_TreeView.sortedColumns;
            if (sortedColumns == null)
                return;

            BuildModelAsync();

            // Analytics
            {
                using var enumerator = sortedColumns.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    var sortDescription = enumerator.Current;
                    if (sortDescription == null)
                        continue;

                    MemoryProfilerAnalytics.StartEvent<MemoryProfilerAnalytics.SortedColumnEvent>();
                    MemoryProfilerAnalytics.EndEvent(new MemoryProfilerAnalytics.SortedColumnEvent()
                    {
                        viewName = MemoryProfilerAnalytics.GetAnalyticsViewNameForOpenPage(),
                        Ascending = sortDescription.direction == SortDirection.Ascending,
                        shown = sortDescription.columnIndex,
                        fileName = sortDescription.columnName
                    });
                    MemoryProfilerAnalytics.AddInteractionCountToEvent<
                        MemoryProfilerAnalytics.InteractionsInPage,
                        MemoryProfilerAnalytics.PageInteractionType>(
                        MemoryProfilerAnalytics.PageInteractionType.TableSortingWasChanged);
                }
            }
        }

        void OnSearchValueChanged(ChangeEvent<string> evt)
        {
            BuildModelAsync();
        }

        void OnSearchFocusLost(FocusOutEvent evt)
        {
            MemoryProfilerAnalytics.AddInteractionCountToEvent<MemoryProfilerAnalytics.InteractionsInPage, MemoryProfilerAnalytics.PageInteractionType>(
                MemoryProfilerAnalytics.PageInteractionType.SearchInPageWasUsed);
        }

        void GenerateContextMenu(ContextualMenuPopulateEvent evt, Column column)
        {
            if (HeaderContextMenuPopulateEvent == null)
                GenerateEmptyContextMenu(evt, column);
            else
                HeaderContextMenuPopulateEvent(evt);
        }

        void GenerateEmptyContextMenu(ContextualMenuPopulateEvent evt, Column column)
        {
            evt.menu.ClearItems();
            evt.StopImmediatePropagation();
        }

        protected abstract void BuildModelAsync();

        protected virtual void RefreshView()
        {
            m_TreeView.SetRootItems(m_Model.RootNodes);
            m_TreeView.Rebuild();
        }

        protected void ConfigureTreeViewColumn(string columnName, string columnTitle, Action<VisualElement, int> bindCell, int width = 0, bool visible = true, Func<VisualElement> makeCell = null)
        {
            var column = m_TreeView.columns[columnName];
            column.title = columnTitle;
            column.bindCell = bindCell;
            column.visible = visible;
            if (width != 0)
            {
                column.width = width;
                column.minWidth = width;
            }
            if (makeCell != null)
                column.makeCell = makeCell;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                m_BuildModelWorker?.Dispose();

            base.Dispose(disposing);
        }
    }
}

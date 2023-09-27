using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    interface INamedTreeItemData
    {
        string Name { get; }
    }
    /// <summary>
    /// Base Tree view controller class, abstracting shared code used by all Tree View Controllers
    /// </summary>
    abstract class TreeViewController<TModel, TTreeItemData> : ViewController where TModel : TreeModel<TTreeItemData> where TTreeItemData : INamedTreeItemData
    {
        // Model.
        protected TModel m_Model;
        protected AsyncWorker<TModel> m_BuildModelWorker;

        // View.
        protected MultiColumnTreeView m_TreeView;

        readonly string m_IDOfDefaultColumnWithPercentageBasedWidth = null;
        protected abstract ToolbarSearchField SearchField { get; }

        protected event Action<IScopedFilter<string>> SearchFilterChanged = null;

        List<string> m_SelectionPath = new List<string>();

        public TreeViewController(string idOfDefaultColumnWithPercentageBasedWidth)
        {
            m_IDOfDefaultColumnWithPercentageBasedWidth = idOfDefaultColumnWithPercentageBasedWidth;
        }

        public event Action<ContextualMenuPopulateEvent> HeaderContextMenuPopulateEvent;

        public virtual void ClearSelection()
        {
            // TreeView doesn't have ClearSelection without notification.
            // We don't need notification as we need notification only
            // on user input
            m_TreeView.SetSelectionWithoutNotify(new int[0]);
            m_SelectionPath.Clear();
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
            }

#if UNITY_2022_2_OR_NEWER
            m_TreeView.selectionChanged += OnTreeViewSelectionChanged;
#else
            m_TreeView.onSelectionChange += OnTreeViewSelectionChanged;
#endif
            m_TreeView.columnSortingChanged += OnTreeViewSortingChanged;
            m_TreeView.headerContextMenuPopulateEvent += GenerateContextMenu;
        }

        protected void ConfigureInitialTreeViewLayout(GeometryChangedEvent evt)
        {
            // There is currently no way to set a tree view column's initial width as a percentage from UXML/USS, so we must do it manually once on load.
            if (!string.IsNullOrEmpty(m_IDOfDefaultColumnWithPercentageBasedWidth))
            {
                var column = m_TreeView.columns[m_IDOfDefaultColumnWithPercentageBasedWidth];
                column.width = m_TreeView.layout.width * 0.4f;
            }
            m_TreeView.UnregisterCallback<GeometryChangedEvent>(ConfigureInitialTreeViewLayout);
        }

        protected void OnTreeViewSelectionChanged(IEnumerable<object> items)
        {
            m_SelectionPath.Clear();
            var selectedIndex = m_TreeView.selectedIndex;
            if (selectedIndex == -1)
                return;

            var itemId = m_TreeView.GetIdForIndex(selectedIndex);
            var itemData = m_TreeView.GetItemDataForIndex<TTreeItemData>(selectedIndex);
            OnTreeItemSelected(itemId, itemData);

            m_SelectionPath.Add(itemData.Name);
            var parentId = m_TreeView.GetParentIdForIndex(selectedIndex);
            while (parentId >= 0)
            {
                itemData = m_TreeView.GetItemDataForId<TTreeItemData>(parentId);
                m_SelectionPath.Insert(0, itemData.Name);
                parentId = m_TreeView.viewController.GetParentId(parentId);
            }
        }

        protected abstract void OnTreeItemSelected(int itemId, TTreeItemData itemData);

        protected virtual void OnTreeViewSortingChanged()
        {
            var sortedColumns = m_TreeView.sortedColumns;
            if (sortedColumns == null)
                return;

            BuildModelAsync();
        }

        void OnSearchValueChanged(ChangeEvent<string> evt)
        {
            if (SearchFilterChanged != null)
            {
                var searchText = evt.newValue;
                var searchFilter = ScopedContainsTextFilter.Create(searchText);
                SearchFilterChanged(searchFilter);
            }
            else if (IsViewLoaded)
                BuildModelAsync();
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
            RestoreSelection();
        }

        void RestoreSelection()
        {
            var idPath = new List<int>();
            bool failedToFindFullPath = false;
            if (m_SelectionPath.Count > 0)
            {
                var rootIds = m_TreeView.GetRootIds();
                if (rootIds != null)
                {
                    var ids = rootIds.GetEnumerator();
                    for (int i = 0; i < m_SelectionPath.Count; i++)
                    {
                        var name = m_SelectionPath[i];
                        while (ids.MoveNext())
                        {
                            var itemData = m_TreeView.GetItemDataForId<TTreeItemData>(ids.Current);
                            if (itemData.Name == name)
                            {
                                idPath.Add(ids.Current);
                                var childIds = m_TreeView.viewController.GetChildrenIds(ids.Current);
                                if (childIds == null)
                                {
                                    failedToFindFullPath = i == m_SelectionPath.Count - 1;
                                }
                                else
                                {
                                    // switch out the ids and jump to the next name
                                    ids.Dispose();
                                    ids = childIds.GetEnumerator();
                                }
                                break;
                            }
                        }
                        if (failedToFindFullPath)
                            break;
                    }
                    ids.Dispose();
                }
            }
            if (idPath.Count > 0 && !failedToFindFullPath)
            {
                // if it was fully found, select, notify, expand towards and frame the selection
                var idToSelect = idPath[idPath.Count - 1];
                m_TreeView.SetSelectionById(idToSelect);
                foreach (var idInPath in idPath)
                {
                    m_TreeView.ExpandItem(idInPath);
                }
                m_TreeView.ScrollToItemById(idToSelect);
            }
            else
                // otherwise silently clear selection so that it doesn't count as an active user selection,
                // doesn't clear m_SelectionPath and allows reconstructing it when search is cleared
                m_TreeView.SetSelectionByIdWithoutNotify(new int[] { -1 });

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

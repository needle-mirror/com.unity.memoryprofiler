#define REMOVE_VIEW_HISTORY
using System.Collections;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEditor;
using System;
using System.Text;
using Unity.EditorCoroutines.Editor;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class OldViewLogic : IViewPaneEventListener
    {
        public event Action<ViewPane> ViewPaneChanged = delegate {};

        UIState UIState { get { return m_UIStateHolder.UIState; } }
        UI.ViewPane currentViewPane
        {
            get
            {
                if (UIState.CurrentMode == null) return null;
                return UIState.CurrentMode.CurrentViewPane;
            }
        }

        Button m_ViewSelectorMenuButton;
        Label m_ViewSelectorMenuButtonLabel;
        VisualElement m_ViewSelectorMenu;
        StringBuilder m_TabelNameStringBuilder = new StringBuilder();
        Dictionary<string, GUIContent> m_UIFriendlyViewOptionNamesWithFullPath = new Dictionary<string, GUIContent>();
        Dictionary<string, GUIContent> m_UIFriendlyViewOptionNames = new Dictionary<string, GUIContent>();

        IUIStateHolder m_UIStateHolder;

        // TODO: fixup Memory Map
        VisualElement m_ToolbarExtension;

        [SerializeField]
        Vector2 m_ViewDropdownSize;

        AnalysisWindow m_MainViewPanel;

        public OldViewLogic(IUIStateHolder memoryProfilerWindow, AnalysisWindow mainViewPanel, VisualElement toolbarExtension, Button viewSelectionButton)
        {
            m_ToolbarExtension = toolbarExtension;
            m_MainViewPanel = mainViewPanel;
            m_UIStateHolder = memoryProfilerWindow;
            m_ViewSelectorMenuButton = viewSelectionButton;
            m_ViewSelectorMenuButton.clicked += () => OpenViewSelectionDropdown(m_ViewSelectorMenuButton.GetRect());
            m_ViewSelectorMenuButtonLabel = m_ViewSelectorMenuButton.Q<Label>();
            m_ViewSelectorMenu = m_ViewSelectorMenuButton;
        }

        void UI.IViewPaneEventListener.OnRepaint()
        {
            m_UIStateHolder.Repaint();
        }

        void UI.IViewPaneEventListener.OnOpenLink(Database.LinkRequest link)
        {
            OpenLink(link);
        }

        void UI.IViewPaneEventListener.OnOpenLink(Database.LinkRequest link, UIState.SnapshotMode mode)
        {
            var tableLinkRequest = link as Database.LinkRequestTable;
            if (tableLinkRequest != null)
            {
                var tableRef = new Database.TableReference(tableLinkRequest.LinkToOpen.TableName, link.Parameters);
                var table = mode.GetSchema().GetTableByReference(tableRef);

                var pane = new UI.SpreadsheetPane(m_UIStateHolder, this);
                if (pane.OpenLinkRequest(tableLinkRequest, tableRef, table))
                {
                    UIState.TransitModeToOwningTable(table);
                    TransitPane(pane, true);
                }
            }
        }

        void UI.IViewPaneEventListener.OnOpenMemoryMap()
        {
            if (UIState.CurrentMode is UI.UIState.SnapshotMode)
            {
                OpenMemoryMap(null);
            }
            else
            {
                OpenMemoryMapDiff(null);
            }
        }

        void UI.IViewPaneEventListener.OnOpenTreeMap()
        {
            OpenTreeMap(null);
        }

        void OpenLink(Database.LinkRequest link)
        {
            var tableLinkRequest = link as Database.LinkRequestTable;
            if (tableLinkRequest != null)
            {
                try
                {
                    ProgressBarDisplay.ShowBar("Resolving Link...");
                    var tableRef = new Database.TableReference(tableLinkRequest.LinkToOpen.TableName, link.Parameters);
                    var table = UIState.CurrentMode.GetSchema().GetTableByReference(tableRef);

                    ProgressBarDisplay.UpdateProgress(0.0f, "Updating Table...");
                    if (table.Update())
                    {
                        UIState.CurrentMode.UpdateTableSelectionNames();
                    }

                    ProgressBarDisplay.UpdateProgress(0.75f, "Opening Table...");
                    var pane = new UI.SpreadsheetPane(m_UIStateHolder, this);
                    if (pane.OpenLinkRequest(tableLinkRequest, tableRef, table))
                    {
                        UIState.TransitModeToOwningTable(table);
                        TransitPane(pane, true);
                    }
                }
                finally
                {
                    ProgressBarDisplay.ClearBar();
                }
            }
            else
                Debug.LogWarning("Cannot open unknown link '" + link.ToString() + "'");
        }

        void OpenViewSelectionDropdown(Rect viewDropdownRect)
        {
            int curTableIndex = -1;
            if (currentViewPane is UI.SpreadsheetPane)
            {
                UI.SpreadsheetPane pane = (UI.SpreadsheetPane)currentViewPane;
                curTableIndex = pane.CurrentTableIndex;
            }

            GenericMenu menu = new GenericMenu();

            // TODO: cleanup and reinstate analytics for these.

            //menu.AddItem(TextContent.SummaryView, UIState.CurrentMode.CurrentViewPane is UI.SummaryPane, () =>
            //{
            //    //MemoryProfilerAnalytics.StartEvent<MemoryProfilerAnalytics.OpenedViewEvent>();
            //    AddCurrentHistoryEvent();
            //    OpenSummary(null);
            //    //MemoryProfilerAnalytics.EndEvent(new MemoryProfilerAnalytics.OpenedViewEvent() { viewName = "Summary" });
            //});

            //if (UIState.CurrentMode is UI.UIState.SnapshotMode)
            //{
            //    menu.AddItem(TextContent.TreeMapView, UIState.CurrentMode.CurrentViewPane is UI.TreeMapPane, () =>
            //    {
            //        MemoryProfilerAnalytics.StartEvent<MemoryProfilerAnalytics.OpenedViewEvent>();
            //        AddCurrentHistoryEvent();
            //        OpenTreeMap(null);
            //        MemoryProfilerAnalytics.EndEvent(new MemoryProfilerAnalytics.OpenedViewEvent() { viewName = "TreeMap" });
            //    });
            //    menu.AddItem(TextContent.MemoryMapView, UIState.CurrentMode.CurrentViewPane is UI.MemoryMapPane, () =>
            //    {
            //        MemoryProfilerAnalytics.StartEvent<MemoryProfilerAnalytics.OpenedViewEvent>();
            //        AddCurrentHistoryEvent();
            //        OpenMemoryMap(null);
            //        MemoryProfilerAnalytics.EndEvent(new MemoryProfilerAnalytics.OpenedViewEvent() { viewName = "MemoryMap" });
            //    });
            //}
            //else
            //{
            //    menu.AddDisabledItem(TextContent.TreeMapView);
            //    menu.AddItem(TextContent.MemoryMapViewDiff, UIState.CurrentMode.CurrentViewPane is UI.MemoryMapDiffPane, () =>
            //    {
            //        MemoryProfilerAnalytics.StartEvent<MemoryProfilerAnalytics.OpenedViewEvent>();
            //        AddCurrentHistoryEvent();
            //        OpenMemoryMapDiff(null);
            //        MemoryProfilerAnalytics.EndEvent(new MemoryProfilerAnalytics.OpenedViewEvent() { viewName = "MemoryMapDiff" });
            //    });
            //}

            if (UIState.CurrentMode != null)
            {
                // skip "none"
                int numberOfTabelsToSkip = 1;

                for (int i = numberOfTabelsToSkip; i < UIState.CurrentMode.TableNames.Length; i++)
                {
                    int newTableIndex = i;
                    menu.AddItem(ConvertTableNameForUI(UIState.CurrentMode.TableNames[i]), newTableIndex == curTableIndex, () =>
                    {
                        ProgressBarDisplay.ShowBar("Opening Table...");
                        try
                        {
                            MemoryProfilerAnalytics.StartEvent<MemoryProfilerAnalytics.OpenedViewEvent>();
                            var tab = UIState.CurrentMode.GetTableByIndex(newTableIndex - numberOfTabelsToSkip);

                            ProgressBarDisplay.UpdateProgress(0.0f, "Updating Table...");
                            if (tab.Update())
                            {
                                UIState.CurrentMode.UpdateTableSelectionNames();
                            }

                            ProgressBarDisplay.UpdateProgress(0.75f, "Opening Table...");
                            OpenTable(new Database.TableReference(tab.GetName()), tab);

                            MemoryProfilerAnalytics.EndEvent(new MemoryProfilerAnalytics.OpenedViewEvent() { viewName = tab.GetName() });
                            ProgressBarDisplay.UpdateProgress(1.0f, "");
                        }
                        finally
                        {
                            ProgressBarDisplay.ClearBar();
                        }
                    });
                }
            }

            menu.DropDown(viewDropdownRect);
        }

        private GUIContent ConvertTableNameForUI(string tableName, bool fullPath = true)
        {
            if (!m_UIFriendlyViewOptionNames.ContainsKey(tableName))
            {
                m_TabelNameStringBuilder.Length = 0;
                // there are ONLY tables in the menu now so, no need to group them
                //m_TabelNameStringBuilder.Append(TextContent.TableMapViewRoot);
                m_TabelNameStringBuilder.Append(tableName);
                if (tableName.StartsWith(TextContent.DiffRawCategoryName))
                    m_TabelNameStringBuilder.Replace(TextContent.DiffRawCategoryName, TextContent.DiffRawDataTableMapViewRoot, 0 /*TextContent.TableMapViewRoot.Length*/, TextContent.DiffRawCategoryName.Length);
                else
                    m_TabelNameStringBuilder.Replace(TextContent.RawCategoryName, TextContent.RawDataTableMapViewRoot, 0 /*TextContent.TableMapViewRoot.Length*/, TextContent.RawCategoryName.Length);
                string name = m_TabelNameStringBuilder.ToString();
                m_UIFriendlyViewOptionNamesWithFullPath[tableName] = new GUIContent(name);

                int lastSlash = name.LastIndexOf('/');
                if (lastSlash >= 0 && lastSlash + 1 < name.Length)
                    name = name.Substring(lastSlash + 1);
                m_UIFriendlyViewOptionNames[tableName] = new GUIContent(name);

                Vector2 potentialViewDropdownSize = Styles.General.ToolbarPopup.CalcSize(m_UIFriendlyViewOptionNames[tableName]);
                potentialViewDropdownSize.x = Mathf.Clamp(potentialViewDropdownSize.x, 100, 300);
                if (m_ViewDropdownSize.x < potentialViewDropdownSize.x)
                {
                    m_ViewDropdownSize = potentialViewDropdownSize;
                }
            }

            return fullPath ? m_UIFriendlyViewOptionNamesWithFullPath[tableName] : m_UIFriendlyViewOptionNames[tableName];
        }

        public void OnUIStateChanged(UIState newState)
        {
            newState.ModeChanged += OnModeChanged;
            OnModeChanged(newState.CurrentMode, newState.CurrentViewMode);
        }

        void OnModeChanged(UIState.BaseMode newMode, UIState.ViewMode newViewMode)
        {
            if (newMode != null)
            {
                newMode.ViewPaneChanged -= OnViewPaneChanged;
                newMode.ViewPaneChanged += OnViewPaneChanged;
            }
            if (newViewMode == UIState.ViewMode.ShowNone || UIState.CurrentMode == null)
            {
                UIState.CurrentViewMode = UIState.ViewMode.ShowNone;
                TransitPane(null, true);
            }
            else if (UIState.CurrentMode.CurrentViewPane == null)
            {
                TransitPane(UIState.CurrentMode.GetDefaultView(m_UIStateHolder, this), true);
            }
            else
            {
                ViewPaneChanged(UIState.CurrentMode.CurrentViewPane);
            }
        }

        void OnViewPaneChanged(ViewPane newPane)
        {
            ViewPaneChanged(newPane);
            if (m_ViewSelectorMenuButton != null)
            {
                if (m_ViewSelectorMenuButtonLabel != null)
                {
                    // it's a dropdown button!
                    // TODO: make sure it is
                    if (newPane != null)
                        m_ViewSelectorMenuButtonLabel.text = newPane.ViewName;
                    else
                        m_ViewSelectorMenuButtonLabel.text = TextContent.NoneView.text;
                }
                else
                {
                    if (newPane != null)
                        m_ViewSelectorMenuButton.text = newPane.ViewName;
                    else
                        m_ViewSelectorMenuButton.text = TextContent.NoneView.text;
                }

                m_ViewSelectorMenuButton.SetEnabled(UIState.CurrentMode != null && UIState.CurrentMode.CurrentViewPane != null);
            }
        }

#if !REMOVE_VIEW_HISTORY
        public void StoreCurrentViewHistoryEventBeforeClosing()
        {
            if (currentViewPane != null)
            {
                UIState.AddHistoryEvent(currentViewPane.GetCloseHistoryEvent());
            }
        }

#endif

        void OpenTable(Database.TableReference tableRef, Database.Table table)
        {
            UIState.TransitModeToOwningTable(table);
            var pane = new UI.SpreadsheetPane(m_UIStateHolder, this);
            pane.OpenTable(tableRef, table);

            if (m_UIStateHolder.UIState.MainSelection.Valid)
                // Not a history event but try to find the active selection anyways
                pane.ApplyActiveSelectionAfterOpening(new SelectionEvent(m_UIStateHolder.UIState.MainSelection));

            TransitPane(pane, true);
        }

        void OpenTable(Database.TableReference tableRef, Database.Table table, Database.CellPosition pos)
        {
            UIState.TransitModeToOwningTable(table);
            var pane = new UI.SpreadsheetPane(m_UIStateHolder, this);
            pane.OpenTable(tableRef, table, pos);

            if (m_UIStateHolder.UIState.MainSelection.Valid)
                // Not a history event but try to find the active selection anyways
                pane.ApplyActiveSelectionAfterOpening(new SelectionEvent(m_UIStateHolder.UIState.MainSelection));

            TransitPane(pane, true);
        }

        public void OpenDefaultTable()
        {
            for (int i = 0; i < UIState.CurrentMode.TableNames.Length; i++)
            {
                if (UIState.CurrentMode.TableNames[i].Contains("All Objects"))
                {
                    var table = UIState.CurrentMode.GetTableByIndex(i - 1); // skip "none" table
                    table.Update();
                    OpenTable(new Database.TableReference(table.GetName()), table);
                }
            }
        }

        public void OpenTable(UI.HistoryEvent history, bool reopen = true,
#if !REMOVE_VIEW_HISTORY
            ViewStateChangedHistoryEvent viewStateToRestore = null,
#endif
            SelectionEvent mainSelection = null, SelectionEvent secondarySelection = null)
        {
            var pane = (reopen || !(currentViewPane is SpreadsheetPane)) ? new UI.SpreadsheetPane(m_UIStateHolder, this)
                // No need to reopen, just use the last one
                : currentViewPane as SpreadsheetPane;

#if !REMOVE_VIEW_HISTORY
            if (history != null)
                pane.OpenHistoryEvent(history as SpreadsheetPane.History, reopen, viewStateToRestore, smainSelection, selectionIsLatent: secondarySelection != null);
            else
#endif
            if (m_UIStateHolder.UIState.MainSelection.Valid)
                // Not a history event but try to find the active selection anyways
                pane.ApplyActiveSelectionAfterOpening(new SelectionEvent(m_UIStateHolder.UIState.MainSelection));

            TransitPane(pane, history == null);
        }

        public void OpenMemoryMap(UI.HistoryEvent history, bool reopen = true,
#if !REMOVE_VIEW_HISTORY
            ViewStateChangedHistoryEvent viewStateToRestore = null,
#endif
            SelectionEvent mainSelection = null, SelectionEvent secondarySelection = null)
        {
            var pane = (reopen || !(currentViewPane is MemoryMapPane)) ? new UI.MemoryMapPane(m_UIStateHolder, this, m_ToolbarExtension)
                // No need to reopen, just use the last one
                : currentViewPane as MemoryMapPane;

#if !REMOVE_VIEW_HISTORY
            if (history != null)
                pane.RestoreHistoryEvent(history, reopen, viewStateToRestore, mainSelection, selectionIsLatent: secondarySelection != null);
            else
#endif
            if (m_UIStateHolder.UIState.MainSelection.Valid)
                // Not a history event but try to find the active selection anyways
                pane.ApplyActiveSelectionAfterOpening(new SelectionEvent(m_UIStateHolder.UIState.MainSelection));

            TransitPane(pane, history == null);
        }

        public void OpenMemoryMapDiff(UI.HistoryEvent history, bool reopen = true,
#if !REMOVE_VIEW_HISTORY
            ViewStateChangedHistoryEvent viewStateToRestore = null,
#endif
            SelectionEvent mainSelection = null, SelectionEvent secondarySelection = null)
        {
            var pane = (reopen || !(currentViewPane is MemoryMapDiffPane)) ? new UI.MemoryMapDiffPane(m_UIStateHolder, this, m_ToolbarExtension)
                // No need to reopen, just use the last one
                : currentViewPane as MemoryMapDiffPane;

#if !REMOVE_VIEW_HISTORY
            if (history != null)
                pane.RestoreHistoryEvent(history, reopen, viewStateToRestore, mainSelection, selectionIsLatent: secondarySelection != null);
            else
#endif
            if (m_UIStateHolder.UIState.MainSelection.Valid)
                // Not a history event but try to find the active selection anyways
                pane.ApplyActiveSelectionAfterOpening(new SelectionEvent(m_UIStateHolder.UIState.MainSelection));

            TransitPane(pane, history == null);
        }

        public void OpenTreeMap(UI.HistoryEvent history, bool reopen = true,
#if !REMOVE_VIEW_HISTORY
            iewStateChangedHistoryEvent viewStateToRestore = null,
#endif
            SelectionEvent mainSelection = null, SelectionEvent secondarySelection = null)
        {
#if !REMOVE_VIEW_HISTORY
            TreeMapPane.History evt = history as TreeMapPane.History;
#else
            object evt = null;
#endif

#if !REMOVE_VIEW_HISTORY
            // TreeMap takes so long to re create, it always reopens, if possible
            if (currentViewPane is UI.TreeMapPane)
            {
                if (evt != null)
                {
                    (currentViewPane as UI.TreeMapPane).OpenHistoryEvent(evt, viewStateToRestore, mainSelection, selectionIsLatent: secondarySelection != null);
                    return;
                }
            }
#endif
            if (m_UIStateHolder.UIState.CurrentViewMode == UIState.ViewMode.ShowDiff || !(m_UIStateHolder.UIState.CurrentMode is UIState.SnapshotMode))
                return;
            var snapshotMode = m_UIStateHolder.UIState.CurrentMode as UIState.SnapshotMode;
            if (snapshotMode.CachedTreeMapPane  == null)
                snapshotMode.CachedTreeMapPane = new UI.TreeMapPane(m_UIStateHolder, this);
            var pane = snapshotMode.CachedTreeMapPane;
#if !REMOVE_VIEW_HISTORY
            if (evt != null)
                pane.OpenHistoryEvent(evt, viewStateToRestore, mainSelection, selectionIsLatent: secondarySelection != null);
            else
#endif
            if (m_UIStateHolder.UIState.MainSelection.Valid)
                // Not a history event but try to find the active selection anyways
                pane.ApplyActiveSelectionAfterOpening(new SelectionEvent(m_UIStateHolder.UIState.MainSelection));

            TransitPane(pane, evt == null);
        }

        public void OpenSummary(UI.HistoryEvent history, bool reopen,
#if !REMOVE_VIEW_HISTORY
            ViewStateChangedHistoryEvent viewStateToRestore = null,
#endif
            SelectionEvent mainSelection = null, SelectionEvent secondarySelection = null)
        {
#if !REMOVE_VIEW_HISTORY
            SummaryPane.History evt = history as SummaryPane.History;
#else
            object evt = null;
#endif

#if !REMOVE_VIEW_HISTORY
            if (currentViewPane is UI.SummaryPane)
            {
                if (evt != null)
                {
                    (currentViewPane as UI.SummaryPane).OpenHistoryEvent(evt, reopen, viewStateToRestore, mainSelection, selectionIsLatent: secondarySelection != null);
                    return;
                }
            }
#endif
            var pane = new UI.SummaryPane(m_UIStateHolder, this);
#if !REMOVE_VIEW_HISTORY
            if (evt != null)
                pane.OpenHistoryEvent(evt, reopen, viewStateToRestore, mainSelection, selectionIsLatent: secondarySelection != null);
            else
#endif
            if (m_UIStateHolder.UIState.MainSelection.Valid)
                // Not a history event but try to find the active selection anyways
                pane.ApplyActiveSelectionAfterOpening(new SelectionEvent(m_UIStateHolder.UIState.MainSelection));

            TransitPane(pane, evt == null);
        }

        void TransitPane(UI.ViewPane newPane, bool recordHistory)
        {
            UIState.CurrentMode.TransitPane(newPane, recordHistory);
        }

        void DrawTableSelection()
        {
            using (new EditorGUI.DisabledGroupScope(UIState.CurrentMode == null || UIState.CurrentMode.CurrentViewPane == null))
            {
                var dropdownContent = TextContent.NoneView;
                if (UIState.CurrentMode != null && UIState.CurrentMode.CurrentViewPane != null)
                {
                    var currentViewPane = UIState.CurrentMode.CurrentViewPane;
                    if (currentViewPane is TreeMapPane)
                    {
                        dropdownContent = TextContent.TreeMapView;
                    }
                    else if (currentViewPane is MemoryMapPane)
                    {
                        dropdownContent = TextContent.MemoryMapView;
                    }
                    else if (currentViewPane is MemoryMapDiffPane)
                    {
                        dropdownContent = TextContent.MemoryMapViewDiff;
                    }
                    else if (currentViewPane is SpreadsheetPane)
                    {
                        dropdownContent = ConvertTableNameForUI((currentViewPane as SpreadsheetPane).TableDisplayName, false);
                    }
                }

                var minSize = EditorStyles.toolbarDropDown.CalcSize(dropdownContent);
                minSize.y = Mathf.Min(minSize.y, EditorGUIUtility.singleLineHeight);
                minSize = new Vector2(Mathf.Max(minSize.x, m_ViewDropdownSize.x), Mathf.Max(minSize.y, m_ViewDropdownSize.y));
                Rect viewDropdownRect = GUILayoutUtility.GetRect(minSize.x, minSize.y, Styles.General.ToolbarPopup);
                viewDropdownRect.x--;
#if UNITY_2019_3_OR_NEWER // Hotfixing theming issues...
                viewDropdownRect.y--;
#endif
                if (m_ViewSelectorMenu != null && Event.current.type == EventType.Repaint)
                {
                    m_ViewSelectorMenu.style.width = minSize.x;
                    m_ViewSelectorMenu.style.height = minSize.y;
                }
                if (EditorGUI.DropdownButton(viewDropdownRect, dropdownContent, FocusType.Passive, Styles.General.ToolbarPopup))
                {
                    OpenViewSelectionDropdown(viewDropdownRect);
                }
            }
        }
    }
}

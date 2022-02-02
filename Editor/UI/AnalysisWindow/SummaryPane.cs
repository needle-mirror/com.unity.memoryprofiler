using System.Collections;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEditor;
using Unity.MemoryProfiler.Editor.Format;
using System;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class SummaryPane : ViewPane
    {
        internal class ViewStateHistory : ViewStateChangedHistoryEvent
        {
            public ViewStateHistory(SummaryPane pane)
            {
            }

            protected override bool IsEqual(HistoryEvent evt)
            {
                return evt != null && evt is ViewStateHistory;
            }
        }

        internal class History : ViewOpenHistoryEvent
        {
            public override ViewStateChangedHistoryEvent ViewStateChangeRestorePoint => throw new NotImplementedException();

            protected override bool IsEqual(HistoryEvent evt)
            {
                return evt != null && evt is History;
            }
        }

        public override VisualElement[] VisualElements
        {
            get
            {
                return m_VisualElements;
            }
        }

        public override string ViewName { get { return TextContent.SummaryView.text; } }

        public override bool ViewStateFilteringChangedSinceLastSelectionOrViewClose => m_ViewStateFilteringChangedSinceLastSelectionOrViewClose;

        bool m_ViewStateFilteringChangedSinceLastSelectionOrViewClose = false;

        public SummaryPane(IUIStateHolder s, IViewPaneEventListener l) : base(s, l)
        {
            VisualTreeAsset summaryViewTree;
            summaryViewTree = AssetDatabase.LoadAssetAtPath(ResourcePaths.SummaryPaneUxmlPath, typeof(VisualTreeAsset)) as VisualTreeAsset;

            m_VisualElements = new[] { summaryViewTree.Clone() };
        }

        public void OpenHistoryEvent(History e, bool reopen, ViewStateChangedHistoryEvent viewStateToRestore = null, SelectionEvent selectionEvent = null, bool selectionIsLatent = false)
        {
        }

        public override void SetSelectionFromHistoryEvent(SelectionEvent selectionEvent)
        {
#if ENABLE_MEMORY_PROFILER_DEBUG
            throw new NotImplementedException();
#endif
        }

        public override ViewOpenHistoryEvent GetOpenHistoryEvent()
        {
            return new History();
        }

        public override void OnClose()
        {
            return;
        }

        public override void OnGUI(Rect r)
        {
            throw new System.InvalidOperationException();
        }

        public override ViewStateChangedHistoryEvent GetViewStateFilteringChangesSinceLastSelectionOrViewClose()
        {
            m_ViewStateFilteringChangedSinceLastSelectionOrViewClose = false;

            var stateEvent = new ViewStateHistory(this);
            stateEvent.ChangeType = ViewStateChangedHistoryEvent.StateChangeType.FiltersChanged;
            return stateEvent;
        }

        public override void OnSelectionChanged(MemorySampleSelection selection)
        {
            throw new NotImplementedException();
        }
    }
}

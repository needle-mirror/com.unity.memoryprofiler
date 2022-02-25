using UnityEngine;
using UnityEngine.UIElements;
using System;
using Unity.Profiling;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal interface IViewPaneEventListener
    {
        void OnOpenLink(Database.LinkRequest link);
        void OnOpenLink(Database.LinkRequest link, UIState.SnapshotMode mode);
        void OnOpenMemoryMap();
        void OnOpenTreeMap();
        void OnRepaint();
    }
    internal abstract class ViewPane : UI.IViewEventListener
    {
        public abstract string ViewName { get; }

        public IUIStateHolder m_UIStateHolder;
        public UIState m_UIState => m_UIStateHolder.UIState;
        public IViewPaneEventListener m_EventListener;
        public ViewPane(IUIStateHolder s, IViewPaneEventListener l)
        {
            m_UIStateHolder = s;
            m_EventListener = l;
        }

        protected VisualElement[] m_VisualElements;
        protected Action<Rect>[] m_VisualElementsOnGUICalls;

        public virtual VisualElement[] VisualElements
        {
            get
            {
                if (m_VisualElements == null)
                {
                    m_VisualElements = new VisualElement[]
                    {
                        new IMGUIContainer(() => OnGUI(0))
                        {
                            style =
                            {
                                flexGrow = 1,
                            }
                        }
                    };
                    m_VisualElementsOnGUICalls = new Action<Rect>[]
                    {
                        OnGUI,
                    };
                }
                return m_VisualElements;
            }
        }

        public abstract UI.ViewOpenHistoryEvent GetOpenHistoryEvent();
        public UI.ViewStateChangedHistoryEvent GetCloseHistoryEvent()
        {
            var closedEvent = GetViewStateFilteringChangesSinceLastSelectionOrViewClose();
            closedEvent.ChangeType = ViewStateChangedHistoryEvent.StateChangeType.ViewClosed;
            return closedEvent;
        }

        // store dirty state after selection or closing state
        public abstract bool ViewStateFilteringChangedSinceLastSelectionOrViewClose { get; }
        public abstract UI.ViewStateChangedHistoryEvent GetViewStateFilteringChangesSinceLastSelectionOrViewClose();
        public abstract void SetSelectionFromHistoryEvent(SelectionEvent selectionEvent);
        // Override if the view pane can't just apply an selection directly after opening
        public virtual void ApplyActiveSelectionAfterOpening(SelectionEvent selectionEvent)
        {
            SetSelectionFromHistoryEvent(selectionEvent);
        }

        static ProfilerMarker s_OnGui = new ProfilerMarker("ViewPane.OnGUI");

        protected virtual void OnGUI(int elementIndex)
        {
            using (s_OnGui.Auto())
            {
                try
                {
                    var rect = m_VisualElements[elementIndex].contentRect;
                    if (float.IsNaN(rect.width) || float.IsNaN(rect.height))
                    {
                        rect = new Rect(0, 0, 1, 1);
                    }
                    m_VisualElementsOnGUICalls[elementIndex](rect);
                }
                catch (Exception)
                {
                    throw;
                }
            }
        }

        public virtual void OnGUI(Rect r) {}
        void UI.IViewEventListener.OnRepaint()
        {
            m_EventListener.OnRepaint();
        }

        public abstract void OnClose();

        public abstract void OnSelectionChanged(MemorySampleSelection selection);
    }
}

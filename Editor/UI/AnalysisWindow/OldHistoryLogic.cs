using System.Collections;
using System;
using Unity.EditorCoroutines.Editor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class OldHistoryLogic
    {
        private UIState UIState { get { return m_UIStateHolder.UIState; } }
        OldViewLogic m_OldViewSelectionLogic;
        IUIStateHolder m_UIStateHolder;
        Button m_BackwardsInHistoryButton;
        Button m_ForwardsInHistoryButton;
        public OldHistoryLogic(IUIStateHolder uiStateHolder, OldViewLogic oldViewSelectionLogic, VisualElement root)
        {
            m_BackwardsInHistoryButton = root.Q<ToolbarButton>("history-button__back");
            m_ForwardsInHistoryButton = root.Q<ToolbarButton>("history-button__forwards");
            m_OldViewSelectionLogic = oldViewSelectionLogic;
            m_UIStateHolder = uiStateHolder;

            m_BackwardsInHistoryButton.SetEnabled(false);
            m_ForwardsInHistoryButton.SetEnabled(false);
            m_BackwardsInHistoryButton.clickable.clicked += StepBackwardsInHistory;
            m_ForwardsInHistoryButton.clickable.clicked += StepForwardsInHistory;
        }

        public void StepBackwardsInHistory()
        {
            var history = UIState.history;
            if (history.hasPast)
            {
                if (!history.hasPresentEvent)
                {
                    if (UIState.CurrentMode != null)
                    {
                        history.SetPresentEvent(UIState.CurrentMode.GetCurrentHistoryEvent());
                    }
                }
                EditorCoroutineUtility.StartCoroutine(DelayedHistoryEvent(history.Backward()), m_UIStateHolder.Window);
                m_UIStateHolder.Repaint();
            }
        }

        public void StepForwardsInHistory()
        {
            var evt = UIState.history.Forward();
            if (evt != null)
            {
                EditorCoroutineUtility.StartCoroutine(DelayedHistoryEvent(evt), m_UIStateHolder.Window);
                m_UIStateHolder.Repaint();
            }
        }

        IEnumerator DelayedHistoryEvent(HistoryEvent eventToOpen)
        {
            yield return null;
            try
            {
                if (eventToOpen != null)
                {
                    OpenHistoryEvent(eventToOpen);
                    eventToOpen = null;
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        internal void OnUIStateChanged(UIState obj)
        {
            UIState.history.historyChanged += HistoryChanged;
        }

        void HistoryChanged()
        {
            m_BackwardsInHistoryButton.SetEnabled(UIState.history.hasPast);
            m_ForwardsInHistoryButton.SetEnabled(UIState.history.hasFuture);
        }

        void OpenHistoryEvent(UI.HistoryEvent evt)
        {
            if (evt == null) return;

            UIState.TransitMode(evt.Mode);

            if (evt is SpreadsheetPane.History)
            {
                m_OldViewSelectionLogic.OpenTable(evt);
            }
            else if (evt is MemoryMapPane.History)
            {
                m_OldViewSelectionLogic.OpenMemoryMap(evt);
            }
            else if (evt is MemoryMapDiffPane.History)
            {
                m_OldViewSelectionLogic.OpenMemoryMapDiff(evt);
            }
            else if (evt is TreeMapPane.History)
            {
                m_OldViewSelectionLogic.OpenTreeMap(evt);
            }
        }
    }
}

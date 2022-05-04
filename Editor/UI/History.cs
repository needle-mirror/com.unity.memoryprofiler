#define REMOVE_VIEW_HISTORY
using System;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal abstract class HistoryEvent : IEquatable<HistoryEvent>
    {
        protected const string seperator = "::";

        protected abstract bool IsEqual(HistoryEvent evt);

        public bool Equals(HistoryEvent other)
        {
            return IsEqual(other);
        }
    }

#if !REMOVE_VIEW_HISTORY
    // These events are only logged for saving the view state when a selection is made or before a new view is opened
    // They are used for restoring the view state when going backwards, they will no be shown in a history log (hidden) and:
    // History navigation will read them to restore the view state when going backwards but otherwise skip them
    internal abstract class ViewStateChangedHistoryEvent : HistoryEvent
    {
        public enum StateChangeType
        {
            ViewClosed,
            FiltersChanged
        }
        public StateChangeType ChangeType { get; set; }
    }
    // Stores everything needed to reopen the view to what it was after it was opened the first time,
    // before manually applying filters or manually selection items.
    internal abstract class ViewOpenHistoryEvent : HistoryEvent
    {
        public UIState.BaseMode Mode;
        public abstract ViewStateChangedHistoryEvent ViewStateChangeRestorePoint { get; }
    }
#endif

    /// <summary>
    /// Keeps a time-line of events that may be revisited on demand.
    /// Useful for navigation and undo mechanisms
    /// </summary>
    internal class History : IDisposable
    {
        public System.Collections.Generic.List<HistoryEvent> events = new System.Collections.Generic.List<HistoryEvent>();
        public int backCount = 0;
        int m_FirstSelectionEventIndex = -1;
        public bool hasPresentEvent = false;

        public event System.Action historyChanged = delegate {};
        public event System.Action lastSelectionEventCleared = delegate {};

        public void Clear()
        {
            var fromerCurrentEventIndex = GetCurrent();
            backCount = 0;
            hasPresentEvent = false;
            events.Clear();
            historyChanged();

            if (m_FirstSelectionEventIndex >= fromerCurrentEventIndex)
                lastSelectionEventCleared();
            m_FirstSelectionEventIndex = -1;
        }

        protected int eventCount
        {
            get
            {
                if (hasPresentEvent) return events.Count;
                return events.Count;
            }
        }
        public bool isPresent
        {
            get
            {
                return backCount == 0;
            }
        }
        public bool hasPast
        {
            get
            {
                return backCount + 1 < eventCount;
            }
        }
        public bool hasFuture
        {
            get
            {
                return backCount > 0;
            }
        }

#if !REMOVE_VIEW_HISTORY
        public bool presentEventIsFollowedByViewChange
        {
            get
            {
                return hasFuture && events[GetCurrent() + 1] is ViewStateChangedHistoryEvent stateChange
                    && stateChange.ChangeType == ViewStateChangedHistoryEvent.StateChangeType.FiltersChanged;
            }
        }
#endif

        public void AddEvent(HistoryEvent e)
        {
            if (hasFuture)
            {
                //remove future
                var i = events.Count - backCount;
                if (i >= m_FirstSelectionEventIndex)
                    m_FirstSelectionEventIndex = -1;
                events.RemoveRange(i, backCount);
            }
            backCount = 0;
            if (events.Count > 0)
            {
                var last = events[events.Count - 1];
                if (!last.Equals(e))
                {
                    events.Add(e);
                }
            }
            else
            {
                events.Add(e);
            }
            hasPresentEvent = false;
            //UnityEngine.Debug.Log("History add: " + e.ToString());
            //PrintHistory();
            historyChanged();
        }

        internal SelectionEvent GetLastSelectionEvent(MemorySampleSelectionRank maxRank)
        {
            for (int i = events.Count - (1 + backCount); i >= 0; i--)
            {
                if (events[i] is SelectionEvent)
                {
                    var selectionEvent = events[i] as SelectionEvent;
                    if (selectionEvent.Selection.Rank <= maxRank)
                        return selectionEvent;
                }
            }
            return null;
        }

        internal SelectionEvent GetNextSelectionEvent(MemorySampleSelectionRank maxRank)
        {
            for (int i = events.Count - (1 + backCount); i < events.Count; i++)
            {
                if (events[i] is SelectionEvent)
                {
                    var selectionEvent = events[i] as SelectionEvent;
                    if (selectionEvent.Selection.Rank <= maxRank)
                        return selectionEvent;
                }
            }
            return null;
        }

#if !REMOVE_VIEW_HISTORY
        internal ViewStateChangedHistoryEvent GetLastViewStateChangeEvent()
        {
            for (int i = events.Count - (1 + backCount); i >= 0; i--)
            {
                if (events[i] is ViewOpenHistoryEvent)
                    return (events[i] as ViewOpenHistoryEvent).ViewStateChangeRestorePoint;
                else if (events[i] is ViewStateChangedHistoryEvent)
                    return events[i] as ViewStateChangedHistoryEvent;
            }
            return null;
        }

        internal ViewOpenHistoryEvent GetLastOpenEvent()
        {
            for (int i = events.Count - (1 + backCount); i >= 0; i--)
            {
                if (events[i] is ViewOpenHistoryEvent)
                    return events[i] as ViewOpenHistoryEvent;
            }
            return null;
        }

        public void SetPresentEvent(HistoryEvent ePresent)
        {
            if (ePresent == null) return;
            events.Add(ePresent);
            hasPresentEvent = true;
            historyChanged();
        }

        public HistoryEvent Backward()
        {
            if (hasPast)
            {
                ++backCount;
                var i = GetCurrent();
                historyChanged();
                if (i + 1 == m_FirstSelectionEventIndex)
                    lastSelectionEventCleared();
                return events[i];
            }

            historyChanged();
            return null;
        }

        public HistoryEvent Forward()
        {
            if (hasFuture)
            {
                --backCount;
                var i = GetCurrent();
                historyChanged();
                return events[i];
            }

            historyChanged();
            return null;
        }

#endif
        protected int GetCurrent()
        {
            return events.Count - 1 - backCount;
        }

#if !REMOVE_VIEW_HISTORY
        void PrintHistory()
        {
            string strOut = "";
            foreach (var e in events)
            {
                strOut += e.ToString() + "\n";
            }
            UnityEngine.Debug.Log(strOut);
        }

#endif

        public void SetCurrentSelectionEvent(SelectionEvent historySelectionEvent)
        {
            AddEvent(historySelectionEvent);
            if (m_FirstSelectionEventIndex == -1)
                m_FirstSelectionEventIndex = GetCurrent();
        }

        public void Dispose()
        {
            historyChanged = null;
            lastSelectionEventCleared = null;
        }
    }
}

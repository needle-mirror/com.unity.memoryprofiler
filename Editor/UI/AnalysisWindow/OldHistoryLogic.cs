#define REMOVE_VIEW_HISTORY
using System.Collections;
using System;
using Unity.EditorCoroutines.Editor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor.UI
{
    // This class is no longer needed, unless we decide to revive View History. For the moment (March 2022), it is left as mostly commented out
    // If you see this in 2023, please feel free to delete it
    internal class OldHistoryLogic
    {
#if !REMOVE_VIEW_HISTORY
        private UIState UIState { get { return m_UIStateHolder.UIState; } }
        OldViewLogic m_OldViewSelectionLogic;
        IUIStateHolder m_UIStateHolder;
#endif
        Button m_BackwardsInHistoryButton;
        Button m_ForwardsInHistoryButton;
        public OldHistoryLogic(IUIStateHolder uiStateHolder, OldViewLogic oldViewSelectionLogic, VisualElement root)
        {
            m_BackwardsInHistoryButton = root.Q<ToolbarButton>("history-button__back");
            m_ForwardsInHistoryButton = root.Q<ToolbarButton>("history-button__forwards");
#if !REMOVE_VIEW_HISTORY
            m_OldViewSelectionLogic = oldViewSelectionLogic;
            m_UIStateHolder = uiStateHolder;
#endif
            UIElementsHelper.SetVisibility(m_BackwardsInHistoryButton, false);
            m_BackwardsInHistoryButton.SetEnabled(false);
            UIElementsHelper.SetVisibility(m_ForwardsInHistoryButton, false);
            m_ForwardsInHistoryButton.SetEnabled(false);

#if !REMOVE_VIEW_HISTORY
            m_BackwardsInHistoryButton.clickable.clicked += StepBackwardsInHistory;
            m_ForwardsInHistoryButton.clickable.clicked += StepForwardsInHistory;
#endif
        }

#if !REMOVE_VIEW_HISTORY
        public void StepBackwardsInHistory()
        {
            var history = UIState.history;
            if (history.hasPast)
            {
                // the current view state might be dirty and therefore might need to be saved
                if ((UIState.CurrentMode.CurrentViewPane.ViewStateFilteringChangedSinceLastSelectionOrViewClose))
                {
                    history.AddEvent(UIState.CurrentMode.CurrentViewPane.GetViewStateFilteringChangesSinceLastSelectionOrViewClose());
                }
                EditorCoroutineUtility.StartCoroutine(DelayedHistoryEvent(history.Backward(), false), m_UIStateHolder.Window);
                m_UIStateHolder.Repaint();
            }
        }

        public void StepForwardsInHistory()
        {
            var evt = UIState.history.Forward();
            if (evt != null)
            {
                EditorCoroutineUtility.StartCoroutine(DelayedHistoryEvent(evt, true), m_UIStateHolder.Window);
                m_UIStateHolder.Repaint();
            }
        }

        IEnumerator DelayedHistoryEvent(HistoryEvent eventToOpen, bool goingForward)
        {
            yield return null;
            try
            {
                if (eventToOpen != null)
                {
                    ViewStateChangedHistoryEvent viewStateEvent = null;
                    ViewOpenHistoryEvent openEvent = null;
                    bool reopen = false;
                    SelectionEvent mainSelectionEvent = null;
                    SelectionEvent secondarySelectionEvent = null;

                    if (eventToOpen is SelectionEvent)
                    {
                        var selectionEvent = eventToOpen as SelectionEvent;
                        // if we get a secondary selection make sure that there is no main selection
                        // if there is no main selection we need to roll back
                        // to the last event there was a main selection and reinstate it
                        if (selectionEvent.Selection.Rank == MemorySampleSelectionRank.SecondarySelection)
                        {
                            for (int i = UIState.history.events.Count - 1; i > 0; i--)
                            {
                                bool found = false;
                                if (UIState.history.events[i] == selectionEvent)
                                {
                                    var ii = i;
                                    while (ii != 0)
                                    {
                                        if (UIState.history.events[ii] is SelectionEvent && ((SelectionEvent)UIState.history.events[ii]).Selection.Rank == MemorySampleSelectionRank.MainSelection)
                                        {
                                            // lets not mess around and assign it again if we already have the correct event in place for the main selection in the UIState
                                            if (((SelectionEvent)UIState.history.events[ii]).Selection.Equals(UIState.MainSelection))
                                            {
                                                found = true;
                                                break;
                                            }
                                            found = true;
                                            mainSelectionEvent = UIState.history.events[ii] as SelectionEvent;
                                            secondarySelectionEvent = selectionEvent;
                                            break;
                                        }
                                        ii--;
                                    }
                                    if (found)
                                        break;
                                }
                            }
                        }
                        if (!goingForward && selectionEvent.Selection.Rank == MemorySampleSelectionRank.MainSelection
                            && UIState.history.presentEventIsFollowedByViewChange)
                        {
                            // we've stepped back from a view state change, the selection was made in a different view state
                            // we need to restore the view state or the selected item might be filtered out
                            openEvent = UIState.history.GetLastOpenEvent();
                            viewStateEvent = UIState.history.GetLastViewStateChangeEvent();
                            reopen = false;
                            mainSelectionEvent = selectionEvent;
                        }
                        else
                        {
                            // just restore the selection, everything else is already handled
                            if (mainSelectionEvent != null)
                            {
                                OpenHistoryEvent(mainSelectionEvent, false);
                                OpenHistoryEvent(secondarySelectionEvent, false);
                            }
                            else
                            {
                                OpenHistoryEvent(selectionEvent, false);
                            }
                            yield break;
                        }
                    }
                    else
                    {
                        if (eventToOpen is ViewOpenHistoryEvent)
                        {
                            openEvent = eventToOpen as ViewOpenHistoryEvent;
                            // going forward, we'll never land on an Open event
                            // going backwards, we'll have stepped over a Close event, which would have triggered a reopen
                            // so no need to reopen
                            reopen = false;
                        }
                        if (eventToOpen is ViewStateChangedHistoryEvent)
                        {
                            viewStateEvent = eventToOpen as ViewStateChangedHistoryEvent;
                            switch (viewStateEvent.ChangeType)
                            {
                                case ViewStateChangedHistoryEvent.StateChangeType.ViewClosed:

                                    if (goingForward)
                                    {
                                        // going forward, a close event is immediately followed by an open event
                                        // get that open event to reopen the view
                                        openEvent = UIState.history.Forward() as ViewOpenHistoryEvent;
                                        //// and get the view state to restore it
                                        //viewStateEvent = openEvent.ViewStateChangeRestorePoint;
                                        // not needed as it will be restored with the open event
                                        viewStateEvent = null;
                                    }
                                    else
                                    {
                                        // Find out what view was getting closed by grabbing the last Open Event
                                        openEvent = UIState.history.GetLastOpenEvent();
                                        // we can't stay on a Close event
                                        // otherwise, if we'd add new history events, things are gonna get in disarray
                                        // with e.g. a Close Event followed by a non-Open Event
                                        var oneFutherBack = UIState.history.Backward();
                                        if (openEvent == oneFutherBack)
                                        {
                                            // the view was opened and closed, just use the UI state as it was on close, already stored in viewStateEvent
                                        }
                                        else if (oneFutherBack is ViewStateChangedHistoryEvent)
                                        {
                                            // the view state was dirty before closing the view, this should be the same as the closing events view state
                                            viewStateEvent = oneFutherBack as ViewStateChangedHistoryEvent;
                                        }
                                        else if (oneFutherBack is SelectionEvent)
                                        {
                                            // a selection was made before closing the view
                                            // the view wasn't significantly changed after the selection was made
                                            // The secondary and Main Selection state retrieval below will take care that it is properly restored.
                                        }
                                    }
                                    reopen = true;
                                    break;

                                case ViewStateChangedHistoryEvent.StateChangeType.FiltersChanged:

                                    // Tree Map does Table Filtering by selecting Groups, so it needs a bit of extra handling
                                    // This is to avoid falling between the type selection and the type filter step
                                    if (viewStateEvent is TreeMapPane.ViewStateHistory)
                                    {
                                        if (goingForward)
                                        {
                                            var nextMainSelection = UIState.history.GetNextSelectionEvent(MemorySampleSelectionRank.MainSelection);
                                            if (nextMainSelection != null /*&& !nextMainSelection.Selection.Equals(UIState.MainSelection)*/ &&
                                                (nextMainSelection.Selection.Type == MemorySampleSelectionType.ManagedType ||
                                                 nextMainSelection.Selection.Type == MemorySampleSelectionType.NativeType))
                                            {
                                                // going forward and stepped onto the view state change preceeding a Group selection in Tree Map
                                                // do the Group selection in the same step
                                                var nextEvent = UIState.history.Forward();
                                                if (nextEvent != null && nextEvent.Equals(nextMainSelection))
                                                {
                                                    // Main selection will now be properly restored below
                                                }
                                                else
                                                {
                                                    Debug.LogError("Stepped over Tree Map view change but did not find the Type selection after it as expected");
                                                    UIState.history.Backward();
                                                }
                                            }
                                        }
                                        else
                                        {
                                            var lastMainSelection = UIState.history.GetLastSelectionEvent(MemorySampleSelectionRank.MainSelection);
                                            // Either there is no last selection, or it is not the same as the current one, which is the selection of a Type (i.e. a valid one)
                                            if ((lastMainSelection == null || !lastMainSelection.Selection.Equals(UIState.MainSelection)) &&
                                                (UIState.MainSelection.Type == MemorySampleSelectionType.ManagedType ||
                                                 UIState.MainSelection.Type == MemorySampleSelectionType.NativeType))
                                            {
                                                // The last main selection that has just been reverted was of a Type, while we're in the Tree Map
                                                // This view state change event is therefore the Type Filter event, skip it
                                                UIState.history.Backward();
                                                // We don't need to do anything with above's return value, because:
                                                // If it is a selection, it will be reselected by the selection restoring code below
                                                // If it is an Open event, this will get the View State restore point
                                                // And If it is a Change event, we will get the state
                                                viewStateEvent = UIState.history.GetLastViewStateChangeEvent();
                                            }
                                        }
                                    }

                                    // forward or back, this is just a regular view state change that needs to be applied to the last opened view
                                    openEvent = UIState.history.GetLastOpenEvent();
                                    reopen = false;
                                    break;

                                default:
                                    throw new NotImplementedException();
                            }
                        }
                        // Get Secondary or Main selection
                        var firstSelection = UIState.history.GetLastSelectionEvent(MemorySampleSelectionRank.SecondarySelection);
                        if (firstSelection != null && firstSelection.Selection.Rank == MemorySampleSelectionRank.SecondarySelection)
                        {
                            secondarySelectionEvent = firstSelection;
                            // Get the main selection
                            mainSelectionEvent = UIState.history.GetLastSelectionEvent(MemorySampleSelectionRank.MainSelection);
                        }
                        else
                        {
                            mainSelectionEvent = firstSelection;
                        }
                    }

                    if (openEvent != null)
                        OpenHistoryEvent(openEvent, reopen, viewStateEvent, mainSelectionEvent, secondarySelectionEvent);
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

#endif

        internal void OnUIStateChanged(UIState newState)
        {
#if !REMOVE_VIEW_HISTORY
            newState.ModeChanged += OnModeChanged;
            newState.history.historyChanged += HistoryChanged;
            OnModeChanged(newState.CurrentMode, newState.CurrentViewMode);
#endif
        }

#if !REMOVE_VIEW_HISTORY
        void OnModeChanged(UIState.BaseMode newMode, UIState.ViewMode newViewMode)
        {
            var oldSelection = UIState.history.GetLastSelectionEvent(MemorySampleSelectionRank.SecondarySelection);
            //this clear is causing problems with the initial state getting deleted.
            //UIState.history.Clear();
            // TODO: Fix retained selection after mode was changed
            // ApplyActiveSelectionAfterOpening will restore the selection as best as possible but currently does not re-trigger a selection Changed event
            // also, when having no selection it won't be cleared
            //if (oldSelection != null)
            //    newMode?.CurrentViewPane?.ApplyActiveSelectionAfterOpening(oldSelection);
        }

        void HistoryChanged()
        {
            m_BackwardsInHistoryButton.SetEnabled(UIState.history.hasPast);
            m_ForwardsInHistoryButton.SetEnabled(UIState.history.hasFuture);
        }

        void OpenHistoryEvent(UI.HistoryEvent evt, bool reopen = true, ViewStateChangedHistoryEvent viewStateToRestore = null, SelectionEvent mainSelection = null, SelectionEvent secondarySelection = null)
        {
            if (evt == null) return;

            if (evt is SelectionEvent)
            {
                var selEvt = evt as SelectionEvent;
                UIState.RestoreSelectionFromHistoryEvent(selEvt);
                return;
            }
            var openMode = evt as ViewOpenHistoryEvent;
            if (openMode != null)
            {
                UIState.TransitMode(openMode.Mode);

                if (evt is SpreadsheetPane.History)
                {
                    m_OldViewSelectionLogic.OpenTable(evt, reopen, viewStateToRestore, mainSelection, secondarySelection);
                }
                else if (evt is MemoryMapPane.History)
                {
                    m_OldViewSelectionLogic.OpenMemoryMap(evt, reopen, viewStateToRestore, mainSelection, secondarySelection);
                }
                else if (evt is MemoryMapDiffPane.History)
                {
                    m_OldViewSelectionLogic.OpenMemoryMapDiff(evt, reopen, viewStateToRestore, mainSelection, secondarySelection);
                }
                else if (evt is TreeMapPane.History)
                {
                    m_OldViewSelectionLogic.OpenTreeMap(evt, reopen, viewStateToRestore, mainSelection, secondarySelection);
                }

                // re-fire selection events as needed
                if (mainSelection == null)
                    mainSelection = new SelectionEvent(MemorySampleSelection.InvalidMainSelection);
                if (!mainSelection.Selection.Equals(UIState.MainSelection))
                {
                    // fire the selection event, no need to update the View Pane as it just got the memo above
                    UIState.RestoreSelectionFromHistoryEvent(mainSelection, updateCurrentViewPane: false);
                }

                if (secondarySelection == null)
                    secondarySelection = new SelectionEvent(MemorySampleSelection.InvalidSecondarySelection);
                if (!secondarySelection.Selection.Equals(UIState.SecondarySelection))
                {
                    // fire the selection event, no need to update the View Pane as it just got the memo above
                    UIState.RestoreSelectionFromHistoryEvent(secondarySelection, updateCurrentViewPane: false);
                }
            }
        }

#endif
    }
}

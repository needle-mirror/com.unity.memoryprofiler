using UnityEngine;
using System;
using Unity.MemoryProfiler.Editor.UI;
using UnityEngine.UIElements;
using Unity.MemoryProfiler.Editor.Format;

namespace Unity.MemoryProfiler.Editor
{
    internal class OpenSnapshotsManager
    {
        OpenSnapshotsWindow m_OpenSnapshotsPane;

        public enum OpenSnapshotSlot
        {
            First,
            Second,
        }

        [NonSerialized]
        SnapshotFileData First;
        [NonSerialized]
        SnapshotFileData Second;

        UIState m_UIState;

        public bool SnapshotALoaded { get { return First != null; } }
        public bool SnapshotBLoaded { get { return Second != null; } }
        public bool SnapshotAOpen { get { return m_UIState.FirstMode == m_UIState.CurrentMode; } }
        public bool SnapshotBOpen { get { return m_UIState.SecondMode == m_UIState.CurrentMode; } }
        public bool DiffOpen { get { return m_UIState.diffMode == m_UIState.CurrentMode; } }

        public event Action SwappedSnapshots = delegate {};

        private UI.ViewPane currentViewPane
        {
            get
            {
                if (m_UIState.CurrentMode == null) return null;
                return m_UIState.CurrentMode.CurrentViewPane;
            }
        }

        public void RegisterUIState(UIState uiState)
        {
            m_UIState = uiState;
            uiState.ModeChanged += OnModeChanged;
            OnModeChanged(uiState.CurrentMode, uiState.CurrentViewMode);
        }

        public OpenSnapshotsWindow InitializeOpenSnapshotsWindow(float initialWidth, VisualElement root)
        {
            m_OpenSnapshotsPane = new OpenSnapshotsWindow(initialWidth, root);

            m_OpenSnapshotsPane.SwapOpenSnapshots += () => SwapOpenSnapshots();
            m_OpenSnapshotsPane.ShowDiffOfOpenSnapshots += ShowDiffOfOpenSnapshots;
            m_OpenSnapshotsPane.ShowFirstOpenSnapshot += ShowFirstOpenSnapshot;
            m_OpenSnapshotsPane.ShowSecondOpenSnapshot += ShowSecondOpenSnapshot;
            m_OpenSnapshotsPane.CompareModeChanged += OnCompareModeChanged;
            return m_OpenSnapshotsPane;
        }

        void SetSnapshotAsOpenInUI(bool first, SnapshotFileData snapshot)
        {
            if (first)
                First = snapshot;
            else
                Second = snapshot;
            m_OpenSnapshotsPane.SetSnapshotUIData(first, snapshot.GuiData, true);
        }

        public void OpenSnapshot(SnapshotFileData snapshot)
        {
            if (snapshot == First)
            {
                // close First
                CloseCapture(snapshot);
                return;
            }
            if (snapshot == Second)
            {
                if (!m_OpenSnapshotsPane.CompareMode)
                {
                    SwapOpenSnapshots(false);
                }
                else
                {
                    // close Second
                    CloseCapture(snapshot);
                }
                return;
            }
            if (First != null)
            {
                if (m_OpenSnapshotsPane.CompareMode)
                {
                    if (Second != null)
                    {
                        // close Second
                        CloseCapture(Second);
                    }
                    // open as Second
                    SetSnapshotAsOpenInUI(false, snapshot);
                }
                else
                {
                    var secondWasOpen = Second != null;
                    // close First
                    CloseCapture(First);
                    // open as second
                    SetSnapshotAsOpenInUI(!secondWasOpen, snapshot);
                    if (secondWasOpen)
                        SwapOpenSnapshots();
                }
            }
            else
            {
                // open as First
                SetSnapshotAsOpenInUI(true, snapshot);
            }

            var reader = snapshot.LoadSnapshot();
            if (reader.HasOpenFile)
            {
                m_UIState.SetSnapshot(reader, snapshot == First);

                if (m_OpenSnapshotsPane.CompareMode && First != null && Second != null)
                    ShowDiffOfOpenSnapshots();
            }
            else
            {
                Debug.LogError("Failed to Open Snapshot");
                CloseCapture(snapshot);
            }
        }

        public bool IsSnapshotOpen(SnapshotFileData snapshot)
        {
            return snapshot == First || snapshot == Second;
        }

        public void CloseCapture(SnapshotFileData snapshot)
        {
            if (snapshot == null)
                return;
            try
            {
                if (Second != null)
                {
                    if (snapshot == Second)
                    {
                        m_UIState.ClearSecondMode();
                        snapshot.GuiData.SetCurrentState(false, false, m_OpenSnapshotsPane.CompareMode);
                    }
                    else if (snapshot == First)
                    {
                        m_UIState.ClearFirstMode();
                        if (First != null)
                            snapshot.GuiData.SetCurrentState(false, true, m_OpenSnapshotsPane.CompareMode);
                        First = Second;
                        m_UIState.SwapLastAndCurrentSnapshot();
                    }
                    else
                    {
                        // The snapshot wasn't open, there is nothing left todo here.
                        return;
                    }
                    Second = null;
                    m_UIState.CurrentViewMode = UIState.ViewMode.ShowFirst;

                    if (First != null)
                        m_OpenSnapshotsPane.SetSnapshotUIData(true, First.GuiData, true);
                    else
                        m_OpenSnapshotsPane.SetSnapshotUIData(true, null, true);
                    m_OpenSnapshotsPane.SetSnapshotUIData(false, null, false);
                    // With two snapshots open, there could also be a diff to be closed/cleared.
                    m_UIState.ClearDiffMode();
                }
                else
                {
                    if (snapshot == First)
                    {
                        snapshot.GuiData.SetCurrentState(false, true, m_OpenSnapshotsPane.CompareMode);
                        First = null;
                        m_UIState.ClearAllOpenModes();
                    }
                    else if (snapshot == Second)
                    {
                        snapshot.GuiData.SetCurrentState(false, false, m_OpenSnapshotsPane.CompareMode);
                        Second = null;
                        m_UIState.ClearAllOpenModes();
                    }
                    else
                    {
                        // The snapshot wasn't open, there is nothing left todo here.
                        return;
                    }
                    m_OpenSnapshotsPane.SetSnapshotUIData(true, null, false);
                    m_OpenSnapshotsPane.SetSnapshotUIData(false, null, false);
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        public void CloseAllOpenSnapshots()
        {
            if (Second != null)
            {
                CloseCapture(Second);
                Second = null;
            }
            if (First != null)
            {
                CloseCapture(First);
                First = null;
            }
        }

        void OnModeChanged(UIState.BaseMode newMode, UIState.ViewMode newViewMode)
        {
            switch (newViewMode)
            {
                case UIState.ViewMode.ShowDiff:
                    if (First != null)
                        First.GuiData.SetCurrentState(true, true, m_OpenSnapshotsPane.CompareMode);
                    if (Second != null)
                        Second.GuiData.SetCurrentState(true, false, m_OpenSnapshotsPane.CompareMode);
                    break;
                case UIState.ViewMode.ShowFirst:
                    if (First != null)
                        First.GuiData.SetCurrentState(true, true, m_OpenSnapshotsPane.CompareMode);
                    if (Second != null)
                        Second.GuiData.SetCurrentState(true, false, m_OpenSnapshotsPane.CompareMode);
                    break;
                case UIState.ViewMode.ShowSecond:
                    if (First != null)
                        First.GuiData.SetCurrentState(true, true, m_OpenSnapshotsPane.CompareMode);
                    if (Second != null)
                        Second.GuiData.SetCurrentState(true, false, m_OpenSnapshotsPane.CompareMode);
                    break;
                default:
                    break;
            }
        }

        void SwapOpenSnapshots(bool keepCurrentSnapshotOpen = true)
        {
            if (First == null && Second == null)
                return; // nothing there to swap

            var temp = Second;
            Second = First;
            First = temp;

            m_UIState.SwapLastAndCurrentSnapshot(keepCurrentSnapshotOpen);

            if (First != null)
                m_OpenSnapshotsPane.SetSnapshotUIData(true, First.GuiData, m_UIState.CurrentViewMode == UIState.ViewMode.ShowFirst);
            else
                m_OpenSnapshotsPane.SetSnapshotUIData(true, null, false);

            if (Second != null)
                m_OpenSnapshotsPane.SetSnapshotUIData(false, Second.GuiData, m_UIState.CurrentViewMode == UIState.ViewMode.ShowSecond);
            else
                m_OpenSnapshotsPane.SetSnapshotUIData(false, null, false);

            if (m_UIState.diffMode != null)
                m_UIState.diffMode.OnSnapshotsSwapped();
            SwappedSnapshots();
        }

        void ShowDiffOfOpenSnapshots()
        {
            if (m_UIState.diffMode != null)
            {
                SwitchSnapshotMode(UIState.ViewMode.ShowDiff);
            }
            else if (First != null && Second != null)
            {
                try
                {
                    MemoryProfilerAnalytics.StartEvent<MemoryProfilerAnalytics.DiffedSnapshotEvent>();

                    m_UIState.DiffLastAndCurrentSnapshot(First.GuiData.UtcDateTime.CompareTo(Second.GuiData.UtcDateTime) < 0, First.GuiData.SessionName != UIContentData.TextContent.UnknownSession && First.GuiData.SessionId == Second.GuiData.SessionId);

                    var crossSessionDiff = First.GuiData.SessionId == MetaData.InvalidSessionGUID || Second.GuiData.SessionId == First.GuiData.SessionId;
                    var snapshotAInfo = MemoryProfilerAnalytics.GetSnapshotProjectAndUnityVersionDetails(First);
                    var snapshotBInfo = MemoryProfilerAnalytics.GetSnapshotProjectAndUnityVersionDetails(Second);
                    if (First.GuiData.RuntimePlatform == Second.GuiData.RuntimePlatform)
                    {
                        snapshotAInfo |= MemoryProfilerAnalytics.SnapshotProjectAndUnityVersionDetails.SamePlatformAsDiff;
                        snapshotBInfo |= MemoryProfilerAnalytics.SnapshotProjectAndUnityVersionDetails.SamePlatformAsDiff;
                    }
                    MemoryProfilerAnalytics.EndEvent(new MemoryProfilerAnalytics.DiffedSnapshotEvent() { sameSessionDiff = !crossSessionDiff, captureInfoA = snapshotAInfo, captureInfoB = snapshotBInfo });
                }
                catch (Exception)
                {
                    throw;
                }
            }
            else
            {
                Debug.LogError("No second snapshot opened to diff to!");
            }
        }

        void ShowFirstOpenSnapshot()
        {
            if (First != null)
            {
                SwitchSnapshotMode(UIState.ViewMode.ShowFirst);
            }
        }

        void ShowSecondOpenSnapshot()
        {
            if (Second != null)
            {
                SwitchSnapshotMode(UIState.ViewMode.ShowSecond);
            }
        }

        void OnCompareModeChanged(bool compare)
        {
            if (First != null)
            {
                First.GuiData.SetCurrentState(true, true, m_OpenSnapshotsPane.CompareMode);
            }
            if (Second != null)
            {
                Second.GuiData.SetCurrentState(true, false, m_OpenSnapshotsPane.CompareMode);
            }
        }

        void SwitchSnapshotMode(UIState.ViewMode mode)
        {
            if (m_UIState.CurrentViewMode == mode)
                return;

            var currentViewName = "Unknown";
            if (currentViewPane is UI.TreeMapPane)
            {
                currentViewName = "TreeMap";
            }
            else if (currentViewPane is UI.MemoryMapPane)
            {
                currentViewName = "MemoryMap";
            }
            else if (currentViewPane is UI.SpreadsheetPane)
            {
                currentViewName = (currentViewPane as UI.SpreadsheetPane).TableDisplayName;
            }
            MemoryProfilerAnalytics.StartEvent<MemoryProfilerAnalytics.DiffToggledEvent>();

            var oldMode = m_UIState.CurrentViewMode;

            m_UIState.CurrentViewMode = mode;

            MemoryProfilerAnalytics.EndEvent(new MemoryProfilerAnalytics.DiffToggledEvent()
            {
                show = (int)ConvertUIModeToAnalyticsDiffToggleEventData(m_UIState.CurrentViewMode),
                shown = (int)ConvertUIModeToAnalyticsDiffToggleEventData(oldMode),
                viewName = currentViewName
            });
        }

        void BackToSnapshotDiffView()
        {
            m_UIState.CurrentViewMode = UIState.ViewMode.ShowDiff;
        }

        MemoryProfilerAnalytics.DiffToggledEvent.ShowSnapshot ConvertUIModeToAnalyticsDiffToggleEventData(UIState.ViewMode mode)
        {
            switch (mode)
            {
                case UIState.ViewMode.ShowDiff:
                    return MemoryProfilerAnalytics.DiffToggledEvent.ShowSnapshot.Both;
                case UIState.ViewMode.ShowFirst:
                    return MemoryProfilerAnalytics.DiffToggledEvent.ShowSnapshot.First;
                case UIState.ViewMode.ShowSecond:
                    return MemoryProfilerAnalytics.DiffToggledEvent.ShowSnapshot.Second;
                default:
                    throw new NotImplementedException();
            }
        }

        internal void RefreshOpenSnapshots(SnapshotCollectionEnumerator snaps)
        {
            SnapshotFileGUIData firstGUIData = null, secondGUIData = null;

            snaps.Reset();
            bool firstStillExists = false;
            bool secondStillExists = false;
            while (snaps.MoveNext())
            {
                if (First == snaps.Current.Snapshot)
                {
                    First = snaps.Current.Snapshot;
                    firstGUIData = First.GuiData;
                    firstGUIData.SetCurrentState(true, true, m_OpenSnapshotsPane.CompareMode);
                    firstStillExists = true;
                }
                else if (Second == snaps.Current.Snapshot)
                {
                    Second = snaps.Current.Snapshot;
                    secondGUIData = Second.GuiData;
                    secondGUIData.SetCurrentState(true, false, m_OpenSnapshotsPane.CompareMode);
                    secondStillExists = true;
                }
            }
            // if it's gone, close the second first as it would otherwise switch into the position of the First
            if (Second != null && !secondStillExists)
            {
                CloseCapture(Second);
            }

            if (First != null && !firstStillExists)
            {
                CloseCapture(First);
            }
            m_OpenSnapshotsPane.RefreshScreenshots(firstGUIData, secondGUIData);
        }

        public void UpdateSessionName(uint sessionId, string name)
        {
            if (First != null && First.GuiData.SessionId == sessionId)
                m_OpenSnapshotsPane.UpdateSessionName(true, name);
            if (Second != null && Second.GuiData.SessionId == sessionId)
                m_OpenSnapshotsPane.UpdateSessionName(false, name);
        }
    }
}

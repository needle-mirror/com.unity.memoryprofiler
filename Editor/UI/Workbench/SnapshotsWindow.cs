using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using Unity.MemoryProfiler.Editor.UI;
using Unity.EditorCoroutines.Editor;
using UnityEngine.SceneManagement;
using System.IO;
using Unity.MemoryProfiler.Editor.Format;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using TwoPaneSplitView = Unity.MemoryProfiler.Editor.TwoPaneSplitView;

namespace Unity.MemoryProfiler.Editor
{
    [Serializable]
    internal class SnapshotsWindow
    {
        TwoPaneSplitView m_Splitter;

        VisualTreeAsset m_SessionListItemTree;

        VisualElement m_EmptyWorkbenchText;
        ScrollView m_SnapshotList;
        bool m_ShowEmptySnapshotListHint = true;
        [NonSerialized]
        OpenSnapshotsManager m_OpenSnapshots = new OpenSnapshotsManager();
        [NonSerialized]
        SnapshotCollection m_MemorySnapshotsCollection;
        [NonSerialized]
        CaptureControlUI m_CaptureControlUI = new CaptureControlUI();

        Stack<SnapshotListItem> m_SnapshotListItemPool = new Stack<SnapshotListItem>();
        Stack<VisualElement> m_SessionListItemPool = new Stack<VisualElement>();

        public event Action SwappedSnapshots = delegate {};

        static Dictionary<BuildTarget, string> s_PlatformIconClasses = new Dictionary<BuildTarget, string>();

        IUIStateHolder m_ParentWindow;

        public void InitializeSnapshotsWindow(IUIStateHolder parentWindow, VisualElement rRoot, VisualElement leftPane, VisualElement right)
        {
            m_ParentWindow = parentWindow;
            m_MemorySnapshotsCollection = new SnapshotCollection(MemoryProfilerSettings.AbsoluteMemorySnapshotStoragePath);
            m_MemorySnapshotsCollection.SessionNameChanged += (sessionId, sessionName) => UpdateSessionName(sessionId, sessionName);
            m_MemorySnapshotsCollection.SessionNameChanged += (sessionId, sessionName) => m_OpenSnapshots.UpdateSessionName(sessionId, sessionName);
            m_OpenSnapshots = new OpenSnapshotsManager();
            m_OpenSnapshots.SwappedSnapshots += () => SwappedSnapshots();

            // setup the references for the snapshot list to be used for initial filling of the snapshot items as well as new captures and imports
            var snaphsotWindowToggle = rRoot.Q<ToolbarToggle>("toolbar__snaphsot-window-toggle");
            snaphsotWindowToggle.RegisterValueChangedCallback(ToggleSnapshotWindowVisibility);

            snaphsotWindowToggle.hierarchy.Insert(0, UIElementsHelper.GetImageWithClasses(new[] {"icon_button", "square-button-icon", "icon-button__snapshot-icon"}));

            m_Splitter = rRoot.Q<TwoPaneSplitView>("snapshot-window__splitter");
            m_EmptyWorkbenchText = rRoot.Q("session-list__empty-text");
            m_SnapshotList = rRoot.Q<ScrollView>("session-list__scroll-view");
            UIElementsHelper.SetScrollViewVerticalScrollerVisibility(m_SnapshotList as ScrollView, false);

            m_SessionListItemTree = AssetDatabase.LoadAssetAtPath(ResourcePaths.SessionListItemUxmlPath, typeof(VisualTreeAsset)) as VisualTreeAsset;

            var openSnapshotsPanel = m_OpenSnapshots.InitializeOpenSnapshotsWindow(GeneralStyles.InitialWorkbenchWidth, rRoot);

            m_CaptureControlUI.Initialize(parentWindow, m_MemorySnapshotsCollection, openSnapshotsPanel, m_OpenSnapshots, rRoot, leftPane);

            // fill snapshot List
            RefreshSnapshotList(m_MemorySnapshotsCollection.GetEnumerator());
            m_MemorySnapshotsCollection.collectionRefresh += RefreshSnapshotList;
            m_MemorySnapshotsCollection.collectionRefresh += m_OpenSnapshots.RefreshOpenSnapshots;
            m_MemorySnapshotsCollection.sessionDeleted += (sessionInfo) => m_SnapshotList.Remove(sessionInfo.DynamicUIElements.Root);
            m_MemorySnapshotsCollection.SnapshotCountIncreased += () => { if (!snaphsotWindowToggle.value) ToggleSnapshotWindowVisibility(ChangeEvent<bool>.GetPooled(false, true)); };
            m_MemorySnapshotsCollection.SnapshotTakenAndAdded += (snap) => { EditorCoroutineUtility.StartCoroutine(ScrollToNewSnapshot(snap), parentWindow as EditorWindow); };
            //SidebarWidthChanged += openSnapshotsPanel.UpdateWidth;
        }

        IEnumerator ScrollToNewSnapshot(SnapshotFileData snap)
        {
            yield return null;
            m_SnapshotList.ScrollTo(snap.GuiData.VisualElement);
        }

        public void RegisterAdditionalCaptureButton(Button captureButton)
        {
            m_CaptureControlUI.RegisterAdditionalCaptureButton(captureButton);
        }

        void ToggleSnapshotWindowVisibility(ChangeEvent<bool> evt)
        {
            if (evt.newValue)
                m_Splitter.UnCollapse();
            else
                m_Splitter.CollapseChild(0);
        }

        Dictionary<uint, SessionInfo> m_Sessions = new Dictionary<uint, SessionInfo>();

        public string GetSessionName(uint sessionId)
        {
            if (m_Sessions.ContainsKey(sessionId))
            {
                return m_Sessions[sessionId].SessionName;
            }
            return TextContent.UnknownSession;
        }

        void PoolSessionListItems(VisualElement root)
        {
            foreach (var item in root.Children())
            {
                m_SessionListItemPool.Push(item);
            }
            root.Clear();
        }

        VisualElement GetSessionListItem()
        {
            if (m_SessionListItemPool.Count > 0)
            {
                return m_SessionListItemPool.Pop();
            }
            return m_SessionListItemTree.Clone();
        }

        void PoolSnapshotListItems(VisualElement root)
        {
            foreach (var item in root.Children())
            {
                if (item is SnapshotListItem)
                {
                    var listItem = item as SnapshotListItem;
                    listItem.CurrentState = SnapshotFileGUIData.State.Closed;
                    m_SnapshotListItemPool.Push(listItem);
                }
            }
            root.Clear();
        }

        SnapshotListItem GetSnapshotListItem(VisualElement root)
        {
            if (m_SnapshotListItemPool.Count > 0)
            {
                var item = m_SnapshotListItemPool.Pop();
                root.Add(item);
                item.AssignCallbacks(m_MemorySnapshotsCollection.RenameSnapshot, OpenCapture, CanRenameSnaphot, DeleteCapture);
                return item;
            }
            var newItem = new SnapshotListItem();
            newItem.AssignCallbacks(m_MemorySnapshotsCollection.RenameSnapshot, OpenCapture, CanRenameSnaphot, DeleteCapture);
            root.Add(newItem);
            return newItem;
        }

        public void AddSessionToUIIfMissing(SessionInfo session)
        {
            VisualElement sessionListItem;
            if (!m_Sessions.ContainsKey(session.SessionId))
                m_Sessions.Add(session.SessionId, session);
            if (session.DynamicUIElements.Root == null)
            {
                sessionListItem = GetSessionListItem();

                session.DynamicUIElements.Root = sessionListItem;
                Foldout sessionFoldout = sessionListItem.Q<Foldout>("seession-list__item__foldout", "seession-list__item__foldout");
                PoolSnapshotListItems(sessionFoldout);
                session.DynamicUIElements.Foldout = sessionFoldout;

                // TODO: Find a way to allow session renaming and storing that name somewhere
                if (session.SessionId == MetaData.InvalidSessionGUID)
                    session.SessionName = TextContent.UnknownSession;
                else
                    session.SessionName = string.Format(TextContent.SessionName, m_SnapshotList.childCount + 1);
            }
            if (!m_SnapshotList.Contains(session.DynamicUIElements.Root))
                m_SnapshotList.Add(session.DynamicUIElements.Root);
        }

        public void UpdateSessionName(uint sessionId, string name)
        {
            if (m_Sessions.ContainsKey(sessionId))
            {
                foreach (var snapshot in m_Sessions[sessionId].Snapshots)
                {
                    snapshot.GuiData.SessionName = name;
                }
                // DON'T actually change the session name, that's where this event cjhain actually came from!
                // m_Sessions[sessionId].SessionName = name;
            }
        }

        void AddSessionToUI(SnapshotCollectionEnumerator snaps)
        {
            if (m_SnapshotList == null)
                return;

            if (m_ShowEmptySnapshotListHint)
            {
                // take out the empty-snapshot-list-please-take-a-capture-hint text
                UIElementsHelper.SwitchVisibility(m_SnapshotList, m_EmptyWorkbenchText);
                m_ShowEmptySnapshotListHint = false;
            }

            VisualElement snapshotListItemParent;

            var snapshot = snaps.Current.Snapshot;

            AddSessionToUIIfMissing(snaps.Current.SessionInfo);

            snapshotListItemParent = m_Sessions[snaps.Current.SessionInfo.SessionId].DynamicUIElements.Foldout;
            var snapshotListItem = GetSnapshotListItem(snapshotListItemParent);

            snapshotListItem.AssignSnapshot(snapshot);
        }

        public static void SetPlatformIcons(VisualElement snapshotItem, SnapshotFileGUIData snapshotGUIData)
        {
            Image platformIcon = snapshotItem.Q<Image>(GeneralStyles.PlatformIconName, GeneralStyles.PlatformIconClassName);
            Image editorIcon = snapshotItem.Q<Image>(GeneralStyles.EditorIconName, GeneralStyles.PlatformIconClassName);
            platformIcon.ClearClassList();
            platformIcon.AddToClassList(GeneralStyles.PlatformIconClassName);
            var platformIconClass = GetPlatformIconClass(snapshotGUIData.RuntimePlatform);
            if (!string.IsNullOrEmpty(platformIconClass))
                platformIcon.AddToClassList(platformIconClass);
            if (snapshotGUIData.MetaPlatform.Contains(GeneralStyles.PlatformIconEditorClassName))
            {
                UIElementsHelper.SetVisibility(editorIcon, true);
                platformIcon.AddToClassList(GeneralStyles.PlatformIconEditorClassName);
            }
            else
                UIElementsHelper.SetVisibility(editorIcon, false);
        }

        static string GetPlatformIconClass(RuntimePlatform platform)
        {
            BuildTarget buildTarget = platform.GetBuildTarget();
            if (buildTarget == BuildTarget.NoTarget)
                return null;

            if (!s_PlatformIconClasses.ContainsKey(buildTarget))
            {
                s_PlatformIconClasses[buildTarget] = buildTarget.ToString();
            }
            return s_PlatformIconClasses[buildTarget];
        }

        void DeleteCapture(SnapshotFileData snapshot)
        {
            if (!EditorUtility.DisplayDialog(TextContent.DeleteSnapshotDialogTitle, TextContent.DeleteSnapshotDialogMessage, TextContent.DeleteSnapshotDialogAccept, TextContent.DeleteSnapshotDialogCancel))
                return;

            m_OpenSnapshots.CloseCapture(snapshot);

            var SessionId = snapshot.GuiData.SessionId;
            if (m_Sessions.ContainsKey(SessionId))
            {
                m_Sessions[SessionId].DynamicUIElements.Foldout.Remove(snapshot.GuiData.VisualElement);
            }

            m_MemorySnapshotsCollection.RemoveSnapshotFromCollection(snapshot);
            if (m_SnapshotList.childCount <= 0)
            {
                m_ShowEmptySnapshotListHint = true;
                ShowWorkBenchHintText();
            }
        }

        bool CanRenameSnaphot(SnapshotFileData snapshot)
        {
            if (m_OpenSnapshots.IsSnapshotOpen(snapshot))
            {
                bool close = EditorUtility.DisplayDialog(TextContent.RenameSnapshotDialogTitle,
                    TextContent.RenameSnapshotDialogMessage,
                    TextContent.RenameSnapshotDialogAccept,
                    TextContent.RenameSnapshotDialogCancel);
                if (close)
                {
                    m_OpenSnapshots.CloseCapture(snapshot);
                    m_ParentWindow.Window.Focus();
                }
                return close;
            }
            return true;
        }

        void OpenCapture(SnapshotFileData snapshot)
        {
            bool isCloseEvent = m_OpenSnapshots.IsSnapshotOpen(snapshot);
            //try
            //{
            if (!isCloseEvent)
                MemoryProfilerAnalytics.StartEvent<MemoryProfilerAnalytics.LoadedSnapshotEvent>();

            m_OpenSnapshots.OpenSnapshot(snapshot);
            if (!isCloseEvent)
            {
                var snapshotDetails = MemoryProfilerAnalytics.GetSnapshotProjectAndUnityVersionDetails(snapshot);
                MemoryProfilerAnalytics.EndEvent(new MemoryProfilerAnalytics.LoadedSnapshotEvent() {success = snapshot.GuiData.ProductName == Application.productName, openSnapshotDetails = snapshotDetails, unityVersionOfSnapshot = snapshot.GuiData.UnityVersion, runtimePlatform = snapshot.GuiData.RuntimePlatform});
            }
            //}
            //catch (Exception e)
            //{
            //    throw e;
            //}
        }

        void RefreshSnapshotList(SnapshotCollectionEnumerator snaps)
        {
            ClearSnapshotListUI();
            m_ShowEmptySnapshotListHint = true;

            snaps.Reset();
            while (snaps.MoveNext())
            {
                AddSessionToUI(snaps);
            }

            if (m_ShowEmptySnapshotListHint)
            {
                ShowWorkBenchHintText();
            }
        }

        void ShowWorkBenchHintText()
        {
            if (m_SnapshotList.childCount <= 0 && m_ShowEmptySnapshotListHint)
            {
                UIElementsHelper.SwitchVisibility(m_EmptyWorkbenchText, m_SnapshotList);
            }
        }

        public void RefreshScreenshots()
        {
            m_MemorySnapshotsCollection.RefreshScreenshots();
        }

        public void RefreshCollection()
        {
            m_MemorySnapshotsCollection?.RefreshCollection();
        }

        public void OnDisable()
        {
            //SidebarWidthChanged = delegate { };
            if (m_MemorySnapshotsCollection != null)
                m_MemorySnapshotsCollection.Dispose();
            m_CaptureControlUI.OnDisable();
        }

        public void  CloseAllOpenSnapshots()
        {
            m_OpenSnapshots.CloseAllOpenSnapshots();
        }

        void ClearSnapshotListUI()
        {
            if (m_Sessions != null)
            {
                foreach (var item in m_Sessions)
                {
                    if (item.Value.DynamicUIElements.Foldout != null)
                    {
                        PoolSnapshotListItems(item.Value.DynamicUIElements.Foldout);
                        item.Value.DynamicUIElements = default;
                    }
                }
            }
            PoolSessionListItems(m_SnapshotList);
            m_SnapshotList.Clear();
            m_Sessions.Clear();
        }

        public void RegisterUIState(UIState stata)
        {
            m_OpenSnapshots.RegisterUIState(stata);
        }
    }
}

using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine.UIElements;
using Unity.MemoryProfiler.Editor.UI;
using TreeView = UnityEngine.UIElements.TreeView;
using UnityEditor;

namespace Unity.MemoryProfiler.Editor
{
    internal class SnapshotFilesListViewController : ViewController
    {
        const string k_UxmlAssetGuid = "87018144588dc0643b5a69ad32eb2571";
        const string k_UxmlTreeItemGuid = "ff52c1ab702481a49a414c311d8c6914";
        const string k_TreePersistencyKey = "com.unity.memoryprofiler.snapshotfileslistviewcontroller.treeview";
        const string k_TreePersistencyItemIdsKey = "com.unity.memoryprofiler.snapshotfileslistviewcontroller.treeview.itemids";

        const string k_UxmlTreeView = "memory-profiler-snapshots__tree-view";
        const string k_UxmlNoSnapshotsHint = "memory-profiler-snapshots__no-snapshots-message";
        const string k_UxmlTreeViewItemSession = "memory-profiler-snapshots__card__session-label";
        const string k_UxmlTreeViewItemSnapshot = "memory-profiler-snapshots__card__snapshots-container";

        readonly struct SnapshotItemData
        {
            public SnapshotItemData(string name, bool sessionGroup, SnapshotFileModel fileData)
            {
                Name = name;
                SessionGroup = sessionGroup;
                FileData = fileData;
            }

            public string Name { get; }
            public bool SessionGroup { get; }
            public SnapshotFileModel FileData { get; }
        }

        // Model
        SnapshotDataService m_SnapshotDataService;
        ScreenshotsManager m_ScreenshotsManager;

        // View
        TreeView m_SnapshotsCollection;
        VisualElement m_NoSnapshotsMessage;
        Dictionary<int, SnapshotFileItemViewController> m_TreeViewControllers;

        public SnapshotFilesListViewController(SnapshotDataService snapshotDataService, ScreenshotsManager screenshotsManager)
        {
            m_SnapshotDataService = snapshotDataService;
            m_ScreenshotsManager = screenshotsManager;

            m_TreeViewControllers = new Dictionary<int, SnapshotFileItemViewController>();

            m_SnapshotDataService.LoadedSnapshotsChanged += RefreshView;
            m_SnapshotDataService.CompareModeChanged += RefreshView;
            m_SnapshotDataService.AllSnapshotsChanged += RefreshView;
        }

        protected override VisualElement LoadView()
        {
            var view = ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlAssetGuid);
            if (view == null)
                throw new InvalidOperationException("Unable to create view from Uxml. Uxml must contain at least one child element.");

            GatherReferencesInView(view);

            return view;
        }

        protected override void ViewLoaded()
        {
            base.ViewLoaded();
            RefreshView();
        }

        void GatherReferencesInView(VisualElement view)
        {
            m_SnapshotsCollection = view.Q<TreeView>(k_UxmlTreeView);
            // Selection is not handled via the Tree View.
            // If a selection was made via the Tree View controls, e.g. by starting a click on a snapshot and ending it outside of it, clear it.
            // Without this, users could end up with a blue selection highlight on a snapshot that wasn't opened.
            m_SnapshotsCollection.selectedIndicesChanged += (a) => m_SnapshotsCollection.ClearSelection();
            m_SnapshotsCollection.viewDataKey = k_TreePersistencyKey;
            m_NoSnapshotsMessage = view.Q(k_UxmlNoSnapshotsHint);
        }

        void RefreshView()
        {
            bool hasSnapshots = m_SnapshotDataService.AllSnapshots.Count > 0;
            UIElementsHelper.SetVisibility(m_SnapshotsCollection, hasSnapshots);
            UIElementsHelper.SetVisibility(m_NoSnapshotsMessage, !hasSnapshots);

            RefreshTreeView();
        }

        void RefreshTreeView()
        {
            m_SnapshotsCollection.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            m_SnapshotsCollection.makeItem = MakeTreeItem;
            m_SnapshotsCollection.bindItem = BindTreeItem;
            m_SnapshotsCollection.unbindItem = UnbindTreeItem;

            var fullSnapshotList = m_SnapshotDataService.FullSnapshotList;
            if (fullSnapshotList.AllSnapshots.Count == 0)
            {
                m_SnapshotsCollection.SetRootItems(new List<TreeViewItemData<SnapshotItemData>>());
                return;
            }

            // Build tree
            var tree = new List<TreeViewItemData<SnapshotItemData>>();
            var usedIds = new HashSet<int>();
            foreach (var sessionId in fullSnapshotList.SortedSessionIds)
            {
                // add all session ids to usedIds so we can avoid duplicates in their children
                var sessionTreeItemId = (int)sessionId;
                usedIds.Add(sessionTreeItemId);
            }

            var oldItemEntries = new HashSet<int>(SessionState.GetIntArray(k_TreePersistencyItemIdsKey, new int[0]));
            var sessionsToExpand = new HashSet<int>();
            foreach (var sessionId in fullSnapshotList.SortedSessionIds)
            {
                // Add all snapshots with the same session id
                var children = fullSnapshotList.SessionsMap[sessionId];
                var sessionTreeItemId = (int)sessionId;
                var childrenItems = new List<TreeViewItemData<SnapshotItemData>>();
                foreach (var child in children)
                {
                    // generate a persistent tree item id based on the session ID and the snapshot file name.
                    var snapshotTreeItemId = sessionTreeItemId + child.Name.GetHashCode();

                    // in case of snapshotId clashes, increment it until it is unique
                    while (usedIds.Contains(snapshotTreeItemId))
                        snapshotTreeItemId++;

                    usedIds.Add(snapshotTreeItemId);
                    childrenItems.Add(new TreeViewItemData<SnapshotItemData>(snapshotTreeItemId, new SnapshotItemData(child.Name, false, child)));

                    if (!oldItemEntries.Contains(snapshotTreeItemId))
                        // There's been an addition/name change in the snapshots of this session, so we need to expand it to show that change
                        // There is a minor chance of false positives here when we have to increment the id more often than before due to clashes,
                        // but it's not a likely scenario, nor a big deal
                        sessionsToExpand.Add(sessionTreeItemId);
                }

                // Generate session name
                var sessionName = String.Format($"{m_SnapshotDataService.SessionNames[sessionId]} - {children.First().ProductName}");
                var groupItem = new TreeViewItemData<SnapshotItemData>(sessionTreeItemId, new SnapshotItemData(sessionName, true, null), childrenItems);
                tree.Add(groupItem);
            }

            SessionState.SetIntArray(k_TreePersistencyItemIdsKey, usedIds.ToArray());

            m_SnapshotsCollection.SetRootItems(tree);
            m_SnapshotsCollection.RefreshItems();
            if (sessionsToExpand.Count > 0)
            {
                // Expand everything next frame, as otherwise
                // TreeView might expand it wrongly
                m_SnapshotsCollection.schedule.Execute(() =>
                {
                    foreach (var id in sessionsToExpand)
                    {
                        m_SnapshotsCollection.ExpandItem(id);
                    }
                });
            }
        }

        VisualElement MakeTreeItem()
        {
            return ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlTreeItemGuid);
        }

        void BindTreeItem(VisualElement element, int index)
        {
            var itemData = m_SnapshotsCollection.GetItemDataForIndex<SnapshotItemData>(index);

            var sessionCard = element.Q<Label>(k_UxmlTreeViewItemSession);
            var fileDataCard = element.Q(k_UxmlTreeViewItemSnapshot);
            UIElementsHelper.SetVisibility(sessionCard, itemData.SessionGroup);
            UIElementsHelper.SetVisibility(fileDataCard, !itemData.SessionGroup);
            if (!itemData.SessionGroup)
            {
                var itemId = m_SnapshotsCollection.GetIdForIndex(index);
                var viewController = new SnapshotFileItemViewController(itemData.FileData, m_SnapshotDataService, m_ScreenshotsManager);

                var loadedState = SnapshotFileItemViewController.State.None;
                if (m_SnapshotDataService.CompareMode)
                {
                    if (PathHelpers.IsSamePath(m_SnapshotDataService.Base?.FullPath, itemData.FileData.FullPath))
                        loadedState = SnapshotFileItemViewController.State.LoadedBase;
                    else if (PathHelpers.IsSamePath(m_SnapshotDataService.Compared?.FullPath, itemData.FileData.FullPath))
                        loadedState = SnapshotFileItemViewController.State.LoadedCompare;
                }
                else if (PathHelpers.IsSamePath(m_SnapshotDataService.Base?.FullPath, itemData.FileData.FullPath))
                    loadedState = SnapshotFileItemViewController.State.Loaded;
                viewController.LoadedState = loadedState;

                fileDataCard.Add(viewController.View);
                AddChild(viewController);
                m_TreeViewControllers[itemId] = viewController;
            }
            else
                sessionCard.text = itemData.Name;
        }

        void UnbindTreeItem(VisualElement element, int index)
        {
            var itemData = m_SnapshotsCollection.GetItemDataForIndex<SnapshotItemData>(index);
            if (itemData.SessionGroup)
                return;

            var fileDataCard = element.Q(k_UxmlTreeViewItemSnapshot);
            fileDataCard.Clear();

            var itemId = m_SnapshotsCollection.GetIdForIndex(index);
            if (m_TreeViewControllers.TryGetValue(itemId, out var viewController))
            {
                RemoveChild(viewController);
                m_TreeViewControllers.Remove(itemId);
            }
        }
    }
}

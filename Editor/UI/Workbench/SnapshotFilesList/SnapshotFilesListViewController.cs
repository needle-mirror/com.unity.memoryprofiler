using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine.UIElements;
using Unity.MemoryProfiler.Editor.UI;
using TreeView = UnityEngine.UIElements.TreeView;

namespace Unity.MemoryProfiler.Editor
{
    internal class SnapshotFilesListViewController : ViewController
    {
        const string k_UxmlAssetGuid = "87018144588dc0643b5a69ad32eb2571";
        const string k_UxmlTreeItemGuid = "ff52c1ab702481a49a414c311d8c6914";

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

            if (!SnapshotDataService.MakeSortedSessionsListIds(m_SnapshotDataService.AllSnapshots, out var sortedSessionsList, out var snapshotsSessionsMap))
            {
                m_SnapshotsCollection.SetRootItems(new List<TreeViewItemData<SnapshotItemData>>());
                return;
            }

            // Build tree
            int itemId = 0;
            var tree = new List<TreeViewItemData<SnapshotItemData>>();
            foreach (var sessionId in sortedSessionsList)
            {
                // Add all snapshots with the same session id
                var children = snapshotsSessionsMap[sessionId];
                var childrenItems = new List<TreeViewItemData<SnapshotItemData>>();
                foreach (var child in children)
                {
                    childrenItems.Add(new TreeViewItemData<SnapshotItemData>(itemId++, new SnapshotItemData(child.Name, false, child)));
                }

                // Generate session name
                var sessionName = String.Format($"{m_SnapshotDataService.SessionNames[sessionId]} - {children.First().ProductName}");
                var groupItem = new TreeViewItemData<SnapshotItemData>(itemId++, new SnapshotItemData(sessionName, true, null), childrenItems);
                tree.Add(groupItem);
            }

            m_SnapshotsCollection.SetRootItems(tree);
            // Expand everything next frame, as otherwise
            // TreeView might expand it wrongly
            m_SnapshotsCollection.schedule.Execute(() => m_SnapshotsCollection.ExpandAll());
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

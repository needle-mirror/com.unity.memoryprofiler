using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UIElements;
using TreeView = UnityEditor.IMGUI.Controls.TreeView;

namespace Unity.MemoryProfiler.Editor.UI.PathsToRoot
{
    internal class PathsToRootDetailView : TreeView
    {
        internal static class Styles
        {
            public static readonly GUIContent CircularRefContent = EditorGUIUtility.IconContent("console.warnicon.sml",
                "|This is a circular reference, click to highlight original");
            //public static readonly Texture2D CSharpIcon = Icons.LoadIcon(Icons.IconFolder + "CSharpIcon@3x.png", false);
            //public static readonly Texture2D CPlusPlusIcon = Icons.LoadIcon(Icons.IconFolder + "CPlusPlusIcon@3x.png", false);
            public static readonly Texture2D CSharpIcon = IconUtility.LoadIconAtPath(Icons.IconFolder + "CSharpIcon.png", true);
            public static readonly Texture2D CPlusPlusIcon = IconUtility.LoadIconAtPath(Icons.IconFolder + "CPlusPlusIcon.png", true);
            public static readonly GUIContent SceneIcon = EditorGUIUtility.IconContent("SceneAsset Icon");
            public static readonly GUIContent CSharpIconContent = new GUIContent(CSharpIcon, "C# Object");
            public static readonly GUIContent CPlusPlusIconContent = new GUIContent(CPlusPlusIcon, "C++ Object");
            public static readonly string NoObjectSelected = L10n.Tr("No Object Selected");
            public static readonly string SelectionHasNoReferences = L10n.Tr("Selection has no references");
            public static readonly string NoInspectableObjectSelected = L10n.Tr("No inspectable object selected.");
        }

        const int k_CurrentSelectionTreeViewItemId = Int32.MaxValue;
        static readonly Color k_CurrentSelectionTreeViewItemBackgroundColor = new Color(0.129f, 0.129f, 0.129f); // #212121

        int m_ProcessingStackSize = 0;
        int m_ObjectsProcessed = 0;

        public event Action<CachedSnapshot.SourceIndex> SelectionChangedEvt;
        bool truncateTypeNames = MemoryProfilerSettings.MemorySnapshotTruncateTypes;

        enum PathsToRootViewColumns
        {
            Type,
            Flags
        }

        private enum PathsToRootViewGUIState
        {
            NothingSelected,
            Searching,
            SearchComplete
        }

        private enum ActiveTree
        {
            RawReferences,
            ReferencesTo
        }

        PathsToRootViewGUIState m_GUIState;
        SnapshotDataService m_SnapshotDataService;

        CachedSnapshot m_CachedSnapshot;
        ActiveTree m_ActiveTree;
        PathsToRootDetailTreeViewItem m_RawReferenceTree;
        PathsToRootDetailTreeViewItem m_ReferencesToTree;
        EditorCoroutine m_EditorCoroutine;
        RibbonButton m_RawConnectionButton;
        RibbonButton m_ReferencesToButton;
        Ribbon m_Ribbon;

        long m_CurrentSelection;

        Thread m_BackgroundThread;
        Selection CurrentSelection;
        enum BackGroundThreadState
        {
            None,
            Analyze,
            AnalyzeDone
        };

        BackGroundThreadState m_BackgroundThreadState;

        public PathsToRootDetailView(TreeViewState state, MultiColumnHeaderWithTruncateTypeName multiColumnHeaderWithTruncateTypeName, Ribbon ribbon)
            : base(state, multiColumnHeaderWithTruncateTypeName)
        {
            columnIndexForTreeFoldouts = 0;
            showAlternatingRowBackgrounds = true;
            showBorder = true;
            m_GUIState = PathsToRootViewGUIState.SearchComplete;
            m_BackgroundThreadState = BackGroundThreadState.None;
            rowHeight = 20f;
            multiColumnHeaderWithTruncateTypeName.ResizeToFit();
            MemoryProfilerSettings.TruncateStateChanged += OnTruncateStateChanged;

            m_RawReferenceTree = new PathsToRootDetailTreeViewItem();
            SetupDepthsFromParentsAndChildren(m_RawReferenceTree);

            m_ReferencesToTree = new PathsToRootDetailTreeViewItem();
            SetupDepthsFromParentsAndChildren(m_ReferencesToTree);

            m_Ribbon = ribbon;
            m_RawConnectionButton = m_Ribbon.Q<RibbonButton>("raw-connections");
            m_ReferencesToButton = m_Ribbon.Q<RibbonButton>("referencing-to");
            m_RawConnectionButton.text = $"Referenced By ({m_RawReferenceTree.children.Count})";
            m_Ribbon.Clicked += RibbonClicked;
            m_ReferencesToButton.text = $"References To ()";
            m_ActiveTree = ActiveTree.RawReferences;

            Reload();
        }

        void RibbonClicked(int idx)
        {
            m_ActiveTree = (ActiveTree)idx;

            // Report view change event
            using var openViewEvent = MemoryProfilerAnalytics.BeginOpenViewEvent(m_ActiveTree.ToString());
            Reload();
        }

        void OnTruncateStateChanged()
        {
            truncateTypeNames = MemoryProfilerSettings.MemorySnapshotTruncateTypes;
        }

        static PathsToRootDetailTreeViewItem DefaultItem()
        {
            return new PathsToRootDetailTreeViewItem()
            {
                id = 0,
                displayName = "No item selected"
            };
        }

        static PathsToRootDetailTreeViewItem CreateRootItem()
        {
            return new PathsToRootDetailTreeViewItem()
            {
                id = 0,
                displayName = "Root",
                depth = -1,
                children = new List<TreeViewItem>(),
            };
        }

        struct Selection
        {
            public ObjectData objectData;
            public CachedSnapshot CachedSnapshot;
        }

        public void SetRoot(CachedSnapshot snapshot, CachedSnapshot.SourceIndex source)
        {
            m_CachedSnapshot = snapshot;
            if (m_CachedSnapshot == null)
                return;

            switch (source.Id)
            {
                case CachedSnapshot.SourceIndex.SourceId.NativeObject:
                    UpdateRootObjects(m_CachedSnapshot.NativeObjectIndexToUnifiedObjectIndex(source.Index));
                    break;
                case CachedSnapshot.SourceIndex.SourceId.ManagedObject:
                    var unifiedIndex = m_CachedSnapshot.ManagedObjectIndexToUnifiedObjectIndex(source.Index);
                    if (unifiedIndex >= 0)
                        UpdateRootObjects(unifiedIndex);
                    break;
                default:
                    UpdateRootObjects(-1);
                    break;
            }
        }

        public void UpdateToAllocation(long item)
        {
            m_RawReferenceTree = CreateRootItem();
            SetupDepthsFromParentsAndChildren(m_RawReferenceTree);
            Reload();
        }

        public void UpdateRootObjects(long item)
        {
            if (item == m_CurrentSelection) return;

            m_CurrentSelection = item;
            if (m_BackgroundThread != null)
            {
                m_BackgroundThread.Abort();
                m_BackgroundThreadState = BackGroundThreadState.None;
                m_GUIState = PathsToRootViewGUIState.SearchComplete;
            }

            if (m_CurrentSelection != -1)
            {
                if (CurrentSelection.CachedSnapshot != null && !CurrentSelection.CachedSnapshot.Valid)
                {
                    Debug.LogError($"a {nameof(PathsToRootDetailView)} was leaked");
                    return;
                }
                m_BackgroundThreadState = BackGroundThreadState.Analyze;
                m_BackgroundThread = new Thread(AnalysisThread);
                m_BackgroundThread.Start(item);
            }
            else
            {
                NoObject(m_CachedSnapshot, ref m_RawReferenceTree);
                NoObject(m_CachedSnapshot, ref m_ReferencesToTree);
                Reload();
            }
        }

        static PathsToRootDetailTreeViewItem GenerateTreeWithoutCircularRefs(PathsToRootDetailTreeViewItem data)
        {
            var pathsToRootTreeViewItem = new PathsToRootDetailTreeViewItem(data) { children = new List<TreeViewItem>() };
            SearchChildren(pathsToRootTreeViewItem, data);

            return pathsToRootTreeViewItem;
        }

        static void SearchChildren(PathsToRootDetailTreeViewItem pathsToRootDetailTreeViewItem, PathsToRootDetailTreeViewItem data)
        {
            pathsToRootDetailTreeViewItem.children = new List<TreeViewItem>();
            foreach (var child in data.children)
            {
                var viewItem = new PathsToRootDetailTreeViewItem((PathsToRootDetailTreeViewItem)child);
                if (viewItem.HasCircularReference) continue;

                pathsToRootDetailTreeViewItem.children.Add(viewItem);
                pathsToRootDetailTreeViewItem.children.Last().parent = pathsToRootDetailTreeViewItem;
                SearchChildren(viewItem, (PathsToRootDetailTreeViewItem)child);
            }
        }

        void AnalysisThread(object o)
        {
            var item = o as long? ?? 0;
            try
            {
                switch (m_BackgroundThreadState)
                {
                    case BackGroundThreadState.None:
                        break;
                    case BackGroundThreadState.Analyze:
                        SearchForRootObjects(item);
                        m_RawReferenceTree = GenerateTreeWithoutCircularRefs(m_RawReferenceTree);
                        // m_RootDetail = InvertPathsToRoot(m_RootDetail);
                        SetupDepthsFromParentsAndChildren(m_RawReferenceTree);
                        if (CurrentSelection.CachedSnapshot != null && !CurrentSelection.CachedSnapshot.Valid)
                        {
                            Debug.Log("Aborting threaded Path To Roots calculation because the snapshot was unloaded");
                            return;
                        }
                        // m_RootDetail.children.Insert(0, new PathsToRootDetailTreeViewItem(CurrentSelection.objectData, CurrentSelection.CachedSnapshot) { id = k_CurrentSelectionTreeViewItemId });

                        GatherReferencesTo(item);

                        m_BackgroundThreadState = BackGroundThreadState.AnalyzeDone;
                        break;
                    case BackGroundThreadState.AnalyzeDone:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            catch (ThreadAbortException)
            {
            }
        }

        void GatherReferencesTo(long item)
        {
            if (m_CachedSnapshot == null)
            {
                Debug.Log(" no snapshot set");
                return;
            }

            var itemObjectData = ObjectData.FromUnifiedObjectIndex(m_CachedSnapshot, item);

            m_ReferencesToTree = CreateRootItem();

            if (!itemObjectData.IsValid) m_ReferencesToTree = DefaultItem();

            var objectsConnectingTo =
                ObjectConnection.GenerateReferencesTo(m_CachedSnapshot, itemObjectData,
                // TODO: Consider improving the PathsToRootDetailTreeViewItem for References To to show referencing field info and flipping addManagedObjectsWithFieldInfo to true
                addManagedObjectsWithFieldInfo: false);

            foreach (var data in objectsConnectingTo)
            {
                m_ReferencesToTree.AddChild(new PathsToRootDetailTreeViewItem(data, m_CachedSnapshot, truncateTypeNames));
            }
        }

        #region DebugHelpers
        /* we can use this to verify if the scene hierarchy is correctly being rebuilt from the snapshot data
        void SceneHierachyFromSceneRootsTransformTree()
        {
            if (m_CachedSnapshot == null)`
            {
                Debug.Log(" no snapshot set");
                return;
            }
            m_ObjectsProcessed = 0;
            m_ProcessingStackSize = 0;
            m_GUIState = PathsToRootViewGUIState.Searching;

            m_BackingData = CreateRootItem();

            for(int i = 0 ; i < m_CachedSnapshot.SceneRoots.SceneHierarchies.Length; i++)
            {
                var sceneRoot = new PathsToRootDetailTreeViewItem();
                sceneRoot.displayName = m_CachedSnapshot.SceneRoots.Name[i];
                m_BackingData.AddChild(sceneRoot);

                foreach (var child in m_CachedSnapshot.SceneRoots.SceneHierarchies[i].Children)
                {
                    var item = new PathsToRootDetailTreeViewItem(ObjectData.FromNativeObjectIndex(m_CachedSnapshot, m_CachedSnapshot.NativeObjects.instanceId2Index[child.GameObjectID]), m_CachedSnapshot);
                    sceneRoot.AddChild(item);
                    AddTransforms(child, item);
                }
            }

            m_GUIState = PathsToRootViewGUIState.SearchComplete;
        }

        void AddTransforms(CachedSnapshot.SceneRootEntriesCache.TransformTree transformTree, PathsToRootDetailTreeViewItem item)
        {
            foreach (var child in transformTree.Children)
            {
                var newChild = new PathsToRootDetailTreeViewItem(ObjectData.FromNativeObjectIndex(m_CachedSnapshot, m_CachedSnapshot.NativeObjects.instanceId2Index[child.GameObjectID]), m_CachedSnapshot);
                item.AddChild(newChild);
                AddTransforms(child, newChild);
            }
        }*/

        /*fun method that can be used to recreate the scene hierarchy when the snapshot has the scene roots data
        void ReconstructScene()
        {
            if (m_CachedSnapshot == null)
            {
                Debug.Log(" no snapshot set");
                return;
            }
            m_ObjectsProcessed = 0;
            m_ProcessingStackSize = 0;
            m_GUIState = PathsToRootViewGUIState.Searching;
            var processingStack = new Stack<PathsToRootDetailTreeViewItem>();

            m_BackingData = CreateRootItem();

            for (int i = 0; i < m_CachedSnapshot.SceneRoots.AllRootTransformInstanceIds.Count; i++)
            {
                var rootId = m_CachedSnapshot.SceneRoots.AllRootTransformInstanceIds[i];
                var current = new PathsToRootDetailTreeViewItem(ObjectData.FromNativeObjectIndex(m_CachedSnapshot, m_CachedSnapshot.NativeObjects.instanceId2Index[rootId]), m_CachedSnapshot);

                var allObjectConnectingTo = ObjectConnection.GetAllReferencedObjects(m_CachedSnapshot, current.Data);
                if (allObjectConnectingTo == null) continue;

                foreach (var objectData in allObjectConnectingTo)
                {
                    if (objectData.IsGameObject(m_CachedSnapshot) &&
                        objectData.isNative &&
                        m_CachedSnapshot.NativeObjects.ObjectName[objectData.nativeObjectIndex] == m_CachedSnapshot.NativeObjects.ObjectName[m_CachedSnapshot.NativeObjects.instanceId2Index[rootId]])
                    {
                        var go = new PathsToRootDetailTreeViewItem(objectData, m_CachedSnapshot);
                        m_BackingData.AddChild(go);
                        processingStack.Push(go);
                    }
                }
            }

            while (processingStack.Count > 0)
            {
                var current = processingStack.Pop();
                m_ObjectsProcessed++;

                var objData = ObjectConnection.GetAllReferencedObjects(m_CachedSnapshot, current.Data);
                if (objData == null) continue;

                //filter on transforms
                if (m_CachedSnapshot.NativeObjects.NativeTypeArrayIndex[current.Data.nativeObjectIndex] == m_CachedSnapshot.NativeTypes.GameObjectIdx)
                {
                    var tmp = new List<ObjectData>();
                    for (int i = 0; i < objData.Length; i++)
                    {
                        if (m_CachedSnapshot.NativeObjects.NativeTypeArrayIndex[objData[i].nativeObjectIndex] == m_CachedSnapshot.NativeTypes.GameObjectIdx)
                        {
                            tmp.Add(objData[i]);
                        }
                    }

                    objData = tmp.ToArray();
                }

                foreach (var objectData in objData)
                {
                    if (objectData.dataType == ObjectDataType.Unknown) continue;

                    var child = new PathsToRootDetailTreeViewItem(objectData, m_CachedSnapshot, current);
                    current.AddChild(child);

                    //global is root so dont push it
                    if (!child.HasCircularReference)
                    {
                        processingStack.Push(child);
                        m_ProcessingStackSize++;
                    }
                }
            }
            m_GUIState = PathsToRootViewGUIState.SearchComplete;
        }*/


        #endregion

        void SearchForRootObjects(long item)
        {
            if (m_CachedSnapshot == null)
            {
                Debug.Log(" no snapshot set");
                return;
            }

            m_RawReferenceTree = CreateRootItem();
            m_ObjectsProcessed = 0;
            m_ProcessingStackSize = 0;
            m_GUIState = PathsToRootViewGUIState.Searching;
            var itemObjectData = ObjectData.FromUnifiedObjectIndex(m_CachedSnapshot, item);

            if (!itemObjectData.IsValid) m_RawReferenceTree = DefaultItem();

            var objectsConnectingTo =
                ObjectConnection.GetAllReferencingObjects(m_CachedSnapshot, itemObjectData);

            if (objectsConnectingTo.Length == 0)
            {
                NoReferenceObject(m_RawReferenceTree);
                m_GUIState = PathsToRootViewGUIState.SearchComplete;
                return;
            }

            var processingQueue = new Queue<PathsToRootDetailTreeViewItem>();
            m_RawReferenceTree.children.Clear();

            foreach (var objectData in objectsConnectingTo)
            {
                m_RawReferenceTree.AddChild(new PathsToRootDetailTreeViewItem(objectData, m_CachedSnapshot, truncateTypeNames));
            }

            foreach (var treeViewItem in m_RawReferenceTree.children)
            {
                var objectTreeChild = (PathsToRootDetailTreeViewItem)treeViewItem;
                processingQueue.Enqueue(objectTreeChild);
            }

            m_ProcessingStackSize = processingQueue.Count;

            RawDataSearch(processingQueue);

            m_GUIState = PathsToRootViewGUIState.SearchComplete;
        }

        void RawDataSearch(Queue<PathsToRootDetailTreeViewItem> processingQueue)
        {
            while (processingQueue.Count > 0 && m_ObjectsProcessed < 1000)
            {
                var current = processingQueue.Dequeue();
                m_ObjectsProcessed++;

                var connections = current.Data.GetAllReferencingObjects(m_CachedSnapshot);
                if (connections == null) continue;

                foreach (var connection in connections)
                {
                    // we can skip something that is referencing a type as its just a static field holding a connection to the type
                    // might need to come back and reconsider this in the future
                    if (connection.IsUnknownDataType() || connection.displayObject.dataType == ObjectDataType.Type) continue;

                    var child = new PathsToRootDetailTreeViewItem(connection.displayObject, m_CachedSnapshot, current, truncateTypeNames);
                    current.AddChild(child);

                    if (!child.HasCircularReference)
                    {
                        processingQueue.Enqueue(child);
                        m_ProcessingStackSize++;
                    }
                }
            }
        }

        void SearchUsingFallBack(Stack<PathsToRootDetailTreeViewItem> processingStack)
        {
            while (processingStack.Count > 0 && m_ObjectsProcessed < 10000)
            {
                var current = processingStack.Pop();
                m_ObjectsProcessed++;

                var objData = ObjectConnection.GetAllReferencingObjects(m_CachedSnapshot, current.Data);
                if (objData == null) continue;


                if (ContainsGameObjects(objData))
                {
                    objData = Filter(objData, new int[] { m_CachedSnapshot.NativeTypes.GameObjectIdx });
                    foreach (var objectData in objData)
                    {
                        current.AddChild(new PathsToRootDetailTreeViewItem(objectData, m_CachedSnapshot, truncateTypeNames));
                    }
                    continue;
                }

                foreach (var objectData in objData)
                {
                    if (objectData.dataType == ObjectDataType.Unknown) continue;

                    var child = new PathsToRootDetailTreeViewItem(objectData, m_CachedSnapshot, current, truncateTypeNames);
                    current.AddChild(child);

                    if (!child.HasCircularReference)
                    {
                        processingStack.Push(child);
                        m_ProcessingStackSize++;
                    }
                }
            }
        }

        void SearchUsingSceneRoots(long item, ObjectData itemObjectData, ObjectData[] objectsConnectingTo, Stack<PathsToRootDetailTreeViewItem> processingStack)
        {
            while (processingStack.Count > 0 && m_ObjectsProcessed < 10000)
            {
                var current = processingStack.Pop();
                m_ObjectsProcessed++;

                var objData = FilterToOnlyTransforms(ObjectConnection.GetAllReferencingObjects(m_CachedSnapshot, current.Data));
                if (objData.Length == 0) continue;

                foreach (var objectData in objData)
                {
                    if (objectData.dataType == ObjectDataType.Unknown) continue;

                    var child = new PathsToRootDetailTreeViewItem(objectData, m_CachedSnapshot, current, truncateTypeNames);
                    current.AddChild(child);

                    if (child.Data.IsRootTransform(m_CachedSnapshot))
                    {
                        var o = ObjectConnection.GetAllReferencingObjects(m_CachedSnapshot, child.Data);
                        foreach (var data in o)
                        {
                            if (data.IsGameObject(m_CachedSnapshot))
                                child.AddChild(new PathsToRootDetailTreeViewItem(data, m_CachedSnapshot, truncateTypeNames));
                        }
                    }
                    else if (!child.HasCircularReference)
                    {
                        processingStack.Push(child);
                        m_ProcessingStackSize++;
                    }
                }
            }
        }

        bool ContainsGameObjects(ObjectData[] objData)
        {
            foreach (var objectData in objData)
            {
                if (objectData.IsGameObject(m_CachedSnapshot))
                    return true;
            }

            return false;
        }

        ObjectData[] FilterToOnlyTransforms(ObjectData[] objectData)
        {
            List<ObjectData> tmp = new List<ObjectData>();
            if (objectData != null)
            {
                foreach (var od in objectData)
                {
                    if (od.IsTransform(m_CachedSnapshot))
                        tmp.Add(od);
                }
            }
            return tmp.ToArray();
        }

        ObjectData[] Filter(ObjectData[] listToFilter, int[] idToFilterOn)
        {
            List<ObjectData> tmp = new List<ObjectData>();
            foreach (var od in listToFilter)
            {
                var id = m_CachedSnapshot.NativeObjects.NativeTypeArrayIndex[od.nativeObjectIndex];
                if (idToFilterOn.Contains(id))
                    tmp.Add(od);
            }

            return tmp.ToArray();
        }

        void NoReferenceObject(PathsToRootDetailTreeViewItem item)
        {
            item = CreateRootItem();
        }

        void NoObject(CachedSnapshot cs, ref PathsToRootDetailTreeViewItem tree)
        {
            CurrentSelection = default;
            tree = CreateRootItem();
        }

        protected override TreeViewItem BuildRoot()
        {
            if (m_RawReferenceTree == null)
            {
                m_RawReferenceTree = new PathsToRootDetailTreeViewItem();
                SetupDepthsFromParentsAndChildren(m_RawReferenceTree);
            }

            if (m_ReferencesToTree == null)
            {
                m_ReferencesToTree = new PathsToRootDetailTreeViewItem();
                SetupDepthsFromParentsAndChildren(m_ReferencesToTree);
            }

            GenerateIcons(m_RawReferenceTree);
            GenerateIcons(m_ReferencesToTree);

            m_RawConnectionButton.text = $"Referenced By ({m_RawReferenceTree.children.Count})";
            m_ReferencesToButton.text = $"References To ({m_ReferencesToTree.children.Count})";
            switch (m_ActiveTree)
            {
                case ActiveTree.RawReferences:
                    return m_RawReferenceTree;
                case ActiveTree.ReferencesTo:
                    return m_ReferencesToTree;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        static PathsToRootDetailTreeViewItem InvertPathsToRoot(PathsToRootDetailTreeViewItem rootDetail)
        {
            var stack = new Stack<PathsToRootDetailTreeViewItem>();

            var viewItem = new PathsToRootDetailTreeViewItem((PathsToRootDetailTreeViewItem)rootDetail.children[0]);
            RecursiveFindAllLeafNodes(viewItem, stack);
            var id = 0;
            rootDetail.children = new List<TreeViewItem>();
            while (stack.Count > 0)
            {
                var pathsToRootDetailTreeViewItem = stack.Pop();

                var items = new List<PathsToRootDetailTreeViewItem>();
                while (pathsToRootDetailTreeViewItem.parent != null)
                {
                    var item = pathsToRootDetailTreeViewItem;
                    item.children = null;
                    items.Add(new PathsToRootDetailTreeViewItem(item));
                    pathsToRootDetailTreeViewItem = (PathsToRootDetailTreeViewItem)pathsToRootDetailTreeViewItem.parent;
                }

                // -2 to exclude the treeview root and the actual selected object
                for (var j = 0; j < items.Count - 2; j++)
                {
                    items[j].children = new List<TreeViewItem> { items[j + 1] };
                }

                foreach (var treeViewItem in items)
                {
                    treeViewItem.id = id++;
                }

                if (items.Count > 0)
                    rootDetail.children.Add(items[0]);
            }
            return rootDetail;
        }

        static void RecursiveFindAllLeafNodes(PathsToRootDetailTreeViewItem pathsToRootDetailTreeViewItem, Stack<PathsToRootDetailTreeViewItem> stack)
        {
            if (!pathsToRootDetailTreeViewItem.hasChildren)
            {
                stack.Push(pathsToRootDetailTreeViewItem);
                return;
            }

            foreach (var child in pathsToRootDetailTreeViewItem.children)
            {
                RecursiveFindAllLeafNodes((PathsToRootDetailTreeViewItem)child, stack);
            }
        }

        public static MultiColumnHeaderState CreateDefaultMultiColumnHeaderState()
        {
            var columns = new[]
            {
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("Details"),
                    headerTextAlignment = TextAlignment.Left,
                    sortedAscending = true,
                    sortingArrowAlignment = TextAlignment.Center,
                    width = 300,
                    minWidth = 60,
                    autoResize = true,
                    allowToggleVisibility = false
                },
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("!"),
                    headerTextAlignment = TextAlignment.Left,
                    sortedAscending = true,
                    sortingArrowAlignment = TextAlignment.Center,
                    width = 40,
                    minWidth = 40,
                    maxWidth = 40,
                    autoResize = false,
                    allowToggleVisibility = true,
                }
            };

            Assert.AreEqual(columns.Length, Enum.GetValues(typeof(PathsToRootViewColumns)).Length,
                "Number of columns should match number of enum values: You probably forgot to update one of them.");

            var state = new MultiColumnHeaderState(columns);
            state.visibleColumns = new[] { 0 };
            return state;
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            var item = args.item;

            var isMainSelection = item.id == k_CurrentSelectionTreeViewItemId;
            if (isMainSelection)
            {
                // Draw dark row background for current selection in main window.
                var color = GUI.color;
                GUI.color = k_CurrentSelectionTreeViewItemBackgroundColor;
                GUI.DrawTexture(args.rowRect, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, false);
                GUI.color = color;
            }

            for (int i = 0; i < args.GetNumVisibleColumns(); ++i)
            {
                CellGUI(args.GetCellRect(i), item, (PathsToRootViewColumns)args.GetColumn(i), ref args);
            }
        }

        public void DoGUI(Rect rect)
        {
            switch (m_BackgroundThreadState)
            {
                case BackGroundThreadState.None:
                    break;
                case BackGroundThreadState.Analyze:
                    break;
                case BackGroundThreadState.AnalyzeDone:
                    Reload();
                    m_BackgroundThreadState = BackGroundThreadState.None;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            switch (m_GUIState)
            {
                case PathsToRootViewGUIState.NothingSelected:
                    break;
                case PathsToRootViewGUIState.Searching:
                    DoSearchingGUI(rect);
                    break;
                case PathsToRootViewGUIState.SearchComplete:
                    OnGUI(rect);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        void GenerateIcons(PathsToRootDetailTreeViewItem item)
        {
            item.SetIcons(m_CachedSnapshot);
            if (!item.hasChildren) return;
            foreach (var child in item.children)
            {
                GenerateIcons(child as PathsToRootDetailTreeViewItem);
            }
        }

        void DoSearchingGUI(Rect rect)
        {
            Rect iconRect = new Rect(rect.x, rect.y + (rect.height / 2), 30, 30);
            Rect textRect = new Rect(rect.x + 35, rect.y + (rect.height / 2), rect.width - 35, 30);
            GUI.Label(textRect, "Processing " + m_ObjectsProcessed + " of " + m_ProcessingStackSize);
            Repaint();
        }

        void CellGUI(Rect cellRect, TreeViewItem item, PathsToRootViewColumns column, ref RowGUIArgs args)
        {
            // Center cell rect vertically (makes it easier to place controls, icons etc in the cells)
            CenterRectUsingSingleLineHeight(ref cellRect);
            var pathsToRootDetailTreeViewItem = (PathsToRootDetailTreeViewItem)item;
            switch (column)
            {
                case PathsToRootViewColumns.Type:
                {
                    var indent = GetContentIndent(item);
                    cellRect.x += indent;
                    cellRect.width -= indent;
                    if (pathsToRootDetailTreeViewItem.Data.IsValid)
                        GUI.DrawTexture(new Rect(cellRect.x, cellRect.y, 20, 20), pathsToRootDetailTreeViewItem.Data.isManaged ? Styles.CSharpIcon : Styles.CPlusPlusIcon);
                    cellRect.x += 20;
                    cellRect.width -= 20;
                    if (pathsToRootDetailTreeViewItem.icon != null)
                        GUI.Label(new Rect(cellRect.x, cellRect.y, 20, 20), pathsToRootDetailTreeViewItem.TypeIcon);
                    var typeNameRect = new Rect(cellRect.x + 20, cellRect.y, cellRect.width - 20, cellRect.height);
                    var text = $"{(truncateTypeNames ? pathsToRootDetailTreeViewItem.TruncatedTypeName : pathsToRootDetailTreeViewItem.TypeName)} \"{pathsToRootDetailTreeViewItem.displayName}\"";
                    if (pathsToRootDetailTreeViewItem.IsRoot(m_CachedSnapshot))
                        text = $"SceneRoot {text}";
                    var isMainSelection = item.id == k_CurrentSelectionTreeViewItemId;
                    if (isMainSelection)
                    {
                        GUI.Label(typeNameRect, text, EditorStyles.boldLabel);
                    }
                    else
                        GUI.Label(typeNameRect, text);

                    if (pathsToRootDetailTreeViewItem.IsRoot(m_CachedSnapshot))
                    {
                        var size = GUI.skin.label.CalcSize(new GUIContent(text));
                        GUI.DrawTexture(new Rect(typeNameRect.x + size.x, typeNameRect.y, 20, 20), Styles.SceneIcon.image);
                    }
                }
                break;
                case PathsToRootViewColumns.Flags:
                {
                    if (pathsToRootDetailTreeViewItem.HasCircularReference)
                    {
                        if (GUI.Button(cellRect, Styles.CircularRefContent))
                        {
                            SetSelection(new[] { pathsToRootDetailTreeViewItem.CircularRefId }, TreeViewSelectionOptions.FireSelectionChanged | TreeViewSelectionOptions.RevealAndFrame);
                        }
                    }

                    if (pathsToRootDetailTreeViewItem.HasFlags())
                    {
                        if (GUI.Button(cellRect, pathsToRootDetailTreeViewItem.ObjectFlags))
                        {
                            EditorUtility.DisplayDialog("Flags info", pathsToRootDetailTreeViewItem.FlagsInfo, "OK");
                        }
                    }
                }
                break;
                default:
                    throw new Exception("Unknown or not implemented PathsToRootColumns enum when doing cell gui");
            }
        }

        static readonly char[] k_GenericBracesChars = { '<', '>' };
        // if we hit something that doesnt truncate properly then use it as a test case in
        // TypeNameTruncationTests and fix it up
        public static string TruncateTypeName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;
            name = name.Replace(" ", "");
            if (name.Contains('.'))
            {
                if (name.Contains('<'))
                {
                    var pos = 0;
                    while (pos < name.Length && name.IndexOf('<', pos + 1) != -1)
                    {
                        pos = name.IndexOf('<', pos + 1);
                        var next = name.IndexOfAny(k_GenericBracesChars, pos + 1);
                        var replacee = name.Substring(pos + 1, next - (pos + 1));
                        if (replacee.Contains(','))
                        {
                            var split = replacee.Split(',');
                            foreach (var s in split)
                            {
                                var truncated = TruncateTypeName(s);
                                pos += truncated.Length;
                                name = name.Replace(s, truncated);
                            }

                            continue;
                        }

                        if (!string.IsNullOrEmpty(replacee))
                        {
                            var truncatedReplacee = TruncateTypeName(replacee);
                            pos += truncatedReplacee.Length;
                            name = name.Replace(replacee, truncatedReplacee);
                        }
                    }
                }


                var nameSplt = name.Split('.');
                name = string.Empty;
                int offset = 1;
                while (offset < nameSplt.Length &&
                    (string.IsNullOrEmpty(nameSplt[nameSplt.Length - offset])
                    || nameSplt[nameSplt.Length - offset].StartsWith("<", StringComparison.Ordinal)))
                    offset++;
                var mainNamePart = nameSplt.Length - offset;
                for (int i = 0; i < nameSplt.Length; i++)
                {
                    if (i < mainNamePart)
                    {
                        if (nameSplt[i].IndexOfAny(k_GenericBracesChars, 0) != -1)
                        {
                            if (!string.IsNullOrEmpty(name))
                                name += $".{nameSplt[i]}";
                            else
                                name = nameSplt[i];
                        }
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(name))
                            name += $".{nameSplt[i]}";
                        else
                            name = nameSplt[i];
                    }
                }
            }

            return name;
        }

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            var item = this.FindRows(selectedIds);
            var source = ((PathsToRootDetailTreeViewItem)item[0]).Data.displayObject.GetSourceLink(m_CachedSnapshot);

            // invalid index means the no object selected object was selected making it safe to ignore the event.
            if (source.Id == CachedSnapshot.SourceIndex.SourceId.None)
                return;

            SelectionChangedEvt?.Invoke(source);

            // Track selection count
            MemoryProfilerAnalytics.AddReferencePanelInteraction(MemoryProfilerAnalytics.ReferencePanelInteraction.SelectionInTableCount);
        }

        protected override void ExpandedStateChanged()
        {
            base.ExpandedStateChanged();

            // Track unfolds count
            MemoryProfilerAnalytics.AddReferencePanelInteraction(MemoryProfilerAnalytics.ReferencePanelInteraction.TreeViewElementExpandCount);
        }

        protected override bool CanMultiSelect(TreeViewItem item)
        {
            return false;
        }

        public void OnDisable()
        {
            MemoryProfilerSettings.TruncateStateChanged -= OnTruncateStateChanged;
        }

        public void ClearSecondarySelection()
        {
            SetSelection(new List<int>());
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.Assertions;
using Debug = UnityEngine.Debug;

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
            public static readonly Texture2D CSharpIcon = Icons.LoadIcon(Icons.IconFolder + "CSharpIcon.png", true);
            public static readonly Texture2D CPlusPlusIcon = Icons.LoadIcon(Icons.IconFolder + "CPlusPlusIcon.png", true);
            public static readonly GUIContent CSharpIconContent = new GUIContent(CSharpIcon, "C# Object");
            public static readonly GUIContent CPlusPlusIconContent = new GUIContent(CPlusPlusIcon, "C++ Object");
        }

        const int k_CurrentSelectionTreeViewItemId = Int32.MaxValue;
        static readonly Color k_CurrentSelectionTreeViewItemBackgroundColor = new Color(0.129f, 0.129f, 0.129f); // #212121

        int m_ProcessingStackSize = 0;
        int m_ObjectsProcessed = 0;

        public event Action<MemorySampleSelection> SelectionChangedEvt = delegate {};
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

        PathsToRootViewGUIState m_GUIState;
        IUIStateHolder m_UIStateHolder;

        CachedSnapshot m_CachedSnapshot;
        PathsToRootDetailTreeViewItem m_RootDetail;
        PathsToRootDetailTreeViewItem m_BackingData;
        EditorCoroutine m_EditorCoroutine;

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

        public PathsToRootDetailView(IUIStateHolder uiStateHolder, TreeViewState state, MultiColumnHeaderWithTruncateTypeName multiColumnHeaderWithTruncateTypeName)
            : base(state, multiColumnHeaderWithTruncateTypeName)
        {
            m_UIStateHolder = uiStateHolder;
            columnIndexForTreeFoldouts = 0;
            showAlternatingRowBackgrounds = true;
            showBorder = true;
            m_GUIState = PathsToRootViewGUIState.SearchComplete;
            m_BackgroundThreadState = BackGroundThreadState.None;
            rowHeight = 20f;
            multiColumnHeaderWithTruncateTypeName.ResizeToFit();
            MemoryProfilerSettings.TruncateStateChanged += OnTruncateStateChanged;
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
                depth = -1
            };
        }

        struct Selection
        {
            public ObjectData objectData;
            public CachedSnapshot CachedSnapshot;
        }


        PathsToRootDetailTreeViewItem NewSelection(ObjectData od, CachedSnapshot cs)
        {
            CurrentSelection = new Selection
            {
                objectData = od,
                CachedSnapshot = cs
            };
            m_BackingData = CreateRootItem();
            m_BackingData.AddChild(new PathsToRootDetailTreeViewItem(od, cs));
            return (PathsToRootDetailTreeViewItem)m_BackingData.children[0];
        }

        public void UpdateRootObjects(MemorySampleSelection selection)
        {
            // Get the appropriate snapshot
            m_CachedSnapshot = selection.GetSnapshotItemIsPresentIn(m_UIStateHolder.UIState);

            if (m_CachedSnapshot != null)
            {
                // Path To/From Roots displays the roots based on the main selection.
                // The Secondary selection is solely for selecting items in the path.
                if (selection.Rank == MemorySampleSelectionRank.MainSelection)
                {
                    switch (selection.Type)
                    {
                        case MemorySampleSelectionType.NativeObject:
                            UpdateRootObjects(m_CachedSnapshot.NativeObjectIndexToUnifiedObjectIndex((long)selection.ItemIndex));
                            break;
                        case MemorySampleSelectionType.ManagedObject:
                            var unifiedIndex = m_CachedSnapshot.ManagedObjectIndexToUnifiedObjectIndex((long)selection.ItemIndex);
                            if (unifiedIndex >= 0)
                            {
                                if (m_CachedSnapshot.CrawledData.ManagedObjects[selection.ItemIndex].IsValid())
                                    UpdateRootObjects(unifiedIndex);
                            }
                            break;
                        case MemorySampleSelectionType.UnifiedObject:
                            UpdateRootObjects((long)selection.ItemIndex);
                            break;
                        case MemorySampleSelectionType.Allocation:
                            UpdateToAllocation(selection.ItemIndex);
                            break;
                        case MemorySampleSelectionType.AllocationSite:
                        case MemorySampleSelectionType.Symbol:
                        case MemorySampleSelectionType.NativeRegion:
                        case MemorySampleSelectionType.ManagedRegion:
                        case MemorySampleSelectionType.Allocator:
                        case MemorySampleSelectionType.Label:
                        case MemorySampleSelectionType.ManagedType:
                        case MemorySampleSelectionType.NativeType:
                        case MemorySampleSelectionType.Connection:
                        case MemorySampleSelectionType.None:
                        default:
                            UpdateRootObjects(-1);
                            break;
                    }
                }
                else if (selection.Rank == MemorySampleSelectionRank.SecondarySelection)
                {
                    // TODO: Restore secondary selection by selecting this item within the Root tree.
                    // TODO: even before that, make sure Secondary Selection events are even registered
                }
            }
            else
                UpdateRootObjects(-1);
        }

        public void UpdateToAllocation(long item)
        {
            m_BackingData = CreateRootItem();
            m_BackingData.AddChild(new PathsToRootDetailTreeViewItem(true));
            m_RootDetail = m_BackingData;
            SetupDepthsFromParentsAndChildren(m_RootDetail);
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
                NoObject(m_CachedSnapshot);
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
                        m_RootDetail = GenerateTreeWithoutCircularRefs(m_BackingData);
                        // m_RootDetail = InvertPathsToRoot(m_RootDetail);
                        SetupDepthsFromParentsAndChildren(m_RootDetail);
                        if (CurrentSelection.CachedSnapshot != null && !CurrentSelection.CachedSnapshot.Valid)
                        {
                            Debug.Log("Aborting threaded Path To Roots calculation because the snapshot was unloaded");
                            return;
                        }
                        // m_RootDetail.children.Insert(0, new PathsToRootDetailTreeViewItem(CurrentSelection.objectData, CurrentSelection.CachedSnapshot) { id = k_CurrentSelectionTreeViewItemId });

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
                Debug.Log("Current Search Cancelled");
            }
        }

        /* fun method that can be used to recreate the scen hierarchy when the snapshot has the scene roots data
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
            //find transform id

            for (int i = 0; i < m_CachedSnapshot.SceneRoots.AllRootIds.Count; i++)
            {
                var rootId = m_CachedSnapshot.SceneRoots.AllRootIds[i];
                var current = new PathsToRootDetailTreeViewItem(ObjectData.FromNativeObjectIndex(m_CachedSnapshot, m_CachedSnapshot.NativeObjects.instanceId2Index[rootId]), m_CachedSnapshot);

                var allObjectConnectingTo = ObjectConnection.GetAllObjectConnectingTo(m_CachedSnapshot, current.Data);

                if (allObjectConnectingTo == null) continue;

                m_BackingData.AddChild(current);
                processingStack.Push(current);
            }

            while (processingStack.Count > 0)
            {
                var current = processingStack.Pop();
                m_ObjectsProcessed++;


                var objData = ObjectConnection.GetAllObjectConnectingTo(m_CachedSnapshot, current.Data);
                if (objData == null) continue;


                //filter on transforms
                if (m_CachedSnapshot.NativeObjects.NativeTypeArrayIndex[current.Data.nativeObjectIndex] == m_CachedSnapshot.NativeTypes.TransformIdx)
                {
                    var tmp = new List<ObjectData>();
                    for (int i = 0; i < objData.Length; i++)
                    {
                        if (m_CachedSnapshot.NativeObjects.NativeTypeArrayIndex[objData[i].nativeObjectIndex] == m_CachedSnapshot.NativeTypes.TransformIdx)
                        {
                            tmp.Add(objData[i]);
                        }
                    }

                    objData = tmp.ToArray();
                }


                foreach (var objectData in objData)
                {
                    if (objectData.IsManagedGlobal() || objectData.dataType == ObjectDataType.Unknown) continue;

                    var child = new PathsToRootDetailTreeViewItem(objectData, m_CachedSnapshot);
                    child.hasCircularReference = HasCircularReference(current, objectData, out child.circularRefID);

                    current.AddChild(child);

                    //global is root so dont push it
                    if (objectData.dataType != ObjectDataType.Global && !child.hasCircularReference)
                    {
                        processingStack.Push(child);
                        m_ProcessingStackSize++;
                    }
                }
            }
            m_GUIState = PathsToRootViewGUIState.SearchComplete;
        }
*/

        void SearchForRootObjects(long item)
        {
            if (m_CachedSnapshot == null)
            {
                Debug.Log(" no snapshot set");
                return;
            }

            m_ObjectsProcessed = 0;
            m_ProcessingStackSize = 0;
            m_GUIState = PathsToRootViewGUIState.Searching;
            var itemObjectData = ObjectData.FromUnifiedObjectIndex(m_CachedSnapshot, item);

            if (!itemObjectData.IsValid) m_BackingData = DefaultItem();

            var objectsConnectingTo =
                ObjectConnection.GetAllReferencingObjects(m_CachedSnapshot, itemObjectData);

            if (objectsConnectingTo.Length == 0)
            {
                NoReferenceObject(itemObjectData, m_CachedSnapshot);
                m_GUIState = PathsToRootViewGUIState.SearchComplete;
                return;
            }

            var processingStack = new Stack<PathsToRootDetailTreeViewItem>();
            var newSelection = NewSelection(itemObjectData, m_CachedSnapshot);

            foreach (var objectData in objectsConnectingTo)
            {
                newSelection.AddChild(new PathsToRootDetailTreeViewItem(objectData, m_CachedSnapshot));
            }

            foreach (var treeViewItem in newSelection.children)
            {
                var objectTreeChild = (PathsToRootDetailTreeViewItem)treeViewItem;
                processingStack.Push(objectTreeChild);
            }

            m_ProcessingStackSize = processingStack.Count;

            RawDataSearch(processingStack);
            /*  if (m_CachedSnapshot.HasSceneRootsAndAssetbundles)
              {
                  SearchUsingSceneRoots(item, itemObjectData, objectsConnectingTo, processingStack);
              }
              else
              {
                  SearchUsingFallBack(processingStack);
              }*/

            m_GUIState = PathsToRootViewGUIState.SearchComplete;
        }

        void RawDataSearch(Stack<PathsToRootDetailTreeViewItem> processingStack)
        {
            while (processingStack.Count > 0 && m_ObjectsProcessed < 1000)
            {
                var current = processingStack.Pop();
                m_ObjectsProcessed++;

                var connections = current.Data.GetAllReferencingObjects(m_CachedSnapshot);
                if (connections == null) continue;

                foreach (var connection in connections)
                {
                    // we can skip something that is referencing a type as its just a static field holding a connection to the type
                    // might need to come back and reconsider this in the future
                    if (connection.IsUnknownDataType() || connection.displayObject.dataType == ObjectDataType.Type) continue;

                    var child = new PathsToRootDetailTreeViewItem(connection.displayObject, m_CachedSnapshot, current);
                    current.AddChild(child);

                    if (!child.HasCircularReference)
                    {
                        processingStack.Push(child);
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
                        current.AddChild(new PathsToRootDetailTreeViewItem(objectData, m_CachedSnapshot));
                    }
                    continue;
                }

                foreach (var objectData in objData)
                {
                    if (objectData.dataType == ObjectDataType.Unknown) continue;

                    var child = new PathsToRootDetailTreeViewItem(objectData, m_CachedSnapshot, current);
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

                    var child = new PathsToRootDetailTreeViewItem(objectData, m_CachedSnapshot, current);
                    current.AddChild(child);

                    if (child.Data.IsRoot(m_CachedSnapshot))
                    {
                        var o = ObjectConnection.GetAllReferencingObjects(m_CachedSnapshot, child.Data);
                        foreach (var data in o)
                        {
                            if (data.IsGameObject(m_CachedSnapshot))
                                child.AddChild(new PathsToRootDetailTreeViewItem(data, m_CachedSnapshot));
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

        void NoReferenceObject(ObjectData od, CachedSnapshot cs)
        {
            m_BackingData = CreateRootItem();
            m_BackingData.AddChild(new PathsToRootDetailTreeViewItem(od, cs));
            m_BackingData.children[0].displayName += " has no references";
        }

        void NoObject(CachedSnapshot cs)
        {
            CurrentSelection = default;
            m_BackingData = CreateRootItem();
            m_BackingData.AddChild(new PathsToRootDetailTreeViewItem(false));
            m_BackingData.children[0].displayName += "No Object Selected";
            m_RootDetail = CreateRootItem();
            m_RootDetail.AddChild(new PathsToRootDetailTreeViewItem(false));
            m_RootDetail.children[0].displayName += "No Object Selected";
        }

        protected override TreeViewItem BuildRoot()
        {
            if (m_BackingData == null)
            {
                m_BackingData = new PathsToRootDetailTreeViewItem();
                m_RootDetail = new PathsToRootDetailTreeViewItem(m_BackingData);
                SetupDepthsFromParentsAndChildren(m_RootDetail);
                return m_RootDetail;
            }

            foreach (var child in m_RootDetail.children)
            {
                GenerateIcons(child as PathsToRootDetailTreeViewItem);
            }
            return m_RootDetail;
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
                    var text = $"{(truncateTypeNames ? TruncateTypeName(pathsToRootDetailTreeViewItem.TypeName) :pathsToRootDetailTreeViewItem.TypeName)} \"{pathsToRootDetailTreeViewItem.displayName}\"";
                    var isMainSelection = item.id == k_CurrentSelectionTreeViewItemId;
                    if (isMainSelection)
                    {
                        GUI.Label(typeNameRect, text, EditorStyles.boldLabel);
                    }
                    else
                        GUI.Label(typeNameRect, text);
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

        public static string TruncateTypeName(string name)
        {
            return name.Contains(".") ? name.Substring(name.LastIndexOf(".") + 1) : name;
        }

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            var item = this.FindRows(selectedIds);
            var objIdx = ((PathsToRootDetailTreeViewItem)item[0]).Data.displayObject.GetUnifiedObjectIndex(m_CachedSnapshot);
            // invalid index means the no object selected object was selected making it safe to ignore the event.
            if (objIdx == -1)
                return;
            SelectionChangedEvt(new MemorySampleSelection(m_UIStateHolder.UIState, objIdx, item[0].id, m_CachedSnapshot));
        }

        long GetItemFromId(int first)
        {
            if (m_RootDetail.children[0].id == first)
            {
                return ((PathsToRootDetailTreeViewItem)m_RootDetail.children[0]).Data.GetUnifiedObjectIndex(m_CachedSnapshot);
            }
            var findIndex = m_RootDetail.children[0].children.FindIndex(x => x.id == first);
            if (findIndex != -1)
            {
                if (m_RootDetail.children[0].children[findIndex] is PathsToRootDetailTreeViewItem)
                {
                    if (m_RootDetail.children[0].children[findIndex] is PathsToRootDetailTreeViewItem ret)
                    {
                        var id = ret.Data.displayObject.GetUnifiedObjectIndex(m_CachedSnapshot);
                        return id;
                    }
                }
            }

            return -1;
        }

        protected override bool CanMultiSelect(TreeViewItem item)
        {
            return false;
        }

        public void OnDisable()
        {
            MemoryProfilerSettings.TruncateStateChanged -= OnTruncateStateChanged;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using Unity.MemoryProfiler.Editor.Diagnostics;
using Unity.MemoryProfiler.Editor.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;
using static Unity.MemoryProfiler.Editor.ExportUtility;

namespace Unity.MemoryProfiler.Editor
{
    /// <summary>
    /// Utility class to help with bigger restructuring of <see cref="TreeViewItemData{T}"/> trees.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    class ModifiableTreeViewItemData<T>
    {
        public int Id;
        public T Data;
        public ModifiableTreeViewItemData<T> Parent;
        public List<ModifiableTreeViewItemData<T>> Children;

        public static ModifiableTreeViewItemData<T> RebuildModifiableTree(TreeViewItemData<T> tree, ModifiableTreeViewItemData<T> parent = null)
        {
            var builtTree = new ModifiableTreeViewItemData<T>(tree.id, tree.data, parent);
            if (tree.hasChildren)
            {
                builtTree.Children = new List<ModifiableTreeViewItemData<T>>();
                foreach (var child in tree.children)
                {
                    builtTree.Children.Add(RebuildModifiableTree(child, builtTree));
                }
            }
            return builtTree;
        }

        public ModifiableTreeViewItemData(int id, T data, ModifiableTreeViewItemData<T> parent, List<ModifiableTreeViewItemData<T>> children = null)
        {
            Id = id;
            Data = data;
            Children = children;
            Parent = parent;
        }

        public ModifiableTreeViewItemData(ModifiableTreeViewItemData<T> copyFrom, ModifiableTreeViewItemData<T> parent, bool copyChildren)
            : this(copyFrom.Id, copyFrom.Data, parent, copyChildren ? copyFrom.Children : null)
        { }

        public static TreeViewItemData<T> BuildReadonlyTree(ModifiableTreeViewItemData<T> tree)
        {
            int unusedId = 0;
            return BuildReadonlyTree(tree, ref unusedId, false);
        }
        public static TreeViewItemData<T> BuildReadonlyTree(ModifiableTreeViewItemData<T> tree, ref int currentId, bool generateUniqueIds)
        {
            var builtTree = new TreeViewItemData<T>(generateUniqueIds ? currentId++ : tree.Id, tree.Data,
                children: tree.Children != null ? BuildReadonlyTreeChildList(tree, ref currentId, generateUniqueIds) : null);
            return builtTree;
        }

        static List<TreeViewItemData<T>> BuildReadonlyTreeChildList(ModifiableTreeViewItemData<T> tree, ref int currentId, bool generateUniqueIds)
        {
            var builtList = new List<TreeViewItemData<T>>(tree.Children.Count);
            for (int i = 0; i < tree.Children.Count; i++)
            {
                builtList.Add(BuildReadonlyTree(tree.Children[i], ref currentId, generateUniqueIds));
            }
            return builtList;
        }

        public ModifiableTreeViewItemData<T> FindChildById(int id)
        {
            var stack = new Stack<(ModifiableTreeViewItemData<T>, int)>();
            stack.Push(new(this, 0));
            while (stack.Count > 0)
            {
                var currentElement = stack.Pop();
                var childCount = currentElement.Item1.Children?.Count ?? 0;
                if (currentElement.Item2 < childCount)
                {
                    var currentChild = currentElement.Item1.Children[currentElement.Item2];
                    if (currentChild.Id == id)
                        return currentChild;
                    var grandchildCount = currentChild.Children?.Count ?? 0;

                    ++currentElement.Item2;
                    stack.Push(currentElement);
                    if (grandchildCount > 0)
                    {
                        stack.Push(new(currentChild, 0));
                    }
                }
            }
            return null;
        }
    }

    class CallstacksTreeWindow : EditorWindow
    {
        const string k_ViewDataKey = "com.Unity.MemoryProfiler.CallStacksTreeWindow.m_TreeView";
        [Serializable]
        public struct SymbolTreeViewItemData
        {
            public StringBuilder CallstackEntry;
            public SourceIndex ItemIndex;
            public ulong Size;
            public int AreaId;
            public string AreaName;
        }

        MultiColumnTreeView m_TreeView;
        Button m_SaveMappingButton;
        Button m_RebuildButton;

        CallstackSymbolNode m_CallstackRootNode;
        TreeViewItemData<SymbolTreeViewItemData> m_BaseModel;
        TreeViewItemData<SymbolTreeViewItemData> m_Model;
        bool m_UsesSplitModel;
        bool m_UsesInvertedCallstacks;
        bool m_MergeSingleEntryBranches;
        bool m_CallstackWindowOwnsSnapshot;
        CachedSnapshot m_CachedSnapshot;
        ICallstackMapping m_MappingInfo;
        List<string> m_MappedAreas;

        Dictionary<VisualElement, EventCallback<ChangeEvent<string>>> m_ExtraEventCallbacks = new Dictionary<VisualElement, EventCallback<ChangeEvent<string>>>();
        string[] m_ColumnNames = new string[]
        {
            "call-stacks-table__call-stack__column",
            "call-stacks-table__item-index__column",
            "call-stacks-table__address-or-field__column",
            "call-stacks-table__size__column",
            "call-stacks-table__mapping__column"
        };
        Dictionary<string, ColumnId> m_ColumnNameToId;

        enum ColumnId
        {
            CallStack = 0,
            ItemIndex,
            AddressOrField,
            Size,
            Mapping,
        }

        void OnEnable()
        {
            if (m_TreeView != null)
                return;

            m_ColumnNameToId = new Dictionary<string, ColumnId>(m_ColumnNames.Length);
            for (int i = 0; i < m_ColumnNames.Length; i++)
            {
                m_ColumnNameToId.Add(m_ColumnNames[i], (ColumnId)i);
            }

            m_TreeView = new MultiColumnTreeView(new Columns
            {
                new Column() { title = "Call Stack", name = m_ColumnNames[(int)ColumnId.CallStack], sortable = false, optional = false, bindCell = BindCellForCallstackColumn(), stretchable = true },
                new Column() { title = "Item Index", name = m_ColumnNames[(int)ColumnId.ItemIndex],  sortable = true, optional = true, bindCell = BindCellForItemIndexColumn(), minWidth = 50 },
                new Column() { title = "Address or Field", name = m_ColumnNames[(int)ColumnId.AddressOrField],  sortable = true, optional = true, bindCell = BindCellForItemAddressOrFieldColumn(), minWidth = 50 },
                new Column() { title = "Size", name = m_ColumnNames[(int)ColumnId.Size], sortable = true, optional = false, bindCell = BindCellForSizeColumn(), minWidth = 65 },
                new Column() { title = "Mapping", name = m_ColumnNames[(int)ColumnId.Mapping], sortable = true, optional = false, makeCell = MakeCellForMappingColumn(), bindCell = BindCellForMappingColumn(), unbindCell = UnbindCellForMappingColumn (), minWidth = 100, stretchable = true }
            });
            m_TreeView.columnSortingChanged += ColumnSortingChanged;

            m_TreeView.SetCustomSortModeEnabled(true);
            rootVisualElement.Add(m_TreeView);
            m_TreeView.SetRootItems(m_Model.children as IList<TreeViewItemData<SymbolTreeViewItemData>>);
            m_TreeView.viewDataKey = k_ViewDataKey;
            m_ExtraEventCallbacks.Clear();

            m_SaveMappingButton = new Button(SaveMapping) { text = "Save Mapping" };
            rootVisualElement.Add(m_SaveMappingButton);
            m_RebuildButton = new Button(RebuildTree) { text = "Rebuild Tree" };
            rootVisualElement.Add(m_RebuildButton);

            var exportButton = new Button(ExportToJson) { text = "Export Tree to Json" };
            rootVisualElement.Add(exportButton);
        }

        void OnDestroy()
        {
            if (m_CallstackWindowOwnsSnapshot && m_CachedSnapshot != null)
                m_CachedSnapshot.Dispose();
            m_CachedSnapshot = null;
            m_CallstackWindowOwnsSnapshot = false;
            if (m_TreeView != null)
                m_TreeView.columnSortingChanged -= ColumnSortingChanged;
        }

        void ColumnSortingChanged()
        {
            UpdateRoot(SortTree());
        }

        void SaveMapping()
        {
            if (m_UsesSplitModel)
            {
                m_MappingInfo?.SaveMapping();
            }
        }

        void RebuildTree()
        {
            if (m_UsesSplitModel)
            {
                // Clear non explicit mappings, the'll be rebuild based on the explicit entries during the remapping process
                m_MappingInfo.ClearNonExplicitMappings();
                m_TreeView.Clear();
                SetRoot(m_CachedSnapshot, m_MappingInfo, m_UsesInvertedCallstacks, m_CallstackRootNode, m_MergeSingleEntryBranches, m_UsesSplitModel);
            }
        }

        void ExportToJson()
        {
            ExportUtility.WriteTreeToJson(m_CachedSnapshot, m_Model, false);
        }

        TreeViewItemData<SymbolTreeViewItemData> SortTree()
        {
            var updatedModel = ModifiableTreeViewItemData<SymbolTreeViewItemData>.RebuildModifiableTree(m_Model);
            return SortTree(updatedModel);
        }

        TreeViewItemData<SymbolTreeViewItemData> SortTree(ModifiableTreeViewItemData<SymbolTreeViewItemData> updatedModel)
        {
            var comparisonOperation = GetColumnComparisonModifiableTree(m_TreeView, m_ColumnNameToId, m_CachedSnapshot, out var sortCount);
            if (sortCount > 0)
            {
                var stack = new Stack<(ModifiableTreeViewItemData<SymbolTreeViewItemData>, int)>();
                stack.Push((updatedModel, 0));
                while (stack.Count > 0)
                {
                    var scope = stack.Pop();
                    if (scope.Item1.Children?.Count > scope.Item2)
                    {
                        // only sort children the first time around
                        if (scope.Item2 == 0)
                            scope.Item1.Children.Sort(comparisonOperation);

                        var nextChild = scope.Item1.Children[scope.Item2++];
                        stack.Push(scope);
                        stack.Push((nextChild, 0));
                    }
                }
            }
            return ModifiableTreeViewItemData<SymbolTreeViewItemData>.BuildReadonlyTree(updatedModel);
        }


        static Comparison<SymbolTreeViewItemData> GetColumnComparer(CachedSnapshot snapshot, ColumnId columnId, SortDirection sortDirection)
        {
            var sign = (sortDirection == SortDirection.Descending ? -1 : 1);
            return columnId switch
            {
                ColumnId.CallStack => (SymbolTreeViewItemData a, SymbolTreeViewItemData b) => string.CompareOrdinal(a.CallstackEntry.ToString(), b.CallstackEntry.ToString()) * sign,
                ColumnId.ItemIndex => (SymbolTreeViewItemData a, SymbolTreeViewItemData b) => ((IComparable<SourceIndex>)a.ItemIndex).CompareTo(b.ItemIndex) * sign,
                ColumnId.AddressOrField => (SymbolTreeViewItemData a, SymbolTreeViewItemData b) => ProduceAddressOrFieldName(a.ItemIndex, snapshot).CompareTo(ProduceAddressOrFieldName(b.ItemIndex, snapshot)) * sign,
                ColumnId.Size => (SymbolTreeViewItemData a, SymbolTreeViewItemData b) => a.Size.CompareTo(b.Size) * sign,
                ColumnId.Mapping => (SymbolTreeViewItemData a, SymbolTreeViewItemData b) => a.AreaId.CompareTo(b.AreaId) * sign,
                _ => (SymbolTreeViewItemData a, SymbolTreeViewItemData b) => 0
            };
        }

        static List<Comparison<SymbolTreeViewItemData>> GetComparisons(MultiColumnTreeView treeView, Dictionary<string, ColumnId> columnNameToId, CachedSnapshot snapshot, out int sortCount)
        {
            var sortColums = treeView.sortedColumns;
            var comparisons = new List<Comparison<SymbolTreeViewItemData>>();
            sortCount = 0;
            foreach (var sortColumn in treeView.sortedColumns)
            {
                if (!columnNameToId.TryGetValue(sortColumn.columnName, out var columnId))
                    columnId = (ColumnId)sortColumn.columnIndex;
                comparisons.Add(GetColumnComparer(snapshot, columnId, sortColumn.direction));
                ++sortCount;
            }
            return comparisons;
        }

        static Comparison<ModifiableTreeViewItemData<SymbolTreeViewItemData>> GetColumnComparisonModifiableTree(MultiColumnTreeView treeView, Dictionary<string, ColumnId> columnNameToId, CachedSnapshot snapshot, out int sortCount)
        {
            var comparisons = GetComparisons(treeView, columnNameToId, snapshot, out sortCount);
            var comparisonOperation = new Comparison<ModifiableTreeViewItemData<SymbolTreeViewItemData>>((a, b) =>
            {
                int result = 0;
                foreach (var comparison in comparisons)
                {
                    result = comparison.Invoke(a.Data, b.Data);
                    if (result == 0)
                        break;
                }
                return result;
            });
            return comparisonOperation;
        }

        static Comparison<TreeViewItemData<SymbolTreeViewItemData>> GetColumnComparison(MultiColumnTreeView treeView, Dictionary<string, ColumnId> columnNameToId, CachedSnapshot snapshot, out int sortCount)
        {
            var comparisons = GetComparisons(treeView, columnNameToId, snapshot, out sortCount);
            var comparisonOperation = new Comparison<TreeViewItemData<SymbolTreeViewItemData>>((a, b) =>
            {
                int result = 0;
                foreach (var comparison in comparisons)
                {
                    result = comparison.Invoke(a.data, b.data);
                    if (result == 0)
                        break;
                }
                return result;
            });
            return comparisonOperation;
        }

        Action<VisualElement, int> BindCellForCallstackColumn()
        {
            const string k_NoName = "<Unknown>";
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<CallstacksTreeWindow.SymbolTreeViewItemData>(rowIndex);

                var displayText = itemData.CallstackEntry?.ToString() ?? "null";
                if (string.IsNullOrEmpty(displayText))
                    displayText = k_NoName;
                ((Label)element).text = displayText;
            };
        }
        Action<VisualElement, int> BindCellForItemIndexColumn()
        {
            const string k_NoValue = "";
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<CallstacksTreeWindow.SymbolTreeViewItemData>(rowIndex);

                var displayText = itemData.ItemIndex.Valid ? itemData.ItemIndex.ToString() : k_NoValue;
                ((Label)element).text = displayText;
            };
        }

        Action<VisualElement, int> BindCellForItemAddressOrFieldColumn()
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<CallstacksTreeWindow.SymbolTreeViewItemData>(rowIndex);

                var displayText = ProduceAddressOrFieldName(itemData.ItemIndex, m_CachedSnapshot);
                ((Label)element).text = displayText;
            };
        }

        static string ProduceAddressOrFieldName(SourceIndex sourceIndex, CachedSnapshot cachedSnapshot)
        {
            const string k_NoValue = "";
            return sourceIndex.Valid && sourceIndex.Id == SourceIndex.SourceId.NativeAllocation ?
                NativeAllocationTools.ProduceNativeAllocationName(sourceIndex, cachedSnapshot, MemoryProfilerSettings.MemorySnapshotTruncateTypes) : k_NoValue;
        }

        Action<VisualElement, int> BindCellForSizeColumn()
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<CallstacksTreeWindow.SymbolTreeViewItemData>(rowIndex);

                var displayText = EditorUtility.FormatBytes((long)itemData.Size);
                var label = ((Label)element);
                label.text = displayText;
                label.tooltip = itemData.Size.ToString();
            };
        }

        Action<VisualElement, int> BindCellForMappingColumn()
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<CallstacksTreeWindow.SymbolTreeViewItemData>(rowIndex);
                var itemId = m_TreeView.GetIdForIndex(rowIndex);

                var dropdown = ((DropdownField)element);

                var callstackText = itemData.CallstackEntry.ToString();

                if (callstackText == "Root" || callstackText == itemData.AreaName)
                {
                    dropdown.SetEnabled(false);
                }
                else
                    dropdown.SetEnabled(true);

                if (m_ExtraEventCallbacks.TryGetValue(element, out var oldCallback))
                {
                    dropdown.UnregisterValueChangedCallback(oldCallback);
                    m_ExtraEventCallbacks.Remove(element);
                }

                if (dropdown.choices == null || dropdown.choices.Count == 0)
                    dropdown.choices = m_MappedAreas;

                var areaIndex = itemData.AreaId != ExportUtility.InvalidMappedAreaId ? itemData.AreaId : m_MappedAreas.Count - 1;
                var areaName = m_MappedAreas[areaIndex];
                dropdown.SetValueWithoutNotify(areaName);

                void callback(ChangeEvent<string> evt)
                {
                    AreaChanged(evt.newValue, rowIndex, itemId);
                }
                dropdown.RegisterValueChangedCallback(callback);
                m_ExtraEventCallbacks.Add(element, callback);
            };
        }
        Action<VisualElement, int> UnbindCellForMappingColumn()
        {
            return (element, rowIndex) =>
            {
                var itemId = m_TreeView.GetIdForIndex(rowIndex);

                var dropdown = ((DropdownField)element);
                if (m_ExtraEventCallbacks.TryGetValue(element, out var callback))
                {
                    dropdown.UnregisterValueChangedCallback(callback);
                    m_ExtraEventCallbacks.Remove(element);
                }
            };
        }

        Func<VisualElement> MakeCellForMappingColumn()
        {
            return () =>
            {
                var dropdown = new DropdownField();
                return dropdown;
            };
        }

        void AreaChanged(string areaName, int rowIndex, int treeItemId)
        {
            int areaId = 0;
            for (areaId = 0; areaId < m_MappedAreas.Count; areaId++)
            {
                if (m_MappedAreas[areaId] == areaName)
                    break;
            }

            var itemData = m_TreeView.GetItemDataForIndex<CallstacksTreeWindow.SymbolTreeViewItemData>(rowIndex);

            if (m_MappingInfo?.UpdateMapping(itemData.CallstackEntry.ToString(), areaId) ?? true)
            {
                var updatedModel = ModifiableTreeViewItemData<CallstacksTreeWindow.SymbolTreeViewItemData>.RebuildModifiableTree(m_Model);
                var id = m_TreeView.GetIdForIndex(rowIndex);
                var childToUpdate = updatedModel.FindChildById(id);
                if (childToUpdate != null)
                {
                    childToUpdate.Data.AreaName = areaName;
                    childToUpdate.Data.AreaId = areaId;

                    var builtUpdatedModel = SortTree(updatedModel);
                    UpdateRoot(builtUpdatedModel);
                }
            }
            else
            {
                m_TreeView?.Rebuild();
            }
        }

        void UpdateRoot(TreeViewItemData<SymbolTreeViewItemData> updatedModel)
        {
            m_Model = updatedModel;
            m_TreeView?.SetRootItems(m_Model.children as IList<TreeViewItemData<SymbolTreeViewItemData>>);
            m_TreeView?.Rebuild();
        }

        float m_CurrentProgressTotalStepCount;
        float m_CurrentProgressStep;

        public void SetRoot(CachedSnapshot snapshot, ICallstackMapping mappingInfo, bool usesInvertedCallstacks, CallstackSymbolNode root, bool mergeSingleEntryBranches = false, bool splitModelByArea = false, bool callstackWindowOwnsSnapshot = false)
        {
            m_CallstackRootNode = root;
            m_MergeSingleEntryBranches = mergeSingleEntryBranches;

            ProgressBarDisplay.ShowBar("Opening Callstack Window");

            var comparisonOperation = GetColumnComparison(m_TreeView, m_ColumnNameToId, m_CachedSnapshot, out var sortCount);
            if (sortCount == 0)
                comparisonOperation = null; // don't spend time sorting if there is nothing to sort.
            if (splitModelByArea)
            {
                m_UsesSplitModel = true;
                m_CurrentProgressTotalStepCount = k_SetRootStepCount + 2;
                m_CurrentProgressStep = 0;
                ProgressBarDisplay.UpdateProgress(m_CurrentProgressStep++ / m_CurrentProgressTotalStepCount, "Generate TreeView Model");
                m_Model = GenerateTreeViewModel(snapshot, root, mappingInfo, mergeSingleEntryBranches, comparisonOperation);
                ProgressBarDisplay.UpdateProgress(m_CurrentProgressStep++ / m_CurrentProgressTotalStepCount, "Splitting Model by Area ID");
                var splitModel = SplitModelByAreaId(m_Model, mappingInfo, !usesInvertedCallstacks);
                SetRoot(snapshot, mappingInfo, usesInvertedCallstacks, m_Model, splitModel, mergeSingleEntryBranches: mergeSingleEntryBranches, callstackWindowOwnsSnapshot: callstackWindowOwnsSnapshot);
            }
            else
            {
                m_CurrentProgressTotalStepCount = k_SetRootStepCount + 1;
                ProgressBarDisplay.UpdateProgress(m_CurrentProgressStep++ / m_CurrentProgressTotalStepCount, "Generate TreeView Model");
                m_Model = GenerateTreeViewModel(snapshot, root, null, mergeSingleEntryBranches, comparisonOperation);
                SetRoot(snapshot, mappingInfo, usesInvertedCallstacks, m_Model, m_Model, mergeSingleEntryBranches: mergeSingleEntryBranches, callstackWindowOwnsSnapshot: callstackWindowOwnsSnapshot);
            }
        }

        const float k_SetRootStepCount = 3;
        void SetRoot(CachedSnapshot snapshot, ICallstackMapping mappingInfo, bool usesInvertedCallstacks, TreeViewItemData<SymbolTreeViewItemData> model, TreeViewItemData<SymbolTreeViewItemData> splitModel, bool mergeSingleEntryBranches = false, bool callstackWindowOwnsSnapshot = false)
        {
            if (m_CurrentProgressStep == 0)
            {
                ProgressBarDisplay.ShowBar("Opening Callstack Window");
                m_CurrentProgressTotalStepCount = k_SetRootStepCount;
            }

            m_BaseModel = model;
            m_UsesInvertedCallstacks = usesInvertedCallstacks;
            // TreeViewItemData<T> doesn't implement equality checks but comparing the child lists to each other is effectively the same a a refference equality check
            m_UsesSplitModel = mappingInfo != null && splitModel.children != null && splitModel.children != model.children;
            m_Model = m_UsesSplitModel ? splitModel : model;
            m_MappedAreas = mappingInfo?.GetMappedAreas();
            m_MappedAreas.Add("Unknown");
            m_MappingInfo = mappingInfo;

            if (m_CallstackWindowOwnsSnapshot && m_CachedSnapshot != null && m_CachedSnapshot != snapshot)
                m_CachedSnapshot.Dispose();
            m_CachedSnapshot = snapshot;
            m_CallstackWindowOwnsSnapshot = callstackWindowOwnsSnapshot;
            ProgressBarDisplay.UpdateProgress(m_CurrentProgressStep++ / m_CurrentProgressTotalStepCount, "Populating Tree with root entries");
            m_TreeView?.SetRootItems(m_Model.children as IList<TreeViewItemData<SymbolTreeViewItemData>>);
            if (m_SaveMappingButton != null)
                UIElementsHelper.SetVisibility(m_SaveMappingButton, m_UsesSplitModel);
            if (m_RebuildButton != null)
                UIElementsHelper.SetVisibility(m_RebuildButton, m_UsesSplitModel);
            ProgressBarDisplay.UpdateProgress(m_CurrentProgressStep++ / m_CurrentProgressTotalStepCount, "Rebuilding Tree UI");
            m_TreeView?.Rebuild();
            ProgressBarDisplay.ClearBar();
        }

        struct BranchLevelData
        {
            public List<TreeViewItemData<SymbolTreeViewItemData>> CurrentLevel;
            public ulong CurrentBranchSize;
            public ulong CurrentSymbol;
            public PartialCallstackSymbolsRef<ulong> CurrentSymbolChain;
        }

        const int k_RootIndex = -1;

        public static TreeViewItemData<SymbolTreeViewItemData> GenerateTreeViewModel(CachedSnapshot snapshot, CallstackSymbolNode root, ICallstackMapping callstackMapping, bool mergeSingleEntryBranches = false, Comparison<TreeViewItemData<SymbolTreeViewItemData>> sortComparison = null)
        {
            var accumulatedBranches = new Dictionary<PartialCallstackSymbolsRef<ulong>, BranchLevelData>();
            BranchLevelData currentBranch = new()
            {
                CurrentLevel = new List<TreeViewItemData<SymbolTreeViewItemData>>(),
                CurrentBranchSize = 0UL,
                CurrentSymbol = 0UL,
                CurrentSymbolChain = default
            };
            foreach (var item in CallstacksUtility.WalkCallstackNodes(snapshot, root, yieldCombinedCallstack: false, yieldPerCallstackLine: true, yieldCallstackLinesAfterItems: true))
            {
                switch (item.ItemIndex.Id)
                {
                    case SourceIndex.SourceId.None:
                        Checks.Equals(currentBranch.CurrentSymbol, item.Symbol);
                        if (currentBranch.CurrentSymbol != item.Symbol)
                        {
                            ParkBranchForLaterAndRetrievePreviouslyParkedBranch(ref currentBranch, accumulatedBranches, item.ParentSymbolsChain, item.Symbol);
                        }
                        // when mergeSingleEntryBranches is true, avoid tree steps with 1 child only and merge them instead
                        var mergeWithPreviousLevel = mergeSingleEntryBranches && (currentBranch.CurrentLevel.Count == 1 && currentBranch.CurrentLevel[0].data.CallstackEntry != null);

                        var areaId = ExportUtility.InvalidMappedAreaId;
                        var areaName = ExportUtility.InvalidMappedAreaName;
                        if (callstackMapping != null)
                        {
                            areaId = GetCallstackArea(out areaName, callstackMapping, item.Callstack);
                        }

                        var childList = mergeWithPreviousLevel ? currentBranch.CurrentLevel[0].children as List<TreeViewItemData<SymbolTreeViewItemData>> : currentBranch.CurrentLevel;
                        if (sortComparison != null)
                            childList.Sort(sortComparison);

                        var finalizedTreeViewItem = new TreeViewItemData<SymbolTreeViewItemData>(HashCode.Combine(item.ItemIndex, item.Symbol, item.ParentSymbolsChain), new SymbolTreeViewItemData
                        {
                            Size = currentBranch.CurrentBranchSize,
                            CallstackEntry = mergeWithPreviousLevel ? new StringBuilder(item.Callstack.ToString() + currentBranch.CurrentLevel[0].data.CallstackEntry) : item.Callstack,
                            AreaId = areaId,
                            AreaName = areaName,
                        }, childList);


                        var branchSize = currentBranch.CurrentBranchSize;
                        RetrievePreviouslyParkedBranchOrCreateNewBranchLevel(ref currentBranch, accumulatedBranches, item.ParentSymbolsChain.GetParentSymbolChain(), item.ParentSymbol);
                        currentBranch.CurrentLevel.Add(finalizedTreeViewItem);
                        currentBranch.CurrentBranchSize += branchSize;
                        break;
                    case SourceIndex.SourceId.SystemMemoryRegion:
                        break;
                    case SourceIndex.SourceId.NativeMemoryRegion:
                        break;
                    case SourceIndex.SourceId.NativeAllocation:
                        if (currentBranch.CurrentSymbol != item.Symbol)
                        {
                            ParkBranchForLaterAndRetrievePreviouslyParkedBranch(ref currentBranch, accumulatedBranches, item.ParentSymbolsChain, item.Symbol);
                        }
                        var size = snapshot.NativeAllocations.Size[item.ItemIndex.Index];
                        currentBranch.CurrentBranchSize += size;
                        currentBranch.CurrentLevel.Add(new TreeViewItemData<SymbolTreeViewItemData>(HashCode.Combine(item.ItemIndex, item.Symbol, item.ParentSymbolsChain), new SymbolTreeViewItemData
                        {
                            ItemIndex = item.ItemIndex,
                            Size = size,
                            CallstackEntry = item.Callstack,
                            AreaId = ExportUtility.InvalidMappedAreaId,
                            AreaName = ExportUtility.InvalidMappedAreaName,
                        }, null));
                        break;
                    case SourceIndex.SourceId.ManagedHeapSection:
                        break;
                    case SourceIndex.SourceId.NativeObject:
                        break;
                    case SourceIndex.SourceId.ManagedObject:
                        break;
                    case SourceIndex.SourceId.NativeType:
                        break;
                    case SourceIndex.SourceId.ManagedType:
                        break;
                    case SourceIndex.SourceId.NativeRootReference:
                        break;
                    case SourceIndex.SourceId.GfxResource:
                        break;
                    default:
                        break;
                }
            }
            Debug.Assert(accumulatedBranches.Count == 0);
            // if the above fails, uncomment the block below for debugging
            //// add up all accumulated branches
            //var invalidSourceIndex = default(SourceIndex);
            //foreach (var item in accumulatedBranches.Values)
            //{
            //    var mergeWithPreviousLevel = mergeSingleEntryBranches && (item.CurrentLevel.Count == 1 && item.CurrentLevel[0].data.CallstackEntry != null);
            //    var finalizedTreeViewItem = new TreeViewItemData<SymbolItemData>(HashCode.Combine(invalidSourceIndex, item.CurrentSymbol, item.CurrentSymbolChain), new SymbolItemData
            //    {
            //        Size = item.CurrentBranchSize,
            //        CallstackEntry = new StringBuilder(mergeWithPreviousLevel ? item.CurrentLevel[0].data.CallstackEntry.ToString() : $"Symbol: {item.CurrentSymbol.ToString()} Depth: {item.CurrentSymbolChain.Depth} Parent: {(item.CurrentSymbolChain.Depth > 0 ? item.CurrentSymbolChain[item.CurrentSymbolChain.Depth-1].ToString() : "")}"),
            //    }, mergeWithPreviousLevel ? item.CurrentLevel[0].children as List<TreeViewItemData<SymbolItemData>> : item.CurrentLevel);

            //    currentBranch.CurrentLevel.Add(finalizedTreeViewItem);
            //}
            if (sortComparison != null)
                currentBranch.CurrentLevel.Sort(sortComparison);

            return new TreeViewItemData<SymbolTreeViewItemData>(k_RootIndex,
                new SymbolTreeViewItemData { Size = currentBranch.CurrentBranchSize, CallstackEntry = new StringBuilder("Root"), ItemIndex = default },
                children: currentBranch.CurrentLevel);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="model"></param>
        /// <param name="callstackAreaMapping"></param>
        /// <param name="takeFirstId">true if the most specific ID should be used and the tree was not generated in reverse.</param>
        /// <returns></returns>
        public static TreeViewItemData<SymbolTreeViewItemData> SplitModelByAreaId(TreeViewItemData<SymbolTreeViewItemData> model, ICallstackMapping callstackAreaMapping, bool takeFirstId = true)
        {
            var mappedAreas = callstackAreaMapping?.GetMappedAreas();
            var mappedAreaCount = (mappedAreas?.Count ?? 0) + 1;
            if (mappedAreaCount == 1)
                return model;

            var currentId = model.id - 1;
            var allAreasRoot = new ModifiableTreeViewItemData<SymbolTreeViewItemData>(id: k_RootIndex,
                data: new SymbolTreeViewItemData { Size = model.data.Size, CallstackEntry = new StringBuilder("AllAreasRoot") }, parent: null);
            allAreasRoot.Children = new List<ModifiableTreeViewItemData<SymbolTreeViewItemData>>(mappedAreaCount);
            var unknownAreaIndex = mappedAreaCount - 1;
            for (int i = 0; i < unknownAreaIndex; i++)
            {
                var areaName = mappedAreas[i];
                allAreasRoot.Children.Add(new ModifiableTreeViewItemData<SymbolTreeViewItemData>(id: currentId--,
                    data: new SymbolTreeViewItemData() { AreaId = i, AreaName = areaName, CallstackEntry = new StringBuilder(areaName) },
                    parent: allAreasRoot));
            }
            allAreasRoot.Children.Add(new ModifiableTreeViewItemData<SymbolTreeViewItemData>(id: currentId--,
                data: new SymbolTreeViewItemData() { AreaId = ExportUtility.InvalidMappedAreaId, AreaName = "Unknown Area", CallstackEntry = new StringBuilder("Unknown Area") },
                parent: allAreasRoot));
            //var allAreasRoot = new TreeViewItemData<SymbolTreeViewItemData>(model.id - 1, new SymbolTreeViewItemData() { Size = model.data.Size, CallstackEntry = new StringBuilder("AllAreasRoot")}, ;
            allAreasRoot.Children[unknownAreaIndex].Children = new List<ModifiableTreeViewItemData<SymbolTreeViewItemData>> { ModifiableTreeViewItemData<SymbolTreeViewItemData>.RebuildModifiableTree(model) };
            var unkownAreaRoot = allAreasRoot.Children[unknownAreaIndex].Children[0];

            var cachedListForMoving = new List<ModifiableTreeViewItemData<SymbolTreeViewItemData>>();
            var stackOfNodes = new Stack<(int, int, bool, ModifiableTreeViewItemData<SymbolTreeViewItemData>)>();
            stackOfNodes.Push((0, ExportUtility.InvalidMappedAreaId, false, unkownAreaRoot));
            while (stackOfNodes.Count > 0)
            {
                var currentNode = stackOfNodes.Pop();
                if (currentNode.Item4.Children?.Count > currentNode.Item1)
                {
                    var currentChild = currentNode.Item4.Children[currentNode.Item1];

                    var childHasParentWithArea = false;
                    var childWasMoved = false;
                    if (currentChild.Data.AreaId != ExportUtility.InvalidMappedAreaId
                        // Some Areas should be ignored
                        && !callstackAreaMapping.IsAreaIgnored(currentChild.Data.AreaId))
                    {
                        if (takeFirstId)
                        {
                            MoveBranch(currentNode.Item4, currentNode.Item1, allAreasRoot.Children[currentChild.Data.AreaId], cachedListForMoving);
                            // subtract here so that the later increment doesn't lead to skipped siblings
                            --currentNode.Item1;
                            childWasMoved = true;
                        }
                    }
                    else if (currentNode.Item2 != ExportUtility.InvalidMappedAreaId
                        // Some Areas should be ignored
                        && !callstackAreaMapping.IsAreaIgnored(currentNode.Item2))
                    {
                        if (!takeFirstId)
                        {
                            childHasParentWithArea = true;
                        }
                    }
                    else if (!takeFirstId)
                    {
                        childHasParentWithArea = currentNode.Item3;
                    }

                    ++currentNode.Item1;
                    stackOfNodes.Push(currentNode);
                    if (!childWasMoved)
                    {
                        var grandChildCount = currentChild.Children?.Count ?? 0;
                        if (grandChildCount > 0
                            // when not taking the first id, leaves have to be processed as well
                            || !takeFirstId)
                        {
                            stackOfNodes.Push((0, childHasParentWithArea ? currentNode.Item2 : currentChild.Data.AreaId, childHasParentWithArea, currentChild));
                        }
                    }
                }
                else if (!takeFirstId && currentNode.Item4.Data.ItemIndex.Valid)
                {
                    // We reached a leaf and are supposed to move based on the last Area ID found.
                    // Check if an id was found in a parent or if the leaf has one
                    if (currentNode.Item3 || (currentNode.Item4.Data.AreaId != ExportUtility.InvalidMappedAreaId
                        // Some Areas should be ignored
                        && !callstackAreaMapping.IsAreaIgnored(currentNode.Item4.Data.AreaId)))
                    {
                        // iterate up the stack to the last item with an id
                        var branchToMove = currentNode;
                        var movedAtLeastOneBranch = false;
                        while (branchToMove.Item4.Data.AreaId == ExportUtility.InvalidMappedAreaId
                            // Some Areas should be ignored
                            || callstackAreaMapping.IsAreaIgnored(branchToMove.Item4.Data.AreaId))
                        {
                            var parentBranch = stackOfNodes.Pop();
                            // if the branch has unprocessed siblings, cut here and process the siblings later
                            if (parentBranch.Item4.Children?.Count > parentBranch.Item1)
                            {
                                MoveBranch(parentBranch.Item4, --parentBranch.Item1, allAreasRoot.Children[branchToMove.Item2], cachedListForMoving);
                                stackOfNodes.Push(parentBranch);
                                movedAtLeastOneBranch = true;
                                break;
                            }
                            branchToMove = parentBranch;
                        }
                        if (!movedAtLeastOneBranch)
                        {
                            var parentBranch = stackOfNodes.Pop();
                            MoveBranch(parentBranch.Item4, --parentBranch.Item1, allAreasRoot.Children[branchToMove.Item2], cachedListForMoving);
                            stackOfNodes.Push(parentBranch);
                        }
                    }
                }
            }
            // Update Unkown Area root size
            allAreasRoot.Children[unknownAreaIndex].Data.Size = unkownAreaRoot.Data.Size;
            CleanupZeroSizedBranchesOfTree(unkownAreaRoot);
            int regeneratedId = allAreasRoot.Id;
            return ModifiableTreeViewItemData<SymbolTreeViewItemData>.BuildReadonlyTree(allAreasRoot, ref regeneratedId, generateUniqueIds: true);
        }

        static void MoveBranch(ModifiableTreeViewItemData<SymbolTreeViewItemData> sourceParent, int sourceChildIndex,
            ModifiableTreeViewItemData<SymbolTreeViewItemData> destinationRoot, List<ModifiableTreeViewItemData<SymbolTreeViewItemData>> cachedParentChainContainer)
        {
            var childNodeToMove = sourceParent.Children[sourceChildIndex];
            sourceParent.Children.RemoveAt(sourceChildIndex);
            var movedBranchSze = childNodeToMove.Data.Size;
            // add the size to the new root
            destinationRoot.Data.Size += movedBranchSze;

            cachedParentChainContainer.Clear();
            var currentParent = sourceParent;
            while (currentParent != null)
            {
                cachedParentChainContainer.Add(currentParent);
                currentParent = currentParent.Parent;
            }

            for (int i = cachedParentChainContainer.Count - 1; i >= 0; i--)
            {
                var currentParentToTransfer = cachedParentChainContainer[i];

                // ensure the child list exists
                destinationRoot.Children ??= new List<ModifiableTreeViewItemData<SymbolTreeViewItemData>>();

                var destinationChildIndex = 0;
                for (destinationChildIndex = 0; destinationChildIndex < destinationRoot.Children.Count; destinationChildIndex++)
                {
                    if (destinationRoot.Children[destinationChildIndex].Id == currentParentToTransfer.Id)
                        break;
                }
                if (destinationChildIndex >= destinationRoot.Children.Count)
                {
                    var newChild = new ModifiableTreeViewItemData<SymbolTreeViewItemData>(currentParentToTransfer, destinationRoot, copyChildren: false);
                    newChild.Data.Size = movedBranchSze;
                    destinationRoot.Children.Add(newChild);
                    destinationRoot = newChild;
                }
                else
                {
                    destinationRoot = destinationRoot.Children[destinationChildIndex];
                    destinationRoot.Data.Size += movedBranchSze;
                }
                // transfere the branch size
                currentParentToTransfer.Data.Size -= movedBranchSze;
            }
            // ensure the child list exists for the last parent as well
            destinationRoot.Children ??= new List<ModifiableTreeViewItemData<SymbolTreeViewItemData>>();

            destinationRoot.Children.Add(childNodeToMove);
        }

        public static int GetCallstackArea(out string areaName, ICallstackMapping callstackAreaMapping, StringBuilder callstack)
        {
            areaName = ExportUtility.InvalidMappedAreaName;
            if (callstackAreaMapping == null)
                return ExportUtility.InvalidMappedAreaId;
            if (callstack?.Length > 0)
            {
                return callstackAreaMapping.TryMap(callstack.ToString(), out areaName);
            }
            return ExportUtility.InvalidMappedAreaId;
        }

        static void CleanupZeroSizedBranchesOfTree(ModifiableTreeViewItemData<SymbolTreeViewItemData> tree)
        {
            var stackOfNodes = new Stack<(int, ModifiableTreeViewItemData<SymbolTreeViewItemData>)>();
            stackOfNodes.Push((0, tree));
            while (stackOfNodes.Count > 0)
            {
                var currentNode = stackOfNodes.Pop();
                if (currentNode.Item2.Children?.Count > currentNode.Item1)
                {
                    var currentChild = currentNode.Item2.Children[currentNode.Item1];

                    if (currentChild.Data.Size <= 0)
                    {
                        currentNode.Item2.Children.RemoveAt(currentNode.Item1);
                        stackOfNodes.Push(currentNode);
                    }
                    else
                    {
                        ++currentNode.Item1;
                        stackOfNodes.Push(currentNode);
                        var grandChildCount = currentChild.Children?.Count ?? 0;
                        if (grandChildCount > 0)
                        {
                            stackOfNodes.Push((0, currentChild));
                        }
                    }
                }
            }
        }

        static void ParkBranchForLaterAndRetrievePreviouslyParkedBranch(ref BranchLevelData branchToBeParkedAndRetrieved, Dictionary<PartialCallstackSymbolsRef<ulong>, BranchLevelData> accumulatedBranches, PartialCallstackSymbolsRef<ulong> symbolChainOfBranchToBeRetrieved, ulong symbolToBeRetrieved)
        {
            if (branchToBeParkedAndRetrieved.CurrentLevel.Count > 0)
            {
                // pause adding up to the current branch and start a new one
                if (!accumulatedBranches.TryAdd(branchToBeParkedAndRetrieved.CurrentSymbolChain, branchToBeParkedAndRetrieved))
                {
                    // merge them
                    var existingParkedBranch = accumulatedBranches[branchToBeParkedAndRetrieved.CurrentSymbolChain];
                    existingParkedBranch.CurrentLevel.AddRange(branchToBeParkedAndRetrieved.CurrentLevel);
                    existingParkedBranch.CurrentBranchSize += branchToBeParkedAndRetrieved.CurrentBranchSize;
                    accumulatedBranches[branchToBeParkedAndRetrieved.CurrentSymbolChain] = existingParkedBranch;
                }
            }
            RetrievePreviouslyParkedBranchOrCreateNewBranchLevel(ref branchToBeParkedAndRetrieved, accumulatedBranches, symbolChainOfBranchToBeRetrieved, symbolToBeRetrieved);
        }

        static void RetrievePreviouslyParkedBranchOrCreateNewBranchLevel(ref BranchLevelData branchToBeRetrieved, Dictionary<PartialCallstackSymbolsRef<ulong>, BranchLevelData> accumulatedBranches, PartialCallstackSymbolsRef<ulong> symbolChainOfBranchToBeRetrieved, ulong symbolToBeRetrieved)
        {
            if (accumulatedBranches.TryGetValue(symbolChainOfBranchToBeRetrieved, out branchToBeRetrieved))
            {
                accumulatedBranches.Remove(symbolChainOfBranchToBeRetrieved);
            }
            else
            {
                branchToBeRetrieved.CurrentLevel = new List<TreeViewItemData<SymbolTreeViewItemData>>();
                branchToBeRetrieved.CurrentSymbol = symbolToBeRetrieved;
                branchToBeRetrieved.CurrentSymbolChain = symbolChainOfBranchToBeRetrieved;
                branchToBeRetrieved.CurrentBranchSize = 0;
            }
        }
    }
}

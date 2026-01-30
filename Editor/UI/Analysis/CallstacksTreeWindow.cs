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
        const string k_CallstackNull = "null";

        [Serializable]
        public struct SymbolTreeViewItemData
        {
            public StringBuilder CallstackEntry;
            public string MemLabel;
            public SourceIndex ItemIndex;
            public ulong Size;
            public int AreaId;
            public string AreaName;
            public bool ExplicitlyMapped;
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
            "call-stacks-table__mem-label__column",
            "call-stacks-table__mapping__column"
        };
        Dictionary<string, ColumnId> m_ColumnNameToId;

        enum ColumnId
        {
            CallStack = 0,
            ItemIndex,
            AddressOrField,
            Size,
            MemLabel,
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
                new Column() { title = "MemLabel", name = m_ColumnNames[(int)ColumnId.MemLabel], sortable = true, optional = false, bindCell = BindCellForMemLabelColumn(), minWidth = 65, stretchable = true},
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
                ColumnId.MemLabel => (SymbolTreeViewItemData a, SymbolTreeViewItemData b) => string.CompareOrdinal(a.MemLabel, b.MemLabel) * sign,
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

                var displayText = itemData.CallstackEntry?.ToString() ?? k_CallstackNull;
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

        Action<VisualElement, int> BindCellForMemLabelColumn()
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<CallstacksTreeWindow.SymbolTreeViewItemData>(rowIndex);

                var displayText = itemData.MemLabel;
                var label = ((Label)element);
                label.text = displayText;
            };
        }

        Action<VisualElement, int> BindCellForMappingColumn()
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<CallstacksTreeWindow.SymbolTreeViewItemData>(rowIndex);
                var itemId = m_TreeView.GetIdForIndex(rowIndex);

                var dropdown = ((DropdownField)element);

                var callstackText = itemData.CallstackEntry?.ToString() ?? k_CallstackNull;

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

            if (m_MappingInfo?.UpdateMapping(itemData.CallstackEntry?.ToString(), itemData.MemLabel, areaId) ?? true)
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
            m_TreeView?.RefreshItems();
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
                var splitModel = SplitModelByAreaId(m_Model, mappingInfo, usesInvertedCallstacks ? TreeSplittingLogic.TakeLastAreaIdFound : TreeSplittingLogic.TakeFirstAreaIdFound);
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
            m_TreeView?.RefreshItems();
            ProgressBarDisplay.ClearBar();
            // bring the window forward once done
            EditorApplication.delayCall += () => Focus();
        }

        struct BranchLevelData
        {
            public List<TreeViewItemData<SymbolTreeViewItemData>> CurrentLevel;
            public ulong CurrentBranchSize;
            public ulong CurrentSymbol;
            public PartialCallstackSymbolsRef<ulong> CurrentSymbolChain;
        }

        const int k_RootIndex = -1;

        public static TreeViewItemData<SymbolTreeViewItemData> GenerateTreeViewModel(CachedSnapshot snapshot, CallstackSymbolNode root, ICallstackMapping callstackMapping = null, bool mergeSingleEntryBranches = false, Comparison<TreeViewItemData<SymbolTreeViewItemData>> sortComparison = null)
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
                var areaId = ExportUtility.InvalidMappedAreaId;
                var areaName = ExportUtility.InvalidMappedAreaName;
                var explicitlySet = false;
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

                        if (callstackMapping != null)
                        {
                            explicitlySet = GetCallstackArea(out areaName, out areaId, callstackMapping, item.Callstack, item.MemLabel);
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
                            MemLabel = item.MemLabel,
                            ExplicitlyMapped = explicitlySet,
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

                        if (callstackMapping != null)
                        {
                            // The allocation can only be mapped based on the MemLabel
                            explicitlySet = GetCallstackArea(out areaName, out areaId, callstackMapping, null, item.MemLabel);
                        }
                        currentBranch.CurrentLevel.Add(new TreeViewItemData<SymbolTreeViewItemData>(HashCode.Combine(item.ItemIndex, item.Symbol, item.ParentSymbolsChain), new SymbolTreeViewItemData
                        {
                            ItemIndex = item.ItemIndex,
                            Size = size,
                            CallstackEntry = item.Callstack,
                            AreaId = areaId,
                            AreaName = areaName,
                            MemLabel = item.MemLabel,
                            ExplicitlyMapped = explicitlySet,
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


        public enum TreeSplittingLogic
        {
            TakeFirstAreaIdFound,
            TakeLastAreaIdFound,
            OnlyTakeLeafAreaId
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="model"></param>
        /// <param name="callstackAreaMapping"></param>
        /// <param name="mode">true if the most specific ID should be used and the tree was not generated in reverse.</param>
        /// <returns></returns>
        public static TreeViewItemData<SymbolTreeViewItemData> SplitModelByAreaId(TreeViewItemData<SymbolTreeViewItemData> model, ICallstackMapping callstackAreaMapping, TreeSplittingLogic mode = TreeSplittingLogic.TakeFirstAreaIdFound)
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
            allAreasRoot.Children[unknownAreaIndex].Children = new List<ModifiableTreeViewItemData<SymbolTreeViewItemData>> { ModifiableTreeViewItemData<SymbolTreeViewItemData>.RebuildModifiableTree(model) };
            var unknownAreaRoot = allAreasRoot.Children[unknownAreaIndex].Children[0];

            var cachedListForMoving = new List<ModifiableTreeViewItemData<SymbolTreeViewItemData>>();
            var stackOfNodes = new Stack<TreeIteratorNode>();
            stackOfNodes.Push(new TreeIteratorNode(0, ExportUtility.InvalidMappedAreaId, false, unknownAreaRoot));

            // First pass, split by area
            IterateAndMoveBranches(stackOfNodes, cachedListForMoving,
                (areaId, memLabel) => callstackAreaMapping.IsAreaIgnored(areaId),
                mode, allAreasRoot,
                // if we only rely on MemLabels, also use them as fallbacks
                useInexplicitLeafNodeAreaAsFallback: mode is TreeSplittingLogic.OnlyTakeLeafAreaId,
                out var totalMovedSize);

            if (mode is TreeSplittingLogic.TakeFirstAreaIdFound)
            {
                // When the first id is taken, leaf nodes with explicit MemLabel to Area mapping won't be mapped correctly, so do another pass for those.
                // Explicitly ALSO go over the Unknown area, where also non explicit MemLabel to Area mapping will be used
                for (int i = 0; i <= unknownAreaIndex; i++)
                {
                    var currentAreaRoot = allAreasRoot.Children[i];
                    if ((currentAreaRoot.Children?.Count ?? 0) <= 0)
                        continue; // ignore empty branches

                    stackOfNodes.Clear();
                    var isUnknownArea = i == unknownAreaIndex;
                    bool AreaShouldBeIgnored(int areaId, string memLabel)
                    {
                        // no moving within the current area but if there is a differing explicitly mapped memLabel, move it.
                        return (areaId != i && AllocationHasDifferingExplicitlyMappedMemLabel(callstackAreaMapping, areaId, i, memLabel, isUnknownArea))
                        || areaId == i || callstackAreaMapping.IsAreaIgnored(areaId);
                    }

                    // The first (and only) element under the area root, should be the "Root" node.
                    // Since all areas with elements under them have it,
                    // it can be used as part of the mirroring process when moving branches over.
                    Checks.CheckIndexInRangeAndThrow(currentAreaRoot.Children.Count - 1, 1);
                    for (int j = currentAreaRoot.Children.Count - 1; j >= 0; j--)
                    {
                        var currentBranch = currentAreaRoot.Children[j];
                        // unparent for processing, so that there is a natural limit to going up the parent chain
                        currentBranch.Parent = null;
                        Checks.CheckEquals(0, stackOfNodes.Count); // Check that processing stack was empty
                        stackOfNodes.Clear(); // but clear it anyways, just for good measure
                        stackOfNodes.Push(new TreeIteratorNode(0, ExportUtility.InvalidMappedAreaId, false, currentBranch));
                        IterateAndMoveBranches(
                            stackOfNodes, cachedListForMoving, AreaShouldBeIgnored,
                            // go all the way to the leaves as only they have the MemLabel info for each allocation
                            mode: TreeSplittingLogic.OnlyTakeLeafAreaId, allAreasRoot: allAreasRoot,
                            // for the Unknown area, fall back onto the memlabel
                            useInexplicitLeafNodeAreaAsFallback: isUnknownArea,
                            out var totalMovedSizeForAreaChild);
                        // restore the parenting
                        currentBranch.Parent = currentAreaRoot;
                        // move the size over from the old parent
                        currentAreaRoot.Data.Size -= totalMovedSizeForAreaChild;
                        if (isUnknownArea)
                            totalMovedSize += totalMovedSizeForAreaChild;
                    }
                }
            }
            // Update Unknown Area root size
            CleanupZeroSizedBranchesOfTree(unknownAreaRoot);

            var sizeDiff = allAreasRoot.Children[unknownAreaIndex].Data.Size - unknownAreaRoot.Data.Size;
            if (sizeDiff != totalMovedSize)
                Debug.Log($"expected Unknown to have reduced by {totalMovedSize} but was {sizeDiff}");

            allAreasRoot.Children[unknownAreaIndex].Data.Size = unknownAreaRoot.Data.Size;
            if (unknownAreaRoot.Data.Size <= 0)
                allAreasRoot.Children[unknownAreaIndex].Children.Clear();

            int regeneratedId = allAreasRoot.Id;
            return ModifiableTreeViewItemData<SymbolTreeViewItemData>.BuildReadonlyTree(allAreasRoot, ref regeneratedId, generateUniqueIds: true);
        }

        static bool AllocationHasDifferingExplicitlyMappedMemLabel(ICallstackMapping callstackAreaMapping, int areaIdOfNode, int currentlyGroupedIntoAreaId, string memLabel, bool alsoAcceptFallbackMapping)
        {
            return !string.IsNullOrEmpty(memLabel)
                && (GetCallstackArea(out var _, out var memLabelBasedAreaId, callstackAreaMapping, null, memLabel)
                // If fallback mappings are accepted, ensure that this isn't an ingored area
                || (alsoAcceptFallbackMapping && callstackAreaMapping.IsAreaIgnored(memLabelBasedAreaId)))
                && memLabelBasedAreaId != currentlyGroupedIntoAreaId
                // just doublecheck again that the memlabel based area ID is the one to be used
                && memLabelBasedAreaId == areaIdOfNode;
        }

        struct TreeIteratorNode
        {
            /// <summary>
            /// The index of the next child node to examine (if not bigger than <see cref="TreeDataOfCurrentNode"/>'s child count, obviously).
            /// </summary>
            public int ChildIndexInCurrentNode;
            /// <summary>
            /// The last time a valid area was encountere, either in a parent or on this node, this was its Area Id.
            /// </summary>
            public readonly int LastValidAreaIDFound;
            /// <summary>
            /// If this node doesn't have a valid area in it's own right (i.e. this is false and <see cref="LastValidAreaIDFound"/> is valid)
            /// it's parent still might have a valid area that could be used as a fallback, depending on the <see cref="TreeSplittingLogic"/> mode.
            /// </summary>
            public readonly bool ChildHasParentWithArea;
            /// <summary>
            /// Only readonly on the reference getting changed, the tree obviously remains modifiable.
            /// </summary>
            public readonly ModifiableTreeViewItemData<SymbolTreeViewItemData> TreeDataOfCurrentNode;

            public TreeIteratorNode(int childIndexInCurrentNode, int lastValidAreaIDOfAParentNode, bool childHasParentWithArea, ModifiableTreeViewItemData<SymbolTreeViewItemData> treeDataOfCurrentNode)
            {
                ChildIndexInCurrentNode = childIndexInCurrentNode;
                LastValidAreaIDFound = lastValidAreaIDOfAParentNode;
                ChildHasParentWithArea = childHasParentWithArea;
                TreeDataOfCurrentNode = treeDataOfCurrentNode;
            }
        }

        /// <summary>
        /// This function iterates through the tree depth first, looking for branches with valid area ids,
        /// and move the ones it finds over to that area's specific root.
        /// Depending on the mode, it will
        /// 1. <see cref="TreeSplittingLogic.TakeFirstAreaIdFound"/> either move a branch over as soon as its area is valid
        /// 2. <see cref="TreeSplittingLogic.TakeLastAreaIdFound"/> register its area to mark the branch as belonging to it but
        ///    keeps going, looking to see if there is a different id deeper down the tree. Then, once it reached a leaf node
        ///    it will use the last area it found and move that over (to avoid being too granular, it will iterate up the branch
        ///    and see how much of the branch is exclusively a part of this area and cut at a higher node if possible.
        /// 3. <see cref="TreeSplittingLogic.OnlyTakeLeafAreaId"/> Only consider the leaf nodes with a valid
        ///    <seealso cref="SourceIndex"/>, which means it can really only consider the MemLabel.
        /// </summary>
        /// <param name="stackOfNodes">The stack of nodes to operate on with the first one already added.</param>
        /// <param name="cachedListForMoving">Can be empty, is just getting cleared and then reused.</param>
        /// <param name="areaShouldBeIgnored">Delegate that returns true if an area or memlabel should be ignored.
        /// Takes AreaId and MemLabel name. Returns true if it should be ignored.</param>
        /// <param name="mode"></param>
        /// <param name="allAreasRoot"></param>
        static void IterateAndMoveBranches(
            Stack<TreeIteratorNode> stackOfNodes,
            List<ModifiableTreeViewItemData<SymbolTreeViewItemData>> cachedListForMoving,
            Func<int, string, bool> areaShouldBeIgnored,
            TreeSplittingLogic mode,
            ModifiableTreeViewItemData<SymbolTreeViewItemData> allAreasRoot,
            bool useInexplicitLeafNodeAreaAsFallback,
            out ulong totalMovedSize
            )
        {
            totalMovedSize = 0;
            while (stackOfNodes.Count > 0)
            {
                var currentNode = stackOfNodes.Pop();
                if (currentNode.TreeDataOfCurrentNode.Children?.Count > currentNode.ChildIndexInCurrentNode)
                {
                    var currentChild = currentNode.TreeDataOfCurrentNode.Children[currentNode.ChildIndexInCurrentNode];

                    var childHasParentWithArea = false;
                    var childWasMoved = false;
                    // If the child is mapped to a valid area, its parent areas are no longer important
                    if (currentChild.Data.AreaId != ExportUtility.InvalidMappedAreaId
                        // Some areas should be ignored
                        && !areaShouldBeIgnored(currentChild.Data.AreaId, currentChild.Data.MemLabel))
                    {
                        // But only if the tree should be split at the first valid area do we actually do anything
                        if (mode is TreeSplittingLogic.TakeFirstAreaIdFound)
                        {
                            MoveBranch(currentNode.TreeDataOfCurrentNode, currentNode.ChildIndexInCurrentNode, allAreasRoot.Children[currentChild.Data.AreaId], cachedListForMoving, ref totalMovedSize);
                            // subtract here so that the later increment doesn't lead to skipped siblings
                            --currentNode.ChildIndexInCurrentNode;
                            childWasMoved = true;
                        }
                    }
                    // When taking the last area id that was found, check if one was already found or the current node
                    // (aka the childs parent) has a valid area Id
                    else if (mode is TreeSplittingLogic.TakeLastAreaIdFound &&
                            (currentNode.ChildHasParentWithArea
                            || (currentNode.LastValidAreaIDFound != ExportUtility.InvalidMappedAreaId
                            // Some areas should be ignored
                            && !areaShouldBeIgnored(currentNode.LastValidAreaIDFound, default))))
                    {
                        childHasParentWithArea = true;
                    }
                    // push the current node back onto the stack after iterating the child index so sibling gets checked next.
                    ++currentNode.ChildIndexInCurrentNode;
                    stackOfNodes.Push(currentNode);

                    // Unless the child is gone, add it on the stack to get processed next, as we iterate depth first.
                    if (!childWasMoved)
                    {
                        // When not taking the first id, leaves have to be processed as well
                        if (mode is not TreeSplittingLogic.TakeFirstAreaIdFound
                            // otherwise only add it for processing as it has grandchildren,
                            // as this child was already fully processed for the TakeFirstAreaIdFound mode
                            || currentChild.Children?.Count > 0)
                        {
                            stackOfNodes.Push(new TreeIteratorNode(0, childHasParentWithArea ? currentNode.LastValidAreaIDFound : currentChild.Data.AreaId, childHasParentWithArea, currentChild));
                        }
                    }
                }
                else if (mode is not TreeSplittingLogic.TakeFirstAreaIdFound && currentNode.TreeDataOfCurrentNode.Data.ItemIndex.Valid)
                {
                    var leafNodeHasValidArea =
                        // only use the leaf node area if it is explicitly, used as a fallback or the only mapping we have
                        (useInexplicitLeafNodeAreaAsFallback || currentNode.TreeDataOfCurrentNode.Data.ExplicitlyMapped || !currentNode.ChildHasParentWithArea)
                        && currentNode.TreeDataOfCurrentNode.Data.AreaId != ExportUtility.InvalidMappedAreaId
                        && !areaShouldBeIgnored(currentNode.TreeDataOfCurrentNode.Data.AreaId, currentNode.TreeDataOfCurrentNode.Data.MemLabel);
                    // We reached a leaf and are supposed to move based on the last Area ID found.
                    // Check if the leaf has area id that isn't being ignored ...
                    if (leafNodeHasValidArea
                        // ... or an id was found in a parent (though only if we care)
                        || (currentNode.ChildHasParentWithArea && mode is TreeSplittingLogic.TakeLastAreaIdFound))
                    {
                        // either the leaf has a valid area, or we're splitting by last valid area found
                        var areaIdOfBranchToMove = leafNodeHasValidArea ? currentNode.TreeDataOfCurrentNode.Data.AreaId : currentNode.LastValidAreaIDFound;
                        var allSiblingsAreLeavesWithTheSameValidAreaId = false;

                        // pop the parent off the stack. If not all siblings of the current leaf node get moved, it gets readded.
                        var parentBranch = stackOfNodes.Pop();
                        if (leafNodeHasValidArea)
                        {
                            // Leaf nodes are individial allocations all coming from the same allocation site
                            // They basically all OUGHT to belong to the same area.
                            // But we make sure that they do here, just in case.

                            // This is an optimization. Moving each individual allocation and its parent stack over would be too costly to do.

                            // Check the index of the next sibling to be processed.
                            // If that is not the second child, there are already processed but non-moved siblings, so we can't just move the parent
                            allSiblingsAreLeavesWithTheSameValidAreaId = parentBranch.ChildIndexInCurrentNode == 1;
                            if (allSiblingsAreLeavesWithTheSameValidAreaId &&
                                // check if there are any siblings to evaluate
                                parentBranch.TreeDataOfCurrentNode.Children?.Count > parentBranch.ChildIndexInCurrentNode)
                            {
                                for (int i = 1; i < parentBranch.TreeDataOfCurrentNode.Children.Count; i++)
                                {
                                    // all siblings ought to be leaves of the same area id if we are to move the parent
                                    if (parentBranch.TreeDataOfCurrentNode.Children[i].Data.AreaId != areaIdOfBranchToMove ||
                                        !parentBranch.TreeDataOfCurrentNode.Children[i].Data.ItemIndex.Valid)
                                    {
                                        allSiblingsAreLeavesWithTheSameValidAreaId = false;
                                        break;
                                    }
                                }
                            }
                        }
                        // A valid area was found, now determine if we can just move the parent, have to cut the current node
                        // or can cut even higher, but do ensure we cut in the end.
                        var movedAtLeastOneBranch = false;
                        if (allSiblingsAreLeavesWithTheSameValidAreaId)
                        {
                            // Jump up a layer and move all siblings as part of the parent branch as they all had a valid area
                            // and that is by definition the last one in the branch.
                            parentBranch = stackOfNodes.Pop();
                        }
                        else
                        {
                            // ... otherwise, we need to take siblings into account.
                            switch (mode)
                            {
                                case TreeSplittingLogic.TakeLastAreaIdFound:
                                {
                                    var branchToMove = currentNode;
                                    // When taking the last found item, iterate up the stack to the last item with an id
                                    // as anything under it will get moved, unless it might be a side-branch with a different area down the line.
                                    while (branchToMove.TreeDataOfCurrentNode.Data.AreaId == ExportUtility.InvalidMappedAreaId
                                        || areaShouldBeIgnored(branchToMove.TreeDataOfCurrentNode.Data.AreaId, branchToMove.TreeDataOfCurrentNode.Data.MemLabel))
                                    {
                                        // if the branch has unprocessed siblings, cut here and process the siblings later
                                        if (parentBranch.TreeDataOfCurrentNode.Children?.Count > parentBranch.ChildIndexInCurrentNode)
                                        {
                                            MoveBranch(parentBranch.TreeDataOfCurrentNode, --parentBranch.ChildIndexInCurrentNode, allAreasRoot.Children[areaIdOfBranchToMove], cachedListForMoving, ref totalMovedSize);
                                            stackOfNodes.Push(parentBranch);
                                            movedAtLeastOneBranch = true;
                                            break;
                                        }
                                        branchToMove = parentBranch;
                                        parentBranch = stackOfNodes.Pop();
                                    }
                                    break;
                                }
                                case TreeSplittingLogic.OnlyTakeLeafAreaId:
                                {
                                    // if the branch has siblings of a different area ID, cut here and process any remaining siblings later
                                    MoveBranch(parentBranch.TreeDataOfCurrentNode, --parentBranch.ChildIndexInCurrentNode, allAreasRoot.Children[areaIdOfBranchToMove], cachedListForMoving, ref totalMovedSize);
                                    stackOfNodes.Push(parentBranch);
                                    movedAtLeastOneBranch = true;
                                    break;
                                }
                                case TreeSplittingLogic.TakeFirstAreaIdFound:
                                default:
                                    throw new NotImplementedException();
                            }
                        }
                        if (!movedAtLeastOneBranch)
                        {
                            MoveBranch(parentBranch.TreeDataOfCurrentNode, --parentBranch.ChildIndexInCurrentNode, allAreasRoot.Children[areaIdOfBranchToMove], cachedListForMoving, ref totalMovedSize);
                            stackOfNodes.Push(parentBranch);
                        }
                    }
                }
            }
        }

        static void MoveBranch(ModifiableTreeViewItemData<SymbolTreeViewItemData> sourceParent, int sourceChildIndex,
            ModifiableTreeViewItemData<SymbolTreeViewItemData> destinationRoot,
            List<ModifiableTreeViewItemData<SymbolTreeViewItemData>> cachedParentChainContainer,
            ref ulong totalMovedSize)
        {
            var childNodeToMove = sourceParent.Children[sourceChildIndex];
            sourceParent.Children.RemoveAt(sourceChildIndex);
            var movedBranchSze = childNodeToMove.Data.Size;
            // add the size to the new root
            destinationRoot.Data.Size += movedBranchSze;

            // Build up the chain of parent items.
            // This will be used to mirror the parent chain over to the new area root, using either existing
            // or newly mirrored copies.
            // Note: Don't worry about tree node id uniqueness here, that's taken care off after processing.
            // Here it actually helps in identifying already mirrored copies.
            cachedParentChainContainer.Clear();
            var currentParent = sourceParent;
            while (currentParent != null)
            {
                cachedParentChainContainer.Add(currentParent);
                currentParent = currentParent.Parent;
            }
            // now, in reverse order, from the root up, make sure the tree nodes leading up to the place
            // where the moved branch gets grafted to exists and has the size of the new sub-branch added along the way.
            for (int i = cachedParentChainContainer.Count - 1; i >= 0; i--)
            {
                var currentParentToTransfer = cachedParentChainContainer[i];

                // ensure the child list exists
                destinationRoot.Children ??= new List<ModifiableTreeViewItemData<SymbolTreeViewItemData>>();

                var destinationChildIndex = 0;
                for (destinationChildIndex = 0; destinationChildIndex < destinationRoot.Children.Count; destinationChildIndex++)
                {
                    // Use the tree node id to check if this node has already been copied over
                    if (destinationRoot.Children[destinationChildIndex].Id == currentParentToTransfer.Id)
                        break;
                }
                // if no child was found matching the tree node id of the node to be copied, create it ...
                if (destinationChildIndex >= destinationRoot.Children.Count)
                {
                    // use the shallow copy copy-constructor to mirror the id
                    var newChild = new ModifiableTreeViewItemData<SymbolTreeViewItemData>(currentParentToTransfer, destinationRoot, copyChildren: false);
                    // but make sure the size is not the full size being copied but only that of the currently moved sub-branch
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
            destinationRoot.Data.Size += movedBranchSze;
            totalMovedSize += totalMovedSize;
        }

        public static bool GetCallstackArea(out string areaName, out int areaId, ICallstackMapping callstackAreaMapping, StringBuilder callstack, string memLabel)
        {
            areaName = ExportUtility.InvalidMappedAreaName;
            areaId = ExportUtility.InvalidMappedAreaId;
            if (callstackAreaMapping != null && (callstack?.Length > 0 || !string.IsNullOrEmpty(memLabel)))
            {
                return callstackAreaMapping.TryMap(callstack?.ToString() ?? string.Empty, memLabel, out areaName, out areaId);
            }
            return false;
        }

        static void CleanupZeroSizedBranchesOfTree(ModifiableTreeViewItemData<SymbolTreeViewItemData> tree)
        {
            var stackOfNodes = new Stack<(int, ModifiableTreeViewItemData<SymbolTreeViewItemData>)>();
            void RemoveLastProcessedNodeFromParentNode(Stack<(int, ModifiableTreeViewItemData<SymbolTreeViewItemData>)> stackOfNodes)
            {
                var currentNode = stackOfNodes.Pop();
                currentNode.Item2.Children.RemoveAt(--currentNode.Item1);
                stackOfNodes.Push(currentNode);
            }

            stackOfNodes.Push((0, tree));
            while (stackOfNodes.Count > 0)
            {
                var currentNode = stackOfNodes.Pop();
                var siblingCount = currentNode.Item2.Children?.Count ?? 0;
                if (siblingCount > currentNode.Item1)
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
                        else if (!currentChild.Data.ItemIndex.Valid)
                        {
                            // remove the child. If there are no children, it has no size (unless it's an actual allocation index)
                            RemoveLastProcessedNodeFromParentNode(stackOfNodes);
                        }
                    }
                }
                else if (!currentNode.Item2.Data.ItemIndex.Valid)
                {
                    // fix up sizes. If there are no children (unless it's an actual allocation index), the size resets to zero, propagating up as the stack unwinds
                    var size = 0ul;
                    for (int i = 0; i < siblingCount; i++)
                    {
                        size += currentNode.Item2.Children[i].Data.Size;
                    }

                    if (size > 0 || stackOfNodes.Count <= 0)
                    {
                        // if there is a size of alloctions left, adjust the size here so it can propagate upd
                        var data = currentNode.Item2.Data;
                        data.Size = size;
                        currentNode.Item2.Data = data;
                    }
                    else
                    {
                        // remove the node from the parent
                        RemoveLastProcessedNodeFromParentNode(stackOfNodes);
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

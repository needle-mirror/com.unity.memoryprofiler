using System;
using System.Globalization;
using Unity.MemoryProfiler.Editor.Database;
using Unity.MemoryProfiler.Editor.Database.Operation;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class SelectionEvent : HistoryEvent
    {
        public MemorySampleSelection Selection { get; private set; }

        public SelectionEvent(MemorySampleSelection selection)
        {
            Selection = selection;
        }

        protected override bool IsEqual(HistoryEvent evt)
        {
            if (evt is SelectionEvent)
            {
                var selectionEvent = (evt as SelectionEvent);
                return this == selectionEvent ||
                    Selection.Type == selectionEvent.Selection.Type &&
                    Selection.ItemIndex == selectionEvent.Selection.ItemIndex;
            }
            return false;
        }
    }

    internal enum MemorySampleSelectionType
    {
        None, // Nothing selected, clear the selection
        NativeObject,
        ManagedObject,
        UnifiedObject,
        Allocation,
        AllocationSite,
        AllocationCallstack,
        NativeRegion,
        ManagedRegion,
        Allocator,
        Label,
        NativeType,
        ManagedType,
        Connection,
        HighlevelBreakdownElement,
        Symbol,
    }
    internal enum MemorySampleSelectionRank
    {
        MainSelection = 0,
        SecondarySelection = 1,
    }

    internal struct MemorySampleSelection : IEquatable<MemorySampleSelection>
    {
        public static readonly MemorySampleSelection InvalidMainSelection = new MemorySampleSelection(MemorySampleSelectionRank.MainSelection);
        public static readonly MemorySampleSelection InvalidSecondarySelection = new MemorySampleSelection(MemorySampleSelectionRank.SecondarySelection);
        public bool Valid { get => Type != MemorySampleSelectionType.None; }
        public readonly MemorySampleSelectionType Type;
        public readonly MemorySampleSelectionRank Rank;
        public readonly long ItemIndex;
        public readonly long RowIndex;
        public readonly long SecondaryItemIndex;
        public readonly long TertiaryItemIndex;
        public readonly string Table;

        readonly SnapshotAge m_SnapshotAge;

        public bool Equals(MemorySampleSelection other)
        {
            return Type == other.Type &&
                Rank == other.Rank &&
                ItemIndex == other.ItemIndex &&
                RowIndex == other.RowIndex &&
                SecondaryItemIndex == other.SecondaryItemIndex &&
                TertiaryItemIndex == other.TertiaryItemIndex &&
                Table == other.Table;
        }

        public CachedSnapshot GetSnapshotItemIsPresentIn(UIState uiState)
        {
            if (!Valid)
                return null;
            if (uiState.CurrentViewMode == UIState.ViewMode.ShowFirst)
                return (uiState.FirstMode as UIState.SnapshotMode).snapshot;

            switch (uiState.FirstSnapshotAge)
            {
                case SnapshotAge.None:
                    // No set age means we have only one snapshot loaded in either first or second mode
                    if (uiState.FirstMode != null)
                        return (uiState.FirstMode as UIState.SnapshotMode).snapshot;
                    if (uiState.SecondMode != null)
                        return (uiState.SecondMode as UIState.SnapshotMode).snapshot;
                    break;
                case SnapshotAge.Older:
                    if (m_SnapshotAge == SnapshotAge.Older)
                        return (uiState.FirstMode as UIState.SnapshotMode).snapshot;
                    return (uiState.SecondMode as UIState.SnapshotMode).snapshot;
                case SnapshotAge.Newer:
                    if (m_SnapshotAge == SnapshotAge.Older)
                        return (uiState.SecondMode as UIState.SnapshotMode).snapshot;
                    return (uiState.FirstMode as UIState.SnapshotMode).snapshot;
                default:
                    break;
            }
            return null;
        }

        MemorySampleSelection(MemorySampleSelectionRank rank)
        {
            Type = MemorySampleSelectionType.None;
            Rank = rank;
            ItemIndex = -1;
            RowIndex = -1;
            SecondaryItemIndex = -1;
            TertiaryItemIndex = -1;
            Table = null;
            m_SnapshotAge = SnapshotAge.None;
        }

        public MemorySampleSelection(string breakdownName, int breakdownBarId, int breakdownElementId) : this()
        {
            m_SnapshotAge = SnapshotAge.None;

            Table = breakdownName;
            Type = MemorySampleSelectionType.HighlevelBreakdownElement;
            Rank = MemorySampleSelectionRank.MainSelection;

            ItemIndex = breakdownElementId;
            SecondaryItemIndex = breakdownBarId;
            RowIndex = breakdownElementId;
        }

        public MemorySampleSelection(UIState uiState, long unifiedObjectIndexFromPathToRoots, long pathToRootsRowId, CachedSnapshot cachedSnapshot) : this()
        {
            // TODO: Adjust once Tree Map has Diff version;
            m_SnapshotAge = SnapshotAge.None;
            if (uiState.CurrentMode is UIState.DiffMode)
            {
                if ((uiState.FirstMode as UIState.SnapshotMode).snapshot == cachedSnapshot)
                    m_SnapshotAge = uiState.FirstSnapshotAge;
                else
                    m_SnapshotAge = uiState.FirstSnapshotAge == SnapshotAge.Newer ? SnapshotAge.Older : SnapshotAge.Newer;
            }
            var snapshot = GetRelevantOpenSnapshot(uiState, ref m_SnapshotAge);

            Table = "Path To Root";
            Rank = MemorySampleSelectionRank.SecondarySelection;

            ItemIndex = snapshot?.UnifiedObjectIndexToNativeObjectIndex(unifiedObjectIndexFromPathToRoots) ?? -1;
            if (ItemIndex != -1)
            {
                Type = MemorySampleSelectionType.NativeObject;
                TertiaryItemIndex = (long)ObjectDataType.NativeObject;
            }
            else
            {
                ItemIndex = snapshot?.UnifiedObjectIndexToManagedObjectIndex(unifiedObjectIndexFromPathToRoots) ?? -1;
                if (ItemIndex != -1)
                {
                    Type = MemorySampleSelectionType.ManagedObject;
                    TertiaryItemIndex = (long)ObjectData.FromManagedObjectIndex(snapshot, (int)ItemIndex).dataType;
                }
                else
                    throw new NotImplementedException();
            }
            RowIndex = pathToRootsRowId;
        }

        public MemorySampleSelection(UIState uiState, Table displayTable, Treemap.Group item) : this()
        {
            // TODO: Adjust once Tree Map has Diff version;
            m_SnapshotAge = SnapshotAge.None;
            var snapshot = GetRelevantOpenSnapshot(uiState, ref m_SnapshotAge);

            Table = displayTable.GetName();
            Rank = MemorySampleSelectionRank.MainSelection;

            switch (item.MetricType)
            {
                case Treemap.ObjectMetricType.Managed:
                    Type = MemorySampleSelectionType.ManagedType;
                    TertiaryItemIndex = (long)ObjectData.FromManagedObjectIndex(snapshot, item.Items[0].Metric.ObjectIndex).dataType;
                    break;
                case Treemap.ObjectMetricType.Native:
                    Type = MemorySampleSelectionType.NativeType;
                    TertiaryItemIndex = (long)ObjectDataType.NativeObject;
                    break;
                case Treemap.ObjectMetricType.None:
                default:
                    throw new NotImplementedException();
            }
            ItemIndex = item.TypeIndex;
            RowIndex = -1;
        }

        public MemorySampleSelection(UIState uiState, Table displayTable, long rowIndex, Treemap.Item item) : this()
        {
            // TODO: Adjust once Tree Map has Diff version;
            m_SnapshotAge = SnapshotAge.None;
            var snapshot = GetRelevantOpenSnapshot(uiState, ref m_SnapshotAge);

            Table = displayTable.GetName();
            Rank = MemorySampleSelectionRank.MainSelection;

            ItemIndex = item.Metric.ObjectIndex;
            SecondaryItemIndex = item.Group.TypeIndex;
            RowIndex = rowIndex;
            switch (item.Group.MetricType)
            {
                case Treemap.ObjectMetricType.Managed:
                    Type = MemorySampleSelectionType.ManagedObject;
                    TertiaryItemIndex = (long)ObjectData.FromManagedObjectIndex(snapshot, item.Metric.ObjectIndex).dataType;
                    break;
                case Treemap.ObjectMetricType.Native:
                    Type = MemorySampleSelectionType.NativeObject;
                    TertiaryItemIndex = (long)ObjectDataType.NativeObject;
                    break;
                case Treemap.ObjectMetricType.None:
                default:
                    throw new NotImplementedException();
            }
        }

        public MemorySampleSelection(UIState uiState, Table displayTable, long rowIndex, SnapshotAge snapshotAge = SnapshotAge.None) : this()
        {
            m_SnapshotAge = snapshotAge;
            var snapshot = GetRelevantOpenSnapshot(uiState, ref m_SnapshotAge,  rowIndex, displayTable);
            Table = displayTable.GetName();
            Rank = MemorySampleSelectionRank.MainSelection;
            RowIndex = rowIndex;

            if (Table.Contains("RawNativeObject"))
            {
                Type = MemorySampleSelectionType.NativeObject;

                var instanceId = GetValue<int>(displayTable, rowIndex, "instanceId", out var success);
                if (success && snapshot.NativeObjects.instanceId2Index.ContainsKey(instanceId))
                    ItemIndex = snapshot.NativeObjects.instanceId2Index[instanceId];
                else
                    ItemIndex = -1;
            }
            else if (Table.Contains("Object")) // Table.Contains("AllObjects") ||  || Table.Contains("AllManagedObjects") || Table.Contains("AllNativeObjects"))
            {
                Type = MemorySampleSelectionType.ManagedObject;
                var address = GetAddress(displayTable, rowIndex);
                int idx;
                if (snapshot.CrawledData.MangedObjectIndexByAddress.TryGetValue(address, out idx) && idx != -1)
                {
                    ItemIndex = idx;
                }
                if (snapshot.NativeObjects.nativeObjectAddressToInstanceId.TryGetValue(address, out idx)
                    && snapshot.NativeObjects.instanceId2Index.TryGetValue(idx, out idx) && idx != -1)
                {
                    ItemIndex = idx;
                    Type = MemorySampleSelectionType.NativeObject;
                }
            }
            else if (Table.Contains("MemoryRegions"))
            {
                Type = MemorySampleSelectionType.NativeRegion;

                var address = GetAddress(displayTable, rowIndex, Table.Contains("Raw") ? "addressBase" : "address");
                var name = GetName(displayTable, rowIndex);

                ItemIndex = -1;
                for (long i = 0; i < snapshot.NativeMemoryRegions.Count; i++)
                {
                    if (snapshot.NativeMemoryRegions.AddressBase[i] == address && snapshot.NativeMemoryRegions.MemoryRegionName[i] == name)
                    {
                        ItemIndex = i;
                        break;
                    }
                }
                if (ItemIndex == -1)
                {
                    Type = MemorySampleSelectionType.ManagedRegion;

                    for (long i = 0; i < snapshot.ManagedHeapSections.Count; i++)
                    {
                        if (snapshot.ManagedHeapSections.StartAddress[i] == address)
                        {
                            ItemIndex = i;
                            break;
                        }
                    }
                }
            }
            else if (Table.Contains("CallstackSymbol"))
            {
                Type = MemorySampleSelectionType.Symbol;
                var symbol = GetValue<ulong>(displayTable, rowIndex, "symbol", out _);

                if (symbol == 0)
                    ItemIndex = -1;
                else
                {
                    for (long i = 0; i < snapshot.NativeCallstackSymbols.Count; i++)
                    {
                        if (snapshot.NativeCallstackSymbols.Symbol[i] == symbol)
                        {
                            ItemIndex = i;
                            break;
                        }
                    }
                }
            }
            else if (Table.Contains("NativeAllocation"))
            {
                Type = MemorySampleSelectionType.Allocation;
                var address = GetAddress(displayTable, rowIndex);

                if (address == 0)
                    ItemIndex = -1;
                else
                {
                    for (long i = 0; i < snapshot.NativeAllocations.Count; i++)
                    {
                        if (snapshot.NativeAllocations.Address[i] == address)
                        {
                            ItemIndex = i;
                            break;
                        }
                    }
                }
            }
            else if (Table.Contains("RawRootReference"))
            {
                Type = MemorySampleSelectionType.Allocation;
                var id = GetValue<long>(displayTable, rowIndex, "id", out _);

                if (id == 0)
                    ItemIndex = -1;
                else
                    ItemIndex = snapshot.NativeRootReferences.IdToIndex[id];
            }
            else if (Table.Contains("Label"))
            {
                Type = MemorySampleSelectionType.Label;
                var labelName = GetName(displayTable, rowIndex);

                if (string.IsNullOrEmpty(labelName))
                    ItemIndex = -1;
                else
                {
                    for (long i = 0; i < snapshot.NativeMemoryLabels.Count; i++)
                    {
                        if (snapshot.NativeMemoryLabels.MemoryLabelName[i] == labelName)
                        {
                            ItemIndex = i;
                            break;
                        }
                    }
                }
            }
            else if (Table.Contains("NativeTypeBase"))
            {
                Type = MemorySampleSelectionType.NativeType;
                ItemIndex = GetValue<int>(displayTable, rowIndex, "typeIndex", out var success);
                // TODO: maybe needs special treatment for the selection? Currently not getting it assuming Type info will include base Type info.
            }
            else if (Table.Contains("NativeType"))
            {
                Type = MemorySampleSelectionType.NativeType;
                var typeName = GetName(displayTable, rowIndex);

                if (string.IsNullOrEmpty(typeName))
                    ItemIndex = -1;
                else
                {
                    for (long i = 0; i < snapshot.NativeTypes.Count; i++)
                    {
                        if (snapshot.NativeTypes.TypeName[i] == typeName)
                        {
                            ItemIndex = i;
                            break;
                        }
                    }
                }
            }
            else if (Table.Contains("ManagedType"))
            {
                Type = MemorySampleSelectionType.ManagedType;
                ItemIndex = GetValue<int>(displayTable, rowIndex, "typeIndex", out var success);
                //var typeName = GetName(displayTable, rowIndex);

                //if (string.IsNullOrEmpty(typeName))
                //    ItemIndex = -1;
                //else
                //{
                //    for (long i = 0; i < snapshot.TypeDescriptions.Count; i++)
                //    {
                //        if (snapshot.TypeDescriptions.TypeDescriptionName[i] == typeName)
                //        {
                //            ItemIndex = i;
                //            break;
                //        }
                //    }
                //}
            }
            else if (Table.Contains("NativeConnection"))
            {
                Type = MemorySampleSelectionType.Connection;
                var from = GetValue<int>(displayTable, rowIndex, "from", out var success);
                if (success)
                {
                    var to = GetValue<int>(displayTable, rowIndex, "to", out success);

                    for (long i = 0; i < snapshot.TypeDescriptions.Count; i++)
                    {
                        if (snapshot.Connections.From[i] == from && snapshot.Connections.To[i] == to)
                        {
                            ItemIndex = i;
                            break;
                        }
                    }
                }
            }
            else
            {
                Debug.LogError("Selection not yet implemented for this table");

                Type = MemorySampleSelectionType.Label;
                ItemIndex = -1;
            }
        }

        MemorySampleSelection(MemorySampleSelectionType type, long itemIndex) : this()
        {
            Type = type;
            Rank = MemorySampleSelectionRank.MainSelection;
            ItemIndex = itemIndex;
        }

        public static MemorySampleSelection FromUnifiedObjectIndex(long unifiedObjectIndex)
        {
            return new MemorySampleSelection(MemorySampleSelectionType.UnifiedObject, unifiedObjectIndex);
        }

        public static MemorySampleSelection FromNativeObjectIndex(long nativeObjectIndex)
        {
            return new MemorySampleSelection(MemorySampleSelectionType.NativeObject, nativeObjectIndex);
        }

        public static MemorySampleSelection FromNativeTypeIndex(long nativeTypeIndex)
        {
            return new MemorySampleSelection(MemorySampleSelectionType.NativeType, nativeTypeIndex);
        }

        public static MemorySampleSelection FromManagedObjectIndex(long managedObjectIndex)
        {
            return new MemorySampleSelection(MemorySampleSelectionType.ManagedObject, managedObjectIndex);
        }

        public static MemorySampleSelection FromManagedTypeIndex(long managedTypeIndex)
        {
            return new MemorySampleSelection(MemorySampleSelectionType.ManagedType, managedTypeIndex);
        }

        public string GetTypeStringForAnalytics(CachedSnapshot snapshot)
        {
            if (ItemIndex < 0)
            {
                return $"Invalid {Type}";
            }
            else
            {
                switch (Type)
                {
                    case MemorySampleSelectionType.NativeObject:
                        return GetTypeStringForAnalyticsFromNativeObject(ItemIndex, snapshot);
                    case MemorySampleSelectionType.ManagedObject:
                        return GetTypeStringForAnalyticsFromManagedObject(ItemIndex, snapshot);

                    case MemorySampleSelectionType.UnifiedObject:
                        var managedObjectIndex = snapshot.UnifiedObjectIndexToManagedObjectIndex(ItemIndex);
                        if (managedObjectIndex >= 0)
                            return GetTypeStringForAnalyticsFromManagedObject(managedObjectIndex, snapshot);
                        else
                            return GetTypeStringForAnalyticsFromNativeObject(snapshot.UnifiedObjectIndexToNativeObjectIndex(ItemIndex), snapshot);

                    case MemorySampleSelectionType.NativeType:
                        return GetTypeStringForAnalyticsFromNativeType(ItemIndex, snapshot);
                    case MemorySampleSelectionType.ManagedType:
                        return GetTypeStringForAnalyticsFromManagedType(ItemIndex, snapshot);

                    case MemorySampleSelectionType.None:
                    case MemorySampleSelectionType.Allocation:
                    case MemorySampleSelectionType.AllocationSite:
                    case MemorySampleSelectionType.AllocationCallstack:
                    case MemorySampleSelectionType.NativeRegion:
                    case MemorySampleSelectionType.ManagedRegion:
                    case MemorySampleSelectionType.Allocator:
                    case MemorySampleSelectionType.Label:
                    case MemorySampleSelectionType.Connection:
                    case MemorySampleSelectionType.HighlevelBreakdownElement:
                    case MemorySampleSelectionType.Symbol:
                        return Type.ToString();
                    default:
                        // If you hit this, consider if the new selection type should send some more type info to Analytics.
                        throw new NotImplementedException();
                }
            }
        }

        static string GetTypeStringForAnalyticsFromNativeObject(long nativeObjectIndex, CachedSnapshot snapshot)
        {
            var nativeTypeIndex = snapshot.NativeObjects.NativeTypeArrayIndex[nativeObjectIndex];
            if (nativeTypeIndex >= 0)
                return $"{nameof(MemorySampleSelectionType.NativeObject)} of Type: {snapshot.NativeTypes.TypeName[nativeTypeIndex]}";
            else
                return $"Invalid {nameof(MemorySampleSelectionType.NativeObject)} for NativeObject";
        }

        static string GetTypeStringForAnalyticsFromManagedObject(long managedObjectIndex, CachedSnapshot snapshot)
        {
            var managedType = snapshot.CrawledData.ManagedObjects[managedObjectIndex].ITypeDescription;
            if (managedType >= 0)
            {
                int nativeTypeIndex;
                if (snapshot.TypeDescriptions.UnityObjectTypeIndexToNativeTypeIndex.TryGetValue((int)managedType, out nativeTypeIndex))
                {
                    return $"{nameof(MemorySampleSelectionType.ManagedObject)} of Unity Type based on: {snapshot.NativeTypes.TypeName[nativeTypeIndex]}";
                }
                else
                    return $"{nameof(MemorySampleSelectionType.ManagedObject)} of Non-Unity Type";
            }
            return $"Invalid {nameof(MemorySampleSelectionType.ManagedType)}  for {nameof(MemorySampleSelectionType.ManagedObject)} ";
        }

        static string GetTypeStringForAnalyticsFromNativeType(long nativeTypeIndex, CachedSnapshot snapshot)
        {
            return $"{nameof(MemorySampleSelectionType.NativeType)}: {snapshot.NativeTypes.TypeName[nativeTypeIndex]}";
        }

        static string GetTypeStringForAnalyticsFromManagedType(long managedTypeIndex, CachedSnapshot snapshot)
        {
            int nativeTypeIndex;
            if (snapshot.TypeDescriptions.UnityObjectTypeIndexToNativeTypeIndex.TryGetValue((int)managedTypeIndex, out nativeTypeIndex))
            {
                return $"{nameof(MemorySampleSelectionType.ManagedType)} based on Unity Type: {snapshot.NativeTypes.TypeName[nativeTypeIndex]}";
            }
            else
                return $"{nameof(MemorySampleSelectionType.ManagedType)} Non-Unity";
        }

        public long FindSelectionInTable(UIState uiState, Table displayTable)
        {
            var snapshot = GetSnapshotItemIsPresentIn(uiState);

            var diffModeIsShown = uiState.CurrentViewMode == UIState.ViewMode.ShowDiff;

            var diffColumnValue = diffModeIsShown ? GetDiffValueFromAge(m_SnapshotAge) : DiffTable.DiffResult.None;

            // if the selection belongs to one of the two diffed ones   &&   diff mode is not shown   &&  the current mode's snapshot is not the one containing the selection
            if (m_SnapshotAge != SnapshotAge.None && !diffModeIsShown && snapshot != (uiState.CurrentMode as UIState.SnapshotMode).snapshot)
                return -1; // ->  just give up, there is nothing to be found here.

            var tableName = displayTable.GetName();

            // A selected Item might be still placed at the row index it was at when it was selected
            // it may, however, have been selected in a different table, a differently filtered table
            // or even an entirely different type of table, but still be found within this one.
            // This code is a bit brute force but it will
            // - double-check that the RowIndex is still correctly pointing to the right item, and if so, just return the RowIndex
            // (it does this first to speed up the most common cases)
            // or
            // - Search for the item in the Table and return the appropriate RowIndex, if it is found.

            if (tableName.Contains("RawNativeObject"))
            {
                if (Type == MemorySampleSelectionType.NativeObject)
                {
                    if (diffModeIsShown)
                    {
                        var instanceId2 = snapshot.NativeObjects.InstanceId[ItemIndex];
                        return FindValue<int>(displayTable, instanceId2, "instanceId", m_SnapshotAge);
                    }
                    var instanceId = GetValue<int>(displayTable, RowIndex, "instanceId, ", out var success);
                    if (success && snapshot.NativeObjects.instanceId2Index.ContainsKey(instanceId) &&
                        ItemIndex == snapshot.NativeObjects.instanceId2Index[instanceId])
                        return RowIndex;
                    else if (ItemIndex >= 0 && snapshot.NativeObjects.Count > ItemIndex)
                    {
                        instanceId = snapshot.NativeObjects.InstanceId[ItemIndex];
                        return FindValue<int>(displayTable, instanceId, "instanceId");
                    }
                }
            }
            else if (tableName.Contains("Object")) // Table.Contains("AllObjects") ||  || Table.Contains("AllManagedObjects") || Table.Contains("AllNativeObjects"))
            {
                if (Type == MemorySampleSelectionType.ManagedObject)
                {
                    if (diffModeIsShown)
                    {
                        var address2 = snapshot.CrawledData.ManagedObjects[ItemIndex].PtrObject;
                        return FindAddress(displayTable, address2, m_SnapshotAge);
                    }

                    var address = GetAddress(displayTable, RowIndex);
                    int managedIndex;
                    if (snapshot.CrawledData.MangedObjectIndexByAddress.TryGetValue(address, out managedIndex) && managedIndex != ItemIndex)
                        return RowIndex;
                    else
                    {
                        var uid = snapshot.ManagedObjectIndexToUnifiedObjectIndex(ItemIndex);
                        return FindValue<long>(displayTable, uid, "Index");
                    }
                }
                else if (Type == MemorySampleSelectionType.NativeObject)
                {
                    if (diffModeIsShown)
                    {
                        var address = snapshot.NativeObjects.NativeObjectAddress[ItemIndex];
                        return FindAddress(displayTable, address, m_SnapshotAge);
                    }

                    var uid = snapshot.NativeObjectIndexToUnifiedObjectIndex(ItemIndex);
                    if (GetValue<long>(displayTable, RowIndex, "Index", out var success) == uid && success)
                        return RowIndex;
                    return FindValue<long>(displayTable, uid, "Index");
                }
            }
            else if (tableName.Contains("MemoryRegions"))
            {
                var columnName = Table.Contains("Raw") ? "addressBase" : "address";

                if (diffModeIsShown)
                {
                    if (Type == MemorySampleSelectionType.NativeRegion)
                    {
                        var address2 = snapshot.NativeMemoryRegions.AddressBase[ItemIndex];
                        return FindValue(displayTable, address2, columnName, m_SnapshotAge);
                    }
                    else if (Type == MemorySampleSelectionType.ManagedRegion && ItemIndex >= 0 && snapshot.ManagedHeapSections.Count > ItemIndex)
                    {
                        var address2 = snapshot.ManagedHeapSections.StartAddress[ItemIndex];
                        return FindValue(displayTable, address2, columnName, m_SnapshotAge);
                    }
                    return -1;
                }

                var address = GetAddress(displayTable, RowIndex, columnName);
                var name = GetName(displayTable, RowIndex);
                if (Type == MemorySampleSelectionType.NativeRegion)
                {
                    if (ItemIndex >= 0 && snapshot.NativeMemoryRegions.Count > ItemIndex)
                    {
                        if (snapshot.NativeMemoryRegions.AddressBase[ItemIndex] == address &&
                            snapshot.NativeMemoryRegions.MemoryRegionName[ItemIndex] == name)
                            return RowIndex;
                        return FindValue(displayTable, snapshot.NativeMemoryRegions.AddressBase[ItemIndex], columnName);
                    }
                }
                else if (Type == MemorySampleSelectionType.ManagedRegion && ItemIndex >= 0 && snapshot.ManagedHeapSections.Count > ItemIndex)
                {
                    if (snapshot.ManagedHeapSections.StartAddress[ItemIndex] == address)
                        return RowIndex;
                    return FindValue(displayTable, snapshot.ManagedHeapSections.StartAddress[ItemIndex], columnName);
                }
            }
            else if (tableName.Contains("CallstackSymbol"))
            {
                if (Type != MemorySampleSelectionType.Symbol && ItemIndex >= 0 && snapshot.NativeCallstackSymbols.Count > ItemIndex)
                {
                    var symbol = snapshot.NativeCallstackSymbols.Symbol[ItemIndex];

                    if (diffModeIsShown)
                    {
                        return FindValue(displayTable, symbol, "symbol", m_SnapshotAge);
                    }

                    if (GetValue<ulong>(displayTable, RowIndex, "symbol", out var success) == symbol && success)
                        return RowIndex;
                    else
                        return FindValue(displayTable, symbol, "symbol");
                }
            }
            else if (tableName.Contains("NativeAllocation"))
            {
                if (Type == MemorySampleSelectionType.Allocation && ItemIndex >= 0 && snapshot.NativeAllocations.Count > ItemIndex)
                {
                    var address = snapshot.NativeAllocations.Address[ItemIndex];

                    if (diffModeIsShown)
                    {
                        return FindAddress(displayTable, address, m_SnapshotAge);
                    }

                    if (GetAddress(displayTable, RowIndex) == address)
                        return RowIndex;
                    else
                        return FindAddress(displayTable, address);
                }
            }
            else if (tableName.Contains("RawRootReference"))
            {
                if (Type == MemorySampleSelectionType.Allocation && ItemIndex >= 0 && snapshot.NativeRootReferences.Count > ItemIndex)
                {
                    var id = snapshot.NativeRootReferences.Id[ItemIndex];

                    if (diffModeIsShown)
                    {
                        return FindValue(displayTable, id, "id", m_SnapshotAge);
                    }

                    if (id == GetValue<long>(displayTable, RowIndex, "id", out var success) && success)
                        return RowIndex;
                    else
                        return FindValue(displayTable, id, "id");
                }
            }
            else if (tableName.Contains("Label"))
            {
                if (Type == MemorySampleSelectionType.Label && ItemIndex >= 0 && snapshot.NativeMemoryLabels.Count > ItemIndex)
                {
                    var labelName = snapshot.NativeMemoryLabels.MemoryLabelName[ItemIndex];

                    if (diffModeIsShown)
                    {
                        return FindName(displayTable, labelName, m_SnapshotAge);
                    }

                    if (labelName == GetName(displayTable, RowIndex))
                        return RowIndex;
                    else
                        return FindName(displayTable, labelName);
                }
            }
            else if (tableName.Contains("NativeTypeBase"))
            {
                if (Type == MemorySampleSelectionType.NativeType)
                {
                    if (diffModeIsShown)
                    {
                        return FindValue(displayTable, ItemIndex, "typeIndex", m_SnapshotAge);
                    }

                    // TODO: maybe needs special treatment for the selection? Currently not getting it assuming Type info will include base Type info.
                    if (ItemIndex == GetValue<int>(displayTable, RowIndex, "typeIndex", out var success) && success)
                        return RowIndex;
                    else
                        return FindValue(displayTable, ItemIndex, "typeIndex");
                }
            }
            else if (tableName.Contains("NativeType"))
            {
                if (Type == MemorySampleSelectionType.NativeType && ItemIndex >= 0 && snapshot.NativeTypes.Count > ItemIndex)
                {
                    var typeName = snapshot.NativeTypes.TypeName[ItemIndex];

                    if (diffModeIsShown)
                    {
                        return FindName(displayTable, typeName, m_SnapshotAge);
                    }

                    if (typeName == GetName(displayTable, RowIndex))
                        return RowIndex;
                    // If the Type was e.g. selected in Tree Map, the RowIndex will be -1 but if one opens the correct Type table without filtering, it's RowIndex == TypeIndex
                    else if (typeName == GetName(displayTable, ItemIndex))
                        return ItemIndex;
                    else
                        return FindName(displayTable, typeName);
                }
            }
            else if (tableName.Contains("ManagedType"))
            {
                if (Type == MemorySampleSelectionType.ManagedType && ItemIndex >= 0 && snapshot.TypeDescriptions.Count > ItemIndex)
                {
                    if (diffModeIsShown)
                    {
                        return FindValue(displayTable, ItemIndex, "typeIndex", m_SnapshotAge);
                    }

                    if (ItemIndex == GetValue<int>(displayTable, RowIndex, "typeIndex", out var success) && success)
                        return RowIndex;
                    // If the Type was e.g. selected in Tree Map, the RowIndex will be -1 but if one opens the correct Type table without filtering, it's RowIndex == TypeIndex
                    if (ItemIndex == GetValue<int>(displayTable, ItemIndex, "typeIndex", out success) && success)
                        return ItemIndex;
                    return FindValue(displayTable, ItemIndex, "typeIndex");
                }
            }
            else if (tableName.Contains("NativeConnection"))
            {
                if (Type == MemorySampleSelectionType.Connection)
                {
                    var fromToFind = snapshot.Connections.From[ItemIndex];
                    var toToFind = snapshot.Connections.To[ItemIndex];
                    var from = GetValue<int>(displayTable, RowIndex, "from", out var success);
                    if (success)
                    {
                        var to = GetValue<int>(displayTable, RowIndex, "to", out success);
                        if (from == fromToFind && to == toToFind && success)
                            return RowIndex;
                        var rowCount = displayTable.GetRowCount();

                        for (long i = 0; i < rowCount; i++)
                        {
                            if (diffModeIsShown)
                            {
                                var diffValue = GetValue<DiffTable.DiffResult>(displayTable, i, k_DiffColumnName, out success);
                                if (!success && diffColumnValue != diffValue)
                                    continue;
                            }
                            from = GetValue<int>(displayTable, i, "from", out success);
                            to = GetValue<int>(displayTable, i, "to", out var succesTo);
                            if (success && succesTo && from == fromToFind && to == toToFind)
                                return i;
                        }
                    }
                }
            }
            else
            {
                Debug.LogError("Selection not yet implemented for this table");
                return -1;
            }
            return -1;
        }

        static CachedSnapshot GetRelevantOpenSnapshot(UIState uiState, ref SnapshotAge snapshotAge,  long rowIndex = -1, Table displayTable = null)
        {
            if (snapshotAge != SnapshotAge.None)
            {
                // We don't need to guess which snapshot the selection is in
                if (uiState.FirstSnapshotAge == snapshotAge)
                {
                    return (uiState.FirstMode as UIState.SnapshotMode).snapshot;
                }
                else
                {
                    return (uiState.SecondMode as UIState.SnapshotMode).snapshot;
                }
            }
            else if (displayTable != null && displayTable.GetDisplayName().Contains("Diff"))
            {
                // TODO: fix for Table diff, this is very brute force

                var diff = GetValue<DiffTable.DiffResult>(displayTable, rowIndex, k_DiffColumnName, out var success);
                if (!success)
                    return null;
                switch (diff)
                {
                    case DiffTable.DiffResult.Deleted:
                        snapshotAge = SnapshotAge.Older;
                        return ((uiState.FirstSnapshotAge == SnapshotAge.Newer ? uiState.SecondMode : uiState.FirstMode)
                            as UIState.SnapshotMode).snapshot;
                    case DiffTable.DiffResult.New:
                        snapshotAge = SnapshotAge.Newer;
                        return ((uiState.FirstSnapshotAge == SnapshotAge.Newer ? uiState.FirstMode : uiState.SecondMode)
                            as UIState.SnapshotMode).snapshot;
                    case DiffTable.DiffResult.Same:
                        snapshotAge = uiState.FirstSnapshotAge;
                        return (uiState.FirstMode as UIState.SnapshotMode).snapshot;
                    case DiffTable.DiffResult.None:
                    default:
                        return null;
                }
            }
            else
            {
                switch (uiState.CurrentViewMode)
                {
                    case UIState.ViewMode.ShowFirst:
                        snapshotAge = uiState.FirstSnapshotAge;
                        return (uiState.FirstMode as UIState.SnapshotMode).snapshot;
                    case UIState.ViewMode.ShowDiff:
                        // Tables have Diff tables, Memory Map passes in a snapshot age and Tree Map doesn't exist in Diff mode
                        // We have caught an unexpected situation here.
                        Debug.LogError("The current view has no selection handling implemented for the Diff Mode.");
                        snapshotAge = SnapshotAge.None;
                        return (uiState.FirstMode as UIState.SnapshotMode).snapshot;
                    case UIState.ViewMode.ShowSecond:
                        snapshotAge = uiState.FirstSnapshotAge == SnapshotAge.Newer ? SnapshotAge.Older : SnapshotAge.Newer;
                        return (uiState.SecondMode as UIState.SnapshotMode).snapshot;
                    case UIState.ViewMode.ShowNone:
                    default:
                        return null;
                }
            }
        }

        static ulong GetAddress(Table displayTable, long rowIndex, string addressColumnName = "address")
        {
            var col = displayTable.GetColumnByName(addressColumnName);
            if (col == null && addressColumnName == "address") // in some tables it's capitalized, for ... reasons
                col = displayTable.GetColumnByName("Address");
            var typedCol = col as ColumnTyped<ulong>;
            ulong address = 0UL;
            if (displayTable.GetRowCount() > rowIndex)
            {
                if (typedCol != null)
                {
                    address = typedCol.GetRowValue(rowIndex);
                }
                else
                {
                    var rowText = col.GetRowValueString(rowIndex, DefaultDataFormatter.Instance);

                    if (!ulong.TryParse(rowText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address))
                        address = 0;
                }
            }
            return address;
        }

        static long FindAddress(Table displayTable, ulong addressToFind, string addressColumnName = "address")
        {
            var rowIndex = displayTable.GetRowCount() - 1;
            if (rowIndex < 0)
                return -1;
            var col = displayTable.GetColumnByName(addressColumnName);
            if (col == null && addressColumnName == "address") // in some tables it's capitalized, for ... reasons
                col = displayTable.GetColumnByName("Address");
            var typedCol = col as ColumnTyped<ulong>;
            ulong address = 0UL;
            if (displayTable.GetRowCount() > rowIndex)
            {
                if (typedCol != null)
                {
                    do
                    {
                        address = typedCol.GetRowValue(rowIndex);
                    }
                    while (!addressToFind.Equals(address) && --rowIndex >= 0);
                }
                else
                {
                    do
                    {
                        var rowText = col.GetRowValueString(rowIndex, DefaultDataFormatter.Instance);
                        if (!ulong.TryParse(rowText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address))
                            address = 0;
                    }
                    while (!addressToFind.Equals(address) && --rowIndex >= 0);
                }
            }
            return rowIndex;
        }

        static long FindAddress(Table displayTable, ulong addressToFind, SnapshotAge snapshotAge, string addressColumnName = "address")
        {
            var typedDiffCol = GetDiffColumn(displayTable);
            var diffValueToFind = GetDiffValueFromAge(snapshotAge);

            var rowIndex = displayTable.GetRowCount() - 1;
            if (rowIndex < 0)
                return -1;
            var col = displayTable.GetColumnByName(addressColumnName);
            if (col == null && addressColumnName == "address") // in some tables it's capitalized, for ... reasons
                col = displayTable.GetColumnByName("Address");
            var typedCol = col as ColumnTyped<ulong>;
            ulong address = 0UL;
            if (displayTable.GetRowCount() > rowIndex)
            {
                if (typedCol != null)
                {
                    do
                    {
                        address = typedCol.GetRowValue(rowIndex);
                    }
                    while (!(addressToFind.Equals(address) && diffValueToFind.Equals(typedDiffCol.GetRowValue(rowIndex))) && --rowIndex >= 0);
                }
                else
                {
                    do
                    {
                        var rowText = col.GetRowValueString(rowIndex, DefaultDataFormatter.Instance);
                        if (!ulong.TryParse(rowText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address))
                            address = 0;
                    }
                    while (!(addressToFind.Equals(address) && diffValueToFind.Equals(typedDiffCol.GetRowValue(rowIndex))) && --rowIndex >= 0);
                }
            }
            return rowIndex;
        }

        static string GetName(Table displayTable, long rowIndex)
        {
            if (displayTable.GetRowCount() <= rowIndex)
                return null;
            var col = displayTable.GetColumnByName("name");
            return col.GetRowValueString(rowIndex, DefaultDataFormatter.Instance);
        }

        static long FindName(Table displayTable, string nameToFind)
        {
            var rowIndex = displayTable.GetRowCount();
            if (rowIndex < 0)
                return -1;

            var col = displayTable.GetColumnByName("name");
            string name = null;
            do
            {
                name = col.GetRowValueString(rowIndex, DefaultDataFormatter.Instance);
            }
            while (!nameToFind.Equals(name) && --rowIndex >= 0);

            return rowIndex;
        }

        static long FindName(Table displayTable, string nameToFind, SnapshotAge snapshotAge)
        {
            var typedDiffCol = GetDiffColumn(displayTable);
            var diffValueToFind = GetDiffValueFromAge(snapshotAge);

            var rowIndex = displayTable.GetRowCount();
            if (rowIndex < 0)
                return -1;

            var col = displayTable.GetColumnByName("name");
            string name = null;
            do
            {
                name = col.GetRowValueString(rowIndex, DefaultDataFormatter.Instance);
            }
            while (!(nameToFind.Equals(name) && diffValueToFind.Equals(typedDiffCol.GetRowValue(rowIndex))) && --rowIndex >= 0);

            return rowIndex;
        }

        static T GetValue<T>(Table displayTable, long rowIndex, string columnName, out bool success) where T : unmanaged, IComparable
        {
            var col = displayTable.GetColumnByName(columnName);
            T value = default;
            success = false;
            if (rowIndex < 0)
                return value;
            var typedCol = col as ColumnTyped<T>;
            if (displayTable.GetRowCount() > rowIndex)
            {
                if (typedCol != null)
                {
                    do
                    {
                        value = typedCol.GetRowValue(rowIndex);
                    }
                    while ((value.CompareTo(default(T)) == 0 || IsMinusOne(value)) && --rowIndex >= 0);
                }
                else
                {
                    string rowText;
                    do
                    {
                        rowText = col.GetRowValueString(rowIndex, DefaultDataFormatter.Instance);
                    }
                    while (!TryParse<T>(rowText, ref value) && --rowIndex >= 0);
                }
            }
            success = rowIndex >= 0 && displayTable.GetRowCount() > rowIndex;
            return value;
        }

        static long FindValue<T>(Table displayTable, T valueToFind, string columnName) where T : unmanaged, IComparable
        {
            var rowIndex = displayTable.GetRowCount() - 1;
            if (rowIndex < 0)
                return -1;
            var col = displayTable.GetColumnByName(columnName);
            T value = default;
            var typedCol = col as ColumnTyped<T>;
            if (typedCol != null)
            {
                do
                {
                    value = typedCol.GetRowValue(rowIndex);
                }
                while (!valueToFind.Equals(value) && --rowIndex >= 0);
            }
            else
            {
                string rowText;
                do
                {
                    rowText = col.GetRowValueString(rowIndex, DefaultDataFormatter.Instance);
                }
                while (!TryParse<T>(rowText, ref value) && !valueToFind.Equals(value) && --rowIndex >= 0);
            }
            return rowIndex;
        }

        static long FindValue<T>(Table displayTable, T valueToFind, string columnName, SnapshotAge snapshotAge) where T : unmanaged, IComparable
        {
            var typedDiffCol = GetDiffColumn(displayTable);
            var diffValueToFind = GetDiffValueFromAge(snapshotAge);

            var rowIndex = displayTable.GetRowCount() - 1;
            if (rowIndex < 0)
                return -1;
            var col = displayTable.GetColumnByName(columnName);
            T value = default;

            var typedCol = col as ColumnTyped<T>;
            if (typedCol != null)
            {
                do
                {
                    value = typedCol.GetRowValue(rowIndex);
                }
                while (!(valueToFind.Equals(value) && diffValueToFind.Equals(typedDiffCol.GetRowValue(rowIndex))) && --rowIndex >= 0);
            }
            else
            {
                string rowText;
                do
                {
                    rowText = col.GetRowValueString(rowIndex, DefaultDataFormatter.Instance);
                }
                while (!TryParse<T>(rowText, ref value) && !(valueToFind.Equals(value) && diffValueToFind.Equals(typedDiffCol.GetRowValue(rowIndex))) && --rowIndex >= 0);
            }
            return rowIndex;
        }

        const string k_DiffColumnName = "Diff";
        static ColumnTyped<DiffTable.DiffResult> GetDiffColumn(Table displayTable)
        {
            var diffCol = displayTable.GetColumnByName(k_DiffColumnName);
            var typedDiffCol = diffCol as ColumnTyped<DiffTable.DiffResult>;
            if (typedDiffCol == null)
                Debug.LogError("Could not find Diff Column");
            return typedDiffCol;
        }

        static DiffTable.DiffResult GetDiffValueFromAge(SnapshotAge snapshotAge)
        {
            switch (snapshotAge)
            {
                case SnapshotAge.None:
                    return DiffTable.DiffResult.Same;
                case SnapshotAge.Older:
                    return DiffTable.DiffResult.New;
                case SnapshotAge.Newer:
                    return DiffTable.DiffResult.Deleted;
                default:
                    break;
            }
            return DiffTable.DiffResult.None;
        }

        static bool TryParse<T>(string rowText, ref T value) where T : unmanaged
        {
            if (string.IsNullOrEmpty(rowText))
                return false;
            if (value is long)
            {
                long v;
                var result = long.TryParse(rowText, out v);
                unsafe
                {
                    T* ptr = (T*)&v;
                    value = *ptr;
                }
                return result;
            }
            if (value is ulong)
            {
                ulong v;
                var result = ulong.TryParse(rowText, out v);
                unsafe
                {
                    T* ptr = (T*)&v;
                    value = *ptr;
                }
                return result;
            }
            if (value is int)
            {
                int v;
                var result = int.TryParse(rowText, out v);
                unsafe
                {
                    T* ptr = (T*)&v;
                    value = *ptr;
                }
                return result;
            }
            return false;
        }

        static bool IsMinusOne<T>(T value) where T : unmanaged
        {
            if (value is long)
            {
                unsafe
                {
                    long* ptr = (long*)&value;
                    return *ptr == -1L;
                }
            }
            if (value is ulong)
            {
                return false;
            }
            if (value is int)
            {
                unsafe
                {
                    int* ptr = (int*)&value;
                    return *ptr == -1;
                }
            }
            return false;
        }
    }
}

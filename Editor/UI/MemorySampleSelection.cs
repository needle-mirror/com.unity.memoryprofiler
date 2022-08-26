using System;
using System.Globalization;
using Unity.MemoryProfiler.Editor.Database;
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
        Group,
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

        // Title & Description used in Group selection types
        public readonly string Title;
        public readonly string Description;

        readonly SnapshotAge m_SnapshotAge;

        public bool Equals(MemorySampleSelection other)
        {
            return Type == other.Type &&
                Rank == other.Rank &&
                ItemIndex == other.ItemIndex &&
                RowIndex == other.RowIndex &&
                SecondaryItemIndex == other.SecondaryItemIndex &&
                TertiaryItemIndex == other.TertiaryItemIndex &&
                Table == other.Table &&
                Title == other.Title &&
                Description == other.Description;
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
            Title = null;
            Description = null;
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

        MemorySampleSelection(
            MemorySampleSelectionType type,
            long itemIndex,
            SnapshotAge snapshotAge = SnapshotAge.None) : this()
        {
            Type = type;
            Rank = MemorySampleSelectionRank.MainSelection;
            ItemIndex = itemIndex;
            m_SnapshotAge = snapshotAge;
        }

        public static MemorySampleSelection FromUnifiedObjectIndex(
            long unifiedObjectIndex,
            SnapshotAge snapshotAge = SnapshotAge.None)
        {
            return new MemorySampleSelection(
                MemorySampleSelectionType.UnifiedObject,
                unifiedObjectIndex,
                snapshotAge);
        }

        public static MemorySampleSelection FromNativeObjectIndex(
            long nativeObjectIndex,
            SnapshotAge snapshotAge = SnapshotAge.None)
        {
            return new MemorySampleSelection(
                MemorySampleSelectionType.NativeObject,
                nativeObjectIndex,
                snapshotAge);
        }

        public static MemorySampleSelection FromNativeTypeIndex(
            long nativeTypeIndex,
            SnapshotAge snapshotAge = SnapshotAge.None)
        {
            return new MemorySampleSelection(
                MemorySampleSelectionType.NativeType,
                nativeTypeIndex,
                snapshotAge);
        }

        public static MemorySampleSelection FromManagedObjectIndex(
            long managedObjectIndex,
            SnapshotAge snapshotAge = SnapshotAge.None)
        {
            return new MemorySampleSelection(
                MemorySampleSelectionType.ManagedObject,
                managedObjectIndex,
                snapshotAge);
        }

        public static MemorySampleSelection FromManagedTypeIndex(
            long managedTypeIndex,
            SnapshotAge snapshotAge = SnapshotAge.None)
        {
            return new MemorySampleSelection(
                MemorySampleSelectionType.ManagedType,
                managedTypeIndex,
                snapshotAge);
        }

        public MemorySampleSelection(string title, string description)
        {
            Type = MemorySampleSelectionType.Group;
            Rank = MemorySampleSelectionRank.MainSelection;
            ItemIndex = -1;
            RowIndex = -1;
            SecondaryItemIndex = -1;
            TertiaryItemIndex = -1;
            m_SnapshotAge = SnapshotAge.None;
            Table = null;
            Title = title;
            Description = description;
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
                    case MemorySampleSelectionType.Group:
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

        static CachedSnapshot GetRelevantOpenSnapshot(UIState uiState, ref SnapshotAge snapshotAge, long rowIndex = -1)
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
    }
}

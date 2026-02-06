using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.EnumerationUtilities;
using Unity.MemoryProfiler.Editor.Format;
using Unity.Profiling;
using UnityEditor;
using Debug = UnityEngine.Debug;
using RuntimePlatform = UnityEngine.RuntimePlatform;

namespace Unity.MemoryProfiler.Editor
{
    internal partial class CachedSnapshot
    {
        public class EntriesMemoryMapCache : IDisposable
        {
            // We assume this address space not to be used in the real life
            const ulong k_GraphicsResourcesStartFakeAddress = 0x8000_0000_0000_0000UL;

            public enum AddressPointType : byte
            {
                EndPoint,
                StartPoint,
            }

            readonly public struct AddressPoint : IComparable<AddressPoint>
            {
                public readonly ulong Address;
                public readonly long ChildrenCount;
                public readonly SourceIndex Source;
                public readonly AddressPointType PointType;

                public AddressPoint(ulong address, long childrenCount, SourceIndex source, AddressPointType pointType)
                {
                    Address = address;
                    ChildrenCount = childrenCount;
                    Source = source;
                    PointType = pointType;
                }

                public AddressPoint(AddressPoint point, long childrenCount)
                {
                    Address = point.Address;
                    Source = point.Source;
                    PointType = point.PointType;
                    ChildrenCount = childrenCount;
                }

                int IComparable<AddressPoint>.CompareTo(AddressPoint other)
                {
                    var ret = Address.CompareTo(other.Address);
                    if (ret == 0)
                    {
                        bool isEndPointA = PointType == AddressPointType.EndPoint;
                        bool isEndPointB = other.PointType == AddressPointType.EndPoint;

                        if (isEndPointA != isEndPointB)
                        {
                            // Comparing start to end point, end point should be first
                            return isEndPointA ? -1 : 1;
                        }
                        else
                        {
                            // Comparing start to start or end to end, prioritize by source type
                            // cast to byte to avoid allocating a default comparer for every comparison.
                            ret = ((byte)Source.Id).CompareTo((byte)other.Source.Id);
                            return isEndPointA ? -ret : ret;
                        }
                    }
                    return ret;
                }
            }

            CachedSnapshot m_Snapshot;
            long m_ItemsCount;
            // The snapshot can easily contain int.MaxValue amount of objects, even more easily half that in Managed Objects
            // and very realistically more than int.MaxValue elements for the total that this list would contain.
            // This therefore has to be a long indexed data type like DynamicArray.
            DynamicArray<AddressPoint> m_CombinedData;

            public EntriesMemoryMapCache(CachedSnapshot snapshot)
            {
                m_Snapshot = snapshot;
                m_ItemsCount = 0;
                m_CombinedData = default;
            }

            internal EntriesMemoryMapCache(DynamicArray<AddressPoint> m_Data)
            {
                m_Snapshot = null;
                m_ItemsCount = m_Data.Count;
                m_CombinedData = m_Data;

                SortPoints();
                PostProcess();
            }

            public long Count
            {
                get
                {
                    // Count is used to trigger Build() from performance tests without using reflection and much other overhead.
                    // Adjust Performance tests if this logic changes.
                    Build();
                    return m_ItemsCount;
                }
            }

            public DynamicArray<AddressPoint> Data
            {
                get
                {
                    Build();
                    return m_CombinedData;
                }
            }

            public delegate void ForEachAction(long index, ulong address, ulong size, SourceIndex source);

            /// <summary>
            /// Iterates all children memory spans under specified parent index.
            /// Use -1 parentIndex for a root level
            /// </summary>
            public void ForEachChild(long parentIndex, ForEachAction action)
            {
                Build();

                if (m_ItemsCount == 0)
                    return;

                // Children iteration rules:
                // - Children go right after the parent in the data array
                // - If children starts not at the parent start address,
                //   we generate "fake" child of the parent type, which should be
                //   treated as "reserved" space
                // - If children ends before the parent end point,
                //   we generate "fake" child of the parent type, which should be
                //   treated as "reserved" space
                var index = parentIndex == -1 ? 0 : parentIndex;
                var endIndex = parentIndex == -1 ? m_ItemsCount - 1 : index + m_CombinedData[parentIndex].ChildrenCount + 1;
                var nextIndex = index + 1;
                while (index < endIndex)
                {
                    var point = m_CombinedData[index];
                    var nextPoint = m_CombinedData[nextIndex];
                    var size = nextPoint.Address - point.Address;

                    // Don't report on free, ignored or zero-sized spans
                    if ((point.Source.Id != SourceIndex.SourceId.None) && (size > 0))
                        action(index, point.Address, size, point.Source);

                    index = nextIndex;
                    nextIndex = index + 1 + nextPoint.ChildrenCount;
                }
            }

            /// <summary>
            /// Flat scan all memory regions
            /// </summary>
            public void ForEachFlat(ForEachAction action)
            {
                Build();

                if (m_ItemsCount == 0)
                    return;

                SourceIndex? currentSystemRegion = null;
                for (long i = 0; i < m_ItemsCount - 1; i++)
                {
                    var cur = m_CombinedData[i];

                    // Register current system region
                    if (cur.Source.Id == SourceIndex.SourceId.SystemMemoryRegion)
                        currentSystemRegion = cur.Source;
                    else if (cur.Source.Id == SourceIndex.SourceId.None)
                        currentSystemRegion = null;

                    // Ignore free memory regions
                    if (cur.Source.Id == SourceIndex.SourceId.None)
                        continue;

                    if (m_Snapshot.HasSystemMemoryRegionsInfo)
                    {
                        // Ignore items outside of system regions
                        // They exist due to time difference between allocation
                        // capture and regions capture
                        //
                        // Nb! Special case for gfx resources, as we add them
                        // with fake address and without system regions root
                        if (!currentSystemRegion.HasValue && (cur.Source.Id != SourceIndex.SourceId.GfxResource))
                            continue;
                    }

                    // Calculate size
                    var next = m_CombinedData[i + 1];
                    var size = next.Address - cur.Address;

                    // Ignore zero sized enitites
                    if (size == 0)
                        continue;

                    action(i, cur.Address, size, cur.Source);
                }
            }

            public delegate void ForEachFlatWithResidentSizeAction(long index, ulong address, ulong size, ulong residentSize, SourceIndex source);

            /// <summary>
            /// Flat scan all memory regions
            /// </summary>
            public void ForEachFlatWithResidentSize(ForEachFlatWithResidentSizeAction action)
            {
                Build();

                if (m_ItemsCount == 0)
                    return;

                SourceIndex? currentSystemRegion = null;
                for (long i = 0; i < m_ItemsCount - 1; i++)
                {
                    var cur = m_CombinedData[i];

                    // Register current system region
                    if (cur.Source.Id == SourceIndex.SourceId.SystemMemoryRegion)
                        currentSystemRegion = cur.Source;
                    else if (cur.Source.Id == SourceIndex.SourceId.None)
                        currentSystemRegion = null;

                    // Ignore free memory regions
                    if (cur.Source.Id == SourceIndex.SourceId.None)
                        continue;

                    // Calculate size
                    var next = m_CombinedData[i + 1];
                    var size = next.Address - cur.Address;

                    // Ignore zero sized entities
                    if (size == 0)
                        continue;

                    var residentSize = 0UL;
                    if (m_Snapshot.HasSystemMemoryRegionsInfo)
                    {
                        if (cur.Source.Id == SourceIndex.SourceId.GfxResource)
                        {
                            // Special case for gfx resources
                            // We consider them to be always resident
                            // and they can be outside of system regions
                            residentSize = size;
                        }
                        else if (!currentSystemRegion.HasValue)
                        {
                            // Ignore items outside of system regions
                            // They exist due to time difference between allocation
                            // capture and regions capture
                            continue;
                        }
                        else if (m_Snapshot.HasSystemMemoryResidentPages)
                        {
                            // Calculate resident size based on the root
                            // system region this object resides in
                            residentSize = m_Snapshot.SystemMemoryResidentPages.CalculateResidentMemory(m_Snapshot, currentSystemRegion.Value.Index, cur.Address, size, cur.Source.Id);
                        }
                    }

                    action(i, cur.Address, size, residentSize, cur.Source);
                }
            }

            public string GetName(long index)
            {
                if (m_Snapshot == null)
                    return string.Empty;

                var item = m_CombinedData[index];
                return item.Source.GetName(m_Snapshot);
            }

            public enum PointType
            {
                Free,
                Untracked,
                NativeReserved,
                Native,
                ManagedReserved,
                Managed,
                Device,
                Mapped,
                Shared,
                AndroidRuntime,
            }

            public PointType GetPointType(SourceIndex source)
            {
                switch (source.Id)
                {
                    case SourceIndex.SourceId.None:
                        return PointType.Free;
                    case SourceIndex.SourceId.SystemMemoryRegion:
                    {
                        if (m_Snapshot.MetaData.TargetInfo.HasValue)
                        {
                            switch (m_Snapshot.MetaData.TargetInfo.Value.RuntimePlatform)
                            {
                                case RuntimePlatform.Android:
                                {
                                    var name = m_Snapshot.SystemMemoryRegions.RegionName[source.Index];
                                    if (name.StartsWith("[anon:dalvik-"))
                                        return PointType.AndroidRuntime;
                                    else if (name.StartsWith("/dev/ashmem/dalvik-"))
                                        return PointType.AndroidRuntime;
                                    else if (name.StartsWith("/dev/"))
                                        return PointType.Device;
                                    break;
                                }
                                default:
                                    break;
                            }
                        }

                        var regionType = m_Snapshot.SystemMemoryRegions.RegionType[source.Index];
                        switch ((SystemMemoryRegionEntriesCache.MemoryType)regionType)
                        {
                            case SystemMemoryRegionEntriesCache.MemoryType.Device:
                                return PointType.Device;
                            case SystemMemoryRegionEntriesCache.MemoryType.Mapped:
                                return PointType.Mapped;
                            case SystemMemoryRegionEntriesCache.MemoryType.Shared:
                                return PointType.Shared;
                            default:
                                return PointType.Untracked;
                        }
                    }

                    case SourceIndex.SourceId.NativeMemoryRegion:
                    {
                        // On some consoles, we see some "Native" allocations which are actually graphics memory.
                        // This is likely an issue for us because these platforms have the combination of being
                        // unified memory that uses BaseAllocators for graphics, with no VirtualQuery equivalent.
                        // See what region the memory was allocated in to let us decide how to tag it.
                        if (m_Snapshot.UseDeviceMemoryForGraphics)
                        {
                            long memIndex = m_Snapshot.NativeMemoryRegions.ParentIndex[source.Index];
                            if (m_Snapshot.NativeMemoryRegions.GPUAllocatorIndices.Contains(memIndex))
                                return PointType.Device; // In this case, this is specifically GPU reserved memory, which we don't properly support at time of writing.
                        }
                        return PointType.NativeReserved;
                    }
                    case SourceIndex.SourceId.NativeAllocation:
                    {
                        // On some consoles, we see some "Native" allocations which are actually graphics memory.
                        // This is likely an issue for us because these platforms have the combination of being
                        // unified memory that uses BaseAllocators for graphics, with no VirtualQuery equivalent.
                        // See what region the memory was allocated in to let us decide how to tag it.
                        if (m_Snapshot.UseDeviceMemoryForGraphics)
                        {
                            var memIndex = m_Snapshot.NativeAllocations.MemoryRegionIndex[source.Index];
                            var parentIndex = m_Snapshot.NativeMemoryRegions.ParentIndex[memIndex];
                            memIndex = (parentIndex != 0) ? parentIndex : memIndex;

                            if (m_Snapshot.NativeMemoryRegions.GPUAllocatorIndices.Contains(memIndex))
                                return PointType.Device;
                        }

                        return PointType.Native;
                    }
                    case SourceIndex.SourceId.NativeObject:
                        return PointType.Native;

                    case SourceIndex.SourceId.ManagedHeapSection:
                    {
                        var sectionType = m_Snapshot.ManagedHeapSections.SectionType[source.Index];
                        switch (sectionType)
                        {
                            case MemorySectionType.VirtualMachine:
                                return PointType.Managed;
                            case MemorySectionType.GarbageCollector:
                                return PointType.ManagedReserved;
                            default:
                                Debug.Assert(false, $"Unknown managed heap section type {sectionType}, please report a bug.");
                                return PointType.ManagedReserved;
                        }
                    }
                    case SourceIndex.SourceId.ManagedObject:
                        return PointType.Managed;

                    case SourceIndex.SourceId.GfxResource:
                        return PointType.Device;

                    default:
                        Debug.Assert(false, $"Unknown source link type {source.Id}, please report a bug.");
                        return PointType.Free;
                }
            }

            // These markers are used in Performance tests. If they are changed or no longer used, adjust the tests accordingly
            public const string BuildMarkerName = "EntriesMemoryMapCache.Build";
            public const string BuildAddPointsMarkerName = "EntriesMemoryMapCache.AddPoints";
            public const string BuildSortPointsMarkerName = "EntriesMemoryMapCache.SortPoints";
            public const string BuildPostProcessMarkerName = "EntriesMemoryMapCache.PostProcess";

            static readonly ProfilerMarker k_Build = new ProfilerMarker(BuildMarkerName);
            static readonly ProfilerMarker k_BuildAddPoints = new ProfilerMarker(BuildAddPointsMarkerName);
            static readonly ProfilerMarker k_BuildSortPoints = new ProfilerMarker(BuildSortPointsMarkerName);
            static readonly ProfilerMarker k_BuildPostProcess = new ProfilerMarker(BuildPostProcessMarkerName);

            void Build()
            {
                if (m_CombinedData.IsCreated)
                {
                    // don't allocate the EnumerationStatus if it isn't necessary
                    return;
                }

                var status = new EnumerationStatus(BuildStepCount + 1 /*final step*/);
                var iterator = Build(status);
                while (iterator.MoveNext()) { }
            }

            public const int BuildStepCount = 3;
            public IEnumerator<EnumerationStatus> Build(EnumerationStatus status)
            {
                if (m_CombinedData.IsCreated)
                {
                    for (int i = 0; i < BuildStepCount; i++)
                    {
                        status.IncrementStep("EntriesMemoryMapCache was already built.");
                    }
                    yield break;
                }

                yield return status.IncrementStep("EntriesMemoryMapCache: Mapping data to address spectrum");
                {
                    using var _ = k_Build.Auto();
                    using (k_BuildAddPoints.Auto())
                        AddPoints();
                }

                yield return status.IncrementStep("EntriesMemoryMapCache: Sorting");
                {
                    using var _ = k_Build.Auto();
                    using (k_BuildSortPoints.Auto())
                        SortPoints();
                }

                yield return status.IncrementStep("EntriesMemoryMapCache: Post processing");
                {
                    using var _ = k_Build.Auto();
                    using (k_BuildPostProcess.Auto())
                        PostProcess();
                }
            }

            void AddPoints()
            {
                // We're building a sorted by address list of ranges with
                // a link to a source of the data
                var maxPointsCount = (
                    m_Snapshot.SystemMemoryRegions.Count +
                    m_Snapshot.NativeMemoryRegions.Count +
                    m_Snapshot.ManagedHeapSections.Count +
                    m_Snapshot.NativeAllocations.Count +
                    m_Snapshot.NativeObjects.Count +
                    m_Snapshot.CrawledData.ManagedObjects.Count +
                    m_Snapshot.NativeGfxResourceReferences.Count) * 2;
                m_CombinedData = new DynamicArray<AddressPoint>(maxPointsCount, Allocator.Persistent);

                // Add OS reported system memory regions
                m_ItemsCount = AddPoints(
                    m_ItemsCount,
                    m_CombinedData,
                    SourceIndex.SourceId.SystemMemoryRegion,
                    Convert.ToInt32(m_Snapshot.SystemMemoryRegions.Count),
                    (long i) =>
                    {
                        var address = m_Snapshot.SystemMemoryRegions.RegionAddress[i];
                        var size = m_Snapshot.SystemMemoryRegions.RegionSize[i];
                        var name = m_Snapshot.SystemMemoryRegions.RegionName[i];
                        var type = m_Snapshot.SystemMemoryRegions.RegionType[i];
                        return (true, address, size);
                    });

                // Add MemoryManager native allocators allocated memory
                // By default it's treated as "reserved" unless allocations
                // are reported
                m_ItemsCount = AddPoints(
                    m_ItemsCount,
                    m_CombinedData,
                    SourceIndex.SourceId.NativeMemoryRegion,
                    Convert.ToInt32(m_Snapshot.NativeMemoryRegions.Count),
                    (long i) =>
                    {
                        var address = m_Snapshot.NativeMemoryRegions.AddressBase[i];
                        var size = m_Snapshot.NativeMemoryRegions.AddressSize[i];
                        var name = m_Snapshot.NativeMemoryRegions.MemoryRegionName[i];

                        // We exclude "virtual" allocators as they report non-committed memory
                        bool valid = (size > 0) && !name.Contains("Virtual Memory") && address != 0UL;

                        return (valid, address, size);
                    });

                // Add Mono reported managed sections
                m_ItemsCount = AddPoints(
                    m_ItemsCount,
                    m_CombinedData,
                    SourceIndex.SourceId.ManagedHeapSection,
                    Convert.ToInt32(m_Snapshot.ManagedHeapSections.Count),
                    (long i) =>
                    {
                        var address = m_Snapshot.ManagedHeapSections.StartAddress[i];
                        var size = m_Snapshot.ManagedHeapSections.SectionSize[i];
                        var sectionType = m_Snapshot.ManagedHeapSections.SectionType[i];
                        bool valid = (size > 0);
                        return (valid, address, size);
                    });

                // Add individual native allocations
                m_ItemsCount = AddPoints(
                    m_ItemsCount,
                    m_CombinedData,
                    SourceIndex.SourceId.NativeAllocation,
                    Convert.ToInt32(m_Snapshot.NativeAllocations.Count),
                    (long i) =>
                    {
                        var address = m_Snapshot.NativeAllocations.Address[i];
                        var size = m_Snapshot.NativeAllocations.Size[i];
                        bool valid = (size > 0);
                        return (valid, address, size);
                    });

                // Add native objects
                m_ItemsCount = AddPoints(
                    m_ItemsCount,
                    m_CombinedData,
                    SourceIndex.SourceId.NativeObject,
                    Convert.ToInt32(m_Snapshot.NativeObjects.Count),
                    (long i) =>
                    {
                        var address = m_Snapshot.NativeObjects.NativeObjectAddress[i];
                        var size = m_Snapshot.NativeObjects.Size[i];
                        bool valid = (size > 0);
                        return (valid, address, size);
                    });

                // Add managed objects
                m_ItemsCount = AddPoints(
                    m_ItemsCount,
                    m_CombinedData,
                    SourceIndex.SourceId.ManagedObject,
                    Convert.ToInt32(m_Snapshot.CrawledData.ManagedObjects.Count),
                    (long i) =>
                    {
                        ref readonly var info = ref m_Snapshot.CrawledData.ManagedObjects[i];
                        var address = info.PtrObject;
                        var size = (ulong)info.Size;
                        bool valid = (size > 0);
                        return (valid, address, size);
                    });
            }

            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            bool IsEndPoint(AddressPoint p) => p.PointType == AddressPointType.EndPoint;

            // We use ChildCount to store begin/end pair IDs, so that
            // we can check that they are from the same pair
            // ChildrenCount is updated to be the actual ChildrenCount in PostProcess once a point is ended and removed from the processing stack.
            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            static long GetPointId(AddressPoint p) => p.ChildrenCount;

            void SortPoints()
            {
                DynamicArrayAlgorithms.IntrospectiveSort(m_CombinedData, 0, m_ItemsCount);
            }

            struct FindAddressPointPredicate : IRefComparer<long>
            {
                public DynamicArray<AddressPoint> CombinedData;

                public readonly int Compare(ref long valueInHierarchyStack, ref long valueToFind)
                {
                    return EntriesMemoryMapCache.GetPointId(CombinedData[valueInHierarchyStack]).CompareTo(valueToFind);
                }
            }
            /// <summary>
            /// Scans all points and updates flags and childs count
            /// based on begin/end flags
            /// </summary>
            void PostProcess()
            {
                const int kMaxStackDepth = 16;
                var hierarchyStack = new DynamicArray<long>(0, kMaxStackDepth, Allocator.Temp);
#if DEBUG_VALIDATION
                var connectionsToContainedObject = new List<ObjectData>();
                var connectionsToEnclosedObject = new List<ObjectData>();
#endif

                for (long i = 0; i < m_ItemsCount; i++)
                {
                    var point = m_CombinedData[i];

                    if (IsEndPoint(point))
                    {
                        if (hierarchyStack.Count <= 0)
                        {
                            // Lose end point. This is valid situation as memory snapshot
                            // capture process modifies memory and system, native and managed
                            // states might be slightly out of sync and have overlapping regions
                            m_CombinedData[i] = new AddressPoint(point.Address, 0, new SourceIndex(), point.PointType);
                            continue;
                        }

                        // We use ChildCount to store begin/end pair IDs, so that
                        // we can check that they are from the same pair
                        var startPointIndex = hierarchyStack.Peek();
                        var startPoint = m_CombinedData[startPointIndex];
                        if (GetPointId(startPoint) != GetPointId(point))
                        {
                            // Non-matching end point. This is valid situation (see "lose end point" comment).
                            // Try to find matching starting point
                            var index = DynamicArrayAlgorithms.FindIndex(hierarchyStack, GetPointId(point), new FindAddressPointPredicate() { CombinedData = m_CombinedData });
                            if (index < 0)
                            {
                                // No starting point, ignore the point entirely
                                // and set its source to the previous point source
                                // as it should be treated as a continuation of the
                                // previous object
                                m_CombinedData[i] = new AddressPoint(point.Address, 0, m_CombinedData[i - 1].Source, point.PointType);
                                continue;
                            }

                            TerminateUndercutRegions(index, hierarchyStack.Count, hierarchyStack, i);
                            // if there is matching begin -> unwind stack to that point
                            startPointIndex = hierarchyStack[index];
                            hierarchyStack.Resize(index + 1, false);
                        }

                        // Remove from stack
                        hierarchyStack.Pop();

                        // Replace start point id with actual children count
                        m_CombinedData[startPointIndex] = new AddressPoint(m_CombinedData[startPointIndex], i - startPointIndex - 1);

                        // Replace end point with continuation of the parent range
                        if (hierarchyStack.Count > 0)
                        {
                            var parentPointIndex = hierarchyStack[hierarchyStack.Count - 1];
                            var parentPoint = m_CombinedData[parentPointIndex];
                            m_CombinedData[i] = new AddressPoint(point.Address, 0, parentPoint.Source, point.PointType);
                        }
                        else
                        {
                            // Last in the hierarchy, restart free region range
                            m_CombinedData[i] = new AddressPoint(point.Address, 0, new SourceIndex(), point.PointType);
                        }
                    }
                    else
                    {
                        if (hierarchyStack.Count > 0 && m_CombinedData[hierarchyStack.Peek()].Source.Id == point.Source.Id)
                        {
                            // The element this element is supposedly nested within is of the same type. Nesting of same types points at incorrect data
                            var parentPointIndex = hierarchyStack.Peek();
                            var parentPoint = m_CombinedData[parentPointIndex];

                            var startPointStackLevel = hierarchyStack.Count - 1;
                            TerminateUndercutRegions(startPointStackLevel, hierarchyStack.Count, hierarchyStack, i);

                            hierarchyStack.Resize(startPointStackLevel, false);

                            // For Native Objects this is a known issue as their GPU size was included in older versions of the backend
                            // So we only report this issue for newer snapshot versions as a reminder to fix it (i.e. bumping the above version is fine but).
                            // Other types having the same issue is not a known issue, so we'd want to know about it for these
                            if (point.Source.Id != SourceIndex.SourceId.NativeObject || m_Snapshot.m_SnapshotVersion > FormatVersion.SystemMemoryResidentPagesVersion)
                            {
                                if (point.Source.Id == SourceIndex.SourceId.ManagedObject)
                                {
                                    ref var enclosingManagedObject = ref m_Snapshot.CrawledData.ManagedObjects[parentPoint.Source.Index];
                                    ref var containedManagedObject = ref m_Snapshot.CrawledData.ManagedObjects[point.Source.Index];
#if DEBUG_VALIDATION
                                    ObjectConnection.GetAllReferencingObjects(m_Snapshot, point.Source, connectionsToContainedObject);
                                    connectionsToEnclosedObject.Clear();
                                    try
                                    {
                                        ObjectConnection.GetAllReferencingObjects(m_Snapshot, parentPoint.Source, connectionsToEnclosedObject);
                                    }
                                    catch
                                    {
                                        Debug.LogError($"Failed to get field description for managed object idx: {point.Source.Index} of type {ObjectData.FromSourceLink(m_Snapshot, point.Source).GenerateTypeName(m_Snapshot)}");
                                    }
                                    string GetFieldInfo(List<ObjectData> connections)
                                    {
                                        if (connections == null || connections.Count == 0)
                                        {
                                            return "(No connections) ";
                                        }
                                        if (connections[0].IsField())
                                            return $"(Found as held by {connections[0].GenerateTypeName(m_Snapshot)} via {connections[0].GetFieldDescription(m_Snapshot)}) ";
                                        if (connections[0].IsArrayItem())
                                            return $"(Found as held by {connections[0].GenerateTypeName(m_Snapshot)} via {connections[0].GenerateArrayDescription(m_Snapshot, true, true)}) ";
                                        if (connections[0].isNativeObject)
                                            return $"(Found as held by {connections[0].GenerateTypeName(m_Snapshot)} named \"{m_Snapshot.NativeObjects.ObjectName[connections[0].nativeObjectIndex]}\") ";
                                        return $"(Found as held by {connections[0].GenerateTypeName(m_Snapshot)}) ";
                                    }
                                    Debug.LogWarning($"The snapshot contains faulty data, a Managed Object item ({ManagedObjectTools.ProduceManagedObjectName(containedManagedObject, m_Snapshot)} index: {containedManagedObject.ManagedObjectIndex}) of type {m_Snapshot.TypeDescriptions.TypeDescriptionName[containedManagedObject.ITypeDescription]} and size {containedManagedObject.Size} {GetFieldInfo(connectionsToContainedObject)}" +
                                        $"was nested within a Managed Object ({ManagedObjectTools.ProduceManagedObjectName(enclosingManagedObject, m_Snapshot)} index: {enclosingManagedObject.ManagedObjectIndex}) of the type {m_Snapshot.TypeDescriptions.TypeDescriptionName[enclosingManagedObject.ITypeDescription]} and size {enclosingManagedObject.Size} {GetFieldInfo(connectionsToEnclosedObject)} (Memory Map index: {i})!");
#endif
                                    // detract the amount of memory that overlaps so that at least the totals are correct
                                    var enclosingManagedObjectSizeThatOverlaps = enclosingManagedObject.Size - (long)(containedManagedObject.PtrObject - enclosingManagedObject.PtrObject);
                                    if (enclosingManagedObjectSizeThatOverlaps < 0)
                                    {
                                        enclosingManagedObjectSizeThatOverlaps = 0;
                                        enclosingManagedObject.Size -= enclosingManagedObjectSizeThatOverlaps;
                                    }
                                }
#if DEBUG_VALIDATION
                                else
                                    Debug.LogWarning($"The snapshot contains faulty data, an item of type {point.Source.Id} was nested within an item of the same type (index {i})!");
#endif
                            }
                        }

                        hierarchyStack.Push(i);
                    }
                }
            }


            void TerminateUndercutRegions(long fromStackLevel, long toStackLevel, DynamicArray<long> hierarchyStack, long currentCombinedDataIndex)
            {
                // Terminate all under-cut regions
                for (long j = fromStackLevel; j < toStackLevel; j++)
                {
                    var dataIndex = hierarchyStack[j];
                    m_CombinedData[dataIndex] = new AddressPoint(m_CombinedData[dataIndex], currentCombinedDataIndex - dataIndex - 1);
                }
            }

            // Adds a fixed number of points from a container of the specific type
            static long AddPoints(long index, DynamicArray<AddressPoint> data, SourceIndex.SourceId sourceName, long count, Func<long, (bool valid, ulong address, ulong size)> accessor)
            {
                for (long i = 0; i < count; i++)
                {
                    var (valid, address, size) = accessor(i);

                    if (!valid || (size == 0))
                        continue;

                    var id = index;
                    data[index++] = new AddressPoint(
                        address,
                        id,
                        new SourceIndex(sourceName, i),
                        AddressPointType.StartPoint
                    );

                    data[index++] = new AddressPoint(
                        address + size,
                        id,
                        new SourceIndex(sourceName, i),
                        AddressPointType.EndPoint
                    );
                }

                return index;
            }

            public void DebugPrintToFile()
            {
                using (var file = new StreamWriter("hierarchy.txt"))
                {
                    DebugPrintToFile(file, 0, m_ItemsCount, "");
                }

                using (var file = new StreamWriter("labels.txt"))
                {
                    for (long i = 0; i < m_Snapshot.NativeMemoryLabels.Count; i++)
                    {
                        var name = m_Snapshot.NativeMemoryLabels.MemoryLabelName[i];
                        var size = m_Snapshot.NativeMemoryLabels.MemoryLabelSizes[i];
                        file.WriteLine($"[{i:D8}] - {EditorUtility.FormatBytes((long)size),8} - {name}");
                    }
                }
                using (var file = new StreamWriter("rootreferences.txt"))
                {
                    for (long i = 0; i < m_Snapshot.NativeRootReferences.Count; i++)
                    {
                        var areaName = m_Snapshot.NativeRootReferences.AreaName[i];
                        var objectName = m_Snapshot.NativeRootReferences.ObjectName[i];
                        var size = m_Snapshot.NativeRootReferences.AccumulatedSize[i];
                        var id = m_Snapshot.NativeRootReferences.Id[i];
                        file.WriteLine($"[{i:D8}] - {EditorUtility.FormatBytes((long)size),8} - {id:D8} - {areaName}:{objectName}");
                    }
                }
            }

            void DebugPrintToFile(StreamWriter file, long start, long count, string prefix)
            {
                for (long i = 0; i < count; i++)
                {
                    var cur = m_CombinedData[start + i];

                    var size = 0UL;
                    if (start + i + 1 < m_CombinedData.Count)
                        size = m_CombinedData[start + i + 1].Address - cur.Address;

                    file.WriteLine($"{prefix,-5} [{start + i:D8}] - {cur.Address:X16} - {EditorUtility.FormatBytes((long)size),8} - {cur.ChildrenCount:D8} - {cur.Source.Id,20} - {GetName(start + i)}");

                    if (cur.ChildrenCount > 0)
                    {
                        DebugPrintToFile(file, start + i + 1, cur.ChildrenCount, prefix + "-");
                        i += cur.ChildrenCount;
                    }
                }
            }

            public void Dispose()
            {
                m_CombinedData.Dispose();
            }
        }
    }
}

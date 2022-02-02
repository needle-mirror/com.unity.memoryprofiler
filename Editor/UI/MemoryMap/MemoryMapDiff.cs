using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor.UI.MemoryMap
{
    internal class MemoryMapDiff : MemoryMapBase
    {
        [Flags]
        public enum DisplayElements
        {
            RegionDiff = 1 << 0,
            AllocationsDiff = 1 << 1,
            ManagedObjectsDiff = 1 << 2,
            NativeObjectsDiff = 1 << 3,
            VirtualMemory = 1 << 4
        }


        [Flags]
        public enum PresenceInSnapshots
        {
            None = 0,
            First = 1 << 0,
            Second = 1 << 1,
            Both = First | Second
        }

        public struct ViewState : IEquatable<ViewState>
        {
            public ulong BytesInRow;
            public ulong HighlightedAddrMin;
            public ulong HighlightedAddrMax;
            public int DisplayElements;
            public ColorScheme ColorScheme;
            public Vector2 ScrollArea;

            public bool Equals(ViewState other)
            {
                return BytesInRow == other.BytesInRow &&
                    HighlightedAddrMin == other.HighlightedAddrMin &&
                    HighlightedAddrMax == other.HighlightedAddrMax &&
                    DisplayElements == other.DisplayElements &&
                    ColorScheme == other.ColorScheme &&
                    ScrollArea.Equals(other.ScrollArea);
            }
        }
        public ViewState CurrentViewState
        {
            get
            {
                ViewState state;
                state.BytesInRow = m_BytesInRow;
                state.HighlightedAddrMin = m_HighlightedAddrMin;
                state.HighlightedAddrMax = m_HighlightedAddrMax;
                state.DisplayElements = m_DisplayElements;
                state.ColorScheme = ActiveColorScheme;
                state.ScrollArea = m_ScrollArea;
                return state;
            }

            set
            {
                m_BytesInRow = value.BytesInRow;
                m_HighlightedAddrMin = value.HighlightedAddrMin;
                m_HighlightedAddrMax = value.HighlightedAddrMax;
                m_DisplayElements = value.DisplayElements;
                m_ColorScheme = value.ColorScheme;
                m_ScrollArea = value.ScrollArea;
            }
        }

        int m_DisplayElements = int.MaxValue & (~(int)DisplayElements.NativeObjectsDiff);

        public int DisplayElement
        {
            get
            {
                return m_DisplayElements;
            }
            set
            {
                m_DisplayElements = value;
                SetupView(m_BytesInRow);
                m_ForceReselect = true;
                ForceRepaint = true;

                UnityEditor.EditorPrefs.SetInt("Unity.MemoryProfiler.Editor.UI.MemoryMapDiff.DisplayElements", m_DisplayElements);
            }
        }

        public bool ShowDisplayElement(DisplayElements element)
        {
            return (m_DisplayElements & (int)element) != 0;
        }

        public void SetDisplayElementVisibility(DisplayElements element, bool state)
        {
            if (state)
                DisplayElement = DisplayElement | (int)element;
            else
                DisplayElement = DisplayElement & (~(int)element);
        }

        public void ToggleDisplayElement(DisplayElements element)
        {
            SetDisplayElementVisibility(element, !ShowDisplayElement(element));
        }

        public enum ColorScheme
        {
            Normal,
            Allocated,
            Deallocated
        };

        ColorScheme m_ColorScheme = ColorScheme.Normal;

        public ColorScheme ActiveColorScheme
        {
            get
            {
                return m_ColorScheme;
            }

            set
            {
                m_ColorScheme = value;
                SetupView(m_BytesInRow);
                m_ForceReselect = true;
                ForceRepaint = true;

                UnityEditor.EditorPrefs.SetInt("Unity.MemoryProfiler.Editor.UI.MemoryMapDiff.ColorScheme", (int)m_ColorScheme);
            }
        }

        Color32[] m_colorNotModified = new Color32[3] { new Color(0.30f , 0.30f , 0.40f), new Color(0.28f , 0.28f , 0.28f), new Color(0.28f , 0.28f , 0.28f) };
        Color32[] m_colorDeallocated = new Color32[3] { new Color(0.60f , 0.00f , 0.00f), new Color(0.34f , 0.28f , 0.28f), new Color(0.60f , 0.00f , 0.00f) };
        Color32[] m_colorModified = new Color32[3] { new Color(0.60f , 0.60f , 0.00f), new Color(0.60f , 0.60f , 0.00f), new Color(0.60f , 0.60f , 0.00f) };
        Color32[] m_colorAllocated = new Color32[3] { new Color(0.00f , 0.60f , 0.00f), new Color(0.00f , 0.60f , 0.00f), new Color(0.28f , 0.34f , 0.28f) };

        bool m_ForceReselect = false;

        public void Reselect()
        {
            m_ForceReselect = true;
        }

        public class DiffMemoryRegion : MemoryRegion
        {
            public PresenceInSnapshots m_Snapshot;

            public DiffMemoryRegion(RegionType type, ulong begin, ulong size, string displayName)
                : base(type, begin, size, displayName)
            {
                m_Snapshot = PresenceInSnapshots.None;
            }
        }


        public ulong BytesInRow
        {
            get { return m_BytesInRow; }

            set
            {
                SetupView(value);
                ForceRepaint = true;
                UnityEditor.EditorPrefs.SetInt("Unity.MemoryProfiler.Editor.UI.MemoryMapDiff.BytesInRow", (int)m_BytesInRow);
            }
        }

        public event Action<ulong, ulong> RegionSelected;

        CachedSnapshot[]    m_Snapshots = new CachedSnapshot[2];
        DiffMemoryRegion[]  m_SnapshotMemoryRegion;
        CappedNativeObjectsColection[] m_SortedAndCappedNativeObjects = new CappedNativeObjectsColection[2];
        List<EntryRange>[]  m_GroupsMangedObj = new List<EntryRange>[2] { new List<EntryRange>(), new List<EntryRange>() };
        List<EntryRange>[]  m_GroupsNativeAlloc = new List<EntryRange>[2] { new List<EntryRange>(), new List<EntryRange>() };
        List<EntryRange>[]  m_GroupsNativeObj = new List<EntryRange>[2] { new List<EntryRange>(), new List<EntryRange>() };
        Vector2 m_ScrollArea;
        ulong m_MouseDragStartAddr = 0;

        public MemoryMapDiff()
            : base(1, "Resources/MemoryMapDiff")
        {
            m_BytesInRow = (ulong)UnityEditor.EditorPrefs.GetInt("Unity.MemoryProfiler.Editor.UI.MemoryMapDiff.BytesInRow", (int)m_BytesInRow);
            m_DisplayElements = UnityEditor.EditorPrefs.GetInt("Unity.MemoryProfiler.Editor.UI.MemoryMapDiff.DisplayElements", m_DisplayElements);
            m_ColorScheme = (ColorScheme)UnityEditor.EditorPrefs.GetInt("Unity.MemoryProfiler.Editor.UI.MemoryMapDiff.ColorScheme", (int)m_ColorScheme);
        }

        void SetupSortedData()
        {
            PrepareSortedData(new CachedSnapshot.ISortedEntriesCache[]
            {
                m_Snapshots[0].SortedManagedObjects,
                m_Snapshots[0].SortedNativeAllocations,
                m_Snapshots[0].SortedNativeObjects,
                m_Snapshots[0].SortedNativeRegionsEntries,
                m_Snapshots[0].SortedManagedHeapEntries,
                m_Snapshots[0].SortedManagedStacksEntries,

                m_Snapshots[1].SortedManagedObjects,
                m_Snapshots[1].SortedNativeAllocations,
                m_Snapshots[1].SortedNativeObjects,
                m_Snapshots[1].SortedNativeRegionsEntries,
                m_Snapshots[1].SortedManagedHeapEntries,
                m_Snapshots[1].SortedManagedStacksEntries
            });
        }

        DiffMemoryRegion CreateNativeRegion(int regionIndex, CachedSnapshot snapshot, int i)
        {
            ulong start = snapshot.SortedNativeRegionsEntries.Address(i);
            ulong size = (ulong)snapshot.SortedNativeRegionsEntries.Size(i);
            string name = snapshot.SortedNativeRegionsEntries.Name(i);

            DiffMemoryRegion region;

            if (name.Contains("Virtual Memory"))
            {
                region = new DiffMemoryRegion(RegionType.VirtualMemory, start, size, name);
                region.ColorRegion = m_ColorNative[(int)EntryColors.VirtualMemory];
            }
            else
            {
                region = new DiffMemoryRegion(RegionType.Native, start, size, name);
                region.ColorRegion = m_ColorNative[(int)EntryColors.Region];
            }

            region.ColorRegion = new Color32(region.ColorRegion.r, region.ColorRegion.g, region.ColorRegion.b, (byte)(1 + regionIndex % 254));
            return region;
        }

        DiffMemoryRegion CreateManagedHeapRegion(int regionIndex, CachedSnapshot snapshot, int i)
        {
            ulong start = snapshot.SortedManagedHeapEntries.Address(i);
            ulong size = (ulong)snapshot.SortedManagedHeapEntries.Bytes(i).Length;

            var isGCHeap = snapshot.ManagedHeapSections.SectionType[i] == CachedSnapshot.MemorySectionType.GarbageCollector;
            DiffMemoryRegion region = new DiffMemoryRegion(isGCHeap ? RegionType.Managed : RegionType.ManagedDomain, start, size, snapshot.ManagedHeapSections.SectionName[i]);
            region.ColorRegion = new Color32(0, 0, 0, (byte)(1 + regionIndex % 254));
            return region;
        }

        DiffMemoryRegion CreateManagedStackRegion(int regionIndex, CachedSnapshot snapshot, int i)
        {
            ulong start = snapshot.SortedManagedStacksEntries.Address(i);
            ulong size = (ulong)snapshot.SortedManagedStacksEntries.Bytes(i).Length;

            DiffMemoryRegion region = new DiffMemoryRegion(RegionType.ManagedStack, start, size, snapshot.ManagedStacks.SectionName[i]);
            region.ColorRegion = new Color32(0, 0, 0, (byte)(1 + regionIndex % 254));
            return region;
        }

        void SetupRegions()
        {
            ProgressBarDisplay.UpdateProgress(0.0f, "Flushing regions ...");

            long regionCount = 0;

            for (int snapshotIdx = 0; snapshotIdx < m_Snapshots.Length; ++snapshotIdx)
                regionCount += m_Snapshots[snapshotIdx].NativeMemoryRegions.Count + m_Snapshots[snapshotIdx].ManagedHeapSections.Count + m_Snapshots[snapshotIdx].ManagedStacks.Count;

            var snapshotMemoryRegions = new List<DiffMemoryRegion>((int)regionCount);

            int offset = 0;

            uint processed = 0;

            for (int i = 0; i != m_Snapshots[0].SortedNativeRegionsEntries.Count; ++i)
            {
                if (processed++ % 10000 == 0) ProgressBarDisplay.UpdateProgress((float)processed / (float)snapshotMemoryRegions.Count);

                DiffMemoryRegion region = CreateNativeRegion(snapshotMemoryRegions.Count, m_Snapshots[0], i);
                region.m_Snapshot = PresenceInSnapshots.First;
                snapshotMemoryRegions.Add(region);
            }

            int offsetMax = snapshotMemoryRegions.Count;

            for (int i = 0; i != m_Snapshots[1].SortedNativeRegionsEntries.Count; ++i)
            {
                if (processed++ % 10000 == 0) ProgressBarDisplay.UpdateProgress((float)processed / (float)snapshotMemoryRegions.Count);

                ulong  addr = m_Snapshots[1].SortedNativeRegionsEntries.Address(i);
                ulong  size = (ulong)m_Snapshots[1].SortedNativeRegionsEntries.Size(i);
                string name = m_Snapshots[1].SortedNativeRegionsEntries.Name(i);

                while (offset < offsetMax && snapshotMemoryRegions[offset].AddressBegin < addr) offset++;

                for (int j = offset; j < offsetMax && snapshotMemoryRegions[j].AddressBegin == addr; ++j)
                {
                    if (snapshotMemoryRegions[j].Name == name && snapshotMemoryRegions[j].Size == size)
                    {
                        snapshotMemoryRegions[j].m_Snapshot |= PresenceInSnapshots.Second;
                        name = null;
                        break;
                    }
                }

                if (name != null)
                {
                    DiffMemoryRegion region = CreateNativeRegion(snapshotMemoryRegions.Count, m_Snapshots[1], i);
                    region.m_Snapshot = PresenceInSnapshots.Second;
                    snapshotMemoryRegions.Add(region);
                }
            }

            offset = snapshotMemoryRegions.Count;

            for (int i = 0; i != m_Snapshots[0].SortedManagedHeapEntries.Count; ++i)
            {
                if (processed++ % 10000 == 0) ProgressBarDisplay.UpdateProgress((float)processed / (float)snapshotMemoryRegions.Count);

                DiffMemoryRegion region = CreateManagedHeapRegion(snapshotMemoryRegions.Count, m_Snapshots[0], i);
                region.m_Snapshot = PresenceInSnapshots.First;
                snapshotMemoryRegions.Add(region);
            }

            offsetMax = snapshotMemoryRegions.Count;

            for (int i = 0; i != m_Snapshots[1].SortedManagedHeapEntries.Count; ++i)
            {
                if (processed++ % 10000 == 0) ProgressBarDisplay.UpdateProgress((float)processed / (float)snapshotMemoryRegions.Count);

                ulong  addr = m_Snapshots[1].SortedManagedHeapEntries.Address(i);
                ulong  size = (ulong)m_Snapshots[1].SortedManagedHeapEntries.Size(i);
                var regionType = m_Snapshots[1].ManagedHeapSections.SectionType[i] == CachedSnapshot.MemorySectionType.GarbageCollector ? RegionType.Managed : RegionType.ManagedDomain;

                // find the first region that overlaps with the start address, not just the first region that matches the start address
                while (offset < offsetMax && snapshotMemoryRegions[offset].AddressEnd < addr) ++offset;

                for (int j = offset; j < offsetMax && snapshotMemoryRegions[j].AddressBegin <= addr; ++j)
                {
                    if (snapshotMemoryRegions[j].Type != regionType)
                        continue;
                    // Managed regions get welded together as the heap expands and split apart as parts are freed.
                    // Once freed these address ranges don't get reused so while the region changed (got split) the pages used here stayed the same.
                    // regions can therefor either be exactly the same, or overlap fully or in part with each other
                    if (snapshotMemoryRegions[j].Size == size && snapshotMemoryRegions[j].AddressBegin == addr)
                    {
                        // it's the exact same managed region
                        snapshotMemoryRegions[j].m_Snapshot |= PresenceInSnapshots.Second;
                        addr = size = 0;
                        break;
                    }
                    else if (snapshotMemoryRegions[j].AddressEnd > addr + size)
                    {
                        // ToDo: consider splitting other region into non shared part(s) and shared part to fix faulty drawing? would have to insert the new parts after the old one. Also would be needed for partial overlaps. seems overkill
                        var regionToSplit = snapshotMemoryRegions[j];
                        if (regionToSplit.AddressBegin < addr)
                        {
                            var splitSize = addr - regionToSplit.AddressBegin;
                            var nonMatchingPartInFrontOfSharedRegion = new DiffMemoryRegion(regionType, regionToSplit.AddressBegin, splitSize, regionToSplit.Name);
                            nonMatchingPartInFrontOfSharedRegion.m_Snapshot = PresenceInSnapshots.First;

                            snapshotMemoryRegions.Insert(j, nonMatchingPartInFrontOfSharedRegion);
                            ++j;
                            ++offset;
                            ++offsetMax;

                            regionToSplit = new DiffMemoryRegion(regionType, nonMatchingPartInFrontOfSharedRegion.AddressEnd + 1, regionToSplit.Size - nonMatchingPartInFrontOfSharedRegion.Size, regionToSplit.Name);

                            if (regionToSplit.Size == size)
                            {
                                regionToSplit.m_Snapshot |= PresenceInSnapshots.Second;
                                snapshotMemoryRegions[j] = regionToSplit; // technically unnecessary unless DiffMemoryRegion is converted to a struct
                                addr = size = 0;
                                break;
                            }
                        }
                        // the region from snapshot 0 overlaps the entirety of this Managed region.
                        DiffMemoryRegion region = CreateManagedHeapRegion(snapshotMemoryRegions.Count, m_Snapshots[1], i);
                        region.m_Snapshot = PresenceInSnapshots.Both;
                        snapshotMemoryRegions.Add(region);

                        if (regionToSplit.Size > size)
                        {
                            var splitSize = regionToSplit.Size - size;
                            var nonMatchingPartAfterSharedRegion = new DiffMemoryRegion(regionType, region.AddressEnd + 1, splitSize, regionToSplit.Name);
                            nonMatchingPartAfterSharedRegion.m_Snapshot = PresenceInSnapshots.First;
                            snapshotMemoryRegions[j] = nonMatchingPartAfterSharedRegion;
                        }
                        addr = size = 0;
                        break;
                    }
                    else if (snapshotMemoryRegions[j].Size > 0)
                    {
                        // the region from snapshot 0 partially overlaps this managed region.
                        // We need to split this region into the part that is shared, and the part that isn't.
                        // This increases the region count
                        var regionToSplit = j;
                        if (snapshotMemoryRegions[j].AddressBegin == addr)
                        {
                            // The other region fits within this one, so it's fully shared
                            snapshotMemoryRegions[j].m_Snapshot |= PresenceInSnapshots.Second;
                            var sharedSize = snapshotMemoryRegions[j].AddressEnd - addr;
                            addr += sharedSize;
                            if (sharedSize > size)
                                size = 0;
                            else
                                size -= sharedSize;
                            // addr changed, get the next region
                            while (j < offsetMax - 1 && snapshotMemoryRegions[j].AddressBegin < addr) ++j;
                        }

                        if (snapshotMemoryRegions[j].AddressBegin > addr)
                        {
                            // if there is a gap between the start of this region and the next found region,
                            // split it off as only present in second, then continue with the remainder
                            ulong splitSize = addr + size < snapshotMemoryRegions[j].AddressBegin ? size : snapshotMemoryRegions[j].AddressBegin - addr;
                            if (splitSize != 0)
                            {
                                var splitRegion = new DiffMemoryRegion(regionType, addr, splitSize, m_Snapshots[1].ManagedHeapSections.SectionName[i]);
                                splitRegion.m_Snapshot = PresenceInSnapshots.Second;
                                snapshotMemoryRegions.Add(splitRegion);

                                // continue processing the remaining size of the region
                                addr = splitRegion.AddressEnd;
                                size -= splitSize;
                            }
                        }
                        if (size != 0 && regionToSplit < j)
                            --j; // do another round on this region, see if more can be distributed on it.
                    }
                }

                if (size != 0)
                {
                    DiffMemoryRegion region = CreateManagedHeapRegion(snapshotMemoryRegions.Count, m_Snapshots[1], i);
                    region.m_Snapshot = PresenceInSnapshots.Second;
                    snapshotMemoryRegions.Add(region);
                }
            }


            offset = snapshotMemoryRegions.Count;

            for (int i = 0; i != m_Snapshots[0].SortedManagedStacksEntries.Count; ++i)
            {
                if (processed++ % 10000 == 0) ProgressBarDisplay.UpdateProgress((float)processed / (float)snapshotMemoryRegions.Count);

                DiffMemoryRegion region = CreateManagedStackRegion(snapshotMemoryRegions.Count, m_Snapshots[0], i);
                region.m_Snapshot = PresenceInSnapshots.First;
                snapshotMemoryRegions.Add(region);
            }

            offsetMax = snapshotMemoryRegions.Count;

            for (int i = 0; i != m_Snapshots[1].SortedManagedStacksEntries.Count; ++i)
            {
                if (processed++ % 10000 == 0) ProgressBarDisplay.UpdateProgress((float)processed / (float)snapshotMemoryRegions.Count);

                ulong  addr = m_Snapshots[1].SortedManagedStacksEntries.Address(i);
                ulong  size = (ulong)m_Snapshots[1].SortedManagedStacksEntries.Size(i);

                while (offset < offsetMax && snapshotMemoryRegions[offset].AddressBegin < addr) offset++;

                for (int j = offset; j < offsetMax && snapshotMemoryRegions[j].AddressBegin == addr; ++j)
                {
                    if (snapshotMemoryRegions[j].Size == size)
                    {
                        snapshotMemoryRegions[j].m_Snapshot |= PresenceInSnapshots.Second;
                        addr = size = 0;
                        break;
                    }
                }

                if (addr != 0)
                {
                    DiffMemoryRegion region = CreateManagedStackRegion(snapshotMemoryRegions.Count, m_Snapshots[1], i);
                    region.m_Snapshot = PresenceInSnapshots.Second;
                    snapshotMemoryRegions.Add(region);
                }
            }


            ProgressBarDisplay.UpdateProgress(0.0f, "Sorting regions ..");

            m_SnapshotMemoryRegion = snapshotMemoryRegions.ToArray();

            Array.Sort(m_SnapshotMemoryRegion, delegate(MemoryRegion a, MemoryRegion b)
            {
                int result = a.AddressBegin.CompareTo(b.AddressBegin);

                if (result == 0)
                    result = -a.AddressEnd.CompareTo(b.AddressEnd);

                return result;
            }
            );
        }

        void CreateGroups()
        {
            m_Groups.Clear();

            if (m_SnapshotMemoryRegion.Length == 0)
                return;

            ProgressBarDisplay.UpdateProgress(0.0f, "Create groups ...");

            int metaRegions = 0;

            while (m_SnapshotMemoryRegion[metaRegions].AddressBegin == 0 && m_SnapshotMemoryRegion[metaRegions].AddressBegin == m_SnapshotMemoryRegion[metaRegions].AddressEnd)
            {
                metaRegions++;
            }

            int   groupIdx = 0;
            ulong groupAddressBegin = m_SnapshotMemoryRegion[metaRegions].AddressBegin;
            ulong groupAddressEnd = groupAddressBegin;

            for (int i = metaRegions; i < m_SnapshotMemoryRegion.Length; ++i)
            {
                if (i % 10000 == 0) ProgressBarDisplay.UpdateProgress((float)i / (float)m_SnapshotMemoryRegion.Length);

                if (m_SnapshotMemoryRegion[i].Type == RegionType.VirtualMemory && !ShowDisplayElement(DisplayElements.VirtualMemory))
                    continue;

                ulong addressBegin = m_SnapshotMemoryRegion[i].AddressBegin;
                ulong addressEnd   = m_SnapshotMemoryRegion[i].AddressEnd;

                if ((addressBegin > groupAddressEnd) && (addressBegin / m_BytesInRow) > (groupAddressEnd / m_BytesInRow) + 1)
                {
                    AddGroup(groupAddressBegin, groupAddressEnd);
                    groupAddressBegin = addressBegin;
                    groupAddressEnd = addressEnd;
                    groupIdx++;
                }
                else
                {
                    groupAddressEnd = Math.Max(groupAddressEnd, addressEnd);
                }

                m_SnapshotMemoryRegion[i].Group = groupIdx;
            }

            AddGroup(groupAddressBegin, groupAddressEnd);

            ProgressBarDisplay.UpdateProgress(1.0f);
        }

        void SetupGroups()
        {
            ProgressBarDisplay.UpdateProgress(0.0f, "Setup groups ...");

            for (Int32 snapshotIdx = 0; snapshotIdx < 2; ++snapshotIdx)
            {
                CachedSnapshot snapshot = m_Snapshots[snapshotIdx];

                int managedObjectsOffset = 0;
                int managedObjectsCount = snapshot.SortedManagedObjects.Count;

                int nativeAllocationsOffset = 0;
                int nativeAllocationsCount = snapshot.SortedNativeAllocations.Count;


                int nativeObjectsOffset = 0;
                int nativeObjectsCount = snapshot.SortedNativeObjects.Count;

                m_SortedAndCappedNativeObjects[snapshotIdx] = new CappedNativeObjectsColection(snapshot.SortedNativeObjects);

                int regionIndex = -1;
                // only if Dynamic Heap Allocator was used instead of System Allocator, do Native Allocations, and with those, Native Objects, have to fall within a Native Region
                if (snapshot.NativeMemoryRegions.UsesDynamicHeapAllocator && m_SnapshotMemoryRegion.Length > 0)
                {
                    for (regionIndex = 0; regionIndex < m_SnapshotMemoryRegion.Length; regionIndex++)
                    {
                        if (m_SnapshotMemoryRegion[regionIndex].Type == RegionType.Native)
                            break;
                    }
                    if (regionIndex >= m_SnapshotMemoryRegion.Length)
                        regionIndex = -1;
                }
                ulong minObjectAddressBasedOnRegion = regionIndex >= 0 ? m_SnapshotMemoryRegion[regionIndex].AddressBegin : 0;
                ulong maxObjectAddressBasedOnRegion = regionIndex >= 0 ? m_SnapshotMemoryRegion[regionIndex].AddressEnd : ulong.MaxValue;

                m_GroupsMangedObj[snapshotIdx].Clear();
                m_GroupsNativeAlloc[snapshotIdx].Clear();
                m_GroupsNativeObj[snapshotIdx].Clear();

                EntryRange range;

                for (int i = 0; i < m_Groups.Count; ++i)
                {
                    if (i % 1000 == 0) ProgressBarDisplay.UpdateProgress((float)i / (float)m_Groups.Count);

                    // Assigning Managed Objects Range
                    while (managedObjectsOffset < managedObjectsCount && m_Groups[i].AddressBegin > snapshot.SortedManagedObjects.Address(managedObjectsOffset))
                        managedObjectsOffset++;

                    range.Begin = managedObjectsOffset;

                    while (managedObjectsOffset < managedObjectsCount && snapshot.SortedManagedObjects.Address(managedObjectsOffset) < m_Groups[i].AddressEnd)
                        managedObjectsOffset++;

                    range.End = managedObjectsOffset;

                    m_GroupsMangedObj[snapshotIdx].Add(range);

                    // Assigning Native Allocation Range
                    while (nativeAllocationsOffset < nativeAllocationsCount && m_Groups[i].AddressBegin > snapshot.SortedNativeAllocations.Address(nativeAllocationsOffset))
                        nativeAllocationsOffset++;

                    range.Begin = nativeAllocationsOffset;

                    while (nativeAllocationsOffset < nativeAllocationsCount && snapshot.SortedNativeAllocations.Address(nativeAllocationsOffset) < m_Groups[i].AddressEnd)
                        nativeAllocationsOffset++;

                    range.End = nativeAllocationsOffset;

                    m_GroupsNativeAlloc[snapshotIdx].Add(range);

                    // Assigning Native Objects Range
                    while (nativeObjectsOffset < nativeObjectsCount && m_Groups[i].AddressBegin > snapshot.SortedNativeObjects.Address(nativeObjectsOffset))
                        nativeObjectsOffset++;

                    range.Begin = nativeObjectsOffset;

                    while (nativeObjectsOffset < nativeObjectsCount && snapshot.SortedNativeObjects.Address(nativeObjectsOffset) < m_Groups[i].AddressEnd)
                        nativeObjectsOffset++;

                    range.End = nativeObjectsOffset;

                    m_GroupsNativeObj[snapshotIdx].Add(range);

                    int firstNativeObjectInGroup = range.Begin;
                    int lastNativeObjectInGroup = range.End;

                    // only if native allocations have been recorded, will we limit the Object sizes to those
                    int allocationIndex = nativeAllocationsCount > 0
                        && m_GroupsNativeAlloc[snapshotIdx][m_GroupsNativeAlloc[snapshotIdx].Count - 1].Begin >= 0
                        && m_GroupsNativeAlloc[snapshotIdx][m_GroupsNativeAlloc[snapshotIdx].Count - 1].Begin < snapshot.SortedNativeAllocations.Count
                        ? m_GroupsNativeAlloc[snapshotIdx][m_GroupsNativeAlloc[snapshotIdx].Count - 1].Begin : -1;
                    ulong minObjectAddressBasedOnAllocation = allocationIndex >= 0 ? snapshot.SortedNativeAllocations.Address(allocationIndex) : 0;
                    ulong maxObjectAddressBasedOnAllocation = allocationIndex >= 0 ? minObjectAddressBasedOnAllocation + snapshot.SortedNativeAllocations.Size(allocationIndex) : ulong.MaxValue;
                    if (allocationIndex > 0 || snapshot.NativeMemoryRegions.UsesDynamicHeapAllocator)
                    {
                        for (int nativeObjectIndex = firstNativeObjectInGroup; nativeObjectIndex < lastNativeObjectInGroup; nativeObjectIndex++)
                        {
                            var nativeObjectStartAddrress = snapshot.SortedNativeObjects.Address(nativeObjectIndex);
                            // only if Dynamic Heap Allocator was used instead of System Allocator, do Native Allocations, and with those, Native Objects, have to fall within a Native Region
                            while (snapshot.NativeMemoryRegions.UsesDynamicHeapAllocator &&
                                   maxObjectAddressBasedOnRegion < nativeObjectStartAddrress)
                            {
                                if (++regionIndex >= m_SnapshotMemoryRegion.Length)
                                {
                                    minObjectAddressBasedOnRegion = 0;
                                    maxObjectAddressBasedOnRegion = 0;
                                    break;
                                }
                                minObjectAddressBasedOnRegion = m_SnapshotMemoryRegion[regionIndex].AddressBegin;
                                maxObjectAddressBasedOnRegion = m_SnapshotMemoryRegion[regionIndex].AddressEnd;
                            }
                            // only if native allocations have been recorded, will we limit the Object sizes to those
                            while (allocationIndex >= 0 && (maxObjectAddressBasedOnAllocation < nativeObjectStartAddrress || minObjectAddressBasedOnAllocation < minObjectAddressBasedOnRegion))
                            {
                                if (++allocationIndex >= nativeAllocationsOffset)
                                {
                                    minObjectAddressBasedOnAllocation = 0;
                                    maxObjectAddressBasedOnAllocation = 0;
                                    if (snapshot.NativeMemoryRegions.UsesDynamicHeapAllocator)
                                        Debug.LogError("A Native Allocation was recorded as being outside of a Native Region. This shouldn't happen when the Dynamic Heap Allocator (i.e not a System Allocator) is used. Please report this as a bug.");
                                    break;
                                }
                                minObjectAddressBasedOnAllocation = snapshot.SortedNativeAllocations.Address(allocationIndex);
                                maxObjectAddressBasedOnAllocation = minObjectAddressBasedOnAllocation + snapshot.SortedNativeAllocations.Size(allocationIndex);

                                if (maxObjectAddressBasedOnAllocation > maxObjectAddressBasedOnRegion)
                                    Debug.LogError("A Native Allocation was recorded as being outside of a Native Region. This shouldn't happen when the Dynamic Heap Allocator (i.e not a System Allocator) is used. Please report this as a bug.");
                            }

                            var minAddress = Math.Min(minObjectAddressBasedOnAllocation, minObjectAddressBasedOnRegion);
                            if (minAddress > nativeObjectStartAddrress)
                                Debug.LogError("A Native Object was recorded as being outside of a Native Region or Allocation. Please report this as a bug.");

                            var maxAddress = Math.Min(maxObjectAddressBasedOnAllocation, maxObjectAddressBasedOnRegion);
                            var nativeObjectLastByteAddrress = nativeObjectStartAddrress + m_SortedAndCappedNativeObjects[snapshotIdx].Size(nativeObjectIndex);
                            if (nativeObjectLastByteAddrress > maxAddress)
                            {
                                m_SortedAndCappedNativeObjects[snapshotIdx].SetSize(nativeObjectIndex, maxAddress - nativeObjectStartAddrress);
                            }
                        }
                    }
                }
            }

            ProgressBarDisplay.UpdateProgress(1.0f);
        }

        public void SetupView(ulong rowMemorySize)
        {
            ProgressBarDisplay.ShowBar("Setup memory map diff view ");

            m_BytesInRow = rowMemorySize;

            CreateGroups();

            SetupGroups();

            ProgressBarDisplay.ClearBar();
        }

        public void Setup(CachedSnapshot snapshotA, CachedSnapshot snapshotB)
        {
            m_Snapshots[0] = snapshotA;
            m_Snapshots[1] = snapshotB;

            ProgressBarDisplay.ShowBar("Setup memory map diff");

            SetupSortedData();

            SetupRegions();

            CreateGroups();

            SetupGroups();

            ForceRepaint = true;

            ProgressBarDisplay.ClearBar();
        }

        void RenderRegions(ulong addressMin, ulong addressMax)
        {
            for (int i = 0; i < m_SnapshotMemoryRegion.Length; ++i)
            {
                if (m_SnapshotMemoryRegion[i].Type == RegionType.VirtualMemory && !ShowDisplayElement(DisplayElements.VirtualMemory))
                    continue;

                ulong stripGroupAddrBegin = m_SnapshotMemoryRegion[i].AddressBegin.Clamp(addressMin, addressMax);
                ulong stripGroupAddrEnd   = m_SnapshotMemoryRegion[i].AddressEnd.Clamp(addressMin, addressMax);

                if (stripGroupAddrBegin == stripGroupAddrEnd)
                    continue;

                MemoryGroup group = m_Groups[m_SnapshotMemoryRegion[i].Group];

                //RenderStrip(group, stripGroupAddrBegin, stripGroupAddrEnd, m_SnapshotMemoryRegion[i].ColorRegion);

                RenderStrip(group, stripGroupAddrBegin, stripGroupAddrEnd, (Color32 c) =>
                    Max(c, new Color32(0x00, 0x00, 0x00, m_SnapshotMemoryRegion[i].ColorRegion.a))
                );
            }
        }

        void RenderRegionsDiff(ulong addressMin, ulong addressMax)
        {
            for (int i = 0; i < m_SnapshotMemoryRegion.Length; ++i)
            {
                if (m_SnapshotMemoryRegion[i].Type == RegionType.VirtualMemory && !ShowDisplayElement(DisplayElements.VirtualMemory))
                    continue;

                ulong stripGroupAddrBegin = m_SnapshotMemoryRegion[i].AddressBegin.Clamp(addressMin, addressMax);
                ulong stripGroupAddrEnd   = m_SnapshotMemoryRegion[i].AddressEnd.Clamp(addressMin, addressMax);

                if (stripGroupAddrBegin == stripGroupAddrEnd)
                    continue;

                PresenceInSnapshots entryMask = m_SnapshotMemoryRegion[i].m_Snapshot;

                MemoryGroup group = m_Groups[m_SnapshotMemoryRegion[i].Group];

                RenderStrip(group, stripGroupAddrBegin, stripGroupAddrEnd, (Color32 c) =>
                    Max(c, new Color32((byte)(entryMask == PresenceInSnapshots.First ? 0xFF : 0x00), (byte)(entryMask == PresenceInSnapshots.Second ? 0xFF : 0x00), (byte)(entryMask == PresenceInSnapshots.Both ? 0xFF : 0x00), m_SnapshotMemoryRegion[i].ColorRegion.a))
                );
            }
        }

        public override void OnRenderMap(ulong addressMin, ulong addressMax, int slot)
        {
            if (ShowDisplayElement(DisplayElements.RegionDiff))
            {
                RenderRegionsDiff(addressMin, addressMax);
            }
            else
            {
                RenderRegions(addressMin, addressMax);
            }

            int groupsBegin = int.MaxValue;
            int groupsEnd = int.MinValue;

            for (int i = 0; i < m_Groups.Count; ++i)
            {
                ulong stripGroupAddrBegin = m_Groups[i].AddressBegin.Clamp(addressMin, addressMax);
                ulong stripGroupAddrEnd   = m_Groups[i].AddressEnd.Clamp(addressMin, addressMax);

                if (stripGroupAddrBegin == stripGroupAddrEnd)
                    continue;

                groupsBegin = Math.Min(groupsBegin, i);
                groupsEnd = Math.Max(groupsEnd, i + 1);
            }


            for (int i = groupsBegin; i < groupsEnd; ++i)
            {
                if (ShowDisplayElement(DisplayElements.AllocationsDiff))
                {
                    RenderDiff(m_Snapshots[0].SortedNativeAllocations, m_GroupsNativeAlloc[0], m_Snapshots[1].SortedNativeAllocations, m_GroupsNativeAlloc[1], i, addressMin, addressMax);
                }

                if (ShowDisplayElement(DisplayElements.NativeObjectsDiff))
                {
                    RenderDiff(m_SortedAndCappedNativeObjects[0], m_GroupsNativeObj[0], m_SortedAndCappedNativeObjects[1], m_GroupsNativeObj[1], i, addressMin, addressMax);
                }

                if (ShowDisplayElement(DisplayElements.ManagedObjectsDiff))
                {
                    RenderDiff(m_Snapshots[0].SortedManagedObjects, m_GroupsMangedObj[0], m_Snapshots[1].SortedManagedObjects, m_GroupsMangedObj[1], i, addressMin, addressMax);
                }
            }
        }

        void OnGUIView(Rect r, Rect viewRect)
        {
            GUI.BeginGroup(r);

            m_ScrollArea = GUI.BeginScrollView(new Rect(0, 0, r.width, r.height), m_ScrollArea, new Rect(0, 0, viewRect.width - Styles.MemoryMap.VScrollBarWidth, viewRect.height), false, true);

            if (m_ScrollArea.y + r.height > viewRect.height)
                m_ScrollArea.y = Math.Max(0, viewRect.height - r.height);

            FlushTextures(m_ScrollArea.y, m_ScrollArea.y + r.height);

            float viewTop    = m_ScrollArea.y;
            float viewBottom = m_ScrollArea.y + r.height;

            HandleMouseClick(r);

            if (Event.current.type == EventType.Repaint)
            {
                Material mat = BindDefaultMaterial();

                mat.SetColor("_ColorNotModified", m_colorNotModified[(int)m_ColorScheme]);
                mat.SetColor("_ColorDeallocated", m_colorDeallocated[(int)m_ColorScheme]);
                mat.SetColor("_ColorModified",    m_colorModified[(int)m_ColorScheme]);
                mat.SetColor("_ColorAllocated",   m_colorAllocated[(int)m_ColorScheme]);

                RenderGroups(viewTop, viewBottom);
            }

            RenderGroupLabels(viewTop, viewBottom);

            GUI.EndScrollView();

            GUI.EndGroup();
        }

        void OnGUILegend(Rect r)
        {
            Color oldColor = GUI.backgroundColor;

            r.xMin += Styles.MemoryMap.HeaderWidth;
            GUILayout.BeginArea(r);
            GUILayout.Space(3);
            GUILayout.BeginHorizontal();

            GUI.backgroundColor = m_colorNotModified[(int)m_ColorScheme];
            GUILayout.Toggle(true, "Not modified", Styles.MemoryMap.SeriesLabel);
            GUILayout.Space(Styles.MemoryMap.LegendSpacerWidth);

            GUI.backgroundColor = m_colorDeallocated[(int)m_ColorScheme];
            GUILayout.Toggle(true, "Deallocated", Styles.MemoryMap.SeriesLabel);
            GUILayout.Space(Styles.MemoryMap.LegendSpacerWidth);

            GUI.backgroundColor = m_colorModified[(int)m_ColorScheme];
            GUILayout.Toggle(true, "Modified", Styles.MemoryMap.SeriesLabel);
            GUILayout.Space(Styles.MemoryMap.LegendSpacerWidth);

            GUI.backgroundColor = m_colorAllocated[(int)m_ColorScheme];
            GUILayout.Toggle(true, "New Allocations", Styles.MemoryMap.SeriesLabel);
            GUILayout.Space(Styles.MemoryMap.LegendSpacerWidth);

            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            GUI.backgroundColor = oldColor;
        }

        public void OnGUI(Rect rect)
        {
            if (m_Groups.Count != 0)
            {
                Rect r = new Rect(rect);
                r.y += Styles.MemoryMap.LegendHeight;
                r.height -= Styles.MemoryMap.LegendHeight;

                Rect viewRect = new Rect(0, 0, r.width, m_Groups[m_Groups.Count - 1].MaxY + Styles.MemoryMap.RowPixelHeight);

                MemoryMapRect = new Rect(
                    viewRect.x + Styles.MemoryMap.HeaderWidth,
                    viewRect.y,
                    viewRect.width - Styles.MemoryMap.HeaderWidth - Styles.MemoryMap.VScrollBarWidth,
                    viewRect.height);

                if (MemoryMapRect.width <= 0 || MemoryMapRect.height <= 0)
                    return;

                OnGUILegend(new Rect(r.x, rect.y, r.width, Styles.MemoryMap.LegendHeight));

                OnGUIView(r, viewRect);
            }

            if (m_ForceReselect)
            {
                RegionSelected(m_HighlightedAddrMin, m_HighlightedAddrMax);
                m_ForceReselect = false;
            }
        }

        int AddressToRegion(ulong addr)
        {
            int select = -1;
            for (int i = 0; i < m_SnapshotMemoryRegion.Length; ++i)
            {
                MemoryRegion region = m_SnapshotMemoryRegion[i];

                if (addr < region.AddressBegin)
                {
                    break;
                }

                if (region.AddressBegin <= addr && addr < region.AddressEnd)
                {
                    select = i;
                }
            }
            return select;
        }

        void HandleMouseClick(Rect r)
        {
            ulong pixelDragLimit = 2 * m_BytesInRow / (ulong)MemoryMapRect.width;

            if (Event.current.mousePosition.y - m_ScrollArea.y >= r.height)
                return;

            if (Event.current.type == EventType.MouseDown)
            {
                m_MouseDragStartAddr = MouseToAddress(Event.current.mousePosition);
                m_HighlightedAddrMin = m_MouseDragStartAddr;
                m_HighlightedAddrMax = m_MouseDragStartAddr;
            }
            else if (Event.current.type == EventType.MouseDrag)
            {
                ulong addr = MouseToAddress(Event.current.mousePosition);;
                m_HighlightedAddrMin = (addr < m_MouseDragStartAddr) ? addr : m_MouseDragStartAddr;
                m_HighlightedAddrMax = (addr < m_MouseDragStartAddr) ? m_MouseDragStartAddr : addr;

                if (m_HighlightedAddrMax - m_HighlightedAddrMin > pixelDragLimit)
                {
                    Event.current.Use();
                }
            }
            else if (Event.current.type == EventType.MouseUp)
            {
                if (m_HighlightedAddrMax - m_HighlightedAddrMin <= pixelDragLimit)
                {
                    if (Event.current.mousePosition.x < Styles.MemoryMap.HeaderWidth)
                    {
                        for (int i = 0; i < m_Groups.Count; ++i)
                        {
                            if (m_Groups[i].Labels[0].TextRect.Contains(Event.current.mousePosition))
                            {
                                m_HighlightedAddrMin = m_Groups[i].AddressBegin;
                                m_HighlightedAddrMax = m_Groups[i].AddressEnd;
                                break;
                            }
                        }
                    }
                    else
                    {
                        m_HighlightedAddrMax = m_HighlightedAddrMin = ulong.MaxValue;
                        int reg = AddressToRegion(m_MouseDragStartAddr);
                        if (reg >= 0)
                        {
                            m_HighlightedAddrMin = m_SnapshotMemoryRegion[reg].AddressBegin;
                            m_HighlightedAddrMax = m_SnapshotMemoryRegion[reg].AddressEnd;
                        }
                    }
                }

                RegionSelected(m_HighlightedAddrMin, m_HighlightedAddrMax);
                Event.current.Use();
            }
        }
    }
}

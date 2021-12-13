using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor.UI.MemoryMap
{
    internal class MemoryMap : MemoryMapBase
    {
        [Flags]
        public enum DisplayElements
        {
            Allocations   = 1 << 0,
            ManagedObjects = 1 << 1,
            NativeObjects = 1 << 2,
            VirtualMemory = 1 << 3,
        }

        public struct ViewState
        {
            public ulong    BytesInRow;
            public ulong    HighlightedAddrMin;
            public ulong    HighlightedAddrMax;
            public int      DisplayElements;
            public Vector2  ScrollArea;
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
                state.ScrollArea = m_ScrollArea;
                return state;
            }

            set
            {
                m_BytesInRow = value.BytesInRow;
                m_HighlightedAddrMin = value.HighlightedAddrMin;
                m_HighlightedAddrMax = value.HighlightedAddrMax;
                m_DisplayElements = value.DisplayElements;
                m_ScrollArea = value.ScrollArea;
            }
        }

        Vector2 m_ScrollArea;
        int m_DisplayElements = int.MaxValue & (~(int)DisplayElements.NativeObjects);

        public bool ShowDisplayElement(DisplayElements element)
        {
            return (m_DisplayElements & (int)element) != 0;
        }

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

                UnityEditor.EditorPrefs.SetInt("Unity.MemoryProfiler.Editor.UI.MemoryMap.DisplayElements", m_DisplayElements);
            }
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

        bool m_ForceReselect = false;

        public void Reselect()
        {
            m_ForceReselect = true;
        }

        public ulong BytesInRow
        {
            get { return m_BytesInRow; }

            set
            {
                SetupView(value);

                ForceRepaint = true;

                UnityEditor.EditorPrefs.SetInt("Unity.MemoryProfiler.Editor.UI.MemoryMap.BytesInRow", (int)m_BytesInRow);
            }
        }


        public event Action<ulong, ulong> RegionSelected;

        CachedSnapshot m_Snapshot;
        MemoryRegion[] m_SnapshotMemoryRegion;
        CappedNativeObjectsColection m_SortedAndCappedNativeObjects;
        List<EntryRange> m_GroupsMangedObj = new List<EntryRange>();
        List<EntryRange> m_GroupsNativeAlloc = new List<EntryRange>();
        List<EntryRange> m_GroupsNativeObj = new List<EntryRange>();
        ulong m_MouseDragStartAddr = 0;

        public MemoryMap()
        {
            m_BytesInRow = (ulong)UnityEditor.EditorPrefs.GetInt("Unity.MemoryProfiler.Editor.UI.MemoryMap.BytesInRow", (int)m_BytesInRow);
            m_DisplayElements = UnityEditor.EditorPrefs.GetInt("Unity.MemoryProfiler.Editor.UI.MemoryMap.DisplayElements", m_DisplayElements);
        }

        void SetupSortedData()
        {
            PrepareSortedData(new CachedSnapshot.ISortedEntriesCache[]
            {
                m_Snapshot.SortedManagedObjects,
                m_Snapshot.SortedNativeAllocations,
                m_Snapshot.SortedNativeObjects
            });
        }

        void SetupRegions()
        {
            ProgressBarDisplay.UpdateProgress(0.0f, "Flushing regions ...");

            int regionIndex = 0;

            m_SnapshotMemoryRegion = new MemoryRegion[m_Snapshot.NativeMemoryRegions.Count + m_Snapshot.ManagedHeapSections.Count + m_Snapshot.ManagedStacks.Count];

            for (int i = 0; i != m_Snapshot.NativeMemoryRegions.Count; ++i)
            {
                if (regionIndex % 10000 == 0) ProgressBarDisplay.UpdateProgress((float)regionIndex / (float)m_SnapshotMemoryRegion.Length);

                ulong start = m_Snapshot.NativeMemoryRegions.AddressBase[i];
                ulong size = (ulong)m_Snapshot.NativeMemoryRegions.AddressSize[i];
                string name = m_Snapshot.NativeMemoryRegions.MemoryRegionName[i];

                MemoryRegion region;
                if (name.Contains("Virtual Memory"))
                {
                    region = new MemoryRegion(RegionType.VirtualMemory, start, size, name);
                    region.ColorRegion = m_ColorNative[(int)EntryColors.VirtualMemory];
                }
                else
                {
                    region = new MemoryRegion(RegionType.Native, start, size, name);
                    region.ColorRegion = m_ColorNative[(int)EntryColors.Region];
                }

                region.ColorRegion = new Color32(region.ColorRegion.r, region.ColorRegion.g, region.ColorRegion.b, (byte)(2 + regionIndex % 253));
                m_SnapshotMemoryRegion[regionIndex++] = region;
            }

            for (int i = 0; i != m_Snapshot.ManagedHeapSections.Count; ++i)
            {
                if (regionIndex % 10000 == 0) ProgressBarDisplay.UpdateProgress((float)regionIndex / (float)m_SnapshotMemoryRegion.Length);

                ulong start = m_Snapshot.ManagedHeapSections.StartAddress[i];
                ulong size = (ulong)m_Snapshot.ManagedHeapSections.Bytes[i].Length;
                var isGCHeap = m_Snapshot.ManagedHeapSections.SectionType[i] == CachedSnapshot.MemorySectionType.GarbageCollector;
                MemoryRegion region = new MemoryRegion(isGCHeap ? RegionType.Managed : RegionType.ManagedDomain, start, size, m_Snapshot.ManagedHeapSections.SectionName[i]);
                region.ColorRegion = m_ColorManaged[isGCHeap ? (int)EntryColors.Region : (int)EntryColors.Allocation];
                region.ColorRegion = new Color32(region.ColorRegion.r, region.ColorRegion.g, region.ColorRegion.b, (byte)(2 + regionIndex % 253));
                m_SnapshotMemoryRegion[regionIndex++] = region;
            }

            ProgressBarDisplay.UpdateProgress((float)regionIndex / (float)m_SnapshotMemoryRegion.Length);

            for (int i = 0; i != m_Snapshot.ManagedStacks.Count; ++i)
            {
                if (regionIndex % 10000 == 0) ProgressBarDisplay.UpdateProgress((float)regionIndex / (float)m_SnapshotMemoryRegion.Length);

                ulong start = m_Snapshot.ManagedStacks.StartAddress[i];
                ulong size = (ulong)m_Snapshot.ManagedStacks.Bytes[i].Length;
                MemoryRegion region = new MemoryRegion(RegionType.ManagedStack, start, size, m_Snapshot.ManagedStacks.SectionName[i]);
                region.ColorRegion = m_ColorManagedStack[(int)EntryColors.Region];
                region.ColorRegion = new Color32(region.ColorRegion.r, region.ColorRegion.g, region.ColorRegion.b, (byte)(2 + regionIndex % 253));
                m_SnapshotMemoryRegion[regionIndex++] = region;
            }

            ProgressBarDisplay.UpdateProgress((float)regionIndex / (float)m_SnapshotMemoryRegion.Length);

            ProgressBarDisplay.UpdateProgress(0.0f, "Sorting regions ...");

            Array.Sort(m_SnapshotMemoryRegion, delegate(MemoryRegion a, MemoryRegion b)
            {
                int result = a.AddressBegin.CompareTo(b.AddressBegin);

                if (result == 0)
                    result = -a.AddressEnd.CompareTo(b.AddressEnd);

                return result;
            }
            );

            ProgressBarDisplay.UpdateProgress(1.0f);
        }

        void CreateGroups()
        {
            m_Groups.Clear();

            if (m_SnapshotMemoryRegion.Length == 0)
                return;

            ProgressBarDisplay.UpdateProgress(0.0f, "Create groups ...");

            int metaRegions = 0;

            while (m_SnapshotMemoryRegion[metaRegions].AddressBegin == 0 && m_SnapshotMemoryRegion[metaRegions].AddressBegin == m_SnapshotMemoryRegion[metaRegions].AddressEnd)
                metaRegions++;

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

            int managedObjectsOffset = 0;
            int managedObjectsCount = m_Snapshot.SortedManagedObjects.Count;

            m_GroupsMangedObj.Clear();

            int nativeAllocationsOffset = 0;
            int nativeAllocationsCount = m_Snapshot.SortedNativeAllocations.Count;

            m_GroupsNativeAlloc.Clear();

            int nativeObjectsOffset = 0;
            int nativeObjectsCount = m_Snapshot.SortedNativeObjects.Count;

            m_GroupsNativeObj.Clear();
            m_SortedAndCappedNativeObjects = new CappedNativeObjectsColection(m_Snapshot.SortedNativeObjects);

            int regionIndex = -1;
            // only if Dynamic Heap Allocator was used instead of System Allocator, do Native Allocations, and with those, Native Objects, have to fall within a Native Region
            if (m_Snapshot.NativeMemoryRegions.UsesDynamicHeapAllocator && m_SnapshotMemoryRegion.Length > 0)
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

            EntryRange range;

            for (int i = 0; i < m_Groups.Count; ++i)
            {
                if (i % 1000 == 0) ProgressBarDisplay.UpdateProgress((float)i / (float)m_Groups.Count);

                // Assigning Managed Objects Range
                while (managedObjectsOffset < managedObjectsCount && m_Groups[i].AddressBegin > m_Snapshot.SortedManagedObjects.Address(managedObjectsOffset))
                    managedObjectsOffset++;

                range.Begin = managedObjectsOffset;

                while (managedObjectsOffset < managedObjectsCount && m_Snapshot.SortedManagedObjects.Address(managedObjectsOffset) < m_Groups[i].AddressEnd)
                    managedObjectsOffset++;

                range.End = managedObjectsOffset;

                m_GroupsMangedObj.Add(range);

                // Assigning Native Allocation Range
                while (nativeAllocationsOffset < nativeAllocationsCount && m_Groups[i].AddressBegin > m_Snapshot.SortedNativeAllocations.Address(nativeAllocationsOffset))
                    nativeAllocationsOffset++;

                range.Begin = nativeAllocationsOffset;

                while (nativeAllocationsOffset < nativeAllocationsCount && m_Snapshot.SortedNativeAllocations.Address(nativeAllocationsOffset) < m_Groups[i].AddressEnd)
                    nativeAllocationsOffset++;

                range.End = nativeAllocationsOffset;

                m_GroupsNativeAlloc.Add(range);

                // Assigning Native Objects Range
                while (nativeObjectsOffset < nativeObjectsCount && m_Groups[i].AddressBegin > m_Snapshot.SortedNativeObjects.Address(nativeObjectsOffset))
                    nativeObjectsOffset++;

                range.Begin = nativeObjectsOffset;

                while (nativeObjectsOffset < nativeObjectsCount && m_Snapshot.SortedNativeObjects.Address(nativeObjectsOffset) < m_Groups[i].AddressEnd)
                    nativeObjectsOffset++;

                range.End = nativeObjectsOffset;

                m_GroupsNativeObj.Add(range);

                int firstNativeObjectInGroup = range.Begin;
                int lastNativeObjectInGroup = range.End;

                // only if native allocations have been recorded, will we limit the Object sizes to those
                int allocationIndex = nativeAllocationsCount > 0
                    && m_GroupsNativeAlloc[m_GroupsNativeAlloc.Count - 1].Begin >= 0
                    && m_GroupsNativeAlloc[m_GroupsNativeAlloc.Count - 1].Begin < m_Snapshot.SortedNativeAllocations.Count
                    ? m_GroupsNativeAlloc[m_GroupsNativeAlloc.Count - 1].Begin : -1;
                ulong minObjectAddressBasedOnAllocation = allocationIndex >= 0 ? m_Snapshot.SortedNativeAllocations.Address(allocationIndex) : 0;
                ulong maxObjectAddressBasedOnAllocation = allocationIndex >= 0 ? minObjectAddressBasedOnAllocation + m_Snapshot.SortedNativeAllocations.Size(allocationIndex) : ulong.MaxValue;
                if (allocationIndex > 0 || m_Snapshot.NativeMemoryRegions.UsesDynamicHeapAllocator)
                {
                    for (int nativeObjectIndex = firstNativeObjectInGroup; nativeObjectIndex < lastNativeObjectInGroup; nativeObjectIndex++)
                    {
                        var nativeObjectStartAddrress = m_Snapshot.SortedNativeObjects.Address(nativeObjectIndex);
                        // only if Dynamic Heap Allocator was used instead of System Allocator, do Native Allocations, and with those, Native Objects, have to fall within a Native Region
                        while (m_Snapshot.NativeMemoryRegions.UsesDynamicHeapAllocator &&
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
                                if (m_Snapshot.NativeMemoryRegions.UsesDynamicHeapAllocator)
                                    Debug.LogError("A Native Object was recorded as being outside of a Native Allocation. This shouldn't happen when the Dynamic Heap Allocator (i.e not a System Allocator) is used. Please report this as a bug.");
                                break;
                            }
                            minObjectAddressBasedOnAllocation = m_Snapshot.SortedNativeAllocations.Address(allocationIndex);
                            maxObjectAddressBasedOnAllocation = minObjectAddressBasedOnAllocation + m_Snapshot.SortedNativeAllocations.Size(allocationIndex);

                            if (maxObjectAddressBasedOnAllocation > maxObjectAddressBasedOnRegion)
                                Debug.LogError("A Native Allocation was recorded as being outside of a Native Region. This shouldn't happen when the Dynamic Heap Allocator (i.e not a System Allocator) is used. Please report this as a bug.");
                        }

                        var minAddress = Math.Min(minObjectAddressBasedOnAllocation, minObjectAddressBasedOnRegion);
                        if (minAddress > nativeObjectStartAddrress)
                            Debug.LogError("A Native Object was recorded as being outside of a Native Region or Allocation. Please report this as a bug.");

                        var maxAddress = Math.Min(maxObjectAddressBasedOnAllocation, maxObjectAddressBasedOnRegion);
                        var nativeObjectLastByteAddrress = nativeObjectStartAddrress + m_SortedAndCappedNativeObjects.Size(nativeObjectIndex);
                        if (nativeObjectLastByteAddrress > maxAddress)
                        {
                            m_SortedAndCappedNativeObjects.SetSize(nativeObjectIndex, maxAddress - nativeObjectStartAddrress);
                        }
                    }
                }
            }

            ProgressBarDisplay.UpdateProgress(1.0f);
        }

        public void SetupView(ulong rowMemorySize)
        {
            ProgressBarDisplay.ShowBar("Setup memory map view");

            m_BytesInRow = rowMemorySize;

            CreateGroups();

            SetupGroups();

            ProgressBarDisplay.ClearBar();
        }

        public void Setup(CachedSnapshot snapshot)
        {
            m_Snapshot = snapshot;

            ProgressBarDisplay.ShowBar("Setup memory map");

            SetupSortedData();

            SetupRegions();

            CreateGroups();

            SetupGroups();

            ProgressBarDisplay.ClearBar();
        }

        public override void OnRenderMap(ulong addressMin, ulong addressMax, int slot)
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

                RenderStrip(group, stripGroupAddrBegin, stripGroupAddrEnd, m_SnapshotMemoryRegion[i].ColorRegion);
            }

            for (int i = 0; i < m_Groups.Count; ++i)
            {
                ulong stripGroupAddrBegin = m_Groups[i].AddressBegin.Clamp(addressMin, addressMax);
                ulong stripGroupAddrEnd   = m_Groups[i].AddressEnd.Clamp(addressMin, addressMax);

                if (stripGroupAddrBegin == stripGroupAddrEnd)
                    continue;

                if (ShowDisplayElement(DisplayElements.Allocations))
                {
                    Color32 color = m_ColorNative[(int)EntryColors.Allocation];
                    if (m_Snapshot.NativeMemoryRegions.UsesDynamicHeapAllocator)
                    {
                        // Native Allocations can't reside outside of A Native Region when using the Dynamic Heap Allocator
                        Render(m_Snapshot.SortedNativeAllocations, m_GroupsNativeAlloc, i, stripGroupAddrBegin, stripGroupAddrEnd, (Color32 c) => new Color32(color.r, color.g, color.b, c.a));
                    }
                    else
                    {
                        // Math.Max((byte)1, c.a) because Native Allocations can fall outside of Regions but should still be drawn at full oppacity. a == 0 means no region, a == 1 means Allocation/Object outside of Region, a > 1 is the region index
                        // change to Math.Max((byte)0, c.a) to find such objects more easily
                        Render(m_Snapshot.SortedNativeAllocations, m_GroupsNativeAlloc, i, stripGroupAddrBegin, stripGroupAddrEnd, (Color32 c) => new Color32(color.r, color.g, color.b, Math.Max((byte)1, c.a)));
                    }
                }

                if (ShowDisplayElement(DisplayElements.NativeObjects))
                {
                    Color32 color = m_ColorNative[(int)EntryColors.Object];
                    if (m_Snapshot.NativeMemoryRegions.UsesDynamicHeapAllocator)
                    {
                        // Native Objects can't reside outside of A Native Region when using the Dynamic Heap Allocator
                        Render(m_Snapshot.SortedNativeAllocations, m_GroupsNativeAlloc, i, stripGroupAddrBegin, stripGroupAddrEnd, (Color32 c) => new Color32(color.r, color.g, color.b, c.a));
                    }
                    else
                    {
                        // Math.Max((byte)1, c.a) because Native Objects can fall outside of Regions but should still be drawn at full oppacity. a == 0 means no region, a == 1 means Allocation/Object outside of Region, a > 1 is the region index
                        // change to Math.Max((byte)0, c.a) to find such objects more easily
                        Render(m_SortedAndCappedNativeObjects, m_GroupsNativeObj, i, stripGroupAddrBegin, stripGroupAddrEnd, (Color32 c) => new Color32(color.r, color.g, color.b, Math.Max((byte)1, c.a)));
                    }
                }

                if (ShowDisplayElement(DisplayElements.ManagedObjects))
                {
                    Color32 color = m_ColorManaged[(int)EntryColors.Object];
                    Render(m_Snapshot.SortedManagedObjects, m_GroupsMangedObj, i, stripGroupAddrBegin, stripGroupAddrEnd, (Color32 c) => new Color32(color.r, color.g, color.b, c.a));
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
                BindDefaultMaterial();

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

            if (m_Snapshot.HasMemoryLabelSizesAndGCHeapTypes)
            {
                GUI.backgroundColor = m_ColorManaged[(int)EntryColors.Allocation];
                GUILayout.Toggle(true, "Virtual Machine", Styles.MemoryMap.SeriesLabel);
                GUILayout.Space(Styles.MemoryMap.LegendSpacerWidth);

                GUI.backgroundColor = m_ColorManaged[(int)EntryColors.Region];
                GUILayout.Toggle(true, "Managed Heap", Styles.MemoryMap.SeriesLabel);
                GUILayout.Space(Styles.MemoryMap.LegendSpacerWidth);
            }
            else
            {
                GUI.backgroundColor = m_ColorManaged[(int)EntryColors.Region];
                GUILayout.Toggle(true, "Managed Memory", Styles.MemoryMap.SeriesLabel);
                GUILayout.Space(Styles.MemoryMap.LegendSpacerWidth);
            }

            if (ShowDisplayElement(DisplayElements.ManagedObjects))
            {
                GUI.backgroundColor = m_ColorManaged[(int)EntryColors.Object];
                GUILayout.Toggle(true, "Managed Object", Styles.MemoryMap.SeriesLabel);
                GUILayout.Space(Styles.MemoryMap.LegendSpacerWidth);
            }

            GUI.backgroundColor = m_ColorNative[(int)EntryColors.Region];
            GUILayout.Toggle(true, "Native Memory (Reserved)", Styles.MemoryMap.SeriesLabel);
            GUILayout.Space(Styles.MemoryMap.LegendSpacerWidth);

            if (ShowDisplayElement(DisplayElements.Allocations))
            {
                GUI.backgroundColor = m_ColorNative[(int)EntryColors.Allocation];
                GUILayout.Toggle(true, "Native Memory (Allocated)", Styles.MemoryMap.SeriesLabel);
                GUILayout.Space(Styles.MemoryMap.LegendSpacerWidth);
            }

            if (ShowDisplayElement(DisplayElements.NativeObjects))
            {
                GUI.backgroundColor = m_ColorNative[(int)EntryColors.Object];
                GUILayout.Toggle(true, "Native Object", Styles.MemoryMap.SeriesLabel);
                GUILayout.Space(Styles.MemoryMap.LegendSpacerWidth);
            }

            if (ShowDisplayElement(DisplayElements.VirtualMemory))
            {
                GUI.backgroundColor = m_ColorNative[(int)EntryColors.VirtualMemory];
                GUILayout.Toggle(true, "Virtual Memory", Styles.MemoryMap.SeriesLabel);
                GUILayout.Space(Styles.MemoryMap.LegendSpacerWidth);
            }
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

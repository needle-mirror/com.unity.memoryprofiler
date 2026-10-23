using System;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;

namespace Unity.MemoryProfiler.Editor.Managed
{
    /// <summary>
    /// Answers whether an address is the start of an object in the backend-provided object table
    /// (<see cref="CachedSnapshot.ManagedObjectsEntriesCache.Target"/>).
    /// Expects each heap section's objects to be listed contiguously in ascending address order, as a linear heap walk
    /// reports them, and then searches only that section's rows. Falls back to a sorted copy of all addresses otherwise.
    /// Only references the heap sections and the object table, which it doesn't own.
    /// </summary>
    /// <remarks>
    /// Measured on a 586k-object CoreCLR Editor snapshot with 14k lookups (build + lookups, release Editor) -
    /// total time (support structure build up + 14k lookups time) and memory usage:
    /// This indexes in 4 ms and uses ~340 bytes, sorted copy - 17 ms and uses 4.7 MB, preallocated NativeHashSet - 32 ms and uses ~10 MB,
    /// preallocated HashSet - 48 ms.
    /// </remarks>
    struct BackendObjectAddressIndex : IDisposable
    {
        readonly CachedSnapshot.ManagedMemorySectionEntriesCache m_HeapSections;
        readonly DynamicArray<ulong> m_Targets;
        DynamicArray<long> m_SectionFirstRow;
        DynamicArray<long> m_SectionEndRow;
        DynamicArray<ulong> m_SortedAddresses;

        public readonly bool IsCreated => m_SectionFirstRow.IsCreated;
        public readonly bool UsesSortedCopy => m_SortedAddresses.IsCreated;

        public BackendObjectAddressIndex(in CachedSnapshot.ManagedMemorySectionEntriesCache heapSections, in DynamicArray<ulong> targets)
        {
            m_HeapSections = heapSections;
            m_Targets = targets;
            m_SectionFirstRow = new DynamicArray<long>(heapSections.Count, Allocator.Persistent, memClear: true);
            m_SectionEndRow = new DynamicArray<long>(heapSections.Count, Allocator.Persistent, memClear: true);
            m_SortedAddresses = default;

            if (!TryBuildSectionRanges())
            {
                m_SortedAddresses = new DynamicArray<ulong>(targets.Count, Allocator.Persistent);
                for (long i = 0; i < targets.Count; i++)
                    m_SortedAddresses[i] = targets[i];
                DynamicArrayAlgorithms.IntrospectiveSort(m_SortedAddresses, 0, m_SortedAddresses.Count);
            }
        }

        public unsafe bool Contains(ulong address)
        {
            if (m_SortedAddresses.IsCreated)
                return DynamicArrayAlgorithms.BinarySearch(m_SortedAddresses, address) >= 0;

            var section = m_HeapSections.FindSectionContaining(address);
            if (section < 0)
                return false;

            // Non-owning view of just this section's rows
            var firstRow = m_SectionFirstRow[section];
            var sectionTargets = DynamicArray<ulong>.ConvertExistingDataToDynamicArray(
                m_Targets.GetUnsafeTypedPtr() + firstRow, m_SectionEndRow[section] - firstRow, Allocator.None);
            return DynamicArrayAlgorithms.BinarySearch(sectionTargets, address) >= 0;
        }

        bool TryBuildSectionRanges()
        {
            long current = -1;
            ulong currentStart = 0, currentEnd = 0;
            for (long row = 0; row < m_Targets.Count; row++)
            {
                var address = m_Targets[row];
                if (current >= 0 && address >= currentStart && address < currentEnd)
                {
                    // if the addresses are not in ascending order withing a section, the assumption of these being reported in order no longer holds and this approach is invalid.
                    if (address <= m_Targets[row - 1])
                        return false;
                    continue;
                }

                if (current >= 0)
                    m_SectionEndRow[current] = row;
                current = m_HeapSections.FindSectionContaining(address);
                // An end row is only set once a section's stretch of rows is over, so a set one means its objects aren't contiguous.
                if (current < 0 || m_SectionEndRow[current] != 0)
                    return false;
                m_SectionFirstRow[current] = row;
                currentStart = m_HeapSections.StartAddress[current];
                currentEnd = currentStart + m_HeapSections.SectionSize[current];
            }
            // The last section's rows run to the end of the table.
            if (current >= 0)
                m_SectionEndRow[current] = m_Targets.Count;
            return true;
        }

        public void Dispose()
        {
            if (m_SectionFirstRow.IsCreated)
                m_SectionFirstRow.Dispose();
            if (m_SectionEndRow.IsCreated)
                m_SectionEndRow.Dispose();
            if (m_SortedAddresses.IsCreated)
                m_SortedAddresses.Dispose();
        }
    }
}

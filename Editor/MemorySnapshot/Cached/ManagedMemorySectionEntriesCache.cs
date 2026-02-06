using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Format;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;
using Unity.Profiling;
using UnityEditor;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;

namespace Unity.MemoryProfiler.Editor
{
    internal static class ManagedMemorySectionEntriesCacheExtensions
    {
        static readonly string[] k_SectionNames = new string[]
        {
            k_VMSection,
            k_GCSection,
            k_ActiveGCSection,
            k_StackSection,
            k_ManagedMemorySection
        };
        static readonly string k_VMSection = L10n.Tr("Virtual Machine Memory Section");
        static readonly string k_GCSection = L10n.Tr("Managed Heap Section");
        static readonly string k_ActiveGCSection = L10n.Tr("Active Managed Heap Section");
        static readonly string k_StackSection = L10n.Tr("Managed Stack Section");
        static readonly string k_ManagedMemorySection = L10n.Tr("Managed Memory Section (unclear if Heap or Virtual Machine memory, please update Unity)");

        public static string SectionName(this ManagedMemorySectionEntriesCache cache, long index) => k_SectionNames[(int)cache.NamedHeapSectionType[index]];
    }

    internal partial class CachedSnapshot
    {
        public enum MemorySectionType : byte
        {
            GarbageCollector,
            VirtualMachine
        }

        // Eventual TODO: Add on demand load of sections, and unused chunks unload
        [BurstCompile]
        public struct ManagedMemorySectionEntriesCache : IDisposable
        {
            static readonly ProfilerMarker k_CacheFind = new ProfilerMarker("ManagedMemorySectionEntriesCache.Find");
            public long Count;
            public readonly DynamicArray<ulong> StartAddress;
            public readonly DynamicArray<ulong> SectionSize;
            public readonly DynamicArray<MemorySectionType> SectionType;
            public readonly DynamicArray<HeapSectionTypeNames> NamedHeapSectionType;
            public readonly NestedDynamicArray<byte> Bytes => m_BytesReadOp.CompleteReadAndGetNestedResults();
            NestedDynamicSizedArrayReadOperation<byte> m_BytesReadOp;
            ulong m_MinAddress;
            ulong m_MaxAddress;
            const ulong k_ReferenceBit = 1UL << 63;

            public enum HeapSectionTypeNames
            {
                VMSection,
                GCSection,
                ActiveGCSection,
                StackSection,
                ManagedMemorySection
            }
            public readonly ulong VirtualMachineMemoryReserved;
            // if the snapshot format is missing the VM section bit, this number will include VM memory
            public readonly ulong ManagedHeapMemoryReserved;
            public readonly ulong TotalActiveManagedHeapSectionReserved;
            public readonly ulong StackMemoryReserved;

            public readonly long FirstAssumedActiveHeapSectionIndex;
            public readonly long LastAssumedActiveHeapSectionIndex;

            public ManagedMemorySectionEntriesCache(ref IFileReader reader, bool HasGCHeapTypes, bool readStackMemory)
            {
                Count = reader.GetEntryCount(readStackMemory ? EntryType.ManagedStacks_StartAddress : EntryType.ManagedHeapSections_StartAddress);
                m_MinAddress = m_MaxAddress = 0;
                StackMemoryReserved = ManagedHeapMemoryReserved = VirtualMachineMemoryReserved = VirtualMachineMemoryReserved = TotalActiveManagedHeapSectionReserved = 0;
                LastAssumedActiveHeapSectionIndex = FirstAssumedActiveHeapSectionIndex = 0;

                SectionType = new DynamicArray<MemorySectionType>(Count, Allocator.Persistent, true);
                NamedHeapSectionType = new DynamicArray<HeapSectionTypeNames>(Count, Allocator.Persistent, true);
                SectionSize = new DynamicArray<ulong>(Count, Allocator.Persistent);
                if (Count == 0)
                {
                    StartAddress = new DynamicArray<ulong>(Count, Allocator.Persistent);
                    m_BytesReadOp = default;
                    return;
                }
                StartAddress = reader.Read(readStackMemory ? EntryType.ManagedStacks_StartAddress : EntryType.ManagedHeapSections_StartAddress, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();

                //long heapSectionIndex = 0;
                //long vmSectionIndex = 0;
                if (HasGCHeapTypes)
                {
                    for (long i = 0; i < StartAddress.Count; ++i)
                    {
                        var encoded = StartAddress[i];
                        StartAddress[i] = encoded & ~k_ReferenceBit; //unmask addr
                        var isVMSection = (encoded & k_ReferenceBit) == k_ReferenceBit;
                        SectionType[i] = isVMSection ? MemorySectionType.VirtualMachine : MemorySectionType.GarbageCollector; //get heaptype
                        if (isVMSection)
                            NamedHeapSectionType[i] = HeapSectionTypeNames.VMSection;
                        else
                            NamedHeapSectionType[i] = HeapSectionTypeNames.GCSection;
                    }
                }
                else
                {
                    for (long i = 0; i < StartAddress.Count; ++i)
                    {
                        NamedHeapSectionType[i] = HeapSectionTypeNames.ManagedMemorySection;
                    }
                }
                if (readStackMemory)
                {
                    for (long i = 0; i < Count; ++i)
                    {
                        NamedHeapSectionType[i] = HeapSectionTypeNames.StackSection;
                    }
                }

                var entryType = readStackMemory ? EntryType.ManagedStacks_Bytes : EntryType.ManagedHeapSections_Bytes;

                m_BytesReadOp = reader.AsyncReadDynamicSizedArray<byte>(entryType, 0, Count, Allocator.Persistent);

                // For Sorting we don't need the Async reading of the Managed Stack / Heap bytes to be loaded yet
                SortSectionEntries(ref StartAddress, ref SectionSize, ref SectionType, ref NamedHeapSectionType, ref m_BytesReadOp, readStackMemory);
                m_MinAddress = StartAddress[0];
                m_MaxAddress = StartAddress[Count - 1] + (ulong)m_BytesReadOp.Count(Count - 1);

                var foundLastAssumedActiveHeap = false;
                var foundFirstAssumedActiveHeap = false;
                for (long i = Count - 1; i >= 0; i--)
                {
                    if (readStackMemory)
                        StackMemoryReserved += SectionSize[i];
                    else
                    {
                        if (SectionType[i] == MemorySectionType.GarbageCollector)
                        {
                            ManagedHeapMemoryReserved += SectionSize[i];
                            if (!foundLastAssumedActiveHeap)
                            {
                                FirstAssumedActiveHeapSectionIndex = i;
                                LastAssumedActiveHeapSectionIndex = i;
                                foundLastAssumedActiveHeap = true;
                            }
                            else if (!foundFirstAssumedActiveHeap && StartAddress[i] + SectionSize[i] + VMTools.X64ArchPtrSize > StartAddress[FirstAssumedActiveHeapSectionIndex])
                            {
                                FirstAssumedActiveHeapSectionIndex = i;
                            }
                            else
                                foundFirstAssumedActiveHeap = true;
                        }
                        else
                            VirtualMachineMemoryReserved += SectionSize[i];
                    }
                }
                if (foundFirstAssumedActiveHeap && foundLastAssumedActiveHeap)
                {
                    for (long i = FirstAssumedActiveHeapSectionIndex; i <= LastAssumedActiveHeapSectionIndex; i++)
                    {
                        NamedHeapSectionType[i] = HeapSectionTypeNames.ActiveGCSection;
                    }
                }
                TotalActiveManagedHeapSectionReserved = StartAddress[LastAssumedActiveHeapSectionIndex] + SectionSize[LastAssumedActiveHeapSectionIndex] - StartAddress[FirstAssumedActiveHeapSectionIndex];
            }

            public void CompleteHeapBytesRead()
            {
                m_BytesReadOp.Complete();
            }

            [MethodImpl(MethodImplementationHelper.AggressiveInlining), BurstCompile(CompileSynchronously = true, DisableDirectCall = false, DisableSafetyChecks = true, Debug = false)]
            public bool Find(ulong address, VirtualMachineInformation virtualMachineInformation, out BytesAndOffset bytesAndOffset)
            {
                bytesAndOffset = Find(address, virtualMachineInformation, out MemorySectionType _);
                return bytesAndOffset.IsValid;
            }

            [BurstCompile(CompileSynchronously = true, DisableDirectCall = false, DisableSafetyChecks = true, Debug = false)]
            public BytesAndOffset Find(ulong address, VirtualMachineInformation virtualMachineInformation, out MemorySectionType sectionType)
            {
                using (k_CacheFind.Auto())
                {
                    var bytesAndOffset = new BytesAndOffset();
                    sectionType = MemorySectionType.VirtualMachine;

                    if (address != 0 && address >= m_MinAddress && address < m_MaxAddress)
                    {
                        long idx = DynamicArrayAlgorithms.BinarySearch(StartAddress, address);
                        if (idx < 0)
                        {
                            // -1 means the address is smaller than the first StartAddress, early out with an invalid bytesAndOffset
                            if (idx == -1)
                                return bytesAndOffset;
                            // otherwise, a negative Index just means there was no direct hit and ~idx - 1 will give us the index to the next smaller StartAddress
                            idx = ~idx - 1;
                        }

                        if (address >= StartAddress[idx] && address < (StartAddress[idx] + (ulong)Bytes.Count(idx)))
                        {
                            bytesAndOffset = new BytesAndOffset(Bytes[idx], virtualMachineInformation.PointerSize, address - StartAddress[idx]);
                        }
                        sectionType = SectionType[idx];
                    }

                    return bytesAndOffset;
                }
            }

            [StructLayout(LayoutKind.Sequential)]
            readonly struct SortIndexHelper : IComparable<SortIndexHelper>
            {
                public readonly long Index;
                public readonly ulong StartAddress;

                public SortIndexHelper(ref long index, ref ulong startAddress)
                {
                    Index = index;
                    StartAddress = startAddress;
                }

                [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
                public int CompareTo(SortIndexHelper other) => StartAddress.CompareTo(other.StartAddress);
            }

            static void SortSectionEntries(ref DynamicArray<ulong> startAddresses, ref DynamicArray<ulong> sizes, ref DynamicArray<MemorySectionType> associatedSectionType, ref DynamicArray<HeapSectionTypeNames> associatedSectionTypeNames,
                ref NestedDynamicSizedArrayReadOperation<byte> associatedByteArrayReadOp, bool isStackMemory)
            {
                using var sortMapping = new DynamicArray<SortIndexHelper>(startAddresses.Count, Allocator.Temp);

                for (long i = 0; i < sortMapping.Count; ++i)
                {
                    sortMapping[i] = new SortIndexHelper(ref i, ref startAddresses[i]);
                }

                var startAddr = startAddresses;
                DynamicArrayAlgorithms.IntrospectiveSort(sortMapping, 0, startAddresses.Count);
                unsafe
                {
                    {
                        using var newSortedAddresses = new DynamicArray<ulong>(startAddresses.Count, Allocator.Temp);
                        using var newSortedSectionTypes = isStackMemory ? default : new DynamicArray<MemorySectionType>(startAddresses.Count, Allocator.Temp);
                        using var newSortedSectionTypeNames = new DynamicArray<HeapSectionTypeNames>(startAddresses.Count, Allocator.Temp);

                        for (long i = 0; i < startAddresses.Count; ++i)
                        {
                            long idx = sortMapping[i].Index;
                            newSortedAddresses[i] = startAddresses[idx];
                            newSortedSectionTypeNames[i] = associatedSectionTypeNames[idx];
                            sizes[i] = (ulong)associatedByteArrayReadOp.Count(idx);

                            if (!isStackMemory)
                                newSortedSectionTypes[i] = associatedSectionType[idx];
                        }

                        UnsafeUtility.MemCpy(startAddresses.GetUnsafePtr(), newSortedAddresses.GetUnsafePtr(), sizeof(ulong) * startAddresses.Count);
                        UnsafeUtility.MemCpy(associatedSectionTypeNames.GetUnsafePtr(), newSortedSectionTypeNames.GetUnsafePtr(), sizeof(HeapSectionTypeNames) * newSortedSectionTypeNames.Count);

                        if (!isStackMemory)
                            UnsafeUtility.MemCpy(associatedSectionType.GetUnsafePtr(), newSortedSectionTypes.GetUnsafePtr(), sizeof(MemorySectionType) * associatedSectionType.Count);
                    }

                    using var sortedIndice = new DynamicArray<long>(startAddresses.Count, Allocator.Temp);
                    UnsafeUtility.MemCpyStride(sortedIndice.GetUnsafePtr(), sizeof(long), sortMapping.GetUnsafePtr(), sizeof(SortIndexHelper), sizeof(SortIndexHelper), (int)startAddresses.Count);
                    associatedByteArrayReadOp.Sort(sortedIndice);
                }
            }

            public void Dispose()
            {
                Count = 0;
                m_MinAddress = m_MaxAddress = 0;
                StartAddress.Dispose();
                SectionType.Dispose();
                SectionSize.Dispose();
                NamedHeapSectionType.Dispose();
                if (m_BytesReadOp.IsCreated)
                {
                    // Dispose the read operation first to abort it ...
                    m_BytesReadOp.Dispose();
                    // ... before disposing the result, as otherwise we'd sync on a pending read op.
                    Bytes.Dispose();
                    m_BytesReadOp = default;
                }
            }
        }
    }
}

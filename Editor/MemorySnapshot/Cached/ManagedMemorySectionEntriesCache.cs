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
            k_ManagedMemorySection,
            k_Gen0Section,
            k_Gen1Section,
            k_Gen2Section,
            k_LargeObjectHeapSection,
            k_PinnedObjectHeapSection,
            k_NonGCSection
        };
        static readonly string k_VMSection = L10n.Tr("Virtual Machine Memory Section");
        static readonly string k_GCSection = L10n.Tr("Managed Heap Section");
        static readonly string k_ActiveGCSection = L10n.Tr("Active Managed Heap Section");
        static readonly string k_StackSection = L10n.Tr("Managed Stack Section");
        static readonly string k_ManagedMemorySection = L10n.Tr("Managed Memory Section (unclear if Heap or Virtual Machine memory, please update Unity)");
        static readonly string k_Gen0Section = L10n.Tr("Managed Heap Section (Gen0)");
        static readonly string k_Gen1Section = L10n.Tr("Managed Heap Section (Gen1)");
        static readonly string k_Gen2Section = L10n.Tr("Managed Heap Section (Gen2)");
        static readonly string k_LargeObjectHeapSection = L10n.Tr("Managed Heap Section (Large Object Heap)");
        static readonly string k_PinnedObjectHeapSection = L10n.Tr("Managed Heap Section (Pinned Object Heap)");
        static readonly string k_NonGCSection = L10n.Tr("Managed Heap Section (Frozen/Non-GC)");

        public static string SectionName(this ManagedMemorySectionEntriesCache cache, long index) => k_SectionNames[(int)cache.NamedHeapSectionType[index]];
    }

    internal partial class CachedSnapshot
    {
        public enum MemorySectionType : byte
        {
            GarbageCollector,
            VirtualMachine
        }

        /// <summary>
        /// Heap-chunk category, mirroring the native <c>IScriptingGCHeapCallbacks::HeapChunkType</c>
        /// (Modules/Scripting/Include/Scripting/ScriptingBackend/ScriptingProfilerApi.h) bit-for-bit —
        /// same value order, <c>kVM</c> = 0. Read from <c>ManagedHeapSections_Flags</c> (format v20+,
        /// <see cref="Format.FormatVersion.StaticFieldAndNonGCHeapVersion"/>); only populated for heap
        /// sections, never for stacks. Supersedes the address bit-63 VM encoding
        /// (<see cref="MemorySectionType"/>) for new readers — the old encoding is kept, unchanged, for
        /// back-compat, so both remain valid to read.
        /// </summary>
        public enum HeapChunkType : byte
        {
            VM = 0,
            Gen0,
            Gen1,
            Gen2,
            LargeObjectHeap,
            PinnedObjectHeap,
            NonGC,
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
            /// <summary>
            /// Per-heap-chunk <see cref="HeapChunkType"/> (format v20+). Empty for stacks and for older
            /// files — check <see cref="Count"/> against the reader's format version before indexing.
            /// </summary>
            public readonly DynamicArray<HeapChunkType> Flags;

            /// <summary>
            /// Mapping from sorted section index to original file index.
            /// Used for on-demand loading after heap bytes are unloaded.
            /// </summary>
            public readonly DynamicArray<long> SortedToOriginalIndex;

            /// <summary>
            /// Get heap bytes. Throws if bytes were unloaded after crawling.
            /// </summary>
            /// <exception cref="InvalidOperationException">Thrown if heap bytes have been unloaded.</exception>
            public readonly NestedDynamicArray<byte> Bytes
            {
                get
                {
                    if (m_BytesUnloaded)
                        throw new InvalidOperationException(
                            "Heap bytes have been unloaded to save memory. " +
                            $"Use CachedSnapshot.{nameof(CachedSnapshot.TableEntryNames)} for cached string and array display info " +
                            $"or CachedSnapshot.{nameof(CachedSnapshot.OnDemandHeapCache)} to read individual heap sections from file on demand." +
                            $"For the latter, prefer calling CachedSnapshot.{nameof(CachedSnapshot.FindHeapBytes)}, CachedSnapshot.{nameof(CachedSnapshot.GetHeapBytes)} or CachedSnapshot.{nameof(CachedSnapshot.TryGetHeapBytesWithFastPath)}.");
                    return m_BytesReadOp.CompleteReadAndGetNestedResults();
                }
            }

            NestedDynamicSizedArrayReadOperation<byte> m_BytesReadOp;
            ulong m_MinAddress;
            ulong m_MaxAddress;
            const ulong k_ReferenceBit = 1UL << 63;

            /// <summary>
            /// Whether heap bytes are still loaded in memory.
            /// After crawling, heap bytes may be unloaded to save memory.
            /// </summary>
            public bool HeapBytesLoaded => !m_BytesUnloaded;
            [MarshalAs(UnmanagedType.U1)]
            bool m_BytesUnloaded;

            public enum HeapSectionTypeNames
            {
                VMSection,
                GCSection,
                ActiveGCSection,
                StackSection,
                ManagedMemorySection,
                // The remaining values mirror HeapChunkType 1:1 (format v20+, heap sections only) for
                // display purposes - SectionType still only distinguishes GarbageCollector/VirtualMachine
                // (all of these except NonGCSection stay GarbageCollector there; NonGCSection does too,
                // since frozen/pinned literal storage is still real, crawlable object data that the
                // crawler/inspector need to walk the same way as live GC heap), so only NamedHeapSectionType
                // gets the finer-grained breakdown.
                Gen0Section,
                Gen1Section,
                Gen2Section,
                LargeObjectHeapSection,
                PinnedObjectHeapSection,
                NonGCSection
            }
            public readonly ulong VirtualMachineMemoryReserved;
            // if the snapshot format is missing the VM section bit, this number will include VM memory
            public readonly ulong ManagedHeapMemoryReserved;
            public readonly ulong TotalActiveManagedHeapSectionReserved;
            public readonly ulong StackMemoryReserved;

            public readonly long FirstAssumedActiveHeapSectionIndex;
            public readonly long LastAssumedActiveHeapSectionIndex;

            public ManagedMemorySectionEntriesCache(ref IFileReader reader, bool HasGCHeapTypes, bool readStackMemory, ref SnapshotMetricsCollector metricsCollector)
            {
                Count = reader.GetEntryCount(readStackMemory ? EntryType.ManagedStacks_StartAddress : EntryType.ManagedHeapSections_StartAddress);
                m_MinAddress = m_MaxAddress = 0;
                StackMemoryReserved = ManagedHeapMemoryReserved = VirtualMachineMemoryReserved = TotalActiveManagedHeapSectionReserved = 0;
                LastAssumedActiveHeapSectionIndex = FirstAssumedActiveHeapSectionIndex = 0;
                m_BytesUnloaded = false;

                SectionType = new DynamicArray<MemorySectionType>(Count, Allocator.Persistent, true);
                NamedHeapSectionType = new DynamicArray<HeapSectionTypeNames>(Count, Allocator.Persistent, true);
                SectionSize = new DynamicArray<ulong>(Count, Allocator.Persistent);
                SortedToOriginalIndex = new DynamicArray<long>(Count, Allocator.Persistent);
                // ManagedHeapSections_Flags only exists from format v20 onward, and only for heap
                // sections (never stacks) — pre-allocate a zero-filled default for every other case,
                // read below only for v20+ heap sections to avoid allocating twice.
                var hasFlagsToRead = !readStackMemory && reader.FormatVersion >= FormatVersion.StaticFieldAndNonGCHeapVersion;
                Flags = hasFlagsToRead ? default : new DynamicArray<HeapChunkType>(Count, Allocator.Persistent, true);

                // Store initial values
                metricsCollector.RecordManagedSectionDiagnostics(isHeap: readStackMemory, Count,
                    (long)(ManagedHeapMemoryReserved + VirtualMachineMemoryReserved));
                if (Count == 0)
                {
                    StartAddress = new DynamicArray<ulong>(Count, Allocator.Persistent);
                    m_BytesReadOp = default;
                    return;
                }
                StartAddress = reader.Read(readStackMemory ? EntryType.ManagedStacks_StartAddress : EntryType.ManagedHeapSections_StartAddress, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();

                if (hasFlagsToRead)
                    Flags = reader.Read(EntryType.ManagedHeapSections_Flags, 0, Count, Allocator.Persistent).Result.Reinterpret<HeapChunkType>();

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
                // Flags (format v20+, heap sections only) supersedes the bit-63 VM encoding above for
                // display purposes - SectionType still only distinguishes GarbageCollector/VirtualMachine
                // (unaffected: the crawler/inspector need to walk every one of these, including NonGC
                // frozen/pinned literal storage, the same way as live GC heap), but NamedHeapSectionType
                // gets the finer-grained per-generation/NonGC breakdown for summary/breakdown display.
                if (hasFlagsToRead)
                {
                    for (long i = 0; i < Flags.Count; ++i)
                    {
                        NamedHeapSectionType[i] = Flags[i] switch
                        {
                            HeapChunkType.VM => HeapSectionTypeNames.VMSection,
                            HeapChunkType.Gen0 => HeapSectionTypeNames.Gen0Section,
                            HeapChunkType.Gen1 => HeapSectionTypeNames.Gen1Section,
                            HeapChunkType.Gen2 => HeapSectionTypeNames.Gen2Section,
                            HeapChunkType.LargeObjectHeap => HeapSectionTypeNames.LargeObjectHeapSection,
                            HeapChunkType.PinnedObjectHeap => HeapSectionTypeNames.PinnedObjectHeapSection,
                            HeapChunkType.NonGC => HeapSectionTypeNames.NonGCSection,
                            _ => NamedHeapSectionType[i]
                        };
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
                metricsCollector.RecordLoadingPhase((readStackMemory ? "ManagedStacks" : "ManagedHeapSections") + ": create bytes readOp");
                m_BytesReadOp = reader.AsyncReadDynamicSizedArray<byte>(entryType, 0, Count, Allocator.Persistent);
                metricsCollector.RecordLoadingPhase((readStackMemory ? "ManagedStacks" : "ManagedHeapSections") + ": created bytes readOp");

                // For Sorting we don't need the Async reading of the Managed Stack / Heap bytes to be loaded yet
                SortSectionEntries(ref StartAddress, ref SectionSize, ref SectionType, ref NamedHeapSectionType, ref Flags, ref m_BytesReadOp, ref SortedToOriginalIndex, readStackMemory);
                metricsCollector.RecordLoadingPhase((readStackMemory ? "ManagedStacks" : "ManagedHeapSections") + ": sorted section entries");
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
                metricsCollector.RecordLoadingPhase((readStackMemory ? "ManagedStacks" : "ManagedHeapSections") + ": calculated total reserved");
                // Store populated values
                metricsCollector.RecordManagedSectionDiagnostics(isHeap: readStackMemory, Count,
                    (long)(ManagedHeapMemoryReserved + VirtualMachineMemoryReserved));

                if (foundFirstAssumedActiveHeap && foundLastAssumedActiveHeap)
                {
                    // Only upgrade the generic GCSection label - a v20+ per-generation label
                    // (Gen0/Gen1/Gen2/LOH/POH/NonGC) is more specific and must survive this pass.
                    for (long i = FirstAssumedActiveHeapSectionIndex; i <= LastAssumedActiveHeapSectionIndex; i++)
                    {
                        if (NamedHeapSectionType[i] == HeapSectionTypeNames.GCSection)
                            NamedHeapSectionType[i] = HeapSectionTypeNames.ActiveGCSection;
                    }
                }
                metricsCollector.RecordLoadingPhase((readStackMemory ? "ManagedStacks" : "ManagedHeapSections") + ": calculated active heap section");
                TotalActiveManagedHeapSectionReserved = StartAddress[LastAssumedActiveHeapSectionIndex] + SectionSize[LastAssumedActiveHeapSectionIndex] - StartAddress[FirstAssumedActiveHeapSectionIndex];

                metricsCollector.RecordLoadingPhase((readStackMemory ? "ManagedStacks" : "ManagedHeapSections") + ": constructor done");
            }

            public void CompleteHeapBytesRead()
            {
                m_BytesReadOp.Complete();
            }

            /// <summary>
            /// Unload bulk heap bytes to free memory. Call after crawling is complete.
            /// </summary>
            /// <remarks>
            /// After calling this method:
            /// - <see cref="HeapBytesLoaded"/> will return false
            /// - Accessing <see cref="Bytes"/> will throw an exception
            /// - <see cref="Find"/> will not work for byte access
            /// </remarks>
            public void UnloadHeapBytes()
            {
                if (!m_BytesUnloaded && m_BytesReadOp.IsCreated)
                {
                    // Complete any pending async read first
                    m_BytesReadOp.Complete();
                    var bytes = m_BytesReadOp.CompleteReadAndGetNestedResults();
#if DEBUG_VALIDATION
                    // while validating, fill the bytes with a known pattern to catch any invalid access after unloading
                    bytes.MemSetAllData(0xCD);
#endif
                    // Dispose the actual bytes data
                    bytes.Dispose();
                    // Dispose the read operation
                    m_BytesReadOp.Dispose();
                    m_BytesReadOp = default;
                    m_BytesUnloaded = true;
                }
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

                    // If bytes have been unloaded, we can't find anything
                    if (m_BytesUnloaded)
                        return bytesAndOffset;

                    var idx = FindSectionContaining(address);
                    if (idx >= 0)
                    {
                        // No need to use the SortedToOriginalIndex mapping because we are accessing the sorted bytes
                        bytesAndOffset = new BytesAndOffset(Bytes[idx], virtualMachineInformation.PointerSize, idx, false, address - StartAddress[idx]);
                        sectionType = SectionType[idx];
                    }

                    return bytesAndOffset;
                }
            }

            /// <summary>
            /// Find which section contains the given address.
            /// Works even after heap bytes have been unloaded.
            /// </summary>
            /// <param name="address">The address to search for.</param>
            /// <returns>The section index, or -1 if not found.</returns>
            [MethodImpl(MethodImplementationHelper.AggressiveInlining), BurstCompile(CompileSynchronously = true, DisableDirectCall = false, DisableSafetyChecks = true, Debug = false)]
            public long FindSectionContaining(ulong address)
            {
                if (address == 0 || address < m_MinAddress || address >= m_MaxAddress)
                    return -1;

                long idx = DynamicArrayAlgorithms.BinarySearch(StartAddress, address);
                if (idx < 0)
                {
                    // -1 means the address is smaller than the first StartAddress, early out
                    if (idx == -1)
                        return -1;
                    // otherwise, a negative Index just means there was no direct hit and ~idx - 1 will give us the index to the next smaller StartAddress
                    idx = ~idx - 1;
                }

                if (address >= StartAddress[idx] && address < (StartAddress[idx] + SectionSize[idx]))
                {
                    return idx;
                }

                return -1;
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
                ref DynamicArray<HeapChunkType> associatedFlags,
                ref NestedDynamicSizedArrayReadOperation<byte> associatedByteArrayReadOp, ref DynamicArray<long> sortedToOriginalIndex, bool isStackMemory)
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
                        // Flags is only populated for heap sections (never stacks) — mirrors the
                        // isStackMemory gating already used for associatedSectionType above.
                        using var newSortedFlags = isStackMemory ? default : new DynamicArray<HeapChunkType>(startAddresses.Count, Allocator.Temp);

                        for (long i = 0; i < startAddresses.Count; ++i)
                        {
                            long idx = sortMapping[i].Index;
                            newSortedAddresses[i] = startAddresses[idx];
                            newSortedSectionTypeNames[i] = associatedSectionTypeNames[idx];
                            sizes[i] = (ulong)associatedByteArrayReadOp.Count(idx);
                            // Store the mapping from sorted index to original file index
                            sortedToOriginalIndex[i] = idx;

                            if (!isStackMemory)
                            {
                                newSortedSectionTypes[i] = associatedSectionType[idx];
                                newSortedFlags[i] = associatedFlags[idx];
                            }
                        }

                        UnsafeUtility.MemCpy(startAddresses.GetUnsafePtr(), newSortedAddresses.GetUnsafePtr(), sizeof(ulong) * startAddresses.Count);
                        UnsafeUtility.MemCpy(associatedSectionTypeNames.GetUnsafePtr(), newSortedSectionTypeNames.GetUnsafePtr(), sizeof(HeapSectionTypeNames) * newSortedSectionTypeNames.Count);

                        if (!isStackMemory)
                        {
                            UnsafeUtility.MemCpy(associatedSectionType.GetUnsafePtr(), newSortedSectionTypes.GetUnsafePtr(), sizeof(MemorySectionType) * associatedSectionType.Count);
                            UnsafeUtility.MemCpy(associatedFlags.GetUnsafePtr(), newSortedFlags.GetUnsafePtr(), sizeof(HeapChunkType) * associatedFlags.Count);
                        }
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
                SortedToOriginalIndex.Dispose();
                Flags.Dispose();

                if (m_BytesReadOp.IsCreated && !m_BytesUnloaded)
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

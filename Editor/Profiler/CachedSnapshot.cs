using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Containers.Unsafe;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Format;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;
using Unity.Profiling;
using UnityEngine;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;

namespace Unity.MemoryProfiler.Editor
{
    internal static class VMTools
    {
        //supported archs
        public const int x64ArchPtrSize = 8;
        public const int x86ArchPtrSize = 4;
        public const int NoArchPtrSize = 0;

        public static bool ValidateVirtualMachineInfo(VirtualMachineInformation vmInfo)
        {
            if (vmInfo.PointerSize == NoArchPtrSize)
                return true; //this case can only happen on legacy UWP .NET snapshots

            if (!(vmInfo.PointerSize == x64ArchPtrSize || vmInfo.PointerSize == x86ArchPtrSize))
                return false;

            //partial checks to validate computations based on pointer size
            int expectedObjHeaderSize = 2 * vmInfo.PointerSize;

            if (expectedObjHeaderSize != vmInfo.ObjectHeaderSize)
                return false;

            if (expectedObjHeaderSize != vmInfo.AllocationGranularity)
                return false;

            return true;
        }
    }
    internal static class TypeTools
    {
        public enum FieldFindOptions
        {
            OnlyInstance,
            OnlyStatic
        }

        static void RecurseCrawlFields(ref List<int> fieldsBuffer, int ITypeArrayIndex, TypeDescriptionEntriesCache typeDescriptions, FieldDescriptionEntriesCache fieldDescriptions, FieldFindOptions fieldFindOptions, bool crawlBase)
        {
            bool isValueType = typeDescriptions.HasFlag(ITypeArrayIndex, TypeFlags.kValueType);
            if (crawlBase)
            {
                int baseTypeIndex = typeDescriptions.BaseOrElementTypeIndex[ITypeArrayIndex];
                if (crawlBase && baseTypeIndex != -1 && !isValueType)
                {
                    int baseArrayIndex = typeDescriptions.TypeIndex2ArrayIndex(baseTypeIndex);
                    RecurseCrawlFields(ref fieldsBuffer, baseArrayIndex, typeDescriptions, fieldDescriptions, fieldFindOptions, true);
                }
            }


            int iTypeIndex = typeDescriptions.TypeIndex[ITypeArrayIndex];
            var fieldIndices = typeDescriptions.FieldIndices[ITypeArrayIndex];
            for (int i = 0; i < fieldIndices.Length; ++i)
            {
                var iField = fieldIndices[i];

                if (!FieldMatchesOptions(iField, fieldDescriptions, fieldFindOptions))
                    continue;

                if (fieldDescriptions.TypeIndex[iField] == iTypeIndex && isValueType)
                {
                    // this happens in primitive types like System.Single, which is a weird type that has a field of its own type.
                    continue;
                }

                if (fieldDescriptions.Offset[iField] == -1) //TODO: verify this assumption
                {
                    // this is how we encode TLS fields. We don't support TLS fields yet.
                    continue;
                }

                fieldsBuffer.Add(iField);
            }
        }

        public static void AllFieldArrayIndexOf(ref List<int> fieldsBuffer, int ITypeArrayIndex, TypeDescriptionEntriesCache typeDescriptions, FieldDescriptionEntriesCache fieldDescriptions, FieldFindOptions findOptions, bool includeBase)
        {
            //make sure we clear before we start crawling
            fieldsBuffer.Clear();
            RecurseCrawlFields(ref fieldsBuffer, ITypeArrayIndex, typeDescriptions, fieldDescriptions, findOptions, includeBase);
        }

        static bool FieldMatchesOptions(int fieldIndex, FieldDescriptionEntriesCache fieldDescriptions, FieldFindOptions options)
        {
            if (options == FieldFindOptions.OnlyStatic)
            {
                return fieldDescriptions.IsStatic[fieldIndex] == 1;
            }
            if (options == FieldFindOptions.OnlyInstance)
            {
                return fieldDescriptions.IsStatic[fieldIndex] == 0;
            }
            return false;
        }
    }

    internal class CachedSnapshot : IDisposable
    {
        FormatVersion m_SnapshotVersion;

        public bool HasConnectionOverhaul
        {
            get { return m_SnapshotVersion >= FormatVersion.NativeConnectionsAsInstanceIdsVersion; }
        }

        public bool HasTargetAndMemoryInfo
        {
            get { return m_SnapshotVersion >= FormatVersion.ProfileTargetInfoAndMemStatsVersion; }
        }

        public bool HasMemoryLabelSizesAndGCHeapTypes
        {
            get { return m_SnapshotVersion >= FormatVersion.MemLabelSizeAndHeapIdVersion; }
        }

        public ManagedData CrawledData { internal set; get; }

        public class NativeAllocationSiteEntriesCache : IDisposable
        {
            public long Count;
            public DynamicArray<long> id = default;
            public DynamicArray<int> memoryLabelIndex = default;
            public ulong[][] callstackSymbols;

            unsafe public NativeAllocationSiteEntriesCache(ref FileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.NativeAllocationSites_Id);
                callstackSymbols = new ulong[Count][];

                if (Count == 0)
                    return;

                id = reader.Read(EntryType.NativeAllocationSites_Id, 0, Count, Allocator.Persistent).Result.Reinterpret<long>();
                memoryLabelIndex = reader.Read(EntryType.NativeAllocationSites_MemoryLabelIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();

                using (var tmpBuffer = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.NativeAllocationSites_CallstackSymbols, 0, Count);
                    tmpBuffer.Resize(tmpSize, false);
                    reader.Read(EntryType.NativeAllocationSites_CallstackSymbols, tmpBuffer, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmpBuffer, ref callstackSymbols);
                }
            }

            public string GetReadableCallstackForId(NativeCallstackSymbolEntriesCache symbols, long id)
            {
                long entryIdx = -1;
                for (long i = 0; i < this.id.Count; ++i)
                {
                    if (this.id[i] == id)
                    {
                        entryIdx = i;
                        break;
                    }
                }

                return entryIdx < 0 ? string.Empty : GetReadableCallstack(symbols, entryIdx);
            }

            public string GetReadableCallstack(NativeCallstackSymbolEntriesCache symbols, long idx)
            {
                string readableStackTrace = "";

                ulong[] callstackSymbols = this.callstackSymbols[idx];

                for (int i = 0; i < callstackSymbols.Length; ++i)
                {
                    long symbolIdx = -1;
                    ulong targetSymbol = callstackSymbols[i];
                    for (int j = 0; j < symbols.Symbol.Count; ++i)
                    {
                        if (symbols.Symbol[j] == targetSymbol)
                        {
                            symbolIdx = i;
                            break;
                        }
                    }

                    if (symbolIdx < 0)
                        readableStackTrace += "<unknown>\n";
                    else
                        readableStackTrace += symbols.ReadableStackTrace[symbolIdx];
                }

                return readableStackTrace;
            }

            public void Dispose()
            {
                id.Dispose();
                memoryLabelIndex.Dispose();
                callstackSymbols = null;
                Count = 0;
            }
        }

        public class NativeRootReferenceEntriesCache : IDisposable
        {
            public long Count;
            public DynamicArray<long> Id = default;
            public DynamicArray<ulong> AccumulatedSize = default;
            public string[] AreaName;
            public string[] ObjectName;
            public Dictionary<long, long> IdToIndex;
            public readonly ulong ExecutableAndDllsReportedValue;
            public const string ExecutableAndDllsRootReferenceName = "ExecutableAndDlls";
            readonly long k_ExecutableAndDllsRootReferenceIndex = -1;

            public NativeRootReferenceEntriesCache(ref FileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.NativeRootReferences_Id);

                AreaName = new string[Count];
                ObjectName = new string[Count];

                IdToIndex = new Dictionary<long, long>((int)Count);

                if (Count == 0)
                    return;

                Id = reader.Read(EntryType.NativeRootReferences_Id, 0, Count, Allocator.Persistent).Result.Reinterpret<long>();
                AccumulatedSize = reader.Read(EntryType.NativeRootReferences_AccumulatedSize, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();

                using (var tmpBuffer = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.NativeRootReferences_AreaName, 0, Count);
                    tmpBuffer.Resize(tmpSize, false);
                    reader.Read(EntryType.NativeRootReferences_AreaName, tmpBuffer, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmpBuffer, ref AreaName);

                    tmpSize = reader.GetSizeForEntryRange(EntryType.NativeRootReferences_ObjectName, 0, Count);
                    tmpBuffer.Resize(tmpSize, false);
                    reader.Read(EntryType.NativeRootReferences_ObjectName, tmpBuffer, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmpBuffer, ref ObjectName);
                }
                for (long i = 0; i < Count; i++)
                {
                    if (k_ExecutableAndDllsRootReferenceIndex == -1 && ObjectName[i] == ExecutableAndDllsRootReferenceName)
                    {
                        k_ExecutableAndDllsRootReferenceIndex = i;
                        ExecutableAndDllsReportedValue = AccumulatedSize[i];
                    }
                    IdToIndex.Add(Id[i], i);
                }
            }

            public void Dispose()
            {
                Id.Dispose();
                AccumulatedSize.Dispose();
                Count = 0;
                AreaName = null;
                ObjectName = null;
            }
        }

        public class NativeMemoryRegionEntriesCache : IDisposable
        {
            public long Count;
            public string[] MemoryRegionName;
            public DynamicArray<int> ParentIndex = default;
            public DynamicArray<ulong> AddressBase = default;
            public DynamicArray<ulong> AddressSize = default;
            public DynamicArray<int> FirstAllocationIndex = default;
            public DynamicArray<int> NumAllocations = default;
            public readonly bool UsesDynamicHeapAllocator = false;
            public readonly bool UsesSystemAllocator;

            const string k_DynamicHeapAllocatorName = "ALLOC_DEFAULT_MAIN";

            public NativeMemoryRegionEntriesCache(ref FileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.NativeMemoryRegions_AddressBase);
                MemoryRegionName = new string[Count];

                if (Count == 0)
                    return;

                ParentIndex = reader.Read(EntryType.NativeMemoryRegions_ParentIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                AddressBase = reader.Read(EntryType.NativeMemoryRegions_AddressBase, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                AddressSize = reader.Read(EntryType.NativeMemoryRegions_AddressSize, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                FirstAllocationIndex = reader.Read(EntryType.NativeMemoryRegions_FirstAllocationIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                NumAllocations = reader.Read(EntryType.NativeMemoryRegions_NumAllocations, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();

                using (DynamicArray<byte> tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.NativeMemoryRegions_Name, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.NativeMemoryRegions_Name, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref MemoryRegionName);
                }

                for (int i = 0; i < Count; i++)
                {
                    if (MemoryRegionName[i].StartsWith(k_DynamicHeapAllocatorName) && AddressSize[i] > 0)
                    {
                        UsesDynamicHeapAllocator = true;
                        break;
                    }
                }
                if (Count > 0)
                    UsesSystemAllocator = !UsesDynamicHeapAllocator;
            }

            public void Dispose()
            {
                Count = 0;
                MemoryRegionName = null;
                ParentIndex.Dispose();
                AddressBase.Dispose();
                AddressSize.Dispose();
                FirstAllocationIndex.Dispose();
                NumAllocations.Dispose();
            }
        }

        public class NativeMemoryLabelEntriesCache : IDisposable
        {
            public long Count;
            public string[] MemoryLabelName;
            public DynamicArray<ulong> MemoryLabelSizes = default;

            public NativeMemoryLabelEntriesCache(ref FileReader reader, bool hasLabelSizes)
            {
                Count = reader.GetEntryCount(EntryType.NativeMemoryLabels_Name);
                MemoryLabelName = new string[Count];

                if (Count == 0)
                    return;

                if (hasLabelSizes)
                    MemoryLabelSizes = reader.Read(EntryType.NativeMemoryLabels_Size, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();

                using (DynamicArray<byte> tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.NativeMemoryLabels_Name, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.NativeMemoryLabels_Name, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref MemoryLabelName);
                }
            }

            public void Dispose()
            {
                Count = 0;
                MemoryLabelSizes.Dispose();
                MemoryLabelName = null;
            }
        }

        public class NativeCallstackSymbolEntriesCache : IDisposable
        {
            public long Count;
            public DynamicArray<ulong> Symbol = default;
            public string[] ReadableStackTrace;

            public NativeCallstackSymbolEntriesCache(ref FileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.NativeCallstackSymbol_Symbol);
                ReadableStackTrace = new string[Count];

                if (Count == 0)
                    return;

                Symbol = reader.Read(EntryType.NativeCallstackSymbol_Symbol, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                using (DynamicArray<byte> tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.NativeCallstackSymbol_ReadableStackTrace, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.NativeCallstackSymbol_ReadableStackTrace, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref ReadableStackTrace);
                }
            }

            public void Dispose()
            {
                Count = 0;
                Symbol.Dispose();
                ReadableStackTrace = null;
            }
        }

        public class NativeAllocationEntriesCache : IDisposable
        {
            public long Count;
            public DynamicArray<int> MemoryRegionIndex = default;
            public DynamicArray<long> RootReferenceId = default;
            public DynamicArray<ulong> Address = default;
            public DynamicArray<ulong> Size = default;
            public DynamicArray<int> OverheadSize = default;
            public DynamicArray<int> PaddingSize = default;
            public DynamicArray<long> AllocationSiteId = default;

            public NativeAllocationEntriesCache(ref FileReader reader, bool allocationSites /*do not read allocation sites if they aren't present*/)
            {
                Count = reader.GetEntryCount(EntryType.NativeAllocations_Address);

                if (Count == 0)
                    return;

                MemoryRegionIndex = reader.Read(EntryType.NativeAllocations_MemoryRegionIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                RootReferenceId = reader.Read(EntryType.NativeAllocations_RootReferenceId, 0, Count, Allocator.Persistent).Result.Reinterpret<long>();
                Address = reader.Read(EntryType.NativeAllocations_Address, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                Size = reader.Read(EntryType.NativeAllocations_Size, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                OverheadSize = reader.Read(EntryType.NativeAllocations_OverheadSize, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                PaddingSize = reader.Read(EntryType.NativeAllocations_PaddingSize, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();

                if (allocationSites)
                    AllocationSiteId = reader.Read(EntryType.NativeAllocations_AllocationSiteId, 0, Count, Allocator.Persistent).Result.Reinterpret<long>();
            }

            public void Dispose()
            {
                Count = 0;
                MemoryRegionIndex.Dispose();
                RootReferenceId.Dispose();
                Address.Dispose();
                Size.Dispose();
                OverheadSize.Dispose();
                PaddingSize.Dispose();
                AllocationSiteId.Dispose();
            }
        }

        public unsafe class NativeTypeEntriesCache : IDisposable
        {
            public long Count;
            public string[] TypeName;
            public DynamicArray<int> NativeBaseTypeArrayIndex = default;

            public NativeTypeEntriesCache(ref FileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.NativeTypes_Name);
                TypeName = new string[Count];

                if (Count == 0)
                    return;

                NativeBaseTypeArrayIndex = reader.Read(EntryType.NativeTypes_NativeBaseTypeArrayIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                using (DynamicArray<byte> tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.NativeTypes_Name, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.NativeTypes_Name, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref TypeName);
                }
            }

            public void Dispose()
            {
                Count = 0;
                NativeBaseTypeArrayIndex.Dispose();
                TypeName = null;
            }
        }

        public class NativeObjectEntriesCache : IDisposable
        {
            public const int InstanceIDNone = 0;

            public long Count;
            public string[] ObjectName;
            public DynamicArray<int> InstanceId = default;
            public DynamicArray<ulong> Size = default;
            public DynamicArray<int> NativeTypeArrayIndex = default;
            public DynamicArray<HideFlags> HideFlags = default;
            public DynamicArray<ObjectFlags> Flags = default;
            public DynamicArray<ulong> NativeObjectAddress = default;
            public DynamicArray<long> RootReferenceId = default;
            public DynamicArray<int> ManagedObjectIndex = default;

            //scondary data
            public DynamicArray<int> refcount = default;
            public Dictionary<ulong, int> nativeObjectAddressToInstanceId { private set; get; }
            public SortedDictionary<int, int> instanceId2Index;

            public readonly ulong TotalSizes = 0ul;

            unsafe public NativeObjectEntriesCache(ref FileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.NativeObjects_InstanceId);
                nativeObjectAddressToInstanceId = new Dictionary<ulong, int>((int)Count);
                instanceId2Index = new SortedDictionary<int, int>();
                ObjectName = new string[Count];

                if (Count == 0)
                    return;

                InstanceId = reader.Read(EntryType.NativeObjects_InstanceId, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                Size = reader.Read(EntryType.NativeObjects_Size, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                NativeTypeArrayIndex = reader.Read(EntryType.NativeObjects_NativeTypeArrayIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                HideFlags = reader.Read(EntryType.NativeObjects_HideFlags, 0, Count, Allocator.Persistent).Result.Reinterpret<HideFlags>();
                Flags = reader.Read(EntryType.NativeObjects_Flags, 0, Count, Allocator.Persistent).Result.Reinterpret<ObjectFlags>();
                NativeObjectAddress = reader.Read(EntryType.NativeObjects_NativeObjectAddress, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                RootReferenceId = reader.Read(EntryType.NativeObjects_RootReferenceId, 0, Count, Allocator.Persistent).Result.Reinterpret<long>();
                ManagedObjectIndex = reader.Read(EntryType.NativeObjects_GCHandleIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                refcount = new DynamicArray<int>(Count, Allocator.Persistent, true);

                using (var tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.NativeObjects_Name, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.NativeObjects_Name, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref ObjectName);
                }

                for (long i = 0; i < NativeObjectAddress.Count; ++i)
                {
                    var id = InstanceId[i];
                    nativeObjectAddressToInstanceId.Add(NativeObjectAddress[i], id);
                    instanceId2Index[id] = (int)i;
                    TotalSizes += Size[i];
                }

                //fallback for the legacy snapshot formats
                //create the managedObjectIndex array and make it -1 on each entry so they can be overridden during crawling
                //TODO: remove this when the new crawler lands :-/
                if (reader.FormatVersion < FormatVersion.NativeConnectionsAsInstanceIdsVersion)
                {
                    ManagedObjectIndex.Dispose();
                    ManagedObjectIndex = new DynamicArray<int>(Count, Allocator.Persistent);
                    for (int i = 0; i < Count; ++i)
                        ManagedObjectIndex[i] = -1;
                }
            }

            public void Dispose()
            {
                Count = 0;
                InstanceId.Dispose();
                Size.Dispose();
                NativeTypeArrayIndex.Dispose();
                HideFlags.Dispose();
                Flags.Dispose();
                NativeObjectAddress.Dispose();
                RootReferenceId.Dispose();
                ManagedObjectIndex.Dispose();
                refcount.Dispose();
                ObjectName = null;
                nativeObjectAddressToInstanceId = null;
                instanceId2Index = null;
            }
        }

        public enum MemorySectionType : byte
        {
            GarbageCollector,
            VirtualMachine
        }

        //TODO: Add on demand load of sections, and unused chunks unload
        public class ManagedMemorySectionEntriesCache : IDisposable
        {
            ProfilerMarker CacheFind = new ProfilerMarker("ManagedMemorySectionEntriesCache.Find");
            public long Count;
            public DynamicArray<ulong> StartAddress = default;
            public DynamicArray<ulong> SectionSize = default;
            public DynamicArray<MemorySectionType> SectionType = default;
            public string[] SectionName = default;
            public byte[][] Bytes;
            ulong m_MinAddress;
            ulong m_MaxAddress;
            const ulong k_ReferenceBit = 1UL << 63;

            static readonly string k_VMSection = UnityEditor.L10n.Tr("Virtual Machine Memory Section");
            static readonly string k_GCSection = UnityEditor.L10n.Tr("Managed Heap Section");
            static readonly string k_ActiveGCSection = UnityEditor.L10n.Tr("Active Managed Heap Section");
            static readonly string k_StackSection = UnityEditor.L10n.Tr("Managed Stack Section");
            static readonly string k_ManagedMemorySection = UnityEditor.L10n.Tr("Managed Memory Section (unclear if Heap or Virtual Machine memory, please update Unity)");

            public readonly ulong VirtualMachineMemoryReserved = 0;
            // if the snapshot format is missing the VM section bit, this number will include VM memory
            public readonly ulong ManagedHeapMemoryReserved = 0;
            public readonly ulong TotalActiveManagedHeapSectionReserved = 0;
            public readonly ulong StackMemoryReserved = 0;

            public readonly long FirstAssumedActiveHeapSectionIndex = 0;
            public readonly long LastAssumedActiveHeapSectionIndex = 0;

            public ManagedMemorySectionEntriesCache(ref FileReader reader, bool HasGCHeapTypes, bool readStackMemory)
            {
                Count = reader.GetEntryCount(readStackMemory ? EntryType.ManagedStacks_StartAddress : EntryType.ManagedHeapSections_StartAddress);
                Bytes = new byte[Count][];
                m_MinAddress = m_MaxAddress = 0;

                if (Count == 0)
                    return;

                SectionType = new DynamicArray<MemorySectionType>(Count, Allocator.Persistent, true);
                SectionName = new string[Count];
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
                        // numbering the sections could be confusing as people might expect the numbers to stay comparable over time,
                        // but if one section is unloaded or merged/split in a following snapshot, people might confuse them as the same one
                        // also, grouping the columns by name doesn't work nicely then either so, only number them for debugging purposes
                        // bonus: waaaay less string memory usage and no GC.Allocs for these!
                        if (isVMSection)
                            SectionName[i] = k_VMSection;//"Managed Virtual Machine Memory Section " + vmSectionIndex++;
                        else
                            SectionName[i] = k_GCSection;//"Managed Heap Section " + heapSectionIndex++;
                    }
                }
                else
                {
                    for (long i = 0; i < StartAddress.Count; ++i)
                    {
                        SectionName[i] = k_ManagedMemorySection;
                    }
                }
                if (readStackMemory)
                {
                    for (long i = 0; i < Count; ++i)
                    {
                        SectionName[i] = k_StackSection;//"Managed Stack Section " + i;
                    }
                }

                //use Persistent instead of TempJob so we don't bust the allocator to bits
                using (DynamicArray<byte> tmp = new DynamicArray<byte>(0, Allocator.Persistent))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.ManagedHeapSections_Bytes, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(readStackMemory ? EntryType.ManagedStacks_Bytes : EntryType.ManagedHeapSections_Bytes, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref Bytes);
                }
                SectionSize = new DynamicArray<ulong>(Count, Allocator.Persistent);
                SortSectionEntries(ref StartAddress, ref SectionSize, ref SectionType, ref SectionName, ref Bytes, readStackMemory);
                m_MinAddress = StartAddress[0];
                m_MaxAddress = StartAddress[Count - 1] + (ulong)Bytes[Count - 1].LongLength;

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
                            else if (!foundFirstAssumedActiveHeap && StartAddress[i] + SectionSize[i] + VMTools.x64ArchPtrSize > StartAddress[FirstAssumedActiveHeapSectionIndex])
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
                        SectionName[i] = k_ActiveGCSection;
                    }
                }
                TotalActiveManagedHeapSectionReserved = StartAddress[LastAssumedActiveHeapSectionIndex] + SectionSize[LastAssumedActiveHeapSectionIndex] - StartAddress[FirstAssumedActiveHeapSectionIndex];
            }

            public BytesAndOffset Find(ulong address, VirtualMachineInformation virtualMachineInformation)
            {
                using (CacheFind.Auto())
                {
                    var bytesAndOffset = new BytesAndOffset();

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

                        if (address >= StartAddress[idx] && address < (StartAddress[idx] + (ulong)Bytes[idx].Length))
                        {
                            bytesAndOffset.bytes = Bytes[idx];
                            bytesAndOffset.offset = (int)(address - StartAddress[idx]);
                            bytesAndOffset.pointerSize = virtualMachineInformation.PointerSize;
                        }
                    }

                    return bytesAndOffset;
                }
            }

            static void SortSectionEntries(ref DynamicArray<ulong> startAddresses, ref DynamicArray<ulong> sizes, ref DynamicArray<MemorySectionType> associatedSectionType, ref string[] associatedSectionNames,
                ref byte[][] associatedByteArrays, bool isStackMemory)
            {
                var sortMapping = new int[startAddresses.Count];

                for (int i = 0; i < sortMapping.Length; ++i)
                    sortMapping[i] = i;

                var startAddr = startAddresses;
                Array.Sort(sortMapping, (x, y) => startAddr[x].CompareTo(startAddr[y]));

                var newSortedAddresses = new ulong[startAddresses.Count];
                var newSortedByteArrays = new byte[startAddresses.Count][];
                var newSortedSectionTypes = isStackMemory ? null : new MemorySectionType[startAddresses.Count];
                var newSortedSectionNames = new string[startAddresses.Count];

                for (long i = 0; i < startAddresses.Count; ++i)
                {
                    long idx = sortMapping[i];
                    newSortedAddresses[i] = startAddresses[idx];
                    newSortedByteArrays[i] = associatedByteArrays[idx];
                    newSortedSectionNames[i] = associatedSectionNames[idx];

                    if (!isStackMemory)
                        newSortedSectionTypes[i] = associatedSectionType[idx];
                }

                for (long i = 0; i < startAddresses.Count; ++i)
                {
                    startAddresses[i] = newSortedAddresses[i];
                    sizes[i] = (ulong)newSortedByteArrays[i].LongLength;
                    if (!isStackMemory)
                        associatedSectionType[i] = newSortedSectionTypes[i];
                }
                associatedByteArrays = newSortedByteArrays;
                associatedSectionNames = newSortedSectionNames;
            }

            public void Dispose()
            {
                Count = 0;
                m_MinAddress = m_MaxAddress = 0;
                StartAddress.Dispose();
                SectionType.Dispose();
                SectionSize.Dispose();
                Bytes = null;
            }
        }

        //leave this as second to last thing to convert, also a major pain in the ass
        public class TypeDescriptionEntriesCache : IDisposable
        {
            public const int ITypeInvalid = -1;
            const int k_DefaultFieldProcessingBufferSize = 64;
            const string k_SystemStringTypeName = "System.String";
            const string k_SystemObjectTypeName = "System.Object";
            const string k_SystemValueTypeName = "System.ValueType";
            const string k_SystemEnumTypeName = "System.Enum";


            public long Count;
            public DynamicArray<TypeFlags> Flags = default;
            public DynamicArray<int> BaseOrElementTypeIndex = default;
            public DynamicArray<int> Size = default;
            public DynamicArray<ulong> TypeInfoAddress = default;
            public DynamicArray<int> TypeIndex = default;

            public string[] TypeDescriptionName;
            public string[] Assembly;
            public int[][] FieldIndices;
            public byte[][] StaticFieldBytes;

            //secondary data, handled inside InitSecondaryItems
            public int[][] FieldIndicesInstance;//includes all bases' instance fields
            public int[][] fieldIndicesStatic;  //includes all bases' static fields
            public int[][] fieldIndicesOwnedStatic;  //includes only type's static fields
            public bool[] HasStaticFields;
            public int ITypeValueType;
            public int ITypeObject;
            public int ITypeEnum;
            public int ITypeString;
            public Dictionary<ulong, int> TypeInfoToArrayIndex;
            public Dictionary<int, int> TypeIndexToArrayIndex;


            public TypeDescriptionEntriesCache(ref FileReader reader, FieldDescriptionEntriesCache fieldDescriptions)
            {
                Count = reader.GetEntryCount(EntryType.TypeDescriptions_TypeIndex);
                TypeDescriptionName = new string[Count];
                Assembly = new string[Count];
                FieldIndices = new int[Count][];
                StaticFieldBytes = new byte[Count][];

                if (Count == 0)
                    return;

                Flags = reader.Read(EntryType.TypeDescriptions_Flags, 0, Count, Allocator.Persistent).Result.Reinterpret<TypeFlags>();
                BaseOrElementTypeIndex = reader.Read(EntryType.TypeDescriptions_BaseOrElementTypeIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                Size = reader.Read(EntryType.TypeDescriptions_Size, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                TypeInfoAddress = reader.Read(EntryType.TypeDescriptions_TypeInfoAddress, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                TypeIndex = reader.Read(EntryType.TypeDescriptions_TypeIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();

                using (DynamicArray<byte> tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.TypeDescriptions_Name, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.TypeDescriptions_Name, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref TypeDescriptionName);

                    tmpSize = reader.GetSizeForEntryRange(EntryType.TypeDescriptions_Assembly, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.TypeDescriptions_Assembly, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref Assembly);

                    tmpSize = reader.GetSizeForEntryRange(EntryType.TypeDescriptions_FieldIndices, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.TypeDescriptions_FieldIndices, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref FieldIndices);

                    tmpSize = reader.GetSizeForEntryRange(EntryType.TypeDescriptions_StaticFieldBytes, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.TypeDescriptions_StaticFieldBytes, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref StaticFieldBytes);
                }

                //change to consume field descriptions instead
                InitSecondaryItems(this, fieldDescriptions);
            }

            // Check all bases' fields
            public bool HasAnyField(int iType)
            {
                return FieldIndicesInstance[iType].Length > 0 || fieldIndicesStatic[iType].Length > 0;
            }

            // Check all bases' fields
            public bool HasAnyStaticField(int iType)
            {
                return fieldIndicesStatic[iType].Length > 0;
            }

            // Check only the type's fields
            public bool HasStaticField(int iType)
            {
                return HasStaticFields[iType];
            }

            public bool HasFlag(int arrayIndex, TypeFlags flag)
            {
                return (Flags[arrayIndex] & flag) == flag;
            }

            public int GetRank(int arrayIndex)
            {
                int r = (int)(Flags[arrayIndex] & TypeFlags.kArrayRankMask) >> 16;
                return r;
            }

            public int TypeIndex2ArrayIndex(int typeIndex)
            {
                int i;
                if (!TypeIndexToArrayIndex.TryGetValue(typeIndex, out i))
                {
                    throw new Exception("typeIndex not found");
                }
                return i;
            }

            public int TypeInfo2ArrayIndex(UInt64 aTypeInfoAddress)
            {
                int i;

                if (!TypeInfoToArrayIndex.TryGetValue(aTypeInfoAddress, out i))
                {
                    return -1;
                }
                return i;
            }

            static ProfilerMarker typeFieldArraysBuild = new ProfilerMarker("MemoryProfiler.TypeFields.TypeFieldArrayBuilding");
            void InitSecondaryItems(TypeDescriptionEntriesCache typeDescriptionEntries, FieldDescriptionEntriesCache fieldDescriptions)
            {
                TypeInfoToArrayIndex = Enumerable.Range(0, (int)TypeInfoAddress.Count).ToDictionary(x => TypeInfoAddress[x], x => x);
                TypeIndexToArrayIndex = Enumerable.Range(0, (int)TypeIndex.Count).ToDictionary(x => TypeIndex[x], x => x);

                using (typeFieldArraysBuild.Auto())
                {
                    HasStaticFields = new bool[Count];
                    FieldIndicesInstance = new int[Count][];
                    fieldIndicesStatic = new int[Count][];
                    fieldIndicesOwnedStatic = new int[Count][];
                    List<int> fieldProcessingBuffer = new List<int>(k_DefaultFieldProcessingBufferSize);

                    for (int i = 0; i < Count; ++i)
                    {
                        HasStaticFields[i] = false;
                        foreach (var iField in FieldIndices[i])
                        {
                            if (fieldDescriptions.IsStatic[iField] == 1)
                            {
                                HasStaticFields[i] = true;
                                break;
                            }
                        }

                        TypeTools.AllFieldArrayIndexOf(ref fieldProcessingBuffer, i, typeDescriptionEntries, fieldDescriptions, TypeTools.FieldFindOptions.OnlyInstance, true);
                        FieldIndicesInstance[i] = fieldProcessingBuffer.ToArray();

                        TypeTools.AllFieldArrayIndexOf(ref fieldProcessingBuffer, i, typeDescriptionEntries, fieldDescriptions, TypeTools.FieldFindOptions.OnlyStatic, true);
                        fieldIndicesStatic[i] = fieldProcessingBuffer.ToArray();

                        TypeTools.AllFieldArrayIndexOf(ref fieldProcessingBuffer, i, typeDescriptionEntries, fieldDescriptions, TypeTools.FieldFindOptions.OnlyStatic, false);
                        fieldIndicesOwnedStatic[i] = fieldProcessingBuffer.ToArray();
                    }
                }

                ITypeValueType = Array.FindIndex(TypeDescriptionName, x => x == k_SystemValueTypeName);
                ITypeObject = Array.FindIndex(TypeDescriptionName, x => x == k_SystemObjectTypeName);
                ITypeEnum = Array.FindIndex(TypeDescriptionName, x => x == k_SystemEnumTypeName);
                ITypeString = Array.FindIndex(TypeDescriptionName, x => x == k_SystemObjectTypeName);
            }

            public void Dispose()
            {
                Count = 0;
                Flags.Dispose();
                BaseOrElementTypeIndex.Dispose();
                Size.Dispose();
                TypeInfoAddress.Dispose();
                TypeIndex.Dispose();
                TypeDescriptionName = null;
                Assembly = null;
                FieldIndices = null;
                StaticFieldBytes = null;

                FieldIndicesInstance = null;
                fieldIndicesStatic = null;
                fieldIndicesOwnedStatic = null;
                HasStaticFields = null;
                ITypeValueType = ITypeInvalid;
                ITypeObject = ITypeInvalid;
                ITypeEnum = ITypeInvalid;
                TypeInfoToArrayIndex = null;
                TypeIndexToArrayIndex = null;
            }
        }

        public class FieldDescriptionEntriesCache : IDisposable
        {
            public long Count;
            public string[] FieldDescriptionName;
            public DynamicArray<int> Offset = default;
            public DynamicArray<int> TypeIndex = default;
            public DynamicArray<byte> IsStatic = default;

            unsafe public FieldDescriptionEntriesCache(ref FileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.FieldDescriptions_Name);
                FieldDescriptionName = new string[Count];

                if (Count == 0)
                    return;

                Offset = new DynamicArray<int>(Count, Allocator.Persistent);
                TypeIndex = new DynamicArray<int>(Count, Allocator.Persistent);
                IsStatic = new DynamicArray<byte>(Count, Allocator.Persistent);

                Offset = reader.Read(EntryType.FieldDescriptions_Offset, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                TypeIndex = reader.Read(EntryType.FieldDescriptions_TypeIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                IsStatic = reader.Read(EntryType.FieldDescriptions_IsStatic, 0, Count, Allocator.Persistent).Result.Reinterpret<byte>();

                using (var tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpBufferSize = reader.GetSizeForEntryRange(EntryType.FieldDescriptions_Name, 0, Count);
                    tmp.Resize(tmpBufferSize, false);
                    reader.Read(EntryType.FieldDescriptions_Name, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref FieldDescriptionName);
                }
            }

            public void Dispose()
            {
                Count = 0;
                Offset.Dispose();
                TypeIndex.Dispose();
                IsStatic.Dispose();
                FieldDescriptionName = null;
            }
        }

        public class GCHandleEntriesCache : IDisposable
        {
            public DynamicArray<ulong> Target = default;
            public long Count;

            public GCHandleEntriesCache(ref FileReader reader)
            {
                unsafe
                {
                    Count = reader.GetEntryCount(EntryType.GCHandles_Target);
                    if (Count == 0)
                        return;

                    Target = reader.Read(EntryType.GCHandles_Target, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                }
            }

            public void Dispose()
            {
                Count = 0;
                Target.Dispose();
            }
        }

        public class ConnectionEntriesCache : IDisposable
        {
            public long Count;
            public DynamicArray<int> From { private set; get; }
            public DynamicArray<int> To { private set; get; }
#if DEBUG_VALIDATION // could be always present but currently only used for validation in the crawler
            public long IndexOfFirstNativeToGCHandleConnection = -1;
#endif

            unsafe public ConnectionEntriesCache(ref FileReader reader, NativeObjectEntriesCache nativeObjects, long gcHandlesCount, bool connectionsNeedRemaping)
            {
                Count = reader.GetEntryCount(EntryType.Connections_From);
                From = new DynamicArray<int>(Count, Allocator.Persistent);
                To = new DynamicArray<int>(Count, Allocator.Persistent);

                if (Count == 0)
                    return;

                From = reader.Read(EntryType.Connections_From, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                To = reader.Read(EntryType.Connections_To, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();

                if (connectionsNeedRemaping)
                {
                    var instanceIds = nativeObjects.InstanceId;
                    var gchandlesIndices = nativeObjects.ManagedObjectIndex;

                    Dictionary<int, int> instanceIDToIndex = new Dictionary<int, int>();
                    Dictionary<int, int> instanceIDToGcHandleIndex = new Dictionary<int, int>();

                    for (int i = 0; i < instanceIds.Count; ++i)
                    {
                        if (gchandlesIndices[i] != -1)
                        {
                            instanceIDToGcHandleIndex.Add(instanceIds[i], gchandlesIndices[i]);
                        }
                        instanceIDToIndex.Add(instanceIds[i], i);
                    }
#if DEBUG_VALIDATION
                    if (instanceIDToGcHandleIndex.Count > 0)
                        IndexOfFirstNativeToGCHandleConnection = Count;
#endif

                    DynamicArray<int> fromRemap = new DynamicArray<int>(Count + instanceIDToGcHandleIndex.Count, Allocator.Persistent);
                    DynamicArray<int> toRemap = new DynamicArray<int>(fromRemap.Count, Allocator.Persistent);

                    // add all Native to Native connections.
                    // The indexes they link to are all bigger than gcHandlesCount.
                    // Such indices in the From/To arrays indicate a link from a native object to a native object
                    // and subtracting gcHandlesCount from them gives the Native Object index
                    for (long i = 0; i < Count; ++i)
                    {
                        fromRemap[i] = (int)(gcHandlesCount + instanceIDToIndex[From[i]]);
                        toRemap[i] = (int)(gcHandlesCount + instanceIDToIndex[To[i]]);
                    }

                    //dispose of original data
                    To.Dispose();
                    From.Dispose();

                    var enumerator = instanceIDToGcHandleIndex.GetEnumerator();
                    for (long i = Count; i < fromRemap.Count; ++i)
                    {
                        enumerator.MoveNext();
                        fromRemap[i] = (int)(gcHandlesCount + instanceIDToIndex[enumerator.Current.Key]);
                        // elements in To that are `To[i] < gcHandlesCount` are indexes into the GCHandles list
                        toRemap[i] = enumerator.Current.Value;
                    }

                    From = fromRemap;
                    To = toRemap;
                    Count = From.Count;
                }
            }

            public void Dispose()
            {
                Count = 0;
                From.Dispose();
                To.Dispose();
            }
        }

        FileReader m_Reader;
        public MetaData MetaData { get; private set; }
        public DateTime TimeStamp { get; private set; }
        public VirtualMachineInformation VirtualMachineInformation { get; private set; }
        public NativeAllocationSiteEntriesCache NativeAllocationSites;
        public TypeDescriptionEntriesCache TypeDescriptions;
        public NativeTypeEntriesCache NativeTypes;
        public NativeRootReferenceEntriesCache NativeRootReferences;
        public NativeObjectEntriesCache NativeObjects;
        public NativeMemoryRegionEntriesCache NativeMemoryRegions;
        public NativeMemoryLabelEntriesCache NativeMemoryLabels;
        public NativeCallstackSymbolEntriesCache NativeCallstackSymbols;
        public NativeAllocationEntriesCache NativeAllocations;
        public ManagedMemorySectionEntriesCache ManagedStacks;
        public ManagedMemorySectionEntriesCache ManagedHeapSections;
        public GCHandleEntriesCache GcHandles;
        public FieldDescriptionEntriesCache FieldDescriptions;
        public ConnectionEntriesCache Connections;
        public CaptureFlags CaptureFlags = 0;

        public SortedNativeMemoryRegionEntriesCache SortedNativeRegionsEntries;
        public SortedManagedMemorySectionEntriesCache SortedManagedStacksEntries;
        public SortedManagedMemorySectionEntriesCache SortedManagedHeapEntries;
        public SortedManagedObjectsCache SortedManagedObjects;
        public SortedNativeAllocationsCache SortedNativeAllocations;
        public SortedNativeObjectsCache SortedNativeObjects;

        public CachedSnapshot(FileReader reader)
        {
            unsafe
            {
                VirtualMachineInformation vmInfo;
                reader.ReadUnsafe(EntryType.Metadata_VirtualMachineInformation, &vmInfo, UnsafeUtility.SizeOf<VirtualMachineInformation>(), 0, 1);

                if (!VMTools.ValidateVirtualMachineInfo(vmInfo))
                {
                    throw new UnityException("Invalid VM info. Snapshot file is corrupted.");
                }

                m_Reader = reader;
                long ticks;
                reader.ReadUnsafe(EntryType.Metadata_RecordDate, &ticks, UnsafeUtility.SizeOf<long>(), 0, 1);
                TimeStamp = new DateTime(ticks);

                VirtualMachineInformation = vmInfo;
                m_SnapshotVersion = reader.FormatVersion;

                MetaData = new MetaData(reader);

                NativeAllocationSites = new NativeAllocationSiteEntriesCache(ref reader);
                FieldDescriptions = new FieldDescriptionEntriesCache(ref reader);
                TypeDescriptions = new TypeDescriptionEntriesCache(ref reader, FieldDescriptions);
                NativeTypes = new NativeTypeEntriesCache(ref reader);
                NativeRootReferences = new NativeRootReferenceEntriesCache(ref reader);
                NativeObjects = new NativeObjectEntriesCache(ref reader);
                NativeMemoryRegions = new NativeMemoryRegionEntriesCache(ref reader);
                NativeMemoryLabels = new NativeMemoryLabelEntriesCache(ref reader, HasMemoryLabelSizesAndGCHeapTypes);
                NativeCallstackSymbols = new NativeCallstackSymbolEntriesCache(ref reader);
                NativeAllocations = new NativeAllocationEntriesCache(ref reader, NativeAllocationSites.Count != 0);
                ManagedStacks = new ManagedMemorySectionEntriesCache(ref reader, false, true);
                ManagedHeapSections = new ManagedMemorySectionEntriesCache(ref reader, HasMemoryLabelSizesAndGCHeapTypes, false);
                GcHandles = new GCHandleEntriesCache(ref reader);
                Connections = new ConnectionEntriesCache(ref reader, NativeObjects, GcHandles.Count, HasConnectionOverhaul);

                if (GcHandles.Count > 0)
                    CaptureFlags |= CaptureFlags.ManagedObjects;
                if (NativeAllocations.Count > 0)
                    CaptureFlags |= CaptureFlags.NativeAllocations;
                if (NativeAllocationSites.Count > 0)
                    CaptureFlags |= CaptureFlags.NativeAllocationSites;
                if (NativeObjects.Count > 0)
                    CaptureFlags |= CaptureFlags.NativeObjects;
                if (NativeCallstackSymbols.Count > 0)
                    CaptureFlags |= CaptureFlags.NativeStackTraces;

                SortedManagedStacksEntries = new SortedManagedMemorySectionEntriesCache(ManagedStacks);
                SortedManagedHeapEntries = new SortedManagedMemorySectionEntriesCache(ManagedHeapSections);
                SortedManagedObjects = new SortedManagedObjectsCache(this);

                SortedNativeRegionsEntries = new SortedNativeMemoryRegionEntriesCache(this);
                SortedNativeAllocations = new SortedNativeAllocationsCache(this);
                SortedNativeObjects = new SortedNativeObjectsCache(this);

                CrawledData = new ManagedData(GcHandles.Count, Connections.Count);
            }
        }

        //Unified Object index are in that order: gcHandle, native object, crawled objects
        public int ManagedObjectIndexToUnifiedObjectIndex(int i)
        {
            if (i < 0) return -1;
            if (i < GcHandles.Count) return i;
            if (i < CrawledData.ManagedObjects.Count) return i + (int)NativeObjects.Count;
            return -1;
        }

        public int NativeObjectIndexToUnifiedObjectIndex(int i)
        {
            if (i < 0) return -1;
            if (i < NativeObjects.Count) return i + (int)GcHandles.Count;
            return -1;
        }

        public int UnifiedObjectIndexToManagedObjectIndex(int i)
        {
            if (i < 0) return -1;
            if (i < GcHandles.Count) return i;
            int firstCrawled = (int)(GcHandles.Count + NativeObjects.Count);
            int lastCrawled = (int)(NativeObjects.Count + CrawledData.ManagedObjects.Count);
            if (i >= firstCrawled && i < lastCrawled) return i - (int)NativeObjects.Count;
            return -1;
        }

        public int UnifiedObjectIndexToNativeObjectIndex(int i)
        {
            if (i < GcHandles.Count) return -1;
            int firstCrawled = (int)(GcHandles.Count + NativeObjects.Count);
            if (i < firstCrawled) return i - (int)GcHandles.Count;
            return -1;
        }

        public void Dispose()
        {
            NativeAllocationSites.Dispose();
            TypeDescriptions.Dispose();
            NativeTypes.Dispose();
            NativeRootReferences.Dispose();
            NativeObjects.Dispose();
            NativeMemoryRegions.Dispose();
            NativeMemoryLabels.Dispose();
            NativeCallstackSymbols.Dispose();
            NativeAllocations.Dispose();
            ManagedStacks.Dispose();
            ManagedHeapSections.Dispose();
            GcHandles.Dispose();
            FieldDescriptions.Dispose();
            Connections.Dispose();
        }

        public interface ISortedEntriesCache
        {
            void Preload();
            int Count { get; }
            ulong Address(int index);
            ulong Size(int index);
        }

        public class SortedNativeMemoryRegionEntriesCache : ISortedEntriesCache
        {
            CachedSnapshot m_Snapshot;
            int[] m_Sorting;

            public SortedNativeMemoryRegionEntriesCache(CachedSnapshot snapshot)
            {
                m_Snapshot = snapshot;
            }

            public void Preload()
            {
                if (m_Sorting == null)
                {
                    m_Sorting = new int[m_Snapshot.NativeMemoryRegions.Count];

                    for (int i = 0; i < m_Sorting.Length; ++i)
                        m_Sorting[i] = i;

                    Array.Sort(m_Sorting, (x, y) => m_Snapshot.NativeMemoryRegions.AddressBase[x].CompareTo(m_Snapshot.NativeMemoryRegions.AddressBase[y]));
                }
            }

            int this[int index]
            {
                get
                {
                    Preload();
                    return m_Sorting[index];
                }
            }

            public int  Count { get { return (int)m_Snapshot.NativeMemoryRegions.Count; } }
            public ulong  Address(int index) { return m_Snapshot.NativeMemoryRegions.AddressBase[this[index]]; }
            public ulong  Size(int index) { return m_Snapshot.NativeMemoryRegions.AddressSize[this[index]]; }

            public string Name(int index) { return m_Snapshot.NativeMemoryRegions.MemoryRegionName[this[index]]; }
            public int    UnsortedParentRegionIndex(int index) { return m_Snapshot.NativeMemoryRegions.ParentIndex[this[index]]; }
            public int    UnsortedFirstAllocationIndex(int index) { return m_Snapshot.NativeMemoryRegions.FirstAllocationIndex[this[index]]; }
            public int    UnsortedNumAllocations(int index) { return m_Snapshot.NativeMemoryRegions.NumAllocations[this[index]]; }
        }

        //TODO: unify with the other old section entries as those are sorted by default now
        public class SortedManagedMemorySectionEntriesCache : ISortedEntriesCache
        {
            ManagedMemorySectionEntriesCache m_Entries;

            public SortedManagedMemorySectionEntriesCache(ManagedMemorySectionEntriesCache entries)
            {
                m_Entries = entries;
            }

            public void Preload()
            {
                //Dummy for the interface
            }

            public int  Count { get { return (int)m_Entries.Count; } }
            public ulong Address(int index) { return m_Entries.StartAddress[index]; }
            public ulong Size(int index) { return (ulong)m_Entries.Bytes[index].Length; }
            public byte[] Bytes(int index) { return m_Entries.Bytes[index]; }
        }

        public class SortedManagedObjectsCache : ISortedEntriesCache
        {
            CachedSnapshot m_Snapshot;
            int[] m_Sorting;

            public SortedManagedObjectsCache(CachedSnapshot snapshot)
            {
                m_Snapshot = snapshot;
            }

            public void Preload()
            {
                if (m_Sorting == null)
                {
                    m_Sorting = new int[m_Snapshot.CrawledData.ManagedObjects.Count];

                    for (int i = 0; i < m_Sorting.Length; ++i)
                        m_Sorting[i] = i;

                    Array.Sort(m_Sorting, (x, y) => m_Snapshot.CrawledData.ManagedObjects[x].PtrObject.CompareTo(m_Snapshot.CrawledData.ManagedObjects[y].PtrObject));
                }
            }

            ManagedObjectInfo this[int index]
            {
                get
                {
                    Preload();
                    return m_Snapshot.CrawledData.ManagedObjects[m_Sorting[index]];
                }
            }

            public int  Count { get { return (int)m_Snapshot.CrawledData.ManagedObjects.Count; } }

            public ulong Address(int index) { return this[index].PtrObject; }
            public ulong Size(int index) { return (ulong)this[index].Size; }
        }

        public class SortedNativeAllocationsCache : ISortedEntriesCache
        {
            CachedSnapshot m_Snapshot;
            int[] m_Sorting;

            public SortedNativeAllocationsCache(CachedSnapshot snapshot)
            {
                m_Snapshot = snapshot;
            }

            public void Preload()
            {
                if (m_Sorting == null)
                {
                    m_Sorting = new int[m_Snapshot.NativeAllocations.Address.Count];

                    for (int i = 0; i < m_Sorting.Length; ++i)
                        m_Sorting[i] = i;

                    Array.Sort(m_Sorting, (x, y) => m_Snapshot.NativeAllocations.Address[x].CompareTo(m_Snapshot.NativeAllocations.Address[y]));
                }
            }

            int this[int index]
            {
                get
                {
                    Preload();
                    return m_Sorting[index];
                }
            }

            public int  Count { get { return (int)m_Snapshot.NativeAllocations.Count; } }
            public ulong Address(int index) { return m_Snapshot.NativeAllocations.Address[this[index]]; }
            public ulong Size(int index) { return m_Snapshot.NativeAllocations.Size[this[index]]; }
            public int MemoryRegionIndex(int index) { return m_Snapshot.NativeAllocations.MemoryRegionIndex[this[index]]; }
            public long RootReferenceId(int index) { return m_Snapshot.NativeAllocations.RootReferenceId[this[index]]; }
            public long AllocationSiteId(int index) { return m_Snapshot.NativeAllocations.AllocationSiteId[this[index]]; }
            public int OverheadSize(int index) { return m_Snapshot.NativeAllocations.OverheadSize[this[index]]; }
            public int PaddingSize(int index) { return m_Snapshot.NativeAllocations.PaddingSize[this[index]]; }
        }

        public class SortedNativeObjectsCache : ISortedEntriesCache
        {
            CachedSnapshot m_Snapshot;
            int[] m_Sorting;

            public SortedNativeObjectsCache(CachedSnapshot snapshot)
            {
                m_Snapshot = snapshot;
            }

            public void Preload()
            {
                if (m_Sorting == null)
                {
                    m_Sorting = new int[m_Snapshot.NativeObjects.NativeObjectAddress.Count];

                    for (int i = 0; i < m_Sorting.Length; ++i)
                        m_Sorting[i] = i;

                    Array.Sort(m_Sorting, (x, y) => m_Snapshot.NativeObjects.NativeObjectAddress[x].CompareTo(m_Snapshot.NativeObjects.NativeObjectAddress[y]));
                }
            }

            int this[int index]
            {
                get
                {
                    Preload();
                    return m_Sorting[index];
                }
            }

            public int  Count { get { return (int)m_Snapshot.NativeObjects.Count; } }
            public ulong Address(int index) { return m_Snapshot.NativeObjects.NativeObjectAddress[this[index]]; }
            public ulong Size(int index) { return m_Snapshot.NativeObjects.Size[this[index]]; }
            public string Name(int index) { return m_Snapshot.NativeObjects.ObjectName[this[index]]; }
            public int InstanceId(int index) { return m_Snapshot.NativeObjects.InstanceId[this[index]]; }
            public int NativeTypeArrayIndex(int index) { return m_Snapshot.NativeObjects.NativeTypeArrayIndex[this[index]]; }
            public HideFlags HideFlags(int index) { return m_Snapshot.NativeObjects.HideFlags[this[index]]; }
            public ObjectFlags Flags(int index) { return m_Snapshot.NativeObjects.Flags[this[index]]; }
            public long RootReferenceId(int index) { return m_Snapshot.NativeObjects.RootReferenceId[this[index]]; }
            public int Refcount(int index) { return m_Snapshot.NativeObjects.refcount[this[index]]; }
            public int ManagedObjectIndex(int index) { return m_Snapshot.NativeObjects.ManagedObjectIndex[this[index]]; }
        }


        unsafe static void ConvertDynamicArrayByteBufferToManagedArray<T>(DynamicArray<byte> nativeEntryBuffer, ref T[] elements) where T : class
        {
            byte* binaryDataStream = (byte*)nativeEntryBuffer.GetUnsafePtr();
            //jump over the offsets array
            long* binaryEntriesLength = (long*)binaryDataStream;
            binaryDataStream = binaryDataStream + sizeof(long) * (elements.Length + 1); //+1 due to the final element offset being at the end

            for (int i = 0; i < elements.Length; ++i)
            {
                byte* srcPtr = binaryDataStream + binaryEntriesLength[i];
                int actualLength = (int)(binaryEntriesLength[i + 1] - binaryEntriesLength[i]);

                if (typeof(T) == typeof(string))
                {
                    var nStr = new string('A', actualLength);
                    elements[i] = nStr as T;
                    fixed(char* dstPtr = nStr)
                    {
                        UnsafeUtility.MemCpyStride(dstPtr, UnsafeUtility.SizeOf<char>(),
                            srcPtr, UnsafeUtility.SizeOf<byte>(), UnsafeUtility.SizeOf<byte>(), actualLength);
                    }
                }
                else
                {
                    object arr = null;
                    if (typeof(T) == typeof(byte[]))
                    {
                        arr = new byte[actualLength];
                    }
                    else if (typeof(T) == typeof(int[]))
                    {
                        arr = new int[actualLength / UnsafeUtility.SizeOf<int>()];
                    }
                    else if (typeof(T) == typeof(ulong[]))
                    {
                        arr = new ulong[actualLength / UnsafeUtility.SizeOf<ulong>()];
                    }
                    else if (typeof(T) == typeof(long[]))
                    {
                        arr = new long[actualLength / UnsafeUtility.SizeOf<long>()];
                    }
                    else
                    {
                        Debug.LogErrorFormat("Unsuported type provided for conversion, type name: {0}", typeof(T).FullName);
                        return;
                    }

                    ulong handle = 0;
                    void* dstPtr = UnsafeUtility.PinGCArrayAndGetDataAddress(arr as Array, out handle);
                    UnsafeUtility.MemCpy(dstPtr, srcPtr, actualLength);
                    UnsafeUtility.ReleaseGCObject(handle);
                    elements[i] = arr as T;
                }
            }
        }
    }
}

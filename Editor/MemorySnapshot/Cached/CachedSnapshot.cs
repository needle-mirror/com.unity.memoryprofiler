using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Editor.Containers.Unsafe;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Diagnostics;
using Unity.MemoryProfiler.Editor.Extensions;
using Unity.MemoryProfiler.Editor.Format;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;
using Unity.Profiling;
using UnityEditor;
using Unity.MemoryProfiler.Editor.EnumerationUtilities;
using Unity.MemoryProfiler.Editor.Managed;
using Unity.MemoryProfiler.Editor.UI;

// Pre com.unity.collections@2.1.0 NativeHashMap was not constraining its held data to unmanaged but to struct.
// NativeHashSet does not have the same issue, but for ease of use may get an alias below for EntityId.
#if !UNMANAGED_NATIVE_HASHMAP_AVAILABLE
#if !ENTITY_ID_CHANGED_SIZE
using AddressToInstanceId = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<ulong, Unity.MemoryProfiler.Editor.EntityId>;
using InstanceIdToNativeObjectIndex = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<Unity.MemoryProfiler.Editor.EntityId, long>;
#else
using AddressToInstanceId = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<ulong, UnityEngine.EntityId>;
using InstanceIdToNativeObjectIndex = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<UnityEngine.EntityId, long>;
#endif
using AddressToIndex = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<ulong, long>;
using AddressToIntIndex = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<ulong, int>;
using LongToLongHashMap = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<long, long>;
using LongToIntHashMap = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<long, int>;
#else
#if !ENTITY_ID_CHANGED_SIZE
using AddressToInstanceId = Unity.Collections.NativeHashMap<ulong, Unity.MemoryProfiler.Editor.EntityId>;
using InstanceIdToNativeObjectIndex = Unity.Collections.NativeHashMap<Unity.MemoryProfiler.Editor.EntityId, long>;
#else
using AddressToInstanceId = Unity.Collections.NativeHashMap<ulong, UnityEngine.EntityId>;
using InstanceIdToNativeObjectIndex = Unity.Collections.NativeHashMap<UnityEngine.EntityId, long>;
#endif
using AddressToIndex = Unity.Collections.NativeHashMap<ulong, long>;
using AddressToIntIndex = Unity.Collections.NativeHashMap<ulong, int>;
using LongToLongHashMap = Unity.Collections.NativeHashMap<long, long>;
using LongToIntHashMap = Unity.Collections.NativeHashMap<long, int>;
#endif
using HideFlags = UnityEngine.HideFlags;
using RuntimePlatform = UnityEngine.RuntimePlatform;
using Debug = UnityEngine.Debug;
using UnityException = UnityEngine.UnityException;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;


#if !ENTITY_ID_CHANGED_SIZE
// the official EntityId lives in the UnityEngine namespace, which might be be added as a using via the IDE,
// so to avoid mistakenly using a version of this struct with the wrong size, alias it here.
using EntityId = Unity.MemoryProfiler.Editor.EntityId;
#else
// This should be greyed out by the IDE, otherwise you're missing an alias above
using UnityEngine;
using EntityId = UnityEngine.EntityId;
#endif

namespace Unity.MemoryProfiler.Editor
{
    // TODO: EntityId to be moved to EditorSelectionAndEntityIdUtilities.cs
#if !ENTITY_ID_CHANGED_SIZE
    // For simple API compatibility usage in versions pre InstanceId change
    struct EntityId : IEquatable<EntityId>, IComparable<EntityId>
    {
        ulong m_Id;

        public static EntityId None => new EntityId { m_Id = 0 };
        public static EntityId From(ulong id) => new EntityId { m_Id = id };
        /// <summary>
        /// This assumption might not hold for an 8byte EntityID future,
        /// and the reported Persistent flag should be relied on instead.
        ///
        /// The actual (not MemoryProfiler package based) EntityId.IsRuntimeCreated() API
        /// is then however only valid during execution of the actually relevant runtime, not for EntityIds loaded out of a snapshot.
        /// </summary>
        /// <returns></returns>
        internal bool IsRuntimeCreated() => this.ConvertToIdInt() < 0;
        public override bool Equals(object obj) => obj is EntityId id && Equals(id);
        public bool Equals(EntityId other) => m_Id == other.m_Id;
        public override int GetHashCode() => m_Id.GetHashCode();
        public int CompareTo(EntityId other) => m_Id.CompareTo(other.m_Id);
        public static bool operator ==(EntityId a, EntityId b) => a.Equals(b);
        public static bool operator !=(EntityId a, EntityId b) => !a.Equals(b);
        public static explicit operator ulong(EntityId instanceID) => instanceID.m_Id;
        public override string ToString()
        {
            if (m_Id > uint.MaxValue)
                return m_Id.ToString();
            return ((int)m_Id).ToString();
        }
    }
#endif

    // TODO: EntityAndInstanceIdHelper to be moved to EditorSelectionAndEntityIdUtilities.cs
    static class EntityAndInstanceIdHelper
    {
#if ENTITY_ID_STRUCT_AVAILABLE
        public static bool IsRuntimeCreated(this UnityEngine.EntityId id) => ConvertToIdInt(ConvertFromUnityEntityId(id)) < 0;
#endif

        public static void ConvertInstanceIdIntsToEntityIds(this DynamicArray<int> intInstanceIds, ref DynamicArray<EntityId> instanceIds)
        {
            if (intInstanceIds.Count == 0)
            {
                return;
            }
            if (intInstanceIds.Count != instanceIds.Count)
            {
                throw new InvalidOperationException("The count of the two arrays must be the same");
            }
            // We are reading old snapshot data here so its not as though those instance IDs mean anything
            // beyond their pure values relative to other usages of the same valus within the snapshot.
            // I.e. they are fake either way but as long as their value is consistently transposed to EntityId for everything in the snapshot,
            // everything still works as expected.
            // So while we're just converting to bogus values to make sure all relevant lookups still work, we might as well do it the fast way

            // The fast path
            unsafe
            {
                // Safe conversion as old data naturally enforce the limit of int.MaxValue for these
                var elementCount = (int)intInstanceIds.Count;
                UnsafeUtility.MemCpyStride(instanceIds.GetUnsafePtr(), sizeof(EntityId), intInstanceIds.GetUnsafePtr(), sizeof(int), sizeof(int), elementCount);
            }

            //// The slow path. Break comment block in case of failure of the above.
            //for (int i = 0; i < intInstanceIds.Count; i++)
            //{
            //    instanceIds[i] = EntityId.From((ulong)intInstanceIds[i]);
            //}
        }

        public static EntityId GetEntityId(this UnityEngine.Object obj)
        {
            if (obj != null)
#if ENTITY_ID_STRUCT_AVAILABLE
                return Convert(obj.GetEntityId());
#else
                return Convert(obj.GetInstanceID());
#endif
            else return EntityId.None;
        }

#if ENTITY_ID_CHANGED_SIZE
        // EntityId is UnityEngine.EntityId
        public static EntityId Convert(EntityId id) => id;
        public static EntityId ConvertToUnityEntityId(EntityId id) => id;
        public static EntityId ConvertFromUnityEntityId(EntityId id) => id;

        public static int ConvertToIdInt(this EntityId id)
        {
            int instanceId = 0;
            unsafe
            {
                // The ID bytes are the first 4 bytes.
                UnsafeUtility.MemCpyStride(&instanceId, sizeof(int), ((byte*)&id), sizeof(EntityId), sizeof(EntityId), 1);
            }
            return instanceId;
        }
#else
        public static int ConvertToIdInt(this EntityId id)
        {
            int instanceId = 0;
            unsafe
            {
                UnsafeUtility.MemCpyStride(&instanceId, sizeof(int), &id, sizeof(EntityId), sizeof(EntityId), 1);
            }
            return instanceId;
        }
#if ENTITY_ID_STRUCT_AVAILABLE
        public unsafe static UnityEngine.EntityId ConvertToUnityEntityId(EntityId id)
        {
            var eId = stackalloc UnityEngine.EntityId[1];
            var eIdInt = (int*)eId;
            eIdInt[0] = id.ConvertToIdInt();
            return eId[0];
        }
        public unsafe static EntityId ConvertFromUnityEntityId(UnityEngine.EntityId id)
        {
            var eId = stackalloc UnityEngine.EntityId[1];
            UnsafeUtility.CopyStructureToPtr(ref id, eId);
            var eIdInt = (int*)eId;
            return ConvertInt(eIdInt[0]);
        }
        public static EntityId Convert(UnityEngine.EntityId id) => ConvertFromUnityEntityId(id);
#endif
        public static EntityId Convert(int id) => ConvertInt(id);
#endif
        /// <summary>
        /// Call <see cref="Convert"/> if <paramref name="id"/> would be an <see cref="EntityId"/>
        /// depending on the define of ENTITY_ID_CHANGED_SIZE being true or false.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public static EntityId ConvertInt(int id)
        {
            ulong instanceId = 0;
            unsafe
            {
                UnsafeUtility.MemCpyStride(&instanceId, sizeof(EntityId), &id, sizeof(int), sizeof(int), 1);
            }
            return EntityId.From(instanceId);
        }

        public static bool TryConvertToEntityID(this string instanceIdStr, out EntityId instanceId)
        {
            if (!string.IsNullOrEmpty(instanceIdStr))
            {
                if (int.TryParse(instanceIdStr, out var instanceID))
                {
                    instanceId = ConvertInt(instanceID);
                    return true;
                }
                if (long.TryParse(instanceIdStr, out var instanceIDLong))
                {
                    instanceId = EntityId.From((ulong)instanceIDLong);
                    return true;
                }
                if (ulong.TryParse(instanceIdStr, out var instanceIDULong))
                {
                    instanceId = EntityId.From(instanceIDULong);
                    return true;
                }
            }
            instanceId = EntityId.From(0UL);
            return false;
        }
    }

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

    internal static class TypeTools
    {
        public enum FieldFindOptions
        {
            OnlyInstance,
            OnlyStatic
        }

        static void RecurseCrawlFields(ref List<int> fieldsBuffer, int typeIndex, TypeDescriptionEntriesCache typeDescriptions, FieldDescriptionEntriesCache fieldDescriptions, FieldFindOptions fieldFindOptions, bool crawlBase)
        {
            bool isValueType = typeDescriptions.HasFlag(typeIndex, TypeFlags.kValueType);
            if (crawlBase)
            {
                int baseTypeIndex = typeDescriptions.BaseOrElementTypeIndex[typeIndex];
                if (crawlBase && baseTypeIndex != -1 && !isValueType)
                {
                    RecurseCrawlFields(ref fieldsBuffer, baseTypeIndex, typeDescriptions, fieldDescriptions, fieldFindOptions, true);
                }
            }


            var fieldIndices = typeDescriptions.FieldIndices[typeIndex];
            for (int i = 0; i < fieldIndices.Count; ++i)
            {
                var iField = fieldIndices[i];

                if (!FieldMatchesOptions(iField, fieldDescriptions, fieldFindOptions))
                    continue;

                if (fieldDescriptions.TypeIndex[iField] == typeIndex && isValueType)
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

    [BurstCompile]
    internal class CachedSnapshot : IDisposable
    {
        public const string InvalidItemName = "<No Name>";
        public const string UnrootedItemName = "Unrooted";
        public const string UnknownMemlabelName = "Unknown MemLabel";
        public const string RootName = "Root";

        static CachedSnapshot()
        {
#if ENTITY_ID_STRUCT_AVAILABLE && !ENTITY_ID_CHANGED_SIZE
#pragma warning disable CS0184 // 'is' expression's given expression is never of the provided type (until it is, because UnityEngine was accidentally included.)
            Debug.Assert(!(typeof(EntityId) is UnityEngine.EntityId), "The wrong type of EntityId struct is used, probably due to accidentally addin a 'using UnityEngine;' to this file.");
#pragma warning restore CS0184 // 'is' expression's given expression is never of the provided type
#endif
        }

        public bool Valid { get { return !m_Disposed && CrawledData.Crawled; } }
        bool m_Disposed = false;

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

        public bool HasSceneRootsAndAssetbundles
        {
            get { return m_SnapshotVersion >= FormatVersion.SceneRootsAndAssetBundlesVersion; }
        }

        public bool HasGfxResourceReferencesAndAllocators
        {
            get { return m_SnapshotVersion >= FormatVersion.GfxResourceReferencesAndAllocatorsVersion; }
        }

        public bool HasNativeObjectMetaData
        {
            get { return m_SnapshotVersion >= FormatVersion.NativeObjectMetaDataVersion; }
        }

        public bool HasSystemMemoryRegionsInfo
        {
            get { return (m_SnapshotVersion >= FormatVersion.SystemMemoryRegionsVersion) && (SystemMemoryRegions.Count > 0); }
        }

        public bool HasSystemMemoryResidentPages
        {
            get { return (m_SnapshotVersion >= FormatVersion.SystemMemoryResidentPagesVersion) && (SystemMemoryResidentPages.Count > 0); }
        }

        public bool HasEntityIDAs8ByteStructs
        {
            get { return (m_SnapshotVersion >= FormatVersion.EntityIDAs8ByteStructs); }
        }

        bool m_HasPrefabRootInfo;
        public bool HasPrefabRootInfo => m_HasPrefabRootInfo;

        public ManagedData CrawledData { internal set; get; }

        public class NativeAllocationSiteEntriesCache : IDisposable
        {
            // Call Side Ids are pointers, so 0 is an invalid Site Id
            public const ulong SiteIdNullPointer = 0;
            public long Count;
            public DynamicArray<ulong> Id = default;
            public DynamicArray<int> MemoryLabelIndex = default;
            public readonly Dictionary<ulong, long> IdToIndex;

            public NestedDynamicArray<ulong> callstackSymbols => m_callstackSymbolsReadOp.CompleteReadAndGetNestedResults();
            NestedDynamicSizedArrayReadOperation<ulong> m_callstackSymbolsReadOp;

            unsafe public NativeAllocationSiteEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.NativeAllocationSites_Id);

                if (Count == 0)
                    return;

                Id = reader.Read(EntryType.NativeAllocationSites_Id, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                MemoryLabelIndex = reader.Read(EntryType.NativeAllocationSites_MemoryLabelIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();

                m_callstackSymbolsReadOp = reader.AsyncReadDynamicSizedArray<ulong>(EntryType.NativeAllocationSites_CallstackSymbols, 0, Count, Allocator.Persistent);
                IdToIndex = new Dictionary<ulong, long>((int)Count);
                for (long idx = 0; idx < Count; idx++)
                {
                    IdToIndex[Id[idx]] = idx;
                }
            }

            public readonly struct ReadableCallstack
            {
                public readonly string MemLabel;
                public readonly string Callstack;
                public readonly List<KeyValuePair<int, string>> FileLinkHashToFileName;
                public ReadableCallstack(string memLabel, string callstack, List<KeyValuePair<int, string>> fileLinkHashToFileName)
                {
                    MemLabel = memLabel;
                    Callstack = callstack;
                    FileLinkHashToFileName = fileLinkHashToFileName;
                }
            }

            public readonly struct CallStackInfo : IEquatable<CallStackInfo>
            {
                public readonly ulong Id;
                public readonly long Index;
                public readonly int MemoryLabelIndex;
                public readonly DynamicArrayRef<ulong> CallstackSymbols;
                // siteId is an address so 0 is an invalid site Id. However, this is handled via TryGet on IdToIndex in GetCallStackInfo
                // Checking the Index and that there is actual symbols should suffice here
                public readonly bool Valid => /* Id != NullPointerId &&*/ Index >= 0 && CallstackSymbols.IsCreated;

                public CallStackInfo(ulong id, long index, int memoryLabelIndex, DynamicArrayRef<ulong> callstackSymbols)
                {
                    Id = id;
                    Index = index;
                    MemoryLabelIndex = memoryLabelIndex;
                    CallstackSymbols = callstackSymbols;
                }

                public bool Equals(CallStackInfo other)
                {
                    // TODO: Check if Call Site ID is a sufficient comparison.
                    // Instinct says two different call stacks might end in the same site ID.

                    // Ignoring Index and MemoryLabelIndex as those are irrelevant for the sameness of the callstack
                    return Id == other.Id && CallstackSymbols.Equals(other.CallstackSymbols);
                }

                public readonly int GetHashCode(CallStackInfo obj) => obj.GetHashCode();
                public override int GetHashCode() => HashCode.Combine(Id, CallstackSymbols);
            }

            public CallStackInfo GetCallStackInfo(ulong id)
            {
                if (id != SiteIdNullPointer && IdToIndex.TryGetValue(id, out var index))
                    return new CallStackInfo(id, index, MemoryLabelIndex[index], callstackSymbols[index]);
                return new CallStackInfo(id, -1, -1, new DynamicArrayRef<ulong>());
            }

            public string GetMemLabelName(CachedSnapshot snapshot, SourceIndex nativeAllocationIndex)
            {
                if (Count <= 0 || !nativeAllocationIndex.Valid || nativeAllocationIndex.Id is not SourceIndex.SourceId.NativeAllocation)
                    return null;
                var siteId = snapshot.NativeAllocations.AllocationSiteId[nativeAllocationIndex.Index];
                var memLabelIndex = GetMemLabel(siteId);

                string memLabelName = memLabelIndex.Valid ? snapshot.NativeMemoryLabels.MemoryLabelName[memLabelIndex.Index] : CachedSnapshot.UnknownMemlabelName;
                return memLabelName;
            }

            static readonly SourceIndex k_FakeInvalidlyLabeledAllocationIndex = new SourceIndex(SourceIndex.SourceId.None, (long)SourceIndex.SpecialNoneCase.UnknownMemLabel);
            public SourceIndex GetMemLabel(ulong siteId)
            {
                if (siteId == NativeAllocationSiteEntriesCache.SiteIdNullPointer
                    || !IdToIndex.TryGetValue(siteId, out var siteIndex))
                    siteIndex = -1;
                var memLabelIndex = siteIndex >= 0 ? MemoryLabelIndex[siteIndex] : -1;
                if (memLabelIndex <= CachedSnapshot.NativeMemoryLabelEntriesCache.InvalidMemLabelIndex)
                    memLabelIndex = -1;
                var memLabelSourceIndex = memLabelIndex >= 0 ? new SourceIndex(SourceIndex.SourceId.MemoryLabel, memLabelIndex) : k_FakeInvalidlyLabeledAllocationIndex;
                return memLabelSourceIndex;
            }

            public ReadableCallstack GetReadableCallstackForId(NativeMemoryLabelEntriesCache memLabels, NativeCallstackSymbolEntriesCache symbols, ulong id)
            {
                long entryIdx = -1;
                for (long i = 0; i < Id.Count; ++i)
                {
                    if (Id[i] == id)
                    {
                        entryIdx = i;
                        break;
                    }
                }

                return entryIdx < 0 ? new ReadableCallstack(string.Empty, string.Empty, null) : GetReadableCallstack(memLabels, symbols, entryIdx);
            }

            public void AppendCallstackLine(NativeCallstackSymbolEntriesCache symbols, ulong targetSymbol, StringBuilder stringBuilder,
                List<KeyValuePair<int, string>> fileLinkHashToFileName = null, bool simplifyCallStacks = true, bool clickableCallStacks = true, bool terminateWithLineBreak = true)
            {
                if (symbols.SymbolToIndex.TryGetValue(targetSymbol, out var symbolIdx))
                {
                    // Format of symbols is: "0x0000000000000000 (Unity) OptionalNativeNamespace::NativeClass<PotentialTemplateType>::NativeMethod (at C:/Path/To/CPPorHeaderFile.h:428)\n"
                    // or "0x0000000000000000 (Unity) Managed.Namespace.List.ClassName:MethodName (parametertypes,separated,by,comma) (at C:/Path/To/CSharpFile.cs:13)\n"
                    // or "0x0000000000000000 (KERNEL32) BaseThreadInitThunk\n"
                    // or "0x0000000000000000 ((<unknown>)) \n"
                    try
                    {
                        var symbol = symbols.ReadableStackTrace[symbolIdx].AsSpan();
                        var firstCharIndexOfAssemblyName = symbol.IndexOf('(');
                        if (firstCharIndexOfAssemblyName > 0)
                        {
                            if (symbol[firstCharIndexOfAssemblyName + 1] == '(')
                            {
                                if (simplifyCallStacks)
                                    stringBuilder.AppendLine("<unknown>");
                                else
                                    stringBuilder.Append(symbol);
                                return;
                            }
                            if (!simplifyCallStacks)
                            {
                                var address = symbol.Slice(0, firstCharIndexOfAssemblyName);
                                stringBuilder.Append(address);
                            }
                            symbol = symbol.Slice(firstCharIndexOfAssemblyName);
                        }
                        var lastCharIndexOfMethodName = symbol.LastIndexOf('(');
                        if (lastCharIndexOfMethodName <= 0)
                        {
                            if (!terminateWithLineBreak)
                            {
                                var indexOfLineBreak = symbol.IndexOf('\n');
                                if (indexOfLineBreak > 0)
                                    symbol = symbol.Slice(0, indexOfLineBreak);
                            }
                            stringBuilder.Append(symbol);
                            return;
                        }
                        var methodName = symbol.Slice(0, lastCharIndexOfMethodName);
                        symbol = symbol.Slice(lastCharIndexOfMethodName);

                        stringBuilder.Append(methodName);

                        const string k_FileNamePrefix = "(at ";

                        var fileNameStart = symbol.IndexOf(k_FileNamePrefix) + k_FileNamePrefix.Length;
                        var fileNameEndIndex = symbol.LastIndexOf(':');
                        var fileNameLength = fileNameEndIndex - fileNameStart;
                        if (clickableCallStacks && fileNameLength > 0)
                        {
                            var fileName = symbol.Slice(fileNameStart, fileNameLength).ToString();
                            var lineNumberEndIndex = symbol.LastIndexOf(')');
                            var lineNumberChars = symbol.Slice(fileNameEndIndex + 1, lineNumberEndIndex - fileNameEndIndex - 1);

                            if (!int.TryParse(lineNumberChars, out var lineNumber))
                                lineNumber = 0;

                            if (fileLinkHashToFileName != null)
                            {
                                // TextCore htlm tags have a character limit of 128 in Unity 2022 and 256 in Unity 6+,
                                // so putting the full file path as the link might break. Using a hash also means less chars wasted.
                                var fileNameHash = fileName.GetHashCode();
                                fileLinkHashToFileName.Add(new KeyValuePair<int, string>(fileNameHash, fileName));

                                stringBuilder.AppendFormat("\t(at <link=\"href='{0}' line='{1}'\"><color={3}><u>{2}:{1}</u></color></link>){4}", fileNameHash, lineNumber, fileName, EditorGUIUtility.isProSkin ? "#40a0ff" : "#0000FF", terminateWithLineBreak ? '\n' : "");
                            }
                            else
                            {
                                stringBuilder.AppendFormat("\t(at <link=\"href='{0}' line='{1}'\"><color={3}><u>{2}:{1}</u></color></link>){4}", fileName, lineNumber, fileName, EditorGUIUtility.isProSkin ? "#40a0ff" : "#0000FF", terminateWithLineBreak ? '\n' : "");
                            }
                        }
                        else
                        {
                            if (!terminateWithLineBreak)
                            {
                                var indexOfLineBreak = symbol.IndexOf('\n');
                                if (indexOfLineBreak > 0)
                                    symbol = symbol.Slice(0, indexOfLineBreak);
                            }
                            stringBuilder.Append(symbol);
                        }
                    }
                    catch (Exception)
                    {
                        stringBuilder.AppendLine(symbols.ReadableStackTrace[symbolIdx]);
                    }
                }
                else
                {
                    stringBuilder.AppendLine("<unknown>");
                }
            }

            public ReadableCallstack GetReadableCallstack(NativeMemoryLabelEntriesCache memLabels, NativeCallstackSymbolEntriesCache symbols, long idx, bool simplifyCallStacks = true, bool clickableCallStacks = true)
            {
                var stringBuilder = new StringBuilder();

                var callstackSymbols = this.callstackSymbols[idx];
                var fileLinkHashToFileName = new List<KeyValuePair<int, string>>((int)callstackSymbols.Count);
                for (long i = 0; i < callstackSymbols.Count; ++i)
                {
                    ulong targetSymbol = callstackSymbols[i];
                    AppendCallstackLine(symbols, targetSymbol, stringBuilder, fileLinkHashToFileName, simplifyCallStacks, clickableCallStacks);
                }

                return new ReadableCallstack(
                    MemoryLabelIndex[idx] >= NativeMemoryLabelEntriesCache.InvalidMemLabelIndex ?
                    memLabels.MemoryLabelName[MemoryLabelIndex[idx]] : CachedSnapshot.UnknownMemlabelName,
                    stringBuilder.ToString(), fileLinkHashToFileName);
            }


            public void Dispose()
            {
                Id.Dispose();
                MemoryLabelIndex.Dispose();
                if (m_callstackSymbolsReadOp.IsCreated)
                {
                    // Dispose the read operation first to abort it ...
                    m_callstackSymbolsReadOp.Dispose();
                    // ... before disposing the result, as otherwise we'd sync on a pending read op.
                    callstackSymbols.Dispose();
                    m_callstackSymbolsReadOp = default;
                }
                Count = 0;
            }
        }

        public class NativeRootReferenceEntriesCache : IDisposable
        {
            public const long InvalidRootId = 0;
            public const long FirstValidRootId = 1;
            public const long InvalidRootIndex = 0;
            public const long FirstValidRootIndex = 1;
            public long Count;
            public DynamicArray<long> Id = default;
            public DynamicArray<ulong> AccumulatedSize = default;
            public string[] AreaName;
            public string[] ObjectName;
            public LongToLongHashMap IdToIndex;
            public readonly SourceIndex VMRootReferenceIndex = default;
            public readonly ulong AccumulatedSizeOfVMRoot = 0UL;
            public readonly ulong ExecutableAndDllsReportedValue;
            public const string ExecutableAndDllsRootReferenceName = "ExecutableAndDlls";
            public readonly long ExecutableAndDllsRootReferenceIndex = -1;
            static readonly string[] k_VMRootNames =
            {
                "Mono VM",
                "IL2CPP VM",
                "IL2CPPMemoryAllocator",
            };

            public NativeRootReferenceEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.NativeRootReferences_Id);

                AreaName = new string[Count];
                ObjectName = new string[Count];

                IdToIndex = new LongToLongHashMap((int)Count, Allocator.Persistent);

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

                var hasCalculatedAccumulatedSizeOfVMRoot = false;
                for (long i = 0; i < Count; i++)
                {
                    if (ExecutableAndDllsRootReferenceIndex == -1 && ObjectName[i] == ExecutableAndDllsRootReferenceName)
                    {
                        ExecutableAndDllsRootReferenceIndex = i;
                        ExecutableAndDllsReportedValue = AccumulatedSize[i];
                        // Nothing is ever actually rooted to "System : ExecutableAndDlls". This is just a hacky way of reporting systeminfo::GetExecutableSizeMB()
                        // therefore there is no need to map it to an index (of 0) and thereby wrongly suggest that allocations with root id 0 would belong to the executable size
                        if (i == 0)
                            continue;
                    }
                    IdToIndex.Add(Id[i], i);

                    if (!hasCalculatedAccumulatedSizeOfVMRoot)
                    {
                        var name = ObjectName[i];
                        foreach (var vmRootName in k_VMRootNames)
                        {
                            if (name.Equals(vmRootName, StringComparison.Ordinal))
                            {
                                // There is only one VM root in a capture, so we can stop looking once found.
                                AccumulatedSizeOfVMRoot = AccumulatedSize[i];
                                VMRootReferenceIndex = new SourceIndex(SourceIndex.SourceId.NativeRootReference, i);
                                hasCalculatedAccumulatedSizeOfVMRoot = true;
                            }
                        }
                    }
                }
            }

            public void Dispose()
            {
                Id.Dispose();
                AccumulatedSize.Dispose();
                Count = 0;
                AreaName = null;
                ObjectName = null;
                IdToIndex.Dispose();
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
            public HashSet<long> GPUAllocatorIndices = new HashSet<long>();

            const string k_DynamicHeapAllocatorName = "ALLOC_DEFAULT_MAIN";
            const string k_GPUAllocatorName = "ALLOC_GPU";

            public NativeMemoryRegionEntriesCache(ref IFileReader reader)
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

                for (long i = 0; i < Count; i++)
                {
                    if (!UsesDynamicHeapAllocator && AddressSize[i] > 0 && MemoryRegionName[i].StartsWith(k_DynamicHeapAllocatorName))
                    {
                        UsesDynamicHeapAllocator = true;
                    }

                    if (MemoryRegionName[i].StartsWith(k_GPUAllocatorName))
                    {
                        GPUAllocatorIndices.Add(i);
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
            /// <summary>
            /// The first memory label index is kMemTempLabels, i.e. the delimiter and start for all temp labels.
            /// The range of temp labels goes from kMemTempLabels to kMemRegularLabels (excluding both of these delimiters).
            /// The non-temp labels go from kMemRegularLabels to kMemLabelCount (excluding both of these delimiters).
            ///
            /// So not only is index 0 a delimiter, temp labeled allocations, in order to not incur a pefromance hit on these,
            /// do not record a callstack and callsite, they also are not even registered with the Memory Profiler,
            /// so there won't be any allocations in a snapshot that have these memory labels assigned to them.
            ///
            /// NOTE: -1 is the default value that gets serialized if no callstack info is present,
            /// so for validity, check for _greater than_ <see cref="InvalidMemLabelIndex"/>.
            /// </summary>
            public const long InvalidMemLabelIndex = 0;
            public long Count;
            public string[] MemoryLabelName;
            public DynamicArray<ulong> MemoryLabelSizes = default;

            public NativeMemoryLabelEntriesCache(ref IFileReader reader, bool hasLabelSizes)
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

            public ulong GetLabelSize(string label)
            {
                if (Count <= 0)
                    return 0;

                var labelIndex = Array.IndexOf(MemoryLabelName, label);
                if (labelIndex == -1)
                    return 0;

                return MemoryLabelSizes[labelIndex];
            }
        }

        public class NativeCallstackSymbolEntriesCache : IDisposable
        {
            public long Count;
            public DynamicArray<ulong> Symbol = default;
            public string[] ReadableStackTrace;
            public Dictionary<ulong, long> SymbolToIndex;

            public NativeCallstackSymbolEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.NativeCallstackSymbol_Symbol);
                ReadableStackTrace = new string[Count];

                if (Count == 0)
                {
                    SymbolToIndex = new Dictionary<ulong, long>();
                    return;
                }

                Symbol = reader.Read(EntryType.NativeCallstackSymbol_Symbol, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                SymbolToIndex = new Dictionary<ulong, long>((int)Count);
                for (long idx = 0; idx < Count; idx++)
                {
                    SymbolToIndex[Symbol[idx]] = idx;
                }
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
            /// <summary>
            /// Note: Reference ID 0 means the allocation was not rooted to anything, not that it was rooted to "System : ExecutableAndDlls"
            /// </summary>
            public DynamicArray<long> RootReferenceId = default;
            public DynamicArray<ulong> Address = default;
            public DynamicArray<ulong> Size = default;
            public DynamicArray<int> OverheadSize = default;
            public DynamicArray<int> PaddingSize = default;
            public DynamicArray<ulong> AllocationSiteId = default;

            public NativeAllocationEntriesCache(ref IFileReader reader, bool allocationSites /*do not read allocation sites if they aren't present*/)
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
                    AllocationSiteId = reader.Read(EntryType.NativeAllocations_AllocationSiteId, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
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

            public string ProduceAllocationNameForAllocation(CachedSnapshot snapshot, long allocationIndex, bool higlevelObjectNameOnlyIfAvailable = true, bool ignoreNativeObjectName = false)
            {
                // Check if we have memory label roots information
                if (snapshot.NativeAllocations.RootReferenceId.Count <= 0)
                    return InvalidItemName;

                // Check if allocation has memory label root
                var rootReferenceId = snapshot.NativeAllocations.RootReferenceId[allocationIndex];
                if (rootReferenceId <= 0)
                    return InvalidItemName;
                return ProduceAllocationNameForRootReferenceId(snapshot, rootReferenceId, higlevelObjectNameOnlyIfAvailable, ignoreNativeObjectName);
            }

            public string ProduceAllocationNameForRootReferenceId(CachedSnapshot snapshot, long rootReferenceId, bool higlevelObjectNameOnlyIfAvailable = true, bool ignoreNativeObjectName = false)
            {
                var nativeObjectName = String.Empty;
                // Lookup native object index associated with memory label root
                if (!ignoreNativeObjectName && snapshot.NativeObjects.RootReferenceIdToIndex.TryGetValue(rootReferenceId, out var objectIndex))
                {
                    if (higlevelObjectNameOnlyIfAvailable)
                        return snapshot.NativeObjects.ObjectName[objectIndex];
                    else
                        nativeObjectName = snapshot.NativeObjects.ObjectName[objectIndex];
                }

                // Try to see if memory label root is associated with any memory area
                if (snapshot.NativeRootReferences.IdToIndex.TryGetValue(rootReferenceId, out long rootIndex))
                {
                    var allocationObjectName = snapshot.NativeRootReferences.ObjectName[rootIndex];
                    return snapshot.NativeRootReferences.AreaName[rootIndex] + (string.IsNullOrEmpty(allocationObjectName) ? "" : (":" + allocationObjectName)) + (string.IsNullOrEmpty(nativeObjectName) || allocationObjectName == nativeObjectName ? "" : $" \"{nativeObjectName}\"");
                }

                return InvalidItemName;
            }
        }

        public unsafe class NativeTypeEntriesCache : IDisposable
        {
            public const int FirstValidTypeIndex = 0;
            public const int InvalidTypeIndex = -1;

            public long Count;
            public string[] TypeName;
            public DynamicArray<int> NativeBaseTypeArrayIndex = default;
            const string k_Transform = "Transform";
            public int TransformIdx { get; private set; } = InvalidTypeIndex;
            const string k_RectTransform = "RectTransform";
            public int RectTransformIdx { get; private set; } = InvalidTypeIndex;

            /// <summary>
            /// Technically, <see cref="IsOrDerivesFrom"/>(typeIndex, <see cref="TransformIdx"/>) could be used instead of this method,
            /// but since that approach would have to check the entire inheritance chain, this method is more efficient,
            /// and finding Transforms is enough of a hot path to warrant this explicit shorthand.
            /// </summary>
            /// <param name="typeIndex"></param>
            /// <returns></returns>
            public bool IsTransformOrRectTransform(long typeIndex) => (typeIndex >= 0) && (typeIndex == TransformIdx || typeIndex == RectTransformIdx);

            const string k_NamedObject = "NamedObject";
            public int NamedObjectIdx { get; private set; } = InvalidTypeIndex;

            const string k_BaseObject = "Object";
            public int BaseObjectIdx { get; private set; } = InvalidTypeIndex;

            const string k_GameObject = "GameObject";
            public int GameObjectIdx { get; private set; } = InvalidTypeIndex;

            const string k_MonoBehaviour = "MonoBehaviour";
            public int MonoBehaviourIdx { get; private set; } = InvalidTypeIndex;

            const string k_MonoScript = "MonoScript";
            public int MonoScriptIdx { get; private set; } = InvalidTypeIndex;

            const string k_Component = "Component";
            public int ComponentIdx { get; private set; } = InvalidTypeIndex;

            const string k_ScriptableObject = "ScriptableObject";
            const int k_ScriptableObjectDefaultTypeArrayIndexOffsetFromEnd = 2;
            public int ScriptableObjectIdx { get; private set; } = InvalidTypeIndex;

            const string k_EditorScriptableObject = "EditorScriptableObject";
            public int EditorScriptableObjectIdx { get; private set; } = InvalidTypeIndex;
            const int k_EditorScriptableObjectDefaultTypeArrayIndexOffsetFromEnd = 1;

            const string k_AssetBundle = "AssetBundle";
            public int AssetBundleIdx { get; private set; } = InvalidTypeIndex;

            const string k_Prefab = "Prefab";
            /// <summary>
            /// Only exists for snapshots where <see cref="MetaData.IsEditorCapture"/> is true.
            /// </summary>
            public int PrefabIdx { get; private set; } = InvalidTypeIndex;

            public NativeTypeEntriesCache(ref IFileReader reader)
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

                BaseObjectIdx = Array.FindIndex(TypeName, x => x == k_BaseObject);
                NamedObjectIdx = Array.FindIndex(TypeName, x => x == k_NamedObject);

                TransformIdx = Array.FindIndex(TypeName, x => x == k_Transform);
                RectTransformIdx = Array.FindIndex(TypeName, x => x == k_RectTransform);
                GameObjectIdx = Array.FindIndex(TypeName, x => x == k_GameObject);
                MonoBehaviourIdx = Array.FindIndex(TypeName, x => x == k_MonoBehaviour);
                MonoScriptIdx = Array.FindIndex(TypeName, x => x == k_MonoScript);
                ComponentIdx = Array.FindIndex(TypeName, x => x == k_Component);
                AssetBundleIdx = Array.FindIndex(TypeName, x => x == k_AssetBundle);
                PrefabIdx = Array.FindIndex(TypeName, x => x == k_Prefab);

                // for the fakable types ScriptableObject and EditorScriptable Objects, with the current backend, Array.FindIndex is always going to hit the worst case
                // in the current format, these types are always added last. Assume that for speed, keep Array.FindIndex as fallback in case the format changes
                ScriptableObjectIdx = FindTypeWithHint(k_ScriptableObject, Count - k_ScriptableObjectDefaultTypeArrayIndexOffsetFromEnd);
                EditorScriptableObjectIdx = FindTypeWithHint(k_EditorScriptableObject, Count - k_EditorScriptableObjectDefaultTypeArrayIndexOffsetFromEnd);
                if (EditorScriptableObjectIdx >= 0 && ScriptableObjectIdx >= 0)
                {
                    // ScriptableObject is more a variation than a base type of EditorScriptableObject, but for the purpose of this tool, we'll treat it as a base type
                    // Especially since the EditorScriptableObject is a fake type and its Managed Type is the same as ScriptableObject.
                    NativeBaseTypeArrayIndex[EditorScriptableObjectIdx] = ScriptableObjectIdx;
                }
            }

            int FindTypeWithHint(string typeName, long hintAtLikelyIndex)
            {
                if (TypeName[hintAtLikelyIndex] == typeName)
                    return (int)hintAtLikelyIndex;
                else
                    return Array.FindIndex(TypeName, x => x == typeName);
            }

            public bool IsOrDerivesFrom(int typeIndexToCheck, int baseTypeToCheckAgainst)
            {
                while (typeIndexToCheck != baseTypeToCheckAgainst && NativeBaseTypeArrayIndex[typeIndexToCheck] >= 0)
                {
                    typeIndexToCheck = NativeBaseTypeArrayIndex[typeIndexToCheck];
                }
                return typeIndexToCheck == baseTypeToCheckAgainst;
            }

            public void Dispose()
            {
                Count = 0;
                NativeBaseTypeArrayIndex.Dispose();
                TypeName = null;
            }
        }

        public unsafe class SceneRootEntriesCache : IDisposable
        {
            public const string DontDestroyOnLoadSceneName = "DontDestroyOnLoad";
            public readonly long DontDestroyOnLoadSceneIndex = -1;

            // the number of scenes
            public long SceneCount;
            public long SceneRootCount;
            public long PrefabRootCount;
            public long PrefabTransformCount;
            // the asset paths for the scenes
            public string[] AssetPath;
            // the scene names
            public string[] Name;
            //the paths to the scenes in the project
            public string[] Path;
            // the scene build index
            public DynamicArray<int> BuildIndex = default;
            //the number of roots in each of the scenes
            public DynamicArray<int> RootCounts = default;
            // each scenes offset into the main roots list
            public DynamicArray<int> RootOffsets = default;
            // first index is for the scene then the second is the array of ids for that scene
            public NestedDynamicArray<SourceIndex> SceneIndexedRootTransformInstanceIds;
            /// <summary>
            /// All of the scene root transform entity ids, only used for loading in the data,
            /// and populating the Source Indices in <see cref="AllSceneRootTransformSourceIndices"/> and
            /// <see cref="SceneIndexedRootTransformInstanceIds"/> and disposed during <see cref="GenerateBaseData"/>
            /// </summary>
            DynamicArray<EntityId> m_AllSceneRootTransformInstanceIds = default;
            /// <summary>
            /// All of the scene root gameobject instance ids
            /// </summary>
            public DynamicArray<SourceIndex> AllSceneRootTransformSourceIndices = default;
            public DynamicArray<SourceIndex> AllPrefabRootTransformSourceIndices = default;
            /// <summary>
            /// Maps all Native Object indices of persistent GameObject and Transform instances (root and non-root)
            /// to Prefab Root Indices which index into <see cref="AllPrefabRootTransformSourceIndices"/> for their
            /// root transforms.
            /// </summary>
            public LongToLongHashMap NativeObjectIndexToPrefabRootIndex = default;

            public SceneRootEntriesCache(ref IFileReader reader)
            {
                SceneCount = reader.GetEntryCount(EntryType.SceneObjects_Name);
                AssetPath = new string[SceneCount];
                Name = new string[SceneCount];
                Path = new string[SceneCount];

                if (SceneCount == 0)
                {
                    using var noOffsets = new DynamicArray<long>(1, Allocator.Temp);
                    noOffsets[0] = 0;
                    var sourceIds = new DynamicArray<SourceIndex>(0, Allocator.Persistent);
                    SceneIndexedRootTransformInstanceIds = new NestedDynamicArray<SourceIndex>(noOffsets, sourceIds);
                    return;
                }

                using (var tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.SceneObjects_Name, 0, SceneCount);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.SceneObjects_Name, tmp, 0, SceneCount);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref Name);
                }
                // Find the DontDestroyOnLoad Scene, which is always the last one unless someone changed the logic in the native NativeMemorySnapshot code.
                for (var i = SceneCount - 1; i >= 0; i--)
                {
                    if (Name[i] == DontDestroyOnLoadSceneName)
                    {
                        DontDestroyOnLoadSceneIndex = i;
                        break;
                    }
                }

                using (var tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.SceneObjects_Path, 0, SceneCount);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.SceneObjects_Path, tmp, 0, SceneCount);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref Path);
                }

                BuildIndex = reader.Read(EntryType.SceneObjects_BuildIndex, 0, SceneCount, Allocator.Persistent).Result.Reinterpret<int>();

                using (var tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.SceneObjects_AssetPath, 0, SceneCount);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.SceneObjects_AssetPath, tmp, 0, SceneCount);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref AssetPath);
                }

                SceneRootCount = reader.GetEntryCount(EntryType.SceneObjects_RootIds);
                RootCounts = reader.Read(EntryType.SceneObjects_RootIdCounts, 0, SceneCount, Allocator.Persistent).Result.Reinterpret<int>();
                RootOffsets = reader.Read(EntryType.SceneObjects_RootIdOffsets, 0, SceneCount, Allocator.Persistent).Result.Reinterpret<int>();

                if (reader.FormatVersion < FormatVersion.EntityIDAs8ByteStructs)
                {
                    // Read file has the old EntityId format
                    using var instanceIDInts = reader.Read(EntryType.SceneObjects_RootIds, 0, SceneRootCount, Allocator.Temp).Result.Reinterpret<int>();
                    // Clear the memory on alloc. The MemCpyStride in ConvertInstanceId won't initialize the blank spaces
                    m_AllSceneRootTransformInstanceIds = new DynamicArray<EntityId>(SceneRootCount, Allocator.Persistent, memClear: true);
                    instanceIDInts.ConvertInstanceIdIntsToEntityIds(ref m_AllSceneRootTransformInstanceIds);
                }
                else
                {
                    m_AllSceneRootTransformInstanceIds = reader.Read(EntryType.SceneObjects_RootIds, 0, SceneRootCount, Allocator.Persistent).Result.Reinterpret<EntityId>();
                }
            }

            public void Dispose()
            {
                SceneCount = 0;
                SceneRootCount = 0;
                PrefabRootCount = 0;
                PrefabTransformCount = 0;
                AssetPath = null;
                Name = null;
                Path = null;
                BuildIndex.Dispose();
                RootCounts.Dispose();
                RootOffsets.Dispose();
                if (SceneIndexedRootTransformInstanceIds.IsCreated)
                    SceneIndexedRootTransformInstanceIds.Dispose();
                else if (AllSceneRootTransformSourceIndices.IsCreated)
                    // Dispose only if it wasn't disposed as the backing data for SceneIndexedRootTransformInstanceIds
                    AllSceneRootTransformSourceIndices.Dispose();
                if (m_AllSceneRootTransformInstanceIds.IsCreated) // Normally already gets disposed during GenerateBaseData
                    m_AllSceneRootTransformInstanceIds.Dispose();
                AllPrefabRootTransformSourceIndices.Dispose();
                NativeObjectIndexToPrefabRootIndex.Dispose();
            }

            // TODO: Jobify, only needs
            // Connections.ReferenceTo
            // NativeObjects.NativeTypeArrayIndex
            // NativeObjects.Flags
            public void GenerateBaseData(CachedSnapshot snapshot, InstanceIdToNativeObjectIndex nativeObjectsInstanceId2Index, int gameObjectNativeTypeIndex)
            {
                AllPrefabRootTransformSourceIndices = new DynamicArray<SourceIndex>(0, 100, Allocator.Persistent);

                ProcessPrefabRoots(snapshot, gameObjectNativeTypeIndex);

                AllSceneRootTransformSourceIndices = new DynamicArray<SourceIndex>(SceneRootCount, Allocator.Persistent);
                {
                    for (long i = 0; i < SceneRootCount; i++)
                    {
                        var transformIndex = new SourceIndex(SourceIndex.SourceId.NativeObject, nativeObjectsInstanceId2Index[m_AllSceneRootTransformInstanceIds[i]]);
                        AllSceneRootTransformSourceIndices[i] = transformIndex;
                        var gameObjectIndex = ObjectConnection.GetGameObjectIndexFromTransformOrComponentIndex(snapshot, transformIndex, gameObjectNativeTypeIndex);
                        if (!snapshot.HasPrefabRootInfo)
                        {
                            // Mark these as roots if the info isn't there
                            snapshot.NativeObjects.Flags[transformIndex.Index] |= ObjectFlags.IsRoot;
                            if (gameObjectIndex.Valid) // with entities, it's possible to have a transform without a gameobject
                                snapshot.NativeObjects.Flags[gameObjectIndex.Index] |= ObjectFlags.IsRoot;
                        }
                    }
                    // Scene root transforms are now stored as Source Indices, so we can dispose of the loaded InstanceID/EntityId data
                    m_AllSceneRootTransformInstanceIds.Dispose();
                }
                if (!snapshot.HasSceneRootsAndAssetbundles || SceneCount == 0)
                {
                    NativeObjectIndexToPrefabRootIndex = new LongToLongHashMap(0, Allocator.Persistent);
                    return;
                }

                using var offsets = GetOffsetsForNestedSceneRoots(sizeof(SourceIndex), Allocator.Temp);

                SceneIndexedRootTransformInstanceIds = new NestedDynamicArray<SourceIndex>(offsets, AllSceneRootTransformSourceIndices);
            }

            DynamicArray<long> GetOffsetsForNestedSceneRoots(int sizeOfElements, Allocator allocator)
            {
                var offsets = new DynamicArray<long>(RootOffsets.Count + 1, allocator);
                for (long i = 0; i < RootOffsets.Count; i++)
                {
                    offsets[i] = RootOffsets[i] * sizeOfElements;
                }
                // Scene offsets are not reported as other nested arrays and are missing the total count added to the end
                offsets[RootOffsets.Count] = SceneRootCount * sizeOfElements;
                return offsets;
            }

            /// <summary>
            ///
            /// </summary>
            /// <param name="snapshot"></param>
            /// <param name="gameObjectNativeTypeIndex"></param>
            /// <param name="prefabObject"></param>
            /// <param name="nativeTypeInfo"></param>
            /// <param name="calculateAndFlagRecursively"> if false, only returns the root count,
            /// otherwise it includes the count of all child transforms recursively</param>
            /// <param name="addTempFlag"> if false, only returns the root count,
            /// otherwise it includes the count of all child transforms recursively</param>
            /// <returns></returns>
            long MarkPrefabRootFromPrefabAndCalculateHierarchySize(CachedSnapshot snapshot, int gameObjectNativeTypeIndex,
                SourceIndex prefabObject, in DynamicArrayRef<UnifiedType> nativeTypeInfo, ref NativeHashSet<SourceIndex> cachedHashset,
                bool calculateAndFlagRecursively = false, bool addTempFlag = false)
            {
                if (!snapshot.Connections.ReferenceTo.TryGetValue(prefabObject, out var refs))
                    return 0;
                var prefabTransformCount = 0L;
                var flagsToSet = ObjectFlags.IsRoot | (addTempFlag ? ObjectFlags.IsTemporarilyMarked : 0);
                foreach (var reference in refs)
                {
                    var transformReference = reference;
                    if (nativeTypeInfo[snapshot.NativeObjects.NativeTypeArrayIndex[reference.Index]].IsGameObjectType)
                    {
                        // redirect from GameObject reference to its Transform
                        transformReference = ObjectConnection.GetTransformIndexFromGameObject(snapshot, reference);
                    }
                    if (transformReference.Id is SourceIndex.SourceId.NativeObject
                        && snapshot.NativeObjects.Flags[transformReference.Index].HasFlag(ObjectFlags.IsPersistent)
                        && !snapshot.NativeObjects.Flags[transformReference.Index].HasFlag(ObjectFlags.IsTemporarilyMarked)
                        && nativeTypeInfo[snapshot.NativeObjects.NativeTypeArrayIndex[transformReference.Index]].IsTransformType)
                    {
                        AllPrefabRootTransformSourceIndices.Push(transformReference);

                        FlagTransformAndItsGameObject(snapshot, gameObjectNativeTypeIndex, transformReference, flagsToSet);

                        if (calculateAndFlagRecursively)
                        {
                            CountAndFlagChildTransforms(snapshot, gameObjectNativeTypeIndex, transformReference, default, addTempFlag, ref prefabTransformCount, ref cachedHashset);
                        }
                        else
                        {
                            ++prefabTransformCount;
                        }
                    }
                }
                return prefabTransformCount;
            }

            void CountAndFlagChildTransforms(CachedSnapshot snapshot, int gameObjectNativeTypeIndex, SourceIndex currentTransform, SourceIndex parentTransform, bool addTempFlag,
                ref long prefabTransformCount, ref NativeHashSet<SourceIndex> cachedHashset)
            {
                var stack = new DynamicArray<(SourceIndex child, SourceIndex parent)>(0, 10, Allocator.Temp);
                var flagsToSet = addTempFlag ? ObjectFlags.IsTemporarilyMarked : 0;

                stack.Push(new(currentTransform, parentTransform));

                while (stack.Count > 0)
                {
                    var (current, parent) = stack.Pop();
                    ++prefabTransformCount;

                    if (ObjectConnection.TryGetConnectedTransformIndicesFromTransformIndex(snapshot, current, parent, ref cachedHashset))
                    {
                        foreach (var reference in cachedHashset)
                        {
                            if (!snapshot.NativeObjects.Flags[reference.Index].HasFlag(ObjectFlags.IsTemporarilyMarked))
                            {
                                FlagTransformAndItsGameObject(snapshot, gameObjectNativeTypeIndex, reference, flagsToSet);
                                stack.Push(new(reference, current));
                            }
                        }
                    }
                }
            }

            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            void FlagTransformAndItsGameObject(CachedSnapshot snapshot, int gameObjectNativeTypeIndex, SourceIndex transform, ObjectFlags flagsToSet)
            {
                var gameObject = ObjectConnection.GetGameObjectIndexFromTransformOrComponentIndex(snapshot, transform, gameObjectNativeTypeIndex, isPersistent: true);

                snapshot.NativeObjects.Flags[transform.Index] |= flagsToSet;
                if (gameObject.Valid) // with entities, it's possible to have a transform without a gameobject
                    snapshot.NativeObjects.Flags[gameObject.Index] |= flagsToSet;
            }

            // TODO: Jobify, only needs
            // Connections.ReferenceTo
            // NativeTypes.IsTransformOrRectTransform (aka a check for Transform and RectTransform types
            // NativeObjects.NativeTypeArrayIndex
            // NativeObjects.Flags
            void ProcessPrefabRoots(CachedSnapshot snapshot, int gameObjectNativeTypeIndex)
            {
                ref readonly var nativeTypeInfo = ref snapshot.TypeDescriptions.UnifiedTypeInfoNative;
                // count the prefabs to avoid hashmap growth
                PrefabTransformCount = 0L;
                if (!snapshot.HasPrefabRootInfo)
                {
                    // if there is no root info for prefabs yet, build it here
                    var cachedHashSetForMarking = new NativeHashSet<SourceIndex>(20, Allocator.Temp);
                    try
                    {
                        if (snapshot.MetaData.IsEditorCapture && snapshot.NativeTypes.PrefabIdx != NativeTypeEntriesCache.InvalidTypeIndex)
                        {
                            // In the Editor, we can use the Prefab object to grab the roots
                            for (long i = 0; i < snapshot.NativeObjects.Count; i++)
                            {
                                if (snapshot.NativeObjects.NativeTypeArrayIndex[i] == snapshot.NativeTypes.PrefabIdx)
                                {
                                    MarkPrefabRootFromPrefabAndCalculateHierarchySize(snapshot, gameObjectNativeTypeIndex,
                                        new SourceIndex(SourceIndex.SourceId.NativeObject, i),
                                        in nativeTypeInfo, ref cachedHashSetForMarking, calculateAndFlagRecursively: true,
                                        // since we process all SceneObjects that are not tied to a Prefab afterwards,
                                        // we need to flag the transforms we found here to avoid double counting
                                        addTempFlag: true
                                        );
                                }
                            }
                        }

                        using var lastTwoTransforms = new DynamicArray<SourceIndex>(0, 2, Allocator.Temp);
                        for (long i = 0; i < snapshot.NativeObjects.Count; i++)
                        {
                            var flags = snapshot.NativeObjects.Flags[i];

                            if (flags.HasFlag(ObjectFlags.IsPersistent) && nativeTypeInfo[snapshot.NativeObjects.NativeTypeArrayIndex[i]].IsTransformType)
                            {
                                ++PrefabTransformCount;
                                // ignore this object if it already got marked by its last child
                                if (flags.HasFlag(ObjectFlags.IsRoot) || flags.HasFlag(ObjectFlags.IsTemporarilyMarked))
                                    continue;
                                lastTwoTransforms.Clear(false);
                                var thisTransform = new SourceIndex(SourceIndex.SourceId.NativeObject, i);
                                var currentTransform = thisTransform;

                                // In lieu of prefab roots being reported with the IsRoot flag, and while EntityId is 4 bytes in size there is a fallback mechanism
                                // Essentially, each Transform has a list of children and then its parent reported as its last connection.
                                // That means that by always recursively taking the last Transform referenced from our current Transform,
                                // we would eventually cycle between the root and its last child. The root has a lower absolut InstanceId.

                                // So, grab up to 2 potential parents
                                for (int j = 0; j < 2; j++)
                                {
                                    var potentialParent = ObjectConnection.GetParentTransformOrLastChild(snapshot, currentTransform, isPersistent: true);
                                    if (!potentialParent.Valid)
                                        break;
                                    lastTwoTransforms.Push(potentialParent);
                                    // was the parent already marked?
                                    if (snapshot.NativeObjects.Flags[potentialParent.Index].HasFlag(ObjectFlags.IsRoot))
                                        break;
                                    currentTransform = potentialParent;
                                }
                                switch (lastTwoTransforms.Count)
                                {
                                    case 0:
                                    {
                                        // no valid parent nor child makes this a lone Transform, and a root
                                        AllPrefabRootTransformSourceIndices.Push(thisTransform);
                                        FlagTransformAndItsGameObject(snapshot, gameObjectNativeTypeIndex, thisTransform, ObjectFlags.IsRoot);
                                        break;
                                    }
                                    case 1:
                                        // The parent was already flagged as root. Nothing to do here
                                        break;
                                    case 2:
                                        // If the second one isn't a reference back, thisTransform is not a root.
                                        // While lastTwoTransforms[0] _might_ be a root, we'll come across it down the line. Drop it for now.
                                        if (lastTwoTransforms[1] == thisTransform)
                                        {
                                            // one of these two is the root.
                                            var thisInstanceId = snapshot.NativeObjects.InstanceId[thisTransform.Index].ConvertToIdInt();
                                            var otherInstanceId = snapshot.NativeObjects.InstanceId[lastTwoTransforms[0].Index].ConvertToIdInt();
                                            // There is a chance that transforms lower down the line than the root where loaded in first
                                            // But since we're down to a coin toss, this is a best guess attempt that is likely to be correct in MOST cases
                                            // And in case it's wrong, the effect is only mildly confusing.
                                            var prefabRootTransform = Math.Abs(thisInstanceId) < Math.Abs(otherInstanceId) ? thisTransform : lastTwoTransforms[0];
                                            AllPrefabRootTransformSourceIndices.Push(prefabRootTransform);
                                            FlagTransformAndItsGameObject(snapshot, gameObjectNativeTypeIndex, prefabRootTransform, ObjectFlags.IsRoot);
                                        }
                                        break;
                                    default:
                                        Debug.LogError($"Found {lastTwoTransforms.Count} elements when looking for the root. The data is faulty.");
                                        break;
                                }
                            }
                        }

                    }
                    catch
                    {
                        // don't throw the exception explicitly but do a generic rethrow in order to not stomp the callstack
                        throw;
                    }
                    finally
                    {
                        cachedHashSetForMarking.Dispose();
                    }

                    // Clear the temp flags
                    for (long i = 0; i < snapshot.NativeObjects.Count; i++)
                    {
                        if (snapshot.NativeObjects.Flags[i].HasFlag(ObjectFlags.IsTemporarilyMarked))
                        {
                            snapshot.NativeObjects.Flags[i] &= ~ObjectFlags.IsTemporarilyMarked;
                        }
                    }
                }
                else
                {
                    // If roots have been reported, we still want to count the amount of prefab transforms and collect their roots here.
                    for (long i = 0; i < snapshot.NativeObjects.Count; i++)
                    {
                        if (snapshot.NativeObjects.Flags[i].HasFlag(ObjectFlags.IsPersistent) && nativeTypeInfo[snapshot.NativeObjects.NativeTypeArrayIndex[i]].IsTransformType)
                        {
                            ++PrefabTransformCount;
                            if (snapshot.NativeObjects.Flags[i].HasFlag(ObjectFlags.IsRoot))
                                AllPrefabRootTransformSourceIndices.Push(new SourceIndex(SourceIndex.SourceId.NativeObject, i));
                        }
                    }
                }

                var lookupCount = PrefabTransformCount * 2;
                NativeObjectIndexToPrefabRootIndex = new LongToLongHashMap((int)lookupCount, Allocator.Persistent);

                PrefabRootCount = AllPrefabRootTransformSourceIndices.Count;
                // Process the prefab hierarchies
                ProcessPrefabHierarchies(snapshot, AllPrefabRootTransformSourceIndices, gameObjectNativeTypeIndex);
            }

            // TODO: Jobify, only needs
            // Connections.ReferenceTo
            // NativeTypes.IsTransformOrRectTransform (aka a check for Transform and RectTransform types
            // NativeObjects.NativeTypeArrayIndex
            // NativeObjects.Flags
            public void ProcessPrefabHierarchies(CachedSnapshot snapshot, DynamicArray<SourceIndex> prefabRootTransforms, int gameObjectNativeTypeIndex)
            {
                var cachedHashSet = new NativeHashSet<SourceIndex>(100, Allocator.Temp);
                var currentDepthLayer = new DynamicArray<(SourceIndex current, SourceIndex parent, long prefabIndex)>(0, prefabRootTransforms.Count, Allocator.Temp);
                var nextDepthLayer = new DynamicArray<(SourceIndex current, SourceIndex parent, long prefabIndex)>(0, prefabRootTransforms.Count, Allocator.Temp);

                // Nested Prefabs might reference into the hierarchy of other prefabs (i.e. not just at their roots)
                // so we process these breadth first and ignore reoccurences as they would only happen for nested prefabs.

                try
                {
                    var prefabIndex = 0L;
                    foreach (var rootTransform in prefabRootTransforms)
                    {
                        currentDepthLayer.Push((rootTransform, default, prefabIndex++));
                    }
                    while (currentDepthLayer.Count > 0)
                    {
                        var (current, parent, prefabIdx) = currentDepthLayer.Pop();

                        if (NativeObjectIndexToPrefabRootIndex.TryAdd(current.Index, prefabIdx))
                        {
                            // Only add the GameObject and its children if the Transform was successfully added and not part of a different prefab
                            var gameObjectIndex =
                                ObjectConnection.GetGameObjectIndexFromTransformOrComponentIndex(snapshot, current,
                                    gameObjectNativeTypeIndex);
                            if (gameObjectIndex.Valid) // with entities, it's possible to have a transform without a GameObject
                            {
                                if (!NativeObjectIndexToPrefabRootIndex.TryAdd(gameObjectIndex.Index, prefabIdx))
                                {
                                    throw new InvalidDataException($"Failed to add prefab gameObjectIndex at index {gameObjectIndex.Index} to NativeObjectIndexToPrefabRootIndex map.");
                                }
                            }

                            if (ObjectConnection.TryGetConnectedTransformIndicesFromTransformIndex(snapshot, current,
                                    parent, ref cachedHashSet))
                            {
                                foreach (var child in cachedHashSet)
                                {
                                    // outright ignore nested prefab roots
                                    if (!snapshot.NativeObjects.Flags[child.Index].HasFlag(ObjectFlags.IsRoot))
                                        nextDepthLayer.Push((child, current, prefabIdx));
                                }
                            }
                        }
                        // If the current depth layer is empty, swap the layers
                        if (currentDepthLayer.Count <= 0)
                            (currentDepthLayer, nextDepthLayer) = (nextDepthLayer, currentDepthLayer);
                    }
                }
                finally
                {
                    currentDepthLayer.Dispose();
                    nextDepthLayer.Dispose();
                    cachedHashSet.Dispose();
                }
                return;
            }
        }

        /// <summary>
        /// A list of gfx resources and their connections to native root id.
        /// </summary>
        public class NativeGfxResourceReferenceEntriesCache : IDisposable
        {
            /// <summary>
            /// Count of active gfx resources.
            /// </summary>
            public long Count;
            /// <summary>
            /// Gfx resource identifiers.
            /// </summary>
            public DynamicArray<ulong> GfxResourceId = default;
            /// <summary>
            /// Size of the gfx resource in bytes.
            /// </summary>
            public DynamicArray<ulong> GfxSize = default;
            /// <summary>
            /// Related native rootId.
            /// Native roots information is present in NativeRootReferenceEntriesCache table.
            /// NativeRootReferenceEntriesCache.idToIndex allows to map RootId to the index in the NativeRootReferenceEntriesCache table and retrive
            /// all available information about the root such as name, ram usage, etc.
            /// The relation is Many-to-one - Multiple entires in NativeGfxResourceReferenceEntriesCache can point to the same native root.
            /// </summary>
            public DynamicArray<long> RootId = default;

            /// <summary>
            /// Use to retrieve related gfx allocations size for the specific RootId.
            /// This is a derived acceleration structure built on top of the table data above.
            /// </summary>
            public Dictionary<long, ulong> RootIdToGfxSize;

            public NativeGfxResourceReferenceEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.NativeGfxResourceReferences_Id);
                RootIdToGfxSize = new Dictionary<long, ulong>((int)Count);
                if (Count == 0)
                    return;

                GfxResourceId = reader.Read(EntryType.NativeGfxResourceReferences_Id, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                GfxSize = reader.Read(EntryType.NativeGfxResourceReferences_Size, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                RootId = reader.Read(EntryType.NativeGfxResourceReferences_RootId, 0, Count, Allocator.Persistent).Result.Reinterpret<long>();

                for (int i = 0; i < Count; ++i)
                {
                    var id = RootId[i];
                    var gfxSize = GfxSize[i];

                    if (RootIdToGfxSize.TryGetValue(id, out var size))
                        RootIdToGfxSize[id] = size + gfxSize;
                    else
                        RootIdToGfxSize.Add(id, gfxSize);
                }
            }

            public void Dispose()
            {
                Count = 0;
                GfxResourceId.Dispose();
                GfxSize.Dispose();
                RootId.Dispose();
                RootIdToGfxSize = null;
            }
        }

        /// <summary>
        /// A table of all allocators which Unity uses to manage memory allocations in native code.
        /// All size values are in bytes.
        /// </summary>
        public class NativeAllocatorEntriesCache : IDisposable
        {
            /// <summary>
            /// Count of allocators.
            /// </summary>
            public long Count;
            /// <summary>
            /// Name of allocator.
            /// </summary>
            public string[] AllocatorName;
            /// <summary>
            /// Memory which was requested by Unity native systems from the allocator and is being used to store data.
            /// </summary>
            public DynamicArray<ulong> UsedSize = default;
            /// <summary>
            /// Total memory that was requested by allocator from System.
            /// May be larger than UsedSize to utilize pooling approach.
            /// </summary>
            public DynamicArray<ulong> ReservedSize = default;
            /// <summary>
            /// Total size of memory dedicated to allocations tracking.
            /// </summary>
            public DynamicArray<ulong> OverheadSize = default;
            /// <summary>
            /// Maximum amount of memory allocated with this allocator since app start.
            /// </summary>
            public DynamicArray<ulong> PeakUsedSize = default;
            /// <summary>
            /// Allocations count made via the specific allocator.
            /// </summary>
            public DynamicArray<ulong> AllocationCount = default;

            public NativeAllocatorEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.NativeAllocatorInfo_AllocatorName);
                AllocatorName = new string[Count];

                if (Count == 0)
                    return;

                UsedSize = reader.Read(EntryType.NativeAllocatorInfo_UsedSize, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                ReservedSize = reader.Read(EntryType.NativeAllocatorInfo_ReservedSize, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                OverheadSize = reader.Read(EntryType.NativeAllocatorInfo_OverheadSize, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                PeakUsedSize = reader.Read(EntryType.NativeAllocatorInfo_PeakUsedSize, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                AllocationCount = reader.Read(EntryType.NativeAllocatorInfo_AllocationCount, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();

                using (DynamicArray<byte> tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.NativeAllocatorInfo_AllocatorName, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.NativeAllocatorInfo_AllocatorName, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref AllocatorName);
                }
            }

            public void Dispose()
            {
                Count = 0;
                AllocatorName = null;
                UsedSize.Dispose();
                ReservedSize.Dispose();
                OverheadSize.Dispose();
                PeakUsedSize.Dispose();
                AllocationCount.Dispose();
            }
        }

        public class NativeObjectEntriesCache : IDisposable
        {
            public static readonly EntityId InstanceIDNone = EntityId.None;
            public const long FirstValidObjectIndex = 0;
            public const long InvalidObjectIndex = -1;

            /// <summary>
            /// This flag only exists in Unity's native code where it is used
            /// to prevent destruction of native objects via Destroy or DestroyImmediately.
            /// </summary>
            public const HideFlags NativeDontAllowDestructionFlag = (HideFlags)(1 << 6);

            public long Count;
            public string[] ObjectName;
            public DynamicArray<EntityId> InstanceId = default;
            public DynamicArray<ulong> Size = default;
            public DynamicArray<int> NativeTypeArrayIndex = default;
            public DynamicArray<HideFlags> HideFlags = default;
            public DynamicArray<ObjectFlags> Flags = default;
            public DynamicArray<ulong> NativeObjectAddress = default;
            public DynamicArray<long> RootReferenceId = default;
            public DynamicArray<int> ManagedObjectIndex = default;

            //secondary data
            public DynamicArray<int> RefCount = default;
            public AddressToInstanceId NativeObjectAddressToEntityId { private set; get; }

            public LongToIntHashMap RootReferenceIdToIndex { private set; get; }
            public LongToLongHashMap GCHandleIndexToIndex { private set; get; }
            public InstanceIdToNativeObjectIndex InstanceId2Index;
            public DynamicArray<SourceIndex> AssetBundles = default;

            public readonly ulong TotalSizes = 0ul;
            DynamicArray<int> MetaDataBufferIndicies = default;
            NestedDynamicArray<byte> MetaDataBuffers => m_MetaDataBuffersReadOp.CompleteReadAndGetNestedResults();
            NestedDynamicSizedArrayReadOperation<byte> m_MetaDataBuffersReadOp;

            unsafe public NativeObjectEntriesCache(ref IFileReader reader, int assetBundleTypeIndex)
            {
                Count = reader.GetEntryCount(EntryType.NativeObjects_InstanceId);
                NativeObjectAddressToEntityId = new AddressToInstanceId((int)Count, Allocator.Persistent);
                RootReferenceIdToIndex = new LongToIntHashMap((int)Count, Allocator.Persistent);
                GCHandleIndexToIndex = new LongToLongHashMap((int)Count, Allocator.Persistent);
                InstanceId2Index = new InstanceIdToNativeObjectIndex((int)Count, Allocator.Persistent);
                ObjectName = new string[Count];
                // AssetBundle usage might vary or not even be used at all, so preallocate with a conservative capacity of 10
                AssetBundles = new DynamicArray<SourceIndex>(0, 10, Allocator.Persistent);

                if (Count == 0)
                    return;

                if (reader.FormatVersion < FormatVersion.EntityIDAs8ByteStructs)
                {
                    using var instanceIDs = reader.Read(EntryType.NativeObjects_InstanceId, 0, Count, Allocator.Temp).Result.Reinterpret<int>();
                    // Clear the memory on alloc. The MemCpyStride in ConvertInstanceId won't initialize the blank spaces
                    InstanceId = new DynamicArray<EntityId>(Count, Allocator.Persistent, memClear: true);
                    instanceIDs.ConvertInstanceIdIntsToEntityIds(ref InstanceId);
                }
                else
                {
                    InstanceId = reader.Read(EntryType.NativeObjects_InstanceId, 0, Count, Allocator.Persistent).Result.Reinterpret<EntityId>();
                }
                Size = reader.Read(EntryType.NativeObjects_Size, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                NativeTypeArrayIndex = reader.Read(EntryType.NativeObjects_NativeTypeArrayIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                HideFlags = reader.Read(EntryType.NativeObjects_HideFlags, 0, Count, Allocator.Persistent).Result.Reinterpret<HideFlags>();
                Flags = reader.Read(EntryType.NativeObjects_Flags, 0, Count, Allocator.Persistent).Result.Reinterpret<ObjectFlags>();
                NativeObjectAddress = reader.Read(EntryType.NativeObjects_NativeObjectAddress, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                RootReferenceId = reader.Read(EntryType.NativeObjects_RootReferenceId, 0, Count, Allocator.Persistent).Result.Reinterpret<long>();
                ManagedObjectIndex = reader.Read(EntryType.NativeObjects_GCHandleIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                RefCount = new DynamicArray<int>(Count, Allocator.Persistent, true);

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
                    NativeObjectAddressToEntityId.Add(NativeObjectAddress[i], id);
                    RootReferenceIdToIndex.Add(RootReferenceId[i], (int)i);
                    InstanceId2Index[id] = (int)i;
                    TotalSizes += Size[i];

                    // While we're iterating over all objects anyways, collect all AssetBundle objects as they represent a special type of root.
                    // This info is then used later for building Path From Root info as well as listing out AssetBundles as roots.
                    if (NativeTypeArrayIndex[i] == assetBundleTypeIndex && assetBundleTypeIndex != NativeTypeEntriesCache.InvalidTypeIndex)
                    {
                        AssetBundles.Push(new SourceIndex(SourceIndex.SourceId.NativeObject, i));
                    }
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
                else
                {
                    for (int i = 0; i < Count; ++i)
                        GCHandleIndexToIndex.TryAdd(ManagedObjectIndex[i], i);
                    // If an invalid entry was added, remove it
                    GCHandleIndexToIndex.Remove(-1);
                }

                // handle formats tht have the new metadata added for native objects
                if (reader.FormatVersion >= FormatVersion.NativeObjectMetaDataVersion)
                {
                    //get the array that tells us how to index the buffers for the actual meta data
                    MetaDataBufferIndicies = reader.Read(EntryType.ObjectMetaData_MetaDataBufferIndicies, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                    // loop the array and get the total number of entries we need
                    int sum = 0;
                    for (int i = 0; i < MetaDataBufferIndicies.Count; i++)
                    {
                        if (MetaDataBufferIndicies[i] != -1)
                            sum++;
                    }

                    m_MetaDataBuffersReadOp = reader.AsyncReadDynamicSizedArray<byte>(EntryType.ObjectMetaData_MetaDataBuffer, 0, sum, Allocator.Persistent);
                }
            }

            public ILongIndexedContainer<byte> MetaData(long nativeObjectIndex)
            {
                if (MetaDataBufferIndicies.Count == 0) return default;
                var bufferIndex = MetaDataBufferIndicies[nativeObjectIndex];
                if (bufferIndex == -1) return default(DynamicArrayRef<byte>);

                return MetaDataBuffers[bufferIndex];
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
                RefCount.Dispose();
                AssetBundles.Dispose();
                ObjectName = null;
                NativeObjectAddressToEntityId.Dispose();
                RootReferenceIdToIndex.Dispose();
                GCHandleIndexToIndex.Dispose();
                InstanceId2Index.Dispose();
                MetaDataBufferIndicies.Dispose();
                if (m_MetaDataBuffersReadOp.IsCreated)
                {
                    // Dispose the read operation first to abort it ...
                    m_MetaDataBuffersReadOp.Dispose();
                    // ... before disposing the result, as otherwise we'd sync on a pending read op.
                    MetaDataBuffers.Dispose();
                    m_MetaDataBuffersReadOp = default;
                }
            }
        }

        public class SystemMemoryRegionEntriesCache : IDisposable
        {
            public enum MemoryType : ushort
            {
                // NB!: The same as in SystemInfoMemory.h
                Private = 0, // Private to this process allocations
                Mapped = 1,  // Allocations mapped to a file (dll/exe/etc)
                Shared = 2,  // Shared memory
                Device = 3,   // Shared device or driver memory (like GPU, sound cards, etc)
                Count
            }

            public long Count;
            public string[] RegionName;
            public DynamicArray<ulong> RegionAddress = default;
            public DynamicArray<ulong> RegionSize = default;
            public DynamicArray<ulong> RegionResident = default;
            public DynamicArray<ushort> RegionType = default;

            public SystemMemoryRegionEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.SystemMemoryRegions_Address);
                RegionName = new string[Count];

                if (Count == 0)
                    return;

                RegionAddress = reader.Read(EntryType.SystemMemoryRegions_Address, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                RegionSize = reader.Read(EntryType.SystemMemoryRegions_Size, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                RegionResident = reader.Read(EntryType.SystemMemoryRegions_Resident, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                RegionType = reader.Read(EntryType.SystemMemoryRegions_Type, 0, Count, Allocator.Persistent).Result.Reinterpret<ushort>();

                using (DynamicArray<byte> tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.SystemMemoryRegions_Name, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.SystemMemoryRegions_Name, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref RegionName);
                }
            }

            public void Dispose()
            {
                Count = 0;
                RegionName = null;
                RegionAddress.Dispose();
                RegionSize.Dispose();
                RegionResident.Dispose();
                RegionType.Dispose();
            }
        }

        public class SystemMemoryResidentPagesEntriesCache : IDisposable
        {
            public readonly long Count;
            public readonly DynamicArray<ulong> RegionAddress;
            public readonly DynamicArray<int> RegionStartPageIndex;
            public readonly DynamicArray<int> RegionEndPageIndex;
            public readonly BitArray PageStates;
            public readonly UInt32 PageSize;

            public SystemMemoryResidentPagesEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.SystemMemoryResidentPages_Address);
                if (Count == 0)
                {
                    PageSize = 0;
                    PageStates = new BitArray(0);
                    RegionAddress = default;
                    RegionStartPageIndex = default;
                    RegionEndPageIndex = default;
                    return;
                }

                RegionAddress = reader.Read(EntryType.SystemMemoryResidentPages_Address, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                RegionStartPageIndex = reader.Read(EntryType.SystemMemoryResidentPages_FirstPageIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                RegionEndPageIndex = reader.Read(EntryType.SystemMemoryResidentPages_LastPageIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();

                var tempPageStates = new BitArray[1];
                using (DynamicArray<byte> tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.SystemMemoryResidentPages_PagesState, 0, 1);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.SystemMemoryResidentPages_PagesState, tmp, 0, 1);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref tempPageStates);
                    PageStates = tempPageStates[0];
                }

                unsafe
                {
                    UInt32 tempPageSize = 0;
                    byte* tempPageSizePtr = (byte*)&tempPageSize;
                    reader.ReadUnsafe(EntryType.SystemMemoryResidentPages_PageSize, tempPageSizePtr, sizeof(UInt32), 0, 1);
                    PageSize = tempPageSize;
                }
            }

            public void Dispose()
            {
                RegionAddress.Dispose();
                RegionStartPageIndex.Dispose();
                RegionEndPageIndex.Dispose();
            }

            public ulong CalculateResidentMemory(CachedSnapshot snapshot, long regionIndex, ulong address, ulong size, SourceIndex.SourceId sourceId)
            {
                if ((Count == 0) || (size == 0))
                    return 0;

                var regionAddress = RegionAddress[regionIndex];
                var firstPageIndex = RegionStartPageIndex[regionIndex];
                var lastPageIndex = RegionEndPageIndex[regionIndex];

                // Calculate first and last page index in bitset PageState
                var addrDelta = address - regionAddress;
                var begPage = (int)(addrDelta / PageSize) + firstPageIndex;
                var endPage = (int)((addrDelta + size - 1) / PageSize) + firstPageIndex;
                if ((begPage < firstPageIndex) || (endPage > lastPageIndex))
                {
                    // FIXME: Ignore the log on Unity 6 and OSX for now to avoid unstable tests. This is being investigated
                    if (snapshot.MetaData.UnityVersionMajor <= 2023 ||
                        !(snapshot.MetaData.TargetInfo is { RuntimePlatform: RuntimePlatform.OSXEditor } || snapshot.MetaData.TargetInfo is { RuntimePlatform: RuntimePlatform.OSXPlayer }))
                        Debug.LogAssertion($"Page range is outside of system region range. Please report a bug! (Source: {sourceId}, regionIndex: {regionIndex}, regionAddress: {regionAddress}, address: {address}, addrDelta: {addrDelta}, begPage: {begPage}, firstPageIndex: {firstPageIndex}, endPage: {endPage}, lastPageIndex: {lastPageIndex})");
                    return 0;
                }

                // Sum total for all pages in range
                ulong residentSize = 0;
                for (var p = begPage; p <= endPage; p++)
                {
                    if (PageStates[p])
                        residentSize += PageSize;
                }

                // As address might be not aligned, we need to subtract
                // difference for the first and the last page
                if (PageStates[begPage])
                {
                    var head = address % PageSize;
                    residentSize -= head;
                }

                if (PageStates[endPage])
                {
                    var tail = (address + size) % PageSize;
                    if (tail > 0)
                        residentSize -= PageSize - tail;
                }

                return residentSize;
            }
        }

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

        //leave this as second to last thing to convert, also a major pain in the ass
        public class TypeDescriptionEntriesCache : IDisposable
        {
            public const int ITypeInvalid = -1;
            public const long UnifiedTypeArrayManagedSection = 0;
            public const long UnifiedTypeArrayNativeSection = 1;
            const int k_DefaultFieldProcessingBufferSize = 64;
            public const string UnityObjectTypeName = "UnityEngine.Object";
            public const string UnityNativeObjectPointerFieldName = "m_CachedPtr";
            public const string NativeCollectionsNamspaceAndTypePrefix = "Unity.Collections.Native";
            public int IFieldUnityObjectMCachedPtr { get; private set; }
            public int IFieldUnityObjectMCachedPtrOffset { get; private set; } = -1;

            const string k_UnityMonoBehaviourTypeName = "UnityEngine.MonoBehaviour";
            const string k_UnityScriptableObjectTypeName = "UnityEngine.ScriptableObject";
            const string k_UnityComponentObjectTypeName = "UnityEngine.Component";
            const string k_UnityGameObjectTypeName = "UnityEngine.GameObject";
            const string k_UnityTransformTypeName = "UnityEngine.Transform";
            const string k_UnityRectTransformTypeName = "UnityEngine.RectTransform";
            const string k_UnityEditorEditorTypeName = "UnityEditor.Editor";

            const string k_SystemObjectTypeName = "System.Object";
            const string k_SystemValueTypeName = "System.ValueType";
            const string k_SystemEnumTypeName = "System.Enum";

            const string k_SystemInt16Name = "System.Int16";
            const string k_SystemInt32Name = "System.Int32";
            const string k_SystemInt64Name = "System.Int64";

            const string k_SystemUInt16Name = "System.UInt16";
            const string k_SystemUInt32Name = "System.UInt32";

            const string k_SystemUInt64Name = "System.UInt64";
            const string k_SystemBoolName = "System.Boolean";
            const string k_SystemCharTypeName = "System.Char";
            const string k_SystemCharArrayTypeName = "System.Char[]";
            const string k_SystemDoubleName = "System.Double";
            const string k_SystemSingleName = "System.Single";
            const string k_SystemStringName = "System.String";
            const string k_SystemIntPtrName = "System.IntPtr";
            const string k_SystemVoidPtrName = "System.Void*";
            const string k_SystemBytePtrName = "System.Byte*";
            const string k_SystemByteName = "System.Byte";

            public int Count;
            public DynamicArray<TypeFlags> Flags = default;
            public DynamicArray<int> BaseOrElementTypeIndex = default;

            /// <summary>
            /// Type size, which for value types does not include a header that would be added if they get boxed onto the heap.
            /// For boxed value types (i.e. <seealso cref="HasFlag(int, TypeFlags)"/> for flag <seealso cref="TypeFlags.kValueType"/> is true)
            /// on the heap (rather than e.g. non-boxed value types) you'll need to add
            /// <seealso cref="VirtualMachineInformation.ObjectHeaderSize"/>.
            /// You can also use <see cref="GetMinSizeForHeapObjectOfType"/> instead of checking manually if you need to add the header size
            /// </summary>
            public DynamicArray<int> Size = default;
            /// <summary>
            /// Get's the size an object of this type would have on the heap.
            /// This method only returns minimal size for strings or arrays instances, as their size depends on their length
            /// and their full size can't be determined for all instances of their type, but only for a concrete object.
            /// For all other types, it returns the definitive size on the heap.
            ///
            /// For the full size of strings use <see cref="ManagedHeapArrayDataTools.ReadArrayObjectSizeInBytes"/> instead.
            /// For the full size of arrays use <see cref="StringTools.ReadStringObjectSizeInBytes"/ instead>.
            /// </summary>
            /// <param name="typeIndex"></param>
            /// <param name="vmInfo"></param>
            /// <returns></returns>
            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            public int GetMinSizeForHeapObjectOfType(long typeIndex, in VirtualMachineInformation vmInfo)
                => Size[typeIndex] + (HasFlag(typeIndex, TypeFlags.kValueType) ? (int)vmInfo.ObjectHeaderSize : 0);

            public DynamicArray<ulong> TypeInfoAddress = default;
            enum TypeCategory
            {
                NotChecked = 0,
                Concrete,
                AbstractInterface,
                AbstractGeneric,
                IgnoreForHeapObjectTypeChecks,
            }
            DynamicArray<TypeCategory> m_TypeCategory = default;
            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            public bool IsConcrete(long typeIndex) => m_TypeCategory[typeIndex] == TypeCategory.Concrete;
            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            public bool IgnoreForHeapObjectTypeChecks(long typeIndex) => m_TypeCategory[typeIndex] == TypeCategory.IgnoreForHeapObjectTypeChecks;

            public string[] TypeDescriptionName;
            public string[] Assembly;

            public NestedDynamicArray<int> FieldIndices => m_FieldIndicesReadOp.CompleteReadAndGetNestedResults();
            NestedDynamicSizedArrayReadOperation<int> m_FieldIndicesReadOp;
            public NestedDynamicArray<byte> StaticFieldBytes => m_StaticFieldBytesReadOp.CompleteReadAndGetNestedResults();
            NestedDynamicSizedArrayReadOperation<byte> m_StaticFieldBytesReadOp;

            //secondary data, handled inside InitSecondaryItems
            // TODO: move over to NestedDynamicArray<int>
            public int[][] FieldIndicesInstance;//includes all bases' instance fields
            public int[][] fieldIndicesStatic;  //includes all bases' static fields
            public int[][] fieldIndicesOwnedStatic;  //includes only type's static fields

            public readonly int ITypeValueType = ITypeInvalid;
            public readonly int ITypeUnityObject = ITypeInvalid;
            public readonly int ITypeObject = ITypeInvalid;
            public readonly int ITypeEnum = ITypeInvalid;
            public readonly int ITypeInt16 = ITypeInvalid;
            public readonly int ITypeInt32 = ITypeInvalid;
            public readonly int ITypeInt64 = ITypeInvalid;
            public readonly int ITypeUInt16 = ITypeInvalid;
            public readonly int ITypeUInt32 = ITypeInvalid;
            public readonly int ITypeUInt64 = ITypeInvalid;
            public readonly int ITypeBool = ITypeInvalid;
            public readonly int ITypeChar = ITypeInvalid;
            public readonly int ITypeCharArray = ITypeInvalid;
            public readonly int ITypeDouble = ITypeInvalid;
            public readonly int ITypeSingle = ITypeInvalid;
            public readonly int ITypeString = ITypeInvalid;
            public readonly int ITypeIntPtr = ITypeInvalid;
            public readonly int ITypeVoidPtr = ITypeInvalid;
            public readonly int ITypeBytePtr = ITypeInvalid;
            public readonly int ITypeByte = ITypeInvalid;

            public readonly int ITypeUnityMonoBehaviour = ITypeInvalid;
            public readonly int ITypeUnityScriptableObject = ITypeInvalid;
            public readonly int ITypeUnityComponent = ITypeInvalid;
            public readonly int ITypeUnityGameObject = ITypeInvalid;
            public readonly int ITypeUnityTransform = ITypeInvalid;
            public readonly int ITypeUnityRectTransform = ITypeInvalid;
            public readonly int ITypeUnityEditorEditor = ITypeInvalid;
            public AddressToIntIndex TypeInfoToArrayIndex { get; private set; }

            // Well these also include native types so making them part of the managed type descriptions isn't that
            // clean but there isn't a very clean alternative right now so, good enough as any place. Consider moving...
            readonly NestedDynamicArray<UnifiedType> m_UnifiedTypeInfo = default;
            /// <summary>
            /// Only fully initialized after the Managed Crawler is done stitching up Objects.
            /// </summary>
            public ref readonly NestedDynamicArray<UnifiedType> UnifiedTypeInfo => ref m_UnifiedTypeInfo;
            public ref readonly DynamicArrayRef<UnifiedType> UnifiedTypeInfoManaged => ref m_UnifiedTypeInfo[UnifiedTypeArrayManagedSection];
            public ref readonly DynamicArrayRef<UnifiedType> UnifiedTypeInfoNative => ref m_UnifiedTypeInfo[UnifiedTypeArrayNativeSection];
            public NativeHashSet<int> PureCSharpTypeIndices { get; private set; }

            readonly NativeHashSet<int> m_UninstantiatableUnityBaseTypesWithSetNativeType = default;
            /// <summary>
            /// AKA: UnityEngine.Object & UnityEngine.Component
            /// If an object where a Scriptable type, its fallback would be set as <see cref="UninstantiatableUnityBaseTypesWithScriptingDefinedTypes"/> after
            /// <see cref="TypeDescriptionEntriesCache"/> has been constructed and <see cref="TypeDescriptionEntriesCache.InitSecondaryItems(FieldDescriptionEntriesCache, NativeTypeEntriesCache, VirtualMachineInformation, Dictionary{string, int})"/> called.
            /// </summary>
            public ref readonly NativeHashSet<int> UninstantiatableUnityBaseTypesWithSetNativeType => ref m_UninstantiatableUnityBaseTypesWithSetNativeType;

            readonly NativeHashSet<int> m_UninstantiatableUnityBaseTypesWithScriptingDefinedTypes = default;
            /// <summary>
            /// AKA: ScriptableObjects and MonoBehaviour
            /// </summary>
            public ref readonly NativeHashSet<int> UninstantiatableUnityBaseTypesWithScriptingDefinedTypes => ref m_UninstantiatableUnityBaseTypesWithScriptingDefinedTypes;

            readonly NativeHashSet<int> m_AllUninstantiatableUnityBaseTypes = default;
            /// <summary>
            /// A combined hashset of both <see cref="UninstantiatableUnityBaseTypesWithSetNativeType"/> and <see cref="UninstantiatableUnityBaseTypesWithScriptingDefinedTypes"/>.
            /// </summary>
            public ref readonly NativeHashSet<int> AllUninstantiatableUnityBaseTypes => ref m_AllUninstantiatableUnityBaseTypes;

            public TypeDescriptionEntriesCache(ref IFileReader reader, FieldDescriptionEntriesCache fieldDescriptions, NativeTypeEntriesCache nativeTypes, VirtualMachineInformation vmInfo)
            {
                Count = (int)reader.GetEntryCount(EntryType.TypeDescriptions_TypeIndex);

                TypeDescriptionName = new string[Count];
                Assembly = new string[Count];

                // granted, that capacity is more than likely too much but it beats reallocs. It's trimmed later.
                PureCSharpTypeIndices = new NativeHashSet<int>((int)Math.Max(0, Count - nativeTypes.Count), Allocator.Persistent);

                m_TypeCategory = new DynamicArray<TypeCategory>(Count, Allocator.Persistent, true);
                var unifiedTypeInfoBackingMemory = new DynamicArray<UnifiedType>(Count + nativeTypes.Count, Allocator.Persistent, false);
                var nativeTypeIndex = 0;
                for (long i = Count; i < unifiedTypeInfoBackingMemory.Count; i++, nativeTypeIndex++)
                {
                    unifiedTypeInfoBackingMemory[i] = new UnifiedType(nativeTypes, null, nativeTypeIndex);
                }
                for (long i = 0; i < Count; i++)
                {
                    unifiedTypeInfoBackingMemory[i] = new UnifiedType(nativeTypes, null, CachedSnapshot.NativeTypeEntriesCache.InvalidTypeIndex, (int)i);
                }
                using var unifiedTypeOffsets = new DynamicArray<long>(3, Allocator.Temp);
                var typeInfoSize = 0L;
                unsafe
                {
                    typeInfoSize = sizeof(UnifiedType);
                }
                unifiedTypeOffsets[UnifiedTypeArrayManagedSection] = 0;
                unifiedTypeOffsets[UnifiedTypeArrayNativeSection] = Count * typeInfoSize;
                unifiedTypeOffsets[2] = unifiedTypeInfoBackingMemory.Count * typeInfoSize;
                m_UnifiedTypeInfo = new NestedDynamicArray<UnifiedType>(unifiedTypeOffsets, unifiedTypeInfoBackingMemory);

                if (Count == 0)
                {
                    Flags = new DynamicArray<TypeFlags>(0, Allocator.Persistent);
                    BaseOrElementTypeIndex = new DynamicArray<int>(0, Allocator.Persistent);
                    Size = new DynamicArray<int>(0, Allocator.Persistent);
                    TypeInfoAddress = new DynamicArray<ulong>(0, Allocator.Persistent);
                    TypeInfoToArrayIndex = new AddressToIntIndex(0, Allocator.Persistent);
                    return;
                }

                Flags = reader.Read(EntryType.TypeDescriptions_Flags, 0, Count, Allocator.Persistent).Result.Reinterpret<TypeFlags>();
                BaseOrElementTypeIndex = reader.Read(EntryType.TypeDescriptions_BaseOrElementTypeIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                Size = reader.Read(EntryType.TypeDescriptions_Size, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                TypeInfoAddress = reader.Read(EntryType.TypeDescriptions_TypeInfoAddress, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
#if DEBUG_VALIDATION
                if (reader.FormatVersion == FormatVersion.SnapshotMinSupportedFormatVersion)
                {
                    // Nb! This code is left here for posterity in case anyone wonders what EntryType.TypeDescriptions_TypeIndex is, and if it is needed. No it is not.

                    // After thorough archeological digging, there seems to be no evidence that this array was ever needed
                    // At the latest after FormatVersion.StreamingManagedMemoryCaptureFormatVersion (9) it is definitely not needed
                    // as the indices reported in this map exactly to the indices in the array

                    var TypeIndex = reader.Read(EntryType.TypeDescriptions_TypeIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                    for (int i = 0; i < TypeIndex.Count; i++)
                    {
                        if (i != TypeIndex[i])
                        {
                            Debug.LogError("Attempted to load a broken Snapshot file from an ancient Unity version!");
                            break;
                        }
                    }
                }
#endif

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
                }

                m_FieldIndicesReadOp = reader.AsyncReadDynamicSizedArray<int>(EntryType.TypeDescriptions_FieldIndices, 0, Count, Allocator.Persistent);

                m_StaticFieldBytesReadOp = reader.AsyncReadDynamicSizedArray<byte>(EntryType.TypeDescriptions_StaticFieldBytes, 0, Count, Allocator.Persistent);


                var typeNameToIndex = new Dictionary<string, int>();
                for (int i = 0; i < Count; ++i)
                {
                    typeNameToIndex[TypeDescriptionName[i]] = i;
                }

                typeNameToIndex.GetOrInitializeValue(UnityObjectTypeName, out ITypeUnityObject, ITypeInvalid);
#if DEBUG_VALIDATION //This shouldn't really happen
                if (ITypeUnityObject == ITypeInvalid)
                {
                    throw new Exception("Unable to find UnityEngine.Object");
                }
#endif
                typeNameToIndex.GetOrInitializeValue(k_SystemValueTypeName, out ITypeValueType, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemObjectTypeName, out ITypeObject, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemEnumTypeName, out ITypeEnum, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemCharTypeName, out ITypeChar, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemCharArrayTypeName, out ITypeCharArray, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemInt16Name, out ITypeInt16, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemInt32Name, out ITypeInt32, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemInt64Name, out ITypeInt64, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemIntPtrName, out ITypeIntPtr, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemVoidPtrName, out ITypeVoidPtr, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemBytePtrName, out ITypeBytePtr, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemStringName, out ITypeString, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemBoolName, out ITypeBool, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemSingleName, out ITypeSingle, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemByteName, out ITypeByte, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemDoubleName, out ITypeDouble, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemUInt16Name, out ITypeUInt16, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemUInt32Name, out ITypeUInt32, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_SystemUInt64Name, out ITypeUInt64, ITypeInvalid);

                typeNameToIndex.GetOrInitializeValue(k_UnityMonoBehaviourTypeName, out ITypeUnityMonoBehaviour, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_UnityScriptableObjectTypeName, out ITypeUnityScriptableObject, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_UnityComponentObjectTypeName, out ITypeUnityComponent, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_UnityGameObjectTypeName, out ITypeUnityGameObject, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_UnityTransformTypeName, out ITypeUnityTransform, ITypeInvalid);
                typeNameToIndex.GetOrInitializeValue(k_UnityRectTransformTypeName, out ITypeUnityRectTransform, ITypeInvalid);

                typeNameToIndex.GetOrInitializeValue(k_UnityEditorEditorTypeName, out ITypeUnityEditorEditor, ITypeInvalid);

                m_UninstantiatableUnityBaseTypesWithScriptingDefinedTypes = new NativeHashSet<int>(3, Allocator.Persistent);

                // There will never be instances of objects in the snapshot that have these key Unity native types as
                // while their managed type would be that of the corresponding managed base type
                // (i.e. UnityEngine.ScriptableObject/MonoBehaviour) as only their derived type can be instantiated.
                // As such, the managed types mapped to these native types are also always the managed base types.
                if (ITypeUnityScriptableObject is not ITypeInvalid)
                {
                    UpdateManagedToNativeTypeMapping(nativeTypes, ITypeUnityScriptableObject, nativeTypes.ScriptableObjectIdx, ref m_UninstantiatableUnityBaseTypesWithScriptingDefinedTypes, baseFallbackType: ITypeUnityScriptableObject);
                    // Not actually a discrete managed type so this shouldn't update the managed base type info for ITypeUnityScriptableObject, only the native info
                    UpdateManagedToNativeTypeMapping(nativeTypes, ITypeUnityScriptableObject, nativeTypes.EditorScriptableObjectIdx, baseFallbackType: ITypeUnityScriptableObject);
                }
                if (ITypeUnityMonoBehaviour is not ITypeInvalid)
                    UpdateManagedToNativeTypeMapping(nativeTypes, ITypeUnityMonoBehaviour, nativeTypes.MonoBehaviourIdx, ref m_UninstantiatableUnityBaseTypesWithScriptingDefinedTypes, baseFallbackType: ITypeUnityMonoBehaviour);

                m_UninstantiatableUnityBaseTypesWithSetNativeType = new NativeHashSet<int>(2, Allocator.Persistent);

                if (ITypeUnityComponent is not ITypeInvalid)
                    UpdateManagedToNativeTypeMapping(nativeTypes, ITypeUnityComponent, nativeTypes.ComponentIdx, ref m_UninstantiatableUnityBaseTypesWithSetNativeType, baseFallbackType: ITypeUnityComponent);

                if (ITypeUnityObject is not ITypeInvalid)
                {
                    UpdateManagedToNativeTypeMapping(nativeTypes, ITypeUnityObject, nativeTypes.BaseObjectIdx, ref m_UninstantiatableUnityBaseTypesWithSetNativeType, baseFallbackType: ITypeUnityObject);
                    // Not actually a discrete managed type so this shouldn't update the managed base type info for ITypeUnityComponent, only the native info
                    UpdateManagedToNativeTypeMapping(nativeTypes, ITypeUnityObject, nativeTypes.NamedObjectIdx, baseFallbackType: ITypeUnityObject);
                }

                m_AllUninstantiatableUnityBaseTypes = new NativeHashSet<int>(m_UninstantiatableUnityBaseTypesWithScriptingDefinedTypes.Count() + m_UninstantiatableUnityBaseTypesWithSetNativeType.Count(), Allocator.Persistent);

                foreach (var item in m_UninstantiatableUnityBaseTypesWithScriptingDefinedTypes)
                {
                    m_AllUninstantiatableUnityBaseTypes.Add(item);
                }
                foreach (var item in m_UninstantiatableUnityBaseTypesWithSetNativeType)
                {
                    m_AllUninstantiatableUnityBaseTypes.Add(item);
                }

                // GameObject, Transforms and RectTransforms can be instantiated and can't be more concrete, so we can update their type here and call it a day
                if (ITypeUnityGameObject is not ITypeInvalid)
                    UpdateManagedToNativeTypeMapping(nativeTypes, ITypeUnityGameObject, nativeTypes.GameObjectIdx, baseFallbackType: ITypeUnityGameObject);

                // Note: while having a managed shell type for Transforms is basically guaranteed as soon as there is even one MonoBehaviour in the runtime, the same is not necessarily true for RectTransforms
                if (ITypeUnityTransform is not ITypeInvalid && nativeTypes.TransformIdx is not NativeTypeEntriesCache.InvalidTypeIndex)
                    UpdateManagedToNativeTypeMapping(nativeTypes, ITypeUnityTransform, nativeTypes.TransformIdx, baseFallbackType: ITypeUnityTransform);
                if (ITypeUnityRectTransform is not ITypeInvalid && nativeTypes.RectTransformIdx is not NativeTypeEntriesCache.InvalidTypeIndex)
                    UpdateManagedToNativeTypeMapping(nativeTypes, ITypeUnityRectTransform, nativeTypes.RectTransformIdx, baseFallbackType: ITypeUnityRectTransform);

                InitSecondaryItems(fieldDescriptions, nativeTypes, vmInfo, typeNameToIndex);
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
            public bool HasStaticField(long iType)
            {
                return fieldIndicesOwnedStatic[iType].Length > 0;
            }

            /// <summary>
            /// Note: A Type may <see cref="HasStaticField"/> but no data for them, presumably because they haven't been initialized yet.
            /// </summary>
            /// <param name="iType"></param>
            /// <returns></returns>
            public bool HasStaticFieldData(long iType)
            {
                return StaticFieldBytes[iType].Count > 0;
            }

            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            public bool CouldThisFieldBeReadAsAnAddress(long arrayIndex, CachedSnapshot snapshot)
            {
                return (!HasFlag(arrayIndex, TypeFlags.kValueType) ||
                    (FieldIndicesInstance[arrayIndex].Length == 0 && Size[arrayIndex] == snapshot.VirtualMachineInformation.PointerSize));
            }

            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            public bool HasFlag(long arrayIndex, TypeFlags flag)
            {
                return (Flags[arrayIndex] & flag) == flag;
            }

            public int GetRank(long arrayIndex)
            {
                int r = (int)(Flags[arrayIndex] & TypeFlags.kArrayRankMask) >> 16;
                Checks.IsTrue(r >= 0);
                return r;
            }

            public int TypeInfo2ArrayIndex(ulong aTypeInfoAddress)
            {
                TypeInfoToArrayIndex.GetOrInitializeValue(aTypeInfoAddress, out var i, ITypeInvalid);
                return i;
            }

            static readonly ProfilerMarker k_TypeFieldArraysBuild = new ProfilerMarker("MemoryProfiler.TypeFields.TypeFieldArrayBuilding");
            void InitSecondaryItems(FieldDescriptionEntriesCache fieldDescriptions, NativeTypeEntriesCache nativeTypes, VirtualMachineInformation vmInfo, Dictionary<string, int> typeNameToIndex)
            {
                TypeInfoToArrayIndex = new AddressToIntIndex((int)TypeInfoAddress.Count, Allocator.Persistent);
                for (int i = 0; i < TypeInfoAddress.Count; i++)
                {
                    TypeInfoToArrayIndex.Add(TypeInfoAddress[i], i);
                }

                using var hashmapOfSaveConcreteTypes = new NativeHashSet<int>(1000, Allocator.Temp);
                hashmapOfSaveConcreteTypes.Add(ITypeObject);
                hashmapOfSaveConcreteTypes.Add(ITypeValueType);

                // Include all Unity Base Types.
                // Inheriting from these doesn't mean the type can't still be an abstract type, but their count is low and the chances of these being concrete types is generally pretty high.
                // The off chance of these being abstract and false flagging them as concrete leading to invalid objects getting crawled due to that flag is worth it over the risk of not crawling
                // a Unity Object derived class due to funky inheritance chain issues.
                // That said, having the actual type flags would be vastly preferable.
                foreach (var item in m_AllUninstantiatableUnityBaseTypes)
                {
                    hashmapOfSaveConcreteTypes.Add(item);
                }
                // It might be that not everyone of these types is present in the snapshot, so in case an invalid one was added, remove it again.
                hashmapOfSaveConcreteTypes.Remove(ITypeInvalid);

                // The kind of types we know about but that don't start with I and a capital letter
                var knownOddInterfaces = new HashSet<int>();
                int getter;
                knownOddInterfaces.Add(typeNameToIndex.TryGetValue("System.Runtime.InteropServices._Attribute", out getter) ? getter : -1);
                knownOddInterfaces.Remove(ITypeInvalid);

                using (k_TypeFieldArraysBuild.Auto())
                {
                    FieldIndicesInstance = new int[Count][];
                    fieldIndicesStatic = new int[Count][];
                    fieldIndicesOwnedStatic = new int[Count][];
                    List<int> fieldProcessingBuffer = new List<int>(k_DefaultFieldProcessingBufferSize);

                    var objectHeaderSize = (int)vmInfo.ObjectHeaderSize;
                    for (int i = 0; i < Count; ++i)
                    {
                        TypeTools.AllFieldArrayIndexOf(ref fieldProcessingBuffer, i, this, fieldDescriptions, TypeTools.FieldFindOptions.OnlyInstance, true);
                        FieldIndicesInstance[i] = fieldProcessingBuffer.ToArray();

                        TypeTools.AllFieldArrayIndexOf(ref fieldProcessingBuffer, i, this, fieldDescriptions, TypeTools.FieldFindOptions.OnlyStatic, true);
                        fieldIndicesStatic[i] = fieldProcessingBuffer.ToArray();

                        TypeTools.AllFieldArrayIndexOf(ref fieldProcessingBuffer, i, this, fieldDescriptions, TypeTools.FieldFindOptions.OnlyStatic, false);
                        fieldIndicesOwnedStatic[i] = fieldProcessingBuffer.ToArray();

                        // There is a bug in the caluclation for the type sizes in the native capture code where it subtracts the header size from the class size for all types.
                        // However, in IL2CPP, the header size is only included if the type derives from object, which abstract types and pointer types (e.g. "void*") don't do.
                        // To fix it in the capture code, we need to expose more type flags and also we still need to be able to read snapshots from versions where it isn't fixed, so
                        // if negative sizes are present, double check that they don't have a base type and if so, assume the bug is present and adjust them accordingly.
                        if (Size[i] < 0)
                        {
                            if (Size[i] < 0 && BaseOrElementTypeIndex[i] < 0)
                            {
#if DEBUG_VALIDATION
                                // pointers are the only value types that should get a negative size
                                if (HasFlag(i, TypeFlags.kValueType))
                                    Checks.IsTrue(TypeDescriptionName[i].EndsWith('*'));
#endif
                                // outside of value types, abstract generic base classes could end up with a negative size
                                Size[i] += objectHeaderSize;
                            }
#if DEBUG_VALIDATION
                            if (Size[i] < 0)
                                Debug.LogWarning($"Type {TypeDescriptionName[i]} has a negative size ({Size[i]}).");
#endif
                        }


                        var typeIndex = i;
                        if (DerivesFromTypes(typeIndex, in m_AllUninstantiatableUnityBaseTypes, out var managedBaseType))
                        {
                            UpdateManagedToNativeTypeMapping(nativeTypes, typeIndex, UnifiedTypeInfoManaged[managedBaseType].NativeTypeIndex, baseFallbackType: managedBaseType);
                        }
                        else
                        {
                            PureCSharpTypeIndices.Add(typeIndex);
                            var iTypeDescription = typeIndex;
                            var isConcrete = false;
                            while (iTypeDescription > ITypeInvalid)
                            {
                                if (hashmapOfSaveConcreteTypes.Contains(iTypeDescription) ||
                                    m_TypeCategory[typeIndex] == TypeCategory.Concrete || HasFlag(iTypeDescription, TypeFlags.kArray) || HasFlag(iTypeDescription, TypeFlags.kValueType))
                                {
                                    isConcrete = true;
                                    break;
                                }
                                iTypeDescription = BaseOrElementTypeIndex[iTypeDescription];
                            }
                            if (isConcrete)
                            {
                                do
                                {
                                    m_TypeCategory[typeIndex] = TypeCategory.Concrete;
                                    // go over all types between this type and the one that proofed this was derived from object and set them as well
                                    if (typeIndex == iTypeDescription)
                                        break;
                                    typeIndex = BaseOrElementTypeIndex[typeIndex];
                                } while (typeIndex > ITypeInvalid);
                            }
                            else
                            {
                                if (Size[typeIndex] == vmInfo.ObjectHeaderSize
                                    && FieldIndices[typeIndex].Count == 0)
                                {
                                    var name = TypeDescriptionName[typeIndex].AsSpan();
                                    var genericBracket = name.IndexOf('<');
                                    if (genericBracket >= 0)
                                        name = name.Slice(0, genericBracket);
                                    var lastDot = name.LastIndexOf('.');
                                    if (lastDot >= 0)
                                        name = name.Slice(lastDot + 1);
                                    if (name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]) || knownOddInterfaces.Contains(typeIndex))
                                    {
                                        // This is super likely to be an interface, it can't have instances on the heap.
                                        m_TypeCategory[typeIndex] = TypeCategory.AbstractInterface;
                                    }
                                    else
                                    {
                                        // There is a veeeeery high chance that this is an abstract class which can't have instances on the heap.
                                        // or an interface not following the IInterface naming convention
                                        // however, it could still be concrete and inheriting from a generic base that wasn't reported, so categorize it as Unlcear we can't be sure and therefore shouldn't ignore it.
                                        m_TypeCategory[typeIndex] = TypeCategory.IgnoreForHeapObjectTypeChecks;
                                    }
                                }
                                else
                                {
                                    bool EndOfGeneric(char c) => c == '>' || c == ',';
                                    var name = TypeDescriptionName[typeIndex].AsSpan();
                                    var genericBracket = name.IndexOf("<T");
                                    if (genericBracket >= 0 && name.Length > genericBracket + 1 &&
                                        (EndOfGeneric(name[genericBracket + 1]) ||
                                        (char.IsUpper(name[genericBracket + 1]) && (name.Length > genericBracket + 2) && (char.IsLower(name[genericBracket + 2]) || EndOfGeneric(name[genericBracket + 2])))))
                                    {
                                        // Pretty sure this is an abstract generic class, it can't have instances on the heap.
                                        m_TypeCategory[typeIndex] = TypeCategory.AbstractGeneric;
                                    }
                                    else
                                    {
                                        // We can't be sure if this is abstract or not, it might be concrete but inheriting from a generic base that wasn't reported
                                        m_TypeCategory[typeIndex] = TypeCategory.IgnoreForHeapObjectTypeChecks;
                                    }
                                }
                            }
                        }
                    }
                }
                var fieldIndices = FieldIndices[ITypeUnityObject];
                long fieldIndicesIndex = -1;
                for (long i = 0; i < fieldIndices.Count; i++)
                {
                    if (fieldDescriptions.FieldDescriptionName[fieldIndices[i]] == UnityNativeObjectPointerFieldName)
                    {
                        fieldIndicesIndex = i;
                        break;
                    }
                }

                IFieldUnityObjectMCachedPtr = fieldIndicesIndex >= 0 ? FieldIndices[ITypeUnityObject][fieldIndicesIndex] : -1;

                IFieldUnityObjectMCachedPtrOffset = -1;

                if (IFieldUnityObjectMCachedPtr >= 0)
                {
                    IFieldUnityObjectMCachedPtrOffset = fieldDescriptions.Offset[IFieldUnityObjectMCachedPtr];
                }
#if UNMANAGED_NATIVE_HASHMAP_AVAILABLE
                PureCSharpTypeIndices.TrimExcess();
#endif

#if DEBUG_VALIDATION
                if (IFieldUnityObjectMCachedPtrOffset < 0)
                {
                    Debug.LogWarning("Could not find unity object instance id field or m_CachedPtr");
                    return;
                }
#endif
            }

            void UpdateManagedToNativeTypeMapping(CachedSnapshot.NativeTypeEntriesCache nativeTypes, int managedType, int nativeType, ref NativeHashSet<int> hashmapOfUnityBaseTypes, int baseFallbackType = CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid)
            {
                UpdateManagedToNativeTypeMapping(nativeTypes, managedType, nativeType, baseFallbackType);
                hashmapOfUnityBaseTypes.Add(managedType);
            }

            public void UpdateManagedToNativeTypeMapping(CachedSnapshot.NativeTypeEntriesCache nativeTypes, int managedType, int nativeType, int baseFallbackType = CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid)
            {
                // Updating the mapping only makes sense if at least the managed types are valid
                Checks.IsTrue(managedType is not ITypeInvalid);

                // Updating the type means we sorta confirmed it is concrete
                // (As we currently assume that all Unity Types are concrete, which isn't necessarily the case but probable enough.
                // In the future we'd ideally be reporting type flags, but also, we're currently only using these to determine
                // if reading some heap bytes (pointed to from somewhere else) as a pointer to a type could reasonably be
                // an object header for a heap object. Since only concrete types could have objects on the heap,
                // including slightly more types than necessary as concrete is preferable to falsely excluding some.)
                m_TypeCategory[managedType] = TypeCategory.Concrete;

                var previousBaseType = UnifiedTypeInfoManaged[managedType].ManagedBaseTypeIndexForNativeType;
                var setFallBackType = baseFallbackType is not CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid;
                var onlyUpdateNativeType = false;

                // if we are setting a base type, only update info if it wasn't already set or the new base type is more specific
                if (setFallBackType && previousBaseType is not CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid
                    && (previousBaseType == baseFallbackType || !DerivesFrom(baseFallbackType, previousBaseType, true)))
                {
                    if (nativeType is not NativeTypeEntriesCache.InvalidTypeIndex)
                        previousBaseType = UnifiedTypeInfoNative[nativeType].ManagedBaseTypeIndexForNativeType;
                    // Check if we also do not need to update the native type info
                    if (previousBaseType is not CachedSnapshot.TypeDescriptionEntriesCache.ITypeInvalid
                        && (previousBaseType == baseFallbackType || !DerivesFrom(baseFallbackType, previousBaseType, true)))
                        return;
                    onlyUpdateNativeType = true;
                }

                // Only update the fallback base type if it wasn't previously a Unity scripting type.
                // All Unity scripting types should get initialized correctly to their fallback types through InitSecondaryItems
                // And subsequently should not be updated to be more specific, as they have a multiple managed to one native(& managed base type) relationship.
                // Other base types such as UnityEngine.Object or Component have specific scripting types that map to their native types 1-to-1
                // so those should get updated to be more specifc.
                var updateFallbackType = setFallBackType && !UninstantiatableUnityBaseTypesWithScriptingDefinedTypes.Contains(previousBaseType);
                var fallbackTypeToSet = updateFallbackType ? baseFallbackType : previousBaseType;

                if (nativeType is not NativeTypeEntriesCache.InvalidTypeIndex
                    && updateFallbackType && UnifiedTypeInfoNative[nativeType].ManagedBaseTypeIndexForNativeType != fallbackTypeToSet)
                {
                    // The native type info never gets a more concrete managed type linked up to it than the base type.
                    UnifiedTypeInfoNative[nativeType] = new UnifiedType(nativeTypes, this, nativeType, fallbackTypeToSet, managedBaseTypeIndex: fallbackTypeToSet);
                }

                if (!onlyUpdateNativeType)
                {
                    // updating the managed type with a new native type mapping shouldn't change the managed type
                    Checks.CheckEquals(managedType, UnifiedTypeInfoManaged[managedType].ManagedTypeIndex);
                    UnifiedTypeInfoManaged[managedType] = new UnifiedType(nativeTypes, this, nativeType, managedType, managedBaseTypeIndex: fallbackTypeToSet);
                }

            }

            /// <summary>
            /// Past <see cref="InitSecondaryItems(FieldDescriptionEntriesCache, NativeTypeEntriesCache, VirtualMachineInformation, Dictionary{string, int})"/> being done, <see cref="DerivesFromUnityObject"/> should be used instead.
            /// </summary>
            /// <param name="iTypeDescription"></param>
            /// <returns></returns>
            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            private bool DerivesFromTypes(int iTypeDescription, in NativeHashSet<int> baseTypes, out int managedBaseType)
            {
                managedBaseType = ITypeInvalid;
                while (!baseTypes.Contains(iTypeDescription) && iTypeDescription >= 0)
                {
                    if (HasFlag(iTypeDescription, TypeFlags.kArray))
                        return false;
                    iTypeDescription = BaseOrElementTypeIndex[iTypeDescription];
                }
                if (baseTypes.Contains(iTypeDescription))
                {
                    managedBaseType = iTypeDescription;
                    return true;
                }
                return false;
            }

            /// <summary>
            /// After <see cref="InitSecondaryItems(FieldDescriptionEntriesCache, VirtualMachineInformation, Dictionary{string, int})"/>,
            /// this is the quickest way to check if a type derives from UnityObject.
            /// Also, <see cref="Managed.ManagedDataCrawler.ConnectNativeToManageObject(Managed.ManagedDataCrawler.IntermediateCrawlData)"/>
            /// adds further types to this can can't be checked with <see cref="DerivesFromTypes"/> as their managed base type is not properly reported,
            /// e.g. due to the use of a generic like ScriptableSingleton<T>.
            /// </summary>
            /// <param name="iTypeDescription"></param>
            /// <returns></returns>
            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            public bool DerivesFromUnityObject(int iTypeDescription)
            {
                return UnifiedTypeInfoManaged[iTypeDescription].IsUnityObjectType;
            }

            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            public bool DerivesFrom(int iTypeDescription, int potentialBase, bool excludeArrayElementBaseTypes)
            {
                while (iTypeDescription != potentialBase && iTypeDescription >= 0)
                {
                    if (excludeArrayElementBaseTypes && HasFlag(iTypeDescription, TypeFlags.kArray))
                        return false;
                    iTypeDescription = BaseOrElementTypeIndex[iTypeDescription];
                }

                return iTypeDescription == potentialBase;
            }


            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            public bool IsNativeContainerType(int managedTypeIndex)
            {
                return TypeDescriptionName[managedTypeIndex].StartsWith(NativeCollectionsNamspaceAndTypePrefix)
                    // only accept types that end on a generic bracket.
                    // E.g. Avoid parsing Unity.Collections.NativeArray<System.Int32>[] as a native collection
                    && TypeDescriptionName[managedTypeIndex].EndsWith('>');
            }

            public void Dispose()
            {
                Count = 0;
                Flags.Dispose();
                BaseOrElementTypeIndex.Dispose();
                Size.Dispose();
                TypeInfoAddress.Dispose();
                TypeDescriptionName = null;
                Assembly = null;
                if (m_FieldIndicesReadOp.IsCreated)
                {
                    // Dispose the read operation first to abort it ...
                    m_FieldIndicesReadOp.Dispose();
                    // ... before disposing the result, as otherwise we'd sync on a pending read op.
                    FieldIndices.Dispose();
                    m_FieldIndicesReadOp = default;
                }
                if (m_StaticFieldBytesReadOp.IsCreated)
                {
                    // Dispose the read operation first to abort it ...
                    m_StaticFieldBytesReadOp.Dispose();
                    // ... before disposing the result, as otherwise we'd sync on a pending read op.
                    StaticFieldBytes.Dispose();
                    m_StaticFieldBytesReadOp = default;
                }

                FieldIndicesInstance = null;
                fieldIndicesStatic = null;
                fieldIndicesOwnedStatic = null;
                if (m_UnifiedTypeInfo.IsCreated)
                    m_UnifiedTypeInfo.Dispose();
                if (TypeInfoToArrayIndex.IsCreated)
                    TypeInfoToArrayIndex.Dispose();
                if (PureCSharpTypeIndices.IsCreated)
                    PureCSharpTypeIndices.Dispose();
                if (m_AllUninstantiatableUnityBaseTypes.IsCreated)
                    m_AllUninstantiatableUnityBaseTypes.Dispose();
                if (m_UninstantiatableUnityBaseTypesWithScriptingDefinedTypes.IsCreated)
                    m_UninstantiatableUnityBaseTypesWithScriptingDefinedTypes.Dispose();
                if (m_UninstantiatableUnityBaseTypesWithSetNativeType.IsCreated)
                    m_UninstantiatableUnityBaseTypesWithSetNativeType.Dispose();
            }
        }

        public class FieldDescriptionEntriesCache : IDisposable
        {
            public long Count;
            public string[] FieldDescriptionName;
            public DynamicArray<int> Offset = default;
            public DynamicArray<int> TypeIndex = default;
            public DynamicArray<byte> IsStatic = default;

            unsafe public FieldDescriptionEntriesCache(ref IFileReader reader)
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
            /// <summary>
            /// Use this Count when iterating over all entries in <see cref="Target"/>.
            /// When checking if a Managed Object index is low enough to belong to a Managed Shell for a Native Object that
            /// reported it via its GCHandle, use <see cref="UniqueCount"/> instead.
            /// </summary>
            public long Count;
            /// <summary>
            /// Pre 19.3 scripting snapshot implementations could report dublicated GCHandles.
            /// after <see cref="CachedSnapshot.PostProcess"/> is finished, or more precisely after <seealso cref="ManagedDataCrawler.GatherIntermediateCrawlData(CachedSnapshot, ManagedDataCrawler.IntermediateCrawlData, ref DynamicArray{ManagedDataCrawler.StackCrawlData})"/>
            /// ran, this value is going to be accurate. Before that it is just a copy of <see cref="Count"/>.
            /// </summary>
            public long UniqueCount;

            public GCHandleEntriesCache(ref IFileReader reader)
            {
                unsafe
                {
                    UniqueCount = Count = reader.GetEntryCount(EntryType.GCHandles_Target);

                    if (Count == 0)
                        return;

                    Target = reader.Read(EntryType.GCHandles_Target, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                }
            }

            public void Dispose()
            {
                Count = 0;
                UniqueCount = 0;
                Target.Dispose();
            }
        }

        public class ConnectionEntriesCache : IDisposable
        {
            public long Count;

            // From/To with the same index forms a pair "from->to"
            public DynamicArray<int> From { private set; get; }
            public DynamicArray<int> To { private set; get; }

            // TODO: Use Native Hashmaps of Native Lists for these to optimze out GC Allocs
            // List of objects referencing an object with the specfic key
            public Dictionary<SourceIndex, List<SourceIndex>> ReferencedBy { get; private set; } = new Dictionary<SourceIndex, List<SourceIndex>>();

            /// <summary>
            /// List of objects an object with the specific key is refereing to.
            /// For GameObjects, their first reference is that to its Transform component, followed by all other components.
            /// For Transforms its their host GameObject, followed by all child transforms, with the final addition of their parent transform, IF IT HAS ONE! (i.e. Root transforms only report child Transforms).
            /// For Components, its their host GameObject, followed by all other references.
            /// </summary>
            public Dictionary<SourceIndex, List<SourceIndex>> ReferenceTo { get; private set; } = new Dictionary<SourceIndex, List<SourceIndex>>();

#if DEBUG_VALIDATION // could be always present but currently only used for validation in the crawler
            public long IndexOfFirstNativeToGCHandleConnection = -1;
#endif

            unsafe public ConnectionEntriesCache(ref IFileReader reader, NativeObjectEntriesCache nativeObjects, long gcHandlesCount, bool connectionsNeedRemaping)
            {
                Count = reader.GetEntryCount(EntryType.Connections_From);

                // Set allocator to `temp` if we're going to discard the data later
                Allocator allocator = Allocator.Persistent;
                if (Count > 0 && connectionsNeedRemaping)
                    allocator = Allocator.Temp;

                From = new DynamicArray<int>(Count, allocator);
                To = new DynamicArray<int>(Count, allocator);

                if (Count == 0)
                    return;

                DynamicArray<EntityId> instanceIDFrom;
                DynamicArray<EntityId> instanceIDTo;
                if (reader.FormatVersion < FormatVersion.EntityIDAs8ByteStructs)
                {
                    From = reader.Read(EntryType.Connections_From, 0, Count, allocator).Result.Reinterpret<int>();
                    To = reader.Read(EntryType.Connections_To, 0, Count, allocator).Result.Reinterpret<int>();
                    // Clear the memory on alloc. The MemCpyStride in ConvertInstanceId won't initialize the blank spaces
                    instanceIDFrom = new DynamicArray<EntityId>(Count, Allocator.Temp, memClear: true);
                    instanceIDTo = new DynamicArray<EntityId>(Count, Allocator.Temp, memClear: true);
                    From.ConvertInstanceIdIntsToEntityIds(ref instanceIDFrom);
                    To.ConvertInstanceIdIntsToEntityIds(ref instanceIDTo);
                }
                else
                {
                    instanceIDFrom = reader.Read(EntryType.Connections_From, 0, Count, allocator).Result.Reinterpret<EntityId>();
                    instanceIDTo = reader.Read(EntryType.Connections_To, 0, Count, allocator).Result.Reinterpret<EntityId>();
                }

                if (connectionsNeedRemaping)
                    RemapInstanceIdsToUnifiedIndex(nativeObjects, gcHandlesCount, instanceIDFrom, instanceIDTo);

                instanceIDFrom.Dispose();
                instanceIDTo.Dispose();

                for (int i = 0; i < Count; i++)
                {
                    var to = ToSourceIndex(To[i], gcHandlesCount);
                    var from = ToSourceIndex(From[i], gcHandlesCount);

                    ReferencedBy.GetAndAddToListOrCreateList(to, from);

                    ReferenceTo.GetAndAddToListOrCreateList(from, to);
                }
            }

            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            SourceIndex ToSourceIndex(int index, long gcHandlesCount)
            {
                if (index < gcHandlesCount)
                    return new SourceIndex(SourceIndex.SourceId.ManagedObject, index);

                return new SourceIndex(SourceIndex.SourceId.NativeObject, index - gcHandlesCount);
            }

            void RemapInstanceIdsToUnifiedIndex(NativeObjectEntriesCache nativeObjects, long gcHandlesCount,
                DynamicArray<EntityId> instanceIDFrom, DynamicArray<EntityId> instanceIDTo)
            {
                var instanceIds = nativeObjects.InstanceId;
                var gcHandlesIndices = nativeObjects.ManagedObjectIndex;

                // Create two temporary acceleration structures:
                // - Native object EntityId to GC object
                // - Native object EntityId to Unified Index
                //
                // Unified Index - [0..gcHandlesCount)[0..nativeObjects.Count]
                var instanceIDToUnifiedIndex = new Dictionary<EntityId, int>();
                var instanceIDToGcHandleIndex = new Dictionary<EntityId, int>();
                for (int i = 0; i < instanceIds.Count; ++i)
                {
                    if (gcHandlesIndices[i] != -1)
                    {
                        instanceIDToGcHandleIndex.Add(instanceIds[i], gcHandlesIndices[i]);
                    }
                    instanceIDToUnifiedIndex.Add(instanceIds[i], (int)gcHandlesCount + i);
                }

#if DEBUG_VALIDATION
                if (instanceIDToGcHandleIndex.Count > 0)
                    IndexOfFirstNativeToGCHandleConnection = Count;
#endif

                // Connections - reported Native objects connections
                // Plus links between Native and Managed objects (instanceIDToGcHandleIndex)
                var newFrom = new DynamicArray<int>(Count + instanceIDToGcHandleIndex.Count, Allocator.Persistent);
                var newTo = new DynamicArray<int>(newFrom.Count, Allocator.Persistent);
                // Add all Native to Native connections reported in snapshot as Unified Index
                for (long i = 0; i < Count; ++i)
                {
                    newFrom[i] = instanceIDToUnifiedIndex[instanceIDFrom[i]];
                    newTo[i] = instanceIDToUnifiedIndex[instanceIDTo[i]];
                }

                // Dispose of original data to save memory
                // as we no longer need it
                To.Dispose();
                From.Dispose();

                // Add all Managed to Native connections
                var enumerator = instanceIDToGcHandleIndex.GetEnumerator();
                for (long i = Count; i < newFrom.Count; ++i)
                {
                    enumerator.MoveNext();
                    newFrom[i] = instanceIDToUnifiedIndex[enumerator.Current.Key];
                    // elements in To that are `To[i] < gcHandlesCount` are indexes into the GCHandles list
                    newTo[i] = enumerator.Current.Value;
                }

                From = newFrom;
                To = newTo;
                Count = From.Count;
            }

            public void Dispose()
            {
                Count = 0;
                From.Dispose();
                To.Dispose();
                // Setting to default isn't necessary, but can avoid confusion when memory profiling the memory profiler
                // without this, Disposing works but leaves the properties backing field with an invalid DynamicArray
                // that looks like it isundisposed
                From = default;
                To = default;

                ReferencedBy = null;
                ReferenceTo = null;
            }
        }

        IFileReader m_Reader;
        public MetaData MetaData { get; private set; }
        public DateTime TimeStamp { get; private set; }
        public ref readonly VirtualMachineInformation VirtualMachineInformation => ref m_VirtualMachineInformation;
        readonly VirtualMachineInformation m_VirtualMachineInformation;
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

        public SortedNativeMemoryRegionEntriesCache SortedNativeRegionsEntries;
        public SortedManagedObjectsCache SortedManagedObjects;
        public SortedNativeAllocationsCache SortedNativeAllocations;
        public SortedNativeObjectsCache SortedNativeObjects;

        public SceneRootEntriesCache SceneRoots;
        public NativeAllocatorEntriesCache NativeAllocators;
        public NativeGfxResourceReferenceEntriesCache NativeGfxResourceReferences;

        public SystemMemoryRegionEntriesCache SystemMemoryRegions;
        public SystemMemoryResidentPagesEntriesCache SystemMemoryResidentPages;
        public EntriesMemoryMapCache EntriesMemoryMap;
        public ProcessedNativeRoots ProcessedNativeRoots;
        public RootAndImpactInfo RootAndImpactInfo;

        public CachedSnapshot(IFileReader reader)
        {
            unsafe
            {
                VirtualMachineInformation vmInfo;
                reader.ReadUnsafe(EntryType.Metadata_VirtualMachineInformation, &vmInfo, sizeof(VirtualMachineInformation), 0, 1);

                // Re-enable this check once we add capture of the VM memory on CoreCLR. It is disabled currently to allow us to have snapshot tests running that are unrelated to VM memory. https://jira.unity3d.com/browse/VM-1768
#if !ENABLE_CORECLR
                if (!VMTools.ValidateVirtualMachineInfo(vmInfo))
                {
                    throw new UnityException("Invalid VM info. Snapshot file is corrupted.");
                }
#endif

                m_Reader = reader;
                long ticks;
                reader.ReadUnsafe(EntryType.Metadata_RecordDate, &ticks, sizeof(long), 0, 1);
                TimeStamp = new DateTime(ticks);

                m_VirtualMachineInformation = vmInfo;
                m_SnapshotVersion = reader.FormatVersion;

                MetaData = new MetaData(reader);
                SetUnityVersionSpecificFlags();

                NativeAllocationSites = new NativeAllocationSiteEntriesCache(ref reader);
                FieldDescriptions = new FieldDescriptionEntriesCache(ref reader);
                NativeTypes = new NativeTypeEntriesCache(ref reader);
                TypeDescriptions = new TypeDescriptionEntriesCache(ref reader, FieldDescriptions, NativeTypes, vmInfo);
                NativeRootReferences = new NativeRootReferenceEntriesCache(ref reader);
                NativeObjects = new NativeObjectEntriesCache(ref reader, NativeTypes.AssetBundleIdx);
                NativeMemoryRegions = new NativeMemoryRegionEntriesCache(ref reader);
                NativeMemoryLabels = new NativeMemoryLabelEntriesCache(ref reader, HasMemoryLabelSizesAndGCHeapTypes);
                NativeCallstackSymbols = new NativeCallstackSymbolEntriesCache(ref reader);
                NativeAllocations = new NativeAllocationEntriesCache(ref reader, NativeAllocationSites.Count != 0);
                ManagedStacks = new ManagedMemorySectionEntriesCache(ref reader, false, true);
                ManagedHeapSections = new ManagedMemorySectionEntriesCache(ref reader, HasMemoryLabelSizesAndGCHeapTypes, false);
                GcHandles = new GCHandleEntriesCache(ref reader);
                Connections = new ConnectionEntriesCache(ref reader, NativeObjects, GcHandles.Count, HasConnectionOverhaul);
                SceneRoots = new SceneRootEntriesCache(ref reader);
                // TODO: Jobifiy GenerateBaseData and sync at end
                SceneRoots.GenerateBaseData(this, NativeObjects.InstanceId2Index, NativeTypes.GameObjectIdx);

                NativeGfxResourceReferences = new NativeGfxResourceReferenceEntriesCache(ref reader);
                NativeAllocators = new NativeAllocatorEntriesCache(ref reader);

                SystemMemoryRegions = new SystemMemoryRegionEntriesCache(ref reader);
                SystemMemoryResidentPages = new SystemMemoryResidentPagesEntriesCache(ref reader);

                SortedManagedObjects = new SortedManagedObjectsCache(this);

                SortedNativeRegionsEntries = new SortedNativeMemoryRegionEntriesCache(this);
                SortedNativeAllocations = new SortedNativeAllocationsCache(this);
                SortedNativeObjects = new SortedNativeObjectsCache(this);

                EntriesMemoryMap = new EntriesMemoryMapCache(this);

                CrawledData = new ManagedData(GcHandles.Count, Connections.Count, NativeTypes.Count);

                ProcessedNativeRoots = new ProcessedNativeRoots();
            }
        }

        void SetUnityVersionSpecificFlags()
        {
            m_HasPrefabRootInfo = UnityVersionHasPrefabRootInfo();
        }

        bool UnityVersionHasPrefabRootInfo()
        {
            return MetaData.UnityVersionEqualOrNewer(6000, 5, 0, MetaData.UnityReleaseType.Alpha, 3)
                   || MetaData.UnityVersionEqualOrNewerWithinMinorRelease(6000, 4, 0, MetaData.UnityReleaseType.Alpha, 6)
                   || MetaData.UnityVersionEqualOrNewerWithinMinorRelease(6000, 3, 1, MetaData.UnityReleaseType.Full, 1)
                   || MetaData.UnityVersionEqualOrNewerWithinMinorRelease(6000, 2, 15, MetaData.UnityReleaseType.Full, 1)
                   // skip 6000.1 as it did not get the prefab fix.
                   || MetaData.UnityVersionEqualOrNewerWithinMinorRelease(6000, 0, 64, MetaData.UnityReleaseType.Full, 1);
        }

        public int PostProcessStepCountWithCrawler =>
            ManagedDataCrawler.CrawlStepCount
            + PostProcessStepCountWithoutCrawler;
        public int PostProcessStepCountWithoutCrawler =>
            +EntriesMemoryMapCache.BuildStepCount
            + ProcessedNativeRoots.ProcessStepCount
            + RootAndImpactInfo.ProcessStepCount
            + 1; //final step

        public IEnumerator<EnumerationStatus> PostProcess(bool crawlManaged = true)
        {
            var status = new EnumerationStatus(crawlManaged ? PostProcessStepCountWithCrawler : PostProcessStepCountWithoutCrawler);

            IEnumerator<EnumerationStatus> processor = null;
            // Managed Objects end up on the Memory Map, so crawl them first
            if (crawlManaged)
            {
                processor = ManagedDataCrawler.Crawl(this, status);
                while (processor.MoveNext())
                    yield return processor.Current;
            }
            // these need the managed object count
            RootAndImpactInfo = new RootAndImpactInfo(this);

            // To populate ProcessedNativeRoots with reliable native sizes for all allocations, as well as Resident vs Committed amounts for all Native Roots,the Memory Map needs to be built
            processor = EntriesMemoryMap.Build(status);
            while (processor.MoveNext())
                yield return processor.Current;

            // The Unity Objects Summary table will need ready to go ProcessedNativeRoots to show Unity Objects with their rooted native, gfx and managed memory
            processor = ProcessedNativeRoots.ReadOrProcess(this, status);
            while (processor.MoveNext())
                yield return processor.Current;

            // The Impact columns need Path To Root info, though this could be moved to the background?
            processor = RootAndImpactInfo.ReadOrProcess(this, status);
            while (processor.MoveNext())
                yield return processor.Current;
        }

        public string FullPath
        {
            get
            {
                return m_Reader.FullPath;
            }
        }

        //Unified Object index are in that order: gcHandle, native object, crawled objects
        public long ManagedObjectIndexToUnifiedObjectIndex(long i)
        {
            // Invalid
            if (i < 0)
                return -1;

            // GC Handle
            if (i < GcHandles.UniqueCount)
                return i;

            // Managed objects, but not reported through GC Handle
            if (i < CrawledData.ManagedObjects.Count)
                return i + NativeObjects.Count;

            return -1;
        }

        public long NativeObjectIndexToUnifiedObjectIndex(long i)
        {
            if (i < 0)
                return -1;

            // Shift behind native objects
            if (i < NativeObjects.Count)
                return i + GcHandles.UniqueCount;

            return -1;
        }

        public int UnifiedObjectIndexToManagedObjectIndex(long i)
        {
            if (i < 0)
                return -1;

            if (i < GcHandles.UniqueCount)
                return (int)i;

            // If CrawledData.ManagedObjects includes GcHandles as first GcHandles.Count
            // than it makes sense as we want to remap only excess
            int firstCrawled = (int)(GcHandles.UniqueCount + NativeObjects.Count);
            int lastCrawled = (int)(NativeObjects.Count + CrawledData.ManagedObjects.Count);

            if (i >= firstCrawled && i < lastCrawled)
                return (int)(i - (int)NativeObjects.Count);

            return -1;
        }

        public int UnifiedObjectIndexToNativeObjectIndex(long i)
        {
            if (i < GcHandles.UniqueCount) return -1;
            int firstCrawled = (int)(GcHandles.UniqueCount + NativeObjects.Count);
            if (i < firstCrawled) return (int)(i - (int)GcHandles.UniqueCount);
            return -1;
        }

        public enum NativeAllocationOrRegionSearchResult
        {
            NullOrUninitialized,
            Invalid,
            NotInTrackedMemory,
            FoundAllocation,
            FoundRegion,
            NothingFound
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public NativeAllocationOrRegionSearchResult FindNativeAllocationOrRegion(ulong pointer,
            out SourceIndex nativeRegionIndex, out SourceIndex nativeAllocationIndex)
        {
            return FindNativeAllocationOrRegion(pointer, out nativeRegionIndex, out nativeAllocationIndex,
                 out _, false);
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public NativeAllocationOrRegionSearchResult FindNativeAllocationOrRegion(ulong pointer,
            out SourceIndex nativeRegionIndex, out SourceIndex nativeAllocationIndex,
            out string nativeRegionPath)
        {
            return FindNativeAllocationOrRegion(pointer, out nativeRegionIndex, out nativeAllocationIndex,
                out nativeRegionPath, true);
        }

        NativeAllocationOrRegionSearchResult FindNativeAllocationOrRegion(ulong pointer,
            out SourceIndex nativeRegionIndex, out SourceIndex nativeAllocationIndex, out string nativeRegionPath,
            bool buildFullNativePath = false)
        {
            nativeRegionIndex = nativeAllocationIndex = default;
            nativeRegionPath = null;
            if (pointer == 0)
            {
                return NativeAllocationOrRegionSearchResult.NullOrUninitialized;
            }
            else if (pointer == ulong.MaxValue)
            {
                return NativeAllocationOrRegionSearchResult.Invalid;
            }
            var nativeRegionIndexInSortedRegions = SortedNativeRegionsEntries.Find(pointer, onlyDirectAddressMatches: false);
            if (nativeRegionIndexInSortedRegions >= 0)
            {
                nativeRegionIndex = new SourceIndex(SourceIndex.SourceId.NativeMemoryRegion, SortedNativeRegionsEntries[nativeRegionIndexInSortedRegions]);
                nativeRegionPath = SortedNativeRegionsEntries.Name(nativeRegionIndexInSortedRegions);

                if (buildFullNativePath)
                {
                    var foundRegionInLayer = SortedNativeRegionsEntries.RegionHierarchLayer[nativeRegionIndexInSortedRegions];
                    if (foundRegionInLayer > 0)
                    {
                        // search backwards for parent regions.
                        for (var iRegion = nativeRegionIndexInSortedRegions - 1; iRegion >= 0; iRegion--)
                        {
                            if (SortedNativeRegionsEntries.RegionHierarchLayer[iRegion] >= foundRegionInLayer
                                || SortedNativeRegionsEntries.Address(iRegion) + SortedNativeRegionsEntries.FullSize(iRegion) < pointer)
                                continue;
                            if (SortedNativeRegionsEntries.Address(iRegion) <= pointer)
                            {

                                nativeRegionPath += $"{SortedNativeRegionsEntries.Name(iRegion)} / {nativeRegionPath}";
                                foundRegionInLayer = SortedNativeRegionsEntries.RegionHierarchLayer[nativeRegionIndexInSortedRegions];
                                // found a parent region, continue searching if it is not on layer 0 though as there should be another parent-region enclosing this one
                                if (foundRegionInLayer == 0)
                                    break;
                            }
                        }
                    }
                }

                long idx = DynamicArrayAlgorithms.BinarySearch(SortedNativeAllocations.UnsafeCache, pointer);
                // -1 means the address is smaller than the first starting Address,
                if (idx != -1)
                {
                    if (idx < 0)
                    {
                        // otherwise, a negative Index just means there was no direct hit and ~idx - 1 will give us the index to the next smaller starting Address
                        idx = ~idx - 1;
                    }
                    var startAddress = SortedNativeAllocations.Address(idx);
                    if (pointer >= startAddress && pointer < (startAddress + SortedNativeAllocations.FullSize(idx)))
                    {
                        nativeAllocationIndex = new SourceIndex(SourceIndex.SourceId.NativeAllocation, SortedNativeAllocations[idx]);
                        return NativeAllocationOrRegionSearchResult.FoundAllocation;
                    }
                }
            }
            else
            {
                // This pointer points out of range! Could be IL2CPP Virtual Machine Memory but it could just be entirely broken
                return NativeAllocationOrRegionSearchResult.NotInTrackedMemory;
            }
            return nativeRegionIndexInSortedRegions >= 0 ? NativeAllocationOrRegionSearchResult.FoundRegion : NativeAllocationOrRegionSearchResult.NothingFound;
        }

        public bool UseDeviceMemoryForGraphics
        {
            get
            {
                return NativeMemoryRegions.GPUAllocatorIndices.Count > 0;
            }
        }

        public void Dispose()
        {
            if (!m_Disposed)
            {
                m_Disposed = true;
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
                SceneRoots.Dispose();
                NativeGfxResourceReferences.Dispose();
                NativeAllocations.Dispose();
                NativeAllocators.Dispose();

                SystemMemoryRegions.Dispose();
                SystemMemoryResidentPages.Dispose();

                RootAndImpactInfo.Dispose();
                EntriesMemoryMap.Dispose();
                CrawledData.Dispose();
                CrawledData = null;

                SortedNativeRegionsEntries.Dispose();
                SortedManagedObjects.Dispose();
                SortedNativeAllocations.Dispose();
                SortedNativeObjects.Dispose();

                ProcessedNativeRoots.Dispose();
                // Close and dispose the reader
                m_Reader.Close();
            }
        }

        public interface ISortedEntriesCache
        {
            void Preload();
            long Count { get; }
            ulong Address(long index);
            ulong FullSize(long index);
        }

        public abstract class IndirectlySortedEntriesCache<TSortComparer, TUnsafeCache>
            : IDisposable, ISortedEntriesCache
            where TSortComparer : unmanaged, IRefComparer<long>
            where TUnsafeCache : unmanaged, ISortedEntriesCache
        {
            protected CachedSnapshot m_Snapshot;
            protected DynamicArray<long> m_Sorting;
            protected bool Loaded => m_Loaded;
            bool m_Loaded;

            protected IndirectlySortedEntriesCache(CachedSnapshot snapshot)
            {
                m_Snapshot = snapshot;
                m_Loaded = false;
            }

            public long this[long index]
            {
                get
                {
                    if (m_Loaded)
                        return m_Sorting[index];
                    // m_Loaded is more likely than not true, but we can't tell the optimizer that in C#,
                    // so have the less likely case as separate fallback branch
                    Preload();
                    return m_Sorting[index];
                }
            }

            public abstract long Count { get; }
            protected abstract TSortComparer SortingComparer { get; }
            unsafe ArraySortingData<long, TSortComparer> Comparer =>
                ArraySortingData<TSortComparer>.GetSortDataForSortingAnIndexingArray(in m_Sorting, SortingComparer);

            public abstract ulong Address(long index);

            public virtual void Preload()
            {
                if (!m_Sorting.IsCreated)
                {
                    m_Sorting = new DynamicArray<long>(Count, Allocator.Persistent);
                    var count = m_Sorting.Count;
                    for (long i = 0; i < count; ++i)
                        m_Sorting[i] = i;
                    if (count > 0)
                    {
                        var comparer = Comparer;
                        DynamicArrayAlgorithms.IntrospectiveSort(0, Count, ref comparer);
                    }
                }
                m_Loaded = true;
            }

            public abstract ulong FullSize(long index);

            public virtual void Dispose()
            {
                m_Sorting.Dispose();
            }

            /// <summary>
            /// A burst compatible way to access the sorted data. It is not safe to use if the holding object has been disposed.
            /// </summary>
            public virtual TUnsafeCache UnsafeCache { get; }

            /// <summary>
            /// Uses <see cref="DynamicArrayAlgorithms.BinarySearch"/> to quickly find an item
            /// which has a start <see cref="Address(long)"/> matching the provided <paramref name="address"/>,
            /// or, if <paramref name="onlyDirectAddressMatches"/> is false, where the <paramref name="address"/> falls between
            /// the items start <see cref="Address(long)"/> and last address (using <see cref="FullSize(long)"/>).
            ///
            /// If there are 0 sized items at the address, the last item will be returned.
            /// </summary>
            /// <param name="address"></param>
            /// <param name="onlyDirectAddressMatches"></param>
            /// <remarks> CAUTION: For data where regions can overlap, e.g. <seealso cref="SortedNativeMemoryRegionEntriesCache"/>
            /// this will find the deepest nested region, not any potential enclosing regions.</remarks>
            /// <returns> Index of the value within the <seealso cref="IndirectlySortedEntriesCache"/>.
            /// -1 means the item wasn't found.</returns>
            public long Find(ulong address, bool onlyDirectAddressMatches)
            {
                var idx = DynamicArrayAlgorithms.BinarySearch(this.UnsafeCache, address);
                if (idx < 0)
                {
                    // -1 means the address is smaller than the first Address, early out with -1
                    if (idx == -1 || onlyDirectAddressMatches)
                        return -1;
                    // otherwise, a negative Index just means there was no match of the any address range (yet matching with a range of size 0 if the address matches)
                    // and ~idx - 1 will give us the index to the next smaller Address
                    idx = ~idx - 1;
                }
                var foundAddress = Address(idx);
                if (address == foundAddress)
                    return idx;
                if (onlyDirectAddressMatches)
                    return -1;
                var size = FullSize(idx);
                if (address > foundAddress && (address < (foundAddress + size) || size == 0))
                {
                    return idx;
                }
                return -1;
            }
        }

        /// <summary>
        /// Used for entry caches that don't have any overlaps and no items of size 0 (or if, not right next to each other,
        /// i.e. <see cref="SortedNativeObjects"/> are fine to use this instead of <see cref="IndirectlySortedEntriesCacheSortedByAddressAndSizeArray"/>
        /// as while some Native Objects may report a size of 0, their addresses will never match
        /// </summary>
        public abstract class IndirectlySortedEntriesCacheSortedByAddressArray<TUnsafeCache> : IndirectlySortedEntriesCache<IndexedArrayValueComparer<ulong>, TUnsafeCache>
            where TUnsafeCache : unmanaged, ISortedEntriesCache
        {
            protected unsafe override IndexedArrayValueComparer<ulong> SortingComparer =>
                new IndexedArrayValueComparer<ulong>(in Addresses);
            public IndirectlySortedEntriesCacheSortedByAddressArray(CachedSnapshot snapshot) : base(snapshot) { }
            protected abstract ref readonly DynamicArray<ulong> Addresses { get; }
            public override ulong Address(long index) => Addresses[this[index]];
        }

        /// <summary>
        /// Used for entry caches that can have overlapping regions or those which border right next to each other while having sizes of 0
        /// </summary>
        public abstract class IndirectlySortedEntriesCacheSortedByAddressAndSizeArray<TUnsafeCache> : IndirectlySortedEntriesCache<IndexedArrayRangeValueComparer<ulong>, TUnsafeCache>
            where TUnsafeCache : unmanaged, ISortedEntriesCache
        {
            protected unsafe override IndexedArrayRangeValueComparer<ulong> SortingComparer =>
                new IndexedArrayRangeValueComparer<ulong>(in Addresses, in Sizes, AllowExactlyOverlappingRegions);
            protected virtual bool AllowExactlyOverlappingRegions => false;
            public IndirectlySortedEntriesCacheSortedByAddressAndSizeArray(CachedSnapshot snapshot) : base(snapshot) { }
            protected abstract ref readonly DynamicArray<ulong> Addresses { get; }
            protected abstract ref readonly DynamicArray<ulong> Sizes { get; }
            public override ulong Address(long index) => Addresses[this[index]];
            public override ulong FullSize(long index) => Sizes[this[index]];
        }

        /// <summary>
        /// Used for entry caches that can have overlapping regions or those which border right next to each other while having sizes of 0
        /// </summary>
        public abstract class IndirectlySortedEntriesCacheSortedByAddressAndSizeArrayWithCache : IndirectlySortedEntriesCacheSortedByAddressAndSizeArray<UnsafeAddressAndSizeCache>
        {
            public IndirectlySortedEntriesCacheSortedByAddressAndSizeArrayWithCache(CachedSnapshot snapshot) : base(snapshot) { }

            public override void Preload()
            {
                var wasLoaded = Loaded;
                base.Preload();
                if (!wasLoaded)
                {
                    m_UnsafeCache = new UnsafeAddressAndSizeCache(m_Sorting, Addresses, Sizes);
                }
            }
            UnsafeAddressAndSizeCache m_UnsafeCache;
            public override UnsafeAddressAndSizeCache UnsafeCache
            {
                get
                {
                    if (Loaded)
                        return m_UnsafeCache;
                    Preload();
                    return m_UnsafeCache;
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public readonly struct UnsafeAddressAndSizeCache : CachedSnapshot.ISortedEntriesCache
        {
            public long Count => m_Addresses.Count;

            public long this[long index] => m_Sorting[index];
            readonly DynamicArray<long> m_Sorting;
            readonly DynamicArray<ulong> m_Addresses;
            readonly DynamicArray<ulong> m_Sizes;
            public ulong Address(long index) => m_Addresses[this[index]];
            public ulong FullSize(long index) => m_Sizes[this[index]];

            public UnsafeAddressAndSizeCache(DynamicArray<long> sorting, DynamicArray<ulong> addresses, DynamicArray<ulong> sizes)
            {
                m_Sorting = sorting;
                m_Addresses = addresses;
                m_Sizes = sizes;
            }
            // already preloaded
            public void Preload() { }
        }

        public class SortedNativeMemoryRegionEntriesCache : IndirectlySortedEntriesCacheSortedByAddressAndSizeArrayWithCache
        {
            public readonly DynamicArray<byte> RegionHierarchLayer;
            public SortedNativeMemoryRegionEntriesCache(CachedSnapshot snapshot) : base(snapshot)
            {
                RegionHierarchLayer = new DynamicArray<byte>(Count, Allocator.Persistent);
            }

            public override long Count => m_Snapshot.NativeMemoryRegions.Count;

            protected override bool AllowExactlyOverlappingRegions => true;
            protected override ref readonly DynamicArray<ulong> Addresses => ref m_Snapshot.NativeMemoryRegions.AddressBase;
            protected override ref readonly DynamicArray<ulong> Sizes => ref m_Snapshot.NativeMemoryRegions.AddressSize;
            public string Name(long index) => m_Snapshot.NativeMemoryRegions.MemoryRegionName[this[index]];
            public int UnsortedParentRegionIndex(long index) => m_Snapshot.NativeMemoryRegions.ParentIndex[this[index]];
            public int UnsortedFirstAllocationIndex(long index) => m_Snapshot.NativeMemoryRegions.FirstAllocationIndex[this[index]];
            public int UnsortedNumAllocations(long index) => m_Snapshot.NativeMemoryRegions.NumAllocations[this[index]];

            public override void Preload()
            {
                base.Preload();
                var count = Count;
                if (count <= 0)
                    return;

                using var regionLayerStack = new DynamicArray<(sbyte, ulong)>(0, 10, Allocator.Temp, memClear: false);
                for (long i = 0; i < count; i++)
                {
                    sbyte currentLayer = -1;
                    var regionEnd = Address(i) + FullSize(i);

                    if (regionLayerStack.Count > 0)
                    {
                        // avoid the copy
                        ref readonly var enclosingRegion = ref regionLayerStack.Peek();
                        while (regionEnd > enclosingRegion.Item2)
                        {
                            // pop layer stack until the enclosung region encompases this region
                            regionLayerStack.Pop();
                            if (regionLayerStack.Count > 0)
                                enclosingRegion = ref regionLayerStack.Peek();
                            else
                                break;
                        }
                        // if there are no enclosing regions, we are at the top level, aka -1
                        currentLayer = regionLayerStack.Count > 0 ? enclosingRegion.Item1 : (sbyte)-1;
                    }

                    regionLayerStack.Push(new(++currentLayer, Address(i)));
                    RegionHierarchLayer[i] = (byte)currentLayer;
                }
            }
            public override void Dispose()
            {
                base.Dispose();
                RegionHierarchLayer.Dispose();
            }
        }

        public class SortedManagedObjectsCache : IndirectlySortedEntriesCache<SortedManagedObjectsCache.IndexedManagedObjectAddressComparer, SortedManagedObjectsCache.UnsafeManagedObjectsCache>
        {
            /// <summary>
            /// This comparer is used to sort <seealso cref="IndirectlySortedEntriesCache.m_Sorting"/>
            /// (which is array of indices of, in the cas of this class, all <seealso cref="ManagedData.ManagedObjects"/>)
            /// based on the address of each object.
            ///
            /// This helper struct is needed as <seealso cref="ManagedObjectInfo"/> does not implement <seealso cref="IComparable{ManagedObjectInfo}"/>
            /// as it would be unclear what parameters it would compare by, and if it would forcibly be the address, that could be rather confusing.
            /// Especially since it is only needed here.
            /// </summary>
            public unsafe readonly struct IndexedManagedObjectAddressComparer : IRefComparer<long>
            {
                readonly ManagedObjectInfo* m_Ptr;
                public IndexedManagedObjectAddressComparer(in DynamicArray<ManagedObjectInfo> managedObjectInfos)
                {
                    m_Ptr = managedObjectInfos.GetUnsafeTypedPtr();
                }

                [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
                public int Compare(ref long objectAIndex, ref long objectBIndex)
                {
                    // managed objects can't have a negative size, or be nested within other managed objects, so we don't need to compare sizes as well
                    return m_Ptr[objectAIndex].PtrObject.CompareTo(m_Ptr[objectBIndex].PtrObject);
                }
            }

            public SortedManagedObjectsCache(CachedSnapshot snapshot) : base(snapshot) { }


            public override long Count => m_Snapshot.CrawledData.ManagedObjects.Count;

            protected override IndexedManagedObjectAddressComparer SortingComparer => new IndexedManagedObjectAddressComparer(in m_Snapshot.CrawledData.ManagedObjects);

            public override ulong Address(long index) => m_Snapshot.CrawledData.ManagedObjects[this[index]].PtrObject;
            public override ulong FullSize(long index) => (ulong)m_Snapshot.CrawledData.ManagedObjects[this[index]].Size;

            public override void Preload()
            {
                if (!m_Snapshot.CrawledData.Crawled)
                    throw new InvalidOperationException("Can't use SortedManagedObjectsCache before crawling is done");
                var wasLoaded = Loaded;
                base.Preload();
                if (!wasLoaded)
                {
                    m_UnsafeCache = new UnsafeManagedObjectsCache(m_Sorting, m_Snapshot.CrawledData.ManagedObjects);
                }
            }
            UnsafeManagedObjectsCache m_UnsafeCache;
            public override UnsafeManagedObjectsCache UnsafeCache
            {
                get
                {
                    if (Loaded)
                        return m_UnsafeCache;
                    Preload();
                    return m_UnsafeCache;
                }
            }
            [StructLayout(LayoutKind.Sequential)]
            public readonly struct UnsafeManagedObjectsCache : CachedSnapshot.ISortedEntriesCache
            {
                public long Count => m_ManagedObjects.Count;

                public long this[long index] => m_Sorting[index];
                readonly DynamicArray<long> m_Sorting;
                readonly DynamicArray<ManagedObjectInfo> m_ManagedObjects;
                public ulong Address(long index) => m_ManagedObjects[this[index]].PtrObject;
                public ulong FullSize(long index) => (ulong)m_ManagedObjects[this[index]].Size;

                public UnsafeManagedObjectsCache(DynamicArray<long> sorting, DynamicArray<ManagedObjectInfo> managedObjects)
                {
                    m_Sorting = sorting;
                    m_ManagedObjects = managedObjects;
                }
                // already preloaded
                public void Preload() { }
            }
        }

        public class SortedNativeAllocationsCache : IndirectlySortedEntriesCacheSortedByAddressAndSizeArray<SortedNativeAllocationsCache.UnsafeNativeAllocationsCache>
        {
            public SortedNativeAllocationsCache(CachedSnapshot snapshot) : base(snapshot) { }

            public override long Count => m_Snapshot.NativeAllocations.Count;

            protected override ref readonly DynamicArray<ulong> Addresses => ref m_Snapshot.NativeAllocations.Address;
            protected override ref readonly DynamicArray<ulong> Sizes => ref m_Snapshot.NativeAllocations.Size;

            // The allocation start address discounts padding and overhead sizes used for Allocation headers
            // (overhead size also includes potential footers but those don't matter for the start address)
            // in order to find allocations based on pointers into the allocation, we therefore need to consider the full size of the allocation.
            public override ulong FullSize(long index) => m_Snapshot.NativeAllocations.Size[this[index]] + (ulong)m_Snapshot.NativeAllocations.OverheadSize[this[index]] + (ulong)m_Snapshot.NativeAllocations.PaddingSize[this[index]];

            public int MemoryRegionIndex(long index) => m_Snapshot.NativeAllocations.MemoryRegionIndex[this[index]];
            public long RootReferenceId(long index) => m_Snapshot.NativeAllocations.RootReferenceId[this[index]];
            public ulong AllocationSiteId(long index) => m_Snapshot.NativeAllocations.AllocationSiteId[this[index]];
            public int OverheadSize(long index) => m_Snapshot.NativeAllocations.OverheadSize[this[index]];
            public int PaddingSize(long index) => m_Snapshot.NativeAllocations.PaddingSize[this[index]];

            public override void Preload()
            {
                var wasLoaded = Loaded;
                base.Preload();
                if (!wasLoaded)
                {
                    m_UnsafeCache = new UnsafeNativeAllocationsCache(m_Sorting, Addresses, Sizes, m_Snapshot.NativeAllocations.OverheadSize, m_Snapshot.NativeAllocations.PaddingSize);
                }
            }
            UnsafeNativeAllocationsCache m_UnsafeCache;
            public override UnsafeNativeAllocationsCache UnsafeCache
            {
                get
                {
                    if (Loaded)
                        return m_UnsafeCache;
                    Preload();
                    return m_UnsafeCache;
                }
            }

            [StructLayout(LayoutKind.Sequential)]
            public readonly struct UnsafeNativeAllocationsCache : CachedSnapshot.ISortedEntriesCache
            {
                public long Count => m_Addresses.Count;

                public long this[long index] => m_Sorting[index];
                readonly DynamicArray<long> m_Sorting;
                readonly DynamicArray<ulong> m_Addresses;
                readonly DynamicArray<ulong> m_Sizes;
                readonly DynamicArray<int> m_OverheadSizes;
                readonly DynamicArray<int> m_PaddingSizes;
                public ulong Address(long index) => m_Addresses[this[index]];
                public ulong FullSize(long index) => m_Sizes[this[index]] + (ulong)m_OverheadSizes[this[index]] + (ulong)m_PaddingSizes[this[index]];

                public UnsafeNativeAllocationsCache(DynamicArray<long> sorting, DynamicArray<ulong> addresses, DynamicArray<ulong> sizes, DynamicArray<int> overheadSizes, DynamicArray<int> paddingSizes)
                {
                    m_Sorting = sorting;
                    m_Addresses = addresses;
                    m_Sizes = sizes;
                    m_OverheadSizes = overheadSizes;
                    m_PaddingSizes = paddingSizes;
                }
                // already preloaded
                public void Preload() { }
            }
        }

        public class SortedNativeObjectsCache : IndirectlySortedEntriesCacheSortedByAddressArray<UnsafeAddressAndSizeCache>
        {
            public SortedNativeObjectsCache(CachedSnapshot snapshot) : base(snapshot) { }
            public override long Count => m_Snapshot.NativeObjects.Count;

            protected override ref readonly DynamicArray<ulong> Addresses => ref m_Snapshot.NativeObjects.NativeObjectAddress;
            public override ulong FullSize(long index) => m_Snapshot.NativeObjects.Size[this[index]];

            public string Name(long index) => m_Snapshot.NativeObjects.ObjectName[this[index]];
            public EntityId InstanceId(long index) => m_Snapshot.NativeObjects.InstanceId[this[index]];
            public int NativeTypeArrayIndex(long index) => m_Snapshot.NativeObjects.NativeTypeArrayIndex[this[index]];
            public HideFlags HideFlags(long index) => m_Snapshot.NativeObjects.HideFlags[this[index]];
            public ObjectFlags Flags(long index) => m_Snapshot.NativeObjects.Flags[this[index]];
            public long RootReferenceId(long index) => m_Snapshot.NativeObjects.RootReferenceId[this[index]];
            public int Refcount(long index) => m_Snapshot.NativeObjects.RefCount[this[index]];
            public int ManagedObjectIndex(long index) => m_Snapshot.NativeObjects.ManagedObjectIndex[this[index]];


            public override void Preload()
            {
                var wasLoaded = Loaded;
                base.Preload();
                if (!wasLoaded)
                {
                    m_UnsafeCache = new UnsafeAddressAndSizeCache(m_Sorting, Addresses, m_Snapshot.NativeObjects.Size);
                }
            }
            UnsafeAddressAndSizeCache m_UnsafeCache;
            public override UnsafeAddressAndSizeCache UnsafeCache
            {
                get
                {
                    if (Loaded)
                        return m_UnsafeCache;
                    Preload();
                    return m_UnsafeCache;
                }
            }
        }


        unsafe static void ConvertDynamicArrayByteBufferToManagedArray<T>(DynamicArray<byte> nativeEntryBuffer, ref T[] elements) where T : class
        {
            byte* binaryDataStream = nativeEntryBuffer.GetUnsafeTypedPtr();
            //jump over the offsets array
            long* binaryEntriesLength = (long*)binaryDataStream;
            binaryDataStream = binaryDataStream + sizeof(long) * (elements.Length + 1); //+1 due to the final element offset being at the end

            for (int i = 0; i < elements.Length; ++i)
            {
                byte* srcPtr = binaryDataStream + binaryEntriesLength[i];
                long actualLength = binaryEntriesLength[i + 1] - binaryEntriesLength[i];

                if (typeof(T) == typeof(string))
                {
                    var intLength = Convert.ToInt32(actualLength);
                    elements[i] = new string(unchecked((sbyte*)srcPtr), 0, intLength, System.Text.Encoding.UTF8) as T;
                }
                else if (typeof(T) == typeof(BitArray))
                {
                    byte[] temp = new byte[actualLength];
                    fixed (byte* dstPtr = temp)
                        UnsafeUtility.MemCpy(dstPtr, srcPtr, actualLength);

                    var arr = new BitArray(temp);
                    elements[i] = arr as T;
                }
                else
                {
                    Debug.LogError($"Use {nameof(NestedDynamicArray<byte>)} instead");
                    if (typeof(T) == typeof(byte[]))
                    {
                        var arr = new byte[actualLength];
                        fixed (void* dstPtr = arr)
                            UnsafeUtility.MemCpy(dstPtr, srcPtr, actualLength);
                        elements[i] = arr as T;
                    }
                    else if (typeof(T) == typeof(int[]))
                    {
                        var arr = new int[actualLength / sizeof(int)];
                        fixed (void* dstPtr = arr)
                            UnsafeUtility.MemCpy(dstPtr, srcPtr, actualLength);
                        elements[i] = arr as T;
                    }
                    else if (typeof(T) == typeof(ulong[]))
                    {
                        var arr = new ulong[actualLength / sizeof(ulong)];
                        fixed (void* dstPtr = arr)
                            UnsafeUtility.MemCpy(dstPtr, srcPtr, actualLength);
                        elements[i] = arr as T;
                    }
                    else if (typeof(T) == typeof(long[]))
                    {
                        var arr = new long[actualLength / sizeof(long)];
                        fixed (void* dstPtr = arr)
                            UnsafeUtility.MemCpy(dstPtr, srcPtr, actualLength);
                        elements[i] = arr as T;
                    }
                    else
                    {
                        Debug.LogErrorFormat("Unsuported type provided for conversion, type name: {0}", typeof(T).FullName);
                    }
                }
            }
        }

        /// <summary>
        /// Compact (64bit) and type-safe index into CachedSnapshot data.
        ///
        /// Nb! SourceIndex doesn't provide safety checks that the correct CachedSnapshot
        /// is used with the index and it's user's responsibility to pair index with
        /// the correct snapshot.
        /// </summary>
        [StructLayout(LayoutKind.Explicit, Size = sizeof(ulong), Pack = sizeof(ulong))]
        readonly public struct SourceIndex : IEquatable<SourceIndex>, IComparable<SourceIndex>, IEqualityComparer<SourceIndex>
        {
            const int kSourceIdShift = 56;
            const long kSourceIdMask = 0xFF;
            const long kIndexMask = 0x00FFFFFFFFFFFFFF;

            public enum SourceId : byte
            {
                None,
                SystemMemoryRegion,
                NativeMemoryRegion,
                NativeAllocation,
                ManagedHeapSection,
                NativeObject,
                ManagedObject,
                NativeType,
                ManagedType,
                NativeRootReference,
                GfxResource,
                GCHandleIndex,
                MemoryLabel,
                Scene,
                Prefab
            }

            public enum SpecialNoneCase : long
            {
                None,
                UnrootedAllocation,
                UnknownMemLabel,
                Root,
            }

            [FieldOffset(0)]
            readonly ulong m_Data;

            public readonly SourceId Id
            {
                get => (SourceId)(m_Data >> kSourceIdShift);
            }

            public readonly long Index
            {
                get => (long)(m_Data & kIndexMask);
            }

            public bool Valid => Id != SourceId.None;

            public SourceIndex(SourceId source, long index)
            {
                if (((ulong)source > kSourceIdMask) || index < 0 || ((ulong)index > kIndexMask))
                    throw new ArgumentOutOfRangeException();

                m_Data = ((ulong)source << kSourceIdShift) | ((ulong)index & kIndexMask);
            }

            public string GetName(CachedSnapshot snapshot)
            {
                switch (Id)
                {
                    case SourceId.None:
                        switch ((SpecialNoneCase)Index)
                        {
                            case SpecialNoneCase.UnrootedAllocation:
                                return UnrootedItemName;
                            case SpecialNoneCase.UnknownMemLabel:
                                return UnknownMemlabelName;
                            case SpecialNoneCase.Root:
                                return RootName;
                            default:
                                return string.Empty;
                        }

                    case SourceId.SystemMemoryRegion:
                    {
                        var name = snapshot.SystemMemoryRegions.RegionName[Index];
                        if (string.IsNullOrEmpty(name))
                        {
                            var regionType = (SystemMemoryRegionEntriesCache.MemoryType)snapshot.SystemMemoryRegions.RegionType[Index];
                            if (regionType != SystemMemoryRegionEntriesCache.MemoryType.Mapped)
                                name = regionType.ToString();
                        }
                        return name;
                    }

                    case SourceId.NativeMemoryRegion:
                        return snapshot.NativeMemoryRegions.MemoryRegionName[Index];
                    case SourceId.NativeAllocation:
                        return snapshot.NativeAllocations.ProduceAllocationNameForAllocation(snapshot, Index);
                    case SourceId.NativeObject:
                        return snapshot.NativeObjects.ObjectName[Index];
                    case SourceId.NativeType:
                        return snapshot.NativeTypes.TypeName[Index];
                    case SourceId.NativeRootReference:
                        return snapshot.NativeRootReferences.ObjectName[Index];

                    case SourceId.ManagedHeapSection:
                        return snapshot.ManagedHeapSections.SectionName(Index);
                    case SourceId.ManagedObject:
                    {
                        return snapshot.CrawledData.ManagedObjects[Index].ProduceManagedObjectName(snapshot);
                    }
                    case SourceId.ManagedType:
                        return snapshot.TypeDescriptions.TypeDescriptionName[Index];


                    case SourceId.GfxResource:
                    {
                        // Get associated memory label root
                        var rootReferenceId = snapshot.NativeGfxResourceReferences.RootId[Index];
                        if (rootReferenceId <= 0)
                            return InvalidItemName;

                        // Lookup native object index associated with memory label root
                        if (snapshot.NativeObjects.RootReferenceIdToIndex.TryGetValue(rootReferenceId, out var objectIndex))
                            return snapshot.NativeObjects.ObjectName[objectIndex];

                        // Try to see is memory label root associated with any memory area
                        if (snapshot.NativeRootReferences.IdToIndex.TryGetValue(rootReferenceId, out long rootIndex))
                            return snapshot.NativeRootReferences.AreaName[rootIndex] + ":" + snapshot.NativeRootReferences.ObjectName[rootIndex];

                        return InvalidItemName;
                    }
                    case SourceId.GCHandleIndex:
                        if (snapshot.NativeObjects.GCHandleIndexToIndex.TryGetValue(Index, out var nativeObjectIndex))
                            return new SourceIndex(SourceId.NativeObject, nativeObjectIndex).GetName(snapshot);
                        return $"GCHandle index: {Index}";
                    case SourceId.MemoryLabel:
                        return Index >= NativeMemoryLabelEntriesCache.InvalidMemLabelIndex ? snapshot.NativeMemoryLabels.MemoryLabelName[Index] : UnknownMemlabelName;
                    case SourceId.Scene:
                        return $"{snapshot.SceneRoots.Name[Index]}.scene";
                    case SourceId.Prefab:
                        return snapshot.SceneRoots.AllPrefabRootTransformSourceIndices[Index].GetName(snapshot);
                }

                Debug.Assert(false, $"Unknown source link type {Id}, please report a bug.");
                return InvalidItemName;
            }

            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            public bool Equals(SourceIndex other) => m_Data == other.m_Data;

            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            public override bool Equals(object obj) => obj is SourceIndex other && Equals(other);

            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            public bool Equals(SourceIndex x, SourceIndex y) => x.Equals(y);

            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            public override int GetHashCode() => m_Data.GetHashCode();
            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            public int GetHashCode(SourceIndex index) => index.m_Data.GetHashCode();

            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            public static bool operator ==(SourceIndex x, SourceIndex y) => x.m_Data == y.m_Data;
            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            public static bool operator !=(SourceIndex x, SourceIndex y) => x.m_Data != y.m_Data;

            public override string ToString()
            {
                return $"(Source:{Id} Index:{Index})";
            }

            readonly int IComparable<SourceIndex>.CompareTo(SourceIndex other)
            {
                var ret = Id.CompareTo(other.Id);
                if (ret == 0)
                    ret = Index.CompareTo(other.Index);
                return ret;
            }
        }

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
                            var startPointIndex = hierarchyStack[startPointStackLevel];
                            // Replace start point id with actual children count
                            m_CombinedData[startPointIndex] = new AddressPoint(m_CombinedData[startPointIndex], i - startPointIndex - 1);

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

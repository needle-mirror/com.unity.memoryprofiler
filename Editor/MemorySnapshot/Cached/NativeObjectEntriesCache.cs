using System;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Format;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;

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
using LongToLongHashMap = Unity.Collections.NativeHashMap<long, long>;
using LongToIntHashMap = Unity.Collections.NativeHashMap<long, int>;
#endif
using HideFlags = UnityEngine.HideFlags;
using Unity.MemoryProfiler.Editor.Diagnostics;

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
    internal partial class CachedSnapshot
    {
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

#if ENTITY_ID_STRUCT_AVAILABLE && !ENTITY_ID_CHANGED_SIZE
            static NativeObjectEntriesCache()
            {
                Checks.IsTrue((typeof(EntityId) != typeof(UnityEngine.EntityId)), "The wrong type of EntityId struct is used, probably due to accidentally addin a 'using UnityEngine;' to this file.");
            }
#endif

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
    }
}

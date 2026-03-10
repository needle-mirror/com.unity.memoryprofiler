using System;
using System.Collections.Generic;
using Unity.Collections;


// Pre com.unity.collections@2.1.0 NativeHashMap was not constraining its held data to unmanaged but to struct.
// NativeHashSet does not have the same issue, but for ease of use may get an alias below for EntityId.
#if !UNMANAGED_NATIVE_HASHMAP_AVAILABLE
using LongToCachedArrayInfoHashMap = Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<long, Unity.MemoryProfiler.Editor.TableEntryNameCache.CachedArrayInfo>;
#else
using LongToCachedArrayInfoHashMap = Unity.Collections.NativeHashMap<long, Unity.MemoryProfiler.Editor.TableEntryNameCache.CachedArrayInfo>;
#endif

namespace Unity.MemoryProfiler.Editor
{
    /// <summary>
    /// Caches extra data needed for table entry names that require heap bytes access.
    /// This includes:
    /// - String previews (first 30 characters) for managed string objects
    /// - Char[] previews for managed char array objects  
    /// - Native allocation names (derived from managed references to native allocations)
    /// - Array descriptions (type[length] format) for array objects
    /// 
    /// Built during crawling and persists after heap bytes are unloaded.
    /// </summary>
    internal class TableEntryNameCache : IDisposable
    {
        /// <summary>
        /// Maximum length of string previews. Matches StringTools.ReadFirstStringLineInternal's maxCharsInLine.
        /// </summary>
        public const int MaxPreviewLength = 30;

        // Map from managed object index (for string/char[] objects) to preview string
        Dictionary<long, string> m_ManagedObjectPreviews;

        // Map from native allocation index to generated name (including field info from managed references)
        Dictionary<long, string> m_NativeAllocationNames;

        // Map from managed object index (for array objects) to cached ArrayInfo
        LongToCachedArrayInfoHashMap m_ArrayInfoCache;

        // Map from CachedArrayInfo.RankIndex to cached their rank arrays
        // This moves the managed arrays out of m_ArrayInfoCache to prepare for serialization and deserialization
        // from/as NestedDynamicArray.
        Dictionary<int, int[]> m_ArrayRankCache;

        /// <summary>
        /// Cached array information that doesn't require heap bytes.
        /// </summary>
        public struct CachedArrayInfo
        {
            public int RankIndex;
            public int Length;
            public uint ElementSize;
            public int ArrayTypeDescription;
            public int ElementTypeDescription;
            public BytesAndOffset PotentiallyStaleHeapBytesHeader;
            public BytesAndOffset PotentiallyStaleHeapBytesData;

            public string GenerateArrayDescription(CachedSnapshot cachedSnapshot, long arrayIndex, bool truncateTypeName, bool includeTypeName)
            {
                // Create a temporary ArrayInfo for description generation
                return ManagedHeapArrayDataTools.GenerateArrayDescription(cachedSnapshot, ToArrayInfo(cachedSnapshot.TableEntryNames), arrayIndex, truncateTypeName, includeTypeName);
            }

            public ArrayInfo ToArrayInfo(TableEntryNameCache tableEntryNamesCache)
            {
                return new ArrayInfo
                {
                    // If RankIndex is negative, the array is single rank and the rank can be calculated as -RankIndex-1. Otherwise, look up the rank in the cache.
                    Rank = RankIndex >= 0 ? tableEntryNamesCache.m_ArrayRankCache[RankIndex] : new int[] { (-RankIndex) - 1 },
                    Length = Length,
                    ElementSize = ElementSize,
                    ArrayTypeDescription = ArrayTypeDescription,
                    ElementTypeDescription = ElementTypeDescription,
                    // ManagedHeapArrayDataTools.GenerateArrayDescription does not need valid heap bytes
                    Header = PotentiallyStaleHeapBytesHeader,
                    Data = PotentiallyStaleHeapBytesData,
                };
            }
        }

        public TableEntryNameCache(int initialCapacity = 1024)
        {
            m_ManagedObjectPreviews = new Dictionary<long, string>(initialCapacity);
            m_NativeAllocationNames = new Dictionary<long, string>(initialCapacity);
            m_ArrayInfoCache = new LongToCachedArrayInfoHashMap(initialCapacity, Allocator.Persistent);
            m_ArrayRankCache = new Dictionary<int, int[]>(initialCapacity);
        }

        /// <summary>
        /// Cache a string/char[] preview during crawling.
        /// </summary>
        /// <param name="managedObjectIndex">The managed object index of the string/char[] object.</param>
        /// <param name="preview">The preview string (already truncated).</param>
        public void CachePreview(long managedObjectIndex, string preview)
        {
            m_ManagedObjectPreviews[managedObjectIndex] = preview;
        }

        /// <summary>
        /// Try to get a cached preview.
        /// </summary>
        /// <param name="managedObjectIndex">The managed object index of the string/char[] object.</param>
        /// <param name="preview">The cached preview if found.</param>
        /// <returns>True if a preview was found in the cache.</returns>
        public bool TryGetPreview(long managedObjectIndex, out string preview)
        {
            return m_ManagedObjectPreviews.TryGetValue(managedObjectIndex, out preview);
        }

        /// <summary>
        /// Cache a native allocation name during crawling.
        /// </summary>
        /// <param name="nativeAllocationIndex">The native allocation index.</param>
        /// <param name="name">The generated name (including field info from managed references).</param>
        public void CacheNativeAllocationName(long nativeAllocationIndex, string name)
        {
            if (m_NativeAllocationNames != null)
                m_NativeAllocationNames[nativeAllocationIndex] = name;
        }

        /// <summary>
        /// Try to get a cached native allocation name.
        /// </summary>
        /// <param name="nativeAllocationIndex">The native allocation index.</param>
        /// <param name="name">The cached name if found.</param>
        /// <returns>True if a name was found in the cache.</returns>
        public bool TryGetNativeAllocationName(long nativeAllocationIndex, out string name)
        {
            if (m_NativeAllocationNames != null)
                return m_NativeAllocationNames.TryGetValue(nativeAllocationIndex, out name);
            name = null;
            return false;
        }

        /// <summary>
        /// Cache array info during crawling.
        /// </summary>
        /// <param name="managedObjectIndex">The managed object index of the array object.</param>
        /// <param name="arrayInfo">The array info to cache.</param>
        public void CacheArrayInfo(long managedObjectIndex, ArrayInfo arrayInfo)
        {
            if (arrayInfo != null)
            {
                // To save space, if the array is single rank we store the rank as a negative number in CachedArrayInfo.RankIndex instead of caching a single element int[] in m_ArrayRankCache.
                // RankIndex is -rank-1 to allow for rank 0 arrays (which are invalid but may be present in memory) to be cached as well.
                var rankIndex = -(arrayInfo.Rank[0] + 1);
                if (arrayInfo.Rank.Length > 1)
                {
                    rankIndex = m_ArrayRankCache.Count;
                    m_ArrayRankCache[rankIndex] = arrayInfo.Rank;
                }
                m_ArrayInfoCache[managedObjectIndex] = new CachedArrayInfo
                {
                    RankIndex = rankIndex,
                    Length = arrayInfo.Length,
                    ElementSize = arrayInfo.ElementSize,
                    ArrayTypeDescription = arrayInfo.ArrayTypeDescription,
                    ElementTypeDescription = arrayInfo.ElementTypeDescription,
                    PotentiallyStaleHeapBytesHeader = arrayInfo.Header,
                    PotentiallyStaleHeapBytesData = arrayInfo.Data
                };
            }
        }

        /// <summary>
        /// Try to get cached array info.
        /// </summary>
        /// <param name="managedObjectIndex">The managed object index of the array object.</param>
        /// <param name="cachedInfo">The cached array info if found.</param>
        /// <returns>True if array info was found in the cache.</returns>
        public bool TryGetArrayInfo(long managedObjectIndex, out ArrayInfo cachedArrayInfo)
        {
            if (m_ArrayInfoCache.TryGetValue(managedObjectIndex, out var cachedInfo))
            {
                cachedArrayInfo = cachedInfo.ToArrayInfo(this);
                return true;
            }
            cachedArrayInfo = default;
            return false;
        }

        public void Dispose()
        {
            if (m_ArrayInfoCache.IsCreated)
                m_ArrayInfoCache.Dispose();
        }
    }
}

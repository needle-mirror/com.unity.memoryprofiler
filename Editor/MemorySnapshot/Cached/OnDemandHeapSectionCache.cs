using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Diagnostics;
using Unity.MemoryProfiler.Editor.Format;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;
using Unity.Profiling;
using UnityEngine;

using static Unity.MemoryProfiler.Editor.CachedSnapshot.OnDemandHeapSectionCache;

namespace Unity.MemoryProfiler.Editor
{
    partial class CachedSnapshot
    {
        /// <summary>
        /// Helper method to get heap bytes with no refetch for static field bytes,
        /// fast path (RefetchHeapBytes) and fallback (FindHeapBytes).
        /// </summary>
        /// <remarks>
        /// When heap bytes are unloaded and on-demand loading is used, returns a DIRECT REFERENCE to cached data (Allocator.Persistent).
        /// The caller MUST dispose the <paramref name="referenceScope"/>
        /// <see cref="HeapSectionCacheReferenceScope"/> when done with the <paramref name="refreshedBytes"/>, unless false is returned.
        /// When heap bytes are loaded in memory, <paramref name="referenceScope"/> 
        /// will be a default scope (safe to dispose without effect
        /// and <paramref name="refreshedBytes"/> points directly to cached memory.
        /// </remarks>
        /// <param name="bytesAndOffset">Existing BytesAndOffset that might have a valid section index (fast path).</param>
        /// <param name="address">The address to find if fast path fails.</param>
        /// <param name="refreshedBytes">The resulting bytes and offset.</param>
        /// <param name="referenceScope">
        /// A reference counting scope that protects the cached data from eviction while active.
        /// Must be disposed when done accessing the data. Safe to dispose in all cases,
        /// but unnecessary when <see langword="false"/> is returned or <see cref="HeapSectionCacheReferenceScope.IsValid"/> is <see langword="false"/>.
        /// </param>
        /// <returns>True if heap bytes were loaded successfully.</returns>
        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public bool TryGetHeapBytesWithFastPath(
            BytesAndOffset bytesAndOffset,
            ulong address,
            out BytesAndOffset refreshedBytes,
            out HeapSectionCacheReferenceScope referenceScope)
        {
            // Fastest Path: Static type fields - no need to refetch, but validate they're actually valid
            if (bytesAndOffset.AreStaticTypeFields)
            {
                // Static field bytes are not unloaded, so no need to refetch
                // But we need to ensure the BytesAndOffset itself is valid
                if (bytesAndOffset.IsValid)
                {
                    refreshedBytes = bytesAndOffset;
                    // Allocate empty array to be disposed of without issues
                    referenceScope = default;
                    return true;
                }
                // If static field BytesAndOffset is invalid, fall through to normal paths
            }

            // Fast path: Use RefetchHeapBytes if section index is known
            if (bytesAndOffset.IsValid && bytesAndOffset.HeapSectionIndex >= 0)
            {
                refreshedBytes = bytesAndOffset.RefetchHeapBytes(this, out referenceScope);
                if (refreshedBytes.IsValid)
                    return true;
            }

            // Fallback: Use FindHeapBytes for binary search
            return FindHeapBytes(address, out refreshedBytes, out referenceScope);
        }

        /// <summary>
        /// Find bytes at the given address, using on-demand loading if heap bytes have been unloaded.
        /// This method should be used when detailed object inspection is needed after crawling.
        /// </summary>
        /// <remarks>
        /// When heap bytes are unloaded and on-demand loading is used, returns a DIRECT REFERENCE to cached data (Allocator.Persistent).
        /// The caller MUST dispose the <paramref name="referenceScope"/>
        /// <see cref="HeapSectionCacheReferenceScope"/> when done with the <paramref name="bytesAndOffset"/>, unless false is returned.
        /// When heap bytes are loaded in memory, <paramref name="referenceScope"/> 
        /// will be a default scope (safe to dispose without effect) 
        /// and <paramref name="bytesAndOffset"/> points directly to loaded memory.
        /// </remarks>
        /// <param name="address">The memory address to find in the managed heap.</param>
        /// <param name="bytesAndOffset">
        /// When the method returns true, contains the bytes and offset information for the requested address.
        /// The bytes point directly to cached memory.
        /// </param>
        /// <param name="referenceScope">
        /// A reference counting scope that protects the cached data from eviction while active.
        /// Must be disposed when done accessing the data. Safe to dispose in all cases,
        /// but unnecessary when <see langword="false"/> is returned or <see cref="HeapSectionCacheReferenceScope.IsValid"/> is <see langword="false"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if the address was found and bytes are available; otherwise, <see langword="false"/>.
        /// </returns>
        /// <seealso cref="FindHeapBytes(ulong, out BytesAndOffset, out MemorySectionType, out HeapSectionCacheReferenceScope)"/>
        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public bool FindHeapBytes(ulong address, out BytesAndOffset bytesAndOffset, out HeapSectionCacheReferenceScope referenceScope)
        {
            return FindHeapBytes(address, out bytesAndOffset, out _, out referenceScope);
        }

        /// <summary>
        /// Find bytes at the given address, using on-demand loading if heap bytes have been unloaded.
        /// This method should be used when detailed object inspection is needed after crawling.
        /// </summary>
        /// <remarks>
        /// <para>
        /// When heap bytes are unloaded and on-demand loading is used, returns a DIRECT REFERENCE to cached data (Allocator.Persistent).
        /// The caller MUST dispose the <paramref name="referenceScope"/>
        /// <see cref="HeapSectionCacheReferenceScope"/> when done with the <paramref name="bytesAndOffset"/>, unless false is returned.
        /// </para>
        /// <para>
        /// When heap bytes are loaded in memory, <paramref name="referenceScope"/> 
        /// will be a default scope (safe to dispose without effect) 
        /// and <paramref name="bytesAndOffset"/> points directly to loaded memory.
        /// </para>
        /// <para>
        /// This method is thread-safe and can be called concurrently from multiple threads.
        /// </para>
        /// </remarks>
        /// <param name="address">The memory address to find in the managed heap.</param>
        /// <param name="bytesAndOffset">
        /// When the method returns true, contains the bytes and offset information for the requested address.
        /// The bytes point directly to cached memory.
        /// </param>
        /// <param name="sectionType">
        /// When the method returns true, contains the type of memory section that contains the address
        /// (either <see cref="MemorySectionType.GarbageCollector"/> or <see cref="MemorySectionType.VirtualMachine"/>).
        /// </param>
        /// <param name="referenceScope">
        /// A reference counting scope that protects the cached data from eviction while active.
        /// Must be disposed when done accessing the data. Safe to dispose in all cases,
        /// but unnecessary when <see langword="false"/> is returned or <see cref="HeapSectionCacheReferenceScope.IsValid"/> is <see langword="false"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if the address was found and bytes are available; otherwise, <see langword="false"/>.
        /// </returns>
        /// <example>
        /// <code>
        /// if (snapshot.FindHeapBytes(address, out var bytes, out var type, out var referenceScope))
        /// {
        ///     try
        ///     {
        ///         int value = bytes.ReadInt32();
        ///         // Use the value...
        ///     }
        ///     finally
        ///     {
        ///         referenceScope.Dispose(); // Safe to call in all cases, but necessary to release on-demand cache reference when heap bytes were unloaded.
        ///     }
        /// }
        /// </code>
        /// </example>
        public bool FindHeapBytes(ulong address, out BytesAndOffset bytesAndOffset, out MemorySectionType sectionType, out HeapSectionCacheReferenceScope referenceScope)
        {
            // First try the in-memory heap bytes (fast path)
            if (ManagedHeapSections.HeapBytesLoaded)
            {
                bytesAndOffset = ManagedHeapSections.Find(address, VirtualMachineInformation, out sectionType);
                // referenceScope is default (safe to dispose without effect)
                // -> BytesAndOffset points to in-memory loaded heap sections
                if (bytesAndOffset.IsValid)
                {
                    referenceScope = default;
                    return true;
                }
                else
                {
                    referenceScope = default;
                    return false; // Address not found in heap sections
                }
            }

            // Get direct reference to cached data - caller must dispose referenceScope to release reference
            return OnDemandHeapCache.FindOnDemand(address, out bytesAndOffset, out sectionType, out referenceScope);
        }

        void InitializeOnDemandHeapCache()
        {
            // Create the on-demand cache
            // Copy the SortedToOriginalIndex array since we need to keep it around
            var sortedToOriginal = new DynamicArray<long>(ManagedHeapSections.SortedToOriginalIndex.Count, Allocator.Persistent);
            unsafe
            {
                UnsafeUtility.MemCpy(sortedToOriginal.GetUnsafePtr(), ManagedHeapSections.SortedToOriginalIndex.GetUnsafePtr(), ManagedHeapSections.SortedToOriginalIndex.Count * sizeof(long));
            }
            OnDemandHeapCache = new OnDemandHeapSectionCache(
                m_Reader,
                ManagedHeapSections,
                VirtualMachineInformation,
                isStackMemory: false,
                sortedToOriginal);
        }

        /// <summary>
        /// Gets the bytes for a specific heap section by index.
        /// Uses on-demand loading if heap bytes have been unloaded after crawling.
        /// </summary>
        /// <remarks>
        /// This method retrieves an entire heap section by its index. The section index must be valid
        /// (within the range [0, <see cref="ManagedMemorySectionEntriesCache.Count"/>)).
        /// See <see cref="FindHeapBytes(ulong, out BytesAndOffset, out MemorySectionType, out HeapSectionCacheReferenceScope)"/> 
        /// for information about memory management and disposal requirements.
        /// </remarks>
        /// <param name="heapSectionIndex">
        /// The zero-based index of the heap section to retrieve. 
        /// Must be in the range [0, <see cref="ManagedMemorySectionEntriesCache.Count"/>).
        /// </param>
        /// <param name="bytesAndOffset">
        /// When the method returns true, contains the bytes and offset information for the section,
        /// starting at the section's base address. Points directly to cached memory.
        /// </param>
        /// <param name="referenceScope">
        /// A reference counting scope that protects the cached data from eviction while active.
        /// Must be disposed when done accessing the data. Safe to dispose in all cases,
        /// but unnecessary when <see langword="false"/> is returned or <see cref="HeapSectionCacheReferenceScope.IsValid"/> is <see langword="false"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if the section was loaded successfully; otherwise, <see langword="false"/>.
        /// </returns>
        /// <seealso cref="GetHeapBytes(long, ulong, out BytesAndOffset, out HeapSectionCacheReferenceScope)"/>
        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public bool GetHeapBytes(long heapSectionIndex, out BytesAndOffset bytesAndOffset, out HeapSectionCacheReferenceScope referenceScope)
        {
            return GetHeapBytes(heapSectionIndex, ManagedHeapSections.StartAddress[heapSectionIndex], out bytesAndOffset, out referenceScope);
        }

        /// <summary>
        /// Gets the bytes for a specific heap section by index, with a specific address offset within that section.
        /// Uses on-demand loading if heap bytes have been unloaded after crawling.
        /// </summary>
        /// <remarks>
        /// This method retrieves a heap section by its index and creates a <see cref="BytesAndOffset"/> 
        /// positioned at a specific address within that section. The address must fall within the section's
        /// address range. This is useful when you know both the section index and the specific address you need.
        /// See <see cref="FindHeapBytes(ulong, out BytesAndOffset, out MemorySectionType, out HeapSectionCacheReferenceScope)"/> 
        /// for information about memory management and disposal requirements.
        /// </remarks>
        /// <param name="heapSectionIndex">
        /// The zero-based index of the heap section to retrieve.
        /// Must be in the range [0, <see cref="ManagedMemorySectionEntriesCache.Count"/>).
        /// </param>
        /// <param name="address">
        /// The specific memory address within the section. 
        /// Must be within the section's address range [StartAddress, StartAddress + Size).
        /// </param>
        /// <param name="bytesAndOffset">
        /// When the method returns true, contains the bytes and offset information for the section,
        /// with the offset positioned at the specified address. Points directly to cached memory.
        /// </param>
        /// <param name="referenceScope">
        /// A reference counting scope that protects the cached data from eviction while active.
        /// Must be disposed when done accessing the data. Safe to dispose in all cases,
        /// but unnecessary when <see langword="false"/> is returned or <see cref="HeapSectionCacheReferenceScope.IsValid"/> is <see langword="false"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if the section was loaded successfully and the address is valid; 
        /// otherwise, <see langword="false"/>.
        /// </returns>
        public bool GetHeapBytes(long heapSectionIndex, ulong address, out BytesAndOffset bytesAndOffset, out HeapSectionCacheReferenceScope referenceScope)
        {
            // First try the in-memory heap bytes (fast path)
            if (ManagedHeapSections.HeapBytesLoaded)
            {
                Checks.CheckIndexInRangeAndThrow((long)(address - ManagedHeapSections.StartAddress[heapSectionIndex]), (long)ManagedHeapSections.SectionSize[heapSectionIndex]);
                // No need to use the SortedToOriginalIndex mapping because we are accessing the sorted bytes
                bytesAndOffset = new BytesAndOffset(ManagedHeapSections.Bytes[heapSectionIndex], VirtualMachineInformation.PointerSize, heapSectionIndex, offset: address - ManagedHeapSections.StartAddress[heapSectionIndex]);
                // referenceScope is default (safe to dispose without effect)
                // -> BytesAndOffset points to in-memory loaded heap sections
                referenceScope = default;
                return bytesAndOffset.IsValid;
            }

            // Get direct reference to cached data - caller must dispose referenceScope to release reference
            return OnDemandHeapCache.GetOnDemand(heapSectionIndex, address, out bytesAndOffset, out _, out referenceScope);
        }

        /// <summary>
        /// Provides on-demand loading of heap sections with LRU (Least Recently Used) caching.
        /// Used when heap bytes have been unloaded after crawling to support
        /// detailed object inspection in the selection details view.
        /// Thread-safe for concurrent access from multiple threads.
        /// </summary>
        internal class OnDemandHeapSectionCache : IDisposable
        {
            public struct HeapSectionCacheReferenceScope : IDisposable
            {
                public bool IsValid => m_Cache != null;
                OnDemandHeapSectionCache m_Cache;
                long m_SectionIndex;

                internal void Acquire(OnDemandHeapSectionCache cache, long sectionIndex)
                {
                    Interlocked.Increment(ref cache.m_OnDemandSections[sectionIndex].RefCount);
                    m_Cache = cache;
                    m_SectionIndex = sectionIndex;
                }

                public void Dispose()
                {
                    if (m_Cache == null)
                        return;

                    m_Cache.m_DisposalLock.EnterReadLock();
                    try
                    {
                        if (!m_Cache.m_Disposed && m_Cache.m_OnDemandSections.IsCreated)
                        {
                            Interlocked.Decrement(ref m_Cache.m_OnDemandSections[m_SectionIndex].RefCount);
                        }
                    }
                    finally
                    {
                        m_Cache.m_DisposalLock.ExitReadLock();
                        m_Cache = null;
                    }
                }
            }
            public const string LoadSectionMarkerName = "OnDemandHeapSectionCache.LoadSection";
            static readonly ProfilerMarker k_LoadSection = new ProfilerMarker(LoadSectionMarkerName);
            public const string FindOnDemandMarkerName = "OnDemandHeapSectionCache.FindOnDemand";
            static readonly ProfilerMarker k_FindOnDemand = new ProfilerMarker(FindOnDemandMarkerName);
            public const string GetOnDemandMarkerName = "OnDemandHeapSectionCache.GetOnDemand";
            static readonly ProfilerMarker k_GetOnDemand = new ProfilerMarker(GetOnDemandMarkerName);

            /// <summary>
            /// Default maximum number of sections to keep in cache.
            /// Each section can be several MB, so this limits memory usage.
            /// </summary>
            public const int DefaultMaxCachedSections = 8;

            readonly IFileReader m_Reader;
            readonly ManagedMemorySectionEntriesCache m_SectionMetadata;
            readonly VirtualMachineInformation m_VmInfo;
            readonly EntryType m_EntryType;
            readonly int m_MaxCachedSections;

            struct OnDemandHeapSection : IDisposable
            {
                public long RefCount;
                public NestedDynamicArray<byte> Bytes;
                public long LoadedState; // 0 = not loaded, 1 = loading, 2 = loaded

                public void Dispose()
                {
                    if (Interlocked.CompareExchange(ref LoadedState, 0, 1) == 1
                        || Interlocked.CompareExchange(ref LoadedState, 0, 2) == 2)
                    {
                        if (Bytes.IsCreated)
                            Bytes.Dispose();
                    }
                }
            }

            // Thread synchronization for all non-atomic LRU operations
            readonly object m_LruLock = new object();
            // Semaphore to serialize file loading operations - prevents concurrent readOp.Complete() 
            // calls which can cause job system deadlock
            readonly SemaphoreSlim m_LoadingSemaphore = new SemaphoreSlim(1, 1);
            // LRU order: most recently used at the end
            readonly LinkedList<long> m_LruOrder;
            readonly Dictionary<long, LinkedListNode<long>> m_LruNodes;

            DynamicArray<OnDemandHeapSection> m_OnDemandSections;
            // Locks for each section to allow concurrent loading of different sections while preventing multiple loads of the same section.
            object[] m_PerSectionStripedLockPool;
            const int k_PerSectionLockPoolSize = 64;

            // Mapping from sorted section index to original file index
            // This is needed because sections are sorted by address after loading
            readonly DynamicArray<long> m_SortedToOriginalIndex;

            bool m_Disposed;
            ReaderWriterLockSlim m_DisposalLock = new();

            /// <summary>
            /// Number of currently cached sections.
            /// </summary>
            public int CachedSectionCount => m_CachedSectionCount;

            /// <summary>
            /// Updated atomically while under lock so that getting the value doesn't require locking.
            /// </summary>
            int m_CachedSectionCount = 0;

            /// <summary>
            /// Maximum number of sections to keep in cache.
            /// </summary>
            public int MaxCachedSections => m_MaxCachedSections;

            /// <summary>
            /// Total bytes currently cached.
            /// </summary>
            public long TotalCachedBytes => m_TotalCachedBytes;

            /// <summary>
            /// Updated atomically while under lock so that getting the value doesn't require locking.
            /// </summary>
            long m_TotalCachedBytes = 0;

            /// <summary>
            /// Creates a new on-demand heap section cache.
            /// </summary>
            /// <param name="reader">The file reader to load sections from.</param>
            /// <param name="sectionMetadata">The section metadata (addresses, sizes, types).</param>
            /// <param name="vmInfo">Virtual machine information for pointer size.</param>
            /// <param name="isStackMemory">True if this is for stack memory, false for heap.</param>
            /// <param name="sortedToOriginalIndex">Mapping from sorted index to original file index.</param>
            /// <param name="maxCachedSections">Maximum sections to keep in cache. The cache can temporarily exceed that if all cached sections are referenced.</param>
            public OnDemandHeapSectionCache(
                IFileReader reader,
                in ManagedMemorySectionEntriesCache sectionMetadata,
                in VirtualMachineInformation vmInfo,
                bool isStackMemory,
                DynamicArray<long> sortedToOriginalIndex,
                int maxCachedSections = DefaultMaxCachedSections)
            {
                m_Reader = reader;
                m_SectionMetadata = sectionMetadata;
                m_VmInfo = vmInfo;
                m_EntryType = isStackMemory ? EntryType.ManagedStacks_Bytes : EntryType.ManagedHeapSections_Bytes;
                m_MaxCachedSections = maxCachedSections;
                m_SortedToOriginalIndex = sortedToOriginalIndex;

                m_LruOrder = new();
                m_LruNodes = new(maxCachedSections);
                m_OnDemandSections = new DynamicArray<OnDemandHeapSection>(sectionMetadata.Count, Allocator.Persistent, memClear: true);
                m_PerSectionStripedLockPool = new object[k_PerSectionLockPoolSize];
                for (int i = 0; i < m_PerSectionStripedLockPool.Length; i++)
                {
                    m_PerSectionStripedLockPool[i] = new object();
                }
            }

            /// <summary>
            /// Find bytes at the given address, loading the section on-demand if needed.
            /// Returns a DIRECT REFERENCE to cached data (Allocator.Persistent).
            /// The reference scope protects the data from eviction while active and
            /// the caller is responsible for ensuring its disposal when done.
            /// Thread-safe: can be called from multiple threads concurrently.
            /// </summary>
            /// <param name="address">The address to find.</param>
            /// <param name="bytesAndOffset">The resulting bytes and offset if found. Points directly to the loaded data.</param>
            /// <param name="referenceScope">
            /// A reference counting scope that protects the cached data from eviction while active.
            /// Must be disposed when done accessing the data. Safe to dispose in all cases,
            /// but unnecessary when <see langword="false"/> is returned or <see cref="HeapSectionCacheReferenceScope.IsValid"/> is <see langword="false"/>.
            /// </param>
            /// <returns>True if the address was found and bytes are available.</returns>
            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            public bool FindOnDemand(ulong address, out BytesAndOffset bytesAndOffset, out HeapSectionCacheReferenceScope referenceScope)
            {
                return FindOnDemand(address, out bytesAndOffset, out _, out referenceScope);
            }

            /// <summary>
            /// Find bytes at the given address, loading the section on-demand if needed.
            /// Returns a DIRECT REFERENCE to cached data (Allocator.Persistent).
            /// The reference scope protects the data from eviction while active and
            /// the caller is responsible for ensuring its disposal when done.
            /// Thread-safe: can be called from multiple threads concurrently.
            /// </summary>
            /// <param name="address">The address to find.</param>
            /// <param name="bytesAndOffset">The resulting bytes and offset. Points directly to cached data.</param>
            /// <param name="sectionType">The type of section containing the address.</param>
            /// <param name="referenceScope">
            /// A reference counting scope that protects the cached data from eviction while active.
            /// Must be disposed when done accessing the data. Safe to dispose in all cases,
            /// but unnecessary when <see langword="false"/> is returned or <see cref="HeapSectionCacheReferenceScope.IsValid"/> is <see langword="false"/>.
            /// </param>
            /// <returns>True if the address was found and bytes are available.</returns>
            public bool FindOnDemand(ulong address, out BytesAndOffset bytesAndOffset, out MemorySectionType sectionType, out HeapSectionCacheReferenceScope referenceScope)
            {
                using (k_FindOnDemand.Auto())
                {
                    sectionType = MemorySectionType.VirtualMachine;
                    bytesAndOffset = new BytesAndOffset();
                    referenceScope = default;

                    if (m_Disposed)
                        return false;

                    // Find which section contains this address (thread-safe, read-only operation)
                    var sectionIndex = m_SectionMetadata.FindSectionContaining(address);
                    if (sectionIndex < 0)
                        return false;

                    // Try to get cached bytes or load them from file - returns direct reference
                    if (!TryGetOrLoadSection(sectionIndex, out var cachedBytesRef, out referenceScope))
                        return false;

                    var offset = address - m_SectionMetadata.StartAddress[sectionIndex];
                    bytesAndOffset = new BytesAndOffset(cachedBytesRef, m_VmInfo.PointerSize, sectionIndex, isStaticType: false, offset);
                    sectionType = m_SectionMetadata.SectionType[sectionIndex];

                    return true;
                }
            }

            /// <summary>
            /// Get a specific heap section by index, loading on-demand if needed.
            /// Returns a DIRECT REFERENCE to cached data (Allocator.Persistent).
            /// The reference scope protects the data from eviction while active.
            /// Thread-safe: can be called from multiple threads concurrently.
            /// </summary>
            /// <param name="sectionIndex">The section index to get.</param>
            /// <param name="address">The address within the section.</param>
            /// <param name="bytesAndOffset">The resulting bytes and offset. Points directly to cached data.</param>
            /// <param name="sectionType">The type of the section.</param>
            /// <param name="referenceScope">
            /// A reference counting scope that protects the cached data from eviction while active.
            /// Must be disposed when done accessing the data. Safe to dispose in all cases,
            /// but unnecessary when <see langword="false"/> is returned or <see cref="HeapSectionCacheReferenceScope.IsValid"/> is <see langword="false"/>.
            /// </param>
            /// <returns>True if the section was found or loaded successfully.</returns>
            public bool GetOnDemand(long sectionIndex, ulong address, out BytesAndOffset bytesAndOffset, out MemorySectionType sectionType, out HeapSectionCacheReferenceScope referenceScope)
            {
                using (k_GetOnDemand.Auto())
                {
                    sectionType = MemorySectionType.VirtualMachine;
                    bytesAndOffset = new BytesAndOffset();
                    referenceScope = default;

                    if (m_Disposed || sectionIndex < 0 || sectionIndex >= m_SectionMetadata.Count)
                        return false;

                    // Try to get cached bytes or load - returns direct reference
                    if (!TryGetOrLoadSection(sectionIndex, out var cachedBytesRef, out referenceScope))
                        return false;

                    var offset = address - m_SectionMetadata.StartAddress[sectionIndex];
                    bytesAndOffset = new BytesAndOffset(cachedBytesRef, m_VmInfo.PointerSize, sectionIndex, isStaticType: false, offset);
                    sectionType = m_SectionMetadata.SectionType[sectionIndex];

                    return true;
                }
            }

            /// <summary>
            /// Try to get a section from cache or load it, returning a direct reference to cached bytes.
            /// Thread-safe: multiple threads can call this for the same section.
            /// Reference counting protects the cached data from eviction while the scope is active.
            /// </summary>
            /// <param name="sectionIndex">The section to get or load.</param>
            /// <param name="cachedBytesRef">Direct reference to the cached bytes (Allocator.Persistent).</param>
            /// <param name="referenceScope">Scope that must be disposed to release the reference.</param>
            /// <returns>True if the section was found or loaded successfully.</returns>
            bool TryGetOrLoadSection(long sectionIndex, out DynamicArrayRef<byte> cachedBytesRef, out HeapSectionCacheReferenceScope referenceScope)
            {
                cachedBytesRef = default;
                referenceScope = default;
                try
                {
                    m_DisposalLock.EnterReadLock();
                    if (m_Disposed)
                    {
                        m_DisposalLock.ExitReadLock();
                        return false;
                    }
                    // Atomically acquire reference to prevent potential eviction while waiting on the lock
                    // The reference scope then ensures that the section is kept as loaded until the caller disposes of it.
                    referenceScope.Acquire(this, sectionIndex);

                    // Fast path: check if already cached 
                    lock (m_PerSectionStripedLockPool[sectionIndex % k_PerSectionLockPoolSize])
                    {
                        m_DisposalLock.ExitReadLock();
                        while (true)
                        {
                            if (Interlocked.Read(ref m_OnDemandSections[sectionIndex].LoadedState) == 2)
                            {
                                // Return direct reference to cached bytes
                                cachedBytesRef = m_OnDemandSections[sectionIndex].Bytes[0];

                                // Move to end of LRU (most recently used)
                                TouchLru(sectionIndex);

                                return true;
                            }

                            // Check if another thread is already loading this section and if so,
                            // wait for the other thread to finish loading
                            while (Interlocked.Read(ref m_OnDemandSections[sectionIndex].LoadedState) == 1)
                            {
                                Monitor.Wait(m_PerSectionStripedLockPool[sectionIndex % k_PerSectionLockPoolSize], millisecondsTimeout: 100);

                                if (m_Disposed)
                                {
                                    referenceScope.Dispose();
                                    return false;
                                }
                            }

                            // If it wasn't loaded, mark this section as being loaded by this thread
                            if (Interlocked.CompareExchange(ref m_OnDemandSections[sectionIndex].LoadedState, 1, 0) == 0)
                                break; // We set it to 1, so we are responsible for loading
                            // otherwise, check again if it's loaded or being loaded by another thread
                        }

                        // Load the section 
                        var loadSuccess = LoadSection(sectionIndex, out cachedBytesRef);
                        if (!loadSuccess)
                            referenceScope.Dispose();
                        return loadSuccess;
                    }
                }
                catch
                {
                    if (referenceScope.IsValid)
                        referenceScope.Dispose();
                    return false;
                }
                finally
                {
                    if (!m_DisposalLock.IsReadLockHeld)
                        m_DisposalLock.EnterReadLock();
                    try
                    {
                        if (!m_Disposed)
                        {
                            // Always clean up the loading tracking
                            lock (m_PerSectionStripedLockPool[sectionIndex % k_PerSectionLockPoolSize])
                            {
                                // reset to 0 (not loaded), IF it was getting loaded so that other threads can attempt to load if needed
                                Interlocked.CompareExchange(ref m_OnDemandSections[sectionIndex].LoadedState, 0, 1);
                                // Notify any threads waiting for this section
                                Monitor.PulseAll(m_PerSectionStripedLockPool[sectionIndex % k_PerSectionLockPoolSize]);
                            }
                        }
                    }
                    finally
                    {
                        m_DisposalLock.ExitReadLock();
                    }
                }
            }

            /// <summary>
            /// Load a section from the snapshot file into the cache (Allocator.Persistent).
            /// Called outside the main lock to avoid blocking other cache accesses.
            /// Returns a direct reference to the cached bytes.
            /// </summary>
            /// <param name="sectionIndex">The section to load.</param>
            /// <param name="cachedBytesRef">Direct reference to the cached bytes.</param>
            /// <returns>True if the section was loaded successfully.</returns>
            bool LoadSection(long sectionIndex, out DynamicArrayRef<byte> cachedBytesRef)
            {
                using (k_LoadSection.Auto())
                {
                    cachedBytesRef = default;
                    NestedDynamicArray<byte> nestedBytes = default;
                    if (m_Reader == null || !m_SortedToOriginalIndex.IsCreated)
                        return false;
                    try
                    {
                        // IMPORTANT: sectionIndex is already the SORTED index (by address).
                        // When heap bytes were initially loaded, they were read in file order,
                        // then sorted by address. The SortedToOriginalIndex maps sorted→original.
                        //
                        // However, the NestedDynamicSizedArrayReadOperation was also sorted,
                        // meaning Bytes[sortedIndex] contains the data for that sorted section.
                        //
                        // When loading on-demand, we need to read from the ORIGINAL file index
                        // to get the same data that was initially loaded (before sorting).
                        // Locking is not needed here since m_SortedToOriginalIndex is immutable and fully populated on construction.
                        long originalFileIndex = m_SortedToOriginalIndex[sectionIndex];

                        // Read the section bytes from file using ORIGINAL file index
                        // This ensures we get the same data that was initially loaded
                        using (var readOp = m_Reader.AsyncReadDynamicSizedArray<byte>(m_EntryType, originalFileIndex, 1, Allocator.Persistent))
                        {
                            // Serialize file loading operations to prevent job system deadlock
                            // The file reader's job system doesn't handle many concurrent Complete() calls well
                            m_LoadingSemaphore.Wait();
                            try
                            {
                                readOp.Complete();
                                nestedBytes = readOp.CompleteReadAndGetNestedResults();
                            }
                            finally
                            {
                                m_LoadingSemaphore.Release();
                            }

                            if (!readOp.IsDone)
                            {
                                // Read failed
                                return false;
                            }
                        }

                        if (nestedBytes.SectionCount == 0)
                        {
                            return false;
                        }

                        // Get reference to the loaded bytes
                        var bytesRef = nestedBytes[0];

                        // Add to cache and return direct reference

                        // section lock is held by calling context already
                        //lock (m_PerSectionLocks[sectionIndex])
                        //{
                        lock (m_LruLock)
                        {
                            if (m_Disposed)
                            {
                                nestedBytes.Dispose();
                                return false;
                            }
                            // Check if we are at capacity before adding new section to cache
                            // Evict oldest sections if at capacity
                            if (m_LruOrder.Count >= m_MaxCachedSections)
                            {
                                EvictOldestUnsafe();
                            }
                        }

                        // Double-check another thread didn't add it while we were loading
                        if (Interlocked.CompareExchange(ref m_OnDemandSections[sectionIndex].LoadedState, 2, 1) == 1)
                        {
                            m_OnDemandSections[sectionIndex].Bytes = nestedBytes;
                            // Atomic update to cached bytes and total cached bytes
                            var bytesSize = nestedBytes[0].Count;
                            Interlocked.Add(ref m_TotalCachedBytes, bytesSize);
                            Interlocked.Increment(ref m_CachedSectionCount);
                            lock (m_LruLock)
                            {
                                // if the cache was disposed while we were loading, don't touch the LRU
                                // and let the Dispose method clean up the bytes when it gets the section lock.
                                if (m_Disposed)
                                    return false;

                                var node = m_LruOrder.AddLast(sectionIndex);
                                m_LruNodes[sectionIndex] = node;
                            }

                            // Return direct reference to cached data
                            cachedBytesRef = nestedBytes[0];
                        }
                        else if (Interlocked.Read(ref m_OnDemandSections[sectionIndex].LoadedState) == 2)
                        {
                            // Another thread added it, dispose our copy and use theirs
                            nestedBytes.Dispose();

                            // Return direct reference to the existing cached data
                            cachedBytesRef = m_OnDemandSections[sectionIndex].Bytes[0];
                            TouchLru(sectionIndex);
                        }
                        //}

                        return true;
                    }
                    catch (Exception
#if DEBUG_VALIDATION
                    e
#endif
                    )
                    {
#if DEBUG_VALIDATION
                        Debug.LogException(e);
#endif
                        //lock (m_PerSectionLocks[sectionIndex]) // held by caller context already, no need to lock here
                        //{
                        // avoid (silently when DEBUG_VALIDATION is false) leaking persistent data if an exception was thrown before a loaded section was saved to the cache.
                        if (nestedBytes.IsCreated && (!m_OnDemandSections[sectionIndex].Bytes.IsCreated || !m_OnDemandSections[sectionIndex].Bytes[0].Equals(nestedBytes[0])))
                            nestedBytes.Dispose();
                        //}
                        return false;
                    }
                }
            }

            /// <summary>
            /// Move a section to the end of the LRU list (most recently used).
            /// Locks on <see cref="m_LruLock"/>.
            /// </summary>
            void TouchLru(long sectionIndex)
            {
                lock (m_LruLock)
                {
                    if (m_Disposed)
                        return;
                    if (m_LruNodes.TryGetValue(sectionIndex, out var node))
                    {
                        m_LruOrder.Remove(node);
                        m_LruOrder.AddLast(node);
                    }
                }
            }

            /// <summary>
            /// Evict the oldest (least recently used) sections from cache until we are under the cache limit
            /// or all sections have active references.
            /// Skips sections that have active references or are currently locked by other threads.
            /// Should be called while holding the lock on <see cref="m_LruLock"/>. It tries to lock each section that gets checked for unloading.
            /// Uses TryEnter to avoid deadlocks when called from within another per-section lock.
            /// </summary>
            void EvictOldestUnsafe()
            {
                if (m_LruOrder.Count == 0)
                    return;

                // Find the oldest section that has no active references and can be locked
                var node = m_LruOrder.First;
                while (node != null)
                {
                    var sectionIndex = node.Value;
                    // Use TryEnter instead of lock to avoid deadlock when EvictOldest is called
                    // while holding another per-section lock (e.g., during LoadSection).
                    // If we can't immediately acquire the lock, skip this section and try the next one.
                    bool lockTaken = false;
                    try
                    {
                        Monitor.TryEnter(m_PerSectionStripedLockPool[sectionIndex % k_PerSectionLockPoolSize], ref lockTaken);
                        if (!lockTaken)
                        {
                            // Section is locked by another thread, skip it
                            node = node.Next;
                            continue;
                        }

                        // Check if this section has active references
                        // While this is a lock-free atomic read, switching the loaded state and removing from LRU does not happen in
                        // a single atomic operation, so we still need to lock to prevent another thread assuming the ref count or loading state means it will be retained.
                        // If value is 0, safe to evict
                        if (Interlocked.Read(ref m_OnDemandSections[sectionIndex].RefCount) == 0)
                        {
                            // No active references, safe to evict, but get next node first before it'll be null due to removal of the node from the list
                            var nextNode = node.Next;

                            m_LruOrder.Remove(node);
                            m_LruNodes.Remove(sectionIndex);
                            if (Interlocked.CompareExchange(ref m_OnDemandSections[sectionIndex].LoadedState, 0, 2) == 2)
                            {
                                var bytes = m_OnDemandSections[sectionIndex].Bytes;
                                m_OnDemandSections[sectionIndex].Bytes = default;
                                // Atomic update to cached bytes and total cached bytes
                                var bytesSize = bytes[0].Count;
                                Interlocked.Add(ref m_TotalCachedBytes, -bytesSize);

                                Interlocked.Decrement(ref m_CachedSectionCount);

#if DEBUG_VALIDATION
                                unsafe
                                {
                                    // MemSet to 0xCD before disposing to help catch use-after-free bugs in callers
                                    // who keep using the BytesAndOffsets after disposing the reference scope.
                                    bytes.MemSetAllData(0xCD);
                                }
#endif
                                bytes.Dispose();
                            }
                            // Successfully evicted one section, check if we're now under the cache limit
                            if (m_LruNodes.Count < m_MaxCachedSections)
                                return;
                            node = nextNode;
                        }
                        else
                        {
                            node = node.Next;
                        }
                    }
                    finally
                    {
                        if (lockTaken)
                            Monitor.Exit(m_PerSectionStripedLockPool[sectionIndex % k_PerSectionLockPoolSize]);
                    }
                }
                // If we get here, we are still over capacity but all cached sections have active references
                // or are locked by other threads. We'll have to exceed the cache limit temporarily, which is acceptable
                // because the references will eventually be released or the locks will be released and eviction can happen later.
            }

            public void Dispose()
            {
                var isDisposing = false;
                m_DisposalLock.EnterWriteLock();
                try
                {
                    lock (m_LruLock)
                    {
                        if (!m_Disposed)
                        {
                            m_Disposed = true;
                            if (m_SortedToOriginalIndex.IsCreated)
                                m_SortedToOriginalIndex.Dispose();
                            m_LoadingSemaphore.Dispose();
                            m_LruOrder.Clear();
                            m_LruNodes.Clear();
                            isDisposing = true;
                        }
                    }
                }
                finally
                {
                    m_DisposalLock.ExitWriteLock();
                }
                // to avoid locking the per section locks within the main lock (potentially leading to an AB - BA deadlock)
                // we do the cleanup of the sections after releasing the main locks.
                if (isDisposing)
                {
                    if (m_OnDemandSections.IsCreated)
                    {
                        // cleanup any pending reads and dispose loaded section bytes
                        var relevantLockCount = Mathf.Min(k_PerSectionLockPoolSize, m_OnDemandSections.Count);
                        for (int i = 0; i < relevantLockCount; i++)
                        {
                            // but acquire a lock on the per-section lock before disposing to avoid issues from pending loading threads
                            lock (m_PerSectionStripedLockPool[i])
                            {
                                // actual disposal will happen as part of disposing the DynamicArray of sections below, but we need to make sure to acquire the lock before disposing to avoid potential race conditions by pending read operations.
                            }
                        }
                        m_OnDemandSections.DisposeArrayOfDisposables();
                        // Wake up any threads that might be waiting
                        for (int i = 0; i < relevantLockCount; i++)
                        {
                            lock (m_PerSectionStripedLockPool[i])
                            {
                                Monitor.PulseAll(m_PerSectionStripedLockPool[i]);
                            }
                        }
                    }
                    m_PerSectionStripedLockPool = null;
                }
            }
        }
    }
}

using System;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;

namespace Unity.MemoryProfiler.Editor
{
    internal partial class CachedSnapshot
    {
        /// <summary>
        /// The backend-provided managed object table (format v19,
        /// <see cref="Format.FormatVersion.ManagedConnectionsVersion"/>). CoreCLR enumerates every live
        /// managed object during capture and lists them here; Mono / IL2CPP leave it empty (Count == 0).
        /// <see cref="ManagedReferencesEntriesCache"/> and <see cref="ManagedRootsEntriesCache"/> reference
        /// objects by row index into this table, which the crawler mirrors 1:1 into
        /// <see cref="ManagedData.ManagedObjects"/> (row i → ManagedObjectIndex i).
        /// </summary>
        public class ManagedObjectsEntriesCache : IDisposable
        {
            public long Count;

            /// <summary>Raw object addresses, one per row. Row index == ManagedObjectIndex.</summary>
            public DynamicArray<ulong> Target = default;

            /// <summary>
            /// Row index into <see cref="CachedSnapshot.TypeDescriptions"/> for each object, or -1 when
            /// the backend could not resolve the type. Decoded from the packed-index column.
            /// </summary>
            public DynamicArray<long> TypeIndex = default;

            /// <summary>
            /// Number of objects of exactly each <see cref="CachedSnapshot.TypeDescriptions"/> row, not counting derived types.
            /// Not created when the table is empty.
            /// </summary>
            public DynamicArray<long> InstanceCountPerType = default;

            public ManagedObjectsEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.ManagedObjects_Target);

                if (Count == 0)
                    return;

                Target = reader.Read(EntryType.ManagedObjects_Target, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                TypeIndex = PackedIndexReader.Read(ref reader, EntryType.ManagedObjects_TypeIndex, Count, Allocator.Persistent);

                var typeCount = reader.GetEntryCount(EntryType.TypeDescriptions_TypeIndex);
                InstanceCountPerType = new DynamicArray<long>(typeCount, Allocator.Persistent, memClear: true);
                for (long i = 0; i < Count; ++i)
                {
                    var typeIndex = TypeIndex[i];
                    if (typeIndex >= 0 && typeIndex < typeCount)
                        ++InstanceCountPerType[typeIndex];
                }
            }

            public void Dispose()
            {
                Count = 0;
                Target.Dispose();
                TypeIndex.Dispose();
                InstanceCountPerType.Dispose();
            }
        }
    }
}

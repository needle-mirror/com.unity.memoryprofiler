using System;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;

namespace Unity.MemoryProfiler.Editor
{
    internal partial class CachedSnapshot
    {
        /// <summary>
        /// The backend-provided managed object→object reference edges (format v19,
        /// <see cref="Format.FormatVersion.ManagedConnectionsVersion"/>). Each row is one directed edge
        /// <c>From[i] → To[i]</c>, as row indices into <see cref="ManagedObjectsEntriesCache"/>. CoreCLR
        /// populates this from the GC's per-object reference enumeration; Mono / IL2CPP leave it empty.
        /// A <c>To</c> value of <see cref="PackedIndexReader.InvalidIndex"/> (-1) means the edge targets an
        /// object with no row in the managed object table (e.g. a frozen / non-GC-heap object); treat such
        /// edges as a reference to an uncaptured object and never dereference the -1 index.
        /// </summary>
        public class ManagedReferencesEntriesCache : IDisposable
        {
            public long Count;

            /// <summary>Source object row index into <see cref="ManagedObjectsEntriesCache"/> for each edge.</summary>
            public DynamicArray<long> FromIndex = default;

            /// <summary>
            /// Target object row index into <see cref="ManagedObjectsEntriesCache"/> for each edge, or
            /// <see cref="PackedIndexReader.InvalidIndex"/> (-1) for an uncaptured target.
            /// </summary>
            public DynamicArray<long> ToIndex = default;

            public ManagedReferencesEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.ManagedReferences_FromIndex);

                if (Count == 0)
                    return;

                FromIndex = PackedIndexReader.Read(ref reader, EntryType.ManagedReferences_FromIndex, Count, Allocator.Persistent);
                ToIndex = PackedIndexReader.Read(ref reader, EntryType.ManagedReferences_ToIndex, Count, Allocator.Persistent);
            }

            public void Dispose()
            {
                Count = 0;
                FromIndex.Dispose();
                ToIndex.Dispose();
            }
        }
    }
}

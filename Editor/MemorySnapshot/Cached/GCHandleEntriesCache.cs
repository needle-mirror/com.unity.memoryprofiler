using System;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Format;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;
namespace Unity.MemoryProfiler.Editor
{
    internal partial class CachedSnapshot
    {
        public class GCHandleEntriesCache : IDisposable
        {
            /// <summary>
            /// One row per unique GCHandle target address. Format 10+ files are unique by construction (every
            /// writer since 2019.3 dedupes before writing); older files are deduplicated once at load. Either
            /// way, every row from this point on is unique and <c>Count == UniqueCount</c> always — no
            /// downstream code needs to special-case a "duplicate" shape.
            /// </summary>
            public DynamicArray<ulong> Target = default;
            /// <summary>Number of rows in <see cref="Target"/>, always equal to <see cref="UniqueCount"/>.</summary>
            public long Count;
            /// <summary>
            /// Kept alongside <see cref="Count"/> for callers that historically distinguished the two;
            /// always equal to <see cref="Count"/> now that duplicates are removed at load. Removal
            /// candidate: every reader could collapse onto <see cref="Count"/> and this field could be
            /// deleted.
            /// </summary>
            public long UniqueCount;
            /// <summary>
            /// The file's original row count before duplicate removal. Only <see cref="ConnectionEntriesCache"/>'s
            /// pre-format-10 unified-index split needs this — that format encodes native-object connections as
            /// a single index space where a value below the GCHandles row count means "GCHandle row", so the
            /// split point must match the file's own row count, not the deduplicated one.
            /// </summary>
            public long RawCount;

            public GCHandleEntriesCache(ref IFileReader reader)
            {
                unsafe
                {
                    RawCount = UniqueCount = Count = reader.GetEntryCount(EntryType.GCHandles_Target);

                    if (Count == 0)
                        return;

                    Target = reader.Read(EntryType.GCHandles_Target, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();

                    // Only pre-2019.3 (format < NativeConnectionsAsInstanceIdsVersion) writers could emit more than
                    // one row for the same target address; every writer since then dedupes before writing.
                    if (reader.FormatVersion < FormatVersion.NativeConnectionsAsInstanceIdsVersion)
                        RemoveDuplicateTargets();
                }
            }

            /// <summary>Compacts <see cref="Target"/> to one row per unique address.</summary>
            void RemoveDuplicateTargets()
            {
                using var seenAddresses = new NativeHashSet<ulong>((int)Count, Allocator.Temp);
                var uniqueCount = 0L;
                for (long i = 0; i < Count; i++)
                {
                    var target = Target[i];
                    if (seenAddresses.Add(target))
                        Target[uniqueCount++] = target;
                }

                if (uniqueCount == Count)
                    return; // no duplicates found - the common, expected case on any modern capture

                Target.Resize(uniqueCount, memClear: false);
                UniqueCount = Count = uniqueCount;
            }

            public void Dispose()
            {
                Count = 0;
                UniqueCount = 0;
                RawCount = 0;
                Target.Dispose();
            }
        }
    }
}

using System;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;

namespace Unity.MemoryProfiler.Editor
{
    internal partial class CachedSnapshot
    {
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
    }
}

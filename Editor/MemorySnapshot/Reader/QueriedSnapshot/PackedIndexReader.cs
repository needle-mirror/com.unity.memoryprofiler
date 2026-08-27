using System;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;

namespace Unity.MemoryProfiler.Editor.Format.QueriedSnapshot
{
    /// <summary>
    /// Decodes the "packed index" columns written by the native snapshot writer
    /// (<c>SerializePackedIndex</c> in <c>ScriptingMemorySnapshot.cpp</c>).
    ///
    /// The writer stores a table of row indices in the narrowest of <see cref="ushort"/> /
    /// <see cref="uint"/> / <see cref="ulong"/> that fits the row count, and reserves the maximum
    /// value of the chosen width as the "invalid / no target" sentinel. The element width is
    /// therefore not fixed per column; it is self-describing on read (element byte size = total
    /// range size / element count).
    ///
    /// Columns using this encoding (all v19, <see cref="FormatVersion.ManagedConnectionsVersion"/>):
    /// <c>ManagedObjects_TypeIndex</c>, <c>ManagedReferences_FromIndex</c>,
    /// <c>ManagedReferences_ToIndex</c>, <c>ManagedRoots_ManagedObjectIndex</c>.
    ///
    /// The decoded output widens every element to a signed <see cref="long"/> and maps the width's
    /// sentinel to <see cref="InvalidIndex"/> (-1).
    /// </summary>
    static class PackedIndexReader
    {
        public const long InvalidIndex = -1;

        /// <summary>
        /// Reads <paramref name="count"/> packed indices from <paramref name="entry"/>, widened to
        /// <see cref="long"/> with the per-width sentinel remapped to <see cref="InvalidIndex"/>.
        /// Caller owns and must Dispose the returned array.
        /// </summary>
        public static DynamicArray<long> Read(ref IFileReader reader, EntryType entry, long count, Allocator allocator)
        {
            var result = new DynamicArray<long>(count, allocator);
            if (count == 0)
                return result;

            // Element width is self-describing: constant-size array, so bytes/element == rangeSize / count.
            // includeOffsets:false because constant-size arrays carry no per-element offset table.
            var rangeSize = reader.GetSizeForEntryRange(entry, 0, count, includeOffsets: false);
            var elementSize = rangeSize / count;

            switch (elementSize)
            {
                case sizeof(ushort):
                {
                    using var raw = reader.Read(entry, 0, count, Allocator.Temp).Result.Reinterpret<ushort>();
                    for (long i = 0; i < count; i++)
                    {
                        var v = raw[i];
                        result[i] = v == ushort.MaxValue ? InvalidIndex : v;
                    }
                    break;
                }
                case sizeof(uint):
                {
                    using var raw = reader.Read(entry, 0, count, Allocator.Temp).Result.Reinterpret<uint>();
                    for (long i = 0; i < count; i++)
                    {
                        var v = raw[i];
                        result[i] = v == uint.MaxValue ? InvalidIndex : v;
                    }
                    break;
                }
                case sizeof(ulong):
                {
                    using var raw = reader.Read(entry, 0, count, Allocator.Temp).Result.Reinterpret<ulong>();
                    for (long i = 0; i < count; i++)
                    {
                        var v = raw[i];
                        result[i] = v == ulong.MaxValue ? InvalidIndex : (long)v;
                    }
                    break;
                }
                default:
                    result.Dispose();
                    // Unconditional throw: Checks.* helpers are debug-only ([Conditional(...)]) and would
                    // otherwise let a corrupted file silently fall through with a mismatched Count/data pair.
                    throw new System.IO.IOException(
                        $"Unexpected packed-index element size {elementSize} for entry {entry}; expected 2, 4, or 8 bytes.");
            }

            return result;
        }
    }
}

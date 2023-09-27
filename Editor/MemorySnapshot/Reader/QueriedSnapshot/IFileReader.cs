using System;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;

namespace Unity.MemoryProfiler.Editor.Format.QueriedSnapshot
{
    interface IFileReader : IDisposable, IReader
    {
        bool HasOpenFile { get; }
        uint FormatVersionNumeric { get; }
        ReadError Open(string filePath);
        EntryFormat GetEntryFormat(EntryType type);
        long GetSizeForEntryRange(EntryType entry, long offset, long count, bool includeOffsets = true);
        uint GetEntryCount(EntryType entry);
        void GetEntryOffsets(EntryType entry, DynamicArray<long> buffer);
        GenericReadOperation Read(EntryType entry, DynamicArray<byte> buffer, long offset, long count, bool includeOffsets = true);
        GenericReadOperation Read(EntryType entry, long offset, long count, Allocator allocator, bool includeOffsets = true);
        unsafe ReadError ReadUnsafe(EntryType entry, void* buffer, long bufferLength, long offset, long count, bool includeOffsets = true);
        GenericReadOperation AsyncRead(EntryType entry, long offset, long count, Allocator allocator, bool includeOffsets = true);
        GenericReadOperation AsyncRead(EntryType entry, DynamicArray<byte> buffer, long offset, long count, bool includeOffsets = true);
        NestedDynamicSizedArrayReadOperation<T> AsyncReadDynamicSizedArray<T>(EntryType entry, long offset, long count, Allocator allocator) where T : unmanaged;
        void Close();
    }
}

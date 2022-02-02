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
        long GetSizeForEntryRange(EntryType entry, long offset, long count);
        uint GetEntryCount(EntryType entry);
        GenericReadOperation Read(EntryType entry, DynamicArray<byte> buffer, long offset, long count);
        GenericReadOperation Read(EntryType entry, long offset, long count, Allocator allocator);
        unsafe ReadError ReadUnsafe(EntryType entry, void* buffer, long bufferLength, long offset, long count);
        GenericReadOperation AsyncRead(EntryType entry, long offset, long count, Allocator allocator);
        GenericReadOperation AsyncRead(EntryType entry, DynamicArray<byte> buffer, long offset, long count);
        void Close();
    }
}

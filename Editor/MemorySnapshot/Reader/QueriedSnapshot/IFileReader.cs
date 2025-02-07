using System;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
#if ENABLE_CORECLR
using Allocator = Unity.Collections.AllocatorManager;
using AllocatorType = Unity.Collections.AllocatorManager.AllocatorHandle;
using static Unity.Collections.AllocatorManager;
#else
using Allocator = Unity.Collections.Allocator;
using AllocatorType = Unity.Collections.Allocator;
#endif



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
        GenericReadOperation Read(EntryType entry, long offset, long count, AllocatorType allocator, bool includeOffsets = true);
        unsafe ReadError ReadUnsafe(EntryType entry, void* buffer, long bufferLength, long offset, long count, bool includeOffsets = true);
        GenericReadOperation AsyncRead(EntryType entry, long offset, long count, AllocatorType allocator, bool includeOffsets = true);
        GenericReadOperation AsyncRead(EntryType entry, DynamicArray<byte> buffer, long offset, long count, bool includeOffsets = true);
        NestedDynamicSizedArrayReadOperation<T> AsyncReadDynamicSizedArray<T>(EntryType entry, long offset, long count, AllocatorType allocator) where T : unmanaged;
        void Close();
    }
}

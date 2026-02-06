using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;

namespace Unity.MemoryProfiler.Editor
{
    internal partial class CachedSnapshot
    {
        public class NativeCallstackSymbolEntriesCache : IDisposable
        {
            public long Count;
            public DynamicArray<ulong> Symbol = default;
            public string[] ReadableStackTrace;
            public Dictionary<ulong, long> SymbolToIndex;

            public NativeCallstackSymbolEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.NativeCallstackSymbol_Symbol);
                ReadableStackTrace = new string[Count];

                if (Count == 0)
                {
                    SymbolToIndex = new Dictionary<ulong, long>();
                    return;
                }

                Symbol = reader.Read(EntryType.NativeCallstackSymbol_Symbol, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();
                SymbolToIndex = new Dictionary<ulong, long>((int)Count);
                for (long idx = 0; idx < Count; idx++)
                {
                    SymbolToIndex[Symbol[idx]] = idx;
                }
                using (DynamicArray<byte> tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.NativeCallstackSymbol_ReadableStackTrace, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.NativeCallstackSymbol_ReadableStackTrace, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref ReadableStackTrace);
                }
            }

            public void Dispose()
            {
                Count = 0;
                Symbol.Dispose();
                ReadableStackTrace = null;
            }
        }

    }
}

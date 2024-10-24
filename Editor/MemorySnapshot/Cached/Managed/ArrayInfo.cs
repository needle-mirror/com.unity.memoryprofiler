namespace Unity.MemoryProfiler.Editor
{
    class ArrayInfo
    {
        public ulong BaseAddress;
        public int[] Rank;
        public int Length;
        public uint ElementSize;
        public int ArrayTypeDescription;
        public int ElementTypeDescription;
        public BytesAndOffset Header;
        public BytesAndOffset Data;
        public BytesAndOffset GetArrayElement(ulong index)
        {
            return Data.Add(ElementSize * index);
        }

        public ulong GetArrayElementAddress(long index)
        {
            return BaseAddress + (ulong)(ElementSize * index);
        }

        public string IndexToRankedString(long index)
        {
            return ManagedHeapArrayDataTools.ArrayRankIndexToString(Rank, index);
        }

        public string ArrayRankToString()
        {
            return ManagedHeapArrayDataTools.ArrayRankToString(Rank);
        }

        internal string GenerateArrayDescription(CachedSnapshot cachedSnapshot, long arrayIndex, bool truncateTypeName, bool includeTypeName)
        {
            return ManagedHeapArrayDataTools.GenerateArrayDescription(cachedSnapshot, this, arrayIndex, truncateTypeName, includeTypeName);
        }
    }
}

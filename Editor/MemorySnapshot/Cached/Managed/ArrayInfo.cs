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
        public BytesAndOffset GetArrayElement(uint index)
        {
            return Data.Add(ElementSize * index);
        }

        public ulong GetArrayElementAddress(int index)
        {
            return BaseAddress + (ulong)(ElementSize * index);
        }

        public string IndexToRankedString(int index)
        {
            return ManagedHeapArrayDataTools.ArrayRankIndexToString(Rank, index);
        }

        public string ArrayRankToString()
        {
            return ManagedHeapArrayDataTools.ArrayRankToString(Rank);
        }
    }
}

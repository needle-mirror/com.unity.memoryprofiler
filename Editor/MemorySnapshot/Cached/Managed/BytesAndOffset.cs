using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.MemoryProfiler.Editor.Containers;

namespace Unity.MemoryProfiler.Editor
{
    [StructLayout(LayoutKind.Sequential)]
    readonly struct BytesAndOffset
    {
        const ulong k_InvalidPtr = unchecked(0xffffffffffffffff);
        public readonly DynamicArrayRef<byte> Bytes;
        public readonly ulong Offset;
        public readonly uint PointerSize;

        // second highest bit used to indicate static type vs heap section, highest bit used for invalid indices (aka -1)
        const long k_StaticTypeFlag = 0x4000000000000000;
        readonly long m_HeapSectionOrStaticFieldsTypeIndex;
        public long HeapSectionIndex { get { return AreStaticTypeFields ? -1 : m_HeapSectionOrStaticFieldsTypeIndex; } }
        public long StaticFieldsTypeIndex { get { return AreStaticTypeFields ? (m_HeapSectionOrStaticFieldsTypeIndex & ~k_StaticTypeFlag) : -1; } }
        public bool AreStaticTypeFields { get { return m_HeapSectionOrStaticFieldsTypeIndex != -1 && (m_HeapSectionOrStaticFieldsTypeIndex & k_StaticTypeFlag) != 0; } }

        public bool IsValid { get { return Bytes.IsCreated; } }
        public long ByteCountFromOffset { get { return IsValid ? Bytes.Count - (long)Offset : -1; } }

        public BytesAndOffset(DynamicArrayRef<byte> bytes, uint pointerSize, long heapSectionOrStaticFieldsTypeIndex, bool isStaticType = false, ulong offset = 0)
            : this(bytes, pointerSize, isStaticType ? heapSectionOrStaticFieldsTypeIndex | k_StaticTypeFlag : heapSectionOrStaticFieldsTypeIndex, offset)
        {
        }
        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        BytesAndOffset(DynamicArrayRef<byte> bytes, uint pointerSize, long heapSectionOrStaticFieldsTypeIndex, ulong offset)
        {
            if (!bytes.IsCreated)
                throw new ArgumentException(nameof(bytes), " does not contain any data.");
            if (bytes.Count > 0 && (ulong)bytes.Count < offset)
                throw new ArgumentOutOfRangeException(nameof(offset), $"{nameof(offset)} is out of range.");
            Bytes = bytes;
            PointerSize = pointerSize;
            Offset = offset;
            m_HeapSectionOrStaticFieldsTypeIndex = heapSectionOrStaticFieldsTypeIndex;
        }

        public enum PtrReadError
        {
            Success,
            OutOfBounds,
            InvalidPtrSize
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public BytesAndOffset RefetchHeapBytes(CachedSnapshot cs, out CachedSnapshot.OnDemandHeapSectionCache.HeapSectionCacheReferenceScope referenceScope)
        {
            if (AreStaticTypeFields || !IsValid)
            {
                // static heap data is never unloaded and invalid data can't be refreshed, so just return the existing bytes and a default reference scope.
                referenceScope = default;
                return this;
            }
            var result = cs.GetHeapBytes(HeapSectionIndex, cs.ManagedHeapSections.StartAddress[HeapSectionIndex] + Offset, out var refreshedBytes, out referenceScope);
            if (result)
                return refreshedBytes;
            return new BytesAndOffset();
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public PtrReadError TryReadPointer(out ulong ptr)
        {
            ptr = k_InvalidPtr;

            if (Offset + PointerSize > (ulong)Bytes.Count)
                return PtrReadError.OutOfBounds;

            unsafe
            {
                switch (PointerSize)
                {
                    case VMTools.X64ArchPtrSize:
                        ptr = BitConverterExt.ToUInt64(Bytes, Offset);
                        return PtrReadError.Success;
                    case VMTools.X86ArchPtrSize:
                        ptr = BitConverterExt.ToUInt32(Bytes, Offset);
                        return PtrReadError.Success;
                    default: //should never happen
                        return PtrReadError.InvalidPtrSize;
                }
            }
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public byte ReadByte()
        {
            return Bytes[(long)Offset];
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public short ReadInt16()
        {
            return BitConverterExt.ToInt16(Bytes, Offset);
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public Int32 ReadInt32()
        {
            return BitConverterExt.ToInt32(Bytes, Offset);
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public Int32 ReadInt32(ulong additionalOffset)
        {
            return BitConverterExt.ToInt32(Bytes, Offset + additionalOffset);
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public Int64 ReadInt64()
        {
            return BitConverterExt.ToInt64(Bytes, Offset);
        }
        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public ushort ReadUInt16()
        {
            return BitConverterExt.ToUInt16(Bytes, Offset);
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public uint ReadUInt32()
        {
            return BitConverterExt.ToUInt32(Bytes, Offset);
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public ulong ReadUInt64()
        {
            return BitConverterExt.ToUInt64(Bytes, Offset);
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public bool ReadBoolean()
        {
            return BitConverterExt.ToBoolean(Bytes, Offset);
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public char ReadChar()
        {
            return BitConverterExt.ToChar(Bytes, Offset);
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public double ReadDouble()
        {
            return BitConverterExt.ToDouble(Bytes, Offset);
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public float ReadSingle()
        {
            return BitConverterExt.ToSingle(Bytes, Offset);
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public unsafe byte* GetUnsafeOffsetTypedPtr()
        {
            return Bytes.GetUnsafeTypedPtr() + Offset;
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public BytesAndOffset Add(ulong add)
        {
            return new BytesAndOffset(Bytes, PointerSize, m_HeapSectionOrStaticFieldsTypeIndex, Offset + add);
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public BytesAndOffset NextPointer()
        {
            return Add(PointerSize);
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public bool CouldFitAllocation(long sizeOfAllocation)
        {
            return Offset + (ulong)sizeOfAllocation <= (ulong)Bytes.Count;
        }
    }
}

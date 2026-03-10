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
        public bool IsValid { get { return Bytes.IsCreated; } }
        public long ByteCountFromOffset { get { return IsValid ? Bytes.Count - (long)Offset : -1; } }
        public BytesAndOffset(DynamicArrayRef<byte> bytes, uint pointerSize, ulong offset = 0)
        {
            if (!bytes.IsCreated)
                throw new ArgumentException(nameof(bytes), " does not contain any data.");
            if (bytes.Count > 0 && (ulong)bytes.Count < offset)
                throw new ArgumentOutOfRangeException(nameof(offset), $"{nameof(offset)} is out of range.");
            Bytes = bytes;
            PointerSize = pointerSize;
            Offset = offset;
        }

        public enum PtrReadError
        {
            Success,
            OutOfBounds,
            InvalidPtrSize
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
            return new BytesAndOffset(Bytes, PointerSize, Offset + add);
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

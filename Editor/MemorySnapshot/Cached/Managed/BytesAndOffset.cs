using System;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.UIContentData;
#if DEBUG_VALIDATION
using UnityEngine;
#endif

namespace Unity.MemoryProfiler.Editor
{
    readonly struct BytesAndOffset
    {
        const ulong k_InvalidPtr = unchecked(0xffffffffffffffff);
        public readonly DynamicArrayRef<byte> Bytes;
        public readonly ulong Offset;
        public readonly uint PointerSize;
        public bool IsValid { get { return Bytes.IsCreated; } }
        public BytesAndOffset(DynamicArrayRef<byte> bytes, uint pointerSize, ulong offset = 0)
        {
            if (!bytes.IsCreated)
                throw new ArgumentException(nameof(bytes), " does not contain any data.");
            // TODO: Enable after landing Array jobification (PR #562) and fix remaining issues where this would throw.
            //if (bytes.Count > 0 && (ulong)bytes.Count < offset)
            //    throw new ArgumentOutOfRangeException(nameof(offset), $"{nameof(offset)} is out of range.");
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadByte()
        {
            return Bytes[(long)Offset];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public short ReadInt16()
        {
            return BitConverterExt.ToInt16(Bytes, Offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Int32 ReadInt32()
        {
            return BitConverterExt.ToInt32(Bytes, Offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Int32 ReadInt32(ulong additionalOffset)
        {
            return BitConverterExt.ToInt32(Bytes, Offset + additionalOffset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Int64 ReadInt64()
        {
            return BitConverterExt.ToInt64(Bytes, Offset);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort ReadUInt16()
        {
            return BitConverterExt.ToUInt16(Bytes, Offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadUInt32()
        {
            return BitConverterExt.ToUInt32(Bytes, Offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong ReadUInt64()
        {
            return BitConverterExt.ToUInt64(Bytes, Offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ReadBoolean()
        {
            return BitConverterExt.ToBoolean(Bytes, Offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public char ReadChar()
        {
            return BitConverterExt.ToChar(Bytes, Offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double ReadDouble()
        {
            return BitConverterExt.ToDouble(Bytes, Offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ReadSingle()
        {
            return BitConverterExt.ToSingle(Bytes, Offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe byte* GetUnsafeOffsetTypedPtr()
        {
            return Bytes.GetUnsafeTypedPtr() + Offset;
        }

        public string ReadString(out int fullLength)
        {
            var readLength = fullLength = ReadInt32();
            var additionalOffsetForObjectHeader = 0ul;
            var size = (long)sizeof(int) + ((long)fullLength * (long)2);
            if (fullLength < 0 || !CouldFitAllocation(size))
            {
                // Why is the header not included for object data in the tables?
                // this workaround here is flakey!
                additionalOffsetForObjectHeader = 16;
                readLength = fullLength = ReadInt32(additionalOffsetForObjectHeader);
                size = (long)sizeof(int) + ((long)fullLength * (long)2);

                if (fullLength < 0 || !CouldFitAllocation(size))
                {
#if DEBUG_VALIDATION
                    Debug.LogError("Attempted to read outside of binary buffer.");
#endif
                    return "Invalid String object, " + TextContent.InvalidObjectPleaseReportABugMessage;
                }
                // find out what causes this and fix it, then remove the additionalOffsetForObjectHeader workaround
#if DEBUG_VALIDATION
                Debug.LogError("String reading is broken.");
#endif
            }
            if (fullLength > StringTools.MaxStringLengthToRead)
            {
                readLength = StringTools.MaxStringLengthToRead;
                readLength += StringTools.Elipsis.Length;
            }
            unsafe
            {
                byte* ptr = Bytes.GetUnsafeTypedPtr();
                {
                    string str = null;
                    char* begin = (char*)(ptr + (Offset + additionalOffsetForObjectHeader + sizeof(int)));
                    str = new string(begin, 0, readLength);
                    if (fullLength != readLength)
                    {
                        fixed (char* s = str, e = StringTools.Elipsis)
                        {
                            var c = s;
                            c += readLength - StringTools.Elipsis.Length;
                            UnsafeUtility.MemCpy(c, e, StringTools.Elipsis.Length);
                        }
                    }
                    return str;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BytesAndOffset Add(ulong add)
        {
            return new BytesAndOffset(Bytes, PointerSize, Offset + add);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BytesAndOffset NextPointer()
        {
            return Add(PointerSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CouldFitAllocation(long sizeOfAllocation)
        {
            return Offset + (ulong)sizeOfAllocation <= (ulong)Bytes.Count;
        }
    }
}

using System;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor
{
    readonly struct BytesAndOffset
    {
        public readonly DynamicArrayRef<byte> Bytes;
        public readonly ulong Offset;
        public readonly uint PointerSize;
        public bool IsValid { get { return Bytes.IsCreated; } }
        public BytesAndOffset(DynamicArrayRef<byte> bytes, uint pointerSize, ulong offset = 0)
        {
            if (!bytes.IsCreated)
                throw new ArgumentException(nameof(bytes), $"{nameof(bytes)} does not contain any data.");
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

        public PtrReadError TryReadPointer(out ulong ptr)
        {
            ptr = unchecked(0xffffffffffffffff);

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

        public byte ReadByte()
        {
            return Bytes[(long)Offset];
        }

        public short ReadInt16()
        {
            return BitConverterExt.ToInt16(Bytes, Offset);
        }

        public Int32 ReadInt32()
        {
            return BitConverterExt.ToInt32(Bytes, Offset);
        }

        public Int32 ReadInt32(ulong additionalOffset)
        {
            return BitConverterExt.ToInt32(Bytes, Offset + additionalOffset);
        }

        public Int64 ReadInt64()
        {
            return BitConverterExt.ToInt64(Bytes, Offset);
        }

        public ushort ReadUInt16()
        {
            return BitConverterExt.ToUInt16(Bytes, Offset);
        }

        public uint ReadUInt32()
        {
            return BitConverterExt.ToUInt32(Bytes, Offset);
        }

        public ulong ReadUInt64()
        {
            return BitConverterExt.ToUInt64(Bytes, Offset);
        }

        public bool ReadBoolean()
        {
            return BitConverterExt.ToBoolean(Bytes, Offset);
        }

        public char ReadChar()
        {
            return BitConverterExt.ToChar(Bytes, Offset);
        }

        public double ReadDouble()
        {
            return BitConverterExt.ToDouble(Bytes, Offset);
        }

        public float ReadSingle()
        {
            return BitConverterExt.ToSingle(Bytes, Offset);
        }

        public unsafe byte* GetUnsafeOffsetTypedPtr()
        {
            return Bytes.GetUnsafeTypedPtr() + Offset;
        }

        public string ReadString(out int fullLength)
        {
            var readLength = fullLength = ReadInt32();
            var additionalOffsetForObjectHeader = 0ul;
            if (fullLength < 0 || (long)Offset + (long)sizeof(int) + ((long)fullLength * (long)2) > Bytes.Count)
            {
                // Why is the header not included for object data in the tables?
                // this workaround here is flakey!
                additionalOffsetForObjectHeader = 16;
                readLength = fullLength = ReadInt32(additionalOffsetForObjectHeader);

                if (fullLength < 0 || (long)Offset + (long)sizeof(int) + ((long)fullLength * (long)2) > Bytes.Count)
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

        public BytesAndOffset Add(ulong add)
        {
            return new BytesAndOffset(Bytes, PointerSize, Offset + add);
        }

        public BytesAndOffset NextPointer()
        {
            return Add(PointerSize);
        }
    }
}

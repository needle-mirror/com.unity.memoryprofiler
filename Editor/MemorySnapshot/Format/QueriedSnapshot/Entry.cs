using System;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Editor.Diagnostics;

namespace Unity.MemoryProfiler.Editor.Format.QueriedSnapshot
{
    enum EntryFormat : ushort
    {
        Undefined = 0,
        SingleElement,
        ConstantSizeElementArray,
        DynamicSizeElementArray
    }

    [StructLayout(LayoutKind.Explicit, Size = 18, Pack = 2)]
    struct EntryHeader
    {
        [FieldOffset(0)]
        public readonly EntryFormat Format;
        [FieldOffset(2)]
        public readonly uint BlockIndex;
        //the data in this meta value needs to be interpreted based on entry format, can be entry size or entries count
        [FieldOffset(6)]
        public readonly uint EntriesMeta;
        //the Data in this meta value needs to be interpreted based on EntryFormat
        [FieldOffset(10)]
        public readonly ulong HeaderMeta;
    }

    unsafe struct Entry : IDisposable
    {
        public readonly EntryHeader Header;
        long* m_AdditionalEntryStorage;

        public uint Count
        {
            get
            {
                switch (Header.Format)
                {
                    case EntryFormat.SingleElement:
                        return 1;
                    case EntryFormat.ConstantSizeElementArray:
                        return (uint)Header.HeaderMeta;
                    case EntryFormat.DynamicSizeElementArray:
                        return Header.EntriesMeta;
                    default:
                        return 0;
                }
            }
        }

        public long ComputeByteSizeForEntryRange(long offset, long count, bool includeOffsetsMemory)
        {
            switch (Header.Format)
            {
                case EntryFormat.SingleElement:
                    return Header.EntriesMeta;
                case EntryFormat.ConstantSizeElementArray:
                    return Header.EntriesMeta * (count - offset);
                case EntryFormat.DynamicSizeElementArray:
                    long size = 0;
                    if (count + offset == Count)
                    {
                        var entryOffset = m_AdditionalEntryStorage[offset];
                        size = (long)(Header.HeaderMeta - (ulong)entryOffset); //adding the size of the last element
                    }
                    else
                        size = (m_AdditionalEntryStorage[offset + count] - m_AdditionalEntryStorage[offset]);

                    return size + (includeOffsetsMemory ? (UnsafeUtility.SizeOf<long>() * (count + 1)) : 0);
                default:
                    return 0;
            }
        }

        public Entry(EntryHeader header)
        {
            m_AdditionalEntryStorage = null;
            Header = header;

            switch (Header.Format)
            {
                // we read uint64 and that's the offset in the block
                case EntryFormat.SingleElement:
                //we cast the uint64 value to uint32 in order to recover the array size
                case EntryFormat.ConstantSizeElementArray:
                    break;
                case EntryFormat.DynamicSizeElementArray:
                    //read from the second index and override the meta value stored in the header with total size
                    m_AdditionalEntryStorage = (long*)UnsafeUtility.Malloc(sizeof(long) * header.EntriesMeta, UnsafeUtility.AlignOf<long>(), Allocator.Persistent);
                    break;
                case EntryFormat.Undefined:
                    //Unwritten block, should be skipped
                    break;
                default:
                    Checks.ThrowExceptionGeneric<IOException>("Invalid chapter format");
                    break;
            }
        }

        public void Dispose()
        {
            if (m_AdditionalEntryStorage != null)
            {
                UnsafeUtility.Free(m_AdditionalEntryStorage, Allocator.Persistent);
                m_AdditionalEntryStorage = null;
            }
        }

        public unsafe long* GetAdditionalStoragePtr() { return m_AdditionalEntryStorage; }
    }
}

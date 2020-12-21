using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.IO.LowLevel.Unsafe;
using Unity.MemoryProfiler.Editor.Format.LowLevel.IO;
using Unity.Profiling;
using Unity.MemoryProfiler.Editor.Diagnostics;
using Unity.MemoryProfiler.Editor.Containers;

namespace Unity.MemoryProfiler.Editor.Format.QueriedSnapshot
{
    public struct ScheduleResult
    {
        public ReadHandle handle;
        public ReadError error;
    }

    //Queried Memory snapshot file format:
    //
    //==============================================
    // Address | Content                           |
    //==============================================    < Blocks Header >
    //  [0x00] :  HeaderSignature
    //  -------------------------------------------     < Blocks Data >
    //         :  Blocks 0 Chunk 0
    //         :  Blocks 0 Chunk 1
    //         :  Blocks 1 Chunk 0
    //         :  Blocks 0 Chunk 2
    //         :  Blocks 2 Chunk 0
    //               ...
    //         :  Blocks I Chunk J
    //  -------------------------------------------     < Blocks Data Header >
    //  [0xA0] :  Block[0]
    //  [0xA1] :  Block[1]
    //  [0xA2] :  Block[2]
    //               ...
    //  [0xAN] :  Block[N]
    // --------------------------------------------     < Blocks Directory >
    //  [0xB0] :  BlockSectionVersion
    //         :    N    (Number of blocks)
    //         :  [0xA0] (Block[0] Address)            - Chunks are not continuous in the file
    //         :  [0xA1] (Block[1] Address)
    //         :  [0xA2] (Block[2] Address)
    //               ...
    //         :  [0xAN] (Block[N] Address)
    // --------------------------------------------\    < Chapters Data >
    //  [0xC0] :  Chapter 1 (header)               |
    //  [0xC1] :  Chapter 2 (header)               |
    //  [0xC2] :  Chapter 5 (header)               |- less than Format::QueriedSnapshot::EntryType::Count
    //               ...                           |
    //  [0xC3] :  Chapter M (header)               |
    // --------------------------------------------/
    //  [0xD0] :  DirectorySignature     < Chapter Directory Header >
    //  [0xD1] :  ChapterSectionVersion
    //  [0xD2] :  [0xB0] (BlockSection Address)
    // ------------------------------------------- \    < Chapter Directory >
    //  [0xE0] :  [0xC0] (Chapter 1 Address)       |
    //         :  [0xC1] (Chapter 2 Address)       |
    //         :    0   (Chapter Don't exist)      |
    //              ...                            |- exactly FileFormat::kEntryTypeCount
    //         :    0   (Chapter Don't exist)      |
    //         :  [0xC3] (Chapter M Address)       |
    //         :    0   (Chapter Don't exist)      |
    // --------------------------------------------/
    //  [0xF0]:  [0xD0] (Directory Address)             < Footer >
    //  [0xF1]:  FooterSignature
    //==============================================
    //
    // Legend:
    //  <Chapter> - EntryType eg: NativeObjectName
    public struct FileReader : IDisposable, IReader
    {
        [StructLayout(LayoutKind.Explicit, Size = 16, Pack = 4)]
        struct Blob16Byte{}

        enum FormatSignature : uint
        {
            HeaderSignature = 0xAEABCDCD,
            DirectorySignature = 0xCDCDAEAB,
            FooterSignature = 0xABCDCDAE,
            ChapterSectionVersion = 0x20170724,
            BlockSectionVersion = 0x20170724
        }

        static ProfilerMarker s_Open = new ProfilerMarker("QueriedSnapshot.FileReader.Open");
        static ProfilerMarker s_Read = new ProfilerMarker("QueriedSnapshot.Filereader.Read");
        static ProfilerMarker s_AsyncRead = new ProfilerMarker("QueriedSnapshot.Filereader.AsyncRead");

        uint m_Version;
        LowLevelFileReader m_FileReader;
        NativeArray<Entry> m_Entries;
        NativeArray<Block> m_Blocks;

        public string FullPath
        {
            get { return m_FileReader.FilePath; }
        }

        public uint FormatVersion
        {
            get { return m_Version; }
        }

        public ReadError Open(string filePath)
        {
            using (s_Open.Auto())
            {
                var error = InternalOpen(filePath);
                m_Version = 0;
                if (error == ReadError.Success)
                {
                    unsafe
                    {
                        uint vBuffer = 0;
                        byte* versionPtr = (byte*)&vBuffer;
                        ReadUnsafe(EntryType.Metadata_Version, versionPtr, sizeof(uint), 0, 1);
                        m_Version = vBuffer;
                    }
                }
                return error;
            }
        }

        public EntryFormat GetEntryFormat(EntryType type)
        {
            return m_Entries[(int)type].Header.Format;
        }

        public ulong GetSizeForEntryRange(EntryType entry, uint offset, uint count)
        {
            Checks.CheckEntryTypeValueIsValidAndThrow(entry);
            var entryData = m_Entries[(int)entry];
            Checks.CheckIndexOutOfBoundsAndThrow(offset + count, entryData.Count);

            return entryData.ComputeByteSizeForEntryRange(offset, count, true);
        }

        public uint GetEntryCount(EntryType entry)
        {
            Checks.CheckEntryTypeValueIsValidAndThrow(entry);
            var entryData = m_Entries[(int)entry];

            return entryData.Count;
        }

        public ReadError Read(EntryType entry, NativeArray<byte> buffer, uint offset, uint count)
        {
            unsafe
            {
                return ReadUnsafe(entry, buffer.GetUnsafePtr(), buffer.Length, offset, count);
            }
        }

        public unsafe ReadError ReadUnsafe(EntryType entry, void* buffer, int bufferLength, uint offset, uint count)
        {
            using (s_Read.Auto())
            {
                var res = InternalRead(entry, offset, count, buffer, bufferLength);

                if (res.error == ReadError.InProgress)
                {
                    res.handle.JobHandle.Complete();
                    res.error = res.handle.Status == ReadStatus.Complete ? ReadError.Success : ReadError.FileReadFailed;
                }
                return res.error;
            }
        }

        public GenericReadOperation AsyncRead(EntryType entry, uint offset, uint count, Allocator allocator)
        {
            var readSize = GetSizeForEntryRange(entry, offset, count);
            return InternalAsyncRead(entry, new NativeArray<byte>((int)readSize, allocator), offset, count, true);
        }

        public GenericReadOperation AsyncRead(EntryType entry, NativeArray<byte> buffer, uint offset, uint count)
        {
            return InternalAsyncRead(entry, buffer, offset, count, false);
        }

        GenericReadOperation InternalAsyncRead(EntryType entry, NativeArray<byte> buffer, uint offset, uint count, bool ownsBuffer)
        {
            using (s_AsyncRead.Auto())
            {
                unsafe
                {
                    var res = InternalRead(entry, offset, count, buffer.GetUnsafePtr(), buffer.Length);
                    GenericReadOperation asyncOp = null;
                    if (res.error != ReadError.InProgress)
                    {
                        asyncOp = new GenericReadOperation(default(ReadHandle), default(NativeArray<byte>));
                        asyncOp.Error = res.error;
                        return asyncOp;
                    }
                    return new GenericReadOperation(res.handle, buffer);
                }
            }
        }

        unsafe ReadError InternalOpen(string filePath)
        {
            Dispose();
            m_FileReader = new LowLevelFileReader(filePath);

            //first 8byte are entriesCount offset, the next are blockCount offset
            Blob16Byte fileOffsets;
            var error = TryGetBlockEntriesOffsetsWithIntegrityChecks(m_FileReader, out fileOffsets);
            if (error != ReadError.Success)
                return error;

            long* fileOffsetsPtr = (long*)(&fileOffsets);
            int* counts = stackalloc int[2];
            ReadCommand* commands = stackalloc ReadCommand[2];

            //read entry offset count
            commands[0] = ReadCommandBufferUtils.GetCommand(counts, sizeof(int), *fileOffsetsPtr);
            //read block offset count
            commands[1] = ReadCommandBufferUtils.GetCommand(counts + 1, sizeof(int), *(fileOffsetsPtr + 1));

            if (m_FileReader.Read(commands, 2, ReadMode.Blocking).Status != ReadStatus.Complete)
                return ReadError.FileReadFailed;

            if (*(counts + 1) < 1)
                return ReadError.InvalidBlockSectionCount;

            if (*counts > (int)EntryType.Count)
                *counts = (int)EntryType.Count;

            var entryTypeToChapterOffset = new NativeArray<long>(counts[0], Allocator.TempJob, NativeArrayOptions.ClearMemory);
            var dataBlockOffsets = new NativeArray<long>(counts[1], Allocator.TempJob);

            //read entry offsets
            commands[0] = ReadCommandBufferUtils.GetCommand(entryTypeToChapterOffset.GetUnsafePtr(), sizeof(long) * counts[0], commands[0].Offset + sizeof(int));
            //read block offsets
            commands[1] = ReadCommandBufferUtils.GetCommand(dataBlockOffsets.GetUnsafePtr(), sizeof(long) * counts[1], commands[1].Offset + sizeof(uint));

            if (m_FileReader.Read(commands, 2, ReadMode.Blocking).Status != ReadStatus.Complete)
                return ReadError.FileReadFailed;

            error = BuildDataBlocks(m_FileReader, dataBlockOffsets, out m_Blocks);
            dataBlockOffsets.Dispose(); //dispose of the block offsets as they are no longer needed

            if (error != ReadError.Success)
                return error;

            error = BuildDataEntries(m_FileReader, entryTypeToChapterOffset, out m_Entries);
            entryTypeToChapterOffset.Dispose();

            if (error != ReadError.Success)
                return error;

            return ReadError.Success;
        }

        unsafe static ReadError TryGetBlockEntriesOffsetsWithIntegrityChecks(LowLevelFileReader file, out Blob16Byte blockEntriesOffsets)
        {
            const int readCommandCount = 3;
            Blob16Byte offsets;
            FormatSignature* sig = stackalloc FormatSignature[2];
            ReadCommand* readCommands = stackalloc ReadCommand[readCommandCount];
            //read first chapter offset
            long _8ByteBuffer = -1;


            //read header sig
            readCommands[0] = ReadCommandBufferUtils.GetCommand(sig, sizeof(uint), 0);
            //read tail sig
            readCommands[1] = ReadCommandBufferUtils.GetCommand(sig + 1, sizeof(uint), file.FileLength - sizeof(uint));
            //read chapters start offset
            readCommands[2] = ReadCommandBufferUtils.GetCommand(&_8ByteBuffer, sizeof(ulong), readCommands[1].Offset - sizeof(ulong));

            if (file.Read(readCommands, readCommandCount, ReadMode.Blocking).Status != ReadStatus.Complete)
                return ReadError.FileReadFailed;

            if (*sig != FormatSignature.HeaderSignature)
                return ReadError.InvalidHeaderSignature;

            if (*(sig + 1) != FormatSignature.FooterSignature)
                return ReadError.InvalidFooterSignature;

            if (!(_8ByteBuffer < file.FileLength && _8ByteBuffer > 0))
                return ReadError.InvalidChapterLocation;

            //read directory signature
            readCommands[0] = ReadCommandBufferUtils.GetCommand(sig, sizeof(uint), _8ByteBuffer);
            //read chapter version
            readCommands[1] = ReadCommandBufferUtils.GetCommand(sig + 1, sizeof(uint), _8ByteBuffer + sizeof(uint));
            //read blocks offset
            readCommands[2] = ReadCommandBufferUtils.GetCommand(&_8ByteBuffer, sizeof(ulong), readCommands[1].Offset + sizeof(uint));

            if (file.Read(readCommands, 3, ReadMode.Blocking).Status != ReadStatus.Complete)
                return ReadError.FileReadFailed;

            if (*sig != FormatSignature.DirectorySignature)
                return ReadError.InvalidDirectorySignature;

            if (*(sig + 1) != FormatSignature.ChapterSectionVersion)
                return ReadError.InvalidChapterSectionVersion;

            //computed offset in file for entries
            var tmpEntriesOffset = readCommands[2].Offset + sizeof(ulong);

            readCommands[0] = ReadCommandBufferUtils.GetCommand(sig, sizeof(uint), _8ByteBuffer);
            if (file.Read(readCommands, 1, ReadMode.Blocking).Status != ReadStatus.Complete)
                return ReadError.FileReadFailed;

            if (*sig != FormatSignature.BlockSectionVersion)
                return ReadError.InvalidBlockSectionVersion;

            long* dataPtr = (long*)(&offsets);
            *dataPtr++ = tmpEntriesOffset;
            *dataPtr = _8ByteBuffer + sizeof(uint);
            blockEntriesOffsets = offsets;

            return ReadError.Success;
        }

        unsafe static ReadError BuildDataEntries(LowLevelFileReader file, NativeArray<long> entryTypeOffsets, out NativeArray<Entry> entryStorage)
        {
            entryStorage = new NativeArray<Entry>(entryTypeOffsets.Length, Allocator.Persistent);

            using (var headers = new NativeArray<EntryHeader>(entryTypeOffsets.Length, Allocator.TempJob, NativeArrayOptions.ClearMemory))
            {
                uint writtenCommands = 0;
                EntryHeader* headersPtr = (EntryHeader*)headers.GetUnsafePtr();
                using (var readCommands = new NativeArray<ReadCommand>(entryTypeOffsets.Length, Allocator.TempJob))
                {
                    ReadCommand* readCommandsPtr = (ReadCommand*)readCommands.GetUnsafePtr();
                    for (int i = 0; i < entryTypeOffsets.Length; ++i)
                    {
                        var offset = entryTypeOffsets[i];
                        if (offset != 0)
                        {
                            readCommandsPtr[writtenCommands++] = ReadCommandBufferUtils.GetCommand((headersPtr + i), sizeof(EntryHeader), offset);
                        }
                    }

                    if (file.Read(readCommandsPtr, writtenCommands, ReadMode.Blocking).Status != ReadStatus.Complete)
                        return ReadError.FileReadFailed;

                    writtenCommands = 0;
                    for (int i = 0; i < headers.Length; ++i)
                    {
                        var entry = new Entry(*(headersPtr + i));
                        entryStorage[i] = entry;
                        var header = (EntryHeader*)(&entry);


                        if (header->Format == EntryFormat.DynamicSizeElementArray)
                        {
                            readCommandsPtr[writtenCommands++] = ReadCommandBufferUtils.GetCommand(entry.GetAdditionalStoragePtr()
                                , sizeof(long) * entry.Count, entryTypeOffsets[i] + sizeof(EntryHeader));
                        }
                    }

                    if (file.Read(readCommandsPtr, writtenCommands, ReadMode.Blocking).Status != ReadStatus.Complete)
                        return ReadError.FileReadFailed;
                }
            }

            var entriesBegin = (Entry*)entryStorage.GetUnsafePtr();
            var entriesEnd = entriesBegin + entryStorage.Length;

            int counter = 0;
            while (entriesBegin != entriesEnd)
            {
                if (entriesBegin->Header.Format == EntryFormat.DynamicSizeElementArray)
                {
                    //swap back the first entry we read during the header read with the total size at the end of the entries array
                    //also memmove the array by one to the right to make space for the first entry
                    //This is required as we should not have to take cache hits when computing size by having to always jump to the end of the array and back
                    long* storagePtr = entriesBegin->GetAdditionalStoragePtr();
                    long* headerMetaPtr = (long*)((byte*)entriesBegin + sizeof(EntryFormat) + sizeof(uint) * 2);
                    long totalSize = storagePtr[entriesBegin->Count - 1];
                    UnsafeUtility.MemMove(storagePtr + 1, storagePtr, sizeof(long) * (entriesBegin->Count - 1));
                    *storagePtr = *headerMetaPtr;
                    *headerMetaPtr = totalSize;
                }
                ++counter;
                ++entriesBegin;
            }

            return ReadError.Success;
        }

        unsafe static ReadError BuildDataBlocks(LowLevelFileReader file, NativeArray<long> blockOffsets, out NativeArray<Block> blockStorage)
        {
            blockStorage = new NativeArray<Block>(blockOffsets.Length, Allocator.Persistent);
            using (var blockReads = new NativeArray<ReadCommand>(blockOffsets.Length, Allocator.Temp))
            {
                using (var headers = new NativeArray<BlockHeader>(blockOffsets.Length, Allocator.Temp))
                {
                    BlockHeader* headerPtr = (BlockHeader*)headers.GetUnsafePtr();
                    ReadCommand* commandPtr = (ReadCommand*)blockReads.GetUnsafePtr();

                    for (int i = 0; i < blockStorage.Length; ++i)
                    {
                        var blockOffset = blockOffsets[i];
                        *commandPtr++ = ReadCommandBufferUtils.GetCommand(headerPtr++, sizeof(BlockHeader), blockOffset);
                    }

                    commandPtr = (ReadCommand*)blockReads.GetUnsafePtr();
                    if (file.Read((ReadCommand*)blockReads.GetUnsafePtr(), (uint)blockReads.Length, ReadMode.Blocking).Status != ReadStatus.Complete)
                        return ReadError.FileReadFailed;

                    for (int i = 0; i < blockStorage.Length; ++i)
                    {
                        var blockOffset = blockOffsets[i];
                        var block = new Block(headers[i]);
                        blockStorage[i] = block;

                        *commandPtr++ = ReadCommandBufferUtils.GetCommand(block.GetOffsetsPtr(), sizeof(long) * block.OffsetCount, blockOffset + sizeof(BlockHeader));
                    }
                    commandPtr = (ReadCommand*)blockReads.GetUnsafePtr();
                    if (file.Read((ReadCommand*)blockReads.GetUnsafePtr(), (uint)blockReads.Length, ReadMode.Blocking).Status != ReadStatus.Complete)
                        return ReadError.FileReadFailed;
                }
            }

            return ReadError.Success;
        }

        unsafe ScheduleResult InternalRead(EntryType entry, uint offset, uint count, void* buffer, int bufferLength)
        {
            Checks.CheckEntryTypeValueIsValidAndThrow(entry);
            var result = new ScheduleResult();

            var entryData = m_Entries[(int)entry];
            if (entryData.Count < 1)
            {
                result.error = ReadError.EmptyFormatEntry;
                return result; //guard against reading empty format entries
            }

            Checks.CheckIndexOutOfBoundsAndThrow(offset, count);
            Checks.CheckIndexOutOfBoundsAndThrow((long)entryData.ComputeByteSizeForEntryRange(offset, count, true), bufferLength);
            long rangeByteSize = (long)entryData.ComputeByteSizeForEntryRange(offset, count, false);

            var bufferPtr = (byte*)buffer;
            ReadHandle readHandle = default(ReadHandle);
            switch (entryData.Header.Format)
            {
                case EntryFormat.SingleElement:
                    readHandle = ScheduleSingleElementEntryReads(entryData, rangeByteSize, bufferPtr);
                    result.error = ReadError.InProgress;
                    break;
                case EntryFormat.ConstantSizeElementArray:
                    readHandle = ScheduleConstSizeElementArrayRead(entryData, offset, rangeByteSize, bufferPtr);
                    result.error = ReadError.InProgress;
                    break;
                case EntryFormat.DynamicSizeElementArray:

                    bool readHeaderMeta = count + offset == entryData.Count;
                    long dynamicEntryLengthsArray = (count + 1) * sizeof(long);

                    //dynamic entries require x bytes in front of the data to store lengths
                    UnsafeUtility.MemCpy(bufferPtr, entryData.GetAdditionalStoragePtr() + offset, dynamicEntryLengthsArray - (readHeaderMeta ? sizeof(long) : 0));

                    if (readHeaderMeta)
                    {
                        var lastOffset = ((long*)bufferPtr) + count;
                        *lastOffset = (long)entryData.Header.HeaderMeta;
                    }

                    //shift the offsets, so that we remove the lengths of the skipped elements
                    if (offset > 0)
                    {
                        var offsetDiff = entryData.GetAdditionalStoragePtr()[offset];
                        long* offsetsPtr = (long*)bufferPtr;
                        for (int i = 0; i < count + 1; ++i)
                        {
                            var offsetVal = *offsetsPtr;
                            *offsetsPtr++ = offsetVal - offsetDiff;
                        }
                    }

                    //offset to jump over where we copied the lengths
                    bufferPtr = bufferPtr + dynamicEntryLengthsArray;
                    readHandle = ScheduleDynamicSizeElementArrayReads(entryData, offset, count, rangeByteSize, bufferPtr);
                    result.error = ReadError.InProgress;
                    break;
                default:
                    result.error = ReadError.InvalidEntryFormat;
                    break;
            }

            result.handle = readHandle;
            return result;
        }

        unsafe ReadHandle ScheduleSingleElementEntryReads(Entry entry, long readSize, void* dst)
        {
            var block = m_Blocks[(int)entry.Header.BlockIndex];
            var blockOffset = entry.Header.HeaderMeta;
            var chunkSize = block.Header.ChunkSize;
            uint chunkIndex = (uint)(blockOffset / chunkSize);
            var chunk = block.GetOffsetsPtr() + chunkIndex;
            var chunkWithLocalOffset = *chunk + (uint)(blockOffset % chunkSize);

            var readCmd = ReadCommandBufferUtils.GetCommand(dst, readSize, chunkWithLocalOffset);
            return m_FileReader.Read(&readCmd, 1, ReadMode.Async);
        }

        unsafe ReadHandle ScheduleConstSizeElementArrayRead(Entry entry, uint firstElement, long readSize, void* dst)
        {
            var block = m_Blocks[(int)entry.Header.BlockIndex];
            var blockOffset = entry.Header.EntriesMeta * firstElement;
            var chunkSize = block.Header.ChunkSize;
            var chunkIndex = (uint)(blockOffset / chunkSize);
            var chunk = block.GetOffsetsPtr() + chunkIndex;
            var chunkWithLocalOffset = *chunk + (uint)(blockOffset % chunkSize);

            byte* dstPtr = (byte*)dst;
            using (NativeBlockList<ReadCommand> chunkReads = new NativeBlockList<ReadCommand>(64, 64))
            {
                var readCmd = ReadCommandBufferUtils.GetCommand(dstPtr, readSize, chunkWithLocalOffset);
                chunkReads.Push(readCmd);
                dstPtr += (long)(chunkSize - (blockOffset % chunkSize));
                readSize -= (long)(chunkSize - (blockOffset % chunkSize));

                while (readSize > 0)
                {
                    ++chunkIndex;
                    var chunkReadSize = Math.Min(readSize, (long)chunkSize);
                    chunk = block.GetOffsetsPtr() + chunkIndex;

                    readCmd = ReadCommandBufferUtils.GetCommand(dstPtr, chunkReadSize, *chunk);
                    dstPtr += chunkReadSize;
                    readSize -= chunkReadSize;
                    chunkReads.Push(readCmd);
                }

                //TODO: find a way to use the block array chunks directly for scheduling, probably add readcommandbuffer
                using (var tempCmds = new NativeArray<ReadCommand>((int)chunkReads.Count, Allocator.TempJob))
                {
                    ReadCommand* cmdPtr = (ReadCommand*)tempCmds.GetUnsafePtr();
                    for (int i = 0; i < tempCmds.Length; ++i)
                        *(cmdPtr + i) = chunkReads[i];

                    return m_FileReader.Read(cmdPtr, (uint)tempCmds.Length, ReadMode.Async);
                }
            }
        }

        unsafe struct ElementRead
        {
            public long start;
            public long end;
            public byte* readDst;
        }

        //Returns ammount of written bytes
        unsafe static long ProcessDynamicSizeElement(ref NativeBlockList<ReadCommand> chunkReads, ref Block block, ElementRead elementRead)
        {
            long written = 0;
            var chunkSize = (long)block.Header.ChunkSize;
            var elementSize = elementRead.end - elementRead.start;
            var chunkIndex = elementRead.start / chunkSize;
            var chunkOffset = block.GetOffsetsPtr()[chunkIndex];
            var elementOffsetInChunk = elementRead.start % chunkSize;
            var remainingChunksize = chunkSize - elementOffsetInChunk;

            var rSize = Math.Min(chunkSize, elementSize);
            if (remainingChunksize != chunkSize)
            {
                chunkOffset += elementOffsetInChunk; //align the read
                if (rSize > remainingChunksize)
                    rSize = remainingChunksize;
            }

            chunkReads.Push(ReadCommandBufferUtils.GetCommand(elementRead.readDst, rSize, chunkOffset));
            elementRead.readDst += rSize;
            elementSize -= rSize;
            written += rSize;

            //if the element spans multiple chunks
            while (elementSize > 0)
            {
                chunkIndex++;
                chunkOffset = block.GetOffsetsPtr()[chunkIndex];
                rSize = Math.Min(chunkSize, elementSize);
                chunkReads.Push(ReadCommandBufferUtils.GetCommand(elementRead.readDst, rSize, chunkOffset));

                elementRead.readDst += rSize;
                elementSize -= rSize;
                written += rSize;
            }

            return written;
        }

        //Dynamic size entries are split into chunks that are spread around the file. We need to read per chunk
        unsafe ReadHandle ScheduleDynamicSizeElementArrayReads(Entry entry, uint elementOffset, uint elementCount, long readSize, void* buffer)
        {
            var block = m_Blocks[(int)entry.Header.BlockIndex];
            byte* dst = (byte*)buffer;

            var chunkReads = new NativeBlockList<ReadCommand>(64, 64);
            for (int i = 0; i < elementCount; ++i)
            {
                var e = new ElementRead();
                e.start = entry.GetAdditionalStoragePtr()[i + elementOffset];
                e.end = i + elementOffset + 1 == entry.Count ? (long)entry.Header.HeaderMeta : entry.GetAdditionalStoragePtr()[i + elementOffset + 1];
                e.readDst = dst;
                var readOffset = ProcessDynamicSizeElement(ref chunkReads, ref block, e);
                dst += readOffset;
                readSize -= readOffset;
            }

            Checks.CheckEquals(0, readSize);

            //TODO: find a way to use the block array chunks directly for scheduling, probably add readcommandbuffer
            using (NativeArray<ReadCommand> readCommands = new NativeArray<ReadCommand>((int)chunkReads.Count, Allocator.Temp))
            {
                var cmdsPtr = (ReadCommand*)readCommands.GetUnsafePtr();

                for (int i = 0; i < readCommands.Length; ++i)
                    *(cmdsPtr + i) = chunkReads[i];
                chunkReads.Dispose();
                return m_FileReader.Read(cmdsPtr, (uint)readCommands.Length, ReadMode.Async);
            }
        }

        public void Dispose()
        {
            if (!m_FileReader.IsCreated)
                return;

            m_FileReader.Dispose();
            if (m_Blocks.IsCreated)
            {
                for (int i = 0; i < m_Blocks.Length; ++i)
                    m_Blocks[i].Dispose();

                m_Blocks.Dispose();
            }
            if (m_Entries.IsCreated)
            {
                for (int i = 0; i < m_Entries.Length; ++i)
                    m_Entries[i].Dispose();

                m_Entries.Dispose();
            }
        }
    }
}

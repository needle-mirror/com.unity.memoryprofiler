using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Format;
using Unity.MemoryProfiler.Editor.UIContentData;
using Unity.Profiling;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor
{
    internal struct ManagedConnection
    {
        public enum ConnectionType
        {
            ManagedObject_To_ManagedObject,
            ManagedType_To_ManagedObject,
            UnityEngineObject,
        }
        public ManagedConnection(ConnectionType t, int from, int to, int fieldFrom, int arrayIndexFrom)
        {
            connectionType = t;
            index0 = from;
            index1 = to;
            this.fieldFrom = fieldFrom;
            this.arrayIndexFrom = arrayIndexFrom;
        }

        private int index0;
        private int index1;

        public int fieldFrom;
        public int arrayIndexFrom;

        public ConnectionType connectionType;
        public long GetUnifiedIndexFrom(CachedSnapshot snapshot)
        {
            switch (connectionType)
            {
                case ConnectionType.ManagedObject_To_ManagedObject:
                    return snapshot.ManagedObjectIndexToUnifiedObjectIndex(index0);
                case ConnectionType.ManagedType_To_ManagedObject:
                    return index0;
                case ConnectionType.UnityEngineObject:
                    return snapshot.NativeObjectIndexToUnifiedObjectIndex(index0);
                default:
                    return -1;
            }
        }

        public long GetUnifiedIndexTo(CachedSnapshot snapshot)
        {
            switch (connectionType)
            {
                case ConnectionType.ManagedObject_To_ManagedObject:
                case ConnectionType.ManagedType_To_ManagedObject:
                case ConnectionType.UnityEngineObject:
                    return snapshot.ManagedObjectIndexToUnifiedObjectIndex(index1);
                default:
                    return -1;
            }
        }

        public int fromManagedObjectIndex
        {
            get
            {
                switch (connectionType)
                {
                    case ConnectionType.ManagedObject_To_ManagedObject:
                    case ConnectionType.ManagedType_To_ManagedObject:
                        return index0;
                }
                return -1;
            }
        }
        public int toManagedObjectIndex
        {
            get
            {
                switch (connectionType)
                {
                    case ConnectionType.ManagedObject_To_ManagedObject:
                    case ConnectionType.ManagedType_To_ManagedObject:
                        return index1;
                }
                return -1;
            }
        }

        public int fromManagedType
        {
            get
            {
                if (connectionType == ConnectionType.ManagedType_To_ManagedObject)
                {
                    return index0;
                }
                return -1;
            }
        }
        public int UnityEngineNativeObjectIndex
        {
            get
            {
                if (connectionType == ConnectionType.UnityEngineObject)
                {
                    return index0;
                }
                return -1;
            }
        }
        public int UnityEngineManagedObjectIndex
        {
            get
            {
                if (connectionType == ConnectionType.UnityEngineObject)
                {
                    return index1;
                }
                return -1;
            }
        }
        public static ManagedConnection MakeUnityEngineObjectConnection(int NativeIndex, int ManagedIndex)
        {
            return new ManagedConnection(ConnectionType.UnityEngineObject, NativeIndex, ManagedIndex, 0, 0);
        }

        public static ManagedConnection MakeConnection(CachedSnapshot snapshot, int fromIndex, ulong fromPtr, int toIndex, ulong toPtr, int fromTypeIndex, int fromField, int fieldArrayIndexFrom)
        {
            if (fromIndex >= 0)
            {
                //from an object
#if DEBUG_VALIDATION
                if (fromField >= 0)
                {
                    if (snapshot.FieldDescriptions.IsStatic[fromField] == 1)
                    {
                        Debug.LogError("Cannot make a connection from an object using a static field.");
                    }
                }
#endif
                return new ManagedConnection(ConnectionType.ManagedObject_To_ManagedObject, fromIndex, toIndex, fromField, fieldArrayIndexFrom);
            }
            else if (fromTypeIndex >= 0)
            {
                //from a type static data
#if DEBUG_VALIDATION
                if (fromField >= 0)
                {
                    if (snapshot.FieldDescriptions.IsStatic[fromField] == 0)
                    {
                        Debug.LogError("Cannot make a connection from a type using a non-static field.");
                    }
                }
#endif
                return new ManagedConnection(ConnectionType.ManagedType_To_ManagedObject, fromTypeIndex, toIndex, fromField, fieldArrayIndexFrom);
            }
            else
            {
                throw new InvalidOperationException("Tried to add a Managed Connection without a valid source.");
            }
        }
    }

#pragma warning disable CS0660 // Type defines operator == or operator != but does not override Object.Equals(object o)
#pragma warning disable CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()
    internal struct ManagedObjectInfo
#pragma warning restore CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()
#pragma warning restore CS0660 // Type defines operator == or operator != but does not override Object.Equals(object o)
    {
        public ulong PtrObject;
        public ulong PtrTypeInfo;
        public int NativeObjectIndex;
        public int ManagedObjectIndex;
        public int ITypeDescription;
        public int Size;
        public int RefCount;

        public bool IsKnownType()
        {
            return ITypeDescription >= 0;
        }

        public BytesAndOffset data;

        public bool IsValid()
        {
            return PtrObject != 0 && PtrTypeInfo != 0 && data.bytes.IsCreated;
        }

        public static bool operator ==(ManagedObjectInfo lhs, ManagedObjectInfo rhs)
        {
            return lhs.PtrObject == rhs.PtrObject
                && lhs.PtrTypeInfo == rhs.PtrTypeInfo
                && lhs.NativeObjectIndex == rhs.NativeObjectIndex
                && lhs.ManagedObjectIndex == rhs.ManagedObjectIndex
                && lhs.ITypeDescription == rhs.ITypeDescription
                && lhs.Size == rhs.Size
                && lhs.RefCount == rhs.RefCount;
        }

        public static bool operator !=(ManagedObjectInfo lhs, ManagedObjectInfo rhs)
        {
            return !(lhs == rhs);
        }
    }

    internal class ManagedData
    {
        public bool Crawled { private set; get; }
        const int k_ManagedObjectBlockSize = 32768;
        const int k_ManagedConnectionsBlockSize = 65536;
        public BlockList<ManagedObjectInfo> ManagedObjects { private set; get; }
        public Dictionary<ulong, int> MangedObjectIndexByAddress { private set; get; }
        public BlockList<ManagedConnection> Connections { private set; get; }
        public Dictionary<int, int> NativeUnityObjectTypeIndexToManagedBaseTypeIndex { get; private set; }
        public ulong ManagedObjectMemoryUsage { private set; get; }
        public ulong AbandonedManagedObjectMemoryUsage { private set; get; }
        public ulong ActiveHeapMemoryUsage { private set; get; }
        public ulong ActiveHeapMemoryEmptySpace { private set; get; }
        public ulong AbandonedManagedObjectActiveHeapMemoryUsage { private set; get; }
        // ConnectionsMappedToUnifiedIndex and ConnectionsMappedToNativeIndex are derived structure used in accelerating searches in the details view
        public Dictionary<long, List<int>> ConnectionsToMappedToUnifiedIndex { private set; get; } = new Dictionary<long, List<int>>();
        public Dictionary<long, List<int>> ConnectionsFromMappedToUnifiedIndex { private set; get; } = new Dictionary<long, List<int>>();
        public Dictionary<long, List<int>> ConnectionsMappedToNativeIndex { private set; get; } = new Dictionary<long, List<int>>();

#if DEBUG_VALIDATION
        // This Dictionary block is here to make the investigations for PROF-2420 easier.
        public Dictionary<int, ManagedObjectInfo> InvalidManagedObjectsReportedViaGCHandles { private set; get; } = new Dictionary<int, ManagedObjectInfo>();
#endif

        public ManagedData(long rawGcHandleCount, long rawConnectionsCount)
        {
            //compute initial block counts for larger snapshots
            ManagedObjects = new BlockList<ManagedObjectInfo>(k_ManagedObjectBlockSize, rawGcHandleCount);
            Connections = new BlockList<ManagedConnection>(k_ManagedConnectionsBlockSize, rawConnectionsCount);

            MangedObjectIndexByAddress = new Dictionary<ulong, int>();
            NativeUnityObjectTypeIndexToManagedBaseTypeIndex = new Dictionary<int, int>();
        }

        internal void AddUpTotalMemoryUsage(CachedSnapshot.ManagedMemorySectionEntriesCache managedMemorySections)
        {
            var totalManagedObjectsCount = ManagedObjects.Count;
            ManagedObjectMemoryUsage = 0;
            if (managedMemorySections.Count <= 0)
            {
                ActiveHeapMemoryUsage = AbandonedManagedObjectMemoryUsage = 0;

                return;
            }

            var activeHeapSectionStartAddress = managedMemorySections.StartAddress[managedMemorySections.FirstAssumedActiveHeapSectionIndex];
            var activeHeapSectionEndAddress = managedMemorySections.StartAddress[managedMemorySections.LastAssumedActiveHeapSectionIndex] + managedMemorySections.SectionSize[managedMemorySections.LastAssumedActiveHeapSectionIndex];
            for (int i = 0; i < totalManagedObjectsCount; i++)
            {
                var size = (ulong)ManagedObjects[i].Size;
                ManagedObjectMemoryUsage += size;
                if (ManagedObjects[i].RefCount == 0)
                    AbandonedManagedObjectMemoryUsage += size;

                if (ManagedObjects[i].PtrObject > activeHeapSectionStartAddress && ManagedObjects[i].PtrObject < activeHeapSectionEndAddress)
                {
                    ActiveHeapMemoryUsage += size;
                    if (ManagedObjects[i].RefCount == 0)
                        AbandonedManagedObjectActiveHeapMemoryUsage += size;
                }
            }
            ActiveHeapMemoryEmptySpace = managedMemorySections.StartAddress[managedMemorySections.LastAssumedActiveHeapSectionIndex]
                + managedMemorySections.SectionSize[managedMemorySections.LastAssumedActiveHeapSectionIndex]
                - managedMemorySections.StartAddress[managedMemorySections.FirstAssumedActiveHeapSectionIndex]
                - ActiveHeapMemoryUsage;
        }

        internal void FinishedCrawling()
        {
            Crawled = true;
        }

        public void CreateConnectionMaps(CachedSnapshot cs)
        {
            for (var i = 0; i < Connections.Count; i++)
            {
                var key = Connections[i].GetUnifiedIndexTo(cs);
                if (ConnectionsToMappedToUnifiedIndex.TryGetValue(key, out var unifiedIndexList))
                    unifiedIndexList.Add(i);
                else
                    ConnectionsToMappedToUnifiedIndex[key] = new List<int> { i };
            }

            for (var i = 0; i < Connections.Count; i++)
            {
                var key = Connections[i].GetUnifiedIndexFrom(cs);
                if (ConnectionsFromMappedToUnifiedIndex.TryGetValue(key, out var unifiedIndexList))
                    unifiedIndexList.Add(i);
                else
                    ConnectionsFromMappedToUnifiedIndex[key] = new List<int> { i };
            }

            for (var i = 0; i < Connections.Count; i++)
            {
                var key = Connections[i].UnityEngineNativeObjectIndex;
                if (ConnectionsMappedToNativeIndex.TryGetValue(key, out var nativeObjectList))
                    nativeObjectList.Add(i);
                else
                    ConnectionsMappedToNativeIndex[key] = new List<int> { i };
            }
        }
    }

    internal readonly struct BytesAndOffset
    {
        public readonly DynamicArray<byte> bytes;
        public readonly ulong offset;
        public readonly uint pointerSize;
        public bool IsValid { get { return bytes.IsCreated; } }
        public BytesAndOffset(DynamicArray<byte> bytes, uint pointerSize, ulong offset = 0)
        {
            if (!bytes.IsCreated)
                throw new ArgumentException(nameof(bytes), $"{nameof(bytes)} does not contain any data.");
            this.bytes = bytes;
            this.pointerSize = pointerSize;
            this.offset = offset;
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

            if (offset + pointerSize > (ulong)bytes.Count)
                return PtrReadError.OutOfBounds;

            unsafe
            {
                switch (pointerSize)
                {
                    case VMTools.X64ArchPtrSize:
                        ptr = BitConverterExt.ToUInt64(bytes, offset);
                        return PtrReadError.Success;
                    case VMTools.X86ArchPtrSize:
                        ptr = BitConverterExt.ToUInt32(bytes, offset);
                        return PtrReadError.Success;
                    default: //should never happen
                        return PtrReadError.InvalidPtrSize;
                }
            }
        }

        public byte ReadByte()
        {
            return bytes[(long)offset];
        }

        public short ReadInt16()
        {
            return BitConverterExt.ToInt16(bytes, offset);
        }

        public Int32 ReadInt32()
        {
            return BitConverterExt.ToInt32(bytes, offset);
        }

        public Int32 ReadInt32(ulong additionalOffset)
        {
            return BitConverterExt.ToInt32(bytes, offset + additionalOffset);
        }

        public Int64 ReadInt64()
        {
            return BitConverterExt.ToInt64(bytes, offset);
        }

        public ushort ReadUInt16()
        {
            return BitConverterExt.ToUInt16(bytes, offset);
        }

        public uint ReadUInt32()
        {
            return BitConverterExt.ToUInt32(bytes, offset);
        }

        public ulong ReadUInt64()
        {
            return BitConverterExt.ToUInt64(bytes, offset);
        }

        public bool ReadBoolean()
        {
            return BitConverterExt.ToBoolean(bytes, offset);
        }

        public char ReadChar()
        {
            return BitConverterExt.ToChar(bytes, offset);
        }

        public double ReadDouble()
        {
            return BitConverterExt.ToDouble(bytes, offset);
        }

        public float ReadSingle()
        {
            return BitConverterExt.ToSingle(bytes, offset);
        }

        public unsafe byte* GetUnsafeOffsetTypedPtr()
        {
            return bytes.GetUnsafeTypedPtr() + offset;
        }

        public string ReadString(out int fullLength)
        {
            var readLength = fullLength = ReadInt32();
            var additionalOffsetForObjectHeader = 0ul;
            if (fullLength < 0 || (long)offset + (long)sizeof(int) + ((long)fullLength * (long)2) > bytes.Count)
            {
                // Why is the header not included for object data in the tables?
                // this workaround here is flakey!
                additionalOffsetForObjectHeader = 16;
                readLength = fullLength = ReadInt32(additionalOffsetForObjectHeader);

                if (fullLength < 0 || (long)offset + (long)sizeof(int) + ((long)fullLength * (long)2) > bytes.Count)
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
                byte* ptr = bytes.GetUnsafeTypedPtr();
                {
                    string str = null;
                    char* begin = (char*)(ptr + (offset + additionalOffsetForObjectHeader + sizeof(int)));
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
            return new BytesAndOffset(bytes, pointerSize, offset + add);
        }

        public BytesAndOffset NextPointer()
        {
            return Add(pointerSize);
        }
    }

    internal static class Crawler
    {
        internal struct StackCrawlData
        {
            public ulong ptr;
            public ulong ptrFrom;
            public int typeFrom;
            public int indexOfFrom;
            public int fieldFrom;
            public int fromArrayIndex;
        }

        class IntermediateCrawlData
        {
            public List<int> TypesWithStaticFields { private set; get; }
            public Stack<StackCrawlData> CrawlDataStack { private set; get; }
            public BlockList<ManagedObjectInfo> ManagedObjectInfos { get { return CachedMemorySnapshot.CrawledData.ManagedObjects; } }
            public BlockList<ManagedConnection> ManagedConnections { get { return CachedMemorySnapshot.CrawledData.Connections; } }
            public CachedSnapshot CachedMemorySnapshot { private set; get; }
            public Stack<int> DuplicatedGCHandleTargetsStack { private set; get; }
            public HashSet<int> TypesWithObjectsThatMayStillNeedNativeTypeConnection { private set; get; }
            public ulong TotalManagedObjectMemoryUsage { set; get; }
            const int kInitialStackSize = 256;
            public IntermediateCrawlData(CachedSnapshot snapshot)
            {
                DuplicatedGCHandleTargetsStack = new Stack<int>(kInitialStackSize);
                CachedMemorySnapshot = snapshot;
                CrawlDataStack = new Stack<StackCrawlData>();
                TypesWithObjectsThatMayStillNeedNativeTypeConnection = new HashSet<int>();

                TypesWithStaticFields = new List<int>();
                for (long i = 0; i != snapshot.TypeDescriptions.Count; ++i)
                {
                    if (snapshot.TypeDescriptions.StaticFieldBytes.Count(i) > 0)
                    {
                        TypesWithStaticFields.Add(snapshot.TypeDescriptions.TypeIndex[i]);
                    }
                }
            }
        }

        static readonly ProfilerMarker k_ConnectNativeToManageObjectProfilerMarker = new ProfilerMarker("Crawler.ConnectNativeToManageObject");
        static readonly ProfilerMarker k_ConnectRemainingManagedTypesToNativeTypesProfilerMarker = new ProfilerMarker("Crawler.ConnectRemainingManagedTypesToNativeTypes");
        static readonly ProfilerMarker k_ReportManagedTypeToNativeTypeConnectionForThisTypeAndItsBaseTypesProfilerMarker = new ProfilerMarker("Crawler.ReportManagedTypeToNativeTypeConnectionForThisTypeAndItsBaseTypes");

        static void GatherIntermediateCrawlData(CachedSnapshot snapshot, IntermediateCrawlData crawlData)
        {
            unsafe
            {
                var uniqueHandlesPtr = (ulong*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<ulong>() * snapshot.GcHandles.Count, UnsafeUtility.AlignOf<ulong>(), Collections.Allocator.Temp);

                ulong* uniqueHandlesBegin = uniqueHandlesPtr;
                int writtenRange = 0;

                // Parse all handles
                for (int i = 0; i != snapshot.GcHandles.Count; i++)
                {
                    var moi = new ManagedObjectInfo();
                    var target = snapshot.GcHandles.Target[i];

                    moi.ManagedObjectIndex = i;
                    moi.ITypeDescription = -1;

                    //this can only happen pre 19.3 scripting snapshot implementations where we dumped all handle targets but not the handles.
                    //Eg: multiple handles can have the same target. Future facing we need to start adding that as we move forward
                    if (snapshot.CrawledData.MangedObjectIndexByAddress.ContainsKey(target))
                    {
                        moi.PtrObject = target;
                        crawlData.DuplicatedGCHandleTargetsStack.Push(i);
                    }
                    else
                    {
                        snapshot.CrawledData.MangedObjectIndexByAddress.Add(target, moi.ManagedObjectIndex);
                        *(uniqueHandlesBegin++) = target;
                        ++writtenRange;
                    }

                    crawlData.ManagedObjectInfos.Add(moi);
                }
                uniqueHandlesBegin = uniqueHandlesPtr; //reset iterator
                ulong* uniqueHandlesEnd = uniqueHandlesPtr + writtenRange;
                //add handles for processing
                while (uniqueHandlesBegin != uniqueHandlesEnd)
                {
                    crawlData.CrawlDataStack.Push(new StackCrawlData { ptr = UnsafeUtility.ReadArrayElement<ulong>(uniqueHandlesBegin++, 0), ptrFrom = 0, typeFrom = -1, indexOfFrom = -1, fieldFrom = -1, fromArrayIndex = -1 });
                }
                UnsafeUtility.Free(uniqueHandlesPtr, Collections.Allocator.Temp);
            }
        }

        public static IEnumerator Crawl(CachedSnapshot snapshot)
        {
            const int stepCount = 5;
            var status = new EnumerationUtilities.EnumerationStatus(stepCount);

            IntermediateCrawlData crawlData = new IntermediateCrawlData(snapshot);

            //Gather handles and duplicates
            status.StepStatus = "Gathering snapshot managed data.";
            yield return status;
            GatherIntermediateCrawlData(snapshot, crawlData);

            //crawl handle data
            status.IncrementStep();
            status.StepStatus = "Crawling GC handles.";
            yield return status;
            while (crawlData.CrawlDataStack.Count > 0)
            {
                CrawlPointer(crawlData);
            }

            //crawl data pertaining to types with static fields and enqueue any heap objects
            status.IncrementStep();
            status.StepStatus = "Crawling data types with static fields";
            yield return status;
            for (int i = 0; i < crawlData.TypesWithStaticFields.Count; i++)
            {
                var iTypeDescription = crawlData.TypesWithStaticFields[i];
                var bytesOffset = new BytesAndOffset(snapshot.TypeDescriptions.StaticFieldBytes[iTypeDescription], snapshot.VirtualMachineInformation.PointerSize);
                CrawlRawObjectData(crawlData, bytesOffset, iTypeDescription, true, 0, -1);
            }

            //crawl handles belonging to static instances
            status.IncrementStep();
            status.StepStatus = "Crawling static instances heap data.";
            yield return status;
            while (crawlData.CrawlDataStack.Count > 0)
            {
                CrawlPointer(crawlData);
            }

            //copy crawled object source data for duplicate objects
            foreach (var i in crawlData.DuplicatedGCHandleTargetsStack)
            {
                var ptr = snapshot.CrawledData.ManagedObjects[i].PtrObject;
                snapshot.CrawledData.ManagedObjects[i] = snapshot.CrawledData.ManagedObjects[snapshot.CrawledData.MangedObjectIndexByAddress[ptr]];
            }

            //crawl connection data
            status.IncrementStep();
            status.StepStatus = "Crawling connection data";
            yield return status;

            // these key Unity Types will never show up as objects of their managed base type as they are only ever used via derived types
            if (snapshot.TypeDescriptions.ITypeUnityMonoBehaviour >= 0 && snapshot.NativeTypes.MonoBehaviourIdx >= 0)
            {
                snapshot.CrawledData.NativeUnityObjectTypeIndexToManagedBaseTypeIndex.Add(snapshot.NativeTypes.MonoBehaviourIdx, snapshot.TypeDescriptions.ITypeUnityMonoBehaviour);
            }
            if (snapshot.TypeDescriptions.ITypeUnityScriptableObject >= 0 && snapshot.NativeTypes.ScriptableObjectIdx >= 0)
            {
                snapshot.CrawledData.NativeUnityObjectTypeIndexToManagedBaseTypeIndex.Add(snapshot.NativeTypes.ScriptableObjectIdx, snapshot.TypeDescriptions.ITypeUnityScriptableObject);
            }
            if (snapshot.TypeDescriptions.ITypeUnityComponent >= 0 && snapshot.NativeTypes.ComponentIdx >= 0)
            {
                snapshot.CrawledData.NativeUnityObjectTypeIndexToManagedBaseTypeIndex.Add(snapshot.NativeTypes.ComponentIdx, snapshot.TypeDescriptions.ITypeUnityComponent);
            }
            ConnectNativeToManageObject(crawlData);
            ConnectRemainingManagedTypesToNativeTypes(crawlData);
            AddupRawRefCount(snapshot);

            snapshot.CrawledData.AddUpTotalMemoryUsage(crawlData.CachedMemorySnapshot.ManagedHeapSections);
            snapshot.CrawledData.CreateConnectionMaps(snapshot);
            snapshot.CrawledData.FinishedCrawling();
        }

        static void AddupRawRefCount(CachedSnapshot snapshot)
        {
            for (long i = 0; i != snapshot.Connections.Count; ++i)
            {
                int iManagedTo = snapshot.UnifiedObjectIndexToManagedObjectIndex(snapshot.Connections.To[i]);
                if (iManagedTo >= 0)
                {
                    var obj = snapshot.CrawledData.ManagedObjects[iManagedTo];
                    ++obj.RefCount;
#if DEBUG_VALIDATION
                    // This whole if block is here to make the investigations for PROF-2420 easier.
                    // It does not manage to fix up all faulty managed objects, but can add extra context to some.
                    if(snapshot.CrawledData.ManagedObjects[iManagedTo].NativeObjectIndex == -1)
                    {
                        int iMissingNativeFrom = snapshot.UnifiedObjectIndexToNativeObjectIndex(snapshot.Connections.From[i]);
                        if (iMissingNativeFrom >= 0)
                        {
                            obj.NativeObjectIndex = iMissingNativeFrom;
                            var nativeTypeIndex = snapshot.NativeObjects.NativeTypeArrayIndex[iMissingNativeFrom];
                            if (obj.ITypeDescription == -1 && snapshot.CrawledData.NativeUnityObjectTypeIndexToManagedBaseTypeIndex.TryGetValue(
                                nativeTypeIndex, out var managedBaseTypeIndex)
                                && managedBaseTypeIndex >= 0)
                                obj.ITypeDescription = managedBaseTypeIndex;

                            var GCHandleReported = snapshot.CrawledData.InvalidManagedObjectsReportedViaGCHandles.ContainsKey(obj.ManagedObjectIndex);
                            snapshot.CrawledData.InvalidManagedObjectsReportedViaGCHandles.Remove(obj.ManagedObjectIndex);

                            Debug.LogError($"Found a Managed Object that was reported because a Native Object held {(GCHandleReported ? "a GCHandle" : "some other kind of reference")} to it, " +
                                $"with a target pointing at {(obj.data.IsValid ? "a valid" : "an invalid")} managed heap section. " +
                                $"The Native Object named {snapshot.NativeObjects.ObjectName[iMissingNativeFrom]} was of type {snapshot.NativeTypes.TypeName[nativeTypeIndex]}. " +
                                (obj.ITypeDescription < 0 ? "No Managed base type was found"
                                : $"The Managed Type was set to the managed base type {snapshot.TypeDescriptions.TypeDescriptionName[obj.ITypeDescription]} as a stop gap."));

                        }
                    }
#endif
                    snapshot.CrawledData.ManagedObjects[iManagedTo] = obj;
                    continue;
                }

                int iNativeTo = snapshot.UnifiedObjectIndexToNativeObjectIndex(snapshot.Connections.To[i]);
                if (iNativeTo >= 0)
                {
                    var rc = ++snapshot.NativeObjects.RefCount[iNativeTo];
                    snapshot.NativeObjects.RefCount[iNativeTo] = rc;
                    continue;
                }
            }
#if DEBUG_VALIDATION
            // This is here to make the investigations for PROF-2420 easier.
            if(snapshot.CrawledData.InvalidManagedObjectsReportedViaGCHandles.Count >= 0)
                Debug.LogError($"There are {snapshot.CrawledData.InvalidManagedObjectsReportedViaGCHandles.Count} Managed Objects that were reported as part of GCHandles that could not be reunited with their native object by the Managed Crawler.");
#endif
        }

        static void ConnectNativeToManageObject(IntermediateCrawlData crawlData)
        {
            using var marker = k_ConnectNativeToManageObjectProfilerMarker.Auto();
            var snapshot = crawlData.CachedMemorySnapshot;
            var objectInfos = crawlData.ManagedObjectInfos;

            if (snapshot.TypeDescriptions.Count == 0)
                return;

            int cachedPtrOffset = snapshot.TypeDescriptions.IFieldUnityObjectMCachedPtrOffset;

#if DEBUG_VALIDATION
            // These are used to double-check that all Native -> Managed connections reported via GCHandles on Native Objects are correctly found via m_CachedPtr
            long firstManagedToNativeConnection = snapshot.CrawledData.Connections.Count;
            Dictionary<ulong, int> managedObjectAddressToNativeObjectIndex = new Dictionary<ulong, int>();
#endif

            for (int i = 0; i != objectInfos.Count; i++)
            {
                //Must derive of unity Object
                var objectInfo = objectInfos[i];
                objectInfo.NativeObjectIndex = -1;
                int instanceID = CachedSnapshot.NativeObjectEntriesCache.InstanceIDNone;
                int nativeTypeIndex;
                if (snapshot.TypeDescriptions.UnityObjectTypeIndexToNativeTypeIndex.TryGetValue(objectInfo.ITypeDescription, out nativeTypeIndex))
                {
                    // TODO: Add index to a list of Managed Unity Objects here
                    var heapSection = snapshot.ManagedHeapSections.Find(objectInfo.PtrObject + (ulong)cachedPtrOffset, snapshot.VirtualMachineInformation);
                    if (!heapSection.IsValid)
                    {
                        Debug.LogWarning("Managed object (addr:" + objectInfo.PtrObject + ", index:" + objectInfo.ManagedObjectIndex + ") does not have data at cachedPtr offset(" + cachedPtrOffset + ")");
                    }
                    else
                    {
                        ulong cachedPtr;
                        heapSection.TryReadPointer(out cachedPtr);

                        if (!snapshot.NativeObjects.NativeObjectAddressToInstanceId.TryGetValue(cachedPtr, out instanceID))
                            instanceID = CachedSnapshot.NativeObjectEntriesCache.InstanceIDNone;
                        // cachedPtr == 0UL or instanceID == CachedSnapshot.NativeObjectEntriesCache.InstanceIDNone -> Leaked Shell
                        // TODO: Add index to a list of leaked shells here.
                    }

                    if (instanceID != CachedSnapshot.NativeObjectEntriesCache.InstanceIDNone)
                    {
                        if (snapshot.NativeObjects.InstanceId2Index.TryGetValue(instanceID, out objectInfo.NativeObjectIndex))
                            snapshot.NativeObjects.ManagedObjectIndex[objectInfo.NativeObjectIndex] = i;

                        if (nativeTypeIndex == -1)
                        {
                            nativeTypeIndex = snapshot.NativeObjects.NativeTypeArrayIndex[objectInfo.NativeObjectIndex];
                            ReportManagedTypeToNativeTypeConnectionForThisTypeAndItsBaseTypes(snapshot, nativeTypeIndex, objectInfo.ITypeDescription);
                        }
                        if (snapshot.HasConnectionOverhaul)
                        {
                            snapshot.CrawledData.Connections.Add(ManagedConnection.MakeUnityEngineObjectConnection(objectInfo.NativeObjectIndex, objectInfo.ManagedObjectIndex));
                            var rc = ++snapshot.NativeObjects.RefCount[objectInfo.NativeObjectIndex];
                            snapshot.NativeObjects.RefCount[objectInfo.NativeObjectIndex] = rc;
#if DEBUG_VALIDATION
                            managedObjectAddressToNativeObjectIndex.Add(objectInfo.PtrObject, objectInfo.NativeObjectIndex);
#endif
                        }
                    }
                    if (nativeTypeIndex == -1)
                    {
                        // make a note of the failure to connect this object's type to its native type
                        // after all objects were connected, the types that are still not connected can then
                        // be filtered by those types that had actual object instances in the snapshot
                        crawlData.TypesWithObjectsThatMayStillNeedNativeTypeConnection.Add(objectInfo.ITypeDescription);
                    }
                }
                //else
                //{
                // TODO: Add index to a list of Pure C# Objects here
                //}

                objectInfos[i] = objectInfo;
            }

#if DEBUG_VALIDATION
            // Double-check that all Native -> Managed connections reported via GCHandles on Native Objects have been correctly found via m_CachedPtr
            if (snapshot.Connections.IndexOfFirstNativeToGCHandleConnection >= 0)
            {
                var gcHandlesCount = snapshot.GcHandles.Count;
                for (long nativeConnectionIndex = snapshot.Connections.IndexOfFirstNativeToGCHandleConnection; nativeConnectionIndex < snapshot.Connections.Count; nativeConnectionIndex++)
                {
                    var nativeObjectIndex = snapshot.Connections.From[nativeConnectionIndex] - gcHandlesCount;
                    var managedShellAddress = snapshot.GcHandles.Target[snapshot.Connections.To[nativeConnectionIndex]];
                    var managedObjectIndex = snapshot.CrawledData.MangedObjectIndexByAddress[managedShellAddress];
                    var managedObject = snapshot.CrawledData.ManagedObjects[managedObjectIndex];
                    if (managedObject.NativeObjectIndex != nativeObjectIndex)
                        Debug.LogError("Native Object is not correctly linked with its Managed Object");
                    bool foundConnection = managedObjectAddressToNativeObjectIndex.ContainsKey(managedShellAddress);
                    if (!foundConnection)
                        Debug.LogError("Native Object is not correctly linked with its Managed Object");
                }
            }
#endif
        }

        /// <summary>
        /// This method serves a double purpose:
        /// 1. It iterates up the entire managed inheritance chain of the passed managed type and connects every managed type along the way to its base native type.
        ///    This is the only way to find the managed to native type connection for managed types like UnityEngine.ScriptableObject or UnityEngine.MonoBehaviour,
        ///    that will never have an instance of their base types in the snapshot, but an object of a derived type is _nearly_ guaranteed.
        /// 2. As the snapshot does not report the connection from a Native Type to the Managed Base Type, this function checks if viable Managed types
        ///    that it iterates over could be the managed Base Type
        ///
        /// Beyond checking that <see cref="CachedSnapshot.TypeDescriptions.UnityObjectTypeIndexToNativeTypeIndex"/> previously mapped the managed type to -1,
        /// no further checks are needed before calling and it stops as soon as it hits the first managed base type that already has an association.
        /// </summary>
        /// <param name="snapshot"></param>
        /// <param name="nativeTypeIndex"></param>
        /// <param name="managedType"></param>
        static void ReportManagedTypeToNativeTypeConnectionForThisTypeAndItsBaseTypes(CachedSnapshot snapshot, int nativeTypeIndex, int managedType)
        {
            if (nativeTypeIndex == -1)
                return;
            using var marker = k_ReportManagedTypeToNativeTypeConnectionForThisTypeAndItsBaseTypesProfilerMarker.Auto();
            // This Method links up the Managed Types to the Native Type and, while at it, seeks to link up the Native Type to the Managed Base Type
            // E.g. the Managed Base Type for a user created component is 'UnityEngine.MonoBehaviour'. No Instances of that exact type will ever be in a capture.
            // Though there will be multiple derived Managed Types, we only need to connect the Native Type 'Monobehaviour' to the Managed Base Type 'UnityEngine.MonoBehaviour' once.
            // Whether or not we still need to do that doesn't change during the while loop, and not rechecking if it is needed is an optimization, so only check this once here.
            bool nativeUnityObjectTypeIndexToManagedBaseTypeIsNotYetReported = !snapshot.CrawledData.NativeUnityObjectTypeIndexToManagedBaseTypeIndex.ContainsKey(nativeTypeIndex);

            while (managedType >= 0 && snapshot.TypeDescriptions.UnityObjectTypeIndexToNativeTypeIndex.TryGetValue(managedType, out var n) && n == -1)
            {
                // Register the managed type connection to the native base type
                //
                // EditorScriptableObject is a fake native type stand-in for ScriptableObjects of types that are located in Editor Only assemblies.
                // The Managed Type UnityEngine.ScriptableObject should not be tracked as derived from this fake native type
                // as there are likely managed derivatives of it that are both located in Editor Only assemblies or not, but on the managed side,
                // they are not tracked as different types.
                // Their derived types are still necessarily EditorScriptableObject (as they are located in and Editor Only assembly).
                // So just ignore the link between the exact types of UnityEngine.ScriptableObject (managed) and EditorScriptableObject (native) here,
                // to avoid confusion or unstable type mapping results of the managed UnityEngine.ScriptableObject type.
                if (!(nativeTypeIndex == snapshot.NativeTypes.EditorScriptableObjectIdx && managedType == snapshot.TypeDescriptions.ITypeUnityScriptableObject))
                    snapshot.TypeDescriptions.UnityObjectTypeIndexToNativeTypeIndex[managedType] = nativeTypeIndex;

                // Check if this could be the still unreported Managed Base Type for this Native Type
                if (nativeUnityObjectTypeIndexToManagedBaseTypeIsNotYetReported)
                {
                    // Check if this managed object's managed type could map directly to a Unity owned native type
                    var typeName = snapshot.TypeDescriptions.TypeDescriptionName[managedType];
                    if (typeName.StartsWith("Unity"))
                    {
                        var startOfNamespaceStrippedManagedTypeName = typeName.LastIndexOf('.') + 1;
                        var managedTypeNameLength = typeName.Length - startOfNamespaceStrippedManagedTypeName;
                        var nativeTypeNameLength = snapshot.NativeTypes.TypeName[nativeTypeIndex].Length;
                        if (managedTypeNameLength == nativeTypeNameLength)
                        {
                            unsafe
                            {
                                fixed (char* nativeName = snapshot.NativeTypes.TypeName[nativeTypeIndex], managedName = typeName)
                                {
                                    // no need to create a bunch of managed substrings in a hot loop
                                    char* managedSubstring = managedName + startOfNamespaceStrippedManagedTypeName;
                                    if (UnsafeUtility.MemCmp(managedSubstring, nativeName, managedTypeNameLength) == 0)
                                    {
                                        snapshot.CrawledData.NativeUnityObjectTypeIndexToManagedBaseTypeIndex.Add(nativeTypeIndex, managedType);
                                        nativeUnityObjectTypeIndexToManagedBaseTypeIsNotYetReported = false;
                                    }
                                }
                            }
                        }
                    }
                }
                // continue with the base type of this managed object type
                managedType = snapshot.TypeDescriptions.BaseOrElementTypeIndex[managedType];
            }
        }

        /// <summary>
        /// Most Unity Object managed and native types should have been connected to each other by
        /// <see cref="ConnectNativeToManageObject(IntermediateCrawlData)"/> before calling this.
        ///
        /// This method is there for cases where there are only Leaked Managed Shell objects or no objects of a Unity type in a snapshot.
        /// </summary>
        /// <param name="crawlData"></param>
        static void ConnectRemainingManagedTypesToNativeTypes(IntermediateCrawlData crawlData)
        {
            using var marker = k_ConnectRemainingManagedTypesToNativeTypesProfilerMarker.Auto();
            var snapshot = crawlData.CachedMemorySnapshot;
            var managedTypes = snapshot.TypeDescriptions;
            if (managedTypes.Count == 0)
                return;
            var unityObjectTypeIndexToNativeTypeIndex = managedTypes.UnityObjectTypeIndexToNativeTypeIndex;
            var managedToNativeTypeDict = new Dictionary<int, int>();
            foreach (var item in unityObjectTypeIndexToNativeTypeIndex)
            {
                // process all Unity Object Types that are not yet connected to their native types.
                if (item.Value == -1)
                {
                    var managedType = item.Key;
                    var topLevelManagedType = managedType;
                    // This process of connecting the managed type to a native type is rather costly
                    // Only spend that effort for types that had actual object instances in the snapshot
                    // Other types will not be displayed in any tables and establishing the connection therefore doesn't matter
                    if (!crawlData.TypesWithObjectsThatMayStillNeedNativeTypeConnection.Contains(managedType))
                        continue;
                    while (managedType >= 0 && unityObjectTypeIndexToNativeTypeIndex.TryGetValue(managedType, out var nativeTypeIndex))
                    {
                        if (nativeTypeIndex == -1)
                        {
                            var typeName = managedTypes.TypeDescriptionName[managedType];
                            if (typeName.StartsWith("Unity"))
                            {
                                typeName = typeName.Substring(typeName.LastIndexOf('.') + 1);
                                nativeTypeIndex = Array.FindIndex(snapshot.NativeTypes.TypeName, e => e.Equals(typeName));
                            }
                        }
                        if (nativeTypeIndex >= 0)
                        {
                            // can't modify the collection while we iterate over it, so store the connectio for after this foreach
                            managedToNativeTypeDict.Add(topLevelManagedType, nativeTypeIndex);
                            break;
                        }
                        // continue with the base type of this managed object type
                        managedType = managedTypes.BaseOrElementTypeIndex[managedType];
                    }
                }
            }
            foreach (var item in managedToNativeTypeDict)
            {
                ReportManagedTypeToNativeTypeConnectionForThisTypeAndItsBaseTypes(snapshot, item.Value, item.Key);
            }
        }

        static void CrawlRawObjectData(IntermediateCrawlData crawlData, BytesAndOffset bytesAndOffset, int iTypeDescription, bool useStaticFields, ulong ptrFrom, int indexOfFrom)
        {
            var snapshot = crawlData.CachedMemorySnapshot;

            var fields = useStaticFields ? snapshot.TypeDescriptions.fieldIndicesOwnedStatic[iTypeDescription] : snapshot.TypeDescriptions.FieldIndicesInstance[iTypeDescription];
            foreach (var iField in fields)
            {
                int iField_TypeDescription_TypeIndex = snapshot.FieldDescriptions.TypeIndex[iField];
                int iField_TypeDescription_ArrayIndex = snapshot.TypeDescriptions.TypeIndex2ArrayIndex(iField_TypeDescription_TypeIndex);

                var fieldLocation = bytesAndOffset.Add((ulong)snapshot.FieldDescriptions.Offset[iField] - (useStaticFields ? 0 : snapshot.VirtualMachineInformation.ObjectHeaderSize));

                if (snapshot.TypeDescriptions.HasFlag(iField_TypeDescription_ArrayIndex, TypeFlags.kValueType))
                {
                    CrawlRawObjectData(crawlData, fieldLocation, iField_TypeDescription_ArrayIndex, useStaticFields, ptrFrom, indexOfFrom);
                    continue;
                }


                ulong fieldAddr;
                if (fieldLocation.TryReadPointer(out fieldAddr) == BytesAndOffset.PtrReadError.Success
                    // don't process null pointers
                    && fieldAddr != 0)
                {
                    crawlData.CrawlDataStack.Push(new StackCrawlData() { ptr = fieldAddr, ptrFrom = ptrFrom, typeFrom = iTypeDescription, indexOfFrom = indexOfFrom, fieldFrom = iField, fromArrayIndex = -1 });
                }
            }
        }

        static bool CrawlPointer(IntermediateCrawlData dataStack)
        {
            UnityEngine.Debug.Assert(dataStack.CrawlDataStack.Count > 0);

            var snapshot = dataStack.CachedMemorySnapshot;
            var typeDescriptions = snapshot.TypeDescriptions;
            var data = dataStack.CrawlDataStack.Pop();
            var virtualMachineInformation = snapshot.VirtualMachineInformation;
            var managedHeapSections = snapshot.ManagedHeapSections;
            var byteOffset = managedHeapSections.Find(data.ptr, virtualMachineInformation);

            if (!byteOffset.IsValid)
            {
#if DEBUG_VALIDATION

                // This whole if block is here to make the investigations for PROF-2420 easier.
                if(snapshot.CrawledData.MangedObjectIndexByAddress.TryGetValue(data.ptr, out var manageObjectIndex))
                {
                    for (long i = 0; i < snapshot.GcHandles.Target.Count; i++)
                    {
                        if(snapshot.GcHandles.Target[i] == data.ptr)
                        {
                            if (snapshot.CrawledData.InvalidManagedObjectsReportedViaGCHandles.ContainsKey(manageObjectIndex))
                                break;
                            snapshot.CrawledData.InvalidManagedObjectsReportedViaGCHandles.Add(manageObjectIndex, new ManagedObjectInfo() { PtrObject = data.ptr, ManagedObjectIndex = manageObjectIndex });
                            break;
                        }
                    }
                }
#endif
                return false;
            }

            ManagedObjectInfo obj;
            bool wasAlreadyCrawled;

            obj = ParseObjectHeader(snapshot, data, out wasAlreadyCrawled, false, byteOffset);
            bool addConnection = (data.typeFrom >= 0 || data.fieldFrom >= 0);
            if (addConnection)
                ++obj.RefCount;

            if (!obj.IsValid())
                return false;

            snapshot.CrawledData.ManagedObjects[obj.ManagedObjectIndex] = obj;
            snapshot.CrawledData.MangedObjectIndexByAddress[obj.PtrObject] = obj.ManagedObjectIndex;

            if (addConnection)
                dataStack.ManagedConnections.Add(ManagedConnection.MakeConnection(snapshot, data.indexOfFrom, data.ptrFrom, obj.ManagedObjectIndex, data.ptr, data.typeFrom, data.fieldFrom, data.fromArrayIndex));

            if (wasAlreadyCrawled)
                return true;

            if (!typeDescriptions.HasFlag(obj.ITypeDescription, TypeFlags.kArray))
            {
                CrawlRawObjectData(dataStack, byteOffset.Add(snapshot.VirtualMachineInformation.ObjectHeaderSize), obj.ITypeDescription, false, data.ptr, obj.ManagedObjectIndex);
                return true;
            }

            var arrayLength = ManagedHeapArrayDataTools.ReadArrayLength(snapshot, data.ptr, obj.ITypeDescription);
            int iElementTypeDescription = typeDescriptions.BaseOrElementTypeIndex[obj.ITypeDescription];
            if (iElementTypeDescription == -1)
            {
                return false; //do not crawl uninitialized object types, as we currently don't have proper handling for these
            }
            var arrayData = byteOffset.Add(virtualMachineInformation.ArrayHeaderSize);
            for (int i = 0; i != arrayLength; i++)
            {
                if (typeDescriptions.HasFlag(iElementTypeDescription, TypeFlags.kValueType))
                {
                    CrawlRawObjectData(dataStack, arrayData, iElementTypeDescription, false, data.ptr, obj.ManagedObjectIndex);
                    arrayData = arrayData.Add((ulong)typeDescriptions.Size[iElementTypeDescription]);
                }
                else
                {
                    ulong arrayDataPtr;
                    if (arrayData.TryReadPointer(out arrayDataPtr) != BytesAndOffset.PtrReadError.Success)
                        return false;

                    // don't process null pointers
                    if (arrayDataPtr != 0)
                        dataStack.CrawlDataStack.Push(new StackCrawlData() { ptr = arrayDataPtr, ptrFrom = data.ptr, typeFrom = obj.ITypeDescription, indexOfFrom = obj.ManagedObjectIndex, fieldFrom = -1, fromArrayIndex = i });
                    arrayData = arrayData.NextPointer();
                }
            }
            return true;
        }

        static int SizeOfObjectInBytes(CachedSnapshot snapshot, int iTypeDescription, BytesAndOffset bo, ulong address)
        {
            if (iTypeDescription < 0) return 0;

            if (snapshot.TypeDescriptions.HasFlag(iTypeDescription, TypeFlags.kArray))
                return (int)ManagedHeapArrayDataTools.ReadArrayObjectSizeInBytes(snapshot, address, iTypeDescription);

            if (snapshot.TypeDescriptions.ITypeString == iTypeDescription)
                return StringTools.ReadStringObjectSizeInBytes(bo, snapshot.VirtualMachineInformation);

            //array and string are the only types that are special, all other types just have one size, which is stored in the type description
            return snapshot.TypeDescriptions.Size[iTypeDescription];
        }

        static int SizeOfObjectInBytes(CachedSnapshot snapshot, int iTypeDescription, BytesAndOffset byteOffset, CachedSnapshot.ManagedMemorySectionEntriesCache heap)
        {
            if (iTypeDescription < 0) return 0;

            if (snapshot.TypeDescriptions.HasFlag(iTypeDescription, TypeFlags.kArray))
                return (int)ManagedHeapArrayDataTools.ReadArrayObjectSizeInBytes(snapshot, byteOffset, iTypeDescription);

            if (snapshot.TypeDescriptions.ITypeString == iTypeDescription)
                return StringTools.ReadStringObjectSizeInBytes(byteOffset, snapshot.VirtualMachineInformation);

            // array and string are the only types that are special, all other types just have one size, which is stored in the type description
            return snapshot.TypeDescriptions.Size[iTypeDescription];
        }

        internal static ManagedObjectInfo ParseObjectHeader(CachedSnapshot snapshot, StackCrawlData crawlData, out bool wasAlreadyCrawled, bool ignoreBadHeaderError, BytesAndOffset byteOffset)
        {
            var objectList = snapshot.CrawledData.ManagedObjects;
            var objectsByAddress = snapshot.CrawledData.MangedObjectIndexByAddress;

            ManagedObjectInfo objectInfo = default(ManagedObjectInfo);

            int idx = 0;
            if (!snapshot.CrawledData.MangedObjectIndexByAddress.TryGetValue(crawlData.ptr, out idx))
            {
                if (TryParseObjectHeader(snapshot, crawlData, out objectInfo, byteOffset))
                {
                    objectInfo.ManagedObjectIndex = (int)objectList.Count;
                    objectList.Add(objectInfo);
                    objectsByAddress.Add(crawlData.ptr, objectInfo.ManagedObjectIndex);
                }
                wasAlreadyCrawled = false;
                return objectInfo;
            }

            objectInfo = snapshot.CrawledData.ManagedObjects[idx];
            // this happens on objects from gcHandles, they are added before any other crawled object but have their ptr set to 0.
            if (objectInfo.PtrObject == 0)
            {
                idx = objectInfo.ManagedObjectIndex;
                if (TryParseObjectHeader(snapshot, crawlData, out objectInfo, byteOffset))
                {
                    objectInfo.ManagedObjectIndex = idx;
                    objectList[idx] = objectInfo;
                    objectsByAddress[crawlData.ptr] = idx;
                }

                wasAlreadyCrawled = false;
                return objectInfo;
            }

            wasAlreadyCrawled = true;
            return objectInfo;
        }

        public static bool TryParseObjectHeader(CachedSnapshot snapshot, StackCrawlData data, out ManagedObjectInfo info, BytesAndOffset boHeader)
        {
            bool resolveFailed = false;
            var heap = snapshot.ManagedHeapSections;
            info = new ManagedObjectInfo();
            info.ManagedObjectIndex = -1;

            ulong ptrIdentity = 0;
            if (!boHeader.IsValid) boHeader = heap.Find(data.ptr, snapshot.VirtualMachineInformation);
            if (!boHeader.IsValid)
                resolveFailed = true;
            else
            {
                boHeader.TryReadPointer(out ptrIdentity);

                info.PtrTypeInfo = ptrIdentity;
                info.ITypeDescription = snapshot.TypeDescriptions.TypeInfo2ArrayIndex(info.PtrTypeInfo);

                if (info.ITypeDescription < 0)
                {
                    var boIdentity = heap.Find(ptrIdentity, snapshot.VirtualMachineInformation);
                    if (boIdentity.IsValid)
                    {
                        ulong ptrTypeInfo;
                        boIdentity.TryReadPointer(out ptrTypeInfo);
                        info.PtrTypeInfo = ptrTypeInfo;
                        info.ITypeDescription = snapshot.TypeDescriptions.TypeInfo2ArrayIndex(info.PtrTypeInfo);
                        resolveFailed = info.ITypeDescription < 0;
                    }
                    else
                    {
                        resolveFailed = true;
                    }
                }
            }

            if (resolveFailed)
            {
                //enable this define in order to track objects that are missing type data, this can happen if for whatever reason mono got changed and there are types / heap chunks that we do not report
                //addresses here can be used to identify the objects within the Unity process by using a debug version of the mono libs in order to add to the capture where this data resides.
#if DEBUG_VALIDATION
                Debug.LogError($"Bad object detected:\nheader at address: 0x{data.ptr:X16} \nvtable at address 0x{ptrIdentity:X16}" +
                    $"\nDetails:\n From object: 0x{data.ptrFrom:X16}\n " +
                    $"From type: {(data.typeFrom != -1 ? snapshot.TypeDescriptions.TypeDescriptionName[data.typeFrom] : data.typeFrom.ToString())}\n" +
                    $"From field: {(data.fieldFrom != -1 ? snapshot.FieldDescriptions.FieldDescriptionName[data.fieldFrom] : data.fieldFrom.ToString())}\n" +
                    $"From array data: arrayIndex - {(data.fromArrayIndex)}, indexOf - {(data.indexOfFrom)}");
                //can add from array index too above if needed
#endif
                info.PtrTypeInfo = 0;
                info.ITypeDescription = -1;
                info.Size = 0;
                info.PtrObject = 0;
                info.data = default(BytesAndOffset);

                return false;
            }


            info.Size = SizeOfObjectInBytes(snapshot, info.ITypeDescription, boHeader, heap);
            info.data = boHeader;
            info.PtrObject = data.ptr;
            return true;
        }
    }

    internal static class StringTools
    {
        // After 8000 chars, StringBuilder will ring buffer the strings and our UI breaks. Also see https://referencesource.microsoft.com/#mscorlib/system/text/stringbuilder.cs,76
        const int k_StringBuilderMaxCap = 8000;
        // However, 8000 chars is quite a bit more than would be necessarily helpful for the memory profiler, so trim it further
        const int k_AdditionalTrimForLongStrings = 6000;
        public const int MaxStringLengthToRead = k_StringBuilderMaxCap - k_AdditionalTrimForLongStrings - 10 /*Buffer for ellipsis, quotes and spaces*/;
        public const string Elipsis = " [...]";

        public static string ReadString(this BytesAndOffset bo, out int fullLength, VirtualMachineInformation virtualMachineInformation)
        {
            fullLength = -1;
            return ReadStringInternal(bo, ref fullLength, virtualMachineInformation);
        }

        public static string ReadString(this ManagedObjectInfo managedObjectInfo, CachedSnapshot snapshot)
        {
            int fullLength = -1;
            return ReadStringInternal(managedObjectInfo.data, ref fullLength, snapshot.VirtualMachineInformation);
        }

        public static string ReadCharArray(this ManagedObjectInfo managedObjectInfo, CachedSnapshot snapshot)
        {
            return ReadCharArray(managedObjectInfo.data, ManagedHeapArrayDataTools.GetArrayInfo(snapshot, managedObjectInfo.data, managedObjectInfo.ITypeDescription).length, snapshot.VirtualMachineInformation);
        }

        public static string ReadCharArray(this BytesAndOffset bo, int fullLength, VirtualMachineInformation virtualMachineInformation)
        {
            return ReadStringInternal(bo, ref fullLength, virtualMachineInformation).Replace((char)0, ' ');
        }

        static string ReadStringInternal(this BytesAndOffset bo, ref int fullLength, VirtualMachineInformation virtualMachineInformation, int maxLengthToRead = MaxStringLengthToRead)
        {
            BytesAndOffset firstChar = bo;
            if (fullLength < 0)
            {
                // parsing a string with an object header
                bo = bo.Add(virtualMachineInformation.ObjectHeaderSize);
                fullLength = bo.ReadInt32();
                firstChar = bo.Add(sizeof(int));
            }
            else
            {
                // pasring a char [] with an array header
                bo = bo.Add(virtualMachineInformation.ArrayHeaderSize);
                firstChar = bo;
            }


            if (fullLength < 0 || (ulong)fullLength * 2 > (ulong)bo.bytes.Count - bo.offset - sizeof(int))
            {
#if DEBUG_VALIDATION
                Debug.LogError("Found a String Object of impossible length.");
#endif
                fullLength = 0;
            }

            unsafe
            {
                if (fullLength > maxLengthToRead)
                {
                    var cappedLength = maxLengthToRead;
                    return $"{System.Text.Encoding.Unicode.GetString(firstChar.GetUnsafeOffsetTypedPtr(), cappedLength * 2)}{Elipsis}";
                }
                else
                    return System.Text.Encoding.Unicode.GetString(firstChar.GetUnsafeOffsetTypedPtr(), fullLength * 2);
            }
        }

        public static string ReadFirstStringLine(this ManagedObjectInfo moi, VirtualMachineInformation virtualMachineInformation, bool addQuotes)
        {
            return ReadFirstStringLineInternal(moi.data, virtualMachineInformation, addQuotes, -1);
        }

        public static string ReadFirstStringLine(this BytesAndOffset bo, VirtualMachineInformation virtualMachineInformation, bool addQuotes)
        {
            return ReadFirstStringLineInternal(bo, virtualMachineInformation, addQuotes, -1);
        }

        public static string ReadFirstCharArrayLine(this ManagedObjectInfo managedObjectInfo, CachedSnapshot snapshot, bool addQuotes)
        {
            return ReadFirstCharArrayLine(managedObjectInfo.data, snapshot.VirtualMachineInformation, addQuotes, ManagedHeapArrayDataTools.GetArrayInfo(snapshot, managedObjectInfo.data, managedObjectInfo.ITypeDescription).length);
        }

        public static string ReadFirstCharArrayLine(this BytesAndOffset bo, VirtualMachineInformation virtualMachineInformation, bool addQuotes, int fullLength)
        {
            return ReadFirstStringLineInternal(bo, virtualMachineInformation, addQuotes, fullLength).Replace((char)0, ' ');
        }

        static string ReadFirstStringLineInternal(this BytesAndOffset bo, VirtualMachineInformation virtualMachineInformation, bool addQuotes, int fullLength)
        {
            const int maxCharsInLine = 30;
            var str = ReadStringInternal(bo, ref fullLength, virtualMachineInformation, maxCharsInLine);
            var firstLineBreak = str.IndexOf('\n');
            if (firstLineBreak >= 0)
            {
                if (firstLineBreak < maxCharsInLine && str.Length > maxCharsInLine)
                {
                    // reduce our working set
                    str = str.Substring(0, Math.Min(str.Length, maxCharsInLine));
                }
                str = str.Replace("\n", "\\n");
                str += Elipsis;
            }
            if (addQuotes)
            {
                if (firstLineBreak >= 0)
                    return $"\"{str}"; // open ended quote
                return $"\"{str}\"";
            }
            else
            {
                return str;
            }
        }

        public static int ReadStringObjectSizeInBytes(this BytesAndOffset bo, VirtualMachineInformation virtualMachineInformation)
        {
            var lengthPointer = bo.Add(virtualMachineInformation.ObjectHeaderSize);
            var length = lengthPointer.ReadInt32();
            if (length < 0 || (ulong)length * 2 > (ulong)bo.bytes.Count - bo.offset - sizeof(int))
            {
#if DEBUG_VALIDATION
                Debug.LogError("Found a String Object of impossible length.");
#endif
                length = 0;
            }

            return (int)virtualMachineInformation.ObjectHeaderSize + /*lengthfield*/ 4 + (length * /*utf16=2bytes per char*/ 2) + /*2 zero terminators*/ 2;
        }
    }
    internal class ArrayInfo
    {
        public ulong baseAddress;
        public int[] rank;
        public int length;
        public uint elementSize;
        public int arrayTypeDescription;
        public int elementTypeDescription;
        public BytesAndOffset header;
        public BytesAndOffset data;
        public BytesAndOffset GetArrayElement(uint index)
        {
            return data.Add(elementSize * index);
        }

        public ulong GetArrayElementAddress(int index)
        {
            return baseAddress + (ulong)(elementSize * index);
        }

        public string IndexToRankedString(int index)
        {
            return ManagedHeapArrayDataTools.ArrayRankIndexToString(rank, index);
        }

        public string ArrayRankToString()
        {
            return ManagedHeapArrayDataTools.ArrayRankToString(rank);
        }
    }
}

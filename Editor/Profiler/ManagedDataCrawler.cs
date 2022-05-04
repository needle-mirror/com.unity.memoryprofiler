using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Database;
using Unity.MemoryProfiler.Editor.Format;
using Unity.MemoryProfiler.Editor.UIContentData;
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
            return PtrObject != 0 && PtrTypeInfo != 0 && data.bytes != null;
        }

        public static bool operator==(ManagedObjectInfo lhs, ManagedObjectInfo rhs)
        {
            return lhs.PtrObject == rhs.PtrObject
                && lhs.PtrTypeInfo == rhs.PtrTypeInfo
                && lhs.NativeObjectIndex == rhs.NativeObjectIndex
                && lhs.ManagedObjectIndex == rhs.ManagedObjectIndex
                && lhs.ITypeDescription == rhs.ITypeDescription
                && lhs.Size == rhs.Size
                && lhs.RefCount == rhs.RefCount;
        }

        public static bool operator!=(ManagedObjectInfo lhs, ManagedObjectInfo rhs)
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

    internal struct BytesAndOffset
    {
        public byte[] bytes;
        public int offset;
        public int pointerSize;
        public bool IsValid { get { return bytes != null; } }
        public BytesAndOffset(byte[] bytes, int pointerSize)
        {
            this.bytes = bytes;
            this.pointerSize = pointerSize;
            offset = 0;
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

            if (offset + pointerSize > bytes.Length)
                return PtrReadError.OutOfBounds;

            switch (pointerSize)
            {
                case VMTools.X64ArchPtrSize:
                    ptr = BitConverter.ToUInt64(bytes, offset);
                    return PtrReadError.Success;
                case VMTools.X86ArchPtrSize:
                    ptr = BitConverter.ToUInt32(bytes, offset);
                    return PtrReadError.Success;
                default: //should never happen
                    return PtrReadError.InvalidPtrSize;
            }
        }

        public byte ReadByte()
        {
            return bytes[offset];
        }

        public short ReadInt16()
        {
            return BitConverter.ToInt16(bytes, offset);
        }

        public Int32 ReadInt32()
        {
            return BitConverter.ToInt32(bytes, offset);
        }

        public Int32 ReadInt32(int additionalOffset)
        {
            return BitConverter.ToInt32(bytes, offset + additionalOffset);
        }

        public Int64 ReadInt64()
        {
            return BitConverter.ToInt64(bytes, offset);
        }

        public ushort ReadUInt16()
        {
            return BitConverter.ToUInt16(bytes, offset);
        }

        public uint ReadUInt32()
        {
            return BitConverter.ToUInt32(bytes, offset);
        }

        public ulong ReadUInt64()
        {
            return BitConverter.ToUInt64(bytes, offset);
        }

        public bool ReadBoolean()
        {
            return BitConverter.ToBoolean(bytes, offset);
        }

        public char ReadChar()
        {
            return BitConverter.ToChar(bytes, offset);
        }

        public double ReadDouble()
        {
            return BitConverter.ToDouble(bytes, offset);
        }

        public float ReadSingle()
        {
            return BitConverter.ToSingle(bytes, offset);
        }

        public string ReadString(out int fullLength)
        {
            var readLength = fullLength = ReadInt32();
            var additionalOffsetForObjectHeader = 0;
            if (fullLength < 0 || (long)offset + (long)sizeof(int) + ((long)fullLength * (long)2) > bytes.Length)
            {
                // Why is the header not included for object data in the tables?
                // this workaround here is flakey!
                additionalOffsetForObjectHeader = 16;
                readLength = fullLength = ReadInt32(additionalOffsetForObjectHeader);

                if (fullLength < 0 || (long)offset + (long)sizeof(int) + ((long)fullLength * (long)2) > bytes.Length)
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
                fixed(byte* ptr = bytes)
                {
                    string str = null;
                    char* begin = (char*)(ptr + (offset + additionalOffsetForObjectHeader + sizeof(int)));
                    str = new string(begin, 0, readLength);
                    if (fullLength != readLength)
                    {
                        fixed(char* s = str, e = StringTools.Elipsis)
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

        public BytesAndOffset Add(int add)
        {
            return new BytesAndOffset() { bytes = bytes, offset = offset + add, pointerSize = pointerSize };
        }

        public void WritePointer(UInt64 value)
        {
            for (int i = 0; i < pointerSize; i++)
            {
                bytes[i + offset] = (byte)value;
                value >>= 8;
            }
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
            public ulong TotalManagedObjectMemoryUsage { set; get; }
            const int kInitialStackSize = 256;
            public IntermediateCrawlData(CachedSnapshot snapshot)
            {
                DuplicatedGCHandleTargetsStack = new Stack<int>(kInitialStackSize);
                CachedMemorySnapshot = snapshot;
                CrawlDataStack = new Stack<StackCrawlData>();

                TypesWithStaticFields = new List<int>();
                for (long i = 0; i != snapshot.TypeDescriptions.Count; ++i)
                {
                    if (snapshot.TypeDescriptions.StaticFieldBytes[i] != null
                        && snapshot.TypeDescriptions.StaticFieldBytes[i].Length > 0)
                    {
                        TypesWithStaticFields.Add(snapshot.TypeDescriptions.TypeIndex[i]);
                    }
                }
            }
        }

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
                var bytesOffset = new BytesAndOffset { bytes = snapshot.TypeDescriptions.StaticFieldBytes[iTypeDescription], offset = 0, pointerSize = snapshot.VirtualMachineInformation.PointerSize };
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
                    snapshot.CrawledData.ManagedObjects[iManagedTo] = obj;
                    continue;
                }

                int iNativeTo = snapshot.UnifiedObjectIndexToNativeObjectIndex(snapshot.Connections.To[i]);
                if (iNativeTo >= 0)
                {
                    var rc = ++snapshot.NativeObjects.refcount[iNativeTo];
                    snapshot.NativeObjects.refcount[iNativeTo] = rc;
                    continue;
                }
            }
        }

        static void ConnectNativeToManageObject(IntermediateCrawlData crawlData)
        {
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
                    var heapSection =  snapshot.ManagedHeapSections.Find(objectInfo.PtrObject + (ulong)cachedPtrOffset, snapshot.VirtualMachineInformation);
                    if (!heapSection.IsValid)
                    {
                        Debug.LogWarning("Managed object (addr:" + objectInfo.PtrObject + ", index:" + objectInfo.ManagedObjectIndex + ") does not have data at cachedPtr offset(" + cachedPtrOffset + ")");
                    }
                    else
                    {
                        ulong cachedPtr;
                        heapSection.TryReadPointer(out cachedPtr);

                        if (!snapshot.NativeObjects.nativeObjectAddressToInstanceId.TryGetValue(cachedPtr, out instanceID))
                            instanceID = CachedSnapshot.NativeObjectEntriesCache.InstanceIDNone;
                        // cachedPtr == 0UL or instanceID == CachedSnapshot.NativeObjectEntriesCache.InstanceIDNone -> Leaked Shell
                        // TODO: Add index to a list of leaked shells here.
                    }

                    if (instanceID != CachedSnapshot.NativeObjectEntriesCache.InstanceIDNone)
                    {
                        if (snapshot.NativeObjects.instanceId2Index.TryGetValue(instanceID, out objectInfo.NativeObjectIndex))
                            snapshot.NativeObjects.ManagedObjectIndex[objectInfo.NativeObjectIndex] = i;

                        if (nativeTypeIndex == -1)
                        {
                            nativeTypeIndex = snapshot.NativeObjects.NativeTypeArrayIndex[objectInfo.NativeObjectIndex];
                            snapshot.TypeDescriptions.UnityObjectTypeIndexToNativeTypeIndex[objectInfo.ITypeDescription] = nativeTypeIndex;

                            if (!snapshot.CrawledData.NativeUnityObjectTypeIndexToManagedBaseTypeIndex.ContainsKey(nativeTypeIndex))
                            {
                                // Check if this managed object's managed type cou
                                var typeName = snapshot.TypeDescriptions.TypeDescriptionName[objectInfo.ITypeDescription];
                                if (typeName.StartsWith("Unity"))
                                {
                                    var startOfNamespaceStrippedManagedTypeName = typeName.LastIndexOf('.') + 1;
                                    var managedTypeNameLength = typeName.Length - startOfNamespaceStrippedManagedTypeName;
                                    var nativeTypeNameLength = snapshot.NativeTypes.TypeName[nativeTypeIndex].Length;
                                    if (managedTypeNameLength == nativeTypeNameLength)
                                    {
                                        unsafe
                                        {
                                            fixed(char* nativeName = snapshot.NativeTypes.TypeName[nativeTypeIndex], managedName = typeName)
                                            {
                                                // no need to create a bunch of managed substrings in a hot loop
                                                char* managedSubstring = managedName + startOfNamespaceStrippedManagedTypeName;
                                                if (UnsafeUtility.MemCmp(managedSubstring, nativeName, managedTypeNameLength) == 0)
                                                    snapshot.CrawledData.NativeUnityObjectTypeIndexToManagedBaseTypeIndex.Add(nativeTypeIndex, objectInfo.ITypeDescription);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        if (snapshot.HasConnectionOverhaul)
                        {
                            snapshot.CrawledData.Connections.Add(ManagedConnection.MakeUnityEngineObjectConnection(objectInfo.NativeObjectIndex, objectInfo.ManagedObjectIndex));
                            var rc = ++snapshot.NativeObjects.refcount[objectInfo.NativeObjectIndex];
                            snapshot.NativeObjects.refcount[objectInfo.NativeObjectIndex] = rc;
#if DEBUG_VALIDATION
                            managedObjectAddressToNativeObjectIndex.Add(objectInfo.PtrObject, objectInfo.NativeObjectIndex);
#endif
                        }
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

        static void CrawlRawObjectData(IntermediateCrawlData crawlData, BytesAndOffset bytesAndOffset, int iTypeDescription, bool useStaticFields, ulong ptrFrom, int indexOfFrom)
        {
            var snapshot = crawlData.CachedMemorySnapshot;

            var fields = useStaticFields ? snapshot.TypeDescriptions.fieldIndicesOwnedStatic[iTypeDescription] : snapshot.TypeDescriptions.FieldIndicesInstance[iTypeDescription];
            foreach (var iField in fields)
            {
                int iField_TypeDescription_TypeIndex = snapshot.FieldDescriptions.TypeIndex[iField];
                int iField_TypeDescription_ArrayIndex = snapshot.TypeDescriptions.TypeIndex2ArrayIndex(iField_TypeDescription_TypeIndex);

                var fieldLocation = bytesAndOffset.Add(snapshot.FieldDescriptions.Offset[iField] - (useStaticFields ? 0 : snapshot.VirtualMachineInformation.ObjectHeaderSize));

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

            var arrayLength = ArrayTools.ReadArrayLength(snapshot, data.ptr, obj.ITypeDescription);
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
                    arrayData = arrayData.Add(typeDescriptions.Size[iElementTypeDescription]);
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
                return ArrayTools.ReadArrayObjectSizeInBytes(snapshot, address, iTypeDescription);

            if (snapshot.TypeDescriptions.ITypeString == iTypeDescription)
                return StringTools.ReadStringObjectSizeInBytes(bo, snapshot.VirtualMachineInformation);

            //array and string are the only types that are special, all other types just have one size, which is stored in the type description
            return snapshot.TypeDescriptions.Size[iTypeDescription];
        }

        static int SizeOfObjectInBytes(CachedSnapshot snapshot, int iTypeDescription, BytesAndOffset byteOffset, CachedSnapshot.ManagedMemorySectionEntriesCache heap)
        {
            if (iTypeDescription < 0) return 0;

            if (snapshot.TypeDescriptions.HasFlag(iTypeDescription, TypeFlags.kArray))
                return ArrayTools.ReadArrayObjectSizeInBytes(snapshot, byteOffset, iTypeDescription);

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
                Debug.LogError($"Bad object detected:\nheader at address: { DefaultDataFormatter.Instance.FormatPointer(data.ptr)} \nvtable at address {DefaultDataFormatter.Instance.FormatPointer(ptrIdentity)}" +
                    $"\nDetails:\n From object: {DefaultDataFormatter.Instance.FormatPointer(data.ptrFrom)}\n " +
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
        const int k_StringBuilderMaxCap = 8000; // After 8000 chars, StringBuilder will ring buffer the strings and our UI breaks. Also see https://referencesource.microsoft.com/#mscorlib/system/text/stringbuilder.cs,76
        public const int MaxStringLengthToRead = k_StringBuilderMaxCap - 10 /*Buffer for ellipsis, quotes and spaces*/;
        public const string Elipsis = " [...]";

        public static string ReadString(this BytesAndOffset bo, out int fullLength, VirtualMachineInformation virtualMachineInformation)
        {
            var lengthPointer = bo.Add(virtualMachineInformation.ObjectHeaderSize);
            fullLength = lengthPointer.ReadInt32();
            var firstChar = lengthPointer.Add(sizeof(int));

            if (fullLength < 0 || (long)fullLength * 2 > bo.bytes.Length - bo.offset - sizeof(int))
            {
#if DEBUG_VALIDATION
                Debug.LogError("Found a String Object of impossible length.");
#endif
                fullLength = 0;
            }

            if (fullLength > MaxStringLengthToRead)
            {
                var cappedLength = MaxStringLengthToRead;
                return $"{System.Text.Encoding.Unicode.GetString(firstChar.bytes, firstChar.offset, cappedLength * 2)}{Elipsis}";
            }
            else
                return System.Text.Encoding.Unicode.GetString(firstChar.bytes, firstChar.offset, fullLength * 2);
        }

        public static string ReadFirstStringLine(this BytesAndOffset bo, VirtualMachineInformation virtualMachineInformation, bool addQuotes)
        {
            var str = ReadString(bo, out _, virtualMachineInformation);
            var firstLineBreak = str.IndexOf('\n');
            const int maxCharsInLine = 30;
            if (firstLineBreak >= 0)
            {
                if (firstLineBreak < maxCharsInLine && str.Length > maxCharsInLine)
                {
                    // reduce our working set
                    str = str.Substring(0, Math.Min(str.Length, maxCharsInLine));
                }
                str = str.Replace("\n", "\\n");
                str += " [...]";
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
            if (length < 0 || (long)length * 2 > bo.bytes.Length - bo.offset - sizeof(int))
            {
#if DEBUG_VALIDATION
                Debug.LogError("Found a String Object of impossible length.");
#endif
                length = 0;
            }

            return virtualMachineInformation.ObjectHeaderSize + /*lengthfield*/ 1 + (length * /*utf16=2bytes per char*/ 2) + /*2 zero terminators*/ 2;
        }
    }
    internal class ArrayInfo
    {
        public ulong baseAddress;
        public int[] rank;
        public int length;
        public int elementSize;
        public int arrayTypeDescription;
        public int elementTypeDescription;
        public BytesAndOffset header;
        public BytesAndOffset data;
        public BytesAndOffset GetArrayElement(int index)
        {
            return data.Add(elementSize * index);
        }

        public ulong GetArrayElementAddress(int index)
        {
            return baseAddress + (ulong)(elementSize * index);
        }

        public string IndexToRankedString(int index)
        {
            return ArrayTools.ArrayRankIndexToString(rank, index);
        }

        public string ArrayRankToString()
        {
            return ArrayTools.ArrayRankToString(rank);
        }
    }
    internal static class ArrayTools
    {
        public static ArrayInfo GetArrayInfo(CachedSnapshot data, BytesAndOffset arrayData, int iTypeDescriptionArrayType)
        {
            var virtualMachineInformation = data.VirtualMachineInformation;
            var arrayInfo = new ArrayInfo();
            arrayInfo.baseAddress = 0;
            arrayInfo.arrayTypeDescription = iTypeDescriptionArrayType;


            arrayInfo.header = arrayData;
            arrayInfo.data = arrayInfo.header.Add(virtualMachineInformation.ArrayHeaderSize);
            ulong bounds;
            arrayInfo.header.Add(virtualMachineInformation.ArrayBoundsOffsetInHeader).TryReadPointer(out bounds);

            if (bounds == 0)
            {
                arrayInfo.length = arrayInfo.header.Add(virtualMachineInformation.ArraySizeOffsetInHeader).ReadInt32();
                arrayInfo.rank = new int[1] { arrayInfo.length };
            }
            else
            {
                int rank = data.TypeDescriptions.GetRank(iTypeDescriptionArrayType);
                arrayInfo.rank = new int[rank];

                var cursor = data.ManagedHeapSections.Find(bounds, virtualMachineInformation);
                if (cursor.IsValid)
                {
                    arrayInfo.length = 1;
                    for (int i = 0; i != rank; i++)
                    {
                        var l = cursor.ReadInt32();
                        arrayInfo.length *= l;
                        arrayInfo.rank[i] = l;
                        cursor = cursor.Add(8);
                    }
                }
                else
                {
                    //object has corrupted data
                    arrayInfo.length = 0;
                    for (int i = 0; i != rank; i++)
                    {
                        arrayInfo.rank[i] = -1;
                    }
                }
            }

            arrayInfo.elementTypeDescription = data.TypeDescriptions.BaseOrElementTypeIndex[iTypeDescriptionArrayType];
            if (arrayInfo.elementTypeDescription == -1) //We currently do not handle uninitialized types as such override the type, making it return pointer size
            {
                arrayInfo.elementTypeDescription = iTypeDescriptionArrayType;
            }
            if (data.TypeDescriptions.HasFlag(arrayInfo.elementTypeDescription, TypeFlags.kValueType))
            {
                arrayInfo.elementSize = data.TypeDescriptions.Size[arrayInfo.elementTypeDescription];
            }
            else
            {
                arrayInfo.elementSize = virtualMachineInformation.PointerSize;
            }
            return arrayInfo;
        }

        public static int GetArrayElementSize(CachedSnapshot data, int iTypeDescriptionArrayType)
        {
            int iElementTypeDescription = data.TypeDescriptions.BaseOrElementTypeIndex[iTypeDescriptionArrayType];
            if (data.TypeDescriptions.HasFlag(iElementTypeDescription, TypeFlags.kValueType))
            {
                return data.TypeDescriptions.Size[iElementTypeDescription];
            }
            return data.VirtualMachineInformation.PointerSize;
        }

        public static string ArrayRankToString(int[] rankLength)
        {
            string o = "";
            for (int i = 0; i < rankLength.Length; ++i)
            {
                if (o.Length > 0)
                {
                    o += ", ";
                }
                o += rankLength[i].ToString();
            }
            return o;
        }

        public static string ArrayRankIndexToString(int[] rankLength, int index)
        {
            string o = "";
            int remainder = index;
            for (int i = 1; i < rankLength.Length; ++i)
            {
                if (o.Length > 0)
                {
                    o += ", ";
                }
                var l = rankLength[i];
                int rankIndex = remainder / l;
                o += rankIndex.ToString();
                remainder = remainder - rankIndex * l;
            }
            if (o.Length > 0)
            {
                o += ", ";
            }
            o += remainder;
            return o;
        }

        public static int[] ReadArrayRankLength(CachedSnapshot data, CachedSnapshot.ManagedMemorySectionEntriesCache heap, UInt64 address, int iTypeDescriptionArrayType, VirtualMachineInformation virtualMachineInformation)
        {
            if (iTypeDescriptionArrayType < 0) return null;

            var bo = heap.Find(address, virtualMachineInformation);
            ulong bounds;
            bo.Add(virtualMachineInformation.ArrayBoundsOffsetInHeader).TryReadPointer(out bounds);

            if (bounds == 0)
            {
                return new int[1] { bo.Add(virtualMachineInformation.ArraySizeOffsetInHeader).ReadInt32() };
            }

            var cursor = heap.Find(bounds, virtualMachineInformation);
            int rank = data.TypeDescriptions.GetRank(iTypeDescriptionArrayType);
            int[] l = new int[rank];
            for (int i = 0; i != rank; i++)
            {
                l[i] = cursor.ReadInt32();
                cursor = cursor.Add(8);
            }
            return l;
        }

        public static int ReadArrayLength(CachedSnapshot data, UInt64 address, int iTypeDescriptionArrayType)
        {
            if (iTypeDescriptionArrayType < 0)
            {
                return 0;
            }

            var heap = data.ManagedHeapSections;
            var bo = heap.Find(address, data.VirtualMachineInformation);
            return ReadArrayLength(data, bo, iTypeDescriptionArrayType);
        }

        public static int ReadArrayLength(CachedSnapshot data, BytesAndOffset arrayData, int iTypeDescriptionArrayType)
        {
            if (iTypeDescriptionArrayType < 0) return 0;

            var virtualMachineInformation = data.VirtualMachineInformation;
            var heap = data.ManagedHeapSections;
            var bo = arrayData;

            ulong bounds;
            bo.Add(virtualMachineInformation.ArrayBoundsOffsetInHeader).TryReadPointer(out bounds);

            if (bounds == 0)
                return bo.Add(virtualMachineInformation.ArraySizeOffsetInHeader).ReadInt32();

            var cursor = heap.Find(bounds, virtualMachineInformation);
            int length = 0;

            if (cursor.IsValid)
            {
                length = 1;
                int rank = data.TypeDescriptions.GetRank(iTypeDescriptionArrayType);
                for (int i = 0; i != rank; i++)
                {
                    length *= cursor.ReadInt32();
                    cursor = cursor.Add(8);
                }
            }

            return length;
        }

        public static int ReadArrayObjectSizeInBytes(CachedSnapshot data, UInt64 address, int iTypeDescriptionArrayType)
        {
            var arrayLength = ReadArrayLength(data, address, iTypeDescriptionArrayType);

            var virtualMachineInformation = data.VirtualMachineInformation;
            var ti = data.TypeDescriptions.BaseOrElementTypeIndex[iTypeDescriptionArrayType];
            var ai = data.TypeDescriptions.TypeIndex2ArrayIndex(ti);
            var isValueType = data.TypeDescriptions.HasFlag(ai, TypeFlags.kValueType);

            var elementSize = isValueType ? data.TypeDescriptions.Size[ai] : virtualMachineInformation.PointerSize;
            return virtualMachineInformation.ArrayHeaderSize + elementSize * arrayLength;
        }

        public static int ReadArrayObjectSizeInBytes(CachedSnapshot data, BytesAndOffset arrayData, int iTypeDescriptionArrayType)
        {
            var arrayLength = ReadArrayLength(data, arrayData, iTypeDescriptionArrayType);
            var virtualMachineInformation = data.VirtualMachineInformation;

            var ti = data.TypeDescriptions.BaseOrElementTypeIndex[iTypeDescriptionArrayType];
            if (ti == -1) // check added as element type index can be -1 if we are dealing with a class member (eg: Dictionary.Entry) whose type is uninitialized due to their generic data not getting inflated a.k.a unused types
            {
                ti = iTypeDescriptionArrayType;
            }

            var ai = data.TypeDescriptions.TypeIndex2ArrayIndex(ti);
            var isValueType = data.TypeDescriptions.HasFlag(ai, TypeFlags.kValueType);
            var elementSize = isValueType ? data.TypeDescriptions.Size[ai] : virtualMachineInformation.PointerSize;

            return virtualMachineInformation.ArrayHeaderSize + elementSize * arrayLength;
        }
    }
}

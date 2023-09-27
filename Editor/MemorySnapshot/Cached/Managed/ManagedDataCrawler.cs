using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Format;
using Unity.Profiling;
using UnityEngine;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;

namespace Unity.MemoryProfiler.Editor
{
    static class ManagedDataCrawler
    {
        internal struct StackCrawlData
        {
            public ulong Ptr;
            public int ActualFieldFromITypeDescription;
            public SourceIndex IndexOfFrom;
            public int FieldFrom;
            public int ValueTypeFieldFrom;
            public int AdditionalValueTypeFieldOffset;
            public int FromArrayIndex;
        }

        class IntermediateCrawlData
        {
            public List<int> TypesWithStaticFields { private set; get; }
            public Stack<StackCrawlData> CrawlDataStack { private set; get; }
            public ref DynamicArray<ManagedObjectInfo> ManagedObjectInfos => ref CachedMemorySnapshot.CrawledData.ManagedObjects;
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
                for (int i = 0; i != snapshot.TypeDescriptions.Count; ++i)
                {
                    if (snapshot.TypeDescriptions.HasStaticFieldData(i))
                    {
                        TypesWithStaticFields.Add(i);
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

                    crawlData.ManagedObjectInfos.Push(moi);
                }
                uniqueHandlesBegin = uniqueHandlesPtr; //reset iterator
                ulong* uniqueHandlesEnd = uniqueHandlesPtr + writtenRange;
                //add handles for processing
                while (uniqueHandlesBegin != uniqueHandlesEnd)
                {
                    crawlData.CrawlDataStack.Push(new StackCrawlData { Ptr = UnsafeUtility.ReadArrayElement<ulong>(uniqueHandlesBegin++, 0), ActualFieldFromITypeDescription = -1, IndexOfFrom = default, FieldFrom = -1, ValueTypeFieldFrom = -1, AdditionalValueTypeFieldOffset = 0, FromArrayIndex = -1 });
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
                CrawlRawObjectData(crawlData, bytesOffset, iTypeDescription, true, new SourceIndex(SourceIndex.SourceId.ManagedType, iTypeDescription));
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
                    ref var obj = ref snapshot.CrawledData.ManagedObjects[iManagedTo];
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
            ref var objectInfos = ref crawlData.ManagedObjectInfos;

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

                var isInUnityObjectTypeIndexToNativeTypeIndex = snapshot.TypeDescriptions.UnityObjectTypeIndexToNativeTypeIndex.TryGetValue(objectInfo.ITypeDescription, out var nativeTypeIndex);
                var isOrCouldBeAUnityObject = isInUnityObjectTypeIndexToNativeTypeIndex;
                if (!isInUnityObjectTypeIndexToNativeTypeIndex)
                {
                    if (i < snapshot.GcHandles.Count)
                    {
                        // If this managed object was reported because someone had a GC Handle on it, chances are pretty good that there is a Native Object behind this
                        // Given that the type isn't yet known to be a UnityObjectType, something might've gone wrong with the TypeDescription reporting
                        // (annecdotal evidence suggests that ScriptableSingletons residing in assemblies other that the Editor Assembly could be affected this way
                        // If this looks to be the case, try to patch the data back up as good as possible
                        isOrCouldBeAUnityObject = true;
                        nativeTypeIndex = -1;
                    }
                }
                if (isOrCouldBeAUnityObject)
                {
                    int instanceID = CachedSnapshot.NativeObjectEntriesCache.InstanceIDNone;
                    // TODO: Add index to a list of Managed Unity Objects here
                    var heapSection = snapshot.ManagedHeapSections.Find(objectInfo.PtrObject + (ulong)cachedPtrOffset, snapshot.VirtualMachineInformation);
                    if (!heapSection.IsValid)
                    {
                        // Don't warn if this was an attempt to fix broken data
                        if (isInUnityObjectTypeIndexToNativeTypeIndex)
                            Debug.LogWarning("Managed object (addr:" + objectInfo.PtrObject + ", index:" + objectInfo.ManagedObjectIndex + ") does not have data at cachedPtr offset(" + cachedPtrOffset + ")");
                    }
                    else
                    {
                        if (heapSection.TryReadPointer(out var cachedPtr) != BytesAndOffset.PtrReadError.Success ||
                            !snapshot.NativeObjects.NativeObjectAddressToInstanceId.TryGetValue(cachedPtr, out instanceID))
                            instanceID = CachedSnapshot.NativeObjectEntriesCache.InstanceIDNone;
                        // cachedPtr == 0UL or instanceID == CachedSnapshot.NativeObjectEntriesCache.InstanceIDNone -> Leaked Shell
                        // TODO: Add index to a list of leaked shells here.
                    }

                    if (instanceID != CachedSnapshot.NativeObjectEntriesCache.InstanceIDNone)
                    {
                        if (snapshot.NativeObjects.InstanceId2Index.TryGetValue(instanceID, out objectInfo.NativeObjectIndex))
                        {
                            snapshot.NativeObjects.ManagedObjectIndex[objectInfo.NativeObjectIndex] = i;

                            if (nativeTypeIndex == -1)
                            {
                                nativeTypeIndex = snapshot.NativeObjects.NativeTypeArrayIndex[objectInfo.NativeObjectIndex];

                                if (!isInUnityObjectTypeIndexToNativeTypeIndex)
                                {
                                    if (nativeTypeIndex == snapshot.NativeTypes.MonoBehaviourIdx
                                        || nativeTypeIndex == snapshot.NativeTypes.ScriptableObjectIdx
                                        || nativeTypeIndex == snapshot.NativeTypes.EditorScriptableObjectIdx)
                                    {
                                        // This acutally WAS a Unity Object with faulty type data reporting, fix up UnityObjectTypeIndexToNativeTypeIndex,
                                        // but set native type to -1 so that ReportManagedTypeToNativeTypeConnectionForThisTypeAndItsBaseTypes can fix up all managed base types that ARE reported
                                        snapshot.TypeDescriptions.UnityObjectTypeIndexToNativeTypeIndex.Add(objectInfo.ITypeDescription, -1);
                                        isInUnityObjectTypeIndexToNativeTypeIndex = true;
                                    }
                                    else
                                    {
#if DEBUG_VALIDATION
                                        Debug.LogWarning("Managed object (addr:" + objectInfo.PtrObject + ", index:" + objectInfo.ManagedObjectIndex + ") looked like it could have been a Unity Object with a faultily reported managed type but wasn't a ScriptableObject or MonoBehaviour");
#endif
                                        // As a safeguard measure, in case that there are objects:
                                        // - with GCHandles on them
                                        // - with a field at the same position as m_CachedHandle
                                        // - which contains data that looks like a valid address to a valid native object
                                        // but which isn't likely to have broken reported type data (aka is not a scriptable type)
                                        // Ignore it and BAIL!
                                        objectInfo.NativeObjectIndex = -1;
                                        objectInfos[i] = objectInfo;
                                        continue;
                                    }
                                }

                                ReportManagedTypeToNativeTypeConnectionForThisTypeAndItsBaseTypes(snapshot, nativeTypeIndex, objectInfo.ITypeDescription);
                            }
                            if (snapshot.HasConnectionOverhaul)
                            {
                                snapshot.CrawledData.Connections.Add(ManagedConnection.MakeUnityEngineObjectConnection(objectInfo.NativeObjectIndex, objectInfo.ManagedObjectIndex));
                                var refCount = ++snapshot.NativeObjects.RefCount[objectInfo.NativeObjectIndex];
                                snapshot.NativeObjects.RefCount[objectInfo.NativeObjectIndex] = refCount;
#if DEBUG_VALIDATION
                                managedObjectAddressToNativeObjectIndex.Add(objectInfo.PtrObject, objectInfo.NativeObjectIndex);
#endif
                            }
                        }
                        else
                            objectInfo.NativeObjectIndex = -1;
                    }
                    if (nativeTypeIndex == -1 && isInUnityObjectTypeIndexToNativeTypeIndex)
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

        static void CrawlRawObjectData(
            IntermediateCrawlData crawlData, BytesAndOffset bytesAndOffsetOfFieldDataWithoutHeader,
            int iTypeDescription, bool useStaticFields, SourceIndex indexOfFrom,
            int baseFieldFrom = -1, int additionalFieldOffset = 0, int fromArrayIndex = -1)
        {
            var snapshot = crawlData.CachedMemorySnapshot;

            var fields = useStaticFields ? snapshot.TypeDescriptions.fieldIndicesOwnedStatic[iTypeDescription] : snapshot.TypeDescriptions.FieldIndicesInstance[iTypeDescription];
            if (!useStaticFields && !snapshot.TypeDescriptions.HasFlag(iTypeDescription, TypeFlags.kValueType))
            {
                // Add the Object header. All callers already skipped the header.
                additionalFieldOffset += (int)snapshot.VirtualMachineInformation.ObjectHeaderSize;
            }
            foreach (var iField in fields)
            {
                var baseField = baseFieldFrom < 0 ? iField : baseFieldFrom;
                int iField_TypeDescription_TypeIndex = snapshot.FieldDescriptions.TypeIndex[iField];

                var fieldOffset = snapshot.FieldDescriptions.Offset[iField];
                if (!useStaticFields)
                    fieldOffset -= (int)snapshot.VirtualMachineInformation.ObjectHeaderSize;
                var fieldLocation = bytesAndOffsetOfFieldDataWithoutHeader.Add((ulong)fieldOffset);

                if (snapshot.TypeDescriptions.HasFlag(iField_TypeDescription_TypeIndex, TypeFlags.kValueType))
                {
                    CrawlRawObjectData(
                        crawlData, fieldLocation,
                        iField_TypeDescription_TypeIndex, false, indexOfFrom,
                        baseFieldFrom: baseField,
                        additionalFieldOffset: additionalFieldOffset + fieldOffset,
                        fromArrayIndex: fromArrayIndex);
                    continue; // FIXME: this means the crawler ignores int/long and pointer types e.g. System.Void*! i.e. primitves that the conservative GC might read as possible references
                }


                if (fieldLocation.TryReadPointer(out var address) == BytesAndOffset.PtrReadError.Success
                    // don't process null pointers
                    && address != 0)
                {
                    crawlData.CrawlDataStack.Push(new StackCrawlData()
                    {
                        Ptr = address,
                        ActualFieldFromITypeDescription = iTypeDescription,
                        IndexOfFrom = indexOfFrom,
                        FieldFrom = baseField,
                        ValueTypeFieldFrom = iField,
                        AdditionalValueTypeFieldOffset = additionalFieldOffset,
                        FromArrayIndex = fromArrayIndex
                    });
                }
            }
        }

        static bool CrawlPointer(IntermediateCrawlData dataStack)
        {
            Debug.Assert(dataStack.CrawlDataStack.Count > 0);

            var snapshot = dataStack.CachedMemorySnapshot;
            var typeDescriptions = snapshot.TypeDescriptions;
            var data = dataStack.CrawlDataStack.Pop();
            var virtualMachineInformation = snapshot.VirtualMachineInformation;
            var managedHeapSections = snapshot.ManagedHeapSections;
            var byteOffset = managedHeapSections.Find(data.Ptr, virtualMachineInformation);

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


            obj = ParseObjectHeader(snapshot, data.Ptr, out var wasAlreadyCrawled, false, byteOffset);
            bool addConnection = data.IndexOfFrom.Valid;
            if (addConnection)
                ++obj.RefCount;

            if (!obj.IsValid())
                return false;

            snapshot.CrawledData.ManagedObjects[obj.ManagedObjectIndex] = obj;
            snapshot.CrawledData.MangedObjectIndexByAddress[obj.PtrObject] = obj.ManagedObjectIndex;

            if (addConnection)
            {
                // if FieldFromITypeDescription differs from the type of the holding Type or Object, that's because the field is held by a value type
                var valueTypeFieldOwningITypeDescription = data.IndexOfFrom.Id switch
                {
                    SourceIndex.SourceId.ManagedType => data.IndexOfFrom.Index != data.ActualFieldFromITypeDescription ? data.ActualFieldFromITypeDescription : -1,
                    SourceIndex.SourceId.ManagedObject => obj.ITypeDescription != data.ActualFieldFromITypeDescription ? data.ActualFieldFromITypeDescription : -1,
                    _ => -1
                };
                dataStack.ManagedConnections.Add(ManagedConnection.MakeConnection(snapshot, data.IndexOfFrom, obj.ManagedObjectIndex, data.FieldFrom,
                    valueTypeFieldOwningITypeDescription, data.ValueTypeFieldFrom, data.AdditionalValueTypeFieldOffset, data.FromArrayIndex));
            }

            if (wasAlreadyCrawled)
                return true;

            if (!typeDescriptions.HasFlag(obj.ITypeDescription, TypeFlags.kArray))
            {
                CrawlRawObjectData(dataStack, byteOffset.Add(snapshot.VirtualMachineInformation.ObjectHeaderSize), obj.ITypeDescription, false, new SourceIndex(SourceIndex.SourceId.ManagedObject, obj.ManagedObjectIndex));
                return true;
            }

            var arrayLength = ManagedHeapArrayDataTools.ReadArrayLength(snapshot, data.Ptr, obj.ITypeDescription);
            int iElementTypeDescription = typeDescriptions.BaseOrElementTypeIndex[obj.ITypeDescription];
            if (iElementTypeDescription == -1)
            {
                return false; //do not crawl uninitialized object types, as we currently don't have proper handling for these
            }
            var arrayData = byteOffset.Add(virtualMachineInformation.ArrayHeaderSize);
            var arrayObjectIndex = new SourceIndex(SourceIndex.SourceId.ManagedObject, obj.ManagedObjectIndex);
            for (int i = 0; i < arrayLength; i++)
            {
                if (typeDescriptions.HasFlag(iElementTypeDescription, TypeFlags.kValueType))
                {
                    CrawlRawObjectData(dataStack, arrayData, iElementTypeDescription, false, arrayObjectIndex, fromArrayIndex: i);
                    arrayData = arrayData.Add((ulong)typeDescriptions.Size[iElementTypeDescription]);
                }
                else
                {
                    if (arrayData.TryReadPointer(out var arrayDataPtr) != BytesAndOffset.PtrReadError.Success)
                        return false;

                    // don't process null pointers
                    if (arrayDataPtr != 0)
                        dataStack.CrawlDataStack.Push(new StackCrawlData() { Ptr = arrayDataPtr, ActualFieldFromITypeDescription = obj.ITypeDescription, IndexOfFrom = arrayObjectIndex, FieldFrom = -1, ValueTypeFieldFrom = -1, AdditionalValueTypeFieldOffset = 0, FromArrayIndex = i });
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

        internal static ManagedObjectInfo ParseObjectHeader(CachedSnapshot snapshot, ulong addressOfHeader, out bool wasAlreadyCrawled, bool ignoreBadHeaderError, BytesAndOffset byteOffset)
        {
            ref var objectList = ref snapshot.CrawledData.ManagedObjects;
            var objectsByAddress = snapshot.CrawledData.MangedObjectIndexByAddress;

            ManagedObjectInfo objectInfo;
            if (!snapshot.CrawledData.MangedObjectIndexByAddress.TryGetValue(addressOfHeader, out var idx))
            {
                if (TryParseObjectHeader(snapshot, addressOfHeader, out objectInfo, byteOffset))
                {
                    objectInfo.ManagedObjectIndex = (int)objectList.Count;
                    objectList.Push(objectInfo);
                    objectsByAddress.Add(addressOfHeader, objectInfo.ManagedObjectIndex);
                }
                wasAlreadyCrawled = false;
                return objectInfo;
            }

            objectInfo = objectList[idx];
            // this happens on objects from gcHandles, they are added before any other crawled object but have their ptr set to 0.
            if (objectInfo.PtrObject == 0)
            {
                idx = objectInfo.ManagedObjectIndex;
                if (TryParseObjectHeader(snapshot, addressOfHeader, out objectInfo, byteOffset))
                {
                    objectInfo.ManagedObjectIndex = idx;
                    objectList[idx] = objectInfo;
                    objectsByAddress[addressOfHeader] = idx;
                }

                wasAlreadyCrawled = false;
                return objectInfo;
            }

            wasAlreadyCrawled = true;
            return objectInfo;
        }

        public static bool TryParseObjectHeader(CachedSnapshot snapshot, ulong addressOfHeader, out ManagedObjectInfo info, BytesAndOffset boHeader)
        {
            bool resolveFailed = false;
            var heap = snapshot.ManagedHeapSections;
            info = new ManagedObjectInfo
            {
                ManagedObjectIndex = -1
            };

            if (!boHeader.IsValid) boHeader = heap.Find(addressOfHeader, snapshot.VirtualMachineInformation);
            if (!boHeader.IsValid)
                resolveFailed = true;
            else
            {
                boHeader.TryReadPointer(out var ptrIdentity);

                info.PtrTypeInfo = ptrIdentity;
                info.ITypeDescription = snapshot.TypeDescriptions.TypeInfo2ArrayIndex(info.PtrTypeInfo);

                if (info.ITypeDescription < 0)
                {
                    var boIdentity = heap.Find(ptrIdentity, snapshot.VirtualMachineInformation);
                    if (boIdentity.IsValid)
                    {
                        boIdentity.TryReadPointer(out var ptrTypeInfo);
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
                info.data = default;

                return false;
            }


            info.Size = SizeOfObjectInBytes(snapshot, info.ITypeDescription, boHeader, heap);
            info.data = boHeader;
            info.PtrObject = addressOfHeader;
            return true;
        }
    }
}

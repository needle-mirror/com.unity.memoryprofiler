using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.MemoryProfiler.Editor.Format;
using UnityEngine;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;

namespace Unity.MemoryProfiler.Editor
{
    internal struct ObjectConnection
    {
        public static ObjectData[] GetAllReferencingObjects(CachedSnapshot snapshot, ObjectData obj)
        {
            var referencingObjects = new List<ObjectData>();
            long objIndex = -1;
            switch (obj.dataType)
            {
                case ObjectDataType.Array:
                case ObjectDataType.BoxedValue:
                case ObjectDataType.Object:
                {
                    if (snapshot.CrawledData.MangedObjectIndexByAddress.TryGetValue(obj.hostManagedObjectPtr, out var idx))
                    {
                        objIndex = snapshot.ManagedObjectIndexToUnifiedObjectIndex(idx);
                        AddManagedReferences(snapshot, objIndex, ref referencingObjects);
                    }
                    break;
                }
                case ObjectDataType.NativeObject:
                    objIndex = snapshot.NativeObjectIndexToUnifiedObjectIndex(obj.nativeObjectIndex);
                    var managedObjectIndex = snapshot.NativeObjects.ManagedObjectIndex[obj.nativeObjectIndex];
                    if (managedObjectIndex > 0)
                        AddManagedReferences(snapshot, managedObjectIndex, ref referencingObjects);

                    break;
            }

            // Add connections from the raw snapshot
            if (objIndex >= 0 && snapshot.Connections.ReferencedBy.ContainsKey((int)objIndex))
            {
                foreach (var i in snapshot.Connections.ReferencedBy[(int)objIndex])
                {
                    referencingObjects.Add(ObjectData.FromUnifiedObjectIndex(snapshot, i));
                }
            }

            return referencingObjects.ToArray();
        }

        static void AddManagedReferences(CachedSnapshot snapshot, long objectIndex, ref List<ObjectData> results)
        {
            if (!snapshot.CrawledData.ConnectionsToMappedToUnifiedIndex.TryGetValue(objectIndex, out var connectionIndicies))
                return;

            // Add crawled connections
            foreach (var i in connectionIndicies)
            {
                var c = snapshot.CrawledData.Connections[i];
                switch (c.TypeOfConnection)
                {
                    case ManagedConnection.ConnectionType.ManagedObject_To_ManagedObject:
                    {
                        var objParent = ObjectData.FromManagedObjectIndex(snapshot, c.FromManagedObjectIndex);
                        if (c.FieldFrom >= 0)
                            results.Add(objParent.GetInstanceFieldBySnapshotFieldIndex(snapshot, c.FieldFrom, false));
                        else if (c.ArrayIndexFrom >= 0)
                            results.Add(objParent.GetArrayElement(snapshot, c.ArrayIndexFrom, false));
                        else
                            results.Add(objParent);

                        break;
                    }
                    case ManagedConnection.ConnectionType.ManagedType_To_ManagedObject:
                    {
                        var objType = ObjectData.FromManagedType(snapshot, c.FromManagedType);
                        if (c.FieldFrom >= 0)
                            results.Add(objType.GetInstanceFieldBySnapshotFieldIndex(snapshot, c.FieldFrom, false));
                        else if (c.ArrayIndexFrom >= 0)
                            results.Add(objType.GetArrayElement(snapshot, c.ArrayIndexFrom, false));
                        else
                            results.Add(objType);

                        break;
                    }
                    case ManagedConnection.ConnectionType.UnityEngineObject:
                    {
                        // these get at added in the loop at the end of the function
                        // tried using a hash set to prevent duplicates but the lookup during add locks up the window
                        // if there are more than about 50k references
                        //referencingObjects.Add(ObjectData.FromNativeObjectIndex(snapshot, c.UnityEngineNativeObjectIndex));
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Tries to get all instance IDs of Transform Components connected to the passed in Transform Component's instance ID.
        /// </summary>
        /// <param name="snapshot"></param>
        /// <param name="transformInstanceID">The instance ID of the Transform to check</param>
        /// <param name="parentTransformInstanceIdToIgnore">If you are only looking for child transforms, pass in the parent ID so it will be ignored. -1 if no parent should be ignored.</param>
        /// <param name="outInstanceIds">The connected instanceIDs if any where found, otherwise it is empty.</param>
        /// <returns>Returns True if connected Transform IDs were found, False if not.</returns>
        public static bool TryGetConnectedTransformInstanceIdsFromTransformInstanceId(CachedSnapshot snapshot, int transformInstanceID, int parentTransformInstanceIdToIgnore, ref HashSet<int> outInstanceIds)
        {
            var found = outInstanceIds;
            found.Clear();
            var transformToSearchConnectionsFor = ObjectData.FromNativeObjectIndex(snapshot, snapshot.NativeObjects.InstanceId2Index[transformInstanceID]);
            if (snapshot.Connections.ReferenceTo.TryGetValue((int)transformToSearchConnectionsFor.GetUnifiedObjectIndex(snapshot), out var list))
            {
                foreach (var connection in list)
                {
                    var possiblyConnectedTransform = ObjectData.FromUnifiedObjectIndex(snapshot, connection);
                    var instanceIdOfPossibleConnection = possiblyConnectedTransform.GetInstanceID(snapshot);
                    if (possiblyConnectedTransform.isNative && snapshot.NativeTypes.TransformIdx == snapshot.NativeObjects.NativeTypeArrayIndex[possiblyConnectedTransform.nativeObjectIndex]
                        && instanceIdOfPossibleConnection != NativeObjectEntriesCache.InstanceIDNone && instanceIdOfPossibleConnection != parentTransformInstanceIdToIgnore && instanceIdOfPossibleConnection != transformInstanceID)
                        found.Add(instanceIdOfPossibleConnection);
                }
                return found.Count > 0;
            }
            return false;
        }

        public static int GetGameObjectInstanceIdFromTransformInstanceId(CachedSnapshot snapshot, int instanceID)
        {
            var transform = ObjectData.FromNativeObjectIndex(snapshot, snapshot.NativeObjects.InstanceId2Index[instanceID]);
            if (snapshot.Connections.ReferenceTo.TryGetValue((int)transform.GetUnifiedObjectIndex(snapshot), out var list))
            {
                foreach (var connection in list)
                {
                    var objectData = ObjectData.FromUnifiedObjectIndex(snapshot, connection);
                    if (objectData.isNative && objectData.IsGameObject(snapshot) && snapshot.NativeObjects.ObjectName[transform.nativeObjectIndex] == snapshot.NativeObjects.ObjectName[ObjectData.FromUnifiedObjectIndex(snapshot, connection).nativeObjectIndex])
                        return snapshot.NativeObjects.InstanceId[objectData.nativeObjectIndex];
                }
            }
            return NativeObjectEntriesCache.InstanceIDNone;
        }

        public static ObjectData[] GenerateReferencesTo(CachedSnapshot snapshot, ObjectData obj)
        {
            var referencedObjects = new List<ObjectData>();
            long objIndex = -1;
            HashSet<long> foundUnifiedIndices = new HashSet<long>();
            switch (obj.dataType)
            {
                case ObjectDataType.Array:
                case ObjectDataType.BoxedValue:
                case ObjectDataType.Object:
                {
                    if (snapshot.CrawledData.MangedObjectIndexByAddress.TryGetValue(obj.hostManagedObjectPtr, out var idx))
                    {
                        objIndex = snapshot.ManagedObjectIndexToUnifiedObjectIndex(idx);

                        if (!snapshot.CrawledData.ConnectionsFromMappedToUnifiedIndex.TryGetValue(objIndex, out var connectionIdxs))
                            break;

                        //add crawled connections
                        foreach (var i in connectionIdxs)
                        {
                            var c = snapshot.CrawledData.Connections[i];
                            switch (c.TypeOfConnection)
                            {
                                case ManagedConnection.ConnectionType.ManagedObject_To_ManagedObject:
                                    referencedObjects.Add(ObjectData.FromUnifiedObjectIndex(snapshot, c.GetUnifiedIndexTo(snapshot)));
                                    break;
                                case ManagedConnection.ConnectionType.UnityEngineObject:
                                    // these get at added in the loop at the end of the function
                                    // tried using a hash set to prevent duplicates but the lookup during add locks up the window
                                    // if there are more than about 50k references
                                    referencedObjects.Add(ObjectData.FromNativeObjectIndex(snapshot, c.UnityEngineNativeObjectIndex));
                                    break;
                            }
                        }
                    }
                    break;
                }
                case ObjectDataType.NativeObject:
                {
                    objIndex = snapshot.NativeObjectIndexToUnifiedObjectIndex(obj.nativeObjectIndex);
                    if (!snapshot.CrawledData.ConnectionsMappedToNativeIndex.TryGetValue(obj.nativeObjectIndex, out var connectionIdxs))
                        break;

                    //add crawled connection
                    foreach (var i in connectionIdxs)
                    {
                        switch (snapshot.CrawledData.Connections[i].TypeOfConnection)
                        {
                            case ManagedConnection.ConnectionType.ManagedObject_To_ManagedObject:
                            case ManagedConnection.ConnectionType.ManagedType_To_ManagedObject:
                                break;
                            case ManagedConnection.ConnectionType.UnityEngineObject:
                                var managedIndex = snapshot.CrawledData.Connections[i].UnityEngineManagedObjectIndex;
                                foundUnifiedIndices.Add(snapshot.ManagedObjectIndexToUnifiedObjectIndex(managedIndex));
                                referencedObjects.Add(ObjectData.FromManagedObjectIndex(snapshot, managedIndex));
                                break;
                        }
                    }
                    break;
                }
                case ObjectDataType.Type:
                {     //TODO this will need to be changed at some point to use the mapped searches
                    if (snapshot.TypeDescriptions.TypeIndexToArrayIndex.TryGetValue(obj.managedTypeIndex, out var idx))
                    {
                        //add crawled connections
                        for (int i = 0; i != snapshot.CrawledData.Connections.Count; ++i)
                        {
                            var c = snapshot.CrawledData.Connections[i];
                            if (c.TypeOfConnection == ManagedConnection.ConnectionType.ManagedType_To_ManagedObject && c.FromManagedType == idx)
                            {
                                if (c.FieldFrom >= 0)
                                {
                                    referencedObjects.Add(obj.GetInstanceFieldBySnapshotFieldIndex(snapshot, c.FieldFrom, false));
                                }
                                else if (c.ArrayIndexFrom >= 0)
                                {
                                    referencedObjects.Add(obj.GetArrayElement(snapshot, c.ArrayIndexFrom, false));
                                }
                                else
                                {
                                    var referencedObject = ObjectData.FromManagedObjectIndex(snapshot, c.ToManagedObjectIndex);
                                    referencedObjects.Add(referencedObject);
                                }
                            }
                        }
                    }
                    break;
                }
            }

            //add connections from the raw snapshot
            if (objIndex >= 0 && snapshot.Connections.ReferenceTo.ContainsKey((int)objIndex))
            {
                var cns = snapshot.Connections.ReferenceTo[(int)objIndex];
                foreach (var i in cns)
                {
                    // Don't count Native -> Managed Connections again if they have been added based on m_CachedPtr entries
                    if (!foundUnifiedIndices.Contains(i))
                    {
                        foundUnifiedIndices.Add(i);
                        referencedObjects.Add(ObjectData.FromUnifiedObjectIndex(snapshot, i));
                    }
                }
            }
            return referencedObjects.ToArray();
        }

        public static ObjectData[] GetAllReferencedObjects(CachedSnapshot snapshot, ObjectData obj)
        {
            var referencedObjects = new List<ObjectData>();
            long objIndex = -1;
            HashSet<long> foundUnifiedIndices = new HashSet<long>();
            switch (obj.dataType)
            {
                case ObjectDataType.Array:
                case ObjectDataType.BoxedValue:
                case ObjectDataType.Object:
                {
                    if (snapshot.CrawledData.MangedObjectIndexByAddress.TryGetValue(obj.hostManagedObjectPtr, out var idx))
                    {
                        objIndex = snapshot.ManagedObjectIndexToUnifiedObjectIndex(idx);

                        if (!snapshot.CrawledData.ConnectionsFromMappedToUnifiedIndex.TryGetValue(objIndex, out var connectionIdxs))
                            break;

                        //add crawled connections
                        foreach (var i in connectionIdxs)
                        {
                            var c = snapshot.CrawledData.Connections[i];
                            switch (c.TypeOfConnection)
                            {
                                case ManagedConnection.ConnectionType.ManagedObject_To_ManagedObject:
                                    if (c.FieldFrom >= 0)
                                    {
                                        referencedObjects.Add(obj.GetInstanceFieldBySnapshotFieldIndex(snapshot, c.FieldFrom, false));
                                    }
                                    else if (c.ArrayIndexFrom >= 0)
                                    {
                                        referencedObjects.Add(obj.GetArrayElement(snapshot, c.ArrayIndexFrom, false));
                                    }
                                    else
                                    {
                                        var referencedObject = ObjectData.FromManagedObjectIndex(snapshot, c.ToManagedObjectIndex);
                                        referencedObjects.Add(referencedObject);
                                    }
                                    break;
                                case ManagedConnection.ConnectionType.UnityEngineObject:
                                    // these get at added in the loop at the end of the function
                                    // tried using a hash set to prevent duplicates but the lookup during add locks up the window
                                    // if there are more than about 50k references
                                    referencedObjects.Add(ObjectData.FromNativeObjectIndex(snapshot, c.UnityEngineNativeObjectIndex));
                                    break;
                            }
                        }
                    }
                    break;
                }
                case ObjectDataType.NativeObject:
                {
                    objIndex = snapshot.NativeObjectIndexToUnifiedObjectIndex(obj.nativeObjectIndex);
                    if (!snapshot.CrawledData.ConnectionsMappedToNativeIndex.TryGetValue(obj.nativeObjectIndex, out var connectionIdxs))
                        break;

                    //add crawled connection
                    foreach (var i in connectionIdxs)
                    {
                        switch (snapshot.CrawledData.Connections[i].TypeOfConnection)
                        {
                            case ManagedConnection.ConnectionType.ManagedObject_To_ManagedObject:
                            case ManagedConnection.ConnectionType.ManagedType_To_ManagedObject:
                                break;
                            case ManagedConnection.ConnectionType.UnityEngineObject:
                                // A ManagedConnection.ConnectionType.UnityEngineObject comes about because a Managed field's m_CachedPtr points at a Native Object
                                // while that connection is technically correct and correctly bidirectional, the Native -> Managed side of the connection
                                // should already be tracked via snapshot.Connections, aka GCHandles reported in the snapshot.
                                // To avoid double reporting this connection, track these in the hashmap to de-dup the list when adding connections based on GCHandle reporting
                                var managedIndex = snapshot.CrawledData.Connections[i].UnityEngineManagedObjectIndex;
                                foundUnifiedIndices.Add(snapshot.ManagedObjectIndexToUnifiedObjectIndex(managedIndex));
                                referencedObjects.Add(ObjectData.FromManagedObjectIndex(snapshot, managedIndex));
                                break;
                        }
                    }
                    break;
                }
                case ObjectDataType.Type:
                {     //TODO this will need to be changed at some point to use the mapped searches
                    if (snapshot.TypeDescriptions.TypeIndexToArrayIndex.TryGetValue(obj.managedTypeIndex, out var idx))
                    {
                        //add crawled connections
                        for (int i = 0; i != snapshot.CrawledData.Connections.Count; ++i)
                        {
                            var c = snapshot.CrawledData.Connections[i];
                            if (c.TypeOfConnection == ManagedConnection.ConnectionType.ManagedType_To_ManagedObject && c.FromManagedType == idx)
                            {
                                if (c.FieldFrom >= 0)
                                {
                                    referencedObjects.Add(obj.GetInstanceFieldBySnapshotFieldIndex(snapshot, c.FieldFrom, false));
                                }
                                else if (c.ArrayIndexFrom >= 0)
                                {
                                    referencedObjects.Add(obj.GetArrayElement(snapshot, c.ArrayIndexFrom, false));
                                }
                                else
                                {
                                    var referencedObject = ObjectData.FromManagedObjectIndex(snapshot, c.ToManagedObjectIndex);
                                    referencedObjects.Add(referencedObject);
                                }
                            }
                        }
                    }
                    break;
                }
            }

            //add connections from the raw snapshot
            if (objIndex >= 0 && snapshot.Connections.ReferenceTo.ContainsKey((int)objIndex))
            {
                var cns = snapshot.Connections.ReferenceTo[(int)objIndex];
                foreach (var i in cns)
                {
                    // Don't count Native -> Managed Connections again if they have been added based on m_CachedPtr entries
                    if (!foundUnifiedIndices.Contains(i))
                    {
                        foundUnifiedIndices.Add(i);
                        referencedObjects.Add(ObjectData.FromUnifiedObjectIndex(snapshot, i));
                    }
                }
            }
            return referencedObjects.ToArray();
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEditor;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;

namespace Unity.MemoryProfiler.Editor
{
    internal struct ObjectConnection
    {
        static readonly ObjectData[] k_EmptyObjectDataArray = new ObjectData[0];
        public static ObjectData[] GetAllReferencingObjects(CachedSnapshot snapshot, ObjectData obj)
        {
            var objIndex = default(SourceIndex);
            switch (obj.dataType)
            {
                case ObjectDataType.Array:
                case ObjectDataType.BoxedValue:
                case ObjectDataType.Object:
                {
                    if (snapshot.CrawledData.MangedObjectIndexByAddress.TryGetValue(obj.hostManagedObjectPtr, out var idx))
                    {
                        objIndex = new SourceIndex(SourceIndex.SourceId.ManagedObject, idx);
                    }
                    break;
                }
                case ObjectDataType.NativeObject:
                    objIndex = new SourceIndex(SourceIndex.SourceId.NativeObject, obj.nativeObjectIndex);
                    break;
            }
            return GetAllReferencingObjects(snapshot, objIndex);
        }

        public static ObjectData[] GetAllReferencingObjects(CachedSnapshot snapshot, SourceIndex objIndex)
        {
            if (!objIndex.Valid)
                return k_EmptyObjectDataArray;

            var referencingObjects = new List<ObjectData>();

            switch (objIndex.Id)
            {
                case SourceIndex.SourceId.None:
                    // ignore invalid indices
                    break;
                case SourceIndex.SourceId.NativeObject:
                    // if this is a Native object with a managed wrapper, get references to the wrapper too
                    var managedObjectIndex = snapshot.NativeObjects.ManagedObjectIndex[objIndex.Index];
                    if (managedObjectIndex > 0)
                        AddManagedReferences(snapshot, new SourceIndex(SourceIndex.SourceId.ManagedObject, managedObjectIndex), ref referencingObjects);

                    AddManagedReferences(snapshot, objIndex, ref referencingObjects);
                    break;
                default:
                    AddManagedReferences(snapshot, objIndex, ref referencingObjects);
                    break;
            }

            // Add connections from the raw snapshot
            if (objIndex.Valid && objIndex.Index >= 0 && snapshot.Connections.ReferencedBy.ContainsKey(objIndex))
            {
                foreach (var i in snapshot.Connections.ReferencedBy[objIndex])
                {
                    referencingObjects.Add(ObjectData.FromSourceLink(snapshot, i));
                }
            }

            return referencingObjects.ToArray();
        }

        static void AddManagedReferences(CachedSnapshot snapshot, SourceIndex objectIndex, ref List<ObjectData> results)
        {
            if (!snapshot.CrawledData.ConnectionsToMappedToSourceIndex.TryGetValue(objectIndex, out var connectionIndicies))
                return;

            // Add crawled connections
            foreach (var i in connectionIndicies)
            {
                var c = snapshot.CrawledData.Connections[i];
                if (c.IndexTo.Id == SourceIndex.SourceId.ManagedObject)
                {
                    if (c.IndexFrom.Id == SourceIndex.SourceId.NativeObject)
                        // Native Unity Engine Object to Managed Object connection based on m_CachedPtr.
                        // these get at added in the loop at the end of the calling GetAllReferencingObjects function.
                        // Tried using a hash set to prevent duplicates but the lookup during add locks up the window
                        // if there are more than about 50k references
                        continue;

                    results.Add(GetManagedReferenceSource(snapshot, c));
                }
                else
                    throw new NotImplementedException();
            }
        }

        static ObjectData GetManagedReferenceSource(CachedSnapshot snapshot, ManagedConnection c)
        {
            var objParent = c.IndexFrom.Id switch
            {
                SourceIndex.SourceId.ManagedType => ObjectData.FromManagedType(snapshot, c.FromManagedType),
                SourceIndex.SourceId.ManagedObject => ObjectData.FromManagedObjectIndex(snapshot, c.FromManagedObjectIndex),
                _ => throw new NotImplementedException()
            };

            // The connection could be through a field on a value type in an array,
            // so get a potential array element first and from there the potential field in that element afterwards
            if (c.ArrayIndexFrom >= 0)
                objParent = objParent.GetArrayElement(snapshot, c.ArrayIndexFrom, false);

            if (c.FieldFrom >= 0)
                return objParent.GetFieldByFieldDescriptionsIndex(snapshot, c.FieldFrom, false,
                    valueTypeFieldOwningITypeDescription: c.ValueTypeFieldOwningITypeDescription,
                    valueTypeFieldIndex: c.ValueTypeFieldFrom,
                    addionalValueTypeFieldOffset: c.AddionalValueTypeFieldOffset);
            else
                return objParent;
        }

        static ObjectData GetManagedReferenceTarget(CachedSnapshot snapshot, ManagedConnection c, bool addManagedObjectsWithFieldInfo)
        {
            var objParent = c.IndexFrom.Id switch
            {
                SourceIndex.SourceId.ManagedType => ObjectData.FromManagedType(snapshot, c.FromManagedType),
                SourceIndex.SourceId.ManagedObject => ObjectData.FromManagedObjectIndex(snapshot, c.FromManagedObjectIndex),
                _ => throw new NotImplementedException()
            };

            var returnBareArrayElement = false;
            // The connection could be through a field on a value type in an array,
            // so get a potential array element first and from there the potential field in that element afterwards
            if (c.ArrayIndexFrom >= 0 && addManagedObjectsWithFieldInfo)
            {
                objParent = objParent.GetArrayElement(snapshot, c.ArrayIndexFrom, true);
                returnBareArrayElement = true;
            }

            if (c.FieldFrom >= 0 && addManagedObjectsWithFieldInfo)
                return objParent.GetFieldByFieldDescriptionsIndex(snapshot, c.FieldFrom, true,
                    valueTypeFieldOwningITypeDescription: c.ValueTypeFieldOwningITypeDescription,
                    valueTypeFieldIndex: c.ValueTypeFieldFrom,
                    addionalValueTypeFieldOffset: c.AddionalValueTypeFieldOffset);
            else if (returnBareArrayElement)
                return objParent;
            else
            {
                var target = c.IndexTo.Id switch
                {
                    SourceIndex.SourceId.ManagedObject => ObjectData.FromSourceLink(snapshot, c.IndexTo),
                    _ => throw new NotImplementedException()
                };
                return target;
            }
        }

        static void AddManagedReferencesTo(CachedSnapshot snapshot, SourceIndex objIndex,
            List<ObjectData> referencedObjects, HashSet<SourceIndex> foundUnityObjectIndices, bool addManagedObjectsWithFieldInfo)
        {
            if (!snapshot.CrawledData.ConnectionsFromMappedToSourceIndex.TryGetValue(objIndex, out var connectionIdxs))
                return;

            //add crawled connections
            foreach (var i in connectionIdxs)
            {
                var c = snapshot.CrawledData.Connections[i];

                if (c.IndexTo.Id == SourceIndex.SourceId.ManagedObject)
                {
                    switch (c.IndexFrom.Id)
                    {
                        case SourceIndex.SourceId.ManagedType:
                        case SourceIndex.SourceId.ManagedObject:
                            //referencedObjects.Add(ObjectData.FromSourceLink(snapshot, c.IndexTo));
                            referencedObjects.Add(GetManagedReferenceTarget(snapshot, c, addManagedObjectsWithFieldInfo));
                            break;
                        case SourceIndex.SourceId.NativeObject:
                            // Native Unity Engine Object to Managed Object connection based on m_CachedPtr.
                            // These type of connections go both ways but are only stored once and in Native -> Managed direction
                            if (!foundUnityObjectIndices.Contains(c.IndexTo))
                            {
                                foundUnityObjectIndices.Add(c.IndexTo);
                                referencedObjects.Add(ObjectData.FromSourceLink(snapshot, c.IndexTo));
                            }
                            break;
                        default:
                            // Not just not implemented but also technically dubious if something else where to have a connection to a Managed Object
                            throw new NotImplementedException();
                    }
                }
                else
                    throw new NotImplementedException();
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
            if (snapshot.Connections.ReferenceTo.TryGetValue(transformToSearchConnectionsFor.GetSourceLink(snapshot), out var list))
            {
                foreach (var connection in list)
                {
                    var possiblyConnectedTransform = ObjectData.FromSourceLink(snapshot, connection);
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
            if (snapshot.Connections.ReferenceTo.TryGetValue(transform.GetSourceLink(snapshot), out var list))
            {
                foreach (var connection in list)
                {
                    var objectData = ObjectData.FromSourceLink(snapshot, connection);
                    if (objectData.isNative && objectData.IsGameObject(snapshot) && snapshot.NativeObjects.ObjectName[transform.nativeObjectIndex] == snapshot.NativeObjects.ObjectName[ObjectData.FromSourceLink(snapshot, connection).nativeObjectIndex])
                        return snapshot.NativeObjects.InstanceId[objectData.nativeObjectIndex];
                }
            }
            return NativeObjectEntriesCache.InstanceIDNone;
        }

        public static ObjectData[] GenerateReferencesTo(CachedSnapshot snapshot, ObjectData obj, bool treatUnityObjectsAsOneObject = true, bool addManagedObjectsWithFieldInfo = false)
        {
            var objIndex = default(SourceIndex);
            switch (obj.dataType)
            {
                case ObjectDataType.Array:
                case ObjectDataType.BoxedValue:
                case ObjectDataType.Object:
                {
                    if (snapshot.CrawledData.MangedObjectIndexByAddress.TryGetValue(obj.hostManagedObjectPtr, out var idx))
                    {
                        objIndex = new SourceIndex(SourceIndex.SourceId.ManagedObject, idx);
                    }
                    break;
                }
                case ObjectDataType.NativeObject:
                {
                    objIndex = new SourceIndex(SourceIndex.SourceId.NativeObject, obj.nativeObjectIndex);
                    break;
                }
                case ObjectDataType.Type:
                {
                    objIndex = new SourceIndex(SourceIndex.SourceId.ManagedType, obj.managedTypeIndex);
                    break;
                }
            }
            return GenerateReferencesTo(snapshot, objIndex, treatUnityObjectsAsOneObject, addManagedObjectsWithFieldInfo);
        }

        public static ObjectData[] GenerateReferencesTo(CachedSnapshot snapshot, SourceIndex objIndex, bool treatUnityObjectsAsOneObject = true, bool addManagedObjectsWithFieldInfo = false)
        {
            if (!objIndex.Valid)
                return k_EmptyObjectDataArray;

            var referencedObjects = new List<ObjectData>();
            // this hashset helps avoid double reported connections for Unity Objects, either reported via the CachedSnapshots.Connections
            // or by the crawler. For the latter, connections are only ignored if they originate from the NativeObject <-> Managed Shell connection
            // If the Managed Shell has a reference to itself, it is still reported. Similarly if it has a reference to its native Object that is not held in m_CachedPtr.
            var foundUnityObjectIndices = new HashSet<SourceIndex>();
            switch (objIndex.Id)
            {
                case SourceIndex.SourceId.NativeObject:

                    // Don't list self references for Unity Objects
                    foundUnityObjectIndices.Add(objIndex);

                    var managedShell = new SourceIndex(SourceIndex.SourceId.ManagedObject, snapshot.NativeObjects.ManagedObjectIndex[objIndex.Index]);

                    if (managedShell.Index != -1)
                    {
                        if (treatUnityObjectsAsOneObject)
                        {
                            // the managed shell is treated as the same object, so no self references of this kind either
                            foundUnityObjectIndices.Add(managedShell);
                            AddManagedReferencesTo(snapshot, managedShell, referencedObjects, foundUnityObjectIndices, addManagedObjectsWithFieldInfo);
                        }
                        // else add managed shell as referenced to, but that is handled in AddManagedReferencesTo called on objIndex
                    }
                    break;
                case SourceIndex.SourceId.ManagedObject:
                    ref readonly var moi = ref snapshot.CrawledData.ManagedObjects[objIndex.Index];
                    if (moi.NativeObjectIndex >= 0)
                    {
                        // Don't list self references for Unity Objects
                        foundUnityObjectIndices.Add(objIndex);
                        var nativeObject = new SourceIndex(SourceIndex.SourceId.NativeObject, moi.NativeObjectIndex);
                        if (treatUnityObjectsAsOneObject)
                        {
                            foundUnityObjectIndices.Add(nativeObject);
                            AddManagedReferencesTo(snapshot, nativeObject, referencedObjects, foundUnityObjectIndices, addManagedObjectsWithFieldInfo);
                            AddReferencesToFromRawSnapshotData(snapshot, nativeObject, foundUnityObjectIndices, referencedObjects);
                        }
                        else
                        {
                            // Since the connection Native <-> Managed is only tracked the other way, add the connection here
                            // as AddManagedReferencesTo operating on objIndex won't find it
                            referencedObjects.Add(ObjectData.FromNativeObjectIndex(snapshot, moi.NativeObjectIndex));
                        }
                    }
                    break;
            }

            AddManagedReferencesTo(snapshot, objIndex, referencedObjects, foundUnityObjectIndices, addManagedObjectsWithFieldInfo);

            //add connections from the raw snapshot
            AddReferencesToFromRawSnapshotData(snapshot, objIndex, foundUnityObjectIndices, referencedObjects);

            return referencedObjects.ToArray();
        }

        static void AddReferencesToFromRawSnapshotData(CachedSnapshot snapshot, SourceIndex objIndex, HashSet<SourceIndex> foundUnityObjectIndices, List<ObjectData> referencedObjects)
        {
            if (objIndex.Index >= 0 && snapshot.Connections.ReferenceTo.ContainsKey(objIndex))
            {
                var cns = snapshot.Connections.ReferenceTo[objIndex];
                foreach (var i in cns)
                {
                    // Don't count Native <-> Managed Connections again if they have been added
                    if (!foundUnityObjectIndices.Contains(i))
                    {
                        foundUnityObjectIndices.Add(i);
                        referencedObjects.Add(ObjectData.FromSourceLink(snapshot, i));
                    }
                }
            }
        }

        public static ObjectData[] GetAllReferencedObjects(CachedSnapshot snapshot, ObjectData obj)
        {
            return GenerateReferencesTo(snapshot, obj, true, true);
        }
    }
}

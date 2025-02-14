using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;

namespace Unity.MemoryProfiler.Editor
{
    internal struct ObjectConnection
    {
        public enum UnityObjectReferencesSearchMode
        {
            /// <summary>
            /// Don't list the corresponding Managed Shell/Native object as a reference but list all references to both
            /// </summary>
            TreatAsOneObject,
            /// <summary>
            /// Search is aware of the corresponding Managed Shell/Native object and will actively ignore it and its references,
            /// reutrning only the results for the passed object
            /// </summary>
            IgnoreCorrespondingObjectAndItsReferences,
            /// <summary>
            /// Finds all references, including the corresponding Managed Shell/Native object as a separate object.
            /// Does not add the references to the corresponding object itself.
            /// </summary>
            Raw
        }

        /// <summary>
        /// Gets all objects or types referencing the object or allocation behind <paramref name="objIndex"/>.
        /// </summary>
        /// <param name="snapshot"></param>
        /// <param name="objIndex"></param>
        /// <param name="allReferencingObjects"></param>
        /// <param name="foundSourceIndices">All found objects as <seealso cref="SourceIndex"/>.</param>
        /// <param name="searchMode"></param>
        /// <param name="ignoreRepeatedManagedReferences">If this object is repeatedly referenced by the same other managed object, ignore it</param>
        public static void GetAllReferencingObjects(CachedSnapshot snapshot, SourceIndex objIndex, ref List<ObjectData> allReferencingObjects, HashSet<SourceIndex> foundSourceIndices = null,
            UnityObjectReferencesSearchMode searchMode = UnityObjectReferencesSearchMode.TreatAsOneObject, bool ignoreRepeatedManagedReferences = true)
        {
            allReferencingObjects.Clear();
            foundSourceIndices?.Clear();
            if (!objIndex.Valid)
                return;

            var foundUnityObjectIndices = foundSourceIndices ?? new HashSet<SourceIndex>();
            // Avoid finding self references
            foundUnityObjectIndices.Add(objIndex);
            var additionalReferenceToRemoveFromReport = default(SourceIndex);
            switch (objIndex.Id)
            {
                case SourceIndex.SourceId.None:
                    // ignore invalid indices
                    break;
                case SourceIndex.SourceId.NativeObject:
                    // if this is a Native object with a managed wrapper, get references to the wrapper too
                    var managedObjectIndex = snapshot.NativeObjects.ManagedObjectIndex[objIndex.Index];
                    if (managedObjectIndex >= ManagedData.FirstValidObjectIndex)
                    {
                        var managedShell = new SourceIndex(SourceIndex.SourceId.ManagedObject, managedObjectIndex);
                        if (searchMode != UnityObjectReferencesSearchMode.Raw)
                        {
                            // skip shell object in results list and don't find it as a reference
                            additionalReferenceToRemoveFromReport = managedShell;
                            foundUnityObjectIndices.Add(managedShell);

                            if (searchMode == UnityObjectReferencesSearchMode.TreatAsOneObject)
                                // find all references to the shell first
                                AddManagedReferences(snapshot, managedShell, ref allReferencingObjects, ref foundUnityObjectIndices, ignoreRepeatedManagedReferences);
                        }
                    }

                    // find all managed references to the native object afterwards
                    AddManagedReferences(snapshot, objIndex, ref allReferencingObjects, ref foundUnityObjectIndices, ignoreRepeatedManagedReferences);
                    break;
                case SourceIndex.SourceId.ManagedObject:
                    var managedObject = objIndex;
                    var nativeObjectIndex = snapshot.CrawledData.ManagedObjects[objIndex.Index].NativeObjectIndex;
                    var nativeObject = nativeObjectIndex >= CachedSnapshot.NativeObjectEntriesCache.FirstValidObjectIndex ?
                        new SourceIndex(SourceIndex.SourceId.NativeObject, nativeObjectIndex) : default;
                    if (nativeObject.Valid && (searchMode != UnityObjectReferencesSearchMode.Raw))
                    {
                        // In TreatAsOneObject mode we will find all managed and native references to the native object, but won't add them to the result
                        // In IgnoreCorrespondingObjectAndItsReferences mode we will ignore the managed (GCHandle -> Managed Object) reference
                        // In Raw mode, we will find find the managed (GCHandle -> Managed Object) reference and add it
                        foundUnityObjectIndices.Add(nativeObject);
                        switch (searchMode)
                        {
                            case UnityObjectReferencesSearchMode.TreatAsOneObject:
                                // The connections from the raw snapshot which are searched for objIndex only contain the
                                // native (Native Object -> GCHandle -> Managed Object) connection which will be ignored
                                // as it is already part of the HashSet. But we want to find all native references,
                                // so objIndex and nativeObject need to swap places
                                additionalReferenceToRemoveFromReport = objIndex;
                                objIndex = nativeObject;
                                break;
                            case UnityObjectReferencesSearchMode.IgnoreCorrespondingObjectAndItsReferences:
                                // remove the native object from the results afterwards
                                additionalReferenceToRemoveFromReport = nativeObject;
                                break;
                            case UnityObjectReferencesSearchMode.Raw:
                            default:
                                throw new NotImplementedException();
                        }
                    }

                    // find all references to the managed object (/shell) first
                    AddManagedReferences(snapshot, managedObject, ref allReferencingObjects, ref foundUnityObjectIndices, ignoreRepeatedManagedReferences);

                    if (nativeObject.Valid && searchMode == UnityObjectReferencesSearchMode.TreatAsOneObject)
                    {
                        // find all managed references to the native object afterwards
                        AddManagedReferences(snapshot, nativeObject, ref allReferencingObjects, ref foundUnityObjectIndices, ignoreRepeatedManagedReferences);
                    }
                    break;
                default:
                    AddManagedReferences(snapshot, objIndex, ref allReferencingObjects, ref foundUnityObjectIndices, ignoreRepeatedManagedReferences);
                    break;
            }

            // Add connections from the raw snapshot
            // ReferencedBy for native Objects finds all other native objects referencing that native object, but not the managed shell referencing the native object
            // ReferencedBy for managed shell objects finds the native object holding the GCHandle to the managed shell object
            if (objIndex.Valid && objIndex.Index >= 0 && snapshot.Connections.ReferencedBy.ContainsKey(objIndex))
            {
                foreach (var i in snapshot.Connections.ReferencedBy[objIndex])
                {
                    if (foundUnityObjectIndices.Add(i))
                    {
                        allReferencingObjects.Add(ObjectData.FromSourceLink(snapshot, i));
                    }
                }
            }

            if (foundSourceIndices != null)
            {
                // don't report the self reference
                foundSourceIndices.Remove(objIndex);
                if (additionalReferenceToRemoveFromReport.Valid)
                    foundSourceIndices.Remove(additionalReferenceToRemoveFromReport);
            }
        }

        static void AddManagedReferences(CachedSnapshot snapshot, SourceIndex objectIndex, ref List<ObjectData> results, ref HashSet<SourceIndex> foundUnityObjectIndices, bool ignoreRepeatedManagedReferences)
        {
            if (!snapshot.CrawledData.ConnectionsToMappedToSourceIndex.TryGetValue(objectIndex, out var connectionIndicies))
                return;

            // Add crawled connections
            foreach (var i in connectionIndicies)
            {
                var c = snapshot.CrawledData.Connections[i];
#if DEBUG_VALIDATION
                // Don't incur costs for validation logic in normal run
                switch (c.IndexTo.Id)
                {
                    case SourceIndex.SourceId.NativeAllocation:
                    case SourceIndex.SourceId.NativeObject:
                        switch (c.IndexFrom.Id)
                        {
                            case SourceIndex.SourceId.ManagedObject:
                            case SourceIndex.SourceId.ManagedType:
                            // Currently intentional possible connections:
                            // Managed Object / Managed Type -> Native Allocation / Native Object
                                break;
                            default:
                                throw new NotImplementedException();
                        }
                        break;
                    case SourceIndex.SourceId.ManagedObject:
                        switch (c.IndexFrom.Id)
                        {
                            case SourceIndex.SourceId.ManagedObject:
                            case SourceIndex.SourceId.ManagedType:
                            case SourceIndex.SourceId.NativeObject:
                            // Currently intentional possible connections:
                            // Managed Object / Managed Type / Native Object -> Managed Object
                                break;
                            default:
                                throw new NotImplementedException();
                        }
                        //if (c.IndexFrom.Id == SourceIndex.SourceId.NativeObject)
                        //    // Native Unity Engine Object to Managed Object connection based on m_CachedPtr.
                        //    // these get at added in the loop at the end of the calling GetAllReferencingObjects function.
                        //    // Tried using a hash set to prevent duplicates but the lookup during add locks up the window
                        //    // if there are more than about 50k references
                        //    continue;

                        break;
                    default:
                        throw new NotImplementedException();
                }
#endif
                if (c.FieldFrom.Equals(snapshot.TypeDescriptions.IFieldUnityObjectMCachedPtr))
                {
                    // Ignore connections from m_CachedPtr on the managed shell to the native object, if the shell is already in the list of found UnityObjectIndices
                    if (foundUnityObjectIndices.Contains(c.IndexFrom) && c.IndexFrom.Id == SourceIndex.SourceId.ManagedObject && c.IndexTo.Id == SourceIndex.SourceId.NativeObject)
                        continue;
                }
                // ignore multiplpe references from the same _managed_ object only if that option was chosen.
                if (foundUnityObjectIndices.Add(c.IndexFrom) || (c.IndexFrom.Id != SourceIndex.SourceId.NativeObject && !ignoreRepeatedManagedReferences))
                {
                    // the connected object was not listed yet
                    results.Add(GetManagedReferenceSource(snapshot, c));
                }
            }
        }

        public static ObjectData GetManagedReferenceSource(CachedSnapshot snapshot, ManagedConnection c)
        {
            var objParent = ObjectData.FromSourceLink(snapshot, c.IndexFrom);

            // The connection could be through a field on a value type in an array,
            // so get a potential array element first and from there the potential field in that element afterwards
            if (c.ArrayIndexFrom >= 0)
                objParent = objParent.GetArrayElement(snapshot, c.ArrayIndexFrom, false);

            if (c.FieldFrom >= 0)
                return objParent.GetFieldByFieldDescriptionsIndex(snapshot, c.FieldFrom, false,
                    valueTypeFieldOwningITypeDescription: c.ValueTypeFieldOwningITypeDescription,
                    valueTypeFieldIndex: c.ValueTypeFieldFrom,
                    offsetFromManagedObjectDataStartToValueTypesField: c.OffsetFromReferenceOwnerHeaderStartToFieldOnValueType);
            else
                return objParent;
        }

        static ObjectData GetManagedReferenceTarget(CachedSnapshot snapshot, ManagedConnection c, bool addManagedObjectsWithFieldInfo)
        {
#if DEBUG_VALIDATION
            var target = c.IndexTo;
            // Don't incur costs for validation logic in normal run
            var targetTypeValid = c.IndexTo.Id switch
            {
                SourceIndex.SourceId.ManagedObject => true,
                SourceIndex.SourceId.NativeAllocation => true,
                SourceIndex.SourceId.NativeObject => true,
                _ => false
            };
            if (!targetTypeValid)
                throw new NotImplementedException($"GetManagedReferenceTarget not implemented for target type {c.IndexTo.Id}.");
#endif
            var obj = ObjectData.FromSourceLink(snapshot, c.IndexTo);
            var holdingObjectOrField = ObjectData.FromSourceLink(snapshot, c.IndexFrom);

            var returnBareArrayElement = false;
            // The connection could be through a field on a value type in an array,
            // so get a potential array element first and from there the potential field in that element afterwards
            if (c.ArrayIndexFrom >= 0 && addManagedObjectsWithFieldInfo)
            {
                holdingObjectOrField = holdingObjectOrField.GetArrayElement(snapshot, c.ArrayIndexFrom, false);
                returnBareArrayElement = true;
            }

            if (c.FieldFrom >= 0 && addManagedObjectsWithFieldInfo)
            {
                holdingObjectOrField = holdingObjectOrField.GetFieldByFieldDescriptionsIndex(snapshot, c.FieldFrom, false,
                    valueTypeFieldOwningITypeDescription: c.ValueTypeFieldOwningITypeDescription,
                    valueTypeFieldIndex: c.ValueTypeFieldFrom,
                    offsetFromManagedObjectDataStartToValueTypesField: c.OffsetFromReferenceOwnerHeaderStartToFieldOnValueType);
                return obj.RegisterHoldingField(holdingObjectOrField, true);
            }
            else if (returnBareArrayElement)
            {
                return obj.RegisterHoldingField(holdingObjectOrField, true);
            }
            return obj;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="snapshot"></param>
        /// <param name="objIndex"></param>
        /// <param name="referencedObjects"></param>
        /// <param name="foundUnityObjectIndices"></param>
        /// <param name="addManagedObjectsWithFieldInfo"></param>
        /// <param name="managedShellToIgnoreAllDefaultUnityNativeManagedConnectionsFor">
        /// If <paramref name="objIndex"/> refers to a native object or managed shell where the other side gets its references parsed for it separatedly,
        /// references will be ignored, unless they are Managed -> Native references outside of m_CachedPtr. </param>
        /// <exception cref="NotImplementedException"></exception>
        static void AddManagedReferencesTo(CachedSnapshot snapshot, SourceIndex objIndex,
            List<ObjectData> referencedObjects, HashSet<SourceIndex> foundUnityObjectIndices, bool addManagedObjectsWithFieldInfo,
            // If the native object is parsed explicitly
            SourceIndex managedShellToIgnoreAllDefaultUnityNativeManagedConnectionsFor = default)
        {
            if (!snapshot.CrawledData.ConnectionsFromMappedToSourceIndex.TryGetValue(objIndex, out var connectionIdxs))
                return;

            //add crawled connections
            foreach (var i in connectionIdxs)
            {
                var c = snapshot.CrawledData.Connections[i];
                switch (c.IndexTo.Id)
                {
                    case SourceIndex.SourceId.ManagedObject:
                        switch (c.IndexFrom.Id)
                        {
                            case SourceIndex.SourceId.ManagedType:
                            case SourceIndex.SourceId.ManagedObject:
                                // Managed to Managed connections are always added and never ignored, even if they are already in foundUnityObjectIndices
                                referencedObjects.Add(GetManagedReferenceTarget(snapshot, c, addManagedObjectsWithFieldInfo));
                                foundUnityObjectIndices.Add(c.IndexTo);
                                break;
                            case SourceIndex.SourceId.NativeObject:
                                // Native Unity Engine Object to Managed Object connection based on m_CachedPtr.
                                // These type of connections go both ways but are only stored once and in Native -> Managed direction in CachedSnapshot.Connections.From -> To
                                // but bidirectionally in ManagedConnection.

                                // If this is a Native Object -> Managed Object reference of the managed shell that we want to ignore, irgnore it.
                                if (c.IndexTo == managedShellToIgnoreAllDefaultUnityNativeManagedConnectionsFor)
                                    break;
                                if (foundUnityObjectIndices.Add(c.IndexTo))
                                {
                                    referencedObjects.Add(ObjectData.FromSourceLink(snapshot, c.IndexTo));
                                }
                                break;
                            default:
                                // Not just not implemented but also technically dubious if something else where to have a connection to a Managed Object
                                throw new NotImplementedException();
                        }
                        break;
                    case SourceIndex.SourceId.NativeObject:
                    case SourceIndex.SourceId.NativeAllocation:
                        switch (c.IndexFrom.Id)
                        {
                            case SourceIndex.SourceId.ManagedType:
                            case SourceIndex.SourceId.ManagedObject:

                                // Native Unity Engine Object to Managed Object connection based on m_CachedPtr.
                                // These type of connections go both ways but are only stored once and in Native -> Managed direction in CachedSnapshot.Connections.From -> To
                                // but bidirectionally in ManagedConnection.

                                // If this is a Managed -> Native Object reference that came about from crawling the m_CachedPtr field and points of the managed shell that we want to ignore, irgnore it.
                                if (c.IndexTo.Id == SourceIndex.SourceId.NativeObject && c.FieldFrom == snapshot.TypeDescriptions.IFieldUnityObjectMCachedPtr
                                    && c.IndexFrom == managedShellToIgnoreAllDefaultUnityNativeManagedConnectionsFor)
                                    break;


                                if (foundUnityObjectIndices.Add(c.IndexTo))
                                {
                                    referencedObjects.Add(GetManagedReferenceTarget(snapshot, c, addManagedObjectsWithFieldInfo));
                                }
                                break;
                            default:
                                // Other types of connections to Native Objects and Native allocations are not implemented via ManagedConnection
                                throw new NotImplementedException();
                        }
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }
        }

        /// <summary>
        /// Tries to get all instance IDs of Transform Components connected to the passed in Transform Component's instance ID,
        /// except those that bridge connections between prefabs and instances (and Editor behavioural artefact unrelated to a GameObject's Transform Hierarchy).
        /// </summary>
        /// <param name="snapshot"></param>
        /// <param name="transformInstanceID">The instance ID of the Transform to check</param>
        /// <param name="parentTransformInstanceIdToIgnore">If you are only looking for child transforms, pass in the parent ID so it will be ignored. -1 if no parent should be ignored.</param>
        /// <param name="outInstanceIds">The connected instanceIDs if any where found, otherwise it is empty.</param>
        /// <returns>Returns True if connected Transform IDs were found, False if not.</returns>
        public static bool TryGetConnectedTransformInstanceIdsFromTransformInstanceId(CachedSnapshot snapshot, InstanceID transformInstanceID, InstanceID parentTransformInstanceIdToIgnore, ref HashSet<InstanceID> outInstanceIds)
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
                    if (possiblyConnectedTransform.isNativeObject && snapshot.NativeTypes.IsTransformOrRectTransform(snapshot.NativeObjects.NativeTypeArrayIndex[possiblyConnectedTransform.nativeObjectIndex])
                        && instanceIdOfPossibleConnection != NativeObjectEntriesCache.InstanceIDNone && instanceIdOfPossibleConnection != parentTransformInstanceIdToIgnore && instanceIdOfPossibleConnection != transformInstanceID
                        // Skip connections that jump from prefab to instance or vice versa.
                        // Those connections are Editor artefacts and not relevant to the Transform Hierarchy.
                        // The most reliable way to detect such connections is to check if the persistent flag status of the connected objects is the not same,
                        // i.e. the prefab would be persistent, the instance not.
                        && snapshot.NativeObjects.Flags[transformToSearchConnectionsFor.nativeObjectIndex].HasFlag(Format.ObjectFlags.IsPersistent)
                        == snapshot.NativeObjects.Flags[possiblyConnectedTransform.nativeObjectIndex].HasFlag(Format.ObjectFlags.IsPersistent))
                        found.Add(instanceIdOfPossibleConnection);
                }
                return found.Count > 0;
            }
            return false;
        }

        public static InstanceID GetGameObjectInstanceIdFromTransformInstanceId(CachedSnapshot snapshot, InstanceID instanceID)
        {
            var transform = ObjectData.FromNativeObjectIndex(snapshot, snapshot.NativeObjects.InstanceId2Index[instanceID]);
            if (snapshot.Connections.ReferenceTo.TryGetValue(transform.GetSourceLink(snapshot), out var list))
            {
                foreach (var connection in list)
                {
                    var objectData = ObjectData.FromSourceLink(snapshot, connection);
                    if (objectData.isNativeObject && objectData.IsGameObject(snapshot) && snapshot.NativeObjects.ObjectName[transform.nativeObjectIndex] == snapshot.NativeObjects.ObjectName[ObjectData.FromSourceLink(snapshot, connection).nativeObjectIndex])
                        return snapshot.NativeObjects.InstanceId[objectData.nativeObjectIndex];
                }
            }
            return NativeObjectEntriesCache.InstanceIDNone;
        }

        /// <summary>
        /// Builds up a list of all Objects that the Object (<paramref name="objIndex"/>) holds a reference to.
        /// </summary>
        /// <param name="snapshot"></param>
        /// <param name="objIndex">The object for which to find what it is referencing.</param>
        /// <param name="allReferencedObjects">The output list. Will be cleared by this method.</param>
        /// <param name="treatUnityObjectsAsOneObject">
        /// For References To display purposes this should be true.
        /// For Managed Object inspector purposes (listing native references for native references) this should be false.</param>
        /// <param name="addManagedObjectsWithFieldInfo"></param>
        /// <param name="foundSourceIndices">All found objects as <seealso cref="SourceIndex"/>. The Hashset will be cleared by this method.</param>
        public static void GenerateReferencesTo(CachedSnapshot snapshot, SourceIndex objIndex, ref List<ObjectData> allReferencedObjects, bool treatUnityObjectsAsOneObject = true, bool addManagedObjectsWithFieldInfo = true, HashSet<SourceIndex> foundSourceIndices = null)
        {
            allReferencedObjects.Clear();
            foundSourceIndices?.Clear();
            if (!objIndex.Valid)
                return;

            // this hashset helps avoid double reported connections for Unity Objects, either reported via the CachedSnapshots.Connections
            // or by the crawler. For the latter, connections are only ignored if they originate from the NativeObject <-> Managed Shell connection
            // If the Managed Shell has a reference to itself, it is still reported. Similarly if it has a reference to its native Object that is not held in m_CachedPtr.
            var foundUnityObjectIndices = foundSourceIndices ?? new HashSet<SourceIndex>();
            var managedShell = default(SourceIndex);

            var additionalReferenceToRemoveFromReport = default(SourceIndex);
            if (treatUnityObjectsAsOneObject)
            {
                switch (objIndex.Id)
                {
                    case SourceIndex.SourceId.NativeObject:
                        var managedObjectIndex = snapshot.NativeObjects.ManagedObjectIndex[objIndex.Index];

                        if (managedObjectIndex >= 0)
                        {
                            managedShell = new SourceIndex(SourceIndex.SourceId.ManagedObject, managedObjectIndex);
                            additionalReferenceToRemoveFromReport = managedShell;
                            // the managed shell is treated as the same object, so no self references of this kind either
                            foundUnityObjectIndices.Add(managedShell);
                        }
                        break;
                    case SourceIndex.SourceId.ManagedObject:
                        ref readonly var moi = ref snapshot.CrawledData.ManagedObjects[objIndex.Index];
                        if (moi.NativeObjectIndex >= 0)
                        {
                            // So that the order of the references is the same, whether or not this is a request for the managed shell or the native objects references,
                            // follow the same order as above by flipping objIndex to be the native object instead of the managed shell
                            managedShell = objIndex;
                            var nativeObject = new SourceIndex(SourceIndex.SourceId.NativeObject, moi.NativeObjectIndex);
                            objIndex = nativeObject;

                            additionalReferenceToRemoveFromReport = managedShell;
                        }
                        break;
                }
            }
            // Avoid finding self references
            foundUnityObjectIndices.Add(objIndex);

            if (managedShell.Valid)
            {
                // If there is a managed Unity Object to consider, process that first
                AddManagedReferencesTo(snapshot, managedShell, allReferencedObjects, foundUnityObjectIndices, addManagedObjectsWithFieldInfo,
                    managedShellToIgnoreAllDefaultUnityNativeManagedConnectionsFor: managedShell);
                // Ignore Managed Shell for the native object part of the search
                foundUnityObjectIndices.Add(managedShell);
            }

            AddManagedReferencesTo(snapshot, objIndex, allReferencedObjects, foundUnityObjectIndices, addManagedObjectsWithFieldInfo,
                managedShellToIgnoreAllDefaultUnityNativeManagedConnectionsFor: managedShell);

            //add connections from the raw snapshot
            AddReferencesToFromRawSnapshotData(snapshot, objIndex, foundUnityObjectIndices, allReferencedObjects);

            if (foundSourceIndices != null)
            {
                // don't report the self reference
                foundSourceIndices.Remove(objIndex);
                if (additionalReferenceToRemoveFromReport.Valid)
                    foundSourceIndices.Remove(additionalReferenceToRemoveFromReport);
            }
        }

        static void AddReferencesToFromRawSnapshotData(CachedSnapshot snapshot, SourceIndex objIndex, HashSet<SourceIndex> foundUnityObjectIndices, List<ObjectData> referencedObjects)
        {
            if (objIndex.Valid && objIndex.Index >= 0 && snapshot.Connections.ReferenceTo.ContainsKey(objIndex))
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

        public static void GetAllReferencedObjects(CachedSnapshot snapshot, SourceIndex item, ref List<ObjectData> allReferencedObjects, bool treatUnityObjectsAsOneObject = true, bool addManagedObjectsWithFieldInfo = true, HashSet<SourceIndex> foundSourceIndices = null)
        {
            GenerateReferencesTo(snapshot, item, ref allReferencedObjects, treatUnityObjectsAsOneObject, addManagedObjectsWithFieldInfo, foundSourceIndices);
        }
    }
}

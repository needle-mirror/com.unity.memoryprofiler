using UnityEditor;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.Collections.LowLevel.Unsafe;
using System;
using Unity.MemoryProfiler.Editor.Diagnostics;


#if !ENTITY_ID_CHANGED_SIZE
// the official EntityId lives in the UnityEngine namespace, which might be be added as a using via the IDE,
// so to avoid mistakenly using a version of this struct with the wrong size, alias it here.
using EntityId = Unity.MemoryProfiler.Editor.EntityId;
#else
using EntityId = UnityEngine.EntityId;
// This should be greyed out by the IDE, otherwise you're missing an alias above
using UnityEngine;
#endif

namespace Unity.MemoryProfiler.Editor
{
    // EntityId and EntityAndInstanceIdHelper to be moved from CachedSnapshot.cs to here
#if !ENTITY_ID_CHANGED_SIZE
    // For simple API compatibility usage in versions pre InstanceId change
    struct EntityId : IEquatable<EntityId>, IComparable<EntityId>
    {
        ulong m_Id;

        public static EntityId None => new EntityId { m_Id = 0 };
        public static EntityId From(ulong id) => new EntityId { m_Id = id };
        /// <summary>
        /// This assumption might not hold for an 8byte EntityID future,
        /// and the reported Persistent flag should be relied on instead.
        ///
        /// The actual (not MemoryProfiler package based) EntityId.IsRuntimeCreated() API
        /// is then however only valid during execution of the actually relevant runtime, not for EntityIds loaded out of a snapshot.
        /// </summary>
        /// <returns></returns>
        internal bool IsRuntimeCreated() => this.ConvertToIdInt() < 0;
        public override bool Equals(object obj) => obj is EntityId id && Equals(id);
        public bool Equals(EntityId other) => m_Id == other.m_Id;
        public override int GetHashCode() => m_Id.GetHashCode();
        public int CompareTo(EntityId other) => m_Id.CompareTo(other.m_Id);
        public static bool operator ==(EntityId a, EntityId b) => a.Equals(b);
        public static bool operator !=(EntityId a, EntityId b) => !a.Equals(b);
        public static explicit operator ulong(EntityId instanceID) => instanceID.m_Id;
        public override string ToString()
        {
            if (m_Id > uint.MaxValue)
                return m_Id.ToString();
            return ((int)m_Id).ToString();
        }
    }
#endif

    static class EntityAndInstanceIdHelper
    {
#if ENTITY_ID_STRUCT_AVAILABLE && !ENTITY_ID_CHANGED_SIZE
        static EntityAndInstanceIdHelper()
        {
            Checks.IsTrue((typeof(EntityId) != typeof(UnityEngine.EntityId)), "The wrong type of EntityId struct is used, probably due to accidentally addin a 'using UnityEngine;' to this file.");
        }
#endif

#if ENTITY_ID_STRUCT_AVAILABLE
        public static bool IsRuntimeCreated(this UnityEngine.EntityId id) => ConvertToIdInt(ConvertFromUnityEntityId(id)) < 0;
#endif

        public static void ConvertInstanceIdIntsToEntityIds(this DynamicArray<int> intInstanceIds, ref DynamicArray<EntityId> instanceIds)
        {
            if (intInstanceIds.Count == 0)
            {
                return;
            }
            if (intInstanceIds.Count != instanceIds.Count)
            {
                throw new InvalidOperationException("The count of the two arrays must be the same");
            }
            // We are reading old snapshot data here so its not as though those instance IDs mean anything
            // beyond their pure values relative to other usages of the same valus within the snapshot.
            // I.e. they are fake either way but as long as their value is consistently transposed to EntityId for everything in the snapshot,
            // everything still works as expected.
            // So while we're just converting to bogus values to make sure all relevant lookups still work, we might as well do it the fast way

            // The fast path
            unsafe
            {
                // Safe conversion as old data naturally enforce the limit of int.MaxValue for these
                var elementCount = (int)intInstanceIds.Count;
                UnsafeUtility.MemCpyStride(instanceIds.GetUnsafePtr(), sizeof(EntityId), intInstanceIds.GetUnsafePtr(), sizeof(int), sizeof(int), elementCount);
            }

            //// The slow path. Break comment block in case of failure of the above.
            //for (int i = 0; i < intInstanceIds.Count; i++)
            //{
            //    instanceIds[i] = EntityId.From((ulong)intInstanceIds[i]);
            //}
        }

        public static EntityId GetEntityId(this UnityEngine.Object obj)
        {
            if (obj != null)
#if ENTITY_ID_STRUCT_AVAILABLE
                return Convert(obj.GetEntityId());
#else
                return Convert(obj.GetInstanceID());
#endif
            else return EntityId.None;
        }

#if ENTITY_ID_CHANGED_SIZE
        // EntityId is UnityEngine.EntityId
        public static EntityId Convert(EntityId id) => id;
        public static EntityId ConvertToUnityEntityId(EntityId id) => id;
        public static EntityId ConvertFromUnityEntityId(EntityId id) => id;

        public static int ConvertToIdInt(this EntityId id)
        {
            int instanceId = 0;
            unsafe
            {
                // The ID bytes are the first 4 bytes.
                UnsafeUtility.MemCpyStride(&instanceId, sizeof(int), ((byte*)&id), sizeof(EntityId), sizeof(EntityId), 1);
            }
            return instanceId;
        }
#else
        public static int ConvertToIdInt(this EntityId id)
        {
            int instanceId = 0;
            unsafe
            {
                UnsafeUtility.MemCpyStride(&instanceId, sizeof(int), &id, sizeof(EntityId), sizeof(EntityId), 1);
            }
            return instanceId;
        }
#if ENTITY_ID_STRUCT_AVAILABLE
        public unsafe static UnityEngine.EntityId ConvertToUnityEntityId(EntityId id)
        {
            var eId = stackalloc UnityEngine.EntityId[1];
            var eIdInt = (int*)eId;
            eIdInt[0] = id.ConvertToIdInt();
            return eId[0];
        }
        public unsafe static EntityId ConvertFromUnityEntityId(UnityEngine.EntityId id)
        {
            var eId = stackalloc UnityEngine.EntityId[1];
            UnsafeUtility.CopyStructureToPtr(ref id, eId);
            var eIdInt = (int*)eId;
            return ConvertInt(eIdInt[0]);
        }
        public static EntityId Convert(UnityEngine.EntityId id) => ConvertFromUnityEntityId(id);
#endif
        public static EntityId Convert(int id) => ConvertInt(id);
#endif
        /// <summary>
        /// Call <see cref="Convert"/> if <paramref name="id"/> would be an <see cref="EntityId"/>
        /// depending on the define of ENTITY_ID_CHANGED_SIZE being true or false.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public static EntityId ConvertInt(int id)
        {
            ulong instanceId = 0;
            unsafe
            {
                UnsafeUtility.MemCpyStride(&instanceId, sizeof(EntityId), &id, sizeof(int), sizeof(int), 1);
            }
            return EntityId.From(instanceId);
        }

        public static bool TryConvertToEntityID(this string instanceIdStr, out EntityId instanceId)
        {
            if (!string.IsNullOrEmpty(instanceIdStr))
            {
                if (int.TryParse(instanceIdStr, out var instanceID))
                {
                    instanceId = ConvertInt(instanceID);
                    return true;
                }
                if (long.TryParse(instanceIdStr, out var instanceIDLong))
                {
                    instanceId = EntityId.From((ulong)instanceIDLong);
                    return true;
                }
                if (ulong.TryParse(instanceIdStr, out var instanceIDULong))
                {
                    instanceId = EntityId.From(instanceIDULong);
                    return true;
                }
            }
            instanceId = EntityId.From(0UL);
            return false;
        }
    }

    internal static class EditorSelectionUtility
    {
        public static EntityId GetActiveSelection()
        {
            return EntityAndInstanceIdHelper.Convert(
#if SELECTION_USES_ENTITY_ID
                Selection.activeEntityId
#else
                Selection.activeInstanceID
#endif
                );
        }

        public static void SetActiveSelection(EntityId selectionToSetAsActive)
        {
#if SELECTION_USES_ENTITY_ID
            Selection.activeEntityId = EntityAndInstanceIdHelper.ConvertToUnityEntityId(selectionToSetAsActive);
#else
            Selection.activeInstanceID = EntityAndInstanceIdHelper.ConvertToIdInt(selectionToSetAsActive);
#endif
        }

        public static DynamicArray<EntityId> GetSelectedObjects(Allocator allocator)
        {
#if SELECTION_USES_ENTITY_ID
            var selection = Selection.entityIds;
#else
            var selection = Selection.instanceIDs;
#endif
            var output = new DynamicArray<EntityId>(selection.Length, allocator, memClear: true);
            for (int i = 0; i < selection.Length; i++)
            {
                output[i] = EntityAndInstanceIdHelper.Convert(selection[i]);
            }
            return output;
        }

        public static void SetSelectedObjects(DynamicArray<EntityId> objectsToSetAsSelection)
        {
#if SELECTION_USES_ENTITY_ID
            var selection = new UnityEngine.EntityId[objectsToSetAsSelection.Count];
#else
            var selection = new int[objectsToSetAsSelection.Count];
#endif
            for (int i = 0; i < objectsToSetAsSelection.Count; i++)
            {
#if !SELECTION_USES_ENTITY_ID && !ENTITY_ID_CHANGED_SIZE
                selection[i] = EntityAndInstanceIdHelper.ConvertToIdInt(objectsToSetAsSelection[i]);
#elif ENTITY_ID_CHANGED_SIZE
                selection[i] = objectsToSetAsSelection[i];
#else
                selection[i] = EntityAndInstanceIdHelper.ConvertToUnityEntityId(objectsToSetAsSelection[i]);
#endif
            }

#if SELECTION_USES_ENTITY_ID
            Selection.entityIds = selection;
#else
            Selection.instanceIDs = selection;
#endif
        }

        public static void ClearSelection()
        {
            Selection.objects = new UnityEngine.Object[0];
        }
    }
}

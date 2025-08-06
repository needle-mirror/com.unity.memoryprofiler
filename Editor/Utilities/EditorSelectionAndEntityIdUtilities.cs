using UnityEditor;
using Unity.Collections;

using Unity.MemoryProfiler.Editor.Containers;

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
            Selection.activeInstanceID = EntityAndInstanceIdHelper.ConvertToInt(selectionToSetAsActive);
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
                selection[i] = EntityAndInstanceIdHelper.ConvertToInt(objectsToSetAsSelection[i]);
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

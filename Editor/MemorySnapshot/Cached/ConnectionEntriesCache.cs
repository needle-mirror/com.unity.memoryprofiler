using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Diagnostics;
using Unity.MemoryProfiler.Editor.Extensions;
using Unity.MemoryProfiler.Editor.Format;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;

using Debug = UnityEngine.Debug;

#if !ENTITY_ID_CHANGED_SIZE
// the official EntityId lives in the UnityEngine namespace, which might be be added as a using via the IDE,
// so to avoid mistakenly using a version of this struct with the wrong size, alias it here.
using EntityId = Unity.MemoryProfiler.Editor.EntityId;
#else
// This should be greyed out by the IDE, otherwise you're missing an alias above
using UnityEngine;
using EntityId = UnityEngine.EntityId;
#endif

namespace Unity.MemoryProfiler.Editor
{
    internal partial class CachedSnapshot
    {
        public class ConnectionEntriesCache : IDisposable
        {
            public long Count;

            // From/To with the same index forms a pair "from->to"
            public DynamicArray<int> From { private set; get; }
            public DynamicArray<int> To { private set; get; }

            // TODO: Use Native Hashmaps of Native Lists for these to optimze out GC Allocs
            // List of objects referencing an object with the specfic key
            public Dictionary<SourceIndex, List<SourceIndex>> ReferencedBy { get; private set; } = new Dictionary<SourceIndex, List<SourceIndex>>();

            /// <summary>
            /// List of objects an object with the specific key is refereing to.
            /// For GameObjects, their first reference is that to its Transform component, followed by all other components.
            /// For Transforms its their host GameObject, followed by all child transforms, with the final addition of their parent transform, IF IT HAS ONE! (i.e. Root transforms only report child Transforms).
            /// For Components, its their host GameObject, followed by all other references.
            /// </summary>
            public Dictionary<SourceIndex, List<SourceIndex>> ReferenceTo { get; private set; } = new Dictionary<SourceIndex, List<SourceIndex>>();

#if DEBUG_VALIDATION // could be always present but currently only used for validation in the crawler
            public long IndexOfFirstNativeToGCHandleConnection = -1;
#endif

#if ENTITY_ID_STRUCT_AVAILABLE && !ENTITY_ID_CHANGED_SIZE
            static ConnectionEntriesCache()
            {
                Checks.IsTrue((typeof(EntityId) != typeof(UnityEngine.EntityId)), "The wrong type of EntityId struct is used, probably due to accidentally addin a 'using UnityEngine;' to this file.");
            }
#endif

            unsafe public ConnectionEntriesCache(ref IFileReader reader, NativeObjectEntriesCache nativeObjects, long gcHandlesCount, bool connectionsNeedRemaping)
            {
                Count = reader.GetEntryCount(EntryType.Connections_From);

                // Set allocator to `temp` if we're going to discard the data later
                Allocator allocator = Allocator.Persistent;
                if (Count > 0 && connectionsNeedRemaping)
                    allocator = Allocator.Temp;

                From = new DynamicArray<int>(Count, allocator);
                To = new DynamicArray<int>(Count, allocator);

                if (Count == 0)
                    return;

                DynamicArray<EntityId> instanceIDFrom;
                DynamicArray<EntityId> instanceIDTo;
                if (reader.FormatVersion < FormatVersion.EntityIDAs8ByteStructs)
                {
                    From = reader.Read(EntryType.Connections_From, 0, Count, allocator).Result.Reinterpret<int>();
                    To = reader.Read(EntryType.Connections_To, 0, Count, allocator).Result.Reinterpret<int>();
                    // Clear the memory on alloc. The MemCpyStride in ConvertInstanceId won't initialize the blank spaces
                    instanceIDFrom = new DynamicArray<EntityId>(Count, Allocator.Temp, memClear: true);
                    instanceIDTo = new DynamicArray<EntityId>(Count, Allocator.Temp, memClear: true);
                    From.ConvertInstanceIdIntsToEntityIds(ref instanceIDFrom);
                    To.ConvertInstanceIdIntsToEntityIds(ref instanceIDTo);
                }
                else
                {
                    instanceIDFrom = reader.Read(EntryType.Connections_From, 0, Count, allocator).Result.Reinterpret<EntityId>();
                    instanceIDTo = reader.Read(EntryType.Connections_To, 0, Count, allocator).Result.Reinterpret<EntityId>();
                }

                if (connectionsNeedRemaping)
                    RemapInstanceIdsToUnifiedIndex(nativeObjects, gcHandlesCount, instanceIDFrom, instanceIDTo);

                instanceIDFrom.Dispose();
                instanceIDTo.Dispose();

                for (int i = 0; i < Count; i++)
                {
                    var to = ToSourceIndex(To[i], gcHandlesCount);
                    var from = ToSourceIndex(From[i], gcHandlesCount);

                    ReferencedBy.GetAndAddToListOrCreateList(to, from);

                    ReferenceTo.GetAndAddToListOrCreateList(from, to);
                }
            }

            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            SourceIndex ToSourceIndex(int index, long gcHandlesCount)
            {
                if (index < gcHandlesCount)
                    return new SourceIndex(SourceIndex.SourceId.ManagedObject, index);

                return new SourceIndex(SourceIndex.SourceId.NativeObject, index - gcHandlesCount);
            }

            void RemapInstanceIdsToUnifiedIndex(NativeObjectEntriesCache nativeObjects, long gcHandlesCount,
                DynamicArray<EntityId> instanceIDFrom, DynamicArray<EntityId> instanceIDTo)
            {
                var instanceIds = nativeObjects.InstanceId;
                var gcHandlesIndices = nativeObjects.ManagedObjectIndex;

                // Create two temporary acceleration structures:
                // - Native object EntityId to GC object
                // - Native object EntityId to Unified Index
                //
                // Unified Index - [0..gcHandlesCount)[0..nativeObjects.Count]
                var instanceIDToUnifiedIndex = new Dictionary<EntityId, int>();
                var instanceIDToGcHandleIndex = new Dictionary<EntityId, int>();
                for (int i = 0; i < instanceIds.Count; ++i)
                {
                    if (gcHandlesIndices[i] != -1)
                    {
                        instanceIDToGcHandleIndex.Add(instanceIds[i], gcHandlesIndices[i]);
                    }
                    instanceIDToUnifiedIndex.Add(instanceIds[i], (int)gcHandlesCount + i);
                }

#if DEBUG_VALIDATION
                if (instanceIDToGcHandleIndex.Count > 0)
                    IndexOfFirstNativeToGCHandleConnection = Count;
#endif

                // Connections - reported Native objects connections
                // Plus links between Native and Managed objects (instanceIDToGcHandleIndex)
                var newFrom = new DynamicArray<int>(Count + instanceIDToGcHandleIndex.Count, Allocator.Persistent);
                var newTo = new DynamicArray<int>(newFrom.Count, Allocator.Persistent);
                // Add all Native to Native connections reported in snapshot as Unified Index
                for (long i = 0; i < Count; ++i)
                {
                    newFrom[i] = instanceIDToUnifiedIndex[instanceIDFrom[i]];
                    newTo[i] = instanceIDToUnifiedIndex[instanceIDTo[i]];
                }

                // Dispose of original data to save memory
                // as we no longer need it
                To.Dispose();
                From.Dispose();

                // Add all Managed to Native connections
                var enumerator = instanceIDToGcHandleIndex.GetEnumerator();
                for (long i = Count; i < newFrom.Count; ++i)
                {
                    enumerator.MoveNext();
                    newFrom[i] = instanceIDToUnifiedIndex[enumerator.Current.Key];
                    // elements in To that are `To[i] < gcHandlesCount` are indexes into the GCHandles list
                    newTo[i] = enumerator.Current.Value;
                }

                From = newFrom;
                To = newTo;
                Count = From.Count;
            }

            public void Dispose()
            {
                Count = 0;
                From.Dispose();
                To.Dispose();
                // Setting to default isn't necessary, but can avoid confusion when memory profiling the memory profiler
                // without this, Disposing works but leaves the properties backing field with an invalid DynamicArray
                // that looks like it isundisposed
                From = default;
                To = default;

                ReferencedBy = null;
                ReferenceTo = null;
            }
        }
    }
}

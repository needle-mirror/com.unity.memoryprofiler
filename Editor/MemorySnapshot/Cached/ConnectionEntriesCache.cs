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
            public DynamicArray<SourceIndex> From { private set; get; }
            public DynamicArray<SourceIndex> To { private set; get; }

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
                Checks.IsTrue((typeof(EntityId) != typeof(UnityEngine.EntityId)), "The wrong type of EntityId struct is used, probably due to accidentally adding a 'using UnityEngine;' to this file.");
            }
#endif

            unsafe public ConnectionEntriesCache(ref IFileReader reader, NativeObjectEntriesCache nativeObjects, long gcHandlesCount)
            {
                Count = reader.GetEntryCount(EntryType.Connections_From);

                if (Count == 0)
                {
                    From = new DynamicArray<SourceIndex>(Count, Allocator.Persistent);
                    To = new DynamicArray<SourceIndex>(Count, Allocator.Persistent);
                    return;
                }

                DynamicArray<EntityId> instanceIDFrom;
                DynamicArray<EntityId> instanceIDTo;
                if (reader.FormatVersion < FormatVersion.EntityIDAs8ByteStructs)
                {
                    using var oldFrom = reader.Read(EntryType.Connections_From, 0, Count, Allocator.Temp).Result.Reinterpret<int>();
                    using var oldTo = reader.Read(EntryType.Connections_To, 0, Count, Allocator.Temp).Result.Reinterpret<int>();
                    // Clear the memory on alloc. The MemCpyStride in ConvertInstanceId won't initialize the blank spaces
                    instanceIDFrom = new DynamicArray<EntityId>(Count, Allocator.Temp, memClear: true);
                    instanceIDTo = new DynamicArray<EntityId>(Count, Allocator.Temp, memClear: true);
                    oldFrom.ConvertInstanceIdIntsToEntityIds(ref instanceIDFrom);
                    oldTo.ConvertInstanceIdIntsToEntityIds(ref instanceIDTo);
                    if (reader.FormatVersion < FormatVersion.NativeConnectionsAsInstanceIdsVersion)
                    {
                        From = new DynamicArray<SourceIndex>(Count, Allocator.Persistent);
                        To = new DynamicArray<SourceIndex>(Count, Allocator.Persistent);
                        for (long i = 0; i < Count; i++)
                        {
                            From[i] = OldUnifiedIndexToSourceIndex(oldFrom[i], gcHandlesCount);
                            To[i] = OldUnifiedIndexToSourceIndex(oldTo[i], gcHandlesCount);
                        }
                    }
                }
                else
                {
                    instanceIDFrom = reader.Read(EntryType.Connections_From, 0, Count, Allocator.Temp).Result.Reinterpret<EntityId>();
                    instanceIDTo = reader.Read(EntryType.Connections_To, 0, Count, Allocator.Temp).Result.Reinterpret<EntityId>();
                }

                if (reader.FormatVersion >= FormatVersion.NativeConnectionsAsInstanceIdsVersion)
                    RemapInstanceIdsToSourceIndices(nativeObjects, gcHandlesCount, instanceIDFrom, instanceIDTo);

                instanceIDFrom.Dispose();
                instanceIDTo.Dispose();

                for (long i = 0; i < Count; i++)
                {
                    var to = To[i];
                    var from = From[i];

                    ReferencedBy.GetAndAddToListOrCreateList(to, from);

                    ReferenceTo.GetAndAddToListOrCreateList(from, to);
                }
            }

            [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
            SourceIndex OldUnifiedIndexToSourceIndex(int index, long gcHandlesCount)
            {
                if (index < gcHandlesCount)
                    return new SourceIndex(SourceIndex.SourceId.ManagedObject, index);

                return new SourceIndex(SourceIndex.SourceId.NativeObject, index - gcHandlesCount);
            }

            void RemapInstanceIdsToSourceIndices(NativeObjectEntriesCache nativeObjects, long gcHandlesCount,
                DynamicArray<EntityId> instanceIDFrom, DynamicArray<EntityId> instanceIDTo)
            {
                var instanceIds = nativeObjects.InstanceId;
                var managedObjectIndex = nativeObjects.ManagedObjectIndex;

                // Create two temporary acceleration structures:
                // - Native object EntityId to Managed Object
                // - Native object EntityId to Native Object
                var instanceIDToNativeObject = new Dictionary<EntityId, SourceIndex>();
                var instanceIDToManagedObject = new Dictionary<EntityId, SourceIndex>();
                for (int i = 0; i < instanceIds.Count; ++i)
                {
                    if (managedObjectIndex[i] != -1)
                    {
                        instanceIDToManagedObject.Add(instanceIds[i], new SourceIndex(SourceIndex.SourceId.ManagedObject, managedObjectIndex[i]));
                    }
                    instanceIDToNativeObject.Add(instanceIds[i], new SourceIndex(SourceIndex.SourceId.NativeObject, i));
                }

#if DEBUG_VALIDATION
                if (instanceIDToManagedObject.Count > 0)
                    IndexOfFirstNativeToGCHandleConnection = Count;
#endif
                if (To.IsCreated)
                    To.Dispose();
                if (From.IsCreated)
                    From.Dispose();

                // Connections - reported Native objects connections
                // Plus links between Native and Managed objects (instanceIDToManagedObject)
                From = new DynamicArray<SourceIndex>(Count + instanceIDToManagedObject.Count, Allocator.Persistent);
                To = new DynamicArray<SourceIndex>(From.Count, Allocator.Persistent);
                // Add all Native to Native connections reported in snapshot as Source Index
                for (long i = 0; i < Count; ++i)
                {
                    From[i] = instanceIDToNativeObject[instanceIDFrom[i]];
                    To[i] = instanceIDToNativeObject[instanceIDTo[i]];
                }

                // Add all Managed to Native connections
                var enumerator = instanceIDToManagedObject.GetEnumerator();
                for (long i = Count; i < From.Count; ++i)
                {
                    enumerator.MoveNext();
                    From[i] = instanceIDToNativeObject[enumerator.Current.Key];
                    To[i] = enumerator.Current.Value;
                }
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

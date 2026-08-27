using System;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;

namespace Unity.MemoryProfiler.Editor
{
    internal partial class CachedSnapshot
    {
        /// <summary>
        /// Managed root kind, mirroring the native <c>IScriptingGCHeapCallbacks::RootType</c>
        /// (Modules/Scripting/Include/Scripting/ScriptingBackend/ScriptingProfilerApi.h). Encoded in the
        /// low 16 bits of <c>ManagedRoots_RootTypeAndFlags</c>.
        /// </summary>
        public enum ManagedRootType : ushort
        {
            Other = 0,
            Stack = 1,
            Finalizer = 2,
            Handle = 3,
        }

        /// <summary>
        /// Managed root flags, mirroring the native <c>IScriptingGCHeapCallbacks::RootFlags</c>. Bitfield
        /// encoded in the high 16 bits of <c>ManagedRoots_RootTypeAndFlags</c>.
        /// </summary>
        [Flags]
        public enum ManagedRootFlags : ushort
        {
            None = 0,
            Pinning = 0x1,
            WeakRef = 0x2,
            Interior = 0x4,
            RefCounted = 0x8,
        }

        /// <summary>
        /// The backend-provided managed roots table (format v19,
        /// <see cref="Format.FormatVersion.ManagedConnectionsVersion"/>). CoreCLR reports handle, stack,
        /// finalizer, and static (Other) roots; Mono / IL2CPP report handle roots only. CoreCLR static-held
        /// objects currently arrive as <see cref="ManagedRootType.Other"/> without per-field attribution, so
        /// consumers show them generically.
        /// </summary>
        public class ManagedRootsEntriesCache : IDisposable
        {
            public long Count;

            /// <summary>
            /// Row index into <see cref="ManagedObjectsEntriesCache"/> the root points at, or
            /// <see cref="PackedIndexReader.InvalidIndex"/> (-1) when the target object was not captured.
            /// </summary>
            public DynamicArray<long> ManagedObjectIndex = default;

            /// <summary>Decoded root kind per row (low 16 bits of the raw type-and-flags column).</summary>
            public DynamicArray<ManagedRootType> RootType = default;

            /// <summary>Decoded root flags per row (high 16 bits of the raw type-and-flags column).</summary>
            public DynamicArray<ManagedRootFlags> RootFlags = default;

            /// <summary>Raw backend handle value per root (e.g. the GCHandle for handle roots); 0 otherwise.</summary>
            public DynamicArray<ulong> Data = default;

            public ManagedRootsEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.ManagedRoots_RootTypeAndFlags);

                if (Count == 0)
                    return;

                ManagedObjectIndex = PackedIndexReader.Read(ref reader, EntryType.ManagedRoots_ManagedObjectIndex, Count, Allocator.Persistent);
                Data = reader.Read(EntryType.ManagedRoots_Data, 0, Count, Allocator.Persistent).Result.Reinterpret<ulong>();

                RootType = new DynamicArray<ManagedRootType>(Count, Allocator.Persistent);
                RootFlags = new DynamicArray<ManagedRootFlags>(Count, Allocator.Persistent);
                using var typeAndFlags = reader.Read(EntryType.ManagedRoots_RootTypeAndFlags, 0, Count, Allocator.Temp).Result.Reinterpret<uint>();
                for (long i = 0; i < Count; i++)
                {
                    var raw = typeAndFlags[i];
                    RootType[i] = (ManagedRootType)(ushort)(raw & 0xFFFF);
                    RootFlags[i] = (ManagedRootFlags)(ushort)(raw >> 16);
                }
            }

            public void Dispose()
            {
                Count = 0;
                ManagedObjectIndex.Dispose();
                RootType.Dispose();
                RootFlags.Dispose();
                Data.Dispose();
            }
        }
    }
}

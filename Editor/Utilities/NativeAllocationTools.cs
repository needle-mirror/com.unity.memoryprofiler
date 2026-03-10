using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;

namespace Unity.MemoryProfiler.Editor
{
    internal static class NativeAllocationTools
    {
        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public static string FormatAllocationAddress(SourceIndex nativeAllocation, CachedSnapshot snapshot)
        {
            // the trailing space helps in case a reference adds type and field info to this
            return string.Format("[0x{0:X16}] ", snapshot.NativeAllocations.Address[nativeAllocation.Index]);
        }

        // This marker is used in performance tests. Adjust the Performance tests if you change the marker or its usage.
        // TODO: Adjust Perf tests to actually use this
        public const string ProduceNativeAllocationMarkerName = "NativeAllocationTools.ProduceNativeAllocationName";
        static ProfilerMarker s_ProduceNativeAllocationId = new ProfilerMarker(ProduceNativeAllocationMarkerName);

        internal static string ProduceNativeAllocationName(SourceIndex nativeAllocation, CachedSnapshot snapshot, bool truncateTypeNames = true)
        {
            using var marker = s_ProduceNativeAllocationId.Auto();
            // Check cache first (populated before heap bytes are unloaded)
            if (snapshot.TableEntryNames?.TryGetNativeAllocationName(nativeAllocation.Index, out var cached) == true)
                return cached;

            if (snapshot.CrawledData.ConnectionsToMappedToSourceIndex.TryGetValue(nativeAllocation, out var references))
            {
                return ProduceNativeAllocationNameInternal(nativeAllocation, snapshot, ref references, truncateTypeNames);
            }
            return FormatAllocationAddress(nativeAllocation, snapshot);
        }

        /// <summary>
        /// Internal method that computes the native allocation name from managed references.
        /// Requires heap bytes to be loaded.
        /// </summary>
        internal static string ProduceNativeAllocationNameInternal(SourceIndex nativeAllocation, CachedSnapshot snapshot, ref UnsafeList<int> references, bool truncateTypeNames = true)
        {
            string name = FormatAllocationAddress(nativeAllocation, snapshot);
            for (int i = 0; i < references.Length; i++)
            {
                var connection = snapshot.CrawledData.Connections[references[i]];
                var fieldName = GetFieldName(connection, snapshot, truncateTypeNames);
                if (fieldName != null)
                {
                    name += fieldName;
                    break;
                }
            }
            return name;
        }

        internal static string GetFieldName(ManagedConnection connection, CachedSnapshot snapshot, bool truncateTypeNames)
        {
            if (connection.FieldFrom >= -1 || connection.ArrayIndexFrom >= 0)
            {
                var holdingObject = ObjectConnection.GetManagedReferenceSource(snapshot, connection);
                switch (holdingObject.displayObject.dataType)
                {
                    case ObjectDataType.Unknown:
                        break;
                    case ObjectDataType.Type:
                    case ObjectDataType.Value:
                    case ObjectDataType.BoxedValue:
                    case ObjectDataType.Object:
                    case ObjectDataType.ReferenceObject:
                        return holdingObject.GetFieldDescription(snapshot, truncateTypeNames: truncateTypeNames);
                    case ObjectDataType.Array:
                    case ObjectDataType.ReferenceArray:
                        return holdingObject.displayObject.GenerateArrayDescription(snapshot, truncateTypeNames);
                    case ObjectDataType.NativeObject:
                    case ObjectDataType.NativeAllocation:
                    case ObjectDataType.GCHandle:
                    default:
                        break;
                }
            }
            return null;
        }
    }
}

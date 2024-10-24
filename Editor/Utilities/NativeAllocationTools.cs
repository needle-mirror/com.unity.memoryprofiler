using Unity.Profiling;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;

namespace Unity.MemoryProfiler.Editor
{
    internal static class NativeAllocationTools
    {
        // This marker is used in performance tests. Adjust the Performance tests if you change the marker or its usage.
        // TODO: Adjust Perf tests to actually use this
        public const string ProduceNativeAllocationMarkerName = "NativeObjectTools.ProduceNativeAllocationName";
        static ProfilerMarker s_ProduceNativeAllocationId = new ProfilerMarker(ProduceNativeAllocationMarkerName);
        internal static string ProduceNativeAllocationName(SourceIndex nativeAllocation, CachedSnapshot snapshot, bool truncateTypeNames = true)
        {
            using var marker = s_ProduceNativeAllocationId.Auto();
            string name = string.Format("[0x{0:X16}] ", snapshot.NativeAllocations.Address[nativeAllocation.Index]);
            if (snapshot.CrawledData.ConnectionsToMappedToSourceIndex.TryGetValue(nativeAllocation, out var references))
            {
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
            }
            return name;
        }

        static string GetFieldName(ManagedConnection connection, CachedSnapshot snapshot, bool truncateTypeNames)
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
                        return holdingObject.GetFieldDescription(snapshot); // FIXME on merge, truncateTypeNames: truncateTypeNames);
                    case ObjectDataType.Array:
                    case ObjectDataType.ReferenceArray:
                        return holdingObject.displayObject.GenerateArrayDescription(snapshot, truncateTypeNames);
                    case ObjectDataType.NativeObject:
                        break;
                    default:
                        break;
                }
            }
            return null;
        }
    }
}

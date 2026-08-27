using System;
using Unity.MemoryProfiler.Editor.UIContentData;
using Unity.Profiling;

namespace Unity.MemoryProfiler.Editor
{
    internal static class ManagedObjectTools
    {
        // This marker is used in performance tests. Adjust the Performance tests if you change the marker or its usage.
        public const string ProduceManagedObjectNameMarkerName = "ManagedObjectTools.ProduceManagedObjectName";
        static ProfilerMarker s_ProduceManagedObjectName = new ProfilerMarker(ProduceManagedObjectNameMarkerName);

        static readonly string k_PointerFormatString = $"{DetailFormatter.PointerFormatString} {{1}}";

        internal static string ProduceManagedObjectName(this ManagedObjectInfo managedObjectInfo, CachedSnapshot snapshot, bool addInstanceId = false)
        {
            using var marker = s_ProduceManagedObjectName.Auto();
            var value = string.Empty;
            if (managedObjectInfo.NativeObjectIndex > 0)
            {
                var nativeObjectIds = snapshot.NativeObjects.InstanceId;
                var nativeObjectName = snapshot.NativeObjects.GetNonEmptyObjectName(managedObjectInfo.NativeObjectIndex);
                if (addInstanceId)
                    return String.Format("{0} ID: {1}", nativeObjectName, nativeObjectIds[managedObjectInfo.NativeObjectIndex]);
                else
                    return nativeObjectName;
            }
            else if (managedObjectInfo.ITypeDescription == snapshot.TypeDescriptions.ITypeString)
                value = StringTools.GetPreviewOrReadFirstLine(managedObjectInfo, snapshot, addQuotes: true);
            else if (managedObjectInfo.ITypeDescription == snapshot.TypeDescriptions.ITypeCharArray)
                value = StringTools.GetCharArrayPreviewOrReadFirstLine(managedObjectInfo, snapshot, addQuotes: true);
            return String.Format(k_PointerFormatString, managedObjectInfo.PtrObject, value);
        }
    }
}

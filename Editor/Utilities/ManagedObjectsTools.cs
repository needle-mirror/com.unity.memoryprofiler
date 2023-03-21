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

        internal static string ProduceManagedObjectName(this ManagedObjectInfo managedObjectInfo, CachedSnapshot snapshot, bool addInstanceId = false)
        {
            using var marker = s_ProduceManagedObjectName.Auto();
            var value = string.Empty;
            if (managedObjectInfo.NativeObjectIndex > 0)
            {
                var nativeObjectNames = snapshot.NativeObjects.ObjectName;
                var nativeObjectIds = snapshot.NativeObjects.InstanceId;
                if (addInstanceId)
                    return String.Format("{0} ID: {1}", nativeObjectNames[managedObjectInfo.NativeObjectIndex], nativeObjectIds[managedObjectInfo.NativeObjectIndex]);
                else
                    return nativeObjectNames[managedObjectInfo.NativeObjectIndex];
            }
            else if (managedObjectInfo.ITypeDescription == snapshot.TypeDescriptions.ITypeString)
                value = managedObjectInfo.ReadFirstStringLine(snapshot.VirtualMachineInformation, true);
            else if (managedObjectInfo.ITypeDescription == snapshot.TypeDescriptions.ITypeCharArray)
                value = managedObjectInfo.ReadFirstCharArrayLine(snapshot, true);
            else if (snapshot.TypeDescriptions.UnityObjectTypeIndexToNativeTypeIndex.ContainsKey(managedObjectInfo.ITypeDescription))
                return String.Format("0x{0:X16} {1}", managedObjectInfo.PtrObject, TextContent.LeakedManagedShellHint);
            return String.Format("0x{0:X16} {1}", managedObjectInfo.PtrObject, value);
        }
    }
}

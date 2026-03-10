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
                value = GetStringPreview(snapshot, managedObjectInfo, addQuotes: true);
            else if (managedObjectInfo.ITypeDescription == snapshot.TypeDescriptions.ITypeCharArray)
                value = GetCharArrayPreview(snapshot, managedObjectInfo, addQuotes: true);
            else if (snapshot.TypeDescriptions.UnifiedTypeInfoManaged[managedObjectInfo.ITypeDescription].IsUnityObjectType)
                return String.Format(k_PointerFormatString, managedObjectInfo.PtrObject, TextContent.LeakedManagedShellHint);
            return String.Format(k_PointerFormatString, managedObjectInfo.PtrObject, value);
        }

        /// <summary>
        /// Gets a string preview from cache, or reads it from heap bytes if available.
        /// </summary>
        public static string GetStringPreview(CachedSnapshot snapshot, ManagedObjectInfo managedObjectInfo, bool addQuotes)
        {
            return StringTools.GetPreviewOrReadFirstLine(managedObjectInfo, snapshot, addQuotes);
        }

        /// <summary>
        /// Gets a char array preview from cache, or reads it from heap bytes if available.
        /// </summary>
        static string GetCharArrayPreview(CachedSnapshot snapshot, ManagedObjectInfo managedObjectInfo, bool addQuotes)
        {
            // Try the cache first (populated during crawling if heap unloading is enabled)
            if (snapshot.TableEntryNames?.TryGetPreview(managedObjectInfo.ManagedObjectIndex, out var cached) == true)
                return cached;
            return managedObjectInfo.ReadFirstCharArrayLine(snapshot, true);
        }
    }
}

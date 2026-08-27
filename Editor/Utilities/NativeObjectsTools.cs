using System;
using Unity.Profiling;

namespace Unity.MemoryProfiler.Editor
{
    internal static class NativeObjectTools
    {
        // This marker is used in performance tests. Adjust the Performance tests if you change the marker or its usage.
        public const string ProduceNativeObjectIdMarkerName = "NativeObjectTools.ProduceNativeObjectId";
        static ProfilerMarker s_ProduceNativeObjectId = new ProfilerMarker(ProduceNativeObjectIdMarkerName);

        const string k_NativeObjectIdFormatString = "ID: {0}";
        const string k_NativeObjectAndIdFormatString = k_NativeObjectIdFormatString + " - \"{1}\"";
        public static readonly string NativeObjectIdFormatStringPrefix = String.Format(k_NativeObjectIdFormatString, "");

        internal static string ProduceNativeObjectNameAndId(long nativeObjectIndex, CachedSnapshot snapshot)
        {
            using var marker = s_ProduceNativeObjectId.Auto();

            return String.Format(k_NativeObjectAndIdFormatString, snapshot.NativeObjects.InstanceId[nativeObjectIndex], snapshot.NativeObjects.GetNonEmptyObjectName(nativeObjectIndex));
        }

        internal static string ProduceNativeObjectId(long nativeObjectIndex, CachedSnapshot snapshot)
        {
            using var marker = s_ProduceNativeObjectId.Auto();

            return String.Format(k_NativeObjectIdFormatString, snapshot.NativeObjects.InstanceId[nativeObjectIndex]);
        }
    }
}

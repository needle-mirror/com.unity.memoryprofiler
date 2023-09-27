using UnityEditor;

namespace Unity.MemoryProfiler.Editor.Analytics
{
    class DebugAnalyticsService : IAnalyticsService
    {
        bool IAnalyticsService.RegisterEventWithLimit(string eventName, int maxEventPerHour, int maxItems, string vendorKey)
        {
            UnityEngine.Debug.Log($"RegisterEventWithLimit({eventName}, {maxEventPerHour}, {maxItems}, {vendorKey})");
            return true;
        }

        bool IAnalyticsService.SendEventWithLimit(string eventName, object parameters)
        {
            UnityEngine.Debug.Log($"SendEventWithLimit({eventName}, {EditorJsonUtility.ToJson(parameters)})");
            return true;
        }
    }
}

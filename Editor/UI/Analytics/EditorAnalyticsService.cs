using UnityEditor;
using UnityEngine.Analytics;

namespace Unity.MemoryProfiler.Editor.Analytics
{
    class EditorAnalyticsService : IAnalyticsService
    {
        bool IAnalyticsService.RegisterEventWithLimit(string eventName, int maxEventPerHour, int maxItems, string vendorKey)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var result = EditorAnalytics.RegisterEventWithLimit(eventName, maxEventPerHour, maxItems, vendorKey);
#pragma warning restore CS0618 // Type or member is obsolete
            switch (result)
            {
                case AnalyticsResult.Ok:
                case AnalyticsResult.TooManyRequests:
                    return true;
                default:
                    return false;
            }
        }

        bool IAnalyticsService.SendEventWithLimit(string eventName, object parameters)
        {
#pragma warning disable CS0618
            return EditorAnalytics.SendEventWithLimit(eventName, parameters) == AnalyticsResult.Ok;
#pragma warning restore CS0618
        }
    }
}

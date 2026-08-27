using UnityEditor;
using UnityEngine.Analytics;

namespace Unity.MemoryProfiler.Editor.Analytics
{
    class EditorAnalyticsService : IAnalyticsService
    {
#if UNITY_6000_0_OR_NEWER
        const string k_EventName = "memoryProfiler";
        const string k_VendorKey = "unity.memoryprofiler";

        [AnalyticInfo(eventName: k_EventName, vendorKey: k_VendorKey)]
        class MemoryProfilerAnalytic : IAnalytic
        {
            readonly IAnalytic.IData m_Data;

            internal MemoryProfilerAnalytic(IAnalytic.IData data) => m_Data = data;

            public bool TryGatherData(out IAnalytic.IData data, out System.Exception error)
            {
                data = m_Data;
                error = null;
                return data != null;
            }
        }

        bool IAnalyticsService.RegisterEventWithLimit(string eventName, int maxEventPerHour, int maxItems, string vendorKey)
            => true; // Registration is not required in Unity 2023.2+

        bool IAnalyticsService.SendEventWithLimit(string eventName, object parameters)
        {
            if (parameters is not IAnalytic.IData analyticsData)
                return false;
            return EditorAnalytics.SendAnalytic(new MemoryProfilerAnalytic(analyticsData)) == AnalyticsResult.Ok;
        }
#else // pre-6000
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
#endif
    }
}

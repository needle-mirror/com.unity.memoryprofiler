namespace Unity.MemoryProfiler.Editor.Analytics
{
    interface IAnalyticsService
    {
        bool RegisterEventWithLimit(string eventName, int maxEventPerHour, int maxItems, string vendorKey);
        bool SendEventWithLimit(string eventName, object parameters);
    }
}

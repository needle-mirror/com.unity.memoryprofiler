using System.Collections.Generic;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Data model to describe the available Analysis options.
    class AnalysisTabBarModel
    {
        AnalysisTabBarModel(List<Option> options)
        {
            Options = options;
        }

        // The available analysis options.
        public List<Option> Options { get; }

        // Create a model containing the available Analysis options for a single snapshot.
        public static AnalysisTabBarModel CreateForSingleSnapshot(CachedSnapshot snapshot)
        {
            const string unityObjectsDescription = "A breakdown of memory contributing to all Unity Objects.";
            const string allTrackedMemoryDescription = "A breakdown of all tracked memory that Unity knows about.";
#if UNITY_2022_1_OR_NEWER
            return new AnalysisTabBarModel(new List<Option>()
            {
                new("Summary",
                    new SummaryViewController(snapshot)),
                new("Unity Objects",
                    new UnityObjectsBreakdownViewController(snapshot, unityObjectsDescription),
                    unityObjectsDescription),
                new("All Of Memory",
                    new AllTrackedMemoryBreakdownViewController(snapshot, allTrackedMemoryDescription),
                    allTrackedMemoryDescription, analyticsPageName: "All Tracked Memory"),
            });
#else
            var errorDescription = $"This feature is not available in Unity {UnityEngine.Application.unityVersion}. Please use Unity 2022.1 or newer.";
            return new AnalysisTabBarModel(new List<Option>()
            {
                new Option("Summary",
                    new SummaryViewController(snapshot)),
                new Option("Unity Objects",
                    new FeatureUnavailableViewController(errorDescription),
                    unityObjectsDescription),
                new Option("All Of Memory",
                    new FeatureUnavailableViewController(errorDescription),
                    allTrackedMemoryDescription, analyticsPageName: "All Tracked Memory"),
            });
#endif
        }

        // Create a model containing the available Analysis options when comparing two snapshots.
        public static AnalysisTabBarModel CreateForComparisonBetweenSnapshots(CachedSnapshot baseSnapshot, CachedSnapshot comparedSnapshot)
        {
            const string unityObjectsComparisonDescription = "A comparison of memory contributing to all Unity Objects in each capture.";
#if UNITY_2022_1_OR_NEWER
            return new AnalysisTabBarModel(new List<Option>()
            {
                new("Summary",
                    new SummaryViewController(baseSnapshot, comparedSnapshot),
                    analyticsPageName: "Summary Comparison"),
                new("Unity Objects",
                    new UnityObjectsComparisonViewController(
                        baseSnapshot,
                        comparedSnapshot,
                        unityObjectsComparisonDescription),
                    unityObjectsComparisonDescription,
                    "Unity Objects Comparison"),
            });
#else
            var errorDescription = $"This feature is not available in Unity {UnityEngine.Application.unityVersion}. Please use Unity 2022.1 or newer.";
            return new AnalysisTabBarModel(new List<Option>()
            {
                new Option("Summary",
                    new SummaryViewController(baseSnapshot, comparedSnapshot),
                    analyticsPageName: "Summary Comparison"),
                new Option("Unity Objects",
                    new FeatureUnavailableViewController(errorDescription),
                    unityObjectsComparisonDescription,
                    "Unity Objects Comparison"),
            });
#endif
        }

        public readonly struct Option
        {
            public Option(string displayName, ViewController viewController, string description = null, string analyticsPageName = null)
            {
                DisplayName = displayName;
                ViewController = viewController;
                Description = description;

                if (string.IsNullOrEmpty(analyticsPageName))
                    analyticsPageName = DisplayName;
                AnalyticsPageName = analyticsPageName;
            }

            public string DisplayName { get; }

            public ViewController ViewController { get; }

            public string Description { get; }

            public string AnalyticsPageName { get; }
        }
    }
}

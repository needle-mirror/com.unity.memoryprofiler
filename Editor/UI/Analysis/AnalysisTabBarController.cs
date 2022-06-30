using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    class AnalysisTabBarController : TabBarController
    {
        const string k_UxmlAssetGuid = "c6e4b2cc6ed5ca245a88ee690b17b4c0";
        const string k_UssClass_Dark = "analysis-tab-bar-view__dark";
        const string k_UssClass_Light = "analysis-tab-bar-view__light";
        const string k_UxmlIdentifier_TabBarView = "analysis-tab-bar-view__tab-bar__items";
        const string k_UxmlIdentifier_HelpButton = "analysis-tab-bar-view__tab-bar__help-button";
        const string k_UxmlIdentifier_ContentView = "analysis-tab-bar-view__content-view";

        // Model.
        readonly CachedSnapshot m_BaseSnapshot;
        readonly CachedSnapshot m_CompareSnapshot;

        // View.
        Button m_HelpButton;

        List<Option> m_Options;

        public AnalysisTabBarController(CachedSnapshot baseSnapshot, CachedSnapshot compareSnapshot) : base()
        {
            m_BaseSnapshot = baseSnapshot;
            m_CompareSnapshot = compareSnapshot;

            MakeTabBarItem = MakeAnalysisTabBarItem;
        }

        public Action MakePageSelector(string name)
        {
            for (int i = 0; i < m_Options.Count; i++)
            {
                if (m_Options[i].DisplayName != name)
                    continue;

                return (() => SelectedIndex = i);
            }

            return null;
        }

        protected override VisualElement ContentView { get; set; }

        protected override VisualElement TabBarView { get; set; }

        protected override VisualElement LoadView()
        {
            var view = ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlAssetGuid);
            if (view == null)
                throw new InvalidOperationException("Unable to create view from Uxml. Uxml must contain at least one child element.");
            view.style.flexGrow = 1;

            var themeUssClass = (EditorGUIUtility.isProSkin) ? k_UssClass_Dark : k_UssClass_Light;
            view.AddToClassList(themeUssClass);

            GatherReferencesInView(view);

            return view;
        }

        protected override void ViewLoaded()
        {
            base.ViewLoaded();
            m_HelpButton.clicked += OpenHelpDocumentation;

            if (m_CompareSnapshot == null)
                InitForSingleSnapshot(m_BaseSnapshot);
            else
                InitForComparisonBetweenSnapshots(m_BaseSnapshot, m_CompareSnapshot);

            ViewControllers = m_Options.Select(x => x.ViewController).ToArray();
            SelectedIndex = 0;
        }

        void GatherReferencesInView(VisualElement view)
        {
            TabBarView = view.Q<VisualElement>(k_UxmlIdentifier_TabBarView);
            ContentView = view.Q<VisualElement>(k_UxmlIdentifier_ContentView);
            m_HelpButton = view.Q<Button>(k_UxmlIdentifier_HelpButton);
        }

        VisualElement MakeAnalysisTabBarItem(ViewController viewController, int viewControllerIndex)
        {
            var option = m_Options[viewControllerIndex];
            var tabBarItem = new Label(option.DisplayName)
            {
                tooltip = option.Description
            };

            tabBarItem.AddToClassList("analysis-tab-bar-view__tab-bar-item");
            tabBarItem.RegisterClickEvent(() =>
            {
                // Stop Collecting interaction events for the old page and send them off
                MemoryProfilerAnalytics.EndEventWithMetadata<MemoryProfilerAnalytics.InteractionsInPage>();
                MemoryProfilerAnalytics.StartEvent<MemoryProfilerAnalytics.OpenedPageEvent>();
                SelectedIndex = viewControllerIndex;
                var viewName = m_Options[SelectedIndex].AnalyticsPageName;
                MemoryProfilerAnalytics.EndEvent(new MemoryProfilerAnalytics.OpenedPageEvent() { viewName = viewName });
                // Start collecting interaction events for this page
                MemoryProfilerAnalytics.StartEventWithMetaData<MemoryProfilerAnalytics.InteractionsInPage>(new MemoryProfilerAnalytics.InteractionsInPage() { viewName = viewName });
            });

            return tabBarItem;
        }

        void OpenHelpDocumentation()
        {
            MemoryProfilerAnalytics.AddInteractionCountToEvent<MemoryProfilerAnalytics.InteractionsInPage, MemoryProfilerAnalytics.PageInteractionType>(
                MemoryProfilerAnalytics.PageInteractionType.DocumentationOpened);
            Application.OpenURL(UIContentData.DocumentationUrls.AnalysisWindowHelp);
        }

        // Create a model containing the available Analysis options for a single snapshot.
        void InitForSingleSnapshot(CachedSnapshot snapshot)
        {
            const string unityObjectsDescription = "A breakdown of memory contributing to all Unity Objects.";
            const string allTrackedMemoryDescription = "A breakdown of all tracked memory that Unity knows about.";
#if UNITY_2022_1_OR_NEWER
            m_Options = new List<Option>()
            {
                new Option("Summary",
                    new SummaryViewController(snapshot, null) { TabController = this }),
                new Option("Unity Objects",
                    new UnityObjectsBreakdownViewController(snapshot, unityObjectsDescription),
                    unityObjectsDescription),
                new Option("All Of Memory",
                    new AllTrackedMemoryBreakdownViewController(snapshot, allTrackedMemoryDescription),
                    allTrackedMemoryDescription),
#if UNITY_ENABLE_EXPERIMENTAL_FEATURES
                new Option("All of System Memory",
                    new AllSystemMemoryBreakdownViewController(snapshot)),
#endif
            };
#else
            var errorDescription = $"This feature is not available in Unity {UnityEngine.Application.unityVersion}. Please use Unity 2022.1 or newer.";
            m_Options = new List<Option>()
            {
                new Option("Summary",
                    new SummaryViewController(snapshot, null) { TabController = this }),
                new Option("Unity Objects",
                    new FeatureUnavailableViewController(errorDescription),
                    unityObjectsDescription),
                new Option("All Of Memory",
                    new FeatureUnavailableViewController(errorDescription),
                    allTrackedMemoryDescription),
            };
#endif
        }

        // Create a model containing the available Analysis options when comparing two snapshots.
        void InitForComparisonBetweenSnapshots(CachedSnapshot baseSnapshot, CachedSnapshot comparedSnapshot)
        {
            const string unityObjectsComparisonDescription = "A comparison of memory contributing to all Unity Objects in each capture.";
            const string allTrackedMemoryComparisonDescription = "A comparison of all tracked memory in each capture.";
#if UNITY_2022_1_OR_NEWER
            m_Options = new List<Option>()
            {
                new Option("Summary",
                    new SummaryViewController(baseSnapshot, comparedSnapshot) { TabController = this },
                    analyticsPageName: "Summary Comparison"),
                new Option("Unity Objects",
                    new UnityObjectsComparisonViewController(
                        baseSnapshot,
                        comparedSnapshot,
                        unityObjectsComparisonDescription),
                    unityObjectsComparisonDescription,
                    "Unity Objects Comparison"),
                new Option("All Of Memory",
                    new AllTrackedMemoryComparisonViewController(
                        baseSnapshot,
                        comparedSnapshot,
                        allTrackedMemoryComparisonDescription),
                    allTrackedMemoryComparisonDescription,
                    "All Of Memory Comparison")
            };
#else
            var errorDescription = $"This feature is not available in Unity {UnityEngine.Application.unityVersion}. Please use Unity 2022.1 or newer.";
            m_Options = new List<Option>()
            {
                new Option("Summary",
                    new SummaryViewController(baseSnapshot, comparedSnapshot) { TabController = this },
                    analyticsPageName: "Summary Comparison"),
                new Option("Unity Objects",
                    new FeatureUnavailableViewController(errorDescription),
                    unityObjectsComparisonDescription,
                    "Unity Objects Comparison"),
                new Option("All Of Memory",
                    new FeatureUnavailableViewController(errorDescription),
                    allTrackedMemoryComparisonDescription,
                    "All Of Memory Comparison"),
            };
#endif
        }

        readonly struct Option
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

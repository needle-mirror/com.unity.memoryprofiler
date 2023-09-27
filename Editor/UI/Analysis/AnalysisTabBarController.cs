using System;
using System.Collections.Generic;
using System.Linq;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class AnalysisTabBarController : TabBarController, IAnalysisViewSelectable
    {
        const string k_UxmlAssetGuid = "c6e4b2cc6ed5ca245a88ee690b17b4c0";
        const string k_UssClass_Dark = "analysis-tab-bar-view__dark";
        const string k_UssClass_Light = "analysis-tab-bar-view__light";
        const string k_UxmlIdentifier_TabBarView = "analysis-tab-bar-view__tab-bar__items";
        const string k_UxmlIdentifier_HelpButton = "analysis-tab-bar-view__tab-bar__help-button";
        const string k_UxmlIdentifier_ContentView = "analysis-tab-bar-view__content-view";

        // Model.
        readonly ISelectionDetails m_SelectionDetails;
        readonly CachedSnapshot m_BaseSnapshot;
        readonly CachedSnapshot m_CompareSnapshot;

        // View.
        Button m_HelpButton;

        List<Option> m_Options;

        public AnalysisTabBarController(ISelectionDetails selectionDetails, CachedSnapshot baseSnapshot, CachedSnapshot compareSnapshot) : base()
        {
            m_SelectionDetails = selectionDetails;
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

        protected override void SelectedIndexChanged(int selectedIndex)
        {
            var analyticsPageName = m_Options[selectedIndex].DisplayName;
            if (m_CompareSnapshot != null)
                analyticsPageName = TextContent.GetComparisonViewName(analyticsPageName);

            MemoryProfilerAnalytics.SendOpenViewEvent(analyticsPageName, false);
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

        VisualElement MakeAnalysisTabBarItem(IViewController viewController, int viewControllerIndex)
        {
            var option = m_Options[viewControllerIndex];
            var tabBarItem = new Label(option.DisplayName)
            {
                tooltip = option.Description
            };

            tabBarItem.AddToClassList("analysis-tab-bar-view__tab-bar-item");
            tabBarItem.RegisterClickEvent(() =>
            {
                SelectedIndex = viewControllerIndex;
            });

            return tabBarItem;
        }

        static void OpenHelpDocumentation()
        {
            Application.OpenURL(UIContentData.DocumentationUrls.AnalysisWindowHelp);
        }

        // Create a model containing the available Analysis options for a single snapshot.
        void InitForSingleSnapshot(CachedSnapshot snapshot)
        {
            const string unityObjectsDescription = "A breakdown of memory contributing to all Unity Objects.";
            const string allTrackedMemoryDescription = "A breakdown of all tracked memory that Unity knows about.";
            m_Options = new List<Option>()
            {
                new Option(TextContent.SummaryViewName,
                    new SummaryViewController(m_SelectionDetails, this, snapshot, null) { TabController = this }),
                new Option(TextContent.UnityObjectsViewName,
                    new UnityObjectsBreakdownViewController(snapshot, unityObjectsDescription, m_SelectionDetails),
                    unityObjectsDescription),
                new Option(TextContent.AllOfMemoryViewName,
                    new AllTrackedMemoryBreakdownViewController(snapshot, allTrackedMemoryDescription, m_SelectionDetails),
                    allTrackedMemoryDescription),
            };

            if (MemoryProfilerSettings.ShowMemoryMapView)
                m_Options.Add(new Option(TextContent.MemoryMapViewName, new MemoryMapBreakdownViewController(snapshot, m_SelectionDetails)));
        }

        // Create a model containing the available Analysis options when comparing two snapshots.
        void InitForComparisonBetweenSnapshots(CachedSnapshot baseSnapshot, CachedSnapshot comparedSnapshot)
        {
            const string unityObjectsComparisonDescription = "A comparison of memory contributing to all Unity Objects in each capture.";
            const string allTrackedMemoryComparisonDescription = "A comparison of all tracked memory in each capture.";

            m_Options = new List<Option>()
            {
                new Option(TextContent.SummaryViewName,
                    new SummaryViewController(m_SelectionDetails, this, baseSnapshot, comparedSnapshot) { TabController = this }),
                new Option(TextContent.UnityObjectsViewName,
                    new UnityObjectsComparisonViewController(
                        baseSnapshot,
                        comparedSnapshot,
                        unityObjectsComparisonDescription,
                        m_SelectionDetails),
                    unityObjectsComparisonDescription),
                new Option(TextContent.AllOfMemoryViewName,
                    new AllTrackedMemoryComparisonViewController(
                        baseSnapshot,
                        comparedSnapshot,
                        allTrackedMemoryComparisonDescription,
                        m_SelectionDetails),
                    allTrackedMemoryComparisonDescription)
            };
        }

        public bool TrySelectCategory(IAnalysisViewSelectable.Category category)
        {
            for (int i = 0; i < ViewControllers.Length; i++)
            {
                var controller = ViewControllers[i];

                // We need to make sure that controller is fully initialized
                // before we can start querying about its state
                controller.EnsureLoaded();

                if ((controller is IAnalysisViewSelectable selectable) && selectable.TrySelectCategory(category))
                {
                    // We switch focus of tab view to the tab where selection is made
                    SelectedIndex = i;
                    return true;
                }
            }

            return false;
        }

        readonly struct Option
        {
            public Option(string displayName, IViewControllerWithVisibilityEvents viewController, string description = null)
            {
                DisplayName = displayName;
                ViewController = viewController;
                Description = description;
            }

            public string DisplayName { get; }

            public IViewControllerWithVisibilityEvents ViewController { get; }

            public string Description { get; }
        }
    }
}

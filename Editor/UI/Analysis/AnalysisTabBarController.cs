using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    class AnalysisTabBarController : TabBarController, TabBarController.IResponder
    {
        const string k_UxmlAssetGuid = "c6e4b2cc6ed5ca245a88ee690b17b4c0";
        const string k_UssClass_Dark = "analysis-tab-bar-view__dark";
        const string k_UssClass_Light = "analysis-tab-bar-view__light";
        const string k_UxmlIdentifier_TabBarView = "analysis-tab-bar-view__tab-bar__items";
        const string k_UxmlIdentifier_HelpButton = "analysis-tab-bar-view__tab-bar__help-button";
        const string k_UxmlIdentifier_ContentView = "analysis-tab-bar-view__content-view";

        // Model.
        readonly AnalysisTabBarModel m_Model;

        // View.
        Button m_HelpButton;

        public AnalysisTabBarController(AnalysisTabBarModel model) : base()
        {
            m_Model = model;
            MakeTabBarItem = MakeAnalysisTabBarItem;
            Responder = this;

            // Set view controllers from model's options.
            var options = model.Options;
            var viewControllers = new ViewController[options.Count];
            for (var i = 0; i < options.Count; i++)
            {
                var option = options[i];
                viewControllers[i] = option.ViewController;
            }

            ViewControllers = viewControllers;
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
        }

        void GatherReferencesInView(VisualElement view)
        {
            TabBarView = view.Q<VisualElement>(k_UxmlIdentifier_TabBarView);
            ContentView = view.Q<VisualElement>(k_UxmlIdentifier_ContentView);
            m_HelpButton = view.Q<Button>(k_UxmlIdentifier_HelpButton);
        }

        VisualElement MakeAnalysisTabBarItem(ViewController viewController, int viewControllerIndex)
        {
            var option = m_Model.Options[viewControllerIndex];
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
                var viewName = m_Model.Options[SelectedIndex].AnalyticsPageName;
                MemoryProfilerAnalytics.EndEvent(new MemoryProfilerAnalytics.OpenedPageEvent() { viewName = viewName});
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

        void IResponder.TabBarControllerSelectedIndexChanged(TabBarController tabBarController, int selectedIndex)
        {
            var option = m_Model.Options[selectedIndex];
        }
    }
}

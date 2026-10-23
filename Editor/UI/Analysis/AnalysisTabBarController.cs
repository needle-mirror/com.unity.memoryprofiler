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
        readonly CrossModeRevealProvider m_CrossModeReveal;

        // View.
        Button m_HelpButton;

        List<Option> m_Options;

        /// <summary>Raised when the shown tab changes, i.e. when <see cref="ActiveLocator"/> may have changed.</summary>
        public event Action ActiveTableChanged;

        public AnalysisTabBarController(ISelectionDetails selectionDetails, CachedSnapshot baseSnapshot, CachedSnapshot compareSnapshot, CrossModeRevealProvider crossModeReveal = null) : base()
        {
            m_SelectionDetails = selectionDetails;
            m_BaseSnapshot = baseSnapshot;
            m_CompareSnapshot = compareSnapshot;
            m_CrossModeReveal = crossModeReveal;

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

        /// <summary>
        /// Opens the Roots &amp; Impact view with a preset exclusive <see cref="ObjectStatus"/> filter mask and
        /// flatten state. Returns false when the view is absent, e.g. in comparison mode.
        /// </summary>
        public bool TryOpenRootsAndImpactWithStatusMask(ulong exclusiveStatusMask, bool flatten)
        {
            var index = IndexOfTable(BreakdownTableId.RootsAndImpact);
            if (index < 0 || ViewControllers[index] is not IRootsAndImpactStatusFilterTarget target)
                return false;

            // Before EnsureLoaded, so the view's initial build already uses the preset.
            target.ApplyStatusMaskPreset(exclusiveStatusMask, flatten);
            ViewControllers[index].EnsureLoaded();
            SelectedIndex = index;
            return true;
        }

        /// <summary>The tab index of <paramref name="table"/>, or -1 when it isn't one of this controller's tabs, e.g. in the other analysis mode.</summary>
        int IndexOfTable(BreakdownTableId table)
        {
            if (ViewControllers == null)
                return -1;

            for (int i = 0; i < ViewControllers.Length; i++)
            {
                if (ViewControllers[i] is ITableItemLocator locator && locator.TableId == table)
                    return i;
            }
            return -1;
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
            ActiveTableChanged?.Invoke();

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
            var unityObjectsDescription = TextContent.UnityObjectsTabDescription;
            var rootsAndImpactDescription = TextContent.RootsAndImpactTabDescription;
            var allTrackedMemoryDescription = TextContent.AllTrackedMemoryTabDescription;
            var memoryMapViewDescription = TextContent.MemoryMapTabDescription;

            m_Options = new List<Option>()
            {
                new Option(TextContent.SummaryViewName,
                    new SummaryViewController(m_SelectionDetails, this, snapshot, null) { TabController = this }),
                new Option(TextContent.UnityObjectsViewName,
                    new UnityObjectsBreakdownViewController(snapshot, unityObjectsDescription, m_SelectionDetails) { TabController = this },
                    unityObjectsDescription),
            };

            m_Options.Add(new Option(TextContent.RootsAndImpactViewName,
                new RootsAndImpactBreakdownViewController(snapshot, rootsAndImpactDescription, m_SelectionDetails) { TabController = this },
                rootsAndImpactDescription));

            m_Options.Add(new Option(TextContent.AllOfMemoryViewName,
                new AllTrackedMemoryBreakdownViewController(snapshot, allTrackedMemoryDescription, m_SelectionDetails) { TabController = this },
                allTrackedMemoryDescription));

            if (MemoryProfilerSettings.ShowMemoryMapView)
                m_Options.Add(new Option(TextContent.MemoryMapViewName,
                    new MemoryMapBreakdownViewController(snapshot, memoryMapViewDescription, m_SelectionDetails) { TabController = this },
                    memoryMapViewDescription));
        }

        // Create a model containing the available Analysis options when comparing two snapshots.
        void InitForComparisonBetweenSnapshots(CachedSnapshot baseSnapshot, CachedSnapshot comparedSnapshot)
        {
            var unityObjectsComparisonDescription = TextContent.UnityObjectsComparisonTabDescription;
            var allTrackedMemoryComparisonDescription = TextContent.AllTrackedMemoryComparisonTabDescription;

            m_Options = new List<Option>()
            {
                new Option(TextContent.SummaryViewName,
                    new SummaryViewController(m_SelectionDetails, this, baseSnapshot, comparedSnapshot) { TabController = this }),
                new Option(TextContent.UnityObjectsViewName,
                    new UnityObjectsComparisonViewController(
                        baseSnapshot,
                        comparedSnapshot,
                        unityObjectsComparisonDescription,
                        m_SelectionDetails) { TabController = this },
                    unityObjectsComparisonDescription),
                new Option(TextContent.AllOfMemoryViewName,
                    new AllTrackedMemoryComparisonViewController(
                        baseSnapshot,
                        comparedSnapshot,
                        allTrackedMemoryComparisonDescription,
                        m_SelectionDetails) { TabController = this },
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

        /// <summary>
        /// Finds the <see cref="ITableItemLocator"/> for <paramref name="table"/> only if it's already built —
        /// never calls <see cref="ViewController.EnsureLoaded"/> — so callers (e.g. populating the "Select In..."
        /// menu) can use its live, accurate <see cref="ITableItemLocator.GetVisibility"/> for free when available,
        /// falling back to a static check for tables that aren't built yet.
        /// </summary>
        public ITableItemLocator TryGetLoadedLocator(BreakdownTableId table)
        {
            var index = IndexOfTable(table);
            return index >= 0 && ViewControllers[index].IsViewLoaded ? (ITableItemLocator)ViewControllers[index] : null;
        }

        /// <summary>Whether <paramref name="table"/> is one of this controller's own tabs, i.e. shown in the current analysis mode.</summary>
        public bool HasTable(BreakdownTableId table) => IndexOfTable(table) >= 0;

        /// <summary>Whether <see cref="TryRevealInTable"/> can reveal an item of <paramref name="owner"/> in <paramref name="table"/>, if need be by switching analysis mode.</summary>
        public bool CanRevealIn(BreakdownTableId table, CachedSnapshot owner)
            => HasTable(table) || (m_CrossModeReveal != null && m_CrossModeReveal.CanSwitchTo(table, owner));

        /// <summary>The <see cref="ITableItemLocator"/> of the currently shown tab, or null when that tab (e.g. Summary) is not a table.</summary>
        public ITableItemLocator ActiveLocator =>
            ViewControllers != null && SelectedIndex >= 0 && SelectedIndex < ViewControllers.Length
                ? ViewControllers[SelectedIndex] as ITableItemLocator
                : null;

        public bool TryRevealInTable(BreakdownTableId table, CachedSnapshot.SourceIndex source, bool frame, bool select, CachedSnapshot owner = null)
        {
            var index = IndexOfTable(table);
            if (index >= 0)
            {
                ViewControllers[index].EnsureLoaded();
                var previousIndex = SelectedIndex;
                SelectedIndex = index;
                if (!TableRevealHelper.RevealWithPrompt((ITableItemLocator)ViewControllers[index], source, frame, select, owner))
                    SelectedIndex = previousIndex;
                return true;
            }

            // Not one of this controller's own tabs - it may still be reachable by switching analysis mode.
            if (m_CrossModeReveal != null && m_CrossModeReveal.CanSwitchTo(table, owner))
            {
                m_CrossModeReveal.SwitchModeAndReveal(new TableRevealRequest(table, source, owner, frame, select));
                return true;
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

using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    class SummaryViewController : ViewController, IViewControllerWithVisibilityEvents
    {
        const string k_UxmlAssetGuid = "63f1db43e50fc4f4288f3d1b1c3d9078";
        const string k_UssClass_Dark = "summary-view__dark";
        const string k_UssClass_Light = "summary-view__light";
        const string k_NormalizedTogglePrefKey = "com.unity.memoryprofiler:MemoryUsageSummary.Normalized";

        // State
        readonly ISelectionDetails m_SelectionDetails;
        readonly IAnalysisViewSelectable m_AnalysisItemSelection;
        readonly CachedSnapshot m_BaseSnapshot;
        readonly CachedSnapshot m_ComparedSnapshot;

        // View
        VisualElement m_SummaryUnavailable;
        Label m_SummaryUnavailableMessage;

        VisualElement m_IssuesBox;

        VisualElement m_NormalizedToggleHeader;
        Toggle m_NormalizedToggle;
        VisualElement m_ResidentMemoryBreakdown;
        VisualElement m_CommittedMemoryBreakdown;
        VisualElement m_ManagedMemoryBreakdown;
        VisualElement m_UnityObjectsBreakdown;
        VisualElement m_RootsAndImpactBreakdown;

        List<IMemorySummaryViewController> m_WidgetControllers;
        GenericMemorySummaryViewController m_RootsAndImpactController;

        public SummaryViewController(ISelectionDetails selectionDetails, IAnalysisViewSelectable itemSelection, CachedSnapshot baseSnapshot, CachedSnapshot comparedSnapshot)
        {
            m_SelectionDetails = selectionDetails;
            m_AnalysisItemSelection = itemSelection;
            m_BaseSnapshot = baseSnapshot;
            m_ComparedSnapshot = comparedSnapshot;

            m_WidgetControllers = new List<IMemorySummaryViewController>();
        }

        public AnalysisTabBarController TabController { private get; init; }

        protected override VisualElement LoadView()
        {
            var view = ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlAssetGuid);
            if (view == null)
                throw new InvalidOperationException("Unable to create view from Uxml. Uxml must contain at least one child element.");
            view.style.flexGrow = 1;

            var themeUssClass = (EditorGUIUtility.isProSkin) ? k_UssClass_Dark : k_UssClass_Light;
            view.AddToClassList(themeUssClass);

            return view;
        }

        protected override void ViewLoaded()
        {
            base.ViewLoaded();
            GatherReferences();
            RefreshView();
        }

        void GatherReferences()
        {
            m_SummaryUnavailable = View.Q("memory-usage-summary__unavailable");
            m_SummaryUnavailableMessage = m_SummaryUnavailable.Q<Label>("memory-usage-summary__unavailable__message");

            m_IssuesBox = View.Q<VisualElement>("memory-usage-summary__warning__box");

            m_ResidentMemoryBreakdown = View.Q("memory-usage-summary__content__system");
            m_CommittedMemoryBreakdown = View.Q("memory-usage-summary__content__total");
            m_ManagedMemoryBreakdown = View.Q("memory-usage-summary__content__managed");
            m_UnityObjectsBreakdown = View.Q("memory-usage-summary__content__unity-objects");
            m_RootsAndImpactBreakdown = View.Q("memory-usage-summary__content__roots-and-impact");

            m_NormalizedToggleHeader = View.Q<VisualElement>("memory-usage-summary__header__right");
            m_NormalizedToggle = View.Q<Toggle>("memory-usage-summary-section__normalized-toggle");
        }

        void RefreshView()
        {
            bool isSupportedSnapshots = IsSupportedSnapshotFormat();
            UIElementsHelper.SetVisibility(m_SummaryUnavailable.parent, !isSupportedSnapshots);
            m_SummaryUnavailableMessage.text = SummaryTextContent.MemoryUsageUnavailableMessage;

            // Warn about issues with snapshots
            var issuesModelBuilder = new SnapshotIssuesModelBuilder(m_BaseSnapshot, m_ComparedSnapshot);
            var issuesViewController = new SnapshotIssuesViewController(issuesModelBuilder.Build());
            AddChild(issuesViewController);
            m_IssuesBox.Add(issuesViewController.View);
            UIElementsHelper.SetVisibility(m_IssuesBox.parent, issuesViewController.HasIssues);

            // "Normalize" checkbox for compare mode
            bool compareMode = m_ComparedSnapshot != null;
            UIElementsHelper.SetVisibility(m_NormalizedToggleHeader, compareMode && isSupportedSnapshots);
            bool normalizedSetting = EditorPrefs.GetBool(k_NormalizedTogglePrefKey, false);
            m_NormalizedToggle.value = normalizedSetting;
            m_NormalizedToggle.RegisterValueChangedCallback(ToggleNormalized);

            // Memory breakdown widgets
            var allOfMemoryInspectAction = TabController.MakePageSelector(TextContent.AllOfMemoryViewName);
            var committedMemoryController = new GenericMemorySummaryViewController(new AllMemorySummaryModelBuilder(m_BaseSnapshot, m_ComparedSnapshot), HasDetailedResidentMemoryInformation())
            {
                TotalLabelFormat = TextContent.TotalAllocatedFormat,
                InspectAction = compareMode ? null : () =>
                {
                    MemoryProfilerAnalytics.AddSummaryViewInteraction(MemoryProfilerAnalytics.SummaryViewInteraction.InspectAllocatedMemoryClickCount);
                    allOfMemoryInspectAction();
                },
                Normalized = normalizedSetting
            };
            committedMemoryController.OnRowSelected += (model, row) => MemoryProfilerAnalytics.AddSummaryViewInteraction(MemoryProfilerAnalytics.SummaryViewInteraction.AllocatedMemoryClickCount);
            committedMemoryController.OnRowHovered += (model, row, state) => MemoryProfilerAnalytics.AddSummaryViewInteraction(MemoryProfilerAnalytics.SummaryViewInteraction.AllocatedMemoryHoverCount);
            committedMemoryController.OnRowDoubleClick += (model, row) => m_AnalysisItemSelection.TrySelectCategory(model.Rows[row].CategoryId);
            AddController(m_CommittedMemoryBreakdown, isSupportedSnapshots, committedMemoryController);

            var residentMemoryController = new ResidentMemorySummaryViewController(new ResidentMemorySummaryModelBuilder(m_BaseSnapshot, m_ComparedSnapshot))
            {
                Normalized = normalizedSetting,
            };
            residentMemoryController.OnRowSelected += (model, row) => MemoryProfilerAnalytics.AddSummaryViewInteraction(MemoryProfilerAnalytics.SummaryViewInteraction.ResidentMemoryClickCount);
            residentMemoryController.OnRowHovered += (model, index, state) =>
            {
                MemoryProfilerAnalytics.AddSummaryViewInteraction(MemoryProfilerAnalytics.SummaryViewInteraction.ResidentMemoryHoverCount);
                committedMemoryController.ForceShowResidentBars = state;
            };
            AddController(m_ResidentMemoryBreakdown, isSupportedSnapshots && HasResidentMemoryInformation(), residentMemoryController);

            var managedMemoryController = new GenericMemorySummaryViewController(new ManagedMemorySummaryModelBuilder(m_BaseSnapshot, m_ComparedSnapshot), HasDetailedResidentMemoryInformation())
            {
                TotalLabelFormat = TextContent.TotalFormat,
                InspectAction = () =>
                {
                    MemoryProfilerAnalytics.AddSummaryViewInteraction(MemoryProfilerAnalytics.SummaryViewInteraction.InspectManagedMemoryClickCount);
                    allOfMemoryInspectAction();
                },
                Normalized = normalizedSetting
            };
            managedMemoryController.OnRowSelected += (model, row) => MemoryProfilerAnalytics.AddSummaryViewInteraction(MemoryProfilerAnalytics.SummaryViewInteraction.ManagedMemoryClickCount);
            managedMemoryController.OnRowHovered += (model, row, state) => MemoryProfilerAnalytics.AddSummaryViewInteraction(MemoryProfilerAnalytics.SummaryViewInteraction.ManagedMemoryHoverCount);
            AddController(m_ManagedMemoryBreakdown, isSupportedSnapshots && HasManagedMemoryInformation(), managedMemoryController);

            var unityObjectInspectAction = TabController.MakePageSelector(TextContent.UnityObjectsViewName);
            var unityObjectController = new GenericMemorySummaryViewController(new UnityObjectsMemorySummaryModelBuilder(m_BaseSnapshot, m_ComparedSnapshot), false)
            {
                TotalLabelFormat = TextContent.TotalFormat,
                InspectAction = () =>
                {
                    MemoryProfilerAnalytics.AddSummaryViewInteraction(MemoryProfilerAnalytics.SummaryViewInteraction.InspectUnityObjectsClickCount);
                    unityObjectInspectAction();
                },
                Normalized = normalizedSetting
            };
            unityObjectController.OnRowSelected += (model, row) => MemoryProfilerAnalytics.AddSummaryViewInteraction(MemoryProfilerAnalytics.SummaryViewInteraction.UnityObjectsClickCount);
            unityObjectController.OnRowHovered += (model, row, state) => MemoryProfilerAnalytics.AddSummaryViewInteraction(MemoryProfilerAnalytics.SummaryViewInteraction.UnityObjectsHoverCount);
            AddController(m_UnityObjectsBreakdown, isSupportedSnapshots && HasNativeObjectsInformation(), unityObjectController);

            // Roots & Impact has no comparison view, so compare mode gets no Inspect or row navigation.
            var rootsAndImpactCompareMode = m_ComparedSnapshot != null;
            var rootsAndImpactController = new GenericMemorySummaryViewController(new RootsAndImpactSummaryModelBuilder(m_BaseSnapshot, m_ComparedSnapshot), true,
                new MemorySummaryDataOverrides(MemorySummaryValueClassification.TotalAndExclusiveImpact))
            {
                InspectAction = rootsAndImpactCompareMode ? null : () =>
                {
                    MemoryProfilerAnalytics.AddSummaryViewInteraction(MemoryProfilerAnalytics.SummaryViewInteraction.InspectRootsAndImpactClickCount);
                    TabController.TryOpenRootsAndImpactWithStatusMask(RootsAndImpactSummaryModelBuilder.AllSavingsStatusMask, flatten: true);
                },
                Normalized = normalizedSetting,
                // Impact categories have no darkened committed color of their own.
                DeEmphasizeNonResidentPortion = true,
            };
            ApplyPotentialSavingsDisplayMode(rootsAndImpactController);
            if (!rootsAndImpactCompareMode)
                rootsAndImpactController.OnRowDoubleClick += (model, row) =>
                {
                    var r = model.Rows[row];
                    if (r.StatusMask != 0)
                        TabController.TryOpenRootsAndImpactWithStatusMask(r.StatusMask, r.DeepLinkFlatten);
                };
            // The Legend shows its Inspect column whenever a provider is set.
            rootsAndImpactController.RowActionProvider = rootsAndImpactCompareMode ? null : new PotentialSavingsRowActionProvider(TabController);
            rootsAndImpactController.OnRowSelected += (model, row) => MemoryProfilerAnalytics.AddSummaryViewInteraction(MemoryProfilerAnalytics.SummaryViewInteraction.RootsAndImpactClickCount);
            rootsAndImpactController.OnRowHovered += (model, row, state) => MemoryProfilerAnalytics.AddSummaryViewInteraction(MemoryProfilerAnalytics.SummaryViewInteraction.RootsAndImpactHoverCount);
            var showRootsAndImpact = isSupportedSnapshots && HasNativeObjectsInformation() && HasRootsAndImpactInformation();
            AddController(m_RootsAndImpactBreakdown, showRootsAndImpact, rootsAndImpactController);
            if (showRootsAndImpact)
            {
                m_RootsAndImpactController = rootsAndImpactController;
                MemoryProfilerSettings.ShowTotalImpactInPotentialSavingsChanged += OnShowTotalImpactInPotentialSavingsChanged;
            }

            View.RegisterCallback<PointerDownEvent>((e) =>
            {
                ClearSelection();
                e.StopPropagation();
            });
        }

        void AddController<T>(VisualElement root, bool state, T controller) where T : ViewController, IMemorySummaryViewController
        {
            if (state)
            {
                var controllerId = m_WidgetControllers.Count;
                controller.OnRowSelected += (model, rowId) => { OnShowDetailsForSelection(controller, model, rowId); };
                root.Clear();
                root.Add(controller.View);
                AddChild(controller);
                m_WidgetControllers.Add(controller);
            }
            else
            {
                controller.Dispose();
                UIElementsHelper.SetVisibility(root.parent, state);
            }
        }

        void ToggleNormalized(ChangeEvent<bool> evt)
        {
            EditorPrefs.SetBool(k_NormalizedTogglePrefKey, evt.newValue);
            SetNormalized(evt.newValue);
        }

        void SetNormalized(bool normalized)
        {
            foreach (var controller in m_WidgetControllers)
                controller.Normalized = normalized;
        }

        void OnShowTotalImpactInPotentialSavingsChanged()
        {
            if (m_RootsAndImpactController != null)
                ApplyPotentialSavingsDisplayMode(m_RootsAndImpactController);
        }

        /// <summary>
        /// Shows either exclusive impact only, with total impact and a preference hint in the tooltips, or both
        /// amounts, with a header matching the deep-linked table's.
        /// </summary>
        static void ApplyPotentialSavingsDisplayMode(GenericMemorySummaryViewController controller)
        {
            var showTotal = MemoryProfilerSettings.ShowTotalImpactInPotentialSavings;
            controller.ShowResidentSizeOnly = !showTotal;
            controller.TotalLabelFormat = showTotal ? TextContent.PotentialSavingsTotalAndExclusiveFormat : TextContent.PotentialSavingsExclusiveTotalFormat;
            controller.TooltipHint = showTotal ? null : TextContent.PotentialSavingsShowTotalImpactHint;
        }

        bool IsSupportedSnapshotFormat()
        {
            if (!m_BaseSnapshot.HasTargetAndMemoryInfo || !m_BaseSnapshot.HasMemoryLabelSizesAndGCHeapTypes)
                return false;

            if (m_ComparedSnapshot != null && (!m_ComparedSnapshot.HasTargetAndMemoryInfo || !m_ComparedSnapshot.HasMemoryLabelSizesAndGCHeapTypes))
                return false;

            return true;
        }

        bool HasResidentMemoryInformation()
        {
            if (!m_BaseSnapshot.HasSystemMemoryRegionsInfo)
                return false;

            if (m_ComparedSnapshot != null && !m_ComparedSnapshot.HasSystemMemoryRegionsInfo)
                return false;

            return true;
        }

        bool HasDetailedResidentMemoryInformation()
        {
            if (!m_BaseSnapshot.HasSystemMemoryResidentPages)
                return false;

            if (m_ComparedSnapshot != null && !m_ComparedSnapshot.HasSystemMemoryResidentPages)
                return false;

            return true;
        }

        bool HasManagedMemoryInformation()
        {
            if (m_BaseSnapshot.ManagedHeapSections.Count <= 0)
                return false;

            if (m_ComparedSnapshot != null && m_ComparedSnapshot.ManagedHeapSections.Count <= 0)
                return false;

            return true;
        }

        bool HasNativeObjectsInformation()
        {
            if ((m_BaseSnapshot.NativeObjects == null) || (m_BaseSnapshot.NativeObjects.Count <= 0))
                return false;

            if (m_ComparedSnapshot != null && ((m_ComparedSnapshot.NativeObjects == null) || (m_ComparedSnapshot.NativeObjects.Count <= 0)))
                return false;

            return true;
        }


        bool HasRootsAndImpactInformation()
        {
            if (m_BaseSnapshot.RootAndImpactInfo is not { SuccessfullyBuilt: true })
                return false;

            return m_ComparedSnapshot == null || m_ComparedSnapshot.RootAndImpactInfo is { SuccessfullyBuilt: true };
        }

        void OnShowDetailsForSelection(IMemorySummaryViewController controller, MemorySummaryModel model, int rowId)
        {
            // Set new selection details
            var selection = controller.MakeSelection(rowId);
            m_SelectionDetails.SetSelection(selection);

            // Clear selection in all other widgets
            foreach (var item in m_WidgetControllers)
            {
                if (item == controller)
                    continue;
                item.ClearSelection();
            }
        }

        void ClearSelection(bool notify = true)
        {
            foreach (var item in m_WidgetControllers)
                item.ClearSelection();

            if (notify)
                m_SelectionDetails.ClearSelection();
        }

        void IViewControllerWithVisibilityEvents.ViewWillBeDisplayed()
        {
            // Silent deselection
            ClearSelection(notify: false);
        }

        void IViewControllerWithVisibilityEvents.ViewWillBeHidden()
        {
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (m_RootsAndImpactController != null)
            {
                MemoryProfilerSettings.ShowTotalImpactInPotentialSavingsChanged -= OnShowTotalImpactInPotentialSavingsChanged;
                m_RootsAndImpactController = null;
            }
            if (m_WidgetControllers != null)
            {
                foreach (var controller in m_WidgetControllers)
                {
                    controller?.Dispose();
                }
                m_WidgetControllers = null;
            }
        }

        /// <summary>
        /// Deep-links a Potential Savings row into the Roots &amp; Impact view, filtered to the row's savings statuses.
        /// </summary>
        sealed class PotentialSavingsRowActionProvider : MemorySummaryRowActionProvider
        {
            readonly AnalysisTabBarController m_TabController;

            public PotentialSavingsRowActionProvider(AnalysisTabBarController tabController)
            {
                m_TabController = tabController;
            }

            public override string Label => TextContent.RootsAndImpactInspectInViewButton;

            public override string Tooltip => TextContent.RootsAndImpactInspectInViewButtonTooltip;

            public override bool HasAction(in MemorySummaryModel.Row row) => row.StatusMask != 0;

            public override Action GetAction(MemorySummaryModel.Row row)
            {
                if (!HasAction(in row))
                    return null;

                return () =>
                {
                    MemoryProfilerAnalytics.AddSummaryViewInteraction(MemoryProfilerAnalytics.SummaryViewInteraction.InspectRootsAndImpactClickCount);
                    m_TabController.TryOpenRootsAndImpactWithStatusMask(row.StatusMask, row.DeepLinkFlatten);
                };
            }
        }
    }
}

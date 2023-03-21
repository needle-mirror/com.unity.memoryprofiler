using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    class SummaryViewController : ViewController
    {
        const string k_UxmlAssetGuid = "63f1db43e50fc4f4288f3d1b1c3d9078";
        const string k_UssClass_Dark = "summary-view__dark";
        const string k_UssClass_Light = "summary-view__light";

        // State
        readonly ISelectionDetails m_SelectionDetails;
        readonly IAnalysisViewSelectable m_AnalysisItemSelection;
        readonly CachedSnapshot m_BaseSnapshot;
        readonly CachedSnapshot m_ComparedSnapshot;

        // View
        VisualElement m_SummaryUnavailable;
        Label m_SummaryUnavailableMessage;

        VisualElement m_IssuesBox;

        Toggle m_NormalizedToggle;
        VisualElement m_ResidentMemoryBreakdown;
        VisualElement m_CommittedMemoryBreakdown;
        VisualElement m_ManagedMemoryBreakdown;
        VisualElement m_UnityObjectsBreakdown;

        List<IMemorySummaryViewController> m_WidgetControllers;

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

            m_NormalizedToggle = View.Q<Toggle>("memory-usage-summary-section__normalized-toggle");
        }

        void RefreshView()
        {
            bool isSupportedSnapshots = IsSupportedSnapshotFormat();
            UIElementsHelper.SetVisibility(m_SummaryUnavailable.parent, !isSupportedSnapshots);
            m_SummaryUnavailableMessage.text = SummaryTextContent.kMemoryUsageUnavailableMessage;

            // Warn about issues with snapshots
            var issuesModelBuilder = new SnapshotIssuesModelBuilder(m_BaseSnapshot, m_ComparedSnapshot);
            var issuesViewController = new SnapshotIssuesViewController(issuesModelBuilder.Build());
            AddChild(issuesViewController);
            m_IssuesBox.Add(issuesViewController.View);
            UIElementsHelper.SetVisibility(m_IssuesBox.parent, issuesViewController.HasIssues);

            // "Normalize" checkbox for compare mode
            bool comapreMode = m_ComparedSnapshot != null;
            UIElementsHelper.SetVisibility(m_NormalizedToggle, comapreMode && isSupportedSnapshots);
            bool normalizedSetting = EditorPrefs.GetBool("com.unity.memoryprofiler:MemoryUsageSummary.Normalized", false);
            m_NormalizedToggle.value = normalizedSetting;
            m_NormalizedToggle.RegisterValueChangedCallback(ToggleNormalized);

            // Memory breakdown widgets
            var committedMemoryController = new GenericMemorySummaryViewController(new AllMemorySummaryModelBuilder(m_BaseSnapshot, m_ComparedSnapshot), HasDetailedResidentMemoryInformation())
            {
                TotalLabelFormat = "Total Allocated: {0}",
                InspectAction = comapreMode ? null : TabController.MakePageSelector("All Of Memory"),
                Normalized = normalizedSetting
            };
            committedMemoryController.OnRowDoubleClick += (model, row) => m_AnalysisItemSelection.TrySelectCategory(model.Rows[row].CategoryId);
            AddController(m_CommittedMemoryBreakdown, isSupportedSnapshots, committedMemoryController);

            var residentMemoryController = new ResidentMemorySummaryViewController(new ResidentMemorySummaryModelBuilder(m_BaseSnapshot, m_ComparedSnapshot))
            {
                Normalized = normalizedSetting,
            };
            residentMemoryController.OnRowHovered += (model, index, state) => { committedMemoryController.ForceShowResidentBars = state; };
            AddController(m_ResidentMemoryBreakdown, isSupportedSnapshots && HasResidentMemoryInformation(), residentMemoryController);

            AddController(
                m_ManagedMemoryBreakdown,
                isSupportedSnapshots && HasManagedMemoryInformation(),
                new GenericMemorySummaryViewController(new ManagedMemorySummaryModelBuilder(m_BaseSnapshot, m_ComparedSnapshot), false)
                {
                    TotalLabelFormat = "Total: {0}",
                    InspectAction = TabController.MakePageSelector("All Of Memory"),
                    Normalized = normalizedSetting
                });

            AddController(
                m_UnityObjectsBreakdown,
                isSupportedSnapshots && HasNativeObjectsInformation(),
                new GenericMemorySummaryViewController(new UnityObjectsMemorySummaryModelBuilder(m_BaseSnapshot, m_ComparedSnapshot), false)
                {
                    TotalLabelFormat = "Total: {0}",
                    InspectAction = TabController.MakePageSelector("Unity Objects"),
                    Normalized = normalizedSetting
                });

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
            EditorPrefs.SetBool("com.unity.memoryprofiler:MemoryUsageSummary.Normalized", evt.newValue);
            SetNormalized(evt.newValue);
        }

        void SetNormalized(bool normalized)
        {
            foreach (var controller in m_WidgetControllers)
                controller.Normalized = normalized;
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
            if (!m_BaseSnapshot.HasSystemMemoryRegionsInfo || (m_BaseSnapshot.SystemMemoryRegions.Count <= 0))
                return false;

            if (m_ComparedSnapshot != null && (!m_ComparedSnapshot.HasSystemMemoryRegionsInfo || (m_ComparedSnapshot.SystemMemoryRegions.Count <= 0)))
                return false;

            return true;
        }

        bool HasDetailedResidentMemoryInformation()
        {
            if (!m_BaseSnapshot.HasSystemMemoryResidentPages || (m_BaseSnapshot.SystemMemoryResidentPages.Count <= 0))
                return false;

            if (m_ComparedSnapshot != null && (!m_ComparedSnapshot.HasSystemMemoryResidentPages || (m_ComparedSnapshot.SystemMemoryResidentPages.Count <= 0)))
                return false;

            return true;
        }

        bool HasManagedMemoryInformation()
        {
            if ((m_BaseSnapshot.ManagedHeapSections == null) || (m_BaseSnapshot.ManagedHeapSections.Count <= 0))
                return false;

            if (m_ComparedSnapshot != null && ((m_ComparedSnapshot.ManagedHeapSections == null) || (m_ComparedSnapshot.ManagedHeapSections.Count <= 0)))
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

        void ClearSelection()
        {
            foreach (var item in m_WidgetControllers)
                item.ClearSelection();

            m_SelectionDetails.ClearSelection();
        }
    }
}

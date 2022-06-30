using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    class SummaryViewController : ViewController, ISelectionDetailsProducer
    {
        const string k_UxmlAssetGuid = "63f1db43e50fc4f4288f3d1b1c3d9078";
        const string k_UssClass_Dark = "summary-view__dark";
        const string k_UssClass_Light = "summary-view__light";

#if UNITY_2021_2_OR_NEWER
        public const string PreSnapshotVersion11UpdgradeInfoMemoryOverview = "Make sure to take snapshots with Unity version 2021.2.0a12 or newer, to be able to see the memory overview. See the documentation for more info.";
        public const string PreSnapshotVersion11UpdgradeInfo = "Make sure to upgrade to Unity version 2021.2.0a12 or newer, to be able to utilize this tool to the full extent. See the documentation for more info.";
#elif UNITY_2021_1_OR_NEWER
        public const string PreSnapshotVersion11UpdgradeInfoMemoryOverview = "Make sure to take snapshots with Unity version 2021.1.9f1 or newer, to be able to see the memory overview. See the documentation for more info.";
        public const string PreSnapshotVersion11UpdgradeInfo = "Make sure to upgrade to Unity version 2021.1.9f1 or newer, to be able to utilize this tool to the full extent. See the documentation for more info.";
#elif UNITY_2020_1_OR_NEWER
        public const string PreSnapshotVersion11UpdgradeInfoMemoryOverview = "Make sure to take snapshots with Unity version 2020.3.12f1 or newer, to be able to see the memory overview. See the documentation for more info.";
        public const string PreSnapshotVersion11UpdgradeInfo = "Make sure to upgrade to Unity version 2020.3.12f1 or newer, to be able to utilize this tool to the full extent. See the documentation for more info.";
#else
        public const string PreSnapshotVersion11UpdgradeInfoMemoryOverview = "Make sure to take snapshots with Unity version 2019.4.29f1 or newer, to be able to see the memory overview. See the documentation for more info.";
        public const string PreSnapshotVersion11UpdgradeInfo = "Make sure to upgrade to Unity version 2019.4.29f1 or newer, to be able to utilize this tool to the full extent. See the documentation for more info.";
#endif
        public const string MemoryUsageUnavailableMessage = "The Memory Usage Overview is not available with this snapshot.\n" + PreSnapshotVersion11UpdgradeInfoMemoryOverview;

        // Model.
        readonly CachedSnapshot m_BaseSnapshot;
        readonly CachedSnapshot m_ComparedSnapshot;

        // View.
        VisualElement m_SummaryUnavailable;
        Label m_SummaryUnavailableMessage;

        VisualElement m_IssuesBox;

        Toggle m_NormalizedToggle;
        VisualElement m_ResidentMemoryBreakdown;
        VisualElement m_CommittedMemoryBreakdown;
        VisualElement m_ManagedMemoryBreakdown;
        VisualElement m_UnityObjectsBreakdown;

        List<IMemoryBreakdownViewController> m_WidgetControllers;

        // Needed for selection callback registration & unregistration
        MemoryProfilerWindow m_Window;

        public SummaryViewController(CachedSnapshot baseSnapshot, CachedSnapshot comparedSnapshot)
        {
            m_BaseSnapshot = baseSnapshot;
            m_ComparedSnapshot = comparedSnapshot;

            m_WidgetControllers = new List<IMemoryBreakdownViewController>();
        }

        public event Action<MemorySampleSelection> SelectionChangedEvt = delegate { };

        public AnalysisTabBarController TabController { private get; set; }

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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_Window?.UIState.CustomSelectionDetailsFactory.DeregisterCustomDetailsDrawer(MemorySampleSelectionType.HighlevelBreakdownElement, this);
            }

            base.Dispose(disposing);
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
            m_SummaryUnavailableMessage.text = MemoryUsageUnavailableMessage;

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
            AddController(
                m_ResidentMemoryBreakdown,
                isSupportedSnapshots && HasResidentMemoryInformation(),
                new DeviceMemoryBreakdownViewController(new DeviceMemoryBreakdownModelBuilder(m_BaseSnapshot, m_ComparedSnapshot))
                {
                    Normalized = normalizedSetting
                });

            AddController(
                m_CommittedMemoryBreakdown,
                isSupportedSnapshots,
                new MemoryBreakdownViewController(new AllProcessMemoryBreakdownModelBuilder(m_BaseSnapshot, m_ComparedSnapshot))
                {
                    TotalLabelFormat = "Total Committed: {0}",
                    InspectAction = comapreMode ? null : TabController.MakePageSelector("All Of Memory"),
                    Normalized = normalizedSetting
                });

            AddController(
                m_ManagedMemoryBreakdown,
                isSupportedSnapshots && HasManagedMemoryInformation(),
                new MemoryBreakdownViewController(new ManagedMemoryBreakdownModelBuilder(m_BaseSnapshot, m_ComparedSnapshot))
                {
                    TotalLabelFormat = "Total: {0}",
                    InspectAction = TabController.MakePageSelector("All Of Memory"),
                    Normalized = normalizedSetting
                });

            AddController(
                m_UnityObjectsBreakdown,
                isSupportedSnapshots && HasNativeObjectsInformation(),
                new MemoryBreakdownViewController(new UnityObjectsMemoryBreakdownModelBuilder(m_BaseSnapshot, m_ComparedSnapshot))
                {
                    TotalLabelFormat = "Total: {0}",
                    InspectAction = TabController.MakePageSelector("Unity Objects"),
                    Normalized = normalizedSetting
                });

            // Selection handling
            m_Window = EditorWindow.GetWindow<MemoryProfilerWindow>();
            m_Window.UIState.SelectionChanged += OnSelectionChanged;
            SelectionChangedEvt += m_Window.UIState.RegisterSelectionChangeEvent;

            // Moved here for now to avoid duplicate registration. Please delete with rework.
            m_Window.UIState.CustomSelectionDetailsFactory.RegisterCustomDetailsDrawer(MemorySampleSelectionType.HighlevelBreakdownElement, this);
        }

        void AddController<T>(VisualElement root, bool state, T controller) where T : ViewController, IMemoryBreakdownViewController
        {
            if (state)
            {
                var controllerId = m_WidgetControllers.Count;
                controller.OnRowSelected += (model, rowId) => { SelectionChangedEvt(new MemorySampleSelection(model.Title, controllerId, rowId)); };
                controller.OnRowDeselected += OnBreakdownElementDeselected;
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

        void OnBreakdownElementDeselected(MemoryBreakdownModel model, int rowId)
        {
            SelectionChangedEvt(MemorySampleSelection.InvalidMainSelection);
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

        public void OnSelectionChanged(MemorySampleSelection selection)
        {
            for (int i = 0; i < m_WidgetControllers.Count; i++)
            {
                if ((selection.Type != MemorySampleSelectionType.HighlevelBreakdownElement) || (selection.SecondaryItemIndex != i))
                    m_WidgetControllers[i].ClearSelection();
            }
        }

        public void OnShowDetailsForSelection(ISelectedItemDetailsUI mainUI, MemorySampleSelection selectedItem)
        {
            throw new NotImplementedException();
        }

        public void OnShowDetailsForSelection(ISelectedItemDetailsUI mainUI, MemorySampleSelection selectedItem, out string summary)
        {
            m_WidgetControllers[(int)selectedItem.SecondaryItemIndex].GetRowDescription((int)selectedItem.RowIndex, out var itemName, out var itemDescrption, out var documentationUrl);
            mainUI.SetItemName($"{selectedItem.Table} : {itemName}");
            mainUI.SetDescription(itemDescrption);
            mainUI.SetDocumentationURL(documentationUrl);
            summary = $"{selectedItem.Table} : {itemName}";
        }

        public void OnClearSelectionDetails(ISelectedItemDetailsUI detailsUI)
        {
        }
    }
}

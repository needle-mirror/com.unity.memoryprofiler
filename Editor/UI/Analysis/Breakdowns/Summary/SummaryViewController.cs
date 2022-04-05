using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    // TODO I have encapsulated the legacy Summary UI into a view controller. Once it's ready, we can replace all legacy UI in here with the reworked view.
    class SummaryViewController : ViewController
    {
        const string k_UxmlAssetGuid = "63f1db43e50fc4f4288f3d1b1c3d9078";
        const string k_UssClass_Dark = "summary-view__dark";
        const string k_UssClass_Light = "summary-view__light";

        // Model.
        CachedSnapshot m_BaseSnapshot;
        CachedSnapshot m_ComparedSnapshot;

        // View.
        //---Legacy summary page. Replace with reworked view.
        MemoryUsageSummary m_MemoryUsageSummary;
        TopIssues m_TopIssues;
        //---

        // Please delete with rework.
        MemoryProfilerWindow m_Window;

        public SummaryViewController(CachedSnapshot snapshot) : this(snapshot, null) { }

        public SummaryViewController(CachedSnapshot baseSnapshot, CachedSnapshot comparedSnapshot)
        {
            m_BaseSnapshot = baseSnapshot;
            m_ComparedSnapshot = comparedSnapshot;
        }

        bool IsComparingSnapshots => m_ComparedSnapshot != null;

        protected override VisualElement LoadView()
        {
            var view = ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlAssetGuid);
            if (view == null)
                throw new InvalidOperationException("Unable to create view from Uxml. Uxml must contain at least one child element.");
            view.style.flexGrow = 1;

            var themeUssClass = (EditorGUIUtility.isProSkin) ? k_UssClass_Dark : k_UssClass_Light;
            view.AddToClassList(themeUssClass);

            GatherReferencesInView(view);
            
            //---Legacy summary page. Replace with reworked view.
            var memoryUsageSummaryContainer = view.Q("MemoryUsageSummary");
            m_Window = EditorWindow.GetWindow<MemoryProfilerWindow>();
            m_MemoryUsageSummary = new MemoryUsageSummary(memoryUsageSummaryContainer, m_Window);
            m_Window.UIState.SelectionChanged += m_MemoryUsageSummary.OnSelectionChanged;
            m_MemoryUsageSummary.SelectionChangedEvt += m_Window.UIState.RegisterSelectionChangeEvent;

            // Moved here for now to avoid duplicate registration. Please delete with rework.
            m_Window.UIState.CustomSelectionDetailsFactory.RegisterCustomDetailsDrawer(MemorySampleSelectionType.HighlevelBreakdownElement, m_MemoryUsageSummary);

            m_TopIssues = new TopIssues(view.Q("top-ten-issues-section"));
            //---

            return view;
        }

        protected override void ViewLoaded()
        {
            base.ViewLoaded();

            //---Legacy summary page. Replace with reworked view.
            if (IsComparingSnapshots)
            {
                m_MemoryUsageSummary.SetSummaryValues(m_BaseSnapshot, m_ComparedSnapshot);
                m_TopIssues.InitializeIssues(m_BaseSnapshot, m_ComparedSnapshot);
            }
            else
            {
                m_MemoryUsageSummary.SetSummaryValues(m_BaseSnapshot);
                m_TopIssues.InitializeIssues(m_BaseSnapshot);
            }
            //---
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Please delete with rework.
                if (m_Window != null)
                    m_Window.UIState.CustomSelectionDetailsFactory.DeregisterCustomDetailsDrawer(
                        MemorySampleSelectionType.HighlevelBreakdownElement,
                        m_MemoryUsageSummary);
            }

            base.Dispose(disposing);
        }

        void GatherReferencesInView(VisualElement view)
        {

        }
    }
}

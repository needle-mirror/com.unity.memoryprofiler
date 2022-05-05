#if UNITY_2022_1_OR_NEWER
using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Abstract base view controller for memory breakdown views, such as 'Unity Objects' and 'All Of Memory'.
    abstract class BreakdownViewController : ViewController
    {
        const string k_UxmlAssetGuid = "01be1ca7b1544f246b5302f05d9e8c5e";
        const string k_UssClass_Dark = "breakdown-view__dark";
        const string k_UssClass_Light = "breakdown-view__light";
        const string k_UxmlIdentifier_DescriptionLabel = "breakdown-view__description-label";
        const string k_UxmlIdentifier_SearchField = "breakdown-view__search-field";
        const string k_UxmlIdentifier_TableSizeBar = "breakdown-view__table-size-bar";
        const string k_UxmlIdentifier_TableContainer = "breakdown-view__table-container";

        // Model.
        readonly string m_Description;

        // View.
        Label m_DescriptionLabel;
        DetailedSizeBar m_TableSizeBar;

        public BreakdownViewController(CachedSnapshot snapshot, string description)
        {
            Snapshot = snapshot;
            m_Description = description;
        }

        // Model.
        protected CachedSnapshot Snapshot { get; }

        // View.
        protected ToolbarSearchField SearchField { get; private set; }
        protected VisualElement TableContainer { get; private set; }

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
            m_DescriptionLabel.text = m_Description;
            SearchField.RegisterCallback<FocusOutEvent>(OnSearchFocusLost);
        }

        protected void RefreshTableSizeBar(
            ulong totalMemorySize,
            ulong totalSnapshotMemorySize)
        {
            var progress = (float)totalMemorySize / totalSnapshotMemorySize;
            m_TableSizeBar.SetRelativeSize(progress);

            var totalMemorySizeText = EditorUtility.FormatBytes((long)totalMemorySize);
            m_TableSizeBar.SetSizeText($"Total Memory In Table: {totalMemorySizeText}");

            var totalSnapshotMemorySizeText = EditorUtility.FormatBytes((long)totalSnapshotMemorySize);
            m_TableSizeBar.SetTotalText($"Total Memory In Snapshot: {totalSnapshotMemorySizeText}");
        }

        void GatherReferencesInView(VisualElement view)
        {
            m_DescriptionLabel = view.Q<Label>(k_UxmlIdentifier_DescriptionLabel);
            SearchField = view.Q<ToolbarSearchField>(k_UxmlIdentifier_SearchField);
            m_TableSizeBar = view.Q<DetailedSizeBar>(k_UxmlIdentifier_TableSizeBar);
            TableContainer = view.Q<VisualElement>(k_UxmlIdentifier_TableContainer);
        }

        void OnSearchFocusLost(FocusOutEvent evt)
        {
            MemoryProfilerAnalytics.AddInteractionCountToEvent<MemoryProfilerAnalytics.InteractionsInPage, MemoryProfilerAnalytics.PageInteractionType>(
                MemoryProfilerAnalytics.PageInteractionType.SearchInPageWasUsed);
        }
    }
}
#endif

using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class ResidentMemorySummaryViewController : ViewController, IMemorySummaryViewController
    {
        const string k_UxmlAssetGuid = "e0fa9dca4a493bf47b10175e110cac37";
        const string k_UssClass_Dark = "memory-summary__dark";
        const string k_UssClass_Light = "memory-summary__light";

        const string k_UxmlClass = "memory-summary-resident-view";
        const string k_UxmlHeaderTitle = "memory-summary__header__title";
        const string k_UxmlHeaderDescription = "memory-summary__description";
        const string k_UxmlHeaderInspectButton = "memory-summary__header__inspect-button";
        const string k_UxmlWarningMsg = "memory-summary__warning";
        const string k_UxmlWarningMsgLabel = "memory-summary__warning__label";
        const string k_UxmlBreakdownBar = "memory-summary__bars";
        const string k_UxmlLegendTable = "memory-summary__legend";

        const string k_UxmlDetailsPageAssetGuid = "72d11bd2c44a8c24195879efaa3af5a3";

        const string k_TotalLabelFormat = "Total Allocated: {0}";

        // Model
        readonly IMemorySummaryModelBuilder<MemorySummaryModel> m_Builder;
        MemorySummaryModel m_Model;

        // View
        Label m_Title;
        Label m_Description;
        Button m_InspectBtn;
        VisualElement m_WarningMsg;
        Label m_WarningMsgLabel;
        Label m_TotalLabelSpacer;
        VisualElement m_LegendTable;
        VisualElement m_BreakdownBars;

        MemorySummaryBarViewController m_BreakdowBarViewController;
        MemorySummaryLegendViewController m_LegendTableViewController;

        // State
        bool m_NormalizeBars;
        bool m_SelectionEnabled;

        public event Action<MemorySummaryModel, int> OnRowSelected;
        public event Action<MemorySummaryModel, int, bool> OnRowHovered;

        public ResidentMemorySummaryViewController(IMemorySummaryModelBuilder<MemorySummaryModel> builder)
        {
            m_Builder = builder;

            m_NormalizeBars = false;
            m_SelectionEnabled = true;
        }

        public bool Selectable
        {
            get => m_SelectionEnabled;
            set
            {
                m_SelectionEnabled = value;
                ClearSelection();
            }
        }

        public bool Normalized
        {
            get => m_NormalizeBars;
            set
            {
                m_NormalizeBars = value;
                if (m_BreakdowBarViewController != null)
                    m_BreakdowBarViewController.Normalize = value;
            }
        }

        public void ClearSelection()
        {
            m_LegendTableViewController?.SetRowSelected(0, false);
            m_BreakdowBarViewController?.SetCellSelected(0, false);
        }

        public ViewController MakeSelection(int index)
        {
            if (index >= m_Model.Rows.Count)
                throw new ArgumentException($"Item index out of range. Expected smaller than {m_Model.Rows.Count}, got {index}.");

            return new PageDetailsViewController(k_UxmlDetailsPageAssetGuid);
        }

        protected override VisualElement LoadView()
        {
            var view = ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlAssetGuid);
            if (view == null)
                throw new InvalidOperationException("Unable to create view from Uxml. Uxml must contain at least one child element.");

            var themeUssClass = (EditorGUIUtility.isProSkin) ? k_UssClass_Dark : k_UssClass_Light;
            view.AddToClassList(themeUssClass);

            return view;
        }

        protected override void ViewLoaded()
        {
            GatherViewReferences();

            BuildModel();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            m_LegendTableViewController?.Dispose();
            m_BreakdowBarViewController?.Dispose();
        }

        void GatherViewReferences()
        {
            m_Title = View.Q<Label>(k_UxmlHeaderTitle);
            m_Description = View.Q<Label>(k_UxmlHeaderDescription);
            m_InspectBtn = View.Q<Button>(k_UxmlHeaderInspectButton);
            m_WarningMsg = View.Q<VisualElement>(k_UxmlWarningMsg);
            m_WarningMsgLabel = View.Q<Label>(k_UxmlWarningMsgLabel);
            m_LegendTable = View.Q(k_UxmlLegendTable);
            m_BreakdownBars = View.Q(k_UxmlBreakdownBar);
            View.AddToClassList(k_UxmlClass);
        }

        public void Update()
        {
            BuildModel();
        }

        void BuildModel()
        {
            m_Model = m_Builder.Build();
            RefreshView();
        }

        void RefreshView()
        {
            // Set header title
            m_Title.text = m_Model.Title;
            m_Description.text = m_Model.Description;

            // Disable inspect button as resident memory view doesn't have detailed view
            UIElementsHelper.SetVisibility(m_InspectBtn, false);

            // Show/Hide resident memory warning
            UIElementsHelper.SetVisibility(m_WarningMsg, !string.IsNullOrEmpty(m_Model.ResidentMemoryWarning));
            m_WarningMsgLabel.text = m_Model.ResidentMemoryWarning;

            // Breakdown bar
            if (m_BreakdowBarViewController != null)
            {
                m_TotalLabelSpacer.text = string.Format(k_TotalLabelFormat, EditorUtility.FormatBytes((long)m_Model.TotalA));
                m_BreakdowBarViewController.Update(m_Model);
                m_LegendTableViewController.Update(m_Model);
            }
            else
            {
                m_BreakdowBarViewController = new MemorySummaryBarViewController(m_Model)
                {
                    TotalLabelFormat = k_TotalLabelFormat,
                    Normalize = m_NormalizeBars,
                    ShowSnapshotLabels = true,
                    ShowResidentMemory = true,
                    UseResidentSizeAsSourceInTooltips = true,
                };
                m_BreakdowBarViewController.OnRowHovered += RowHovered;
                m_BreakdowBarViewController.OnRowClicked += RowClicked;
                AddChild(m_BreakdowBarViewController);
                m_BreakdownBars.Add(m_BreakdowBarViewController.View);

                // Legend table
                m_LegendTableViewController = new MemorySummaryLegendViewController(m_Model)
                {
                    ShowResidentSize = true,
                    UseResidentAsSource = true,
                };
                m_LegendTableViewController.OnRowHovered += RowHovered;
                m_LegendTableViewController.OnRowClicked += RowClicked;
                AddChild(m_LegendTableViewController);
                m_LegendTable.Add(m_LegendTableViewController.View);

                // Breakdown bar total text is made as position absolute to position it correctly
                // in compare mode. So, to make UI shrink correctly we need to add an invisible
                // spacer that will prevent widget from shrinking too much
                m_TotalLabelSpacer = new Label();
                m_TotalLabelSpacer.visible = false;
                m_TotalLabelSpacer.text = string.Format(k_TotalLabelFormat, EditorUtility.FormatBytes((long)m_Model.TotalA));
                m_LegendTable.Add(m_TotalLabelSpacer);
            }

            View.RegisterCallback<GeometryChangedEvent>(ViewLayoutPerformedAfterRefresh);
        }

        void ViewLayoutPerformedAfterRefresh(GeometryChangedEvent evt)
        {
            View.UnregisterCallback<GeometryChangedEvent>(ViewLayoutPerformedAfterRefresh);

            // Ensure that the breakdown bar's total label can never overlap with the legend below.
            var legendWidth = m_LegendTableViewController.View.contentRect.width;
            m_BreakdowBarViewController.SetMinimumWidthExcludingTotalLabel(legendWidth);
        }

        void RowHovered(int index, bool state)
        {
            m_LegendTableViewController.SetRowHovered(0, state);
            m_BreakdowBarViewController.SetCellHovered(0, state);

            OnRowHovered?.Invoke(m_Model, index, state);
        }

        void RowClicked(int index)
        {
            if (!m_SelectionEnabled)
                return;

            ClearSelection();

            m_LegendTableViewController.SetRowSelected(0, true);
            m_BreakdowBarViewController.SetCellSelected(0, true);

            OnRowSelected?.Invoke(m_Model, 0);
        }
    }
}

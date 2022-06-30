using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class DeviceMemoryBreakdownViewController : ViewController, IMemoryBreakdownViewController
    {
        const string k_UxmlAssetGuid = "e0fa9dca4a493bf47b10175e110cac37";

        const string k_UxmlHeaderTitle = "memory-usage-breakdown__header__title";
        const string k_UxmlHeaderInspectButton = "memory-usage-breakdown__header__inspect-button";
        const string k_UxmlBreakdownBar = "memory-usage-breakdown__bars";
        const string k_UxmlLegendTable = "memory-usage-breakdown__legend";

        const string k_TotalLabelFormat = "Max Available: {0}";

        // Model
        readonly IMemoryBreakdownModelBuilder<DeviceMemoryBreakdownModel> m_Builder;
        DeviceMemoryBreakdownModel m_Model;

        // View
        Label m_Title;
        Button m_InspectBtn;
        VisualElement m_LegendTable;
        VisualElement m_BreakdownBars;

        MemoryBreakdownBarViewController m_BreakdowBarViewController;
        MemoryBreakdownLegendViewController m_LegendTableViewController;

        // State
        bool m_NormalizeBars;
        bool m_SelectionEnabled;

        public event Action<MemoryBreakdownModel, int> OnRowSelected = delegate { };
        public event Action<MemoryBreakdownModel, int> OnRowDeselected = delegate { };
        public event Action<MemoryBreakdownModel> OnInspectDetails = delegate { };

        public DeviceMemoryBreakdownViewController(IMemoryBreakdownModelBuilder<DeviceMemoryBreakdownModel> builder)
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

        public void GetRowDescription(int index, out string name, out string descr, out string docsUrl)
        {
            name = m_Model.Rows[0].Name;
            descr = m_Model.Rows[0].Description;
            docsUrl = m_Model.Rows[0].DocumentationUrl;
        }

        protected override VisualElement LoadView()
        {
            var view = ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlAssetGuid);
            if (view == null)
                throw new InvalidOperationException("Unable to create view from Uxml. Uxml must contain at least one child element.");

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
        }

        void GatherViewReferences()
        {
            m_Title = View.Q<Label>(k_UxmlHeaderTitle);
            m_InspectBtn = View.Q<Button>(k_UxmlHeaderInspectButton);
            m_LegendTable = View.Q(k_UxmlLegendTable);
            m_BreakdownBars = View.Q(k_UxmlBreakdownBar);
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

            // Disable inspect button as resident memory view doesn't have detailed view
            UIElementsHelper.SetVisibility(m_InspectBtn, false);

            // Breakdown bar
            m_BreakdowBarViewController = new DeviceMemoryBreakdownBarViewController(m_Model)
            {
                TotalLabelFormat = k_TotalLabelFormat,
                Normalize = m_NormalizeBars,
            };
            m_BreakdowBarViewController.OnRowHovered += OnRowHovered;
            m_BreakdowBarViewController.OnRowClicked += OnRowClicked;
            AddChild(m_BreakdowBarViewController);
            m_BreakdownBars.Add(m_BreakdowBarViewController.View);

            // Legend table
            m_LegendTableViewController = new MemoryBreakdownLegendViewController(m_Model);
            m_LegendTableViewController.OnRowHovered += OnRowHovered;
            m_LegendTableViewController.OnRowClicked += OnRowClicked;
            AddChild(m_LegendTableViewController);
            m_LegendTable.Add(m_LegendTableViewController.View);

            // Breakdown bar total text is made as position absolute to position it correctly
            // in compare mode. So, to make UI shrink correctly we need to add an invisible
            // spacer that will prevent widget from shrinking too much
            var totalLabelSpacer = new Label();
            totalLabelSpacer.text = string.Format(k_TotalLabelFormat, EditorUtility.FormatBytes((long)m_Model.TotalA));
            totalLabelSpacer.visible = false;
            m_LegendTable.Add(totalLabelSpacer);
        }

        void OnRowHovered(int index, bool state)
        {
            m_LegendTableViewController.SetRowHovered(0, state);
            m_BreakdowBarViewController.SetCellHovered(0, state);
        }

        void OnRowClicked(int index)
        {
            if (!m_SelectionEnabled)
                return;

            ClearSelection();

            m_LegendTableViewController.SetRowSelected(0, true);
            m_BreakdowBarViewController.SetCellSelected(0, true);

            OnRowSelected(m_Model, 0);
        }
    }
}

using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class MemoryBreakdownViewController : ViewController, IMemoryBreakdownViewController
    {
        const string k_UxmlAssetGuid = "e0fa9dca4a493bf47b10175e110cac37";
        const string k_UssClass_Dark = "memory-usage-breakdown__dark";
        const string k_UssClass_Light = "memory-usage-breakdown__light";

        const string k_UxmlHeaderTitle = "memory-usage-breakdown__header__title";
        const string k_UxmlHeaderInspectButton = "memory-usage-breakdown__header__inspect-button";
        const string k_UxmlBreakdownBar = "memory-usage-breakdown__bars";
        const string k_UxmlLegendTable = "memory-usage-breakdown__legend";

        const string k_BreakdownTotalFormatString = "Total: {0}";

        // Model
        readonly IMemoryBreakdownModelBuilder<MemoryBreakdownModel> m_Builder;
        MemoryBreakdownModel m_Model;
        string m_TotalLabelFormat;

        // View
        Label m_Title;
        Button m_InspectBtn;
        VisualElement m_LegendTable;
        VisualElement m_BreakdownBars;

        MemoryBreakdownBarViewController m_BreakdowBarViewController;
        MemoryBreakdownLegendViewController m_LegendTableViewController;

        // State
        int m_SelectedRow;
        bool m_NormalizeBars;
        bool m_SelectionEnabled;

        public event Action<MemoryBreakdownModel, int> OnRowSelected = delegate { };
        public event Action<MemoryBreakdownModel, int> OnRowDeselected = delegate { };
        public event Action<MemoryBreakdownModel> OnInspectDetails = delegate { };

        public MemoryBreakdownViewController(IMemoryBreakdownModelBuilder<MemoryBreakdownModel> builder)
        {
            m_Builder = builder;

            m_SelectedRow = -1;
            m_NormalizeBars = false;
            m_SelectionEnabled = true;
            m_TotalLabelFormat = k_BreakdownTotalFormatString;
        }

        public Action InspectAction { get; set; }

        public string TotalLabelFormat
        {
            get => m_TotalLabelFormat;
            set
            {
                m_TotalLabelFormat = value;
                if (m_BreakdowBarViewController != null)
                    m_BreakdowBarViewController.TotalLabelFormat = m_TotalLabelFormat;
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

        public bool Selectable
        {
            get => m_SelectionEnabled;
            set
            {
                m_SelectionEnabled = value;
                ClearSelection();
            }
        }

        public void ClearSelection()
        {
            if (m_SelectedRow == -1)
                return;

            m_LegendTableViewController.SetRowSelected(m_SelectedRow, false);
            m_BreakdowBarViewController.SetCellSelected(m_SelectedRow, false);
            m_SelectedRow = -1;
        }

        public void GetRowDescription(int index, out string name, out string descr, out string docsUrl)
        {
            if (index >= m_Model.Rows.Count)
                throw new ArgumentException($"Item index out of range. Expected smaller than {m_Model.Rows.Count}, got {index}.");

            name = m_Model.Rows[index].Name;
            descr = m_Model.Rows[index].Description;
            docsUrl = m_Model.Rows[index].DocumentationUrl;
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

            // Pre-sort data
            if (!m_Model.CompareMode)
                m_Model.Sort(MemoryBreakdownModel.SortableItemDataProperty.SnapshotA, MemoryBreakdownModel.SortDirection.Descending);
            else
                m_Model.Sort(MemoryBreakdownModel.SortableItemDataProperty.Difference, MemoryBreakdownModel.SortDirection.Descending);

            RefreshView();
        }

        void RefreshView()
        {
            // Set header title and inspect button name
            m_Title.text = m_Model.Title;

            UIElementsHelper.SetVisibility(m_InspectBtn, InspectAction != null);
            m_InspectBtn.clicked += () => { InspectAction?.Invoke(); };

            // Breakdown bar
            m_BreakdowBarViewController = new MemoryBreakdownBarViewController(m_Model)
            {
                TotalLabelFormat = m_TotalLabelFormat,
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
            if (m_TotalLabelFormat != null)
            {
                var totalLabelSpacer = new Label();
                totalLabelSpacer.text = string.Format(m_TotalLabelFormat, EditorUtility.FormatBytes((long)m_Model.TotalA));
                totalLabelSpacer.visible = false;
                m_LegendTable.Add(totalLabelSpacer);
            }
        }

        void OnRowHovered(int index, bool state)
        {
            m_LegendTableViewController.SetRowHovered(index, state);
            m_BreakdowBarViewController.SetCellHovered(index, state);
        }

        void OnRowClicked(int index)
        {
            if (!m_SelectionEnabled)
                return;

            ClearSelection();

            m_SelectedRow = index;
            m_LegendTableViewController.SetRowSelected(m_SelectedRow, true);
            m_BreakdowBarViewController.SetCellSelected(m_SelectedRow, true);
            OnRowSelected(m_Model, m_SelectedRow);
        }
    }
}

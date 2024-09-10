using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class GenericMemorySummaryViewController : ViewController, IMemorySummaryViewController
    {
        const string k_UxmlAssetGuid = "e0fa9dca4a493bf47b10175e110cac37";
        const string k_UssClass_Dark = "memory-summary__dark";
        const string k_UssClass_Light = "memory-summary__light";

        const string k_UxmlHeaderTitle = "memory-summary__header__title";
        const string k_UxmlHeaderDescription = "memory-summary__description";
        const string k_UxmlHeaderInspectButton = "memory-summary__header__inspect-button";
        const string k_UxmlWarningMsg = "memory-summary__warning";
        const string k_UxmlBreakdownBar = "memory-summary__bars";
        const string k_UxmlLegendTable = "memory-summary__legend";

        const string k_BreakdownTotalFormatString = "Total: {0}";

        // Model
        readonly IMemorySummaryModelBuilder<MemorySummaryModel> m_Builder;
        MemorySummaryModel m_Model;
        string m_TotalLabelFormat;

        // View
        Label m_Title;
        Label m_Description;
        Button m_InspectBtn;
        VisualElement m_WarningMsg;
        VisualElement m_LegendTable;
        VisualElement m_BreakdownBars;
        Label m_TotalLabelSpacer;

        MemorySummaryBarViewController m_BreakdowBarViewController;
        MemorySummaryLegendViewController m_LegendTableViewController;

        // State
        int m_SelectedRow;
        bool m_NormalizeBars;
        bool m_ShowResidentMemory;
        bool m_SelectionEnabled;
        readonly bool m_ShowResidentSize; // Controls visibility of resident memory in cells and tooltips

        public event Action<MemorySummaryModel, int> OnRowSelected;
        public event Action<MemorySummaryModel, int, bool> OnRowHovered;
        public event Action<MemorySummaryModel, int> OnRowDoubleClick;

        public GenericMemorySummaryViewController(IMemorySummaryModelBuilder<MemorySummaryModel> builder, bool showResidentSize)
        {
            m_Builder = builder;

            m_SelectedRow = -1;
            m_NormalizeBars = false;
            m_ShowResidentMemory = false;
            m_SelectionEnabled = true;
            m_ShowResidentSize = showResidentSize;
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
                if (m_NormalizeBars == value)
                    return;

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

        public bool ShowResidentBars
        {
            get => m_ShowResidentMemory;
            set
            {
                if (m_ShowResidentMemory == value)
                    return;

                m_ShowResidentMemory = value;
                m_BreakdowBarViewController.ShowResidentMemory = value;

            }
        }

        public bool ForceShowResidentBars
        {
            get => m_BreakdowBarViewController.ForceShowResidentBars;
            set => m_BreakdowBarViewController.ForceShowResidentBars = value;
        }

        public void ClearSelection()
        {
            if (m_SelectedRow == -1)
                return;

            m_LegendTableViewController.SetRowSelected(m_SelectedRow, false);
            m_BreakdowBarViewController.SetCellSelected(m_SelectedRow, false);
            m_SelectedRow = -1;
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
            m_LegendTable = View.Q(k_UxmlLegendTable);
            m_BreakdownBars = View.Q(k_UxmlBreakdownBar);
        }

        public void Update()
        {
            BuildModel();
        }

        void BuildModel()
        {
            m_Model = m_Builder.Build();

            // Pre-sort data
            if (!m_Model.CompareMode)
                m_Model.Sort(MemorySummaryModel.SortableItemDataProperty.BaseSnapshot, MemorySummaryModel.SortDirection.Descending);
            else
                m_Model.Sort(MemorySummaryModel.SortableItemDataProperty.Difference, MemorySummaryModel.SortDirection.Descending);

            RefreshView();
        }

        void RefreshView()
        {
            // Setup header title, description and inspect button
            m_Title.text = m_Model.Title;

            if (!String.IsNullOrEmpty(m_Model.Description))
            {
                m_Description.text = m_Model.Description;
                UIElementsHelper.SetVisibility(m_Description, true);
            }
            else
                UIElementsHelper.SetVisibility(m_Description, false);

            UIElementsHelper.SetVisibility(m_InspectBtn, InspectAction != null);

            // Generic view doesn't have warnings
            UIElementsHelper.SetVisibility(m_WarningMsg, false);

            if (m_BreakdowBarViewController != null)
            {
                m_BreakdowBarViewController.Update(m_Model);
                m_LegendTableViewController.Update(m_Model);
            }
            else
            {
                m_InspectBtn.clicked += () => { InspectAction?.Invoke(); };

                // Breakdown bar
                m_BreakdowBarViewController = new MemorySummaryBarViewController(m_Model)
                {
                    TotalLabelFormat = m_TotalLabelFormat,
                    Normalize = m_NormalizeBars,
                    ShowResidentMemory = m_ShowResidentSize,
                };
                m_BreakdowBarViewController.OnRowHovered += RowHovered;
                m_BreakdowBarViewController.OnRowClicked += RowClicked;
                m_BreakdowBarViewController.OnRowDoubleClicked += (e) => OnRowDoubleClick?.Invoke(m_Model, e);
                AddChild(m_BreakdowBarViewController);
                m_BreakdownBars.Add(m_BreakdowBarViewController.View);

                // Legend table
                m_LegendTableViewController = new MemorySummaryLegendViewController(m_Model)
                {
                    ShowResidentSize = m_ShowResidentSize,
                };
                m_LegendTableViewController.OnRowHovered += RowHovered;
                m_LegendTableViewController.OnRowClicked += RowClicked;
                m_LegendTableViewController.OnRowDoubleClicked += (e) => OnRowDoubleClick?.Invoke(m_Model, e);
                AddChild(m_LegendTableViewController);
                m_LegendTable.Add(m_LegendTableViewController.View);

                // Breakdown bar total text is made as position absolute to position it correctly
                // in compare mode. So, to make UI shrink correctly we need to add an invisible
                // spacer that will prevent widget from shrinking too much
                if (m_TotalLabelSpacer == null)
                {
                    m_TotalLabelSpacer = new Label();
                    m_TotalLabelSpacer.visible = false;
                    m_LegendTable.Add(m_TotalLabelSpacer);
                }
            }

            if (m_TotalLabelFormat != null)
            {
                m_TotalLabelSpacer.text = string.Format(m_TotalLabelFormat, EditorUtility.FormatBytes((long)m_Model.TotalA));
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
            OnRowHovered?.Invoke(m_Model, index, state);

            m_LegendTableViewController.SetRowHovered(index, state);
            m_BreakdowBarViewController.SetCellHovered(index, state);
        }

        void RowClicked(int index)
        {
            if (!m_SelectionEnabled)
                return;

            ClearSelection();

            m_SelectedRow = index;
            m_LegendTableViewController.SetRowSelected(m_SelectedRow, true);
            m_BreakdowBarViewController.SetCellSelected(m_SelectedRow, true);
            OnRowSelected?.Invoke(m_Model, m_SelectedRow);
        }

        public ViewController MakeSelection(int index)
        {
            if (index >= m_Model.Rows.Count)
                throw new ArgumentException($"Item index out of range. Expected smaller than {m_Model.Rows.Count}, got {index}.");

            var name = m_Model.Rows[index].Name;
            var descr = m_Model.Rows[index].Description;
            var docsUrl = m_Model.Rows[index].DocumentationUrl;

            return new SimpleDetailsViewController(name, descr, docsUrl);
        }
    }
}

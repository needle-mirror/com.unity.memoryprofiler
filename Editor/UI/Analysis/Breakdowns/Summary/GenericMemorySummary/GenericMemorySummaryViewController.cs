using System;
using Unity.MemoryProfiler.Editor.UIContentData;
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

        static readonly string k_BreakdownTotalFormatString = TextContent.TotalFormat;

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
        bool m_ShowResidentSizeOnly;
        bool m_DeEmphasizeNonResidentPortion;
        string m_TooltipHint;

        readonly MemorySummaryDataOverrides m_DataOverrides;

        public event Action<MemorySummaryModel, int> OnRowSelected;
        public event Action<MemorySummaryModel, int, bool> OnRowHovered;
        public event Action<MemorySummaryModel, int> OnRowDoubleClick;

        public GenericMemorySummaryViewController(IMemorySummaryModelBuilder<MemorySummaryModel> builder, bool showResidentSize,
            in MemorySummaryDataOverrides dataOverrides = default)
        {
            m_Builder = builder;

            m_SelectedRow = -1;
            m_NormalizeBars = false;
            m_ShowResidentMemory = false;
            m_SelectionEnabled = true;
            m_ShowResidentSize = showResidentSize;
            m_TotalLabelFormat = k_BreakdownTotalFormatString;

            m_DataOverrides = dataOverrides;
        }

        public Action InspectAction { get; set; }

        /// <summary>Optional provider of per-row Inspect actions for the legend and the Selection Details panel.</summary>
        public MemorySummaryRowActionProvider RowActionProvider { get; set; }

        public string TotalLabelFormat
        {
            get => m_TotalLabelFormat;
            set
            {
                m_TotalLabelFormat = value;
                if (m_BreakdowBarViewController != null)
                {
                    m_BreakdowBarViewController.TotalLabelFormat = m_TotalLabelFormat;
                    RefreshTotalLabelSpacer();
                }
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

        /// <summary>
        /// Size the bars by and show the resident portion in the legend, while tooltips still report the
        /// committed size. Potential Savings uses this to show exclusive impact with total impact in the tooltips.
        /// </summary>
        public bool ShowResidentSizeOnly
        {
            get => m_ShowResidentSizeOnly;
            set
            {
                if (m_ShowResidentSizeOnly == value)
                    return;

                // Update() can rebuild the model with fewer rows than the stale selected index.
                if (m_Model != null && m_SelectedRow >= m_Model.Rows.Count)
                    m_SelectedRow = -1;

                // The re-sort below can move the selected row, so reselect it by name afterwards.
                var selectedRowName = m_SelectedRow >= 0 ? m_Model.Rows[m_SelectedRow].Name : null;
                if (selectedRowName != null)
                    ClearSelection();

                m_ShowResidentSizeOnly = value;
                if (m_Model != null)
                {
                    if (!m_Model.CompareMode)
                        m_Model.Sort(MemorySummaryModel.SortableItemDataProperty.BaseSnapshot, MemorySummaryModel.SortDirection.Descending, m_ShowResidentSizeOnly);
                    else
                        m_Model.Sort(MemorySummaryModel.SortableItemDataProperty.Difference, MemorySummaryModel.SortDirection.Descending, m_ShowResidentSizeOnly);
                }
                if (m_BreakdowBarViewController != null)
                {
                    m_BreakdowBarViewController.ShowResidentSizeOnly = value;
                    m_LegendTableViewController.ShowResidentSizeOnly = value;
                }

                if (selectedRowName != null)
                {
                    var newIndex = m_Model.Rows.FindIndex(row => row.Name == selectedRowName);
                    if (newIndex >= 0)
                    {
                        m_SelectedRow = newIndex;
                        m_LegendTableViewController.SetRowSelected(m_SelectedRow, true);
                        m_BreakdowBarViewController.SetCellSelected(m_SelectedRow, true);
                    }
                }
            }
        }

        /// <summary>Optional line appended to the bar and legend tooltips.</summary>
        public string TooltipHint
        {
            get => m_TooltipHint;
            set
            {
                if (m_TooltipHint == value)
                    return;

                m_TooltipHint = value;
                if (m_BreakdowBarViewController != null)
                {
                    m_BreakdowBarViewController.TooltipHint = value;
                    m_LegendTableViewController.TooltipHint = value;
                }
            }
        }

        /// <summary>See <see cref="MemoryBarElement.DeEmphasizeNonResidentPortion"/>.</summary>
        public bool DeEmphasizeNonResidentPortion
        {
            get => m_DeEmphasizeNonResidentPortion;
            set
            {
                if (m_DeEmphasizeNonResidentPortion == value)
                    return;

                m_DeEmphasizeNonResidentPortion = value;
                if (m_BreakdowBarViewController != null)
                    m_BreakdowBarViewController.DeEmphasizeNonResidentPortion = value;
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

            // Pre-sort by whichever value (Committed or Resident) is displayed.
            if (!m_Model.CompareMode)
                m_Model.Sort(MemorySummaryModel.SortableItemDataProperty.BaseSnapshot, MemorySummaryModel.SortDirection.Descending, m_ShowResidentSizeOnly);
            else
                m_Model.Sort(MemorySummaryModel.SortableItemDataProperty.Difference, MemorySummaryModel.SortDirection.Descending, m_ShowResidentSizeOnly);

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
                m_BreakdowBarViewController = new MemorySummaryBarViewController(m_Model, in m_DataOverrides)
                {
                    TotalLabelFormat = m_TotalLabelFormat,
                    Normalize = m_NormalizeBars,
                    ShowResidentMemory = m_ShowResidentSize,
                    ShowResidentSizeOnly = m_ShowResidentSizeOnly,
                    DeEmphasizeNonResidentPortion = m_DeEmphasizeNonResidentPortion,
                    TooltipHint = m_TooltipHint,
                };
                m_BreakdowBarViewController.OnRowHovered += BarCellHovered;
                m_BreakdowBarViewController.OnRowClicked += RowClicked;
                m_BreakdowBarViewController.OnRowDoubleClicked += (e) => OnRowDoubleClick?.Invoke(m_Model, e);
                AddChild(m_BreakdowBarViewController);
                m_BreakdownBars.Add(m_BreakdowBarViewController.View);

                // Legend table
                m_LegendTableViewController = new MemorySummaryLegendViewController(m_Model, in m_DataOverrides)
                {
                    ShowResidentSize = m_ShowResidentSize,
                    ShowResidentSizeOnly = m_ShowResidentSizeOnly,
                    TooltipHint = m_TooltipHint,
                    RowActionProvider = RowActionProvider,
                };
                m_LegendTableViewController.OnRowHovered += LegendRowHovered;
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

            RefreshTotalLabelSpacer();
            View.RegisterCallback<GeometryChangedEvent>(ViewLayoutPerformedAfterRefresh);
        }

        void RefreshTotalLabelSpacer()
        {
            if (m_TotalLabelFormat == null || m_TotalLabelSpacer == null)
                return;

            m_TotalLabelSpacer.text = m_BreakdowBarViewController.FormatTotalLabel(m_Model.TotalA, m_Model.ResidentTotalA);
        }

        void ViewLayoutPerformedAfterRefresh(GeometryChangedEvent evt)
        {
            View.UnregisterCallback<GeometryChangedEvent>(ViewLayoutPerformedAfterRefresh);

            // Ensure that the breakdown bar's total label can never overlap with the legend below.
            var legendWidth = m_LegendTableViewController.View.contentRect.width;
            m_BreakdowBarViewController.SetMinimumWidthExcludingTotalLabel(legendWidth);
        }

        // A bar hover reveals every bar's split for comparison; a legend row hover only splits its own bar.
        void BarCellHovered(int index, bool state) => RowHovered(index, state, revealSplitOnAllBars: true);

        void LegendRowHovered(int index, bool state) => RowHovered(index, state, revealSplitOnAllBars: false);

        void RowHovered(int index, bool state, bool revealSplitOnAllBars)
        {
            OnRowHovered?.Invoke(m_Model, index, state);

            m_LegendTableViewController.SetRowHovered(index, state);
            m_BreakdowBarViewController.SetCellHovered(index, state, revealSplitOnAllBars);
        }

        internal void RowClicked(int index)
        {
            if (!m_SelectionEnabled)
                return;

            ClearSelection();

            m_SelectedRow = index;
            m_LegendTableViewController.SetRowSelected(m_SelectedRow, true);
            m_BreakdowBarViewController.SetCellSelected(m_SelectedRow, true);
            OnRowSelected?.Invoke(m_Model, m_SelectedRow);
        }

        /// <summary>Test-only seam exposing the currently selected row index.</summary>
        internal int SelectedRowForTesting => m_SelectedRow;

        public ViewController MakeSelection(int index)
        {
            if (index >= m_Model.Rows.Count)
                throw new ArgumentException($"Item index out of range. Expected smaller than {m_Model.Rows.Count}, got {index}.");

            var row = m_Model.Rows[index];
            return new SimpleDetailsViewController(row.Name, row.Description, row.DocumentationUrl, RowActionProvider, row);
        }
    }
}

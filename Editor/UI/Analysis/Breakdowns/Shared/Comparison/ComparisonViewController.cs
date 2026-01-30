using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.MemoryProfiler.Editor.Format;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Abstract base view controller for visual comparison between two trees. Displays a comparison table, for which you must provide a ComparisonTableModel builder. Subclasses then provide the specialized base/compared views (see AllTrackedMemoryComparisonViewController for example).
    abstract class ComparisonViewController<TBaseModel, TBaseModelBuilderArgs, TBaseModelItemData>
        : AbstractComparisonTreeViewController<
            ComparisonTableModel<TBaseModel, TBaseModelItemData>,
            ComparisonTableModel<TBaseModel, TBaseModelItemData>.ComparisonData,
            TBaseModel, TBaseModelItemData>
        where TBaseModelItemData : INamedTreeItemData
        where TBaseModel : TreeModel<TBaseModelItemData>
    {
        const string k_UxmlAssetGuid = "c7699cc627afa2943b34e839e48b88af";
        const string k_UssClass_Dark = "comparison-view__dark";
        const string k_UssClass_Light = "comparison-view__light";
        const string k_UxmlIdentifier_DescriptionLabel = "comparison-view__description-label";
        const string k_UxmlIdentifier_SearchField = "comparison-view__search-field";
        const string k_UxmlIdentifier_SplitView = "comparison-view__split-view";
        const string k_UxmlIdentifier_BaseTotalSizeBar = "comparison-view__base-total-size-bar";
        const string k_UxmlIdentifier_ComparedTotalSizeBar = "comparison-view__compared-total-size-bar";
        const string k_UxmlIdentifier_TreeView = "comparison-view__tree-view";
        const string k_UxmlIdentifier_TreeViewColumn__Description = "comparison-view__tree-view__column__description";
        const string k_UxmlIdentifier_TreeViewColumn__CountDelta = "comparison-view__tree-view__column__count-delta";
        const string k_UxmlIdentifier_TreeViewColumn__SizeDeltaBar = "comparison-view__tree-view__column__size-delta-bar";
        const string k_UxmlIdentifier_TreeViewColumn__SizeDelta = "comparison-view__tree-view__column__size-delta";
        const string k_UxmlIdentifier_TreeViewColumn__TotalSizeInA = "comparison-view__tree-view__column__total-size-in-a";
        const string k_UxmlIdentifier_TreeViewColumn__TotalSizeInB = "comparison-view__tree-view__column__total-size-in-b";
        const string k_UxmlIdentifier_TreeViewColumn__CountInA = "comparison-view__tree-view__column__count-in-a";
        const string k_UxmlIdentifier_TreeViewColumn__CountInB = "comparison-view__tree-view__column__count-in-b";
        const string k_UxmlIdentifier_UnchangedToggle = "comparison-view__toolbar__unchanged-toggle";
        const string k_UxmlIdentifier_LoadingOverlay = "comparison-view__loading-overlay";
        const string k_UxmlIdentifier_BaseTitleLabel = "comparison-view__secondary__base-title-label";
        const string k_UxmlIdentifier_BaseDescriptionLabel = "comparison-view__secondary__base-description-label";
        const string k_UxmlIdentifier_BaseViewContainer = "comparison-view__secondary__base-table-container";
        const string k_UxmlIdentifier_ComparedTitleLabel = "comparison-view__secondary__compared-title-label";
        const string k_UxmlIdentifier_ComparedDescriptionLabel = "comparison-view__secondary__compared-description-label";
        const string k_UxmlIdentifier_ComparedViewContainer = "comparison-view__secondary__compared-table-container";
        const string k_UxmlIdentifier_ErrorLabel = "comparison-view__error-label";

        // Sort comparisons for each column.
        protected override Dictionary<string, Comparison<TreeViewItemData<ComparisonTableModel<TBaseModel, TBaseModelItemData>.ComparisonData>>> SortComparisons => k_SortComparisons;
        static readonly Dictionary<string, Comparison<TreeViewItemData<ComparisonTableModel<TBaseModel, TBaseModelItemData>.ComparisonData>>> k_SortComparisons = new()
        {
            { k_UxmlIdentifier_TreeViewColumn__Description, (x, y) => string.Compare(x.data.Name, y.data.Name, StringComparison.OrdinalIgnoreCase) },
            { k_UxmlIdentifier_TreeViewColumn__CountDelta, (x, y) => x.data.CountDelta.CompareTo(y.data.CountDelta) },
            { k_UxmlIdentifier_TreeViewColumn__SizeDelta, (x, y) => x.data.SizeDelta.CompareTo(y.data.SizeDelta) },
            { k_UxmlIdentifier_TreeViewColumn__SizeDeltaBar, (x, y) => x.data.SizeDelta.CompareTo(y.data.SizeDelta) },
            { k_UxmlIdentifier_TreeViewColumn__TotalSizeInA, (x, y) => x.data.TotalSizeInA.Committed.CompareTo(y.data.TotalSizeInA.Committed) },
            { k_UxmlIdentifier_TreeViewColumn__TotalSizeInB, (x, y) => x.data.TotalSizeInB.Committed.CompareTo(y.data.TotalSizeInB.Committed) },
            { k_UxmlIdentifier_TreeViewColumn__CountInA, (x, y) => x.data.CountInA.CompareTo(y.data.CountInA) },
            { k_UxmlIdentifier_TreeViewColumn__CountInB, (x, y) => x.data.CountInB.CompareTo(y.data.CountInB) },
        };

        // Model.
        readonly string m_Description;
        readonly Func<CachedSnapshot, CachedSnapshot, TBaseModelBuilderArgs, TreeComparisonBuilder.BuildArgs, ComparisonTableModel<TBaseModel, TBaseModelItemData>> m_BuildModel;

        // View.
        Label m_DescriptionLabel;
        ToolbarSearchField m_SearchField;
        Toggle m_UnchangedToggle;
        TwoPaneSplitView m_SplitView;
        DetailedSizeBar m_TotalSizeBarA;
        DetailedSizeBar m_TotalSizeBarB;
        Label m_BaseTitleLabel;
        Label m_ComparedTitleLabel;

        public ComparisonViewController(
            CachedSnapshot snapshotA,
            CachedSnapshot snapshotB,
            string description,
            Func<CachedSnapshot, CachedSnapshot, TBaseModelBuilderArgs, TreeComparisonBuilder.BuildArgs, ComparisonTableModel<TBaseModel, TBaseModelItemData>> buildModel)
            : base(idOfDefaultColumnWithPercentageBasedWidth: k_UxmlIdentifier_TreeViewColumn__Description)
        {
            SnapshotA = snapshotA;
            SnapshotB = snapshotB;
            m_Description = description;
            m_BuildModel = buildModel;
        }

        protected override ToolbarSearchField SearchField => m_SearchField;

        protected CachedSnapshot SnapshotA { get; }

        protected CachedSnapshot SnapshotB { get; }

        protected MultiColumnTreeView TreeView => m_TreeView;

        protected VisualElement BaseViewContainer { get; private set; }

        protected Label BaseDescriptionLabel { get; private set; }

        protected VisualElement ComparedViewContainer { get; private set; }

        protected Label ComparedDescriptionLabel { get; private set; }

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
            m_SplitView.RegisterCallback<GeometryChangedEvent>(ConfigureSplitViewLayout);

            m_DescriptionLabel.text = m_Description;
            m_UnchangedToggle.text = "Show Unchanged";
            m_UnchangedToggle.RegisterValueChangedCallback(ApplyFilter);

            m_BaseTitleLabel.text = "Base";
            m_ComparedTitleLabel.text = "Compared";
            base.ViewLoaded();
        }

        protected IScopedFilter<string> BuildTextFilterFromSearchText()
        {
            var searchText = m_SearchField.value;
            // ToolbarSearchField provides an empty (non-null) string when the search bar is empty. In that case we don't want a valid filter for "", i.e. "any text".
            if (string.IsNullOrEmpty(searchText))
                searchText = null;
            return ScopedContainsTextFilter.Create(searchText);
        }

        void GatherReferencesInView(VisualElement view)
        {
            m_DescriptionLabel = view.Q<Label>(k_UxmlIdentifier_DescriptionLabel);
            m_SearchField = view.Q<ToolbarSearchField>(k_UxmlIdentifier_SearchField);
            m_SplitView = view.Q<TwoPaneSplitView>(k_UxmlIdentifier_SplitView);
            m_TotalSizeBarA = view.Q<DetailedSizeBar>(k_UxmlIdentifier_BaseTotalSizeBar);
            m_TotalSizeBarB = view.Q<DetailedSizeBar>(k_UxmlIdentifier_ComparedTotalSizeBar);
            m_TreeView = view.Q<MultiColumnTreeView>(k_UxmlIdentifier_TreeView);
            m_UnchangedToggle = view.Q<Toggle>(k_UxmlIdentifier_UnchangedToggle);
            m_LoadingOverlay = view.Q<ActivityIndicatorOverlay>(k_UxmlIdentifier_LoadingOverlay);
            m_BaseTitleLabel = view.Q<Label>(k_UxmlIdentifier_BaseTitleLabel);
            BaseDescriptionLabel = view.Q<Label>(k_UxmlIdentifier_BaseDescriptionLabel);
            BaseViewContainer = view.Q<VisualElement>(k_UxmlIdentifier_BaseViewContainer);
            m_ComparedTitleLabel = view.Q<Label>(k_UxmlIdentifier_ComparedTitleLabel);
            ComparedDescriptionLabel = view.Q<Label>(k_UxmlIdentifier_ComparedDescriptionLabel);
            ComparedViewContainer = view.Q<VisualElement>(k_UxmlIdentifier_ComparedViewContainer);
            m_ErrorLabel = view.Q<Label>(k_UxmlIdentifier_ErrorLabel);
        }

        void ConfigureSplitViewLayout(GeometryChangedEvent evt)
        {
            // There is currently no way to set a split view's initial dimension as a percentage from UXML/USS, so we must do it manually once on load.
            m_SplitView.fixedPaneInitialDimension = m_SplitView.layout.height * 0.5f;
            m_SplitView.UnregisterCallback<GeometryChangedEvent>(ConfigureSplitViewLayout);
        }

        protected override void ConfigureTreeView()
        {
            base.ConfigureTreeView();

            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__Description, "Description", BindCellForDescriptionColumn());
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__CountDelta, "Count Difference", BindCellForCountDeltaColumn(), makeCell: CountDeltaCell.Instantiate);
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__SizeDeltaBar, "Size Difference Bar", BindCellForSizeDeltaBarColumn(), makeCell: DeltaBarCell.Instantiate);
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__SizeDelta, "Size Difference", BindCellForSizeColumn(SizeType.SizeDelta), makeCell: MakeSizeDeltaCell);
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__TotalSizeInA, "Size In A", BindCellForSizeColumn(SizeType.TotalSizeInA));
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__TotalSizeInB, "Size In B", BindCellForSizeColumn(SizeType.TotalSizeInB));
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__CountInA, "Count In A", BindCellForCountColumn(CountType.CountInA));
            ConfigureTreeViewColumn(k_UxmlIdentifier_TreeViewColumn__CountInB, "Count In B", BindCellForCountColumn(CountType.CountInB));
        }

        protected abstract TBaseModelBuilderArgs GetBaseModelBuilderArgs(IScopedFilter<string> itemNameFilter, bool sameSessionComparison);

        protected virtual TreeComparisonBuilder.BuildArgs GetComparisonModelBuilderArgs()
        {
            // TODO: fix comparison tables to work with resident memory info
            return new TreeComparisonBuilder.BuildArgs(m_UnchangedToggle.value, AllTrackedMemoryTableMode.OnlyCommitted);
        }

        protected override Func<ComparisonTableModel<TBaseModel, TBaseModelItemData>> GetModelBuilderTask(CancellationToken cancellationToken)
        {
            // Capture all variables locally in case they are changed before the task is started
            var modelTypeName = GetType().ToString();

            var snapshotA = SnapshotA;
            var snapshotB = SnapshotB;
            var sameSessionComparison = SnapshotA.MetaData.SessionGUID != MetaData.InvalidSessionGUID && SnapshotA.MetaData.SessionGUID == SnapshotB.MetaData.SessionGUID;
            var itemNameFilter = BuildTextFilterFromSearchText();

            var args = GetBaseModelBuilderArgs(itemNameFilter, sameSessionComparison);
            var compareArgs = GetComparisonModelBuilderArgs();

            return () =>
                {
                    AsyncTaskHelper.DebugLogAsyncStep("Start Building                 " + modelTypeName);
                    // Build the data model.
                    var model = m_BuildModel.Invoke(
                        snapshotA,
                        snapshotB,
                        args,
                        compareArgs);

                    cancellationToken.ThrowIfCancellationRequested();
                    AsyncTaskHelper.DebugLogAsyncStep("Start Building (not canceled, sorting)                 " + modelTypeName);

                    AsyncTaskHelper.DebugLogAsyncStep("Building Finished                 " + modelTypeName);
                    return model;
                };
        }

        protected override void OnViewReloaded(bool success)
        {
            base.OnViewReloaded(success);

            var itemNameFilter = BuildTextFilterFromSearchText();
            MemoryProfilerAnalytics.AddAllTrackedMemoryComparisonUsage(itemNameFilter != null, MemoryProfilerSettings.ShowReservedMemoryBreakdown, m_UnchangedToggle.value);
        }

        protected override void RefreshView()
        {
            var maxValue = Math.Max(m_Model.TotalSnapshotSizeA.Committed, m_Model.TotalSnapshotSizeB.Committed);
            SetDetailedProgressBarValues(m_TotalSizeBarA, m_Model.TotalSizeA.Committed, m_Model.TotalSnapshotSizeA.Committed, maxValue);
            SetDetailedProgressBarValues(m_TotalSizeBarB, m_Model.TotalSizeB.Committed, m_Model.TotalSnapshotSizeB.Committed, maxValue);

            base.RefreshView();
        }

        void SetDetailedProgressBarValues(DetailedSizeBar detailedSizeBar, ulong totalMemorySize, ulong totalSnapshotMemorySize, ulong maxValue)
        {
            detailedSizeBar.Bar.Mode = MemoryBarElement.VisibilityMode.CommittedOnly;
            detailedSizeBar.SetValue(new MemorySize(totalMemorySize, 0), totalSnapshotMemorySize, maxValue);

            var totalMemorySizeText = EditorUtility.FormatBytes((long)totalMemorySize);
            detailedSizeBar.SetSizeText($"Allocated Memory In Table: {totalMemorySizeText}", $"{totalMemorySize:N0} B");

            var totalSnapshotMemorySizeText = EditorUtility.FormatBytes((long)totalSnapshotMemorySize);
            detailedSizeBar.SetTotalText($"Total Memory In Snapshot: {totalSnapshotMemorySizeText}", $"{totalSnapshotMemorySize:N0} B");
        }

        Action<VisualElement, int> BindCellForDescriptionColumn()
        {
            const string k_NoName = "<No Name>";
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<ComparisonTableModel<TBaseModel, TBaseModelItemData>.ComparisonData>(rowIndex);
                var name = itemData.Name;
                if (string.IsNullOrEmpty(name))
                    name = k_NoName;

                // UITK Label supports undocumented escape formatting
                // We need to escape all `\` to make sure that paths don't trigger it
                name = name.Replace("\\", "\\\\");

                var cell = (Label)element;
                cell.text = name;
            };
        }

        Action<VisualElement, int> BindCellForCountDeltaColumn()
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<ComparisonTableModel<TBaseModel, TBaseModelItemData>.ComparisonData>(rowIndex);
                var countDelta = itemData.CountDelta;

                var cell = (CountDeltaCell)element;
                cell.SetCountDelta(countDelta);
            };
        }

        Action<VisualElement, int> BindCellForSizeDeltaBarColumn()
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<ComparisonTableModel<TBaseModel, TBaseModelItemData>.ComparisonData>(rowIndex);
                var sizeDelta = itemData.SizeDelta;

                var cell = (DeltaBarCell)element;
                var proportionalSizeDelta = 0f;
                if (sizeDelta != 0)
                    proportionalSizeDelta = (float)sizeDelta / m_Model.LargestAbsoluteSizeDelta;
                cell.SetDeltaScalar(proportionalSizeDelta);
                cell.tooltip = FormatBytes(sizeDelta);
            };
        }

        Action<VisualElement, int> BindCellForSizeColumn(SizeType sizeType)
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<ComparisonTableModel<TBaseModel, TBaseModelItemData>.ComparisonData>(rowIndex);
                var size = sizeType switch
                {
                    SizeType.SizeDelta => itemData.SizeDelta,
                    SizeType.TotalSizeInA => Convert.ToInt64(itemData.TotalSizeInA.Committed),
                    SizeType.TotalSizeInB => Convert.ToInt64(itemData.TotalSizeInB.Committed),
                    _ => throw new ArgumentException("Unknown size type."),
                };

                var cell = (Label)element;
                cell.text = FormatBytes(size);
            };
        }

        Action<VisualElement, int> BindCellForCountColumn(CountType countType)
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<ComparisonTableModel<TBaseModel, TBaseModelItemData>.ComparisonData>(rowIndex);
                var count = countType switch
                {
                    CountType.CountInA => Convert.ToInt32(itemData.CountInA),
                    CountType.CountInB => Convert.ToInt32(itemData.CountInB),
                    _ => throw new ArgumentException("Unknown count type."),
                };

                var cell = (Label)element;
                cell.text = $"{count:N0}";
            };
        }

        VisualElement MakeSizeDeltaCell()
        {
            var cell = new Label();
            cell.AddToClassList("unity-multi-column-view__cell__label");

            // Make this a cell with a darkened background. This requires quite a bit of styling to be compatible with tree view selection styling, so that is why it is its own class.
            cell.AddToClassList("dark-tree-view-cell");

            return cell;
        }

        void ApplyFilter(ChangeEvent<bool> evt)
        {
            // ApplyFilter is a listener to UI input (Unchanged filter changes) and therefore doesn't expect an awaitable task.
            // No caller or following code expects any part of the asnyc build to be done.
            // Therefore: Discard the returned task.
            _ = BuildModelAsync(false);
        }

        static string FormatBytes(long bytes)
        {
            var sizeText = new System.Text.StringBuilder();

            // Our built-in formatter for bytes doesn't support negative values.
            if (bytes < 0)
                sizeText.Append("-");

            var absoluteBytes = Math.Abs(bytes);
            sizeText.Append(EditorUtility.FormatBytes(absoluteBytes));
            return sizeText.ToString();
        }

        enum SizeType
        {
            SizeDelta,
            TotalSizeInA,
            TotalSizeInB,
        }

        enum CountType
        {
            CountInA,
            CountInB,
        }
    }
}

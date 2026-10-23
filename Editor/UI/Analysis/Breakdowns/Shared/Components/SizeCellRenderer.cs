using System;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    class SizeCellRenderer
    {
        readonly MultiColumnTreeView m_TreeView;
        readonly Func<AllTrackedMemoryTableMode> m_GetTableMode;
        readonly string m_UnavailableText;
        readonly string m_UnreliableCssClass;

        public SizeCellRenderer(
            MultiColumnTreeView treeView,
            Func<AllTrackedMemoryTableMode> getTableMode,
            string unavailableText = null,
            string unreliableCssClass = "analysis-view__text__information-unreliable-or-unavailable")
        {
            m_TreeView = treeView;
            m_GetTableMode = getTableMode;
            m_UnavailableText = string.IsNullOrEmpty(unavailableText) ? TextContent.NotAvailable : unavailableText;
            m_UnreliableCssClass = unreliableCssClass;
        }

        /// <summary>
        /// Whether the size bars scale to their parent row instead of the table total. Rows without a parent,
        /// including every row of a flattened table, always scale to the total.
        /// </summary>
        public bool ScaleBarsToParent { get; set; }

        /// <summary>
        /// Wires a footer toggle to <see cref="ScaleBarsToParent"/>, persisted under <paramref name="sessionStateKey"/>.
        /// Toggling only rebinds the visible rows.
        /// </summary>
        public void SetUpScaleBarsToParentToggle(Toggle toggle, string sessionStateKey)
        {
            ScaleBarsToParent = ScaleBarsToParentOption.SetUp(toggle, sessionStateKey, value =>
            {
                ScaleBarsToParent = value;
                m_TreeView.RefreshItems();
            });
        }

        /// <summary>
        /// The parent row's size while <see cref="ScaleBarsToParent"/> is on and the parent has a non-zero size,
        /// otherwise <paramref name="totalMax"/>.
        /// </summary>
        MemorySize ResolveBarScale(int rowIndex, MemorySize totalMax, Func<int, MemorySize> getParentSize)
        {
            if (!ScaleBarsToParent)
                return totalMax;

            var parentId = m_TreeView.GetParentIdForIndex(rowIndex);
            if (parentId < 0)
                return totalMax;

            var parentSize = getParentSize(parentId);
            var parentValue = m_GetTableMode() == AllTrackedMemoryTableMode.OnlyResident ? parentSize.Resident : parentSize.Committed;
            return parentValue == 0 ? totalMax : new MemorySize(parentValue, parentValue);
        }

        public Action<VisualElement, int> CreateSizeBinding<TItemData>(
            Func<TItemData, ulong> sizeSelector,
            Func<TItemData, bool> isUnreliableSelector = null,
            string unavailableTooltip = null)
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<TItemData>(rowIndex);
                var size = sizeSelector(itemData);
                var cell = element.Q<Label>();
                var isUnreliable = isUnreliableSelector?.Invoke(itemData) ?? false;

                if (!isUnreliable)
                {
                    cell.text = EditorUtility.FormatBytes((long)size);
                    cell.tooltip = $"{size:N0} B";
                    cell.displayTooltipWhenElided = false;
                    cell.RemoveFromClassList(m_UnreliableCssClass);
                }
                else
                {
                    cell.text = m_UnavailableText;
                    cell.tooltip = unavailableTooltip ?? string.Empty;
                    cell.AddToClassList(m_UnreliableCssClass);
                }
            };
        }

        public Action<VisualElement, int> CreateMemorySizeBinding<TItemData>(
            Func<TItemData, MemorySize> sizeSelector,
            Func<TItemData, bool> isUnreliableSelector = null,
            string unavailableTooltip = null,
            bool? useResident = null)
        {
            return CreateSizeBinding<TItemData>(
                itemData => useResident ?? m_GetTableMode() == AllTrackedMemoryTableMode.OnlyResident ? sizeSelector(itemData).Resident : sizeSelector(itemData).Committed,
                isUnreliableSelector,
                unavailableTooltip);
        }

        /// <summary>
        /// Binds a <see cref="DualSizeCell"/>, showing the filtered value as primary and the row's own value as the
        /// secondary when they differ. <paramref name="dualTooltipFormat"/> takes {0}=filtered and {1}=own byte counts.
        /// </summary>
        public Action<VisualElement, int> CreateDualMemorySizeBinding<TItemData>(
            Func<TItemData, MemorySize> precalcSelector,
            Func<int, MemorySize?> filteredSelector,
            string dualTooltipFormat,
            Func<TItemData, bool> isUnreliableSelector = null,
            string unavailableTooltip = null,
            bool? useResident = null)
        {
            return (element, rowIndex) =>
            {
                var itemData = m_TreeView.GetItemDataForIndex<TItemData>(rowIndex);
                var cell = element.Q<DualSizeCell>();

                if (isUnreliableSelector?.Invoke(itemData) ?? false)
                {
                    cell.SetSingle(m_UnavailableText, unavailableTooltip ?? string.Empty, unreliable: true, m_UnreliableCssClass);
                    return;
                }

                var resident = useResident ?? m_GetTableMode() == AllTrackedMemoryTableMode.OnlyResident;
                var precalc = resident ? precalcSelector(itemData).Resident : precalcSelector(itemData).Committed;

                var filteredSize = filteredSelector(m_TreeView.GetIdForIndex(rowIndex));
                var filtered = filteredSize.HasValue
                    ? (resident ? filteredSize.Value.Resident : filteredSize.Value.Committed)
                    : (ulong?)null;

                if (filtered.HasValue && filtered.Value != precalc)
                {
                    cell.SetDual(
                        EditorUtility.FormatBytes((long)filtered.Value),
                        EditorUtility.FormatBytes((long)precalc),
                        string.Format(dualTooltipFormat, $"{filtered.Value:N0} B", $"{precalc:N0} B"),
                        m_UnreliableCssClass);
                }
                else
                {
                    cell.SetSingle(EditorUtility.FormatBytes((long)precalc), $"{precalc:N0} B", unreliable: false, m_UnreliableCssClass);
                }
            };
        }

        /// <summary>
        /// Bar binding that uses the filtered value, when present, instead of the row's own value.
        /// </summary>
        public Action<VisualElement, int> CreateDualMemoryBarBinding<TItemData>(
            Func<TItemData, MemorySize> precalcSelector,
            Func<int, MemorySize?> filteredSelector,
            Func<MemorySize> maxValueSelector,
            Func<TItemData, bool> isUnreliableSelector = null)
        {
            return (element, rowIndex) =>
            {
                var maxValue = maxValueSelector();
                var item = m_TreeView.GetItemDataForIndex<TItemData>(rowIndex);
                var cell = element.Q<MemoryBar>();

                if (isUnreliableSelector?.Invoke(item) ?? false)
                {
                    cell.SetEmpty();
                    return;
                }

                var value = filteredSelector(m_TreeView.GetIdForIndex(rowIndex)) ?? precalcSelector(item);
                var scale = ResolveBarScale(rowIndex, maxValue,
                    parentId => filteredSelector(parentId) ?? precalcSelector(m_TreeView.GetItemDataForId<TItemData>(parentId)));
                cell.Set(value, maxValue.Committed, scale.Committed);
            };
        }

        public Action<VisualElement, int> CreateMemoryBarBinding<TItemData>(
            Func<TItemData, MemorySize> sizeSelector,
            Func<MemorySize> maxValueSelector,
            Func<TItemData, bool> isUnreliableSelector = null)
        {
            return (element, rowIndex) =>
            {
                var maxValue = maxValueSelector();
                var item = m_TreeView.GetItemDataForIndex<TItemData>(rowIndex);
                var cell = element.Q<MemoryBar>();
                var isUnreliable = isUnreliableSelector?.Invoke(item) ?? false;

                if (!isUnreliable)
                {
                    var scale = ResolveBarScale(rowIndex, maxValue,
                        parentId => sizeSelector(m_TreeView.GetItemDataForId<TItemData>(parentId)));
                    cell.Set(sizeSelector(item), maxValue.Committed, scale.Committed);
                }
                else
                    cell.SetEmpty();
            };
        }

        public VisualElement MakeMemoryBarCell()
        {
            var bar = new MemoryBar();
            bar.Mode = m_GetTableMode() switch
            {
                AllTrackedMemoryTableMode.OnlyCommitted => MemoryBarElement.VisibilityMode.CommittedOnly,
                AllTrackedMemoryTableMode.OnlyResident => MemoryBarElement.VisibilityMode.ResidentOnly,
                AllTrackedMemoryTableMode.CommittedAndResident => MemoryBarElement.VisibilityMode.CommittedAndResident,
                _ => throw new NotImplementedException(),
            };
            return bar;
        }
    }
}

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Shared setup of the "Scale bars to parent" footer toggle.
    /// </summary>
    static class ScaleBarsToParentOption
    {
        /// <summary>
        /// Labels the toggle and persists its state under <paramref name="sessionStateKey"/>, calling
        /// <paramref name="onChanged"/> on change. Returns the restored value.
        /// </summary>
        public static bool SetUp(Toggle toggle, string sessionStateKey, Action<bool> onChanged, string tooltip = null)
        {
            toggle.text = TextContent.ScaleBarsToParentToggle;
            toggle.tooltip = tooltip ?? TextContent.ScaleBarsToParentToggleTooltip;
            var initialValue = SessionState.GetBool(sessionStateKey, false);
            toggle.SetValueWithoutNotify(initialValue);
            toggle.RegisterValueChangedCallback(evt =>
            {
                SessionState.SetBool(sessionStateKey, evt.newValue);
                onChanged(evt.newValue);
            });
            return initialValue;
        }
    }
}

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
                    cell.Set(sizeSelector(item), maxValue.Committed, maxValue.Committed);
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

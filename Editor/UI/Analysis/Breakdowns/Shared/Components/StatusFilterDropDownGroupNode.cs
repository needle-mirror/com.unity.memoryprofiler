using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    class StatusFilterDropDownGroupNode : VisualElement
    {
        const string k_UxmlAssetGuid = "c6d7e8f9a0b1c2d3e4f5a6b7c8d9e0f1";
        const string k_SubItemRowUssClass = "status-filter-dropdown__sub-item-row";

        bool IsSelected => (m_GroupValue & m_ParentWindow.CurrentMaskValue) == m_GroupValue;

        readonly StatusFilterDropDownWindow m_ParentWindow;
        readonly Toggle m_GroupToggle;
        readonly ulong m_GroupValue;
        readonly ulong m_EnabledGroupValue;
        readonly ulong m_DisabledGroupValue;

        /// <summary>The translated display name of this group, used for runtime sort-order verification.</summary>
        public string GroupName { get; }
        /// <summary>This group's full bitmask, including statuses unavailable in the current table.</summary>
        public ulong GroupValue => m_GroupValue;
        /// <summary>True when at least one sub-item status is available (interactive) in the current table.</summary>
        public bool HasEnabledItems => m_EnabledGroupValue != 0;

        public StatusFilterDropDownGroupNode(StatusFilterDropDownWindow parentWindow, string name, string tooltip, ulong value,
            ulong unavailableStatusesMask = 0)
        {
            GroupName = name;
            m_GroupValue = value;
            m_ParentWindow = parentWindow;

            AddToClassList("status-filter-dropdown__group-node");

            var visualTree = ViewControllerUtility.LoadVisualTreeAssetFromUxml(k_UxmlAssetGuid);
            visualTree?.CloneTree(this);

            m_GroupToggle = this.Q<Toggle>("group-node__toggle");
            var groupFoldout = this.Q<Foldout>("group-node__foldout");
            var subItemsContainer = this.Q<VisualElement>("group-node__sub-items");

            m_GroupToggle.text = name;
            m_GroupToggle.tooltip = tooltip;

            // Collect sub-items that belong to this group, then sort by translated display-name rank.
            // In the default English locale, SetDisplayName calls are already in alphabetical order
            // so this sort is a no-op; for translated locales it reorders correctly.
            var names = ObjectStatusExtensions.StatusMaskChoiceNames;
            var values = ObjectStatusExtensions.StatusMaskValues;
            var tooltips = ObjectStatusExtensions.StatusMaskChoiceTooltips;
            var subItems = new List<(ulong val, string text, string itemTooltip)>();
            for (int i = ObjectStatusExtensions.CountOfGroupMaskChoices; i < names.Count; i++)
            {
                if ((values[i] & value) == values[i])
                    subItems.Add((values[i], names[i], tooltips[i]));
            }
            subItems.Sort((a, b) => StatusSortKeyFromMask(a.val).CompareTo(StatusSortKeyFromMask(b.val)));

            // Split the group's bits into the interactive (available) subset and the disabled
            // (unavailable-in-this-table) subset. The disabled bits are linked to the interactive
            // ones: they follow whenever every interactive item in the group turns on/off, so a
            // fully-selected group's mask equals its full value (and the button shows the group's
            // name rather than "Mixed").
            m_EnabledGroupValue = value & ~unavailableStatusesMask;
            m_DisabledGroupValue = value & unavailableStatusesMask;

            m_GroupToggle.SetValueWithoutNotify(IsSelected);
            if (m_EnabledGroupValue != 0)
                parentWindow.OnCurrentMaskValueUpdated += UpdateGroupToggle;

            if (!IsSelected && (m_GroupValue & parentWindow.CurrentMaskValue) != 0)
                m_GroupToggle.showMixedValue = true;

            m_GroupToggle.RegisterCallback<ChangeEvent<bool>>((evt) =>
            {
                if (evt.newValue)
                    parentWindow.CurrentMaskValue |= m_GroupValue;
                else
                    parentWindow.CurrentMaskValue &= ~m_GroupValue;
            });

            UIElementsHelper.SetVisibility(subItemsContainer, false);

            groupFoldout.RegisterCallback<ChangeEvent<bool>>((evt) =>
            {
                UIElementsHelper.SetVisibility(subItemsContainer, evt.newValue);
                schedule.Execute(parentWindow.ResizeToFit).ExecuteLater(1);
            });

            foreach (var (val, text, itemTooltip) in subItems)
            {
                var v = val;
                var isUnavailable = (unavailableStatusesMask & v) != 0;

                var toggle = new Toggle() { text = text, value = (v & parentWindow.CurrentMaskValue) == v };
                // Both interactive and disabled toggles mirror the mask, so a disabled item that is
                // linked on (because its group is fully selected) shows as checked.
                parentWindow.OnCurrentMaskValueUpdated += (mask) => toggle.SetValueWithoutNotify((mask & v) == v);

                if (isUnavailable)
                {
                    // The disabled toggle can't be clicked; wrap it in an enabled row element so its
                    // tooltip (naming the view(s) where this status CAN be inspected) still shows on hover.
                    toggle.SetEnabled(false);
                    var row = new VisualElement();
                    row.AddToClassList(k_SubItemRowUssClass);
                    row.tooltip = ObjectStatusExtensions.GetStatusUnavailableTooltip(v);
                    row.Add(toggle);
                    subItemsContainer.Add(row);
                }
                else
                {
                    toggle.tooltip = itemTooltip;
                    toggle.RegisterCallback<ChangeEvent<bool>>((evt) =>
                    {
                        var mask = parentWindow.CurrentMaskValue;
                        if (evt.newValue)
                            mask |= v;
                        else
                            mask &= ~v;
                        // Link this group's disabled bits to its interactive ones after the change.
                        parentWindow.CurrentMaskValue = ReconcileDisabledBits(mask);
                    });
                    subItemsContainer.Add(toggle);
                }
            }
        }

        // Extracts the ObjectStatus from a single-bit mask (bit N → ObjectStatus N).
        internal static ObjectStatus SingleBitMaskToStatus(ulong singleBitMask)
        {
            int bitIndex = 0;
            ulong b = singleBitMask;
            while ((b >>= 1) != 0) bitIndex++;
            return (ObjectStatus)bitIndex;
        }

        // Recovers GetStatusSortKey for a single-bit status mask without boxing an ObjectStatusWarningInfo.
        internal static int StatusSortKeyFromMask(ulong singleBitMask)
        {
            if (singleBitMask == 0) return int.MaxValue;
            return SingleBitMaskToStatus(singleBitMask).GetStatusSortKey();
        }

        // Links the group's disabled (unavailable) bits to its interactive ones: the disabled bits
        // are set iff every interactive item in the group is on. Keeps a fully-selected group's mask
        // equal to its full value even though the disabled items can't be toggled directly.
        public ulong ReconcileDisabledBits(ulong mask)
        {
            if (m_DisabledGroupValue == 0)
                return mask;
            if (m_EnabledGroupValue != 0 && (mask & m_EnabledGroupValue) == m_EnabledGroupValue)
                return mask | m_DisabledGroupValue;
            return mask & ~m_DisabledGroupValue;
        }

        void UpdateGroupToggle(ulong maskValue)
        {
            if ((m_GroupValue & maskValue) == m_GroupValue)
            {
                m_GroupToggle.SetValueWithoutNotify(true);
                m_GroupToggle.showMixedValue = false;
            }
            else if ((m_GroupValue & maskValue) != 0)
            {
                m_GroupToggle.SetValueWithoutNotify(false);
                m_GroupToggle.showMixedValue = true;
            }
            else
            {
                m_GroupToggle.SetValueWithoutNotify(false);
                m_GroupToggle.showMixedValue = false;
            }
        }
    }
}

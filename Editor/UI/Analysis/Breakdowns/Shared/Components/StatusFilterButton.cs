using System;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// A self-contained status filter control: an enable toggle followed by a dropdown button that
    /// selects which statuses to filter the table by. The dropdown stays interactive regardless of the
    /// toggle, so a selection can be prepared up front, but the filter is only applied while the toggle
    /// is on. Default: toggle off (no filtering on open) with "All Leaked" preselected, so enabling
    /// immediately surfaces leaks.
    /// </summary>
#if UNITY_6000_0_OR_NEWER
    [UxmlElement]
#endif
    partial class StatusFilterButton : VisualElement
    {
        const string k_UxmlAssetGuid = "f0e1d2c3b4a5968778695a4b3c2d1e0f";

        readonly Toggle m_EnableToggle;
        readonly Button m_DropdownButton;
        readonly Label m_DropdownLabel;

        IExclusiveStatusFilterManager m_FilterManager;
        Action m_OnFilterChanged;
        ulong m_CurrentMask;
        double m_DropdownLastClosed;
        ulong m_UnavailableStatusesMask;

        public StatusFilterButton()
        {
            AddToClassList("table-view__filter-group");

            var visualTree = ViewControllerUtility.LoadVisualTreeAssetFromUxml(k_UxmlAssetGuid);
            visualTree?.CloneTree(this);

            m_EnableToggle = this.Q<Toggle>("status-filter-button__enable-toggle");
            m_DropdownButton = this.Q<Button>("status-filter-button__dropdown-button");
            m_DropdownLabel = this.Q<Label>("status-filter-button__dropdown-label");

            m_EnableToggle.RegisterValueChangedCallback(OnToggleChanged);
            m_DropdownButton.clicked += OnDropdownClicked;
        }

        public void SetUp(IExclusiveStatusFilterManager filterManager, Action onFilterChanged, ulong unavailableStatusesMask = 0)
        {
            m_FilterManager = filterManager;
            m_OnFilterChanged = onFilterChanged;
            m_UnavailableStatusesMask = unavailableStatusesMask;
            m_EnableToggle.text = TextContent.ObjectStatusFilterLabel;
            m_EnableToggle.tooltip = TextContent.ObjectStatusFilterButtonTooltip;

            // Default: filtering off, with "All Leaked" preselected so enabling the toggle immediately
            // filters to leaks. The dropdown stays interactive while off so the selection can be changed first.
            m_CurrentMask = ObjectStatusExtensions.AllLeakedStatusesMask;
            UpdateDropdownLabel(m_CurrentMask);
            m_EnableToggle.SetValueWithoutNotify(false);
            ApplyCurrentFilter();
        }

        void OnToggleChanged(ChangeEvent<bool> evt)
        {
            ApplyCurrentFilter();
            m_OnFilterChanged?.Invoke();
        }

        void OnDropdownClicked()
        {
            if (Time.realtimeSinceStartupAsDouble - m_DropdownLastClosed < 0.1)
                return;

            var window = EditorWindow.CreateInstance<StatusFilterDropDownWindow>();
            var buttonRect = m_DropdownButton.GetRect();
            var buttonPosition = GUIUtility.GUIToScreenPoint(buttonRect.position);
            var rect = new Rect(buttonPosition, buttonRect.size);
            m_DropdownLastClosed = Time.realtimeSinceStartupAsDouble;
            window.OnMaskChoiceConfirmed += OnMaskConfirmed;
            window.position = rect;
            // Initialize AFTER CreateInstance (CreateGUI fires at instantiation) but BEFORE
            // ShowAsDropDown, so PopulateGroups runs with the correct mask and unavailable statuses.
            window.Initialize(m_CurrentMask, m_UnavailableStatusesMask);
            window.ShowAsDropDown(rect, new Vector2(rect.width, 500));
        }

        void OnMaskConfirmed(ulong mask)
        {
            m_DropdownLastClosed = Time.realtimeSinceStartupAsDouble;

            // Closing the dropdown without changing the selection must not trigger a model rebuild —
            // that is expensive on large snapshots. The mask already applied is the source of truth.
            if (mask == m_CurrentMask)
                return;

            m_CurrentMask = mask;
            UpdateDropdownLabel(mask);

            // While the toggle is off the filter isn't applied, so changing the selection only updates
            // the stored mask + label and must not trigger an (expensive) rebuild. The new mask takes
            // effect when the toggle is next turned on.
            if (!m_EnableToggle.value)
                return;

            ApplyCurrentFilter();
            m_OnFilterChanged?.Invoke();
        }

        void ApplyCurrentFilter()
        {
            if (!m_EnableToggle.value)
            {
                // Filtering disabled: no mask, full model. The selection is preserved for when it's re-enabled.
                m_FilterManager.ExclusiveStatusMask = null;
                return;
            }

            var allAvailable = ObjectStatusExtensions.AllStatuses & ~m_UnavailableStatusesMask;
            if (m_CurrentMask == 0)
            {
                // User explicitly cleared all status groups. Filter to show only ObjectStatus.None
                // items (bit 0 = None). This is distinct from a null mask (no filter at all).
                m_FilterManager.ExclusiveStatusMask = 1ul;
            }
            else if (allAvailable != 0 && (m_CurrentMask & allAvailable) == allAvailable)
            {
                // Every status that can occur in this table is selected, which is equivalent to no
                // filter. Use a null mask so the full model is built rather than a filtered one.
                m_FilterManager.ExclusiveStatusMask = null;
            }
            else
            {
                m_FilterManager.ExclusiveStatusMask = m_CurrentMask;
            }
        }

        void UpdateDropdownLabel(ulong maskValue)
        {
            if (maskValue == 0)
            {
                // All groups unchecked — filtering to ObjectStatus.None only.
                m_DropdownLabel.text = TextContent.ObjectStatusNoneLabel;
                return;
            }

            // When every status that can occur in this table is selected, show "All" rather than
            // falling through to "Mixed". The available set excludes statuses unavailable in this
            // table (m_UnavailableStatusesMask), which is why a plain equality with the global
            // AllStatuses mask isn't enough (e.g. the Unity Objects table, where many statuses can
            // never occur and so can never be toggled on).
            var allAvailable = ObjectStatusExtensions.AllStatuses & ~m_UnavailableStatusesMask;
            if (allAvailable != 0 && (maskValue & allAvailable) == allAvailable)
            {
                m_DropdownLabel.text = TextContent.ObjectStatusAll;
                return;
            }

            var names = ObjectStatusExtensions.StatusMaskChoiceNames;
            var tooltips = ObjectStatusExtensions.StatusMaskChoiceTooltips;
            var values = ObjectStatusExtensions.StatusMaskValues;
            for (int i = 0; i < names.Count; i++)
            {
                if (values[i] == maskValue)
                {
                    m_DropdownLabel.text = names[i];
                    return;
                }
            }
            m_DropdownLabel.text = TextContent.ObjectStatusMixed;
        }

#if !UNITY_6000_0_OR_NEWER
        public new class UxmlFactory : UxmlFactory<StatusFilterButton> { }
#endif
    }
}

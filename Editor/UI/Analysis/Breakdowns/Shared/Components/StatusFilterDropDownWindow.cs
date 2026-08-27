using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    class StatusFilterDropDownWindow : EditorWindow
    {
        const string k_UxmlAssetGuid = "b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7";

        const float k_WindowWidth = 300f;
        const float k_MaxScrollHeight = 800f;

        public ulong m_CurrentMaskValue;
        public ulong CurrentMaskValue
        {
            get => m_CurrentMaskValue;
            set
            {
                m_CurrentMaskValue = value;
                OnCurrentMaskValueUpdated?.Invoke(m_CurrentMaskValue);
            }
        }

        // OR of the full bitmask of every group shown in the list (interactive bits plus the
        // disabled bits linked to them). "Select All" sets exactly these bits.
        ulong m_AllShownStatuses;
        readonly List<StatusFilterDropDownGroupNode> m_GroupNodes = new List<StatusFilterDropDownGroupNode>();

        // Data set by Initialize() — stored so CreateGUI can populate groups if called afterwards.
        // Bitmask (bit N = ObjectStatus N) of statuses unavailable in the owning table.
        ulong m_UnavailableStatusesMask;
        bool m_InitializeCalled;
        bool m_SkeletonCreated;
        bool m_GroupsPopulated;

        public event Action<ulong> OnMaskChoiceConfirmed;
        public event Action<ulong> OnCurrentMaskValueUpdated;

        bool m_SubscribedToUIUpdates = false;
        Toggle m_SelectAllToggle;
        ScrollView m_ScrollView;
        VisualElement m_Separator;

        // Called with the mask and unavailable-status set before the window is shown.
        // Because CreateGUI fires during EditorWindow.CreateInstance (before ShowAsDropDown),
        // CreateGUI only builds the skeleton; this method populates the group nodes once the
        // caller has set up both parameters.
        public void Initialize(ulong initialMask, ulong unavailableStatusesMask)
        {
            m_CurrentMaskValue = initialMask;
            m_UnavailableStatusesMask = unavailableStatusesMask;
            m_InitializeCalled = true;

            if (m_SkeletonCreated)
                PopulateGroups();
        }

        void CreateGUI()
        {
            rootVisualElement.AddToClassList(MemoryProfilerViewController.USSThemeClassToUse);
            rootVisualElement.AddToClassList("status-filter-dropdown");

            // Load the base layout (Select All toggle, separator, scroll area) from UXML when
            // the GUID is known. The UXML also pulls in StatusFilterDropDown.uss via its <Style>.
            // Fall back to code-based element creation until the GUID is set.
            bool loadedFromUxml = false;
            if (!string.IsNullOrEmpty(k_UxmlAssetGuid))
            {
                var visualTree = ViewControllerUtility.LoadVisualTreeAssetFromUxml(k_UxmlAssetGuid);
                if (visualTree != null)
                {
                    visualTree.CloneTree(rootVisualElement);
                    loadedFromUxml = true;
                }
            }

            if (!loadedFromUxml)
            {
                // Manually apply the stylesheet so classes resolve correctly.
                var styleSheetGuids = AssetDatabase.FindAssets("StatusFilterDropDown t:StyleSheet");
                if (styleSheetGuids.Length > 0)
                {
                    var ss = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                        AssetDatabase.GUIDToAssetPath(styleSheetGuids[0]));
                    if (ss != null)
                        rootVisualElement.styleSheets.Add(ss);
                }

                rootVisualElement.Add(new Toggle
                {
                    name = "status-filter-dropdown__select-all-toggle",
                });
                rootVisualElement.Q<Toggle>("status-filter-dropdown__select-all-toggle")
                    .AddToClassList("status-filter-dropdown__select-all");

                var sep = new VisualElement { name = "status-filter-dropdown__separator-element" };
                sep.AddToClassList("status-filter-dropdown__separator");
                rootVisualElement.Add(sep);

                var sv = new ScrollView { name = "status-filter-dropdown__scroll-view" };
                sv.AddToClassList("status-filter-dropdown__scroll");
                rootVisualElement.Add(sv);
            }

            m_SelectAllToggle = rootVisualElement.Q<Toggle>("status-filter-dropdown__select-all-toggle");
            m_SelectAllToggle.text = UIContentData.TextContent.StatusFilterSelectAll;
            m_Separator = rootVisualElement.Q<VisualElement>("status-filter-dropdown__separator-element");
            m_ScrollView = rootVisualElement.Q<ScrollView>("status-filter-dropdown__scroll-view");

            m_SkeletonCreated = true;
            if (m_InitializeCalled)
                PopulateGroups();
        }

        // Populates the scroll view with group nodes. Called from Initialize() once the skeleton
        // exists — safe to call regardless of which fired first (CreateGUI or Initialize).
        void PopulateGroups()
        {
            if (m_GroupsPopulated)
                return;
            m_GroupsPopulated = true;

            // Groups are registered in English-alphabetical order in ObjectStatusExtensions so no
            // sort is needed for the default (untranslated) case. A single linear pass checks whether
            // translations have changed the order; only then is an O(N log N) sort performed.
            var names = ObjectStatusExtensions.StatusMaskChoiceNames;
            var tooltips = ObjectStatusExtensions.StatusMaskChoiceTooltips;
            var values = ObjectStatusExtensions.StatusMaskValues;
            m_GroupNodes.Clear();
            for (int i = 0; i < ObjectStatusExtensions.CountOfGroupMaskChoices; i++)
            {
                var menuEntry = values[i];
                if (menuEntry == 0 || menuEntry == ObjectStatusExtensions.AllStatuses)
                    continue;
                var node = new StatusFilterDropDownGroupNode(this, names[i], tooltips[i], menuEntry, m_UnavailableStatusesMask);
                // If every sub-item in this group is unavailable in the current table, omit the group entirely.
                if (!node.HasEnabledItems)
                    continue;
                m_GroupNodes.Add(node);
            }

            bool needsSort = false;
            for (int i = 1; i < m_GroupNodes.Count; i++)
            {
                if (string.Compare(m_GroupNodes[i - 1].GroupName, m_GroupNodes[i].GroupName, System.StringComparison.CurrentCultureIgnoreCase) > 0)
                {
                    needsSort = true;
                    break;
                }
            }
            if (needsSort)
                m_GroupNodes.Sort((a, b) => string.Compare(a.GroupName, b.GroupName, System.StringComparison.CurrentCultureIgnoreCase));

            m_AllShownStatuses = 0;
            foreach (var node in m_GroupNodes)
            {
                m_AllShownStatuses |= node.GroupValue;
                m_ScrollView.Add(node);
            }

            // Reconcile the initial mask: drop bits from groups not shown here, then link each
            // group's disabled bits to its interactive ones. Assigning via the property fires the
            // update event so every toggle (group, interactive, and disabled) syncs to this state.
            var reconciled = m_CurrentMaskValue & m_AllShownStatuses;
            foreach (var node in m_GroupNodes)
                reconciled = node.ReconcileDisabledBits(reconciled);
            CurrentMaskValue = reconciled;

            // ── Wire up the "Select all" toggle ───────────────────────────────
            UpdateSelectAllState();
            m_SelectAllToggle.RegisterCallback<ChangeEvent<bool>>(evt =>
                CurrentMaskValue = evt.newValue ? m_AllShownStatuses : 0);
            OnCurrentMaskValueUpdated += _ => UpdateSelectAllState();

            maxSize = minSize = new Vector2(k_WindowWidth, 50f);
            rootVisualElement.schedule.Execute(ResizeToFit).ExecuteLater(0);
        }

        void UpdateSelectAllState()
        {
            // Compare against the set of statuses actually shown in this table.
            var shownMask = m_AllShownStatuses != 0
                ? m_CurrentMaskValue & m_AllShownStatuses
                : m_CurrentMaskValue;
            var allShown = m_AllShownStatuses != 0 ? m_AllShownStatuses : ObjectStatusExtensions.AllStatuses;
            if (shownMask == allShown)
            {
                m_SelectAllToggle.SetValueWithoutNotify(true);
                m_SelectAllToggle.showMixedValue = false;
            }
            else if (shownMask == 0)
            {
                m_SelectAllToggle.SetValueWithoutNotify(false);
                m_SelectAllToggle.showMixedValue = false;
            }
            else
            {
                m_SelectAllToggle.SetValueWithoutNotify(false);
                m_SelectAllToggle.showMixedValue = true;
            }
        }

        public void ResizeToFit()
        {
            const long k_UpdateDelay = 1;
            if (m_ScrollView?.contentContainer == null)
            {
                rootVisualElement.schedule.Execute(ResizeToFit).ExecuteLater(k_UpdateDelay);
                return;
            }

            var contentRect = m_ScrollView.contentContainer.GetRect();
            if (float.IsNaN(contentRect.size.y) || contentRect.size.y <= 0f)
            {
                rootVisualElement.schedule.Execute(ResizeToFit).ExecuteLater(k_UpdateDelay);
                return;
            }
            bool needsScroll = contentRect.size.y > k_MaxScrollHeight;
            m_ScrollView.verticalScrollerVisibility = needsScroll
                ? ScrollerVisibility.AlwaysVisible
                : ScrollerVisibility.Hidden;

            // Compute each element's full contribution to the window height:
            // layout.height = content + padding + border (from UIElements box model)
            // Add margins separately — they are not included in layout.height.
            static float FullHeight(VisualElement e)
            {
                if (e == null) return 0f;
                var s = e.resolvedStyle;
                return e.layout.height + s.paddingTop + s.paddingBottom + s.marginTop + s.marginBottom;
            }

            var scrollHeight = needsScroll ? k_MaxScrollHeight : contentRect.size.y;
            var scrollContrib = scrollHeight
                + m_ScrollView.resolvedStyle.paddingTop + m_ScrollView.resolvedStyle.paddingBottom
                + m_ScrollView.resolvedStyle.marginTop + m_ScrollView.resolvedStyle.marginBottom;
            maxSize = minSize = new Vector2(k_WindowWidth, scrollContrib + FullHeight(m_SelectAllToggle) + FullHeight(m_Separator));

            if (!m_SubscribedToUIUpdates)
            {
                m_ScrollView.contentContainer.RegisterCallback<GeometryChangedEvent>(_ =>
                    rootVisualElement.schedule.Execute(ResizeToFit).ExecuteLater(k_UpdateDelay));
                m_SubscribedToUIUpdates = true;
            }
        }

        void OnDisable()
        {
            OnMaskChoiceConfirmed?.Invoke(CurrentMaskValue);
        }
    }
}

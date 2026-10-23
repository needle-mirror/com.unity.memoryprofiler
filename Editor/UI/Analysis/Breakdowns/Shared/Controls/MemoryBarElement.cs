using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Memory usage bar element (bar).
    /// It doesn't have backround and relies on parent to provide all elements apart form the bar part.
    /// It's used when:
    /// - You have multiple bar elements (like in Summary view) in a single bar
    /// - You want to have custom background and/or container
    ///
    /// For everything else use MemoryBar, as it's a subclass that
    /// provides usage bar with container
    /// </summary>
#if UNITY_6000_0_OR_NEWER
    [UxmlElement]
#endif
    partial class MemoryBarElement : VisualElement
    {
        const string k_UxmlClass = "memory-bar-element";
        const string k_UxmlClass_BaseBar = k_UxmlClass + "__committed-bar";
        const string k_UxmlClass_InnerBar = k_UxmlClass + "__resident-bar";
        const string k_UxmlClass_InnerBarVisible = k_UxmlClass + "__resident-bar-visible";
        const string k_UxmlClass_DeEmphasisTint = k_UxmlClass + "__de-emphasis-tint";
        const string k_UxmlClass_TintedRemainder = k_UxmlClass + "__tinted-remainder";

        const string k_UxmlStyle_CategoryColor = "memory-category-color__";

        [System.Flags]
        public enum VisibilityMode
        {
            None = 0,

            /// <summary>Size the bar by the resident size instead of the committed size.</summary>
            SizeByResident = 1 << 0,
            /// <summary>Draw the resident portion inside the committed bar.</summary>
            ResidentBar = 1 << 1,
            /// <summary>Only draw the <see cref="ResidentBar"/> while hovered.</summary>
            ResidentBarOnHoverOnly = 1 << 2,
            CommittedInTooltip = 1 << 3,
            ResidentInTooltip = 1 << 4,

            CommittedOnly = CommittedInTooltip,
            ResidentOnly = SizeByResident | ResidentInTooltip,
            CommittedAndResident = ResidentBar | CommittedInTooltip | ResidentInTooltip,
            CommittedAndResidentOnHover = CommittedAndResident | ResidentBarOnHoverOnly,
            ResidentOverCommitted = ResidentBar | ResidentInTooltip,
            ResidentOnlyWithCommittedTooltip = ResidentOnly | CommittedInTooltip,
        }

        bool m_HoveredState;
        VisibilityMode m_VisibilityMode;
        MemorySize m_MemorySize;
        BackgroundPattern m_BaseBar;
        VisualElement m_InnerBar;

        public MemoryBarElement()
        {
            AddToClassList(k_UxmlClass);

            m_BaseBar = new BackgroundPattern()
            {
                style =
                {
                    flexGrow = 1,
                },
                Scale = 0.5f
            };
            m_BaseBar.AddToClassList(k_UxmlClass_BaseBar);
            Add(m_BaseBar);

            // Must be added before the inner bar so the inner bar paints over it, tinting only the committed remainder.
            var deEmphasisTint = new VisualElement();
            deEmphasisTint.AddToClassList(k_UxmlClass_DeEmphasisTint);
            m_BaseBar.Add(deEmphasisTint);

            m_InnerBar = new VisualElement()
            {
                style =
                {
                    flexGrow = 1,
                }
            };
            m_InnerBar.AddToClassList(k_UxmlClass_InnerBar);
            m_BaseBar.Add(m_InnerBar);

            m_VisibilityMode = VisibilityMode.CommittedAndResident;
            UpdateState();

            RegisterCallback<MouseEnterEvent>((e) => { m_HoveredState = true; UpdateState(); });
            RegisterCallback<MouseLeaveEvent>((e) => { m_HoveredState = false; UpdateState(); });
        }

        public VisibilityMode Mode
        {
            get => m_VisibilityMode;
            set
            {
                if (m_VisibilityMode == value)
                    return;

                m_VisibilityMode = value;
                UpdateState();
            }
        }

        /// <summary>
        /// Draw the committed portion not covered by the resident bar in the category color under a translucent
        /// grey, instead of a separate darker per-category color. Only applies while the resident bar is visible.
        /// </summary>
        public bool DeEmphasizeNonResidentPortion
        {
            get => ClassListContains(k_UxmlClass_TintedRemainder);
            set => EnableInClassList(k_UxmlClass_TintedRemainder, value);
        }

        // size - allocated/resident size to show
        // total - total allocated memory in bar
        // maxValue - total allocated across all bars if multiple shown (compare)
        public void Set(string name, MemorySize size, ulong total, ulong maxValue,
            in MemorySummaryDataOverrides dataOverrides = default)
        {
            m_MemorySize = size;

            // Apply size to the container, rather than BaseBar
            // to make it work inside containers which contain multiple
            // MemoryUsageBar elements
            var outerValue = Has(VisibilityMode.SizeByResident) ? size.Resident : size.Committed;
            var outerElementSize = maxValue > 0 ? (float)outerValue / maxValue : 0;
            SetWidthClamped(this, outerElementSize);

            var residentOfTotal = size.Committed > 0 ? (float)size.Resident / size.Committed : 0;
            SetWidthClamped(m_InnerBar, residentOfTotal);

            bool showAllocated = Has(VisibilityMode.CommittedInTooltip);
            bool showResident = Has(VisibilityMode.ResidentInTooltip);
            tooltip = MemorySizeTooltipBuilder.MakeTooltip(name, size, total, showAllocated, showResident, string.Empty, in dataOverrides);

            UpdateState();
        }

        public void SetStyle(string id)
        {
            m_BaseBar.AddToClassList(k_UxmlStyle_CategoryColor + id);
            m_InnerBar.AddToClassList(k_UxmlStyle_CategoryColor + id);
        }

        void SetWidthClamped(VisualElement elem, float width)
        {
            var clampedWidth = Mathf.Clamp01(width);
            elem.style.width = new StyleLength(Length.Percent(clampedWidth * 100));
        }

        bool Has(VisibilityMode flag) => (m_VisibilityMode & flag) != 0;

        void UpdateState()
        {
            if (!Has(VisibilityMode.ResidentBar))
            {
                m_InnerBar.style.visibility = Visibility.Hidden;
                RemoveFromClassList(k_UxmlClass_InnerBarVisible);
                return;
            }

            m_InnerBar.style.visibility = Visibility.Visible;

            if (Has(VisibilityMode.ResidentBarOnHoverOnly) && !m_HoveredState)
                RemoveFromClassList(k_UxmlClass_InnerBarVisible);
            else
                AddToClassList(k_UxmlClass_InnerBarVisible);
        }

#if !UNITY_6000_0_OR_NEWER
        public new class UxmlFactory : UxmlFactory<MemoryBarElement> { }
#endif
    }
}

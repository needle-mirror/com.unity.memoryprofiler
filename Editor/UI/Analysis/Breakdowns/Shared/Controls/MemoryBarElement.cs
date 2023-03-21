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
    class MemoryBarElement : VisualElement
    {
        const string k_UxmlClass = "memory-bar-element";
        const string k_UxmlClass_BaseBar = k_UxmlClass + "__committed-bar";
        const string k_UxmlClass_InnerBar = k_UxmlClass + "__resident-bar";
        const string k_UxmlClass_InnerBarVisible = k_UxmlClass + "__resident-bar-visible";

        const string k_UxmlStyle_CategoryColor = "memory-category-color__";

        public enum VisibilityMode
        {
            CommittedOnly,
            ResidentOnly,
            CommittedAndResident,
            CommittedAndResidentOnHover,
            ResidentOverCommitted
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

        // size - allocated/resident size to show
        // total - total allocated memory in bar
        // maxValue - total allocated across all bars if multiple shown (compare)
        public void Set(string name, MemorySize size, ulong total, ulong maxValue)
        {
            m_MemorySize = size;

            // Apply size to the container, rather than BaseBar
            // to make it work inside containers which contain multiple
            // MemoryUsageBar elements
            var outerValue = m_VisibilityMode != VisibilityMode.ResidentOnly ? size.Committed : size.Resident;
            var outerElementSize = maxValue > 0 ? (float)outerValue / maxValue : 0;
            SetWidthClamped(this, outerElementSize);

            var residentOfTotal = size.Committed > 0 ? (float)size.Resident / size.Committed : 0;
            SetWidthClamped(m_InnerBar, residentOfTotal);

            bool showAllocated = (m_VisibilityMode != VisibilityMode.ResidentOnly) && (m_VisibilityMode != VisibilityMode.ResidentOverCommitted);
            bool showResident = m_VisibilityMode != VisibilityMode.CommittedOnly;
            tooltip = MemorySizeTooltipBuilder.MakeTooltip(name, size, total, showAllocated, showResident, string.Empty);

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

        void UpdateState()
        {
            if ((m_VisibilityMode == VisibilityMode.CommittedOnly) || (m_VisibilityMode == VisibilityMode.ResidentOnly))
            {
                m_InnerBar.style.visibility = Visibility.Hidden;
                RemoveFromClassList(k_UxmlClass_InnerBarVisible);
                return;
            }

            m_InnerBar.style.visibility = Visibility.Visible;

            if (((m_VisibilityMode == VisibilityMode.CommittedAndResidentOnHover) && !m_HoveredState))
                RemoveFromClassList(k_UxmlClass_InnerBarVisible);
            else
                AddToClassList(k_UxmlClass_InnerBarVisible);
        }

        public new class UxmlFactory : UxmlFactory<MemoryBarElement> { }
    }
}

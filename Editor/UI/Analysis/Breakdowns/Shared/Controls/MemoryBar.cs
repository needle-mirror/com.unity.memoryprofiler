using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Memory usage bar control.
    /// Use where you need allocated/resident bar.
    /// </summary>
    class MemoryBar : VisualElement
    {
        const string k_UxmlClass = "memory-bar";

        MemoryBarElement m_Element;

        public MemoryBar()
        {
            AddToClassList(k_UxmlClass);

            m_Element = new MemoryBarElement();
            Add(m_Element);
        }

        public MemoryBarElement.VisibilityMode Mode
        {
            get => m_Element.Mode;
            set => m_Element.Mode = value;
        }

        // size - allocated/resident size to show
        // total - total allocated memory in bar
        // maxValue - total allocated across all bars if multiple shown (compare)
        public void Set(MemorySize size, ulong total, ulong maxValue)
        {
            m_Element.Set(string.Empty, size, total, maxValue);
            tooltip = m_Element.tooltip;
            UIElementsHelper.SetElementDisplay(m_Element, true);
        }

        public void SetEmpty()
        {
            UIElementsHelper.SetElementDisplay(m_Element, false);
            tooltip = "";
        }
    }
}

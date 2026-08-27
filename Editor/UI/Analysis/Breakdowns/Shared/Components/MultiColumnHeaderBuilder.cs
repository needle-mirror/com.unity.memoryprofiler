using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// UIToolkit class/name constants for the multi-column header DOM structure, shared by every column manager
    /// that rebuilds its own header elements (<see cref="TreeViewColumnManager{TItemData}"/>,
    /// <see cref="ComparisonTableColumnManager{TItemData}"/>) to keep the default header styling.
    /// </summary>
    static class MultiColumnHeaderClassNames
    {
        internal const string ColumnHeaderContainer = "unity-multi-column-header__column";
        internal const string ColumnHeaderContent = "unity-multi-column-header__column__content";
        internal const string ColumnHeaderDefaultContent = "unity-multi-column-header__column__default-content";
        internal const string ColumnHeaderContentHasTitle = "unity-multi-column-header__column__content--has-title";
        internal const string ColumnHeaderContentHasIcon = "unity-multi-column-header__column__content--has-icon";
        internal const string ColumnHeaderIconName = "unity-multi-column-header-column-icon";
        internal const string ColumnHeaderIcon = "unity-multi-column-header__column__icon";
        internal const string ColumnHeaderTitleName = "unity-multi-column-header-column-title";
        internal const string ColumnHeaderTitle = "unity-multi-column-header__column__title";
    }

    /// <summary>
    /// Builds and binds a replacement multi-column header VisualElement, replicating the tree view's default
    /// header DOM structure and class names so the custom header keeps the default styling. The default header's
    /// title label has picking mode Ignore, so its tooltip never shows; overriding makeHeader/bindHeader (rather
    /// than querying the default header afterwards) is what makes tooltips work while keeping header look and
    /// behavior under the column manager's control.
    /// </summary>
    static class MultiColumnHeaderBuilder
    {
        public static VisualElement MakeHeader(out Label titleLabel, out Image iconImage)
        {
            var header = new VisualElement();
            header.AddToClassList(MultiColumnHeaderClassNames.ColumnHeaderDefaultContent);
            header.AddToClassList(MultiColumnHeaderClassNames.ColumnHeaderContent);
            header.AddToClassList(MultiColumnHeaderClassNames.ColumnHeaderContentHasTitle);

            iconImage = new Image { name = MultiColumnHeaderClassNames.ColumnHeaderIconName };
            iconImage.AddToClassList(MultiColumnHeaderClassNames.ColumnHeaderIcon);

            titleLabel = new Label { name = MultiColumnHeaderClassNames.ColumnHeaderTitleName };
            titleLabel.AddToClassList(MultiColumnHeaderClassNames.ColumnHeaderTitle);

            header.Add(iconImage);
            header.Add(titleLabel);
            return header;
        }

        /// <summary>
        /// Applies title/icon/tooltip to a header built by <see cref="MakeHeader"/>. Returns the element the
        /// tooltip was applied to (the ancestor <c>unity-multi-column-header__column</c> container when found,
        /// otherwise the header itself) so callers can cache it and update the tooltip directly later without
        /// re-querying the tree view.
        /// </summary>
        public static VisualElement Bind(VisualElement header, string title, string tooltip, Texture2D icon)
        {
            var label = header.Q<Label>(MultiColumnHeaderClassNames.ColumnHeaderTitleName);
            if (label != null)
                label.text = title;

            var iconImage = header.Q<Image>(MultiColumnHeaderClassNames.ColumnHeaderIconName);
            if (icon != null)
            {
                if (iconImage != null)
                    iconImage.image = icon;
                header.AddToClassList(MultiColumnHeaderClassNames.ColumnHeaderContentHasIcon);
            }
            else
            {
                if (iconImage != null)
                    iconImage.image = null;
                header.RemoveFromClassList(MultiColumnHeaderClassNames.ColumnHeaderContentHasIcon);
            }

            // Ideally the tooltip should be set on the parent column header container so it shows up when
            // hovering any part of the header, but this relies on the current class naming. As a fallback we set
            // it on the header itself if we can't find the parent container.
            var tooltipTarget = header;
            while (tooltipTarget != null && !tooltipTarget.ClassListContains(MultiColumnHeaderClassNames.ColumnHeaderContainer))
                tooltipTarget = tooltipTarget.parent;
            if (tooltipTarget == null)
                tooltipTarget = header;

            tooltipTarget.tooltip = tooltip ?? title;
            return tooltipTarget;
        }
    }
}

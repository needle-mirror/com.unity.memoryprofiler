using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System;
using Unity.MemoryProfiler.Editor.UI;
using Unity.MemoryProfiler.Editor.UIContentData;

namespace Unity.MemoryProfiler.Editor
{
    internal class OpenSnapshotsWindow : VisualElement
    {
        public event Action SwapOpenSnapshots = delegate {};
        public event Action ShowDiffOfOpenSnapshots = delegate {};
        public event Action ShowFirstOpenSnapshot = delegate {};
        public event Action ShowSecondOpenSnapshot = delegate {};
        public event Action<bool> CompareModeChanged = delegate {};

        class OpenSnapshotItemUI
        {
            public VisualElement Item;
            public Image Image;
            public Image PlatformIcon;
            public Image EditorPlatformIcon;
            public VisualElement NoData;
            public Label NoDataLabel;
            public VisualElement DataContainer;
            public Label Name;
            public Label Date;
            public MemoryUsageDial MemoryUsageDial;
            //public Label Age;
            public DateTime UtcDateTime;

            // Rework UI
            public Label ProjectName;
            public Label SessionName;
            public VisualElement TotalAvailableBar;
            public VisualElement TotalUseddBar;
            public Label TotalUsedMemory;
            public Label TotalAvailableMemory;
        }
        OpenSnapshotItemUI m_OpenSnapshotItemUIFirst = new OpenSnapshotItemUI();
        OpenSnapshotItemUI m_OpenSnapshotItemUISecond = new OpenSnapshotItemUI();
        Button m_DiffButton;

        VisualElement m_SnappshotViewSeparator;
        VisualElement m_SwapButtonHolder;

        public bool CompareMode { get { return m_CompareSnapshotMode; } }
        bool m_CompareSnapshotMode = false;
        Ribbon m_Ribbon;


        VisualElement m_FirstSnapshotHolder;
        VisualElement m_FirstSnapshotHolderEmpty;
        VisualElement m_SecondSnapshotHolder;
        VisualElement m_SecondSnapshotHolderEmpty;

        VisualElement m_TagA;

        const string k_UxmlPath = "Packages/com.unity.memoryprofiler/Package Resources/UXML/OpenSnapshotsWindow.uxml";
        const string k_CommonStyleSheetPath = "Packages/com.unity.memoryprofiler/Package Resources/StyleSheets/OpenSnapshotsWindow_style.uss";
        const string k_OpenSnapshotItemUxmlPath = "Packages/com.unity.memoryprofiler/Package Resources/UXML/OpenSnapshotItem.uxml";

        bool firstIsOpen;
        bool secondIsOpen;

        public OpenSnapshotsWindow(float initialWidth, VisualElement root)
        {
            var holderA = root.Q<VisualElement>("open-snapshot-item-a");
            m_FirstSnapshotHolder = holderA.Q<VisualElement>("open-snapshot", "open-snapshot__container");
            m_FirstSnapshotHolderEmpty = holderA.Q<VisualElement>("no-snapshot-loaded", "open-snapshot__container");

            m_TagA = holderA.Q<Label>("open-snapshot__compare-tag");

            UIElementsHelper.SetVisibility(m_TagA, false);
            UIElementsHelper.SetVisibility(m_FirstSnapshotHolder, false);
            UIElementsHelper.SetVisibility(m_FirstSnapshotHolderEmpty, true);

            var holderB = root.Q<VisualElement>("open-snapshot-item-b");
            m_SecondSnapshotHolder = holderB.Q<VisualElement>("open-snapshot", "open-snapshot__container");
            m_SecondSnapshotHolderEmpty = holderB.Q<VisualElement>("no-snapshot-loaded", "open-snapshot__container");

            var tagB = holderB.Q<Label>("open-snapshot__compare-tag");
            tagB.SwitchClasses(classToAdd: GeneralStyles.ImageTintColorClassSnapshotB, classToRemove: GeneralStyles.ImageTintColorClassSnapshotA);
            tagB.text = "B";

            UIElementsHelper.SetVisibility(m_SecondSnapshotHolder, false);
            UIElementsHelper.SetVisibility(m_SecondSnapshotHolderEmpty, false);

            m_SnappshotViewSeparator = root.Q<VisualElement>("open-snapshot-view__separator");
            m_SwapButtonHolder = root.Q<VisualElement>("swap-snapshot-buttons-holder");
            m_SwapButtonHolder.Q<Button>("swap-snapshots-button").clicked += OnSwapOpenSnapshots;

            UIElementsHelper.SetVisibility(m_SnappshotViewSeparator, false);
            UIElementsHelper.SetVisibility(m_SwapButtonHolder, false);
            UIElementsHelper.SetVisibility(m_TagA, false);

            m_Ribbon = root.Q<Ribbon>("snapshot-window__ribbon__container");
            m_Ribbon.Clicked += RibbonButtonStateChanged;

            m_Ribbon.HelpClicked += () => Application.OpenURL(DocumentationUrls.OpenSnapshotsPane);

            InitializeOpenSnapshotItem(m_FirstSnapshotHolder, ref m_OpenSnapshotItemUIFirst, () => ShowFirstOpenSnapshot());
            InitializeOpenSnapshotItem(m_SecondSnapshotHolder, ref m_OpenSnapshotItemUISecond, () => ShowSecondOpenSnapshot());
            UIElementsHelper.SetVisibility(m_SecondSnapshotHolderEmpty, false);
        }

        void RibbonButtonStateChanged(int i)
        {
            if (i == 0)
                OnSingleSnapshotRibbonButtonClicked();
            else
                OnCompareSnapshotRibbonButtonClicked();
        }

        void OnSingleSnapshotRibbonButtonClicked()
        {
            if (m_CompareSnapshotMode)
            {
                m_CompareSnapshotMode = !m_CompareSnapshotMode;

                SetSecondSnapshotInvisible();
                ShowFirstOpenSnapshot();
                CompareModeChanged(m_CompareSnapshotMode);
            }
        }

        void SetSecondSnapshotInvisible()
        {
            UIElementsHelper.SetVisibility(m_SecondSnapshotHolder, false);
            UIElementsHelper.SetVisibility(m_SecondSnapshotHolderEmpty, false);
            UIElementsHelper.SetVisibility(m_SnappshotViewSeparator, false);
            UIElementsHelper.SetVisibility(m_SwapButtonHolder, false);
            UIElementsHelper.SetVisibility(m_TagA, false);
        }

        void OnCompareSnapshotRibbonButtonClicked()
        {
            if (!m_CompareSnapshotMode)
            {
                m_CompareSnapshotMode = !m_CompareSnapshotMode;

                UIElementsHelper.SetVisibility(m_SecondSnapshotHolder, secondIsOpen);
                UIElementsHelper.SetVisibility(m_SecondSnapshotHolderEmpty, !secondIsOpen);
                UIElementsHelper.SetVisibility(m_SnappshotViewSeparator, true);
                UIElementsHelper.SetVisibility(m_SwapButtonHolder, true);
                UIElementsHelper.SetVisibility(m_TagA, true);
                if (firstIsOpen && secondIsOpen)
                    ShowDiffOfOpenSnapshots();
                CompareModeChanged(m_CompareSnapshotMode);
            }
        }

        void OnSwapOpenSnapshots()
        {
            SwapOpenSnapshots();
        }

        void InitializeOpenSnapshotItem(VisualElement snapshotHolder, ref OpenSnapshotItemUI openSnapshotItemUI, Action openSnapshotHandler)
        {
            VisualElement item = snapshotHolder;
            openSnapshotItemUI.ProjectName = item.Q<Label>("open-snapshot__project-name");
            openSnapshotItemUI.SessionName = item.Q<Label>("open-snapshot__session-name");
            openSnapshotItemUI.TotalUseddBar = item.Q("total-ram-usage-bar__used-bar");
            openSnapshotItemUI.TotalAvailableBar = item.Q("total-ram-usage-bar");
            openSnapshotItemUI.TotalUsedMemory = item.Q<Label>("open-snapshot__total-ram-used");
            openSnapshotItemUI.TotalAvailableMemory = item.Q<Label>("open-snapshot__total-hardware-resources");

            //item.AddManipulator(new Clickable(openSnapshotHandler));
            openSnapshotItemUI.Item = item;
            openSnapshotItemUI.Image =  item.Q<Image>("preview-image-open-item");
            openSnapshotItemUI.Image.scaleMode = ScaleMode.ScaleToFit;
            openSnapshotItemUI.PlatformIcon = item.Q<Image>("preview-image__platform-icon", GeneralStyles.PlatformIconClassName);
            openSnapshotItemUI.EditorPlatformIcon = item.Q<Image>("preview-image__editor-icon", GeneralStyles.PlatformIconClassName);
            openSnapshotItemUI.NoDataLabel = item.Q<Label>("no-snapshot-loaded-text");
            openSnapshotItemUI.NoData = item.parent.Q<VisualElement>("no-snapshot-loaded" , "open-snapshot__container");
            openSnapshotItemUI.Name = item.Q<Label>("snapshot-name");
            openSnapshotItemUI.DataContainer = item.Q<VisualElement>("open-snapshot", "open-snapshot__container");
            openSnapshotItemUI.Date = item.Q<Label>("snapshot-date");
            openSnapshotItemUI.MemoryUsageDial = item.Q<MemoryUsageDial>();
            UIElementsHelper.SetVisibility(openSnapshotItemUI.PlatformIcon, false);
            UIElementsHelper.SetVisibility(openSnapshotItemUI.EditorPlatformIcon, false);
            UIElementsHelper.SetVisibility(openSnapshotItemUI.NoData, true);
            UIElementsHelper.SetVisibility(openSnapshotItemUI.DataContainer, false);
        }

        public void SetSnapshotUIData(bool first, SnapshotFileGUIData snapshotGUIData, bool isInView)
        {
            OpenSnapshotItemUI itemUI = m_OpenSnapshotItemUIFirst;
            if (!first)
            {
                itemUI = m_OpenSnapshotItemUISecond;
            }
            if (snapshotGUIData == null)
            {
                UIElementsHelper.SwitchVisibility(itemUI.NoData, itemUI.DataContainer);
                itemUI.Name.text = "";
                itemUI.Date.text = "";
                itemUI.ProjectName.text = "";
                itemUI.ProjectName.tooltip = "";
                itemUI.SessionName.text = "";
                itemUI.Image.image = null;
                UIElementsHelper.SetVisibility(itemUI.PlatformIcon, false);
                UIElementsHelper.SetVisibility(itemUI.EditorPlatformIcon, false);
                itemUI.UtcDateTime = default(DateTime);

                itemUI.MemoryUsageDial.Percentage = 0;
            }
            else
            {
                UIElementsHelper.SwitchVisibility(itemUI.DataContainer, itemUI.NoData);
                itemUI.Name.text = snapshotGUIData.Name;
                itemUI.Date.text = snapshotGUIData.Date;
                itemUI.Image.image = snapshotGUIData.MetaScreenshot;
                UIElementsHelper.SetVisibility(itemUI.PlatformIcon, true);
                SnapshotsWindow.SetPlatformIcons(itemUI.Item, snapshotGUIData);
                itemUI.UtcDateTime = snapshotGUIData.UtcDateTime;

                itemUI.ProjectName.text = snapshotGUIData.ProductName;
                itemUI.SessionName.text = snapshotGUIData.SessionName;
                itemUI.SessionName.tooltip = $"{itemUI.SessionName.text} - Unity {snapshotGUIData.UnityVersion}";
                ulong totalAvailableMemory = 0;
                if (snapshotGUIData.TargetInfo.HasValue)
                {
                    UIElementsHelper.SetVisibility(itemUI.TotalAvailableMemory, true);
                    UIElementsHelper.SetVisibility(itemUI.TotalAvailableBar, true);
                    var info = snapshotGUIData.TargetInfo.Value;

                    totalAvailableMemory = PlatformsHelper.GetPlatformSpecificTotalAvailableMemory(info);

                    var totalAvailableInfoText = PlatformsHelper.GetPlatformSpecificTotalAvailableMemoryText(info);
                    itemUI.TotalAvailableBar.tooltip = totalAvailableInfoText.tooltip;
                    itemUI.TotalAvailableMemory.tooltip = totalAvailableInfoText.tooltip;
                    itemUI.TotalAvailableMemory.text = totalAvailableInfoText.text;
                }
                else
                {
                    UIElementsHelper.SetVisibility(itemUI.TotalAvailableMemory, false);
                    UIElementsHelper.SetVisibility(itemUI.TotalAvailableBar, false);
                }
                if (snapshotGUIData.MemoryStats.HasValue)
                {
                    UIElementsHelper.SetVisibility(itemUI.TotalUsedMemory, true);
                    var totalUsed = string.Format(TextContent.TotalUsedMemory,
                        EditorUtility.FormatBytes((long)snapshotGUIData.MemoryStats.Value.TotalVirtualMemory));
                    itemUI.TotalUseddBar.tooltip = totalUsed;

                    var totalKnownUsedMemory = snapshotGUIData.MemoryStats.Value.TotalVirtualMemory;
                    if (totalKnownUsedMemory == 0)
                    {
                        // Fallback for platforms where System Used Memory isn't implemented yet
                        totalKnownUsedMemory = snapshotGUIData.MemoryStats.Value.TotalReservedMemory;
                    }
                    // if more is used than available, we might have a non unified device in a mixed platform or otherwise mis-accounted the available space
                    // assume that the apps memory still fitted in when the snapshot was taken.
                    totalAvailableMemory = Math.Max(totalAvailableMemory, totalKnownUsedMemory);
                    float usagePercentage = (float)totalKnownUsedMemory / totalAvailableMemory * 100f;

                    itemUI.TotalUseddBar.style.SetBarWidthInPercent(usagePercentage);
                    itemUI.TotalUsedMemory.text = totalUsed;
                    itemUI.MemoryUsageDial.Percentage = Mathf.RoundToInt(usagePercentage);
                    UIElementsHelper.SetVisibility(itemUI.MemoryUsageDial.parent, true);
                }
                else
                {
                    UIElementsHelper.SetVisibility(itemUI.TotalUsedMemory, false);
                    UIElementsHelper.SetVisibility(itemUI.MemoryUsageDial.parent, false);
                }

                // TODO: Always hide the dial until we have correct memory pressure metrics.
                UIElementsHelper.SetVisibility(itemUI.MemoryUsageDial.parent, false);

                snapshotGUIData.SetCurrentState(true, first, CompareMode);
            }
            if (first)
                firstIsOpen = snapshotGUIData != null;
            else
            {
                secondIsOpen = snapshotGUIData != null;
                if (!CompareMode)
                    SetSecondSnapshotInvisible();
            }
        }

        string GetAgeDifference(DateTime dateTime, DateTime otherDateTime)
        {
            return dateTime.CompareTo(otherDateTime) < 0 ? "Old" : "New";
        }

        struct LabelWidthData
        {
            public readonly float Padding;
            public readonly float Margin;
            public readonly float TextWidth;

            float m_SnapshotMinWidth;
            Label m_Label;

            public float ExtraWidthNeeded
            {
                get
                {
                    return TextWidth + Padding + Margin - m_SnapshotMinWidth;
                }
            }

            public LabelWidthData(Label label, float snapshotMinWidth)
            {
                Padding = label.resolvedStyle.paddingLeft + label.resolvedStyle.paddingRight;
                Margin = label.resolvedStyle.marginLeft + label.resolvedStyle.marginRight;
                TextWidth = label.MeasureTextSize(label.text, label.resolvedStyle.width, MeasureMode.Undefined, 20, MeasureMode.Exactly).x;
                m_SnapshotMinWidth = snapshotMinWidth;
                m_Label = label;
            }

            public void SetMaxWidth(float maxWidth)
            {
                maxWidth -= Margin;
                m_Label.style.maxWidth = maxWidth;
                m_Label.style.minWidth = maxWidth;
                m_Label.style.unityTextAlign = (maxWidth >= ExtraWidthNeeded + m_SnapshotMinWidth) ? TextAnchor.UpperCenter : TextAnchor.UpperLeft;
            }
        }

        struct SnapshotUIWidthData
        {
            public readonly LabelWidthData NameData;
            public readonly LabelWidthData DateData;

            public float ExtraWidthNeeded
            {
                get
                {
                    return Mathf.Max(Mathf.Max(NameData.ExtraWidthNeeded, DateData.ExtraWidthNeeded), 0);
                }
            }

            public SnapshotUIWidthData(OpenSnapshotItemUI uiItem, float snapshotMinWidth)
            {
                NameData = new LabelWidthData(string.IsNullOrEmpty(uiItem.Name.text) ? uiItem.NoDataLabel : uiItem.Name, snapshotMinWidth);
                DateData = new LabelWidthData(uiItem.Date, snapshotMinWidth);
            }

            public void SetMaxWidth(float maxWidth)
            {
                NameData.SetMaxWidth(maxWidth);
                DateData.SetMaxWidth(maxWidth);
            }
        }

        internal void RefreshScreenshots(SnapshotFileGUIData guiDataFirst, SnapshotFileGUIData guiDataSecond)
        {
            if (m_OpenSnapshotItemUIFirst != null && m_OpenSnapshotItemUIFirst.Image != null)
            {
                var tex = Texture2D.blackTexture;
                if (guiDataFirst != null && guiDataFirst.GuiTexture.Texture != null)
                    tex = guiDataFirst.GuiTexture.Texture;

                m_OpenSnapshotItemUIFirst.Image.image = tex;
            }

            if (m_OpenSnapshotItemUISecond != null && m_OpenSnapshotItemUISecond.Image != null)
            {
                var tex = Texture2D.blackTexture;
                if (guiDataSecond != null && guiDataSecond.GuiTexture.Texture != null)
                    tex = guiDataSecond.GuiTexture.Texture;

                m_OpenSnapshotItemUISecond.Image.image = tex;
            }
        }

        public void UpdateSessionName(bool first, string name)
        {
            if (first)
                m_OpenSnapshotItemUIFirst.SessionName.text = name;
            else
                m_OpenSnapshotItemUISecond.SessionName.text = name;
        }
    }
}

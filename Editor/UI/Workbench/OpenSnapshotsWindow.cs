using System;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using Unity.MemoryProfiler.Editor.UI;
using Unity.MemoryProfiler.Editor.UIContentData;

namespace Unity.MemoryProfiler.Editor
{
    internal class OpenSnapshotsWindow : VisualElement
    {
        const string k_TotalResidentMemoryNotAvailable = "Total resident memory information isn't available for this snapshot. Use a newer version of Unity to capture this information.";

        public event Action SwapOpenSnapshots = delegate { };
        public event Action ShowDiffOfOpenSnapshots = delegate { };
        public event Action ShowFirstOpenSnapshot = delegate { };
        public event Action ShowSecondOpenSnapshot = delegate { };
        public event Action<bool> CompareModeChanged = delegate { };

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
            public DateTime UtcDateTime;

            // Rework UI
            public Label ProjectName;
            public Label SessionName;
            public VisualElement TotalAvailableBar;
            public VisualElement TotalResidentBar;
            public Label TotalUsedMemory;
            public Label TotalUsedMemoryNotAvailable;
            public Label TotalAvailableMemory;
        }
        OpenSnapshotItemUI m_OpenSnapshotItemUIFirst = new OpenSnapshotItemUI();
        OpenSnapshotItemUI m_OpenSnapshotItemUISecond = new OpenSnapshotItemUI();

        VisualElement m_SnappshotViewSeparator;

        public bool CompareMode { get { return m_CompareSnapshotMode; } }
        bool m_CompareSnapshotMode = false;
        Ribbon m_Ribbon;

        VisualElement m_FirstSnapshotHolder;
        VisualElement m_FirstSnapshotHolderEmpty;
        VisualElement m_SecondSnapshotHolder;
        VisualElement m_SecondSnapshotHolderEmpty;

        VisualElement m_TagA;

        bool firstIsOpen;
        bool secondIsOpen;

        public OpenSnapshotsWindow(float initialWidth, VisualElement root, Action closeFirstSnapshot, Action closeSecondSnapshot)
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

            UIElementsHelper.SetVisibility(m_SnappshotViewSeparator, false);
            UIElementsHelper.SetVisibility(m_TagA, false);

            m_Ribbon = root.Q<Ribbon>("snapshot-window__ribbon__container");
            m_Ribbon.Clicked += RibbonButtonStateChanged;

            m_Ribbon.HelpClicked += () => Application.OpenURL(DocumentationUrls.OpenSnapshotsPane);

            InitializeOpenSnapshotItem(m_FirstSnapshotHolder, ref m_OpenSnapshotItemUIFirst, closeFirstSnapshot);
            InitializeOpenSnapshotItem(m_SecondSnapshotHolder, ref m_OpenSnapshotItemUISecond, closeSecondSnapshot);
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

        void InitializeOpenSnapshotItem(VisualElement snapshotHolder, ref OpenSnapshotItemUI openSnapshotItemUI, Action closeSnapshotHandler)
        {
            VisualElement item = snapshotHolder;
            openSnapshotItemUI.ProjectName = item.Q<Label>("open-snapshot__project-name");
            openSnapshotItemUI.SessionName = item.Q<Label>("open-snapshot__session-name");
            openSnapshotItemUI.TotalResidentBar = item.Q("total-ram-usage-bar__used-bar");
            openSnapshotItemUI.TotalAvailableBar = item.Q("total-ram-usage-bar");
            openSnapshotItemUI.TotalUsedMemory = item.Q<Label>("open-snapshot__total-ram-used");
            openSnapshotItemUI.TotalUsedMemoryNotAvailable = item.Q<Label>("open-snapshot__total-ram-used-not-available");
            openSnapshotItemUI.TotalAvailableMemory = item.Q<Label>("open-snapshot__total-hardware-resources");

            openSnapshotItemUI.Item = item;
            openSnapshotItemUI.Image = item.Q<Image>("preview-image-open-item");
            openSnapshotItemUI.Image.scaleMode = ScaleMode.ScaleToFit;
            openSnapshotItemUI.PlatformIcon = item.Q<Image>("preview-image__platform-icon", GeneralStyles.PlatformIconClassName);
            openSnapshotItemUI.EditorPlatformIcon = item.Q<Image>("preview-image__editor-icon", GeneralStyles.PlatformIconClassName);
            openSnapshotItemUI.NoDataLabel = item.Q<Label>("no-snapshot-loaded-text");
            openSnapshotItemUI.NoData = item.parent.Q<VisualElement>("no-snapshot-loaded", "open-snapshot__container");
            openSnapshotItemUI.Name = item.Q<Label>("snapshot-name");
            openSnapshotItemUI.DataContainer = item.Q<VisualElement>("open-snapshot", "open-snapshot__container");
            openSnapshotItemUI.Date = item.Q<Label>("snapshot-date");
            var closeButton = item.Q<Button>("open-snapshot__close-button");
            closeButton.clicked += closeSnapshotHandler;
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

                itemUI.TotalUsedMemoryNotAvailable.tooltip = k_TotalResidentMemoryNotAvailable;

                if (snapshotGUIData.TargetInfo.HasValue)
                {
                    UIElementsHelper.SetVisibility(itemUI.TotalAvailableMemory, true);
                    var info = snapshotGUIData.TargetInfo.Value;

                    var totalAvailableMemory = info.TotalPhysicalMemory;
                    var text = string.Format(TextContent.TotalPhysicallyAvailableMemory.text, EditorUtility.FormatBytes((long)info.TotalPhysicalMemory));
                    var tooltip = string.Format(TextContent.TotalPhysicallyAvailableMemory.tooltip, EditorUtility.FormatBytes((long)info.TotalPhysicalMemory));
                    itemUI.TotalAvailableBar.tooltip = tooltip;
                    itemUI.TotalAvailableMemory.tooltip = tooltip;
                    itemUI.TotalAvailableMemory.text = text;

                    // Show resident/total usage bar only if both resident and total physical values are available
                    bool residentMemoryAvailable = snapshotGUIData.TotalResident > 0;
                    UIElementsHelper.SetVisibility(itemUI.TotalAvailableBar, residentMemoryAvailable);
                    UIElementsHelper.SetVisibility(itemUI.TotalUsedMemory, residentMemoryAvailable);
                    UIElementsHelper.SetVisibility(itemUI.TotalUsedMemoryNotAvailable, !residentMemoryAvailable);
                    if (residentMemoryAvailable)
                    {
                        var totalLabel = string.Format(TextContent.TotalResidentMemory,
                            EditorUtility.FormatBytes((long)snapshotGUIData.TotalResident));
                        itemUI.TotalUsedMemory.text = totalLabel;

                        // Resident/Physical memory bar
                        var totalResident = Math.Min(snapshotGUIData.TotalResident, totalAvailableMemory);
                        float usagePercentage = (float)totalResident / totalAvailableMemory * 100f;
                        itemUI.TotalResidentBar.style.SetBarWidthInPercent(usagePercentage);
                        itemUI.TotalResidentBar.tooltip = totalLabel;
                    }
                }
                else
                {
                    UIElementsHelper.SetVisibility(itemUI.TotalUsedMemory, false);
                    UIElementsHelper.SetVisibility(itemUI.TotalAvailableBar, false);
                    UIElementsHelper.SetVisibility(itemUI.TotalAvailableMemory, false);

                    UIElementsHelper.SetVisibility(itemUI.TotalUsedMemoryNotAvailable, true);
                }

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

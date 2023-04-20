using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal abstract class SnapshotFileBaseViewController : ViewController
    {
        const string k_UxmlNameLabel = "memory-profile-snapshotfile__meta-data__name";
        const string k_UxmlDateLabel = "memory-profile-snapshotfile__meta-data__date";
        const string k_UxmlPreviewImage = "memory-profile-snapshotfile__preview-image";
        const string k_UxmlPlatformIcon = "memory-profile-snapshotfile__preview-image__platform-icon";
        const string k_UxmlEditorPlatformIcon = "memory-profile-snapshotfile__preview-image__editor-icon";

        const string k_UxmlTotalBarAllocated = "memory-profile-snapshotfile__bar__allocated";
        const string k_UxmlTotalResidentLabel = "memory-profile-snapshotfile__bar__allocated-label";
        const string k_UxmlTotalAvailableLabel = "memory-profile-snapshotfile__bar__available-label";
        const string k_UxmlTotalDataNotAvailableLabel = "memory-profile-snapshotfile__bar__not-available-label";

        const string k_TotalMemoryNotAvailable = "N/A";
        const string k_Tooltip = "{0}\nPlatform: {1}\nUnity Version: {2}\n\nMax Available: {3}\nTotal Resident: {4}\nTotal Allocated: {5}";
        const string k_TooltipValueNotAvailable = "not available";

        // Model & state
        readonly SnapshotFileModel m_Model;
        readonly ScreenshotsManager m_ScreenshotsManager;

        // View
        string m_TotalResidentFormat = "{0}";
        string m_TotalAvailableFormat = "{0}";

        Label m_Name;
        Label m_Date;
        Image m_Screenshot;
        Image m_PlatformIcon;
        Image m_EditorPlatformIcon;

        VisualElement m_TotalBarAllocated;
        Label m_TotalLabelResident;
        Label m_TotalLabelAvailable;
        Label m_TotalLabelDataNotAvailable;

        public SnapshotFileBaseViewController(SnapshotFileModel model, ScreenshotsManager screenshotsManager)
        {
            m_Model = model;
            m_ScreenshotsManager = screenshotsManager;
        }

        public string TotalResidentFormat
        {
            get => m_TotalResidentFormat;
            set
            {
                m_TotalResidentFormat = value;
                if (IsViewLoaded)
                    RefreshView();
            }
        }
        public string TotalAvailableFormat
        {
            get => m_TotalAvailableFormat;
            set
            {
                m_TotalAvailableFormat = value;
                if (IsViewLoaded)
                    RefreshView();
            }
        }

        protected SnapshotFileModel Model => m_Model;

        protected Image ScreenshotImage => m_Screenshot;

        protected override void ViewLoaded()
        {
            base.ViewLoaded();
            RefreshView();
        }

        protected virtual void GatherReferencesInView(VisualElement view)
        {
            m_Name = view.Q<Label>(k_UxmlNameLabel);
            m_Date = view.Q<Label>(k_UxmlDateLabel);
            m_Screenshot = view.Q<Image>(k_UxmlPreviewImage);
            m_PlatformIcon = view.Q<Image>(k_UxmlPlatformIcon);
            m_EditorPlatformIcon = view.Q<Image>(k_UxmlEditorPlatformIcon);

            m_TotalBarAllocated = view.Q(k_UxmlTotalBarAllocated);
            m_TotalLabelResident = view.Q<Label>(k_UxmlTotalResidentLabel);
            m_TotalLabelAvailable = view.Q<Label>(k_UxmlTotalAvailableLabel);
            m_TotalLabelDataNotAvailable = view.Q<Label>(k_UxmlTotalDataNotAvailableLabel);
        }

        protected virtual void RefreshView()
        {
            if (m_Model == null)
                return;

            m_Name.text = m_Model.Name;
            m_PlatformIcon.image = PlatformsHelper.GetPlatformIcon(m_Model.Platform);
            UIElementsHelper.SetVisibility(m_EditorPlatformIcon, m_Model.EditorPlatform);

            var dateAsString = m_Model.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            m_Date.text = dateAsString;

            m_Screenshot.image = m_Model.Screenshot;
            m_Screenshot.scaleMode = ScaleMode.ScaleToFit;

            var screenshotPath = Path.ChangeExtension(Model.FullPath, ".png");
            if (File.Exists(screenshotPath))
            {
                var image = m_ScreenshotsManager.Enqueue(screenshotPath);
                ScreenshotImage.image = image;
            }

            RefreshTotal();
        }

        void RefreshTotal()
        {
            var totalAllocatedLabel = k_TooltipValueNotAvailable;
            var residentMemoryLabel = k_TooltipValueNotAvailable;
            var totalAvailableMemoryLabel = k_TooltipValueNotAvailable;
            if (m_Model.MemoryInformationAvailable)
            {
                if (m_Model.TotalResidentMemory > 0)
                {
                    UIElementsHelper.SetVisibility(m_TotalLabelResident, true);
                    UIElementsHelper.SetVisibility(m_TotalLabelDataNotAvailable, false);

                    var filledInPercent = ((float)m_Model.TotalResidentMemory / (float)m_Model.MaxAvailableMemory * 100f);
                    m_TotalBarAllocated.style.SetBarWidthInPercent(filledInPercent);

                    residentMemoryLabel = EditorUtility.FormatBytes((long)m_Model.TotalResidentMemory);
                    m_TotalLabelResident.text = string.Format(m_TotalResidentFormat, residentMemoryLabel);
                }
                else
                {
                    UIElementsHelper.SetVisibility(m_TotalBarAllocated, false);
                    UIElementsHelper.SetVisibility(m_TotalLabelResident, false);
                    UIElementsHelper.SetVisibility(m_TotalLabelDataNotAvailable, true);
                }

                if (m_Model.TotalAllocatedMemory > 0)
                    totalAllocatedLabel = EditorUtility.FormatBytes((long)m_Model.TotalAllocatedMemory);

                if (m_Model.MaxAvailableMemory > 0)
                {
                    totalAvailableMemoryLabel = EditorUtility.FormatBytes((long)m_Model.MaxAvailableMemory);
                    m_TotalLabelAvailable.text = string.Format(m_TotalAvailableFormat, totalAvailableMemoryLabel);
                }
            }
            else
            {
                // No information to show at all
                UIElementsHelper.SetVisibility(m_TotalBarAllocated, false);
                UIElementsHelper.SetVisibility(m_TotalLabelResident, false);
                UIElementsHelper.SetVisibility(m_TotalLabelAvailable, false);
                UIElementsHelper.SetVisibility(m_TotalLabelDataNotAvailable, true);
                m_TotalLabelDataNotAvailable.text = k_TotalMemoryNotAvailable;
            }

            View.tooltip = String.Format(k_Tooltip,
                m_Model.Name,
                m_Model.Platform,
                m_Model.UnityVersion,
                totalAvailableMemoryLabel,
                residentMemoryLabel,
                totalAllocatedLabel);
        }
    }
}

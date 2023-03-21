using System;
using System.IO;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class LoadedSnapshotsItemViewController : SnapshotFileBaseViewController
    {
        const string k_UxmlAssetGuid = "f9914b6108ad70640b3d2b989f3feb35";

        const string k_UxmlCloseButton = "memory-profile-snapshotfile__close";
        const string k_UxmlProductLabel = "memory-profile-snapshotfile__meta-data__project-name";
        const string k_UxmlSessionLabel = "memory-profile-snapshotfile__meta-data__session-name";
        const string k_UxmlOpenSnapshotTag = "memory-profile-snapshotfile__tag";
        const string k_UxmlNoDataContainer = "memory-profile-snapshotfile__no-data";
        const string k_UxmlImageAndMetaContainer = "memory-profile-snapshotfile__with-data";

        // State
        SnapshotDataService m_SnapshotDataService;

        // View
        Button m_CloseButton;
        Label m_ProductLabel;
        Label m_SessionLabel;
        Label m_OpenSnapshotTag;

        VisualElement m_NoDataContainer;
        VisualElement m_ImageAndMetaContainer;

        public LoadedSnapshotsItemViewController(SnapshotFileModel model, SnapshotDataService snapshotDataService, ScreenshotsManager screenshotsManager) :
            base(model, screenshotsManager)
        {
            m_SnapshotDataService = snapshotDataService;

            TotalResidentFormat = "Total Resident: {0}";
            TotalAvailableFormat = "Hardware Resources: {0}";
        }

        protected override VisualElement LoadView()
        {
            var view = ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlAssetGuid);
            if (view == null)
                throw new InvalidOperationException("Unable to create view from Uxml. Uxml must contain at least one child element.");

            GatherReferencesInView(view);

            return view;
        }

        protected override void GatherReferencesInView(VisualElement view)
        {
            base.GatherReferencesInView(view);

            m_CloseButton = view.Q<Button>(k_UxmlCloseButton);
            m_ProductLabel = view.Q<Label>(k_UxmlProductLabel);
            m_SessionLabel = view.Q<Label>(k_UxmlSessionLabel);
            m_OpenSnapshotTag = view.Q<Label>(k_UxmlOpenSnapshotTag);

            m_NoDataContainer = view.Q(k_UxmlNoDataContainer);
            m_ImageAndMetaContainer = view.Q(k_UxmlImageAndMetaContainer);
        }

        protected override void RefreshView()
        {
            base.RefreshView();

            if (Model == null)
            {
                UIElementsHelper.SetVisibility(m_NoDataContainer, true);
                UIElementsHelper.SetVisibility(m_ImageAndMetaContainer, false);
                return;
            }

            m_CloseButton.clickable.clicked += () => CloseCapture();

            m_ProductLabel.text = Model.ProductName;
            m_SessionLabel.text = m_SnapshotDataService.SessionNames[Model.SessionId];

            UIElementsHelper.SetVisibility(m_NoDataContainer, false);

            if (m_SnapshotDataService.CompareMode)
            {
                UIElementsHelper.SetVisibility(m_OpenSnapshotTag, true);
                if (PathHelpers.IsSamePath(m_SnapshotDataService.Base?.FullPath, Model.FullPath))
                    m_OpenSnapshotTag.text = "A";
                else if (PathHelpers.IsSamePath(m_SnapshotDataService.Compared?.FullPath, Model.FullPath))
                    m_OpenSnapshotTag.text = "B";
                else
                    UIElementsHelper.SetVisibility(m_OpenSnapshotTag, false);
            }
            else
                UIElementsHelper.SetVisibility(m_OpenSnapshotTag, false);
        }

        void CloseCapture()
        {
            m_SnapshotDataService.Unload(Model.FullPath);
        }
    }
}

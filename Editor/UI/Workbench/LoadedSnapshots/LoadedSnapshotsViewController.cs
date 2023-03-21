using System;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.MemoryProfiler.Editor.UI;
using Unity.MemoryProfiler.Editor.UIContentData;

namespace Unity.MemoryProfiler.Editor
{
    internal class LoadedSnapshotsViewController : ViewController
    {
        const string k_UxmlAssetGuid = "2fbcb3a795f2b2c4188681b0720ee76c";
        const string k_UxmlIdentifierRibbon = "memory-profiler-loaded-snapshots__ribbon";
        const string k_UxmlIdentifierBaseSnapshot = "memory-profiler-loaded-snapshots__item__base";
        const string k_UxmlIdentifierCompareSnapshot = "memory-profiler-loaded-snapshots__item__compare";

        // State
        SnapshotDataService m_SnapshotDataService;
        ScreenshotsManager m_ScreenshotsManager;

        // View
        Ribbon m_Ribbon;
        VisualElement m_BaseSnapshotContainer;
        VisualElement m_CompareSnapshotContainer;

        LoadedSnapshotsItemViewController m_BaseSnapshotController;
        LoadedSnapshotsItemViewController m_CompareSnapshotController;

        public LoadedSnapshotsViewController(SnapshotDataService snapshotDataService, ScreenshotsManager screenshotsManager)
        {
            m_SnapshotDataService = snapshotDataService;
            m_ScreenshotsManager = screenshotsManager;
        }

        protected override VisualElement LoadView()
        {
            var view = ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlAssetGuid);
            if (view == null)
                throw new InvalidOperationException("Unable to create view from Uxml. Uxml must contain at least one child element.");

            GatherReferencesInView(view);

            return view;
        }

        protected override void ViewLoaded()
        {
            base.ViewLoaded();
            SetupView();
            RefreshView();
        }

        void GatherReferencesInView(VisualElement view)
        {
            m_Ribbon = view.Q<Ribbon>(k_UxmlIdentifierRibbon);
            m_BaseSnapshotContainer = view.Q<VisualElement>(k_UxmlIdentifierBaseSnapshot);
            m_CompareSnapshotContainer = view.Q<VisualElement>(k_UxmlIdentifierCompareSnapshot);
        }

        void SetupView()
        {
            m_SnapshotDataService.LoadedSnapshotsChanged += RefreshView;
            m_SnapshotDataService.CompareModeChanged += RefreshView;

            m_Ribbon.Clicked += RibbonButtonStateChanged;
            m_Ribbon.HelpClicked += () => Application.OpenURL(DocumentationUrls.OpenSnapshotsPane);
        }

        void RefreshView()
        {
            // Reset state
            m_BaseSnapshotController = null;
            m_BaseSnapshotContainer.Clear();
            m_CompareSnapshotController = null;
            m_CompareSnapshotContainer.Clear();
            UIElementsHelper.SetVisibility(m_CompareSnapshotContainer, false);

            // Refresh base snapshot
            SnapshotFileModel baseModel = null;
            if (m_SnapshotDataService.Base != null)
            {
                var modelBuilder = new SnapshotFileModelBuilder(m_SnapshotDataService.Base.FullPath);
                baseModel = modelBuilder.Build();
            }
            m_BaseSnapshotController = new LoadedSnapshotsItemViewController(baseModel, m_SnapshotDataService, m_ScreenshotsManager);
            m_BaseSnapshotContainer.Add(m_BaseSnapshotController.View);

            // Refresh compare snapshot if in compare mode
            if (m_SnapshotDataService.CompareMode)
            {
                SnapshotFileModel compareModel = null;
                if (m_SnapshotDataService.Compared != null)
                {
                    var comparedModelBuilder = new SnapshotFileModelBuilder(m_SnapshotDataService.Compared.FullPath);
                    compareModel = comparedModelBuilder.Build();
                }
                m_CompareSnapshotController = new LoadedSnapshotsItemViewController(compareModel, m_SnapshotDataService, m_ScreenshotsManager);
                m_CompareSnapshotContainer.Add(m_CompareSnapshotController.View);

                UIElementsHelper.SetVisibility(m_CompareSnapshotContainer, true);
            }
        }

        void RibbonButtonStateChanged(int i)
        {
            if (i == 0)
                m_SnapshotDataService.CompareMode = false;
            else
                m_SnapshotDataService.CompareMode = true;
        }
    }
}

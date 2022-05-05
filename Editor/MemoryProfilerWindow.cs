using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System;
using System.Runtime.CompilerServices;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEditorInternal;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using Unity.MemoryProfiler.Editor.UI;
using Unity.MemoryProfiler.Editor.UIContentData;

[assembly: InternalsVisibleTo("Unity.MemoryProfiler.Editor.Tests")]
namespace Unity.MemoryProfiler.Editor
{
    internal interface IUIStateHolder
    {
        event Action<UIState> UIStateChanged;
        UI.UIState UIState { get; }
        void Repaint();
        EditorWindow Window { get; }
    }

    internal class MemoryProfilerWindow : EditorWindow, IUIStateHolder
    {
        [NonSerialized]
        bool m_PrevApplicationFocusState;

        [NonSerialized]
        bool m_WindowInitialized = false;

        SnapshotsWindow m_SnapshotWindow = new SnapshotsWindow();
        DetailViewController m_DetailsViewController;

        public event Action<UIState> UIStateChanged = delegate { };

        public UI.UIState UIState { get; private set; }

        public EditorWindow Window => this;

        // Eventually the window will instantiate a single root view controller, once we have whole window migrated to view controller architecture. For now we manage the AnalysisViewController from the window.
        const string k_UxmlIdentifier_AnalysisViewContainer = "memory-profiler-window__analysis-view-container";
        VisualElement m_AnalysisViewContainer;
        ViewController m_AnalysisViewController;

        [MenuItem("Window/Analysis/Memory Profiler", false, 4)]
        static void ShowWindow()
        {
            var window = GetWindow<MemoryProfilerWindow>();
            window.Show();
        }

        void OnEnable()
        {
            var icon = Icons.MemoryProfilerWindowTabIcon;
            titleContent = new GUIContent("Memory Profiler", icon);

            minSize = new Vector2(500, 500);
        }

        void Init()
        {
            m_WindowInitialized = true;
            Styles.Initialize();

            UIState?.Dispose();
            UIStateChanged = null;
            UIState = new UI.UIState();

            var root = this.rootVisualElement;
            VisualTreeAsset reworkedWindowTree;
            reworkedWindowTree = AssetDatabase.LoadAssetAtPath(ResourcePaths.WindowUxmlPathStyled, typeof(VisualTreeAsset)) as VisualTreeAsset;
            reworkedWindowTree.Clone(root);

            // Retrieve the analysis view container element.
            m_AnalysisViewContainer = root.Q<VisualElement>(k_UxmlIdentifier_AnalysisViewContainer);
            UIState.ModeChanged += OnSnapshotsModeChanged;
            m_SnapshotWindow.SwappedSnapshots += OnSnapshotsSwapped;

            // Detail sidebar creation
            m_DetailsViewController = new DetailViewController(root.Q<VisualElement>("details-panel"), this)
            {
                UIState = UIState
            };
            var view = m_DetailsViewController.View; // Fake loading controller as this controller uses parent view sub-item for now

            MemoryProfilerAnalytics.EnableAnalytics(this);
            m_PrevApplicationFocusState = InternalEditorUtility.isApplicationActive;
            EditorApplication.update += PollForApplicationFocus;
            EditorSceneManager.activeSceneChangedInEditMode += OnSceneChanged;

            m_SnapshotWindow.InitializeSnapshotsWindow(this, root, root.Q<VisualElement>("snapshot-window"), null);
            RebuildAnalysisView();

            root.Q<ToolbarButton>("toolbar__help-button").clickable.clicked += () => Application.OpenURL(DocumentationUrls.LatestPackageVersionUrl);
            var menuButton = root.Q<ToolbarButton>("toolbar__menu-button");
            menuButton.clickable.clicked += () => OpenFurtherOptions(menuButton.GetRect());

            UIStateChanged += m_SnapshotWindow.RegisterUIState;
            UIStateChanged(UIState);

            EditorGUICompatibilityHelper.hyperLinkClicked -= EditorGUI_HyperLinkClicked;
            EditorGUICompatibilityHelper.hyperLinkClicked += EditorGUI_HyperLinkClicked;
        }

        static void EditorGUI_HyperLinkClicked(MemoryProfilerHyperLinkClickedEventArgs args)
        {
            if (args.window is MemoryProfilerWindow)
            {
                int inspectorId;
                int treeViewId;
                if (ManagedObjectInspectorItem.TryParseHyperlink(args.hyperLinkData, out inspectorId, out treeViewId))
                {
                    var memoryProfiler = args.window as MemoryProfilerWindow;
                    memoryProfiler.m_DetailsViewController.ManagedInspectorLinkWasClicked(inspectorId, treeViewId);
                }
            }
        }

        void OnGUI()
        {
            if (m_WindowInitialized)
                return;

            Init();
        }

        void OnSceneChanged(Scene sceneA, Scene sceneB)
        {
            m_SnapshotWindow.RefreshScreenshots();
        }

        void PollForApplicationFocus()
        {
            if (m_PrevApplicationFocusState != InternalEditorUtility.isApplicationActive)
            {
                m_SnapshotWindow.RefreshCollection();
                m_PrevApplicationFocusState = InternalEditorUtility.isApplicationActive;
            }
        }

        void OnDisable()
        {
            m_WindowInitialized = false;
            Styles.Cleanup();
            ProgressBarDisplay.ClearBar();
            UIStateChanged = delegate { };

            m_AnalysisViewController?.Dispose();
            m_SnapshotWindow.SwappedSnapshots -= OnSnapshotsSwapped;
            if (UIState != null)
                UIState.ModeChanged -= OnSnapshotsModeChanged;

            m_SnapshotWindow.OnDisable();
            m_DetailsViewController?.Dispose();

            if (UIState != null)
            {
                UIState.Dispose();
                UIState = null;
            }
            EditorApplication.update -= PollForApplicationFocus;
            EditorSceneManager.activeSceneChangedInEditMode -= OnSceneChanged;

            EditorGUICompatibilityHelper.hyperLinkClicked -= EditorGUI_HyperLinkClicked;

            MemoryProfilerAnalytics.DisableAnalytics();
        }

        void OpenFurtherOptions(Rect furtherOptionsRect)
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(TextContent.OpenSettingsOption, false, () => SettingsService.OpenUserPreferences(MemoryProfilerSettingsEditor.SettingsPath));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(TextContent.TruncateTypeName), MemoryProfilerSettings.MemorySnapshotTruncateTypes, MemoryProfilerSettings.ToggleTruncateTypes);
            menu.DropDown(furtherOptionsRect);
        }

        void RebuildAnalysisView()
        {
            // Dispose the existing analysis view controller.
            m_AnalysisViewController?.Dispose();
            m_AnalysisViewController = null;

            // Try ending any interaction events
            // Stop Collecting interaction events for the old page and send them off
            MemoryProfilerAnalytics.EndEventWithMetadata<MemoryProfilerAnalytics.InteractionsInPage>();

            MemoryProfilerAnalytics.StartEvent<MemoryProfilerAnalytics.OpenedPageEvent>();
            // Build analysis tab bar model from loaded snapshots.
            var viewName = "Summary(Default)";
            bool updateAnalytics = true;
            if (UIState.CurrentViewMode != UIState.ViewMode.ShowDiff)
            {
                // Instantiate the analysis-tab-bar model using the selected snapshot.
                var snapshot = UIState.snapshotMode?.snapshot;
                if (snapshot != null)
                    m_AnalysisViewController = new AnalysisTabBarController(snapshot, null);
            }
            else
            {
                viewName = "Summary Comparison(Default)";
                // Instantiate the analysis-tab-bar model using both selected snapshots.
                var snapshotA = (UIState.FirstMode as UIState.SnapshotMode)?.snapshot;
                var snapshotB = (UIState.SecondMode as UIState.SnapshotMode)?.snapshot;
                m_AnalysisViewController = new AnalysisTabBarController(snapshotA, snapshotB);
            }

            // Instantiate default controller when no snapshot is loaded.
            if (m_AnalysisViewController == null)
            {
                updateAnalytics = false;
                var noDataViewController = new NoDataViewController();
                noDataViewController.TakeSnapshotSelected += OnTakeSnapshotSelected;
                m_AnalysisViewController = noDataViewController;
            }

            m_AnalysisViewContainer.Add(m_AnalysisViewController.View);

            if (updateAnalytics)
            {
                MemoryProfilerAnalytics.EndEvent(new MemoryProfilerAnalytics.OpenedPageEvent() { viewName = viewName });
                // Stop Collecting interaction events for the old page and send them off
                MemoryProfilerAnalytics.EndEventWithMetadata<MemoryProfilerAnalytics.InteractionsInPage>();
                // Start collecting interaction events for this page
                MemoryProfilerAnalytics.StartEventWithMetaData<MemoryProfilerAnalytics.InteractionsInPage>(new MemoryProfilerAnalytics.InteractionsInPage() { viewName = viewName });
            }
        }

        void OnSnapshotsModeChanged(UIState.BaseMode arg1, UIState.ViewMode arg2)
        {
            RebuildAnalysisView();
        }

        void OnSnapshotsSwapped()
        {
            RebuildAnalysisView();
        }

        void OnTakeSnapshotSelected()
        {
            m_SnapshotWindow.TakeSnapshot();
        }
    }
}

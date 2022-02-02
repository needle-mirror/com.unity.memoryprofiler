using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System;
using System.Collections;
using System.Runtime.CompilerServices;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEditorInternal;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
#if UNITY_2020_1_OR_NEWER
#else
using ConnectionUtility = UnityEditor.Experimental.Networking.PlayerConnection.EditorGUIUtility;
using ConnectionGUI = UnityEditor.Experimental.Networking.PlayerConnection.EditorGUI;
using UnityEngine.Experimental.Networking.PlayerConnection;
#endif

using Unity.EditorCoroutines.Editor;
using Unity.MemoryProfiler.Editor.UI;
using Unity.MemoryProfiler.Editor.UI.PathsToRoot;
using Unity.MemoryProfiler.Editor.UI.Treemap;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor.IMGUI.Controls;

[assembly: InternalsVisibleTo("Unity.MemoryProfiler.Editor.Tests")]
namespace Unity.MemoryProfiler.Editor
{
    internal interface IUIStateHolder
    {
        event Action<UIState> UIStateChanged;
        UI.UIState UIState { get;  }
        void Repaint();
        EditorWindow Window { get; }
    }

    internal class MemoryProfilerWindow : EditorWindow, IUIStateHolder
    {
        static Dictionary<BuildTarget, string> s_PlatformIconClasses = new Dictionary<BuildTarget, string>();

        [NonSerialized]
        bool m_PrevApplicationFocusState;

        [NonSerialized]
        bool m_WindowInitialized = false;

        [MenuItem("Window/Analysis/Memory Profiler", false, 4)]
        public static void ShowWindow()
        {
            GetWindow<MemoryProfilerWindow>(TextContent.Title.text);
        }

        AnalysisWindow m_MainViewPanel;

        SnapshotsWindow m_SnapshotWindow = new SnapshotsWindow();
        PathsToRootDetailView m_PathsToRootDetailView;
        SelectedItemDetailsPanel m_SelectedObjectDetailsPanel;

        public event Action<UIState> UIStateChanged = delegate {};

        public UI.UIState UIState { get; private set; }

        public EditorWindow Window => this;

        void Init()
        {
            m_WindowInitialized = true;
            m_PathsToRootDetailView = new PathsToRootDetailView(this, new TreeViewState(),
                new MultiColumnHeaderWithTruncateTypeName(PathsToRootDetailView.CreateDefaultMultiColumnHeaderState())
                {
                    canSort = false
                });

            minSize = new Vector2(500, 500);

            var root = this.rootVisualElement;
            VisualTreeAsset reworkedWindowTree;
            reworkedWindowTree = AssetDatabase.LoadAssetAtPath(ResourcePaths.WindowUxmlPathStyled, typeof(VisualTreeAsset)) as VisualTreeAsset;

            reworkedWindowTree.Clone(root);

            var detailsSplitter = root.Q("details-panel").Q<TwoPaneSplitView>("details-panel__splitter");
            var referencesFoldout = root.Q("details-panel").Q<Foldout>("details-panel__section-header__references");
            var selectionDetailsFolout = root.Q("details-panel").Q<Foldout>("details-panel__section-header__selection-details");
            referencesFoldout.RegisterValueChangedCallback((evt) =>
            {
                if (evt.target != referencesFoldout || evt.newValue == evt.previousValue)
                    return;
                if (evt.newValue)
                {
                    detailsSplitter.UnCollapse(0);
                }
                else
                {
                    if (detailsSplitter.hasCollapsedPanes)
                    {
                        // only one section can be collapsed at a time, or the UI goes funky because it doesn't know how to handle the situation.
                        // Also the UX behavior for that case is somewhat undefined
                        // Unfold the References section before collapsing the Selection Details Section,
                        // skipping a frame in-between so that the style changes can get applied and the UI gets re-layouted properly
                        EditorCoroutineUtility.StartCoroutine(FlipDetailsFoldoutsDelayed(detailsSplitter, 0, selectionDetailsFolout), this);
                    }
                    else
                        detailsSplitter.CollapseChild(0, true);
                }
            });
            selectionDetailsFolout.RegisterValueChangedCallback((evt) =>
            {
                if (evt.target != selectionDetailsFolout || evt.newValue == evt.previousValue)
                    return;
                if (evt.newValue)
                {
                    detailsSplitter.UnCollapse(1);
                }
                else
                {
                    if (detailsSplitter.hasCollapsedPanes)
                    {
                        // only one section can be collapsed at a time, or the UI goes funky because it doesn't know how to handle the situation.
                        // Also the UX behavior for that case is somewhat undefined
                        // Unfold the Selection Details section before collapsing the References section,
                        // skipping a frame in-between so that the style changes can get applied and the UI gets re-layouted properly
                        EditorCoroutineUtility.StartCoroutine(FlipDetailsFoldoutsDelayed(detailsSplitter, 1, referencesFoldout), this);
                    }
                    else
                        detailsSplitter.CollapseChild(1, true);
                }
            });

            m_SelectedObjectDetailsPanel = new SelectedItemDetailsPanel(this, root.Q("details-panel").Q("selected-item-details"));


            UIState = new UI.UIState();
            new SelectedItemDetailsForTypesAndObjects(this);
            UIState.SelectionChanged += m_SelectedObjectDetailsPanel.NewDetailItem;
            Styles.Initialize();
            EditorCoroutineUtility.StartCoroutine(UpdateTitle(), this);
            MemoryProfilerAnalytics.EnableAnalytics();
            m_PrevApplicationFocusState = InternalEditorUtility.isApplicationActive;
            EditorApplication.update += PollForApplicationFocus;
            EditorSceneManager.activeSceneChangedInEditMode += OnSceneChanged;

            m_SnapshotWindow.InitializeSnapshotsWindow(this, root, root.Q<VisualElement>("snapshot-window"), null);

            m_MainViewPanel = new AnalysisWindow(this, root, m_SnapshotWindow, m_PathsToRootDetailView);

            m_SnapshotWindow.RegisterAdditionalCaptureButton(m_MainViewPanel.NoSnapshotOpenedCaptureButton);

            root.Q<ToolbarButton>("toolbar__help-button").clickable.clicked += () => Application.OpenURL(DocumentationUrls.LatestPackageVersionUrl);
            var menuButton = root.Q<ToolbarButton>("toolbar__menu-button");
            menuButton.clickable.clicked += () => OpenFurtherOptions(menuButton.GetRect());

            UIStateChanged -= m_SnapshotWindow.RegisterUIState;
            UIStateChanged += m_SnapshotWindow.RegisterUIState;
            UIStateChanged(UIState);

            var referencesContainer = root.Q("details-panel").Q<IMGUIContainer>("references-imguicontainer");
            referencesContainer.onGUIHandler += () =>
            {
                m_PathsToRootDetailView.DoGUI(referencesContainer.contentRect);
            };
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
                    memoryProfiler.m_SelectedObjectDetailsPanel?.ManagedInspectorLinkWasClicked(inspectorId, treeViewId);
                }
            }
        }

        void OnGUI()
        {
            if (m_WindowInitialized)
                return;

            Init();
        }

        IEnumerator UpdateTitle()
        {
            yield return null;
            titleContent = TextContent.Title;
        }

        IEnumerator FlipDetailsFoldoutsDelayed(TwoPaneSplitView detailsSplitter, int index, Foldout foldout)
        {
            foldout.value = true;
            yield return null;
            detailsSplitter.CollapseChild(index, true);
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
            UIStateChanged = delegate {};
            if (UIState != null)
                UIState.Clear();
            EditorApplication.update -= PollForApplicationFocus;
            EditorSceneManager.activeSceneChangedInEditMode -= OnSceneChanged;

            EditorGUICompatibilityHelper.hyperLinkClicked -= EditorGUI_HyperLinkClicked;
            m_SnapshotWindow.OnDisable();
            m_PathsToRootDetailView.OnDisable();
            m_SelectedObjectDetailsPanel.OnDisable();
        }

        void OpenFurtherOptions(Rect furtherOptionsRect)
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(TextContent.OpenSettingsOption, false, () => PreferencesCompatibilityHelper.OpenProfilerPreferences());
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(TextContent.TruncateTypeName), MemoryProfilerSettings.MemorySnapshotTruncateTypes, MemoryProfilerSettings.ToggleTruncateTypes);
            menu.DropDown(furtherOptionsRect);
        }
    }
}

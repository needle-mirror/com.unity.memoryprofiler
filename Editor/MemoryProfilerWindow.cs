using UnityEngine;
using UnityEditor;

using System.Collections.Generic;
using System;
using System.Text;
using System.Collections;
using System.Runtime.CompilerServices;
using System.IO;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.Profiling.Memory.Experimental;
using UnityEditorInternal;
#if UNITY_2019_1_OR_NEWER
using UnityEngine.UIElements;
using UnityEditor.UIElements;
#else
using UnityEngine.Experimental.UIElements;
using UnityEditor.Experimental.UIElements;
#endif
#if UNITY_2019_3_OR_NEWER
using UnityEngine.Profiling.Experimental;
#endif

#if UNITY_2020_1_OR_NEWER
using UnityEditor.Networking.PlayerConnection;
using UnityEngine.Networking.PlayerConnection;
#else
using ConnectionUtility = UnityEditor.Experimental.Networking.PlayerConnection.EditorGUIUtility;
using ConnectionGUI = UnityEditor.Experimental.Networking.PlayerConnection.EditorGUI;
using UnityEngine.Experimental.Networking.PlayerConnection;
#endif


using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.EditorCoroutines.Editor;
using Unity.MemoryProfiler.Editor.UI;
using Unity.MemoryProfiler.Editor.Legacy;
using Unity.MemoryProfiler.Editor.Legacy.LegacyFormats;
using Unity.MemoryProfiler.Editor.EnumerationUtilities;
using Unity.MemoryProfiler.Editor.UIContentData;

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

        public event Action<UIState> UIStateChanged = delegate {};

        public UI.UIState UIState { get; private set; }

        public EditorWindow Window => this;

        void Init()
        {
            m_WindowInitialized = true;

            minSize = new Vector2(500, 500);

            UIState = new UI.UIState();
            Styles.Initialize();
            EditorCoroutineUtility.StartCoroutine(UpdateTitle(), this);
            MemoryProfilerAnalytics.EnableAnalytics();
            m_PrevApplicationFocusState = InternalEditorUtility.isApplicationActive;
            EditorApplication.update += PollForApplicationFocus;
            EditorSceneManager.activeSceneChangedInEditMode += OnSceneChanged;

            var root = this.rootVisualElement;
            VisualTreeAsset reworkedWindowTree;
            reworkedWindowTree = AssetDatabase.LoadAssetAtPath(ResourcePaths.WindowUxmlPathStyled, typeof(VisualTreeAsset)) as VisualTreeAsset;

            reworkedWindowTree.Clone(root);

            m_SnapshotWindow.InitializeSnapshotsWindow(this, root, root.Q<VisualElement>("snapshot-window"), null);

            m_MainViewPanel = new AnalysisWindow(this, root, m_SnapshotWindow);

            m_SnapshotWindow.RegisterAdditionalCaptureButton(m_MainViewPanel.NoSnapshotOpenedCaptureButton);

            root.Q<ToolbarButton>("toolbar__help-button").clickable.clicked += () => Application.OpenURL(DocumentationUrls.LatestPackageVersionUrl);
            var menuButton = root.Q<ToolbarButton>("toolbar__menu-button");
            menuButton.clickable.clicked += () => OpenFurtherOptions(menuButton.GetRect());

            UIStateChanged += m_SnapshotWindow.RegisterUIState;
            UIStateChanged(UIState);
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
                UIState.ClearAllOpenModes();
            EditorApplication.update -= PollForApplicationFocus;
            EditorSceneManager.activeSceneChangedInEditMode -= OnSceneChanged;

            m_SnapshotWindow.OnDisable();
        }

        void OpenFurtherOptions(Rect furtherOptionsRect)
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(TextContent.OpenSettingsOption, false, () => PreferencesCompatibilityHelper.OpenProfilerPreferences());

            menu.DropDown(furtherOptionsRect);
        }
    }
}

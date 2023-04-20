using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using Unity.MemoryProfiler.Editor.UI;

[assembly: InternalsVisibleTo("Unity.MemoryProfiler.Editor.Tests")]
namespace Unity.MemoryProfiler.Editor
{
    internal class MemoryProfilerWindow : EditorWindow
    {
        bool m_PrevApplicationFocusState;
        bool m_WindowInitialized = false;

        SnapshotDataService m_SnapshotDataService;
        PlayerConnectionService m_PlayerConnectionService;

        MemoryProfilerViewController m_ProfilerViewController;

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

            m_SnapshotDataService = new SnapshotDataService();
            m_PlayerConnectionService = new PlayerConnectionService(this, m_SnapshotDataService);

            // Analytics
            MemoryProfilerAnalytics.EnableAnalytics(this);
            m_PrevApplicationFocusState = InternalEditorUtility.isApplicationActive;
            EditorApplication.update += PollForApplicationFocus;
            EditorSceneManager.activeSceneChangedInEditMode += RefreshScreenshotsOnSceneChange;

            m_ProfilerViewController = new MemoryProfilerViewController(m_PlayerConnectionService, m_SnapshotDataService);
            this.rootVisualElement.Add(m_ProfilerViewController.View);
        }

        void OnGUI()
        {
            if (m_WindowInitialized)
                return;

            Init();
        }

        void PollForApplicationFocus()
        {
            if (m_PrevApplicationFocusState != InternalEditorUtility.isApplicationActive)
            {
                m_SnapshotDataService.SyncSnapshotsFolder();
                m_PrevApplicationFocusState = InternalEditorUtility.isApplicationActive;
            }
        }

        void RefreshScreenshotsOnSceneChange(Scene sceneA, Scene sceneB)
        {
            // We need to refresh screenshot textures as they get collected on scene change
            m_ProfilerViewController.RefreshScreenshotsOnSceneChange();
        }

        void OnDisable()
        {
            m_WindowInitialized = false;

            m_ProfilerViewController?.Dispose();
            m_ProfilerViewController = null;

            EditorApplication.update -= PollForApplicationFocus;
            EditorSceneManager.activeSceneChangedInEditMode -= RefreshScreenshotsOnSceneChange;

            m_PlayerConnectionService?.Dispose();
            m_PlayerConnectionService = null;

            m_SnapshotDataService?.Dispose();
            m_SnapshotDataService = null;

            MemoryProfilerAnalytics.DisableAnalytics();
        }
    }
}

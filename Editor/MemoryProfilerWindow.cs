using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEditor;
using Unity.MemoryProfiler.Editor.UI;

[assembly: InternalsVisibleTo("Unity.MemoryProfiler.Editor.Tests")]
namespace Unity.MemoryProfiler.Editor
{
    internal class MemoryProfilerWindow : EditorWindow
    {
        bool m_WindowInitialized = false;

        SnapshotDataService m_SnapshotDataService;
        PlayerConnectionService m_PlayerConnectionService;

        MemoryProfilerViewController m_ProfilerViewController;

        // Api exposed for testing purposes
        internal PlayerConnectionService PlayerConnectionService => m_PlayerConnectionService;
        internal SnapshotDataService SnapshotDataService => m_SnapshotDataService;
        internal MemoryProfilerViewController ProfilerViewController => m_ProfilerViewController;

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

            // initialize quick search in the background so that it is ready for finding assets once a snapshot is openes
            QuickSearchUtility.InitializeQuickSearch(async: true);
        }

        void Init()
        {
            m_WindowInitialized = true;

            m_SnapshotDataService = new SnapshotDataService();
            m_PlayerConnectionService = new PlayerConnectionService(this, m_SnapshotDataService);

            // Analytics
            MemoryProfilerAnalytics.EnableAnalytics();

            m_ProfilerViewController = new MemoryProfilerViewController(m_PlayerConnectionService, m_SnapshotDataService);
            this.rootVisualElement.Add(m_ProfilerViewController.View);
        }

        void OnGUI()
        {
            if (m_WindowInitialized)
                return;

            Init();
        }

        void OnDisable()
        {
            m_WindowInitialized = false;

            m_ProfilerViewController?.Dispose();
            m_ProfilerViewController = null;

            m_PlayerConnectionService?.Dispose();
            m_PlayerConnectionService = null;

            m_SnapshotDataService?.Dispose();
            m_SnapshotDataService = null;

            MemoryProfilerAnalytics.DisableAnalytics();
        }
    }
}

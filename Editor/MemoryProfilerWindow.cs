using System.Runtime.CompilerServices;
using Unity.MemoryProfiler.Editor.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

[assembly: InternalsVisibleTo("Unity.MemoryProfiler.Editor.Tests")]
namespace Unity.MemoryProfiler.Editor
{
    internal class MemoryProfilerWindow : EditorWindow
    {
        bool m_WindowInitialized = false;

        SnapshotDataService m_SnapshotDataService;
        PlayerConnectionService m_PlayerConnectionService;

        MemoryProfilerViewController m_ProfilerViewController;

        IMGUIContainer m_InitContainer;
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

            m_InitContainer = new IMGUIContainer(OnInitContainerGUI)
            {
                style =
                {
                    flexGrow = 0,
                    flexShrink = 1,
                    position = Position.Absolute,
                }
            };
            rootVisualElement.Add(m_InitContainer);
        }

        void OnInitContainerGUI()
        {
            if (m_InitContainer == null)
                return;
            if (Event.current.type == EventType.Repaint)
            {
                // Initialize the PlayerConnectionCompatibilityHelper parts that need to happen during OnGUI
                PlayerConnectionCompatibilityHelper.InitGUI();
                // Then remove the container again, its job is done
                rootVisualElement.Remove(m_InitContainer);
                m_InitContainer = null;
            }
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

        void CreateGUI()
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

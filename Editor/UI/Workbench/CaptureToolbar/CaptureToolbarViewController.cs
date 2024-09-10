using System;
using UnityEngine;
using UnityEditor;
using Unity.MemoryProfiler.Editor.UI;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using Unity.MemoryProfiler.Editor.UIContentData;
using Unity.Profiling.Memory;

namespace Unity.MemoryProfiler.Editor
{
    internal class CaptureToolbarViewController : ViewController
    {
        const string k_UxmlAssetGuid = "5d1afd1dbd49ed94a9b738d9b174701f";
        const string k_UssClass_Dark = "capture-toolbar-view__dark";
        const string k_UssClass_Light = "capture-toolbar-view__light";
        const string k_UxmlIdentifierSnapshotsToggle = "memory-profiler-view__toolbar__snaphsot-window-toggle";
        const string k_UxmlIdentifierDetailsToggle = "memory-profiler-view__toolbar__details-toggle";
        const string k_UxmlIdentifierCaptureButton = "memory-profiler-view__toolbar__capture-button";
        const string k_UxmlIdentifierCaptureDropdownButton = "memory-profiler-view__toolbar__capture-button__dropdown";
        const string k_UxmlIdentifierImportButton = "memory-profiler-view__toolbar__import-button";
        const string k_UxmlIdentifierSettingsButton = "memory-profiler-view__toolbar__settings-button";
        const string k_UxmlIdentifierHelpButton = "memory-profiler-view__toolbar__help-button";
        const string k_UxmlIdentifierTargetSelection = "memory-profiler-view__toolbar__snaphsot-window-toggle__target-selection";

        // State
        PlayerConnectionService m_PlayerConnectionService;
        SnapshotDataService m_SnapshotDataService;

        // View
        ToolbarToggle m_SnapshotsToggle;
        ToolbarToggle m_DetailsToggle;
        ToolbarButton m_ImportButton;
        ToolbarButton m_SettingsButton;
        ToolbarButton m_HelpButton;
        Button m_CaptureButton;
        Button m_CaptureDropdownButton;
        Button m_TargetSelectionDropdown;
        Label m_TargetSelectionDropdownLabel;

        public CaptureToolbarViewController(PlayerConnectionService playerConnectionService, SnapshotDataService snapshotDataService)
        {
            m_PlayerConnectionService = playerConnectionService;
            m_SnapshotDataService = snapshotDataService;
        }

        public event EventCallback<ChangeEvent<bool>> SnapshotsPanelToggle;
        public event EventCallback<ChangeEvent<bool>> DetailsPanelToggle;

        protected override VisualElement LoadView()
        {
            var view = ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlAssetGuid);
            if (view == null)
                throw new InvalidOperationException("Unable to create view from Uxml. Uxml must contain at least one child element.");

            var themeUssClass = (EditorGUIUtility.isProSkin) ? k_UssClass_Dark : k_UssClass_Light;
            view.AddToClassList(themeUssClass);

            GatherReferencesInView(view);

            return view;
        }

        protected override void ViewLoaded()
        {
            base.ViewLoaded();
            RefreshView();
        }

        void GatherReferencesInView(VisualElement view)
        {
            m_SnapshotsToggle = view.Q<ToolbarToggle>(k_UxmlIdentifierSnapshotsToggle);
            m_DetailsToggle = view.Q<ToolbarToggle>(k_UxmlIdentifierDetailsToggle);

            m_CaptureButton = view.Q<Button>(k_UxmlIdentifierCaptureButton);
            m_CaptureDropdownButton = m_CaptureButton.Q<Button>(k_UxmlIdentifierCaptureDropdownButton);
            m_ImportButton = view.Q<ToolbarButton>(k_UxmlIdentifierImportButton);
            m_SettingsButton = view.Q<ToolbarButton>(k_UxmlIdentifierSettingsButton);
            m_HelpButton = view.Q<ToolbarButton>(k_UxmlIdentifierHelpButton);

            m_TargetSelectionDropdown = view.Q<Button>(k_UxmlIdentifierTargetSelection);
            m_TargetSelectionDropdownLabel = m_TargetSelectionDropdown.Q<Label>();
        }

        void RefreshView()
        {
            // Snapshots list and detail view panels toggle buttons
            // We have to do this to get the image to ToolbarToggle
            m_SnapshotsToggle.Q(null, ToolbarToggle.inputUssClassName).Add(UIElementsHelper.GetImageWithClasses(new[] { "icon_button", "square-button-icon", "icon-button__snapshot-icon" }));
            m_DetailsToggle.Q(null, ToolbarToggle.inputUssClassName).Add(UIElementsHelper.GetImageWithClasses(new[] { "icon_button", "square-button-icon", "icon-button__details-icon" }));
            m_SnapshotsToggle.RegisterValueChangedCallback((x) => SnapshotsPanelToggle?.Invoke(x));
            m_DetailsToggle.RegisterValueChangedCallback((x) => DetailsPanelToggle?.Invoke(x));

            // Targe selection
            m_TargetSelectionDropdown.clicked += ShowTargetSelectionMenu;
            m_PlayerConnectionService.PlayerConnectionChanged += UpdateTargetSelection;
            UpdateTargetSelection();

            // Snapshot capture button
            m_CaptureButton.clicked += () => m_PlayerConnectionService.TakeCapture();
            m_CaptureDropdownButton.clicked += () => OpenCaptureFlagsMenu(m_CaptureButton.GetRect());

            // Import snapshots
            m_ImportButton.clicked += ImportCapture;

            // Right side help & settings
            m_SettingsButton.clickable.clicked += () => OpenFurtherOptions(m_SettingsButton.GetRect());
            m_HelpButton.clickable.clicked += () => Application.OpenURL(DocumentationUrls.LatestPackageVersionUrl);
        }

        void ImportCapture()
        {
            string path = EditorUtility.OpenFilePanelWithFilters(TextContent.ImportSnapshotWindowTitle, MemoryProfilerSettings.LastImportPath, TextContent.MemorySnapshotImportWindowFileExtensions);
            if (path.Length == 0)
                return;

            MemoryProfilerSettings.LastImportPath = path;

            if (!m_SnapshotDataService.Import(path))
                Debug.LogFormat($"{path} has already been imported or is locked.");
        }

        void UpdateTargetSelection()
        {
            m_TargetSelectionDropdownLabel.text = m_PlayerConnectionService.PlayerConnectionName;
        }

        void ShowTargetSelectionMenu()
        {
            m_PlayerConnectionService.ShowPlayerConnectionSelection(m_TargetSelectionDropdown.GetRect());
        }

        void OpenCaptureFlagsMenu(Rect position)
        {
            GenerateCaptureFlagsMenu().DropDown(position);
        }

        GenericMenu GenerateCaptureFlagsMenu()
        {
            var menu = new GenericMenu();
            AddCaptureFlagMenuItem(menu, TextContent.CaptureManagedObjectsItem, CaptureFlags.ManagedObjects);
            AddCaptureFlagMenuItem(menu, TextContent.CaptureNativeObjectsItem, CaptureFlags.NativeObjects);
            //// For now disable all the native allocation flags in one go, the call-stack flags will have an effect only when the player has call-stacks support
            AddCaptureFlagMenuItem(menu, TextContent.CaptureNativeAllocationsItem, CaptureFlags.NativeAllocations | CaptureFlags.NativeAllocationSites | CaptureFlags.NativeStackTraces);
            menu.AddSeparator("");
            menu.AddItem(TextContent.CaptureScreenshotItem, MemoryProfilerSettings.CaptureWithScreenshot, () => { MemoryProfilerSettings.CaptureWithScreenshot = !MemoryProfilerSettings.CaptureWithScreenshot; });
            if (m_PlayerConnectionService.IsConnectedToEditor)
            {
                menu.AddItem(TextContent.CloseSnapshotsItem, MemoryProfilerSettings.CloseSnapshotsWhenCapturingEditor, () => { MemoryProfilerSettings.CloseSnapshotsWhenCapturingEditor = !MemoryProfilerSettings.CloseSnapshotsWhenCapturingEditor; });
                menu.AddItem(TextContent.GCCollectItem, MemoryProfilerSettings.GCCollectWhenCapturingEditor, () => { MemoryProfilerSettings.GCCollectWhenCapturingEditor = !MemoryProfilerSettings.GCCollectWhenCapturingEditor; });
            }

            return menu;
        }

        void FlipCaptureFlag(CaptureFlags flag)
        {
            if (MemoryProfilerSettings.MemoryProfilerCaptureFlags.HasFlag(flag))
                MemoryProfilerSettings.MemoryProfilerCaptureFlags &= ~flag;
            else
                MemoryProfilerSettings.MemoryProfilerCaptureFlags |= flag;
        }

        void AddCaptureFlagMenuItem(GenericMenu menu, GUIContent name, CaptureFlags flag)
        {
            menu.AddItem(name, MemoryProfilerSettings.MemoryProfilerCaptureFlags.HasFlag(flag), () => { FlipCaptureFlag(flag); });
        }

        void OpenFurtherOptions(Rect furtherOptionsRect)
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(TextContent.OpenSettingsOption, false, () => SettingsService.OpenUserPreferences(MemoryProfilerSettingsEditor.SettingsPath));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(TextContent.TruncateTypeName), MemoryProfilerSettings.MemorySnapshotTruncateTypes, MemoryProfilerSettings.ToggleTruncateTypes);
            menu.DropDown(furtherOptionsRect);
        }
    }
}

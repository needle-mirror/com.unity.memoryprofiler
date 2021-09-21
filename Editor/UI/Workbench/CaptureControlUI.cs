using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
#if UNITY_2020_1_OR_NEWER
using UnityEditor.Networking.PlayerConnection;
using UnityEngine.Networking.PlayerConnection;
#else
using ConnectionUtility = UnityEditor.Experimental.Networking.PlayerConnection.EditorGUIUtility;
using ConnectionGUI = UnityEditor.Experimental.Networking.PlayerConnection.EditorGUI;
using UnityEngine.Experimental.Networking.PlayerConnection;
#endif
using Unity.MemoryProfiler.Editor.UIContentData;
using Unity.MemoryProfiler.Editor.UI;
using UnityEditor.Compilation;
using Unity.MemoryProfiler.Editor.EnumerationUtilities;
using Unity.MemoryProfiler.Editor.Legacy.LegacyFormats;
using System.IO;
using Unity.MemoryProfiler.Editor.Legacy;
using UnityEngine.Profiling.Experimental;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.EditorCoroutines.Editor;
using UnityEngine.Profiling.Memory.Experimental;
#if UNITY_2019_1_OR_NEWER
using UnityEngine.UIElements;
using UnityEditor.UIElements;
#else
using UnityEngine.Experimental.UIElements;
using UnityEditor.Experimental.UIElements;
#endif

namespace Unity.MemoryProfiler.Editor
{
    using QueryMemoryProfiler = UnityEngine.Profiling.Memory.Experimental.MemoryProfiler;
    [Serializable]
    internal class CaptureControlUI
    {
        VisualElement m_CaptureButtonWithDropdown;
        ToolbarButton m_ImportButton;
        [NonSerialized]
        SnapshotCollection m_SnapshotsCollection;
        [NonSerialized]
        OpenSnapshotsWindow m_OpenSnapshotsWindow;
        [NonSerialized]
        OpenSnapshotsManager m_OpenSnapshotsManager;
        [NonSerialized]
        IUIStateHolder m_ParentWindow;
        IConnectionState m_PlayerConnectionState = null;

        [SerializeField]
        CaptureFlags m_CaptureFlags = CaptureFlags.ManagedObjects
            | CaptureFlags.NativeObjects
            | CaptureFlags.NativeAllocations
            | CaptureFlags.NativeAllocationSites
            | CaptureFlags.NativeStackTraces;

        [SerializeField]
        bool m_CaptureWithScreenshot = true;

        [SerializeField]
        bool m_CloseSnapshotsWhenCapturingEditor = true;

        [SerializeField]
        bool m_GCCollectWhenCapturingEditor = true;

        [NonSerialized]
        LegacyReader m_LegacyReader;

        public void Initialize(IUIStateHolder parentWindow, SnapshotCollection snapshotsCollection, OpenSnapshotsWindow openSnapshotsWindow, OpenSnapshotsManager openSnapshotsManager, VisualElement root, VisualElement leftPane)
        {
            m_SnapshotsCollection = snapshotsCollection;
            m_OpenSnapshotsWindow = openSnapshotsWindow;
            m_OpenSnapshotsManager = openSnapshotsManager;
            m_ParentWindow = parentWindow;

            m_LegacyReader = new LegacyReader();

            // Add toolbar functionality
#if UNITY_2020_1_OR_NEWER
            m_PlayerConnectionState = PlayerConnectionGUIUtility.GetConnectionState(parentWindow.Window);
#else
            m_PlayerConnectionState = ConnectionUtility.GetAttachToPlayerState(parentWindow.Window);
#endif
            var captureButton = root.Q<Button>("snapshot-control-area__capture-button");
            m_CaptureButtonWithDropdown = captureButton;
            captureButton.clicked += TakeCapture;
            var captureButtonDropdown = m_CaptureButtonWithDropdown.Q<Button>("snapshot-control-area__capture-dropdown");
            captureButtonDropdown.clicked += () => OpenCaptureFlagsMenu(captureButton.GetRect());

            m_ImportButton = root.Q<ToolbarButton>("toolbar__import-button");
            m_ImportButton.clicked += ImportCapture;

            var exportButton = root.Q<Button>("toolbar__export-button");
            exportButton.clicked += () => OpenExportMenu(exportButton.GetRect());

            // TODO: Implement Export
            UIElementsHelper.SetVisibility(exportButton, false);

            //captureButton.clickable.clicked +=
            var targetSelectionDropdown = root.Q<Button>("snapshot-control-area__target-selection-drop-down-button");
            targetSelectionDropdown.clicked += () => PlayerConnectionCompatibilityHelper.ShowTargetSelectionDropdownMenu(m_PlayerConnectionState, targetSelectionDropdown.GetRect());
            //targetSelectionDropdown.clicked += () => {
            //    var menu = GenerateCaptureFlagsMenu();
            //    menu.AddSeparator("");
            //    PlayerConnectionCompatibilityHelper.ShowTargetSelectionDropdownMenu(m_PlayerConnectionState, targetSelectionDropdown.GetRect(), menu);
            //    };
            EditorCoroutineUtility.StartCoroutine(UpdateTargetSelectionDropdown(targetSelectionDropdown), parentWindow);

#if UNITY_2021_1_OR_NEWER
            CompilationPipeline.compilationStarted += StartedCompilationCallback;
            CompilationPipeline.compilationFinished += FinishedCompilationCallback;
#else
            CompilationPipeline.assemblyCompilationStarted += StartedCompilationCallback;
            CompilationPipeline.assemblyCompilationFinished += FinishedCompilationCallback;
#endif
        }

        public void RegisterAdditionalCaptureButton(Button captureButton)
        {
            captureButton.clicked += TakeCapture;
        }

        IEnumerator UpdateTargetSelectionDropdown(Button targetSelectionDropdown)
        {
            var label = targetSelectionDropdown.Q<Label>();
            var lastConnectionName = "";
            while (m_ParentWindow.Window)
            {
                if (lastConnectionName != m_PlayerConnectionState.connectionName)
                {
                    label.text = PlayerConnectionCompatibilityHelper.GetPlayerDisplayName(m_PlayerConnectionState.connectionName);
                    lastConnectionName = m_PlayerConnectionState.connectionName;
                }
                yield return null;
            }
        }

        void OpenCaptureFlagsMenu(Rect position)
        {
            GenerateCaptureFlagsMenu().DropDown(position);
        }

        GenericMenu GenerateCaptureFlagsMenu()
        {
            bool mo = (uint)(m_CaptureFlags & CaptureFlags.ManagedObjects) != 0;
            bool no = (uint)(m_CaptureFlags & CaptureFlags.NativeObjects) != 0;
            bool na = (uint)(m_CaptureFlags & CaptureFlags.NativeAllocations) != 0;

            var menu = new GenericMenu();
            menu.AddItem(TextContent.CaptureManagedObjectsItem, mo, () => { SetFlag(ref m_CaptureFlags, CaptureFlags.ManagedObjects, !mo); });
            menu.AddItem(TextContent.CaptureNativeObjectsItem, no, () => { SetFlag(ref m_CaptureFlags, CaptureFlags.NativeObjects, !no); });
            //For now disable all the native allocation flags in one go, the call-stack flags will have an effect only when the player has call-stacks support
            menu.AddItem(TextContent.CaptureNativeAllocationsItem, na, () => { SetFlag(ref m_CaptureFlags, CaptureFlags.NativeAllocations | CaptureFlags.NativeStackTraces, !na); });
            menu.AddSeparator("");
            menu.AddItem(TextContent.CaptureScreenshotItem, m_CaptureWithScreenshot, () => { m_CaptureWithScreenshot = !m_CaptureWithScreenshot; });
            if (m_PlayerConnectionState.connectedToTarget == ConnectionTarget.Editor)
            {
                menu.AddItem(TextContent.CloseSnapshotsItem, m_CloseSnapshotsWhenCapturingEditor, () => { m_CloseSnapshotsWhenCapturingEditor = !m_CloseSnapshotsWhenCapturingEditor; });
                menu.AddItem(TextContent.GCCollectItem, m_GCCollectWhenCapturingEditor, () => { m_GCCollectWhenCapturingEditor = !m_GCCollectWhenCapturingEditor; });
            }

            return menu;
        }

        void OpenExportMenu(Rect position)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Not Yet Implemented!"), false, () => {});
            menu.AddSeparator("");
            if (m_OpenSnapshotsWindow.CompareMode)
            {
                if (m_OpenSnapshotsManager.SnapshotALoaded)
                    menu.AddItem(new GUIContent("Export Snapshot A"), false, () => {});
                else
                    menu.AddDisabledItem(new GUIContent("No Snapshot A loaded"));

                if (m_OpenSnapshotsManager.SnapshotBLoaded)
                    menu.AddItem(new GUIContent("Export Snapshot B"), false, () => {});
                else
                    menu.AddDisabledItem(new GUIContent("No Snapshot B loaded"));

                if (m_OpenSnapshotsManager.DiffOpen)
                    menu.AddItem(new GUIContent("Export Diff"), false, () => {});
                else
                    menu.AddDisabledItem(new GUIContent("Open Diff to export it"));
            }
            else
            {
                if (m_OpenSnapshotsManager.SnapshotALoaded)
                    menu.AddItem(new GUIContent("Export Open Snapshot"), false, () => {});
            }
            if (m_OpenSnapshotsManager.SnapshotALoaded)
                menu.AddItem(new GUIContent("Export Current View"), false, () => {});

            menu.DropDown(position);
        }

        void ImportCapture()
        {
            string path = EditorUtility.OpenFilePanelWithFilters(TextContent.ImportSnapshotWindowTitle, MemoryProfilerSettings.LastImportPath, TextContent.MemorySnapshotImportWindowFileExtensions);
            if (path.Length == 0)
            {
                GUIUtility.ExitGUI();
                return;
            }

            if (m_SnapshotsCollection.SnapshotExists(path))
            {
                Debug.LogFormat("{0} has already been imported.", path);
                return;
            }

            MemoryProfilerSettings.LastImportPath = path;

            EditorCoroutineUtility.StartCoroutine(ImportCaptureRoutine(path), m_ParentWindow.Window);
            GUIUtility.ExitGUI();
        }

        IEnumerator ImportCaptureRoutine(string path)
        {
            m_ImportButton.SetEnabled(false);

            MemoryProfilerAnalytics.StartEvent<MemoryProfilerAnalytics.ImportedSnapshotEvent>();
            string targetPath = null;
            ProgressBarDisplay.ShowBar("Importing snapshot.");
            yield return null;
            bool legacy = m_LegacyReader.IsLegacyFileFormat(path);
            if (legacy)
            {
                float initalProgress = 0.15f;
                ProgressBarDisplay.UpdateProgress(initalProgress, "Reading legacy format file.");
                var oldSnapshot = m_LegacyReader.ReadFromFile(path);
                targetPath = Path.Combine(Application.temporaryCachePath, FileExtensionContent.ConvertedSnapshotTempFileName);

                ProgressBarDisplay.UpdateProgress(0.25f, "Converting to current snapshot format.");

                var conversion = LegacyPackedMemorySnapshotConverter.Convert(oldSnapshot, targetPath);
                conversion.MoveNext(); //start execution

                if (conversion.Current == null)
                {
                    ProgressBarDisplay.ClearBar();
                    MemoryProfilerAnalytics.CancelEvent<MemoryProfilerAnalytics.ImportedSnapshotEvent>();
                    m_ImportButton.SetEnabled(true);
                    yield break;
                }

                var status = conversion.Current as EnumerationStatus;
                float progressPerStep = (1.0f - initalProgress) / status.StepCount;
                while (conversion.MoveNext())
                {
                    ProgressBarDisplay.UpdateProgress(initalProgress + status.CurrentStep * progressPerStep, status.StepStatus);
                }
            }
            else
            {
                targetPath = path;
            }

            m_SnapshotsCollection.AddSnapshotToCollection(targetPath, legacy ? ImportMode.Move : ImportMode.Copy);

            ProgressBarDisplay.ClearBar();
            MemoryProfilerAnalytics.EndEvent(new MemoryProfilerAnalytics.ImportedSnapshotEvent());

            m_ImportButton.SetEnabled(true);
            m_ImportButton.MarkDirtyRepaint();
        }

        void TakeCapture()
        {
            if (EditorApplication.isCompiling)
            {
                Debug.LogError("Unable to snapshot while compilation is ongoing");
                return;
            }

            if (EditorUtilityCompatibilityHelper.DisplayDialog(TextContent.HeapWarningWindowTitle,
                TextContent.HeapWarningWindowContent, TextContent.HeapWarningWindowOK,
                EditorUtilityCompatibilityHelper.DialogOptOutDecisionType.ForThisMachine, MemoryProfilerSettings.HeapWarningWindowOptOutKey))
            {
                EditorCoroutineUtility.StartCoroutine(DelayedSnapshotRoutine(), m_ParentWindow.Window);
            }
            GUIUtility.ExitGUI();
        }

        void SetFlag(ref CaptureFlags target, CaptureFlags bit, bool on)
        {
            if (on)
                target |= bit;
            else
                target &= ~bit;
        }

        void StartedCompilationCallback(object msg)
        {
            //Disable the capture button during compilation.
            m_CaptureButtonWithDropdown.SetEnabled(false);
        }

#if UNITY_2021_1_OR_NEWER
        void FinishedCompilationCallback(object msg)
#else
        void FinishedCompilationCallback(string msg, UnityEditor.Compilation.CompilerMessage[] compilerMsg)
#endif
        {
            m_CaptureButtonWithDropdown.SetEnabled(true);
        }

        public void OnDisable()
        {
            if (m_PlayerConnectionState != null)
            {
                m_PlayerConnectionState.Dispose();
                m_PlayerConnectionState = null;
            }

#if UNITY_2021_1_OR_NEWER
            CompilationPipeline.compilationStarted -= StartedCompilationCallback;
            CompilationPipeline.compilationFinished -= FinishedCompilationCallback;
#else
            CompilationPipeline.assemblyCompilationStarted -= StartedCompilationCallback;
            CompilationPipeline.assemblyCompilationFinished -= FinishedCompilationCallback;
#endif
        }

        void CopyDataToTexture(Texture2D tex, NativeArray<byte> byteArray)
        {
            unsafe
            {
                void* srcPtr = NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(byteArray);
                void* dstPtr = tex.GetRawTextureData<byte>().GetUnsafeReadOnlyPtr();
                UnsafeUtility.MemCpy(dstPtr, srcPtr, byteArray.Length * sizeof(byte));
            }
        }

        void ClearAllDataAndGCCollect()
        {
            if (m_PlayerConnectionState.connectedToTarget == ConnectionTarget.Editor && m_CloseSnapshotsWhenCapturingEditor)
            {
                m_OpenSnapshotsManager.CloseAllOpenSnapshots();
                m_ParentWindow.UIState.ClearAllOpenModes();
            }
            if (m_PlayerConnectionState.connectedToTarget == ConnectionTarget.Editor && m_GCCollectWhenCapturingEditor)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        bool m_SnapshotInProgress;
        bool m_ScreenshotInProgress;
#if UNITY_2019_3_OR_NEWER
        double m_TimestamprOfLastSnapshotReceived;
        const double k_TimeOutForScreenshots = 60;
#endif
        IEnumerator DelayedSnapshotRoutine()
        {
            if (m_SnapshotInProgress || m_ScreenshotInProgress)
            {
                if (m_SnapshotInProgress)
                    Debug.LogWarning("Snapshot already in progress.");

                if (m_ScreenshotInProgress)
                    Debug.LogWarning("Screenshot already in progress.");

                yield break;
            }

            ProgressBarDisplay.ShowBar("Memory capture");
            ProgressBarDisplay.UpdateProgress(0.0f, "Taking capture...");

            // make sure the collection is up to date with the state of the folder
            // snapshots added to the folder between here and adding the snapshot to the selection will not show up in the UI until the next refresh
            if (m_SnapshotsCollection.CheckSnapshotRootDirectoryForChanges())
                m_SnapshotsCollection.RefreshCollection();

            yield return null; //skip one frame so we can draw the progress bar

            m_SnapshotInProgress = true;
            m_ScreenshotInProgress = true;

            MemoryProfilerAnalytics.StartEvent<MemoryProfilerAnalytics.CapturedSnapshotEvent>();

            ClearAllDataAndGCCollect();

            string basePath = Path.Combine(MemoryProfilerSettings.AbsoluteMemorySnapshotStoragePath, FileExtensionContent.SnapshotTempFileName);

            bool snapshotCaptureResult = false;
            bool screenshotCaptureResult = false;
            string capturePath = "";

            Action<string, bool, DebugScreenCapture> screenshotCaptureFunc = null;
            if (m_CaptureWithScreenshot)
            {
                screenshotCaptureFunc = (string path, bool result, DebugScreenCapture screenCapture) =>
                {
                    var wasStillWaitingForScreenshot = m_ScreenshotInProgress; // or did it time out?
                    m_ScreenshotInProgress = false;
                    screenshotCaptureResult = result;
                    if (!screenshotCaptureResult)
                        return;

                    if (screenCapture.rawImageDataReference.Length == 0)
                        return;

                    if (wasStillWaitingForScreenshot) // only update progress if not timed out
                        ProgressBarDisplay.UpdateProgress(0.8f, "Processing Screenshot");

                    if (Path.HasExtension(path))
                    {
                        path = Path.ChangeExtension(path, FileExtensionContent.SnapshotTempScreenshotFileExtension);
                    }

                    Texture2D tex = new Texture2D(screenCapture.width, screenCapture.height, screenCapture.imageFormat, false);
                    CopyDataToTexture(tex, screenCapture.rawImageDataReference);
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                    if (Application.isPlaying)
                        UnityEngine.Object.Destroy(tex);
                    else
                        UnityEngine.Object.DestroyImmediate(tex);
                };
            }

            Action<string, bool> snapshotCaptureFunc = (string path, bool result) =>
            {
                m_SnapshotInProgress = false;
                m_TimestamprOfLastSnapshotReceived = EditorApplication.timeSinceStartup;

                snapshotCaptureResult = result;

                capturePath = path;
            };

            if (m_CaptureWithScreenshot && (m_PlayerConnectionState.connectedToTarget == ConnectionTarget.Player || Application.isPlaying))
            {
                QueryMemoryProfiler.TakeSnapshot(basePath, snapshotCaptureFunc, screenshotCaptureFunc, m_CaptureFlags);
            }
            else
            {
                QueryMemoryProfiler.TakeSnapshot(basePath, snapshotCaptureFunc, m_CaptureFlags);
                m_ScreenshotInProgress = false; //screenshot is not in progress
            }

            ProgressBarDisplay.UpdateProgress(1.0f);

            //wait for snapshotting operation to finish and skip one frame to update loading bar
            int skipFrames = 2;
            while (skipFrames > 0)
            {
                if (!m_SnapshotInProgress)
                    --skipFrames;

                yield return null;
            }

            //wait for screenshot op to complete and time out if it does not
            while (m_ScreenshotInProgress)
            {
                if (EditorApplication.timeSinceStartup - m_TimestamprOfLastSnapshotReceived >= k_TimeOutForScreenshots)
                {
                    m_ScreenshotInProgress = false;
                    break;
                }
                yield return null;
            }

            MemoryProfilerAnalytics.EndEvent(new MemoryProfilerAnalytics.CapturedSnapshotEvent() { success = snapshotCaptureResult });

            if (snapshotCaptureResult)
            {
                string snapshotPath = Path.Combine(MemoryProfilerSettings.AbsoluteMemorySnapshotStoragePath, FileExtensionContent.SnapshotFileNamePart + DateTime.Now.Ticks + FileExtensionContent.SnapshotFileExtension);
                File.Move(capturePath, snapshotPath);

                if (screenshotCaptureResult)
                {
                    capturePath = Path.ChangeExtension(capturePath, FileExtensionContent.SnapshotTempScreenshotFileExtension);
                    string screenshotPath = Path.ChangeExtension(snapshotPath, FileExtensionContent.SnapshotScreenshotFileExtension);
                    File.Move(capturePath, screenshotPath);
                }

                m_SnapshotsCollection.AddSnapshotToCollection(snapshotPath, ImportMode.Move);
            }

            ProgressBarDisplay.ClearBar();
        }
    }
}

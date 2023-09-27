using System;
using System.IO;
using System.Collections;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.Networking.PlayerConnection;
using UnityEngine;
using UnityEngine.Networking.PlayerConnection;
using Unity.Profiling;
using Unity.MemoryProfiler.Editor.UI;
using Unity.MemoryProfiler.Editor.UIContentData;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.EditorCoroutines.Editor;
using QueryMemoryProfiler = Unity.Profiling.Memory.MemoryProfiler;
using UnityEngine.Profiling;

namespace Unity.MemoryProfiler.Editor
{
    internal class PlayerConnectionService : IDisposable
    {
        EditorWindow m_Window;

        IConnectionState m_PlayerConnectionState;
        ISnapshotDataService m_SnapshotDataService;
        string m_ConnectionName;
        string m_ConnectionDisplayName;

        // Capture state
        bool m_CaptureInProgress; // Exclusively for the tests
        bool m_SnapshotInProgress;
        bool m_ScreenshotInProgress;
        double m_TimestamprOfLastSnapshotReceived;
        const double k_TimeOutForScreenshots = 60;

        public PlayerConnectionService(EditorWindow window, ISnapshotDataService snapshotDataService)
        {
            m_Window = window;

            m_PlayerConnectionState = PlayerConnectionGUIUtility.GetConnectionState(m_Window);
            m_SnapshotDataService = snapshotDataService;

            CompilationPipeline.compilationStarted += StartedCompilationCallback;
            CompilationPipeline.compilationFinished += FinishedCompilationCallback;

            EditorCoroutineUtility.StartCoroutine(PollTargetConnectionName(), m_Window);
        }

        public event Action PlayerConnectionChanged = delegate { };

        public string PlayerConnectionName => m_ConnectionDisplayName;
        public bool IsConnectedToEditor => m_PlayerConnectionState?.connectedToTarget == ConnectionTarget.Editor;
        internal bool IsCaptureInProgress => m_CaptureInProgress;

        public void ShowPlayerConnectionSelection(Rect rect)
        {
            PlayerConnectionCompatibilityHelper.ShowTargetSelectionDropdownMenu(m_PlayerConnectionState, rect);
        }

        public void TakeCapture()
        {
            if (EditorApplication.isCompiling)
            {
                Debug.LogError("Unable to snapshot while compilation is ongoing");
                return;
            }

            if (Application.isBatchMode || EditorUtilityCompatibilityHelper.DisplayDialog(TextContent.HeapWarningWindowTitle,
                TextContent.HeapWarningWindowContent, TextContent.HeapWarningWindowOK,
                EditorUtilityCompatibilityHelper.DialogOptOutDecisionType.ForThisMachine, MemoryProfilerSettings.HeapWarningWindowOptOutKey))
            {
                m_CaptureInProgress = true;
                EditorCoroutineUtility.StartCoroutine(DelayedSnapshotRoutine(), m_Window);
            }
            GUIUtility.ExitGUI();
        }

        void StartedCompilationCallback(object msg)
        {
            //Disable the capture button during compilation.
            //m_CaptureButtonWithDropdown.SetEnabled(false);
        }

        void FinishedCompilationCallback(object msg)
        {
            //m_CaptureButtonWithDropdown.SetEnabled(true);
        }

        IEnumerator PollTargetConnectionName()
        {
            while (m_Window && m_PlayerConnectionState != null)
            {
                if (m_ConnectionName != m_PlayerConnectionState.connectionName)
                {
                    m_ConnectionDisplayName = PlayerConnectionCompatibilityHelper.GetPlayerDisplayName(m_PlayerConnectionState.connectionName);
                    m_ConnectionName = m_PlayerConnectionState.connectionName;
                    PlayerConnectionChanged.Invoke();
                }
                yield return null;
            }
        }

        internal IEnumerator DelayedSnapshotRoutine()
        {
            if (m_SnapshotInProgress || m_ScreenshotInProgress)
            {
                if (m_SnapshotInProgress)
                    Debug.LogWarning("Snapshot already in progress.");

                if (m_ScreenshotInProgress)
                    Debug.LogWarning("Screenshot already in progress.");

                m_CaptureInProgress = false;
                yield break;
            }

            ProgressBarDisplay.ShowBar("Memory capture");
            yield return null; //skip one frame so we can draw the progress bar

            m_SnapshotInProgress = true;
            m_ScreenshotInProgress = true;

            using var capturedSnapshotEvent = MemoryProfilerAnalytics.BeginCapturedSnapshotEvent();

            if (m_PlayerConnectionState.connectedToTarget == ConnectionTarget.Editor)
            {
                if (MemoryProfilerSettings.CloseSnapshotsWhenCapturingEditor)
                {
                    ProgressBarDisplay.UpdateProgress(0.0f, "Closing snapshot...");
                    m_SnapshotDataService.UnloadAll();
                    // Skip a frame
                    yield return null;
                }

                if (MemoryProfilerSettings.GCCollectWhenCapturingEditor)
                {
                    ProgressBarDisplay.UpdateProgress(0.1f, "Collecting garbage...");

                    var oldGCReserved = Profiler.GetMonoHeapSizeLong();
                    // Repeat 6 times together with finalizers wait to give a higher chance to boehm to catch up with pending finalizers.
                    // Finalizers delay collection of objects and may cause other object being considered as referenced.
                    for (var i = 0; i < 6; ++i)
                    {
                        GC.Collect();
                        // Ensure finalizers are executed so we can collect objects with pending finalizers next iteration
                        GC.WaitForPendingFinalizers();
                        // Early exit if we were able to collect successfully and release free heap sections.
                        if (Profiler.GetMonoHeapSizeLong() < oldGCReserved)
                            break;
                    }
                }
            }

            ProgressBarDisplay.UpdateProgress(0.2f, "Taking capture...");

            var snapshotFolderPath = m_SnapshotDataService.GetSnapshotFolderPath();
            string basePath = Path.Combine(snapshotFolderPath, FileExtensionContent.SnapshotTempFileName);

            bool snapshotCaptureResult = false;
            bool screenshotCaptureResult = false;
            string capturePath = "";

            Action<string, bool, DebugScreenCapture> screenshotCaptureFunc = null;
            if (MemoryProfilerSettings.CaptureWithScreenshot)
            {
                screenshotCaptureFunc = (string path, bool result, DebugScreenCapture screenCapture) =>
                {
                    var wasStillWaitingForScreenshot = m_ScreenshotInProgress; // or did it time out?
                    m_ScreenshotInProgress = false;
                    screenshotCaptureResult = result;
                    if (!screenshotCaptureResult)
                        return;

                    if (screenCapture.RawImageDataReference.Length == 0)
                        return;

                    if (wasStillWaitingForScreenshot) // only update progress if not timed out
                        ProgressBarDisplay.UpdateProgress(0.8f, "Processing Screenshot");

                    if (Path.HasExtension(path))
                    {
                        path = Path.ChangeExtension(path, FileExtensionContent.SnapshotTempScreenshotFileExtension);
                    }

                    Texture2D tex = new Texture2D(screenCapture.Width, screenCapture.Height, screenCapture.ImageFormat, false);
                    CopyDataToTexture(tex, screenCapture.RawImageDataReference);
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

            try
            {
                if (MemoryProfilerSettings.CaptureWithScreenshot && (m_PlayerConnectionState.connectedToTarget == ConnectionTarget.Player || Application.isPlaying))
                {
                    QueryMemoryProfiler.TakeSnapshot(basePath, snapshotCaptureFunc, screenshotCaptureFunc, MemoryProfilerSettings.MemoryProfilerCaptureFlags);
                }
                else
                {
                    QueryMemoryProfiler.TakeSnapshot(basePath, snapshotCaptureFunc, MemoryProfilerSettings.MemoryProfilerCaptureFlags);
                    m_ScreenshotInProgress = false; //screenshot is not in progress
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to take a Memory Snapshot. Configured storage path was: {basePath}");
                throw ex;
            }

            capturedSnapshotEvent.SetResult(snapshotCaptureResult);
            ProgressBarDisplay.UpdateProgress(0.7f);

            // Wait for snapshotting operation to finish and skip one frame to update loading bar
            int skipFrames = 2;
            while (skipFrames > 0)
            {
                if (!m_SnapshotInProgress)
                    --skipFrames;

                yield return null;
            }

            // Wait for screenshot op to complete and time out if it does not
            while (m_ScreenshotInProgress)
            {
                if (EditorApplication.timeSinceStartup - m_TimestamprOfLastSnapshotReceived >= k_TimeOutForScreenshots)
                {
                    m_ScreenshotInProgress = false;
                    break;
                }
                yield return null;
            }

            if (snapshotCaptureResult)
            {
                ProgressBarDisplay.UpdateProgress(0.9f, "Copying capture...");

                // Read meta information to build human readable file name
                var modelBuilder = new SnapshotFileModelBuilder(capturePath);
                var snapshotFileModel = modelBuilder.Build();
                FileInfo file = new FileInfo(capturePath);
                var dateString = snapshotFileModel.Timestamp.ToLocalTime().ToString("yyyy-MM-dd_HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture);

                // Santise the product name
                var invalidChars = Path.GetInvalidFileNameChars();
                StringBuilder prodNameSanitised = new StringBuilder(snapshotFileModel.ProductName);
                for (int i = 0; i < invalidChars.Length; i++)
                {
                    prodNameSanitised.Replace(invalidChars[i], '_');
                }

                var finalFileName = $"{prodNameSanitised}_{dateString}{FileExtensionContent.SnapshotFileExtension}";

                // Move file to the final location
                string snapshotPath = Path.Combine(snapshotFolderPath, finalFileName);
                try
                {
                    File.Move(capturePath, snapshotPath);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to store Memory Snapshot. Attempted to store as: {snapshotPath}");
                    throw ex;
                }
                if (screenshotCaptureResult)
                {
                    capturePath = Path.ChangeExtension(capturePath, FileExtensionContent.SnapshotTempScreenshotFileExtension);
                    string screenshotPath = Path.ChangeExtension(snapshotPath, FileExtensionContent.SnapshotScreenshotFileExtension);
                    File.Move(capturePath, screenshotPath);
                }
            }

            ProgressBarDisplay.ClearBar();

            if (snapshotCaptureResult)
                m_SnapshotDataService.SyncSnapshotsFolder();

            m_CaptureInProgress = false;
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

        public void Dispose()
        {
            CompilationPipeline.compilationStarted -= StartedCompilationCallback;
            CompilationPipeline.compilationFinished -= FinishedCompilationCallback;

            m_PlayerConnectionState?.Dispose();
            m_PlayerConnectionState = null;
        }
    }
}

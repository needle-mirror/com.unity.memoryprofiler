using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Unity.MemoryProfiler.Editor.Analytics;
using Unity.MemoryProfiler.Editor.UI;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Analytics;

namespace Unity.MemoryProfiler.Editor
{
    // the internal APIs for Analytic Events do not follow the correct naming guidelines and sometimes using odd variable names.
    // This is because their data and field names gets serialized out and send to our analytics back-end using those field names.
    // There is a fixed set of expected field names that will get processed. Please check our internal Analytics spreadsheet, linked to from the Dashboard for more info.
    static class MemoryProfilerAnalytics
    {
        const int k_MaxEventsPerHour = 100; // Max Events send per hour.
        const int k_MaxNumberOfElements = 1000; //Max number of elements sent.
        const string k_VendorKey = "unity.memoryprofiler";
        const string k_EventTopicName = "memoryProfiler";

        static IAnalyticsService s_AnalyticsService;
        static bool s_MemoryProfilerEventRegistered;

        /// <summary>
        /// Set the IAnalyticsService which would register and send events to the analytics endpoint
        /// </summary>
        /// <param name="service">IAnalyticsService provider</param>
        /// <returns></returns>
        public static IAnalyticsService SetAnalyticsService(IAnalyticsService service)
        {
            var oldService = s_AnalyticsService;
            s_AnalyticsService = service;

            // Reset event registration
            s_MemoryProfilerEventRegistered = false;

            return oldService;
        }

        /// <summary>
        /// Enable Memory Profiler analytics service and register all required events
        /// </summary>
        public static void EnableAnalytics()
        {
            // Instantiate default editor analytics if no service is set.
            // But only only for user sessions when analytics is globally enabled.
            if (s_AnalyticsService == null && !InternalEditorUtility.inBatchMode && EditorAnalytics.enabled)
                SetAnalyticsService(new EditorAnalyticsService());

            // Register the main and only event
            if (!s_MemoryProfilerEventRegistered && s_AnalyticsService != null)
            {
                s_MemoryProfilerEventRegistered = s_AnalyticsService.RegisterEventWithLimit(k_EventTopicName, k_MaxEventsPerHour, k_MaxNumberOfElements, k_VendorKey);
                if (!s_MemoryProfilerEventRegistered)
                    s_AnalyticsService = null;
            }
        }

        /// <summary>
        /// Flush all pending events and deactivate analytics
        /// </summary>
        public static void DisableAnalytics()
        {
            if (s_AnalyticsService == null)
                return;

            // Flush interaction events when we disable analytics
            FlushInteractionEvents();
        }

        static void SendPayload<T>(T payload)
        {
            s_AnalyticsService?.SendEventWithLimit(k_EventTopicName, payload);
        }


        /// <summary>
        /// Measures duration and reports the result of the capture operation
        /// </summary>
        public struct CapturedSnapshotEvent : IDisposable
        {
            [Serializable]
            [StructLayout(LayoutKind.Sequential)]
            internal struct Payload
            {
                public string subtype;
                public float duration;
                public bool success;
            }

            readonly double m_StartTime;
            bool m_Result;

            internal CapturedSnapshotEvent(double startTime)
            {
                m_StartTime = startTime;
                m_Result = false;
            }

            public void SetResult(bool result)
            {
                m_Result = result;
            }

            public void Dispose()
            {
                var payload = new Payload()
                {
                    subtype = "captureSnapshot",
                    duration = (float)(EditorApplication.timeSinceStartup - m_StartTime),
                    success = m_Result
                };
                SendPayload(payload);
            }
        }

        public static CapturedSnapshotEvent BeginCapturedSnapshotEvent()
        {
            return new CapturedSnapshotEvent(EditorApplication.timeSinceStartup);
        }

        /// <summary>
        /// Measures and reports snapshot opening operation.
        /// Platform is send as RuntimePlatform value in 'show' field
        /// </summary>
        public struct LoadSnapshotEvent : IDisposable
        {
            [Serializable]
            [StructLayout(LayoutKind.Sequential)]
            internal struct Payload
            {
                public string subtype;
                public float duration;
                public bool success;
                public int shown;
                public int show;
                public string fileName;
            }

            readonly double m_StartTime;
            bool m_ProductNameBelongsToSameProject;
            SnapshotProjectAndUnityVersionDetails m_SnapshotDetails;
            string m_UnityVersionOfSnapshot;
            RuntimePlatform m_RuntimePlatform;

            internal LoadSnapshotEvent(double startTime)
            {
                m_StartTime = startTime;
                m_ProductNameBelongsToSameProject = false;
                m_SnapshotDetails = SnapshotProjectAndUnityVersionDetails.None;
                m_UnityVersionOfSnapshot = null;
                m_RuntimePlatform = (RuntimePlatform)(-1);
            }

            public void SetResult(CachedSnapshot cachedSnapshot)
            {
                m_ProductNameBelongsToSameProject = cachedSnapshot.MetaData.ProductName == Application.productName;
                m_SnapshotDetails = GetSnapshotProjectAndUnityVersionDetails(cachedSnapshot);
                m_UnityVersionOfSnapshot = cachedSnapshot.MetaData.UnityVersion;
                m_RuntimePlatform = PlatformsHelper.GetRuntimePlatform(cachedSnapshot.MetaData.Platform);
            }

            public void Dispose()
            {
                var payload = new Payload()
                {
                    subtype = "loadSnapshot",
                    success = m_ProductNameBelongsToSameProject,
                    shown = (int)m_SnapshotDetails,
                    fileName = m_UnityVersionOfSnapshot,
                    show = (int)m_RuntimePlatform,
                    duration = (float)(EditorApplication.timeSinceStartup - m_StartTime),
                };
                SendPayload(payload);
            }
        }

        // These values can be added to but existing flags shouldn't change their meaning as the backend would not know how to adjust
        [Flags]
        internal enum SnapshotProjectAndUnityVersionDetails : int
        {
            None = 0,
            SameSession = 1 << 0,
            SameProject = 1 << 1,
            SameUnityVersion = 1 << 2,
            SnapshotFromOlderUnityVersion = 1 << 3,
            SnapshotFromNewerUnityVersion = 1 << 4,
            MajorVersionJump = 1 << 5,
            MinorVersionJump = 1 << 6,
            PatchVersionJump = 1 << 7,
            EditorSnapshot = 1 << 8,
            SamePlatformAsEditor = 1 << 9,
            SamePlatformAsDiff = 1 << 10,
        }

        static SnapshotProjectAndUnityVersionDetails GetSnapshotProjectAndUnityVersionDetails(CachedSnapshot snapshot)
        {
            var snapshotDetails = MemoryProfilerAnalytics.SnapshotProjectAndUnityVersionDetails.None;
            if (snapshot.MetaData.SessionGUID == EditorSessionUtility.CurrentSessionId)
                snapshotDetails |= MemoryProfilerAnalytics.SnapshotProjectAndUnityVersionDetails.SameSession;
            if (snapshot.MetaData.ProductName == Application.productName)
                snapshotDetails |= MemoryProfilerAnalytics.SnapshotProjectAndUnityVersionDetails.SameProject;
            if (snapshot.MetaData.UnityVersion == Application.unityVersion)
                snapshotDetails |= MemoryProfilerAnalytics.SnapshotProjectAndUnityVersionDetails.SameUnityVersion;
            else
            {
                if (string.Compare(snapshot.MetaData.UnityVersion, Application.unityVersion) > 0)
                    snapshotDetails |= MemoryProfilerAnalytics.SnapshotProjectAndUnityVersionDetails.SnapshotFromNewerUnityVersion;
                else
                    snapshotDetails |= MemoryProfilerAnalytics.SnapshotProjectAndUnityVersionDetails.SnapshotFromOlderUnityVersion;
                var snapshotVersion = snapshot.MetaData.UnityVersion.Split('.');
                var editorVersion = Application.unityVersion.Split('.');
                if (snapshotVersion != null && editorVersion != null && snapshotVersion.Length >= 3 && editorVersion.Length >= 3)
                {
                    if (snapshotVersion[0] != editorVersion[0])
                        snapshotDetails |= MemoryProfilerAnalytics.SnapshotProjectAndUnityVersionDetails.MajorVersionJump;
                    else if (snapshotVersion[1] != editorVersion[1])
                        snapshotDetails |= MemoryProfilerAnalytics.SnapshotProjectAndUnityVersionDetails.MinorVersionJump;
                    else if (snapshotVersion[2] != editorVersion[2])
                        snapshotDetails |= MemoryProfilerAnalytics.SnapshotProjectAndUnityVersionDetails.PatchVersionJump;
                }
            }

            var platform = PlatformsHelper.GetRuntimePlatform(snapshot.MetaData.Platform);
            if (PlatformsHelper.RuntimePlatformIsEditorPlatform(platform))
            {
                snapshotDetails |= SnapshotProjectAndUnityVersionDetails.EditorSnapshot;
                snapshotDetails |= SnapshotProjectAndUnityVersionDetails.SamePlatformAsEditor;
            }
            else
            {
                if (PlatformsHelper.SameRuntimePlatformAsEditorPlatform(platform))
                    snapshotDetails |= SnapshotProjectAndUnityVersionDetails.SamePlatformAsEditor;
            }
            return snapshotDetails;
        }

        public static LoadSnapshotEvent BeginLoadSnapshotEvent()
        {
            // We flush all interaction events as soon as we change mode or load a snapshot
            FlushInteractionEvents();

            return new LoadSnapshotEvent(EditorApplication.timeSinceStartup);
        }

        /// <summary>
        /// Reports and measures duration of the snapshot importing (copying).
        /// </summary>
        public readonly struct ImportSnapshotEvent : IDisposable
        {
            [Serializable]
            [StructLayout(LayoutKind.Sequential)]
            internal struct Payload
            {
                public string subtype;
                public float duration;
            }

            readonly double m_StartTime;

            internal ImportSnapshotEvent(double startTime)
            {
                m_StartTime = startTime;
            }

            public void Dispose()
            {
                var payload = new Payload()
                {
                    subtype = "importSnapshot",
                    duration = (float)(EditorApplication.timeSinceStartup - m_StartTime),
                };
                SendPayload(payload);
            }
        }

        public static ImportSnapshotEvent BeginImportSnapshotEvent()
        {
            return new ImportSnapshotEvent(EditorApplication.timeSinceStartup);
        }

        /// <summary>
        /// Reports and measures view open event.
        /// </summary>
        public readonly struct OpenViewEvent : IDisposable
        {
            /// <summary>
            /// Reports visible view state - view is opened - for various UI elements in Memory Profiler Window.
            /// </summary>
            [Serializable]
            [StructLayout(LayoutKind.Sequential)]
            internal struct Payload
            {
                public string subtype;
                public string viewname;
                public int shown;
                public float duration;
            }


            readonly string m_ViewName;
            readonly double m_StartTime;


            internal OpenViewEvent(string viewName, double startTime)
            {
                m_ViewName = viewName;
                m_StartTime = startTime;
            }

            public void Dispose()
            {
                var payload = new Payload()
                {
                    subtype = "openView",
                    viewname = m_ViewName,
                    shown = 0,
                    duration = (float)(EditorApplication.timeSinceStartup - m_StartTime),
                };
                SendPayload(payload);
            }
        }

        /// <summary>
        /// Begin scoped openView event which will be sent in Dispose and include timed duration
        /// </summary>
        /// <param name="viewName"></param>
        /// <returns>OpenViewEvent which sends analytics message on Dispose call</returns>
        public static OpenViewEvent BeginOpenViewEvent(string viewName)
        {
            UpdateSummaryViewName(viewName);

            return new OpenViewEvent(viewName, EditorApplication.timeSinceStartup);
        }

        /// <summary>
        /// Send one-shot openView event with 0 duration
        /// </summary>
        /// <param name="viewName"></param>
        /// <param name="openByDefault"></param>
        public static void SendOpenViewEvent(string viewName, bool openByDefault)
        {
            // We flush all interaction events as soon as we change mode or load a snapshot
            if (openByDefault)
                FlushInteractionEvents();

            UpdateSummaryViewName(viewName);

            var payload = new OpenViewEvent.Payload()
            {
                subtype = "openView",
                viewname = viewName,
                shown = openByDefault ? 1 : 0,
                duration = 0
            };
            SendPayload(payload);
        }

        /// <summary>
        /// Reports visible view state - view is opened - for various UI elements in Memory Profiler Window.
        /// </summary>
        [Serializable]
        [StructLayout(LayoutKind.Sequential)]
        struct InteractionViewEventPayload
        {
            public string subtype;
            public string viewname;
            public string filename;
        }

        class ViewInteractionTracker<T> where T :
#if CSHARP_7_3_OR_NEWER
        unmanaged, Enum
#else
        struct
#endif
        {
            public ViewInteractionTracker(string viewName)
            {
                m_ViewName = viewName;
                m_Interactions = new Dictionary<T, int>();
            }

            public void AddInteraction(T interaction)
            {
                if (m_Interactions.ContainsKey(interaction))
                    ++m_Interactions[interaction];
                else
                    m_Interactions.Add(interaction, 1);
            }

            public void UpdateViewName(string viewName) => m_ViewName = viewName;

            public void Flush()
            {
                if (m_Interactions.Count == 0)
                    return;

                if (m_ViewName != null)
                {
                    var sb = new StringBuilder("{");
                    var firstElement = true;
                    foreach (var interaction in m_Interactions)
                    {
                        if (!firstElement)
                            sb.Append(",");
                        sb.Append("\"");
                        sb.Append(interaction.Key);
                        sb.Append("\":");
                        sb.Append(interaction.Value.ToString());
                        firstElement = false;
                    }
                    sb.Append("}");
                    var jsonPayload = sb.ToString();
                    var payload = new InteractionViewEventPayload()
                    {
                        subtype = "interactionsInView",
                        viewname = m_ViewName,
                        filename = jsonPayload
                    };
                    SendPayload(payload);
                }

                m_Interactions.Clear();
            }

            Dictionary<T, int> m_Interactions;
            string m_ViewName;
        }

        /// <summary>
        /// Summary view interactions.
        /// Adding new items is fine but NO renaming of these.
        /// </summary>
        public enum SummaryViewInteraction
        {
            ResidentMemoryClickCount,
            AllocatedMemoryClickCount,
            ManagedMemoryClickCount,
            UnityObjectsClickCount,
            ResidentMemoryHoverCount,
            AllocatedMemoryHoverCount,
            ManagedMemoryHoverCount,
            UnityObjectsHoverCount,
            InspectAllocatedMemoryClickCount,
            InspectManagedMemoryClickCount,
            InspectUnityObjectsClickCount,
        }

        // Adding new items is fine but ideally no renaming of these or the analytics backend would need loose version over version consistency
        public enum ReferencePanelInteraction
        {
            SelectionInTableCount,
            TreeViewElementExpandCount,
        }

        /// <summary>
        /// Details view interactions.
        /// Adding new items is fine but NO renaming of these.
        /// </summary>
        public enum SelectionDetailsViewInteraction
        {
            DocumentationOpenCount,
            SelectInEditorButtonClickCount,
            SearchInEditorButtonClickCount,
            QuickSearchButtonClickCount,
        }

        /// <summary>
        /// Unity Objects view in single mode interactions
        /// </summary>
        enum UnityObjectsViewUsage
        {
            BuildCount,
            FilterUsedCount,
            FlattenHierarchyUsedCount,
            DuplicateToggleUsedCount,
            OnlyCommittedModeUsedCount,
            OnlyResidentModeUsedCount,
            CommittedAndResidentModeUsedCount
        }

        /// <summary>
        /// Unity Objects usage in Compare mode
        /// </summary>
        enum UnityObjectsComparisonUsage
        {
            BuildCount,
            FilterUsedCount,
            FlattenHierarchyUsedCount,
            ShowUnchangedUsedCount,
        }

        /// <summary>
        /// All of Memory usage
        /// </summary>
        enum AllTrackedMemoryUsage
        {
            BuildCount,
            FilterUsedCount,
            ShowReservedUsedCount,
            OnlyCommittedModeUsedCount,
            OnlyResidentModeUsedCount,
            CommittedAndResidentModeUsedCount,
        }

        /// <summary>
        /// All of Memory usage in compare mode
        /// </summary>
        enum AllTrackedMemoryComparisonUsage
        {
            BuildCount,
            FilterUsedCount,
            ShowReservedUsedCount,
            ShowUnchangedUsedCount,
        }

        /// <summary>
        /// Memory Map usage
        /// </summary>
        enum MemoryMapUsage
        {
            BuildCount,
            FilterUsedCount,
        }

        public static void AddSummaryViewInteraction(SummaryViewInteraction interaction)
        {
            s_SummaryViewInteractions.AddInteraction(interaction);
        }

        /// <summary>
        /// Called when any view is activated to update summary view name.
        /// (Single and compare mode is implemented by the same SummaryViewController)
        /// </summary>
        /// <param name="newViewName">Activated view name</param>
        static void UpdateSummaryViewName(string newViewName)
        {
            if (newViewName != null && !newViewName.Contains(TextContent.SummaryViewName))
                return;

            s_SummaryViewInteractions.UpdateViewName(newViewName);
        }

        public static void AddUnityObjectsUsage(bool filterUsed, bool flattenHierarchyUsed, bool duplicateToggleUsed, AllTrackedMemoryTableMode tableModeUsed)
        {
            s_UnityObjectsViewUsage.AddInteraction(UnityObjectsViewUsage.BuildCount);
            if (filterUsed)
                s_UnityObjectsViewUsage.AddInteraction(UnityObjectsViewUsage.FilterUsedCount);
            if (flattenHierarchyUsed)
                s_UnityObjectsViewUsage.AddInteraction(UnityObjectsViewUsage.FlattenHierarchyUsedCount);
            if (duplicateToggleUsed)
                s_UnityObjectsViewUsage.AddInteraction(UnityObjectsViewUsage.DuplicateToggleUsedCount);
            switch (tableModeUsed)
            {
                case AllTrackedMemoryTableMode.OnlyCommitted:
                    s_UnityObjectsViewUsage.AddInteraction(UnityObjectsViewUsage.OnlyCommittedModeUsedCount);
                    break;
                case AllTrackedMemoryTableMode.OnlyResident:
                    s_UnityObjectsViewUsage.AddInteraction(UnityObjectsViewUsage.OnlyResidentModeUsedCount);
                    break;
                case AllTrackedMemoryTableMode.CommittedAndResident:
                    s_UnityObjectsViewUsage.AddInteraction(UnityObjectsViewUsage.CommittedAndResidentModeUsedCount);
                    break;
            }
        }

        public static void AddReferencePanelInteraction(ReferencePanelInteraction interaction)
        {
            s_ReferencePanelUsage.AddInteraction(interaction);
        }

        public static void AddSelectionDetailsViewInteraction(SelectionDetailsViewInteraction interaction)
        {
            s_SelectionDetailsViewUsage.AddInteraction(interaction);
        }

        public static void AddUnityObjectsComparisonUsage(bool filterUsed, bool flattenHierarchyUsed, bool showUnchangedUsed)
        {
            s_UnityObjectsComparisonUsage.AddInteraction(UnityObjectsComparisonUsage.BuildCount);
            if (filterUsed)
                s_UnityObjectsComparisonUsage.AddInteraction(UnityObjectsComparisonUsage.FilterUsedCount);
            if (flattenHierarchyUsed)
                s_UnityObjectsComparisonUsage.AddInteraction(UnityObjectsComparisonUsage.FlattenHierarchyUsedCount);
            if (showUnchangedUsed)
                s_UnityObjectsComparisonUsage.AddInteraction(UnityObjectsComparisonUsage.ShowUnchangedUsedCount);
        }

        public static void AddAllTrackedMemoryUsage(bool filterUsed, bool showReservedUsed, AllTrackedMemoryTableMode tableModeUsed)
        {
            s_AllTrackedMemoryUsage.AddInteraction(AllTrackedMemoryUsage.BuildCount);
            if (filterUsed)
                s_AllTrackedMemoryUsage.AddInteraction(AllTrackedMemoryUsage.FilterUsedCount);
            if (showReservedUsed)
                s_AllTrackedMemoryUsage.AddInteraction(AllTrackedMemoryUsage.ShowReservedUsedCount);
            switch (tableModeUsed)
            {
                case AllTrackedMemoryTableMode.OnlyCommitted:
                    s_AllTrackedMemoryUsage.AddInteraction(AllTrackedMemoryUsage.OnlyCommittedModeUsedCount);
                    break;
                case AllTrackedMemoryTableMode.OnlyResident:
                    s_AllTrackedMemoryUsage.AddInteraction(AllTrackedMemoryUsage.OnlyResidentModeUsedCount);
                    break;
                case AllTrackedMemoryTableMode.CommittedAndResident:
                    s_AllTrackedMemoryUsage.AddInteraction(AllTrackedMemoryUsage.CommittedAndResidentModeUsedCount);
                    break;
            }
        }

        public static void AddAllTrackedMemoryComparisonUsage(bool filterUsed, bool showReservedUsed, bool showUnchangedUsed)
        {
            s_AllTrackedMemoryComparisonUsage.AddInteraction(AllTrackedMemoryComparisonUsage.BuildCount);
            if (filterUsed)
                s_AllTrackedMemoryComparisonUsage.AddInteraction(AllTrackedMemoryComparisonUsage.FilterUsedCount);
            if (showReservedUsed)
                s_AllTrackedMemoryComparisonUsage.AddInteraction(AllTrackedMemoryComparisonUsage.ShowReservedUsedCount);
            if (showUnchangedUsed)
                s_AllTrackedMemoryComparisonUsage.AddInteraction(AllTrackedMemoryComparisonUsage.ShowUnchangedUsedCount);
        }

        public static void AddMemoryMapUsage(bool filterUsed)
        {
            s_MemoryMapUsage.AddInteraction(MemoryMapUsage.BuildCount);
            if (filterUsed)
                s_MemoryMapUsage.AddInteraction(MemoryMapUsage.FilterUsedCount);
        }

        static ViewInteractionTracker<SummaryViewInteraction> s_SummaryViewInteractions = new(null);
        static ViewInteractionTracker<ReferencePanelInteraction> s_ReferencePanelUsage = new(ObjectDetailsViewController.ReferencesLabelText);
        static ViewInteractionTracker<SelectionDetailsViewInteraction> s_SelectionDetailsViewUsage = new(ObjectDetailsViewController.DetailsLabelText);
        static ViewInteractionTracker<UnityObjectsViewUsage> s_UnityObjectsViewUsage = new(TextContent.UnityObjectsViewName);
        static ViewInteractionTracker<UnityObjectsComparisonUsage> s_UnityObjectsComparisonUsage = new(TextContent.GetComparisonViewName(TextContent.UnityObjectsViewName));
        static ViewInteractionTracker<AllTrackedMemoryUsage> s_AllTrackedMemoryUsage = new(TextContent.AllOfMemoryViewName);
        static ViewInteractionTracker<AllTrackedMemoryComparisonUsage> s_AllTrackedMemoryComparisonUsage = new(TextContent.GetComparisonViewName(TextContent.AllOfMemoryViewName));
        static ViewInteractionTracker<MemoryMapUsage> s_MemoryMapUsage = new(TextContent.MemoryMapViewName);

        static void FlushInteractionEvents()
        {
            s_SummaryViewInteractions.Flush();
            s_ReferencePanelUsage.Flush();
            s_SelectionDetailsViewUsage.Flush();
            s_UnityObjectsViewUsage.Flush();
            s_UnityObjectsComparisonUsage.Flush();
            s_AllTrackedMemoryUsage.Flush();
            s_AllTrackedMemoryComparisonUsage.Flush();
            s_MemoryMapUsage.Flush();
        }
    }
}

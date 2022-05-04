using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Analytics;
using UnityEngine;
using Unity.MemoryProfiler.Editor.Extensions;
using Unity.EditorCoroutines.Editor;
using System.Linq;

namespace Unity.MemoryProfiler.Editor
{
    // the internal APIs for Analytic Events do not follow the correct naming guidelines and sometimes using odd variable names.
    // This is because their data and field names gets serialized out and send to our analytics back-end using those field names.
    // There is a fixed set of expected field names that will get processed. Please check our internal Analytics spreadsheet, linked to from the Dashboard for more info.
    internal static class MemoryProfilerAnalytics
    {
        static bool s_EnableAnalytics = false;

        static bool s_TestButDontSendAnalytics = false;

        static EditorCoroutine s_FlushAnalyticsBackgroundUpdate;
        // flush Interaction events with data in them out after 10 seconds of inactivity
        const double k_InteractionEventTimeOut = 10;
        const double k_InvalidTimeStamp = -1;
        static double s_TimestampOfLastInteractionEvent = 0;

        static Dictionary<Type, double> s_PendingEvents = new Dictionary<Type, double>();

        static Dictionary<Type, List<int>> s_MetaDataForPendingEvents = new Dictionary<Type, List<int>>();

        static Dictionary<Type, IMemoryProfilerAnalyticsEventWithMetaData> s_PendingEventsWithMetadata = new Dictionary<Type, IMemoryProfilerAnalyticsEventWithMetaData>();
        // For GC free and better performing traversal
        static List<IMemoryProfilerAnalyticsEventWithMetaData> s_PendingEventsWithMetadataList = new List<IMemoryProfilerAnalyticsEventWithMetaData>();

        static List<Filter> s_PendingFilterChanges = new List<Filter>();
        static string s_TableNameOfPendingFilterChanges = "";

        const int k_MaxEventsPerHour = 100;
        const int k_MaxEventItems = 1000;
        const string k_VendorKey = "unity.memoryprofiler";

        public interface IMemoryProfilerAnalyticsEvent
        {
            void SetTime(int ts, float duration);
        }

        public interface IMemoryProfilerAnalyticsEventWithMetaData : IMemoryProfilerAnalyticsEvent
        {
            int[] Data { set; }
            bool NeedsSending { get; }
            void Reset();
        }

        public interface IInteractionCounterEvent<TEnum> : IMemoryProfilerAnalyticsEventWithMetaData where TEnum :
#if CSHARP_7_3_OR_NEWER
            unmanaged, Enum
#else
            struct
#endif
        {
            void AddInteraction(TEnum interactionType);
        }

        [Serializable]
        public struct InteractionCount
        {
            public string interaction;
            public int count;
        }

        [Serializable]
        public abstract class InteractionCounterEvent<TEnum> : IInteractionCounterEvent<TEnum> where TEnum :
#if CSHARP_7_3_OR_NEWER
            unmanaged, Enum
#else
            struct
#endif
        {
            /// <summary>
            /// Use <see cref="count"/> instead
            /// </summary>
            [SerializeField]
            public int shown;

            public int count { set => shown = value; }

            public InteractionCount[] interactionData;
            static List<InteractionCount> s_InteractionDataBuilder = new List<InteractionCount>();

            public bool NeedsSending => shown > 0;

            int[] IMemoryProfilerAnalyticsEventWithMetaData.Data
            {
                set
                {
                    if (value == null || value.Length <= 0)
                    {
                        interactionData = null;
                        return;
                    }
                    s_InteractionDataBuilder.Clear();
                    unsafe
                    {
                        for (int i = 0; i < value.Length; i++)
                        {
                            if (value[i] != 0)
                            {
                                s_InteractionDataBuilder.Add(new InteractionCount() { interaction = EnumExtensions.ConvertToEnum<TEnum>(i).ToString(), count = value[i] });
                            }
                        }
                    }
                    interactionData = s_InteractionDataBuilder.ToArray();
                }
            }

            public void AddInteraction(TEnum interactionType)
            {
                unsafe
                {
                    var type = GetType();
                    if (!s_PendingEvents.ContainsKey(type))
                        return;
                    if (shown == 0 && s_PendingEvents[type] < 0)
                        // The event timed out before and just got restarted through a new interaction, so restart the timer
                        s_PendingEvents[type] = EditorApplication.timeSinceStartup;
                    var value = EnumExtensions.GetValue(interactionType);
                    var list = s_MetaDataForPendingEvents[GetType()];
                    while (list.Count <= value)
                        list.Add(0);
                    ++list[(int)value];
                    ++shown;
                    s_TimestampOfLastInteractionEvent = EditorApplication.timeSinceStartup;
                }
            }

            public abstract void SetTime(int ts, float duration);
            public void Reset()
            {
                interactionData = null;
                shown = 0;
            }
        }

        // Adding new items is fine but ideally no renaming of these or the analytics backend would need loose version over version consistency
        internal enum PageInteractionType
        {
            Uncategorized,
            SelectedTotalCommittedMemoryBarElement,
            SelectedTotalMemoryBarElement,
            SelectedManagedMemoryBarElement,
            SelectedObjectVsAllocationMemoryBarElement, // Technically not used yet as that bar is not actually visible yet
            MemoryUsageBarNormalizeToggledOn,
            MemoryUsageBarNormalizeToggledOff,
            MemoryUsageWasHidden,
            MemoryUsageWasRevealed,
            AllTopIssuesWasHidden,
            AllTopIssuesWasRevealed,
            ATopIssueWasHidden,
            ATopIssueWasRevealed,
            ATopIssueInvestigateButtonWasClicked,
            ATopIssueDocumentationButtonWasClicked,
            DocumentationOpened,
            SearchInPageWasUsed,
            SelectionInTableWasUsed,
            TreeViewElementWasExpanded,
            TableSortingWasChanged,
            TreeViewWasFlattened,
            ColumnWasHidden,
            ColumnWasRevealed,
            DetailsSidePanelWasHidden,
            DetailsSidePanelWasRevealed,
            SnapshotListSidePanelWasHidden,
            SnapshotListSidePanelWasRevealed,
            TreeViewWasUnflattened,
            DuplicateFilterWasApplied,
            DuplicateFilterWasRemoved,
        }

        [Serializable]
        internal class InteractionsInPage : InteractionCounterEvent<PageInteractionType>
        {
            public override void SetTime(int ts, float duration)
            {
                this.ts = ts;
                this.duration = duration;
                subtype = nameof(InteractionsInPage);
            }

            public string subtype;
            public int ts;
            public float duration;
            public string viewName;
        }

        // Adding new items is fine but ideally no renaming of these or the analytics backend would need loose version over version consistency
        internal enum ReferencePanelInteractionType
        {
            Uncategorized,
            ReferencePanelWasHidden,
            ReferencePanelWasRevealed,
            SelectionInTableWasUsed,
            TreeViewElementWasExpanded,
            TableSortingWasChanged,
            TypeNameTruncationWasEnabled,
            TypeNameTruncationWasDisabled,
            SelectionInTableWasCleared,
        }

        [Serializable]
        internal class InteractionsInReferencesPanel : InteractionCounterEvent<ReferencePanelInteractionType>
        {
            public override void SetTime(int ts, float duration)
            {
                this.ts = ts;
                this.duration = duration;
                subtype = nameof(InteractionsInReferencesPanel);
            }

            public string subtype;
            public int ts;
            public float duration;
            /// <summary>
            /// RawReferences, ReferencedFrom, ReferencingTo, PathFromRoot, PathToRoot
            /// </summary>
            public string viewName;
        }

        // Adding new items is fine but ideally no renaming of these or the analytics backend would need loose version over version consistency
        internal enum SelectionDetailsPanelInteractionType
        {
            Uncategorized,
            SelectionDetailsPanelWasHidden,
            SelectionDetailsPanelWasRevealed,
            SelectionInManagedObjectTableWasUsed,
            ManagedObjectTreeViewElementWasRevealed,
            ManagedObjectTableSortingWasChanged,
            ManagedObjectTypeNameTruncationWasEnabled,
            ManagedObjectTypeNameTruncationWasDisabled,
            ManagedObjectRecursiveInNotesWasClicked,
            ManagedObjectDuplicateInNotesWasClicked,
            ManagedObjectShowMoreLinkWasClicked,
            SelectSceneObjectInEditorButtonClicked,
            SelectAssetInEditorButtonClicked,
            SearchInSceneButtonClicked,
            SearchInProjectButtonClicked,
            QuickSearchForSceneObjectButtonClicked,
            QuickSearchForAssetButtonClicked,
            DetailsSelectableLabelWasSelected,
            BasicSectionWasRevealed,
            BasicSectionWasHidden,
            HelpSectionWasRevealed,
            HelpSectionWasHidden,
            AdvancedSectionWasRevealed,
            AdvancedSectionWasHidden,
            PreviewSectionWasRevealed,
            PreviewSectionWasHidden ,
            ManagedObjectInspectorSectionWasRevealed,
            ManagedObjectInspectorSectionWasHidden,
            OtherSectionWasRevealed,
            OtherSectionWasHidden,
            DebugSectionWasRevealed,
            DebugSectionWasHidden,
            CopiedFullTitleFromContextMenu,
            CopiedNativeObjectNameFromContextMenu,
            CopiedManagedTypeNameFromContextMenu,
            CopiedNativeTypeNameFromContextMenu,
            CopiedFullTitleViaButton,
            CopiedNativeObjectNameViaButton,
            CopiedManagedTypeNameViaButton,
            CopiedNativeTypeNameViaButton,
        }

        [Serializable]
        internal class InteractionsInSelectionDetailsPanel : InteractionCounterEvent<SelectionDetailsPanelInteractionType>
        {
            public override void SetTime(int ts, float duration)
            {
                this.ts = ts;
                this.duration = duration;
                subtype = nameof(InteractionsInSelectionDetailsPanel);
            }

            public string subtype;
            public int ts;
            public float duration;
            /// <summary>
            /// Use <see cref="selectedElementType"/> instead.
            /// </summary>
            public string viewName;
            public string selectedElementType { set => viewName = value; }
            /// <summary>
            /// Use <see cref="selectedElementStatus"/> instead.
            /// </summary>
            public string fileName;
            public string selectedElementStatus { set => fileName = value; }
        }

        [Serializable]
        internal struct CapturedSnapshotEvent : IMemoryProfilerAnalyticsEvent
        {
            public void SetTime(int ts, float duration)
            {
                this.ts = ts;
                this.duration = duration;
                subtype = "captureSnapshot";
            }

            public string subtype;
            public int ts;
            public float duration;
            public bool success;
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

        internal static SnapshotProjectAndUnityVersionDetails GetSnapshotProjectAndUnityVersionDetails(SnapshotFileData snapshot)
        {
            var snapshotDetails = MemoryProfilerAnalytics.SnapshotProjectAndUnityVersionDetails.None;
            if (snapshot.GuiData.SessionId == EditorAssetFinderUtility.CurrentSessionId)
                snapshotDetails |= MemoryProfilerAnalytics.SnapshotProjectAndUnityVersionDetails.SameSession;
            if (snapshot.GuiData.ProductName == Application.productName)
                snapshotDetails |= MemoryProfilerAnalytics.SnapshotProjectAndUnityVersionDetails.SameProject;
            if (snapshot.GuiData.UnityVersion == Application.unityVersion)
                snapshotDetails |= MemoryProfilerAnalytics.SnapshotProjectAndUnityVersionDetails.SameUnityVersion;
            else
            {
                if (string.Compare(snapshot.GuiData.UnityVersion, Application.unityVersion) > 0)
                    snapshotDetails  |= MemoryProfilerAnalytics.SnapshotProjectAndUnityVersionDetails.SnapshotFromNewerUnityVersion;
                else
                    snapshotDetails  |= MemoryProfilerAnalytics.SnapshotProjectAndUnityVersionDetails.SnapshotFromOlderUnityVersion;
                var snapshotVersion = snapshot.GuiData.UnityVersion.Split('.');
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
            if (PlatformsHelper.RuntimePlatformIsEditorPlatform(snapshot.GuiData.RuntimePlatform))
            {
                snapshotDetails |= SnapshotProjectAndUnityVersionDetails.EditorSnapshot;
                snapshotDetails |= SnapshotProjectAndUnityVersionDetails.SamePlatformAsEditor;
            }
            else
            {
                if (PlatformsHelper.SameRuntimePlatformAsEditorPlatform(snapshot.GuiData.RuntimePlatform))
                    snapshotDetails |= SnapshotProjectAndUnityVersionDetails.SamePlatformAsEditor;
            }
            return snapshotDetails;
        }

        [Serializable]
        internal struct LoadedSnapshotEvent : IMemoryProfilerAnalyticsEvent
        {
            public void SetTime(int ts, float duration)
            {
                this.ts = ts;
                this.duration = duration;
                subtype = "loadSnapshot";
            }

            public string subtype;
            public int ts;
            public float duration;
            /// <summary>
            /// Use <see cref="SnapshotFromSameProjectAsOpen"/> instead
            /// </summary>
            public bool success;
            public bool SnapshotFromSameProjectAsOpen { set => success = value;}
            /// <summary>
            /// Use <see cref="openSnapshotDetails"/> instead.
            /// </summary>
            public int shown;
            public SnapshotProjectAndUnityVersionDetails openSnapshotDetails { set => shown = (int)value;}
            /// <summary>
            /// Use <see cref="runtimePlatform"/> instead.
            /// </summary>
            public int show;
            public RuntimePlatform runtimePlatform { set => show = (int)value; }
            /// <summary>
            /// use <see cref="unityVersionOfSnapshot"/> instead
            /// </summary>
            public string fileName;
            public string unityVersionOfSnapshot { set => fileName = value; }
        }
        [Serializable]
        internal struct ImportedSnapshotEvent : IMemoryProfilerAnalyticsEvent
        {
            public void SetTime(int ts, float duration)
            {
                this.ts = ts;
                this.duration = duration;
                subtype = "importSnapshot";
            }

            public string subtype;
            public int ts;
            public float duration;
            /// <summary>
            /// use <see cref="fileExtensionOrVersionOfImportedSnapshot"/> instead
            /// </summary>
            public string fileName;
            public string fileExtensionOrVersionOfImportedSnapshot { set => fileName = value; }
        }

        [Serializable]
        internal struct SortedColumnEvent : IMemoryProfilerAnalyticsEvent
        {
            public void SetTime(int ts, float duration)
            {
                this.ts = ts;
                this.duration = duration;
                subtype = "columnSorted";
            }

            public string subtype;
            public int ts;
            public float duration;
            public string viewName;
            /// <summary>
            /// Use <see cref="Ascending"/> instead
            /// </summary>
            public bool success;
            public bool Ascending { set => success = value; }
            /// <summary>
            /// Column
            /// </summary>
            public int shown;
            /// <summary>
            /// columnName
            /// </summary>
            public string fileName;
        }

        [Serializable]
        internal struct ColumnVisibilityChangedEvent : IMemoryProfilerAnalyticsEvent
        {
            public void SetTime(int ts, float duration)
            {
                this.ts = ts;
                this.duration = duration;
                subtype = "columnVisibilityChanged";
            }

            public string subtype;
            public int ts;
            public float duration;
            /// <summary>
            /// viewName == File extension or version of imported snapshot
            /// </summary>
            public string viewName;
            /// <summary>
            /// Use <see cref="shownOrHidden"/>
            /// </summary>
            public bool success;
            /// <summary>
            /// true == shown, false == hidden
            /// </summary>
            public bool shownOrHidden { set => success = value; }
            /// <summary>
            /// Use <see cref="columnIndex"/>.
            /// </summary>
            public int shown;
            public int columnIndex { set => shown = value; }
            /// <summary>
            /// Use <see cref="columnName"/>
            /// </summary>
            public string fileName;
            public string columnName { set => fileName = value; }
        }

        [Serializable]
        internal struct DiffedSnapshotEvent : IMemoryProfilerAnalyticsEvent
        {
            public void SetTime(int ts, float duration)
            {
                this.ts = ts;
                this.duration = duration;
                subtype = "diffSnapshot";
            }

            public string subtype;
            public int ts;
            public float duration;
            public bool success;
            public bool sameSessionDiff {set => success = value; }
            /// <summary>
            /// Use <see cref="captureInfoA"/>
            /// </summary>
            public int shown;
            public SnapshotProjectAndUnityVersionDetails captureInfoA { set => shown = (int)value; }
            /// <summary>
            /// Use <see cref="captureInfoB"/>
            /// </summary>
            public int show;
            public SnapshotProjectAndUnityVersionDetails captureInfoB { set => show = (int)value; }
        }

        [Serializable]
        internal struct DiffToggledEvent : IMemoryProfilerAnalyticsEvent
        {
            public void SetTime(int ts, float duration)
            {
                this.ts = ts;
                this.duration = duration;
                subtype = "diffToggle";
            }

            public string subtype;
            public int ts;
            public float duration;
            public enum ShowSnapshot
            {
                Both, // 0
                First, // 1
                Second, // 2
            }
            public int shown;
            public int show;
            public string viewName;
        }

        internal enum ViewLevel
        {
            Page, // 0
            ViewInPage , // 1
            ViewInSidePanel, // 2
        }

        // needs to be a different type than OpenedViewEvent so that the sub view event within the page can start nested within the event of the page opening
        [Serializable]
        public struct OpenedPageEvent : IMemoryProfilerAnalyticsEvent
        {
            public void SetTime(int ts, float duration)
            {
                this.ts = ts;
                this.duration = duration;
                subtype = "openView";
                viewLevel = ViewLevel.Page;
            }

            public string subtype;
            public int ts;
            public float duration;
            /// <summary>
            /// This is automatically set to <see cref="ViewLevel.Page"/> for all <see cref="OpenedPageEvent"/>s
            /// by <see cref="SetTime(int, float)"/> as the event ends. No need to set this manually
            /// </summary>
            public ViewLevel viewLevel { set {shown = (int)value; } }
            /// <summary>
            /// Use <see cref="viewLevel"/> instead.
            /// </summary>
            public int shown;
            public string viewName;
        }

        [Serializable]
        public struct OpenedViewEvent : IMemoryProfilerAnalyticsEvent
        {
            public void SetTime(int ts, float duration)
            {
                this.ts = ts;
                this.duration = duration;
                subtype = "openView";
                viewLevel = ViewLevel.ViewInPage;
            }

            public string subtype;
            public int ts;
            public float duration;
            /// <summary>
            /// This is automatically set to <see cref="ViewLevel.ViewInPage"/> for all <see cref="OpenedViewEvent"/>s
            /// by <see cref="SetTime(int, float)"/> as the event ends. No need to set this manually
            /// </summary>
            public ViewLevel viewLevel { set {shown = (int)value; } }
            /// <summary>
            /// Use <see cref="viewLevel"/> instead.
            /// </summary>
            public int shown;
            public string viewName;
        }

        [Serializable]
        public struct OpenedViewInSidePanelEvent : IMemoryProfilerAnalyticsEvent
        {
            public void SetTime(int ts, float duration)
            {
                this.ts = ts;
                this.duration = duration;
                subtype = "openView";
                viewLevel = ViewLevel.ViewInSidePanel;
            }

            public string subtype;
            public int ts;
            public float duration;
            /// <summary>
            /// This is automatically set to <see cref="ViewLevel.ViewInSidePanel"/> for all <see cref="OpenedViewInSidePanelEvent"/>s
            /// by <see cref="SetTime(int, float)"/> as the event ends. No need to set this manually
            /// </summary>
            public ViewLevel viewLevel { set {shown = (int)value; } }
            /// <summary>
            /// Use <see cref="viewLevel"/> instead.
            /// </summary>
            public int shown;
            public string viewName;
        }


        [Serializable]
        public struct GenerateViewEvent : IMemoryProfilerAnalyticsEvent
        {
            public void SetTime(int ts, float duration)
            {
                this.ts = ts;
                this.duration = duration;
                subtype = "generateView";
            }

            public string subtype;
            public int ts;
            public float duration;
            public string viewName;
        }

        [Serializable]
        public struct Filter
        {
            public string filterName;
            public string column;
            public int key;
        }

        [Serializable]
        public struct TableFilteredEvent : IMemoryProfilerAnalyticsEvent
        {
            public void SetTime(int ts, float duration)
            {
                this.ts = ts;
                this.duration = duration;
                subtype = "filterTable";
            }

            public string subtype;
            public int ts;
            public float duration;
            public string viewName;
            public Filter filter;
        }

        const string k_EventTopicName = "memoryProfiler";

        public static void EnableAnalytics(EditorWindow parentEditorWindow)
        {
            s_EnableAnalytics = true;
            EditorAnalytics.RegisterEventWithLimit(k_EventTopicName, k_MaxEventsPerHour, k_MaxEventItems, k_VendorKey);
            s_FlushAnalyticsBackgroundUpdate = EditorCoroutineUtility.StartCoroutine(FlushPendingEvents(), parentEditorWindow);

            Application.quitting += OnApplicationQuitting;
        }

        static void OnApplicationQuitting()
        {
            // No Sending of events during shutdown
            s_EnableAnalytics = false;
        }

        public static void DisableAnalytics()
        {
            s_EnableAnalytics = false;
            EditorCoroutineUtility.StopCoroutine(s_FlushAnalyticsBackgroundUpdate);
            s_FlushAnalyticsBackgroundUpdate = null;
            Application.quitting -= OnApplicationQuitting;
        }

        static IEnumerator FlushPendingEvents()
        {
            while (s_EnableAnalytics)
            {
                if (s_TimestampOfLastInteractionEvent >= 0 && (EditorApplication.timeSinceStartup - s_TimestampOfLastInteractionEvent > k_InteractionEventTimeOut))
                {
                    s_TimestampOfLastInteractionEvent = EditorApplication.timeSinceStartup;
                    for (int i = 0; i < s_PendingEventsWithMetadataList.Count; i++)
                    {
                        var evt = s_PendingEventsWithMetadataList[i];
                        var type = evt.GetType();
                        var eventStartTime = s_PendingEvents[type];
                        if (eventStartTime == k_InvalidTimeStamp || EditorApplication.timeSinceStartup - eventStartTime < k_InteractionEventTimeOut || !evt.NeedsSending)
                        {
                            if (!evt.NeedsSending) // no data to send, just timed out? pause the counter.
                                s_PendingEvents[type] = k_InvalidTimeStamp;
                            continue;
                        }
                        FlushEventWithMetadata(evt, false);
                        // invalidate the initial start time, it'll be restarted proper on the first interaction
                        s_PendingEvents[type] = k_InvalidTimeStamp;
                        evt.Reset();
                    }
                }

                yield return null;
            }
        }

        public static void SendEvent<T>(T eventData) where T : IMemoryProfilerAnalyticsEvent
        {
            if (s_EnableAnalytics && !s_TestButDontSendAnalytics)
                EditorAnalytics.SendEventWithLimit(k_EventTopicName, eventData);
            if (s_TestButDontSendAnalytics)
                Debug.Log($"Memory Profiler Analytics Event test: {JsonUtility.ToJson(eventData)}");
        }

        public static void StartEvent<T>() where T : IMemoryProfilerAnalyticsEvent
        {
            if (s_EnableAnalytics)
                s_PendingEvents[typeof(T)] = EditorApplication.timeSinceStartup;
        }

        public static void CancelEvent<T>() where T : IMemoryProfilerAnalyticsEvent
        {
            if (s_EnableAnalytics && s_PendingEvents.ContainsKey(typeof(T)))
            {
                s_PendingEvents[typeof(T)] = k_InvalidTimeStamp;
            }
        }

        public static void EndEvent<T>(T eventData) where T : IMemoryProfilerAnalyticsEvent
        {
            if (s_EnableAnalytics)
            {
                var type = eventData.GetType();
                if (s_PendingEvents.ContainsKey(type) && s_PendingEvents[type] >= 0)
                {
                    int unixTimestamp = (int)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                    eventData.SetTime(unixTimestamp, (float)(EditorApplication.timeSinceStartup - s_PendingEvents[type]));
                    s_PendingEvents[type] = k_InvalidTimeStamp;
                    SendEvent(eventData);
                }
#if DEBUG_VALIDATION
                // Interaction Events may be end before they are started. This is because they are wrapped around a view open and closing
                // and we might close a view that was never used.
                else if (!(type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IInteractionCounterEvent<>))))
                {
                    Debug.LogError("Ending analytics event before it even started: " + eventData);
                }
#endif
            }
        }

        public static void StartEventWithMetaData<T>(IMemoryProfilerAnalyticsEventWithMetaData metaDataEvent) where T : class, IMemoryProfilerAnalyticsEventWithMetaData
        {
            if (s_EnableAnalytics)
            {
                StartEvent<T>();
                var eventType = typeof(T);
                if (s_MetaDataForPendingEvents.ContainsKey(eventType))
                {
                    s_MetaDataForPendingEvents[eventType].Clear();
                    for (int i = 0; i < s_PendingEventsWithMetadataList.Count; i++)
                    {
                        if (s_PendingEventsWithMetadataList[i].GetType() == eventType)
                        {
                            if (s_PendingEventsWithMetadataList[i].NeedsSending)
                                FlushEventWithMetadata(s_PendingEventsWithMetadataList[i], true);
                            else
                                s_PendingEventsWithMetadataList.RemoveAt(i);
                            break;
                        }
                    }
                }
                else
                {
                    s_MetaDataForPendingEvents[eventType] = new List<int>();
                }
                s_PendingEventsWithMetadata[eventType] = metaDataEvent;
                s_PendingEventsWithMetadataList.Add(metaDataEvent);
                s_TimestampOfLastInteractionEvent = EditorApplication.timeSinceStartup;
            }
        }

        // This variation exists in cases where we do need to modify the event before ending it with information we don't have at the start of the event
        //public static void EndEventWithMetadata<T>(T eventData) where T : class, IMemoryProfilerAnalyticsEventWithMetaData
        //{
        //    if (s_EnableAnalytics)
        //    {
        //        if (s_EnableAnalytics && s_PendingEvents.ContainsKey(typeof(T)) && s_PendingEvents[typeof(T)] >= 0)
        //        {
        //            eventData.Data = s_MetaDataForPendingEvents[typeof(T)].ToArray();
        //            s_MetaDataForPendingEvents[typeof(T)].Clear();
        //        }
        //        EndEvent<T>(eventData);
        //        s_PendingEventsWithMetadata[typeof(T)] = default;
        //    }
        //}

        public static void EndEventWithMetadata<T>() where T : class, IMemoryProfilerAnalyticsEventWithMetaData
        {
            var eventType = typeof(T);
            if (s_EnableAnalytics && s_PendingEventsWithMetadata.ContainsKey(eventType))
            {
                var eventData = (T)s_PendingEventsWithMetadata[eventType];
                // Only send an event if there is anything to report.
                // We could send all on Ending these but that might be too spammy as it would happen for every selection
                if (eventData.NeedsSending)
                    FlushEventWithMetadata(eventData, true);
                else
                {
                    s_PendingEventsWithMetadata.Remove(eventType);
                    s_PendingEvents.Remove(eventType);
                    s_PendingEventsWithMetadataList.Remove(eventData);
                }
            }
        }

        static void FlushEventWithMetadata(IMemoryProfilerAnalyticsEventWithMetaData evt, bool removeEvent = false)
        {
            var type = evt.GetType();
            if (s_EnableAnalytics && s_PendingEventsWithMetadata.ContainsKey(type))
            {
                var eventData = s_PendingEventsWithMetadata[type];
                if (s_EnableAnalytics && s_PendingEvents.ContainsKey(type) && s_PendingEvents[type] >= 0)
                {
                    eventData.Data = s_MetaDataForPendingEvents[type].ToArray();
                    if (removeEvent)
                    {
                        for (int i = 0; i < s_PendingEventsWithMetadataList.Count; i++)
                        {
                            if (s_PendingEventsWithMetadataList[i] == eventData)
                            {
                                s_PendingEventsWithMetadataList.RemoveAt(i);
                                break;
                            }
                        }
                    }
                    EndEvent(eventData);
                    s_MetaDataForPendingEvents[type].Clear();
                    // if there are no more pending metadata events, set the time-stamp to 0
                    if (s_PendingEventsWithMetadataList.Count == 0)
                        s_TimestampOfLastInteractionEvent = k_InvalidTimeStamp;
                }
            }
        }

        public static void AddMetaDataToEvent<T>(byte data) where T : class, IMemoryProfilerAnalyticsEventWithMetaData
        {
            if (s_EnableAnalytics && s_PendingEvents.ContainsKey(typeof(T)) && s_PendingEvents[typeof(T)] >= 0)
                s_MetaDataForPendingEvents[typeof(T)].Add(data);
        }

        public static void AddInteractionCountToEvent<TEvent, TEnum>(TEnum eventType) where TEvent : IInteractionCounterEvent<TEnum> where TEnum :
#if CSHARP_7_3_OR_NEWER
        unmanaged, Enum
#else
        struct
#endif
        {
            var type = typeof(TEvent);
            if (s_EnableAnalytics && s_PendingEvents.ContainsKey(type))
            {
                var e = (s_PendingEventsWithMetadata[typeof(TEvent)] as IInteractionCounterEvent<TEnum>);
                if (e != null)
                    e.AddInteraction(eventType);
#if DEBUG_VALIDATION
                else
                    Debug.LogError($"Event of type {typeof(TEvent)} is not pending and therefore can't collect an interaction event of type {typeof(TEnum)} {eventType}");
#endif
            }
        }

        public static void FiltersChanged(string tableName, List<Filter> filters)
        {
            if (s_EnableAnalytics)
            {
                bool changesOccured = false;
                if (s_PendingFilterChanges.Count == filters.Count)
                {
                    for (int i = 0; i < filters.Count; i++)
                    {
                        changesOccured = s_PendingFilterChanges[i].column != filters[i].column || s_PendingFilterChanges[i].filterName != filters[i].filterName;
                        if (changesOccured)
                            break;
                    }
                }
                else
                {
                    changesOccured = true;
                }
                if (!changesOccured)
                    return;

                if (!s_PendingEvents.ContainsKey(typeof(TableFilteredEvent)) || s_PendingEvents[typeof(TableFilteredEvent)] < 0)
                {
                    StartEvent<TableFilteredEvent>();
                }
                s_TableNameOfPendingFilterChanges = tableName;
                s_PendingFilterChanges.Clear();

                foreach (var item in filters)
                {
                    s_PendingFilterChanges.Add(item);
                }
            }
        }

        public static void SendPendingFilterChanges()
        {
            //TODO: Send off 20seconds after the last change
            if (s_PendingFilterChanges.Count > 0)
            {
                if (s_PendingEvents.ContainsKey(typeof(TableFilteredEvent)) && s_PendingEvents[typeof(TableFilteredEvent)] >= 0)
                {
                    int unixTimestamp = (int)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                    foreach (var filter in s_PendingFilterChanges)
                    {
                        var filterToSend = filter;
                        filterToSend.key = unixTimestamp;
                        var eventData = new TableFilteredEvent() { viewName = s_TableNameOfPendingFilterChanges, filter = filterToSend };
                        eventData.SetTime(unixTimestamp, (float)(EditorApplication.timeSinceStartup - s_PendingEvents[typeof(TableFilteredEvent)]));
                        SendEvent(eventData);
                    }
                    s_PendingEvents[typeof(TableFilteredEvent)] = k_InvalidTimeStamp;
                }
                //TODO substract the time waited until sending from the time spend filtering
                s_PendingFilterChanges.Clear();
                s_TableNameOfPendingFilterChanges = "";
            }
        }
    }
}

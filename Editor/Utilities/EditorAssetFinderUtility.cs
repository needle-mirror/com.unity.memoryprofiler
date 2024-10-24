using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Unity.MemoryProfiler.Editor.UI;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEditor.Search;
using UnityEngine;
using static Unity.MemoryProfiler.Editor.QuickSearchUtility;
using Object = UnityEngine.Object;

namespace Unity.MemoryProfiler.Editor
{
    /// <summary>
    /// Split out so that <see cref="MemoryProfilerAnalytics"/> doesn't accidentally trigger search
    /// initialization by triggering the static initializer for <see cref="EditorAssetFinderUtility"/>.
    /// </summary>
    internal static class EditorSessionUtility
    {
        static MethodInfo s_GetLocalGuid = null;
        public static bool InstanceIdPingingSupportedByUnityVersion => s_GetLocalGuid != null;
        public static uint CurrentSessionId => InstanceIdPingingSupportedByUnityVersion ? (uint)s_GetLocalGuid.Invoke(null, null) : uint.MaxValue;
        static EditorSessionUtility()
        {
            var editorAssembly = typeof(EditorGUIUtility).Assembly;
            s_GetLocalGuid = editorAssembly.GetType("UnityEditor.EditorConnectionInternal").GetMethod("GetLocalGuid");
        }
    }

    /// <summary>
    /// Split out so that the Test Project can doesn't accidentally trigger async search
    /// by triggering the static initializer for <see cref="EditorAssetFinderUtility"/>.
    /// </summary>
    internal static class QuickSearchUtility
    {
        public static bool QuickSearchCanUseNameFilter { get; private set; }
        public const string SearchProviderIdScene = "scene";
        public const string SearchProviderIdAsset = "asset";
        public const string SearchProviderIdAssetDatabase = "adb";
        public static readonly string[] AssetSearchProviders = new string[] { SearchProviderIdAsset, SearchProviderIdAssetDatabase };
        public static readonly string[] SceneObjectSearchProviders = new string[] { SearchProviderIdScene };

        const int k_TimeIntervalToCheckAsyncSearchMS = 100;
        // Quick Search has a timeout of 10 seconds. There is no way to get the timeout value from the API so we hardcode it here.
        const int k_SearchSessionTimeoutInMS = 10000;
        // Give ourselves a bit of a buffer so we can timeout before Quick Search does to avoid it logging a timeout error.
        const int k_SearchTimoutInSeconds = (k_SearchSessionTimeoutInMS - 2 * k_TimeIntervalToCheckAsyncSearchMS) / 1000;
        const int k_FailedSearchRemovalDelayInMS = 5000;

        // Quick Search only needs initializing once per session, not after every domain reload
        static bool QuickSearchInitialized
        {
            get => SessionState.GetBool("com.unity.memoryprofiler.quicksearch.initialized", false);
            set => SessionState.SetBool("com.unity.memoryprofiler.quicksearch.initialized", value);
        }

        public static void InitializeQuickSearch(bool async = true)
        {
            if (QuickSearchInitialized)
                return;
            // Initialize quick search
            var context = SearchService.CreateContext(providerIds: new[] { SearchProviderIdScene, SearchProviderIdAsset, SearchProviderIdAssetDatabase }, searchText: $"t:{nameof(MemoryProfilerWindow)}");
            if (async)
            {
                // Initialize Async, preferred path, should be triggered by e.g. opening the Memory Profiler window
                var searchTask = InitializeAsync(context);
                searchTask.ContinueWith((t) => t.Result.Dispose());
            }
            else
            {
                // Initialize Synchronously should ideally only fire for search tests during their initialization
                using var search = SearchService.Request(context, SearchFlags.Synchronous);
                QuickSearchInitialized = true;
                context.Dispose();
            }
        }

        static async Task<AsyncSearchHelper> InitializeAsync(SearchContext context)
        {
            var asyncSearchHelper = new AsyncSearchHelper(context, "Memory Profiler/Initializing Quick Search");
            var search = await asyncSearchHelper.RequestSearchAndAwaitResults();

            if (asyncSearchHelper.State == AsyncSearchHelper.SearchState.FinishedSuccessfully)
                QuickSearchInitialized = true;
            return asyncSearchHelper;
        }

        public class AsyncSearchHelper : IDisposable
        {
            public enum SearchState
            {
                NotYetStarted,
                InProgress,
                Canceled,
                TimedOut,
                FinishedSuccessfully
            }
            public SearchState State { get; private set; } = SearchState.NotYetStarted;
            SearchContext m_SearchContext;
            ISearchList m_SearchList;
            string m_ProgressTitle;
            int m_ProgressId;
            bool m_Disposed = false;

            /// <summary>
            /// Construct the helper before you start the search, then call <see cref="AwaitSearchResult(ISearchList)"/> once you started the search.
            /// </summary>
            /// <param name="context"></param>
            public AsyncSearchHelper(SearchContext context, string progressTitle)
            {
                m_SearchContext = context;
                m_ProgressTitle = progressTitle;
                m_ProgressId = -1;
                if (context.searchInProgress)
                    State = SearchState.InProgress;
                else
                    context.sessionStarted += AsyncSessionStarted;
                context.sessionEnded += AsyncSessionFinished;
            }

            public async Task<ISearchList> RequestSearchAndAwaitResults(CancellationToken cancellationToken = default)
            {
                m_SearchList = SearchService.Request(m_SearchContext);
                if (State == SearchState.FinishedSuccessfully)
                    return m_SearchList;

                m_ProgressId = Progress.Start(m_ProgressTitle);

                if (cancellationToken.CanBeCanceled)
                    cancellationToken.Register(Cancel);
                // Cancelation via the Background Tasks window is still possible
                Progress.RegisterCancelCallback(m_ProgressId, CancelIfCancelable);

                var startTime = EditorApplication.timeSinceStartup;
                Progress.SetRemainingTime(m_ProgressId, k_SearchTimoutInSeconds);
                Progress.SetTimeDisplayMode(m_ProgressId, Progress.TimeDisplayMode.ShowRemainingTime);
                while (State <= SearchState.InProgress)
                {
                    var runningTime = EditorApplication.timeSinceStartup - startTime;
                    if (SearchDatabaseIsReady())
                    {
                        if (startTime > 0)
                        {
                            Progress.Report(m_ProgressId, (float)runningTime / k_SearchTimoutInSeconds);
                            Progress.SetRemainingTime(m_ProgressId, k_SearchTimoutInSeconds - (int)runningTime);
                            if (runningTime > k_SearchTimoutInSeconds)
                            {
                                //stop the "timout"
                                m_SearchContext.progressId = m_ProgressId;
                                // switch from counting down towards a timeout and instead do an infinite spinning wheel
                                Progress.UnregisterCancelCallback(m_ProgressId);
                                Progress.Remove(m_ProgressId);
                                m_ProgressId = Progress.Start(m_ProgressTitle, options: Progress.Options.Managed | Progress.Options.Indefinite);
                                Progress.RegisterCancelCallback(m_ProgressId, CancelIfCancelable);
                                Progress.SetTimeDisplayMode(m_ProgressId, Progress.TimeDisplayMode.ShowRunningTime);
                                startTime = -1;
                            }
                        }
                        else
                            Progress.Report(m_ProgressId, 0);
                    }
                    else
                    {
                        Progress.Report(m_ProgressId, (float)runningTime / k_SearchTimoutInSeconds);
                        Progress.SetRemainingTime(m_ProgressId, k_SearchTimoutInSeconds - (int)runningTime);
                        // This timeout is here to work around UUM-81554. Once that is fixed, the timeout can be removed again
                        if (runningTime > k_SearchTimoutInSeconds)
                        {
                            AbortSearch();
                            Progress.Finish(m_ProgressId, Progress.Status.Failed);
                            State = SearchState.TimedOut;
                            _ = Task.Factory.StartNew(RemoveProgressAfterTimeout);
                            return m_SearchList;
                        }
                    }
                    await Task.Delay(k_TimeIntervalToCheckAsyncSearchMS);
                    // there is a short periode after requesting a search where it is not yet pending.
                    // The time interval should be enough to get past that and to exit the loop if search somehow finished instantly
                    // and AsyncSessionFinished was never called.
                    if (!m_SearchList.pending && !m_SearchContext.searchInProgress)
                        break;
                }
                if (State == SearchState.Canceled)
                {
                    Progress.Finish(m_ProgressId, Progress.Status.Canceled);
                }
                else
                {
                    Progress.Finish(m_ProgressId, Progress.Status.Succeeded);
                    State = SearchState.FinishedSuccessfully;
                }
                Progress.Remove(m_ProgressId);
                m_ProgressId = -1;
                return m_SearchList;
            }

            async Task RemoveProgressAfterTimeout()
            {
                await Task.Delay(k_FailedSearchRemovalDelayInMS);
                if (m_ProgressId >= 0)
                {
                    Progress.Remove(m_ProgressId);
                    m_ProgressId = -1;
                }
            }

            bool CancelIfCancelable()
            {
                if (State <= SearchState.InProgress)
                {
                    Cancel();
                    return true;
                }
                return false;
            }

            public void Cancel()
            {
                State = SearchState.Canceled;
                AbortSearch();
            }

            void AbortSearch()
            {
                m_SearchList?.Dispose();
                m_SearchContext.Dispose();
            }

            // Careful! Both of these are called PER PROVIDER!
            // Given that we might use more than one, they need to safeguard against double calls
            // and against assuming we're done when only the first provider is done searching.
            void AsyncSessionStarted(SearchContext context)
            {
                if (State == SearchState.NotYetStarted)
                    State = SearchState.InProgress;
            }
            void AsyncSessionFinished(SearchContext context)
            {
                if (State <= SearchState.InProgress && !context.searchInProgress && !m_SearchList.pending)
                    State = SearchState.FinishedSuccessfully;
            }

            public void Dispose()
            {
                CancelIfCancelable();
                if (!m_Disposed)
                {
                    m_Disposed = true;
                    AbortSearch();
                    if (m_ProgressId >= 0 && Progress.Exists(m_ProgressId))
                    {
                        Progress.UnregisterCancelCallback(m_ProgressId);
                    }
                }
            }
        }

        static MethodInfo s_SearchDatabase_Enumerate;
        static PropertyInfo s_SearchDatabase_Ready;
        static PropertyInfo s_SearchDatabase_Updating;
        static FieldInfo s_SearchDatabase_Settings;
        static FieldInfo s_SearchDatabase_Settings_Options;
        static FieldInfo s_SearchDatabase_Options_Disabled;
        static FieldInfo s_SearchDatabase_Options_Types;
        static QuickSearchUtility()
        {
            var searchDatabase = typeof(ISearchView).Assembly.GetType("UnityEditor.Search.SearchDatabase");
            if (searchDatabase == null)
                return;
            s_SearchDatabase_Enumerate = searchDatabase.GetMethod("EnumerateAll", BindingFlags.Static | BindingFlags.Public);
            s_SearchDatabase_Ready = searchDatabase.GetProperty("ready", BindingFlags.Instance | BindingFlags.Public);
            s_SearchDatabase_Updating = searchDatabase.GetProperty("updating", BindingFlags.Instance | BindingFlags.Public);
            var searchDatabase_Settings = searchDatabase.GetNestedType("Settings");
            if (searchDatabase_Settings == null)
                return;
            s_SearchDatabase_Settings = searchDatabase.GetField("settings", BindingFlags.Instance | BindingFlags.Public);
            s_SearchDatabase_Settings_Options = searchDatabase_Settings.GetField("options", BindingFlags.Instance | BindingFlags.Public);
            var searchDatabaseOptions = searchDatabase.GetNestedType("Options");
            if (searchDatabase_Settings == null)
                return;
            s_SearchDatabase_Options_Disabled = searchDatabaseOptions.GetField("disabled", BindingFlags.Instance | BindingFlags.Public);
            s_SearchDatabase_Options_Types = searchDatabaseOptions.GetField("types", BindingFlags.Instance | BindingFlags.Public);
        }

        // This is less than ideal since the whole SearchDatabase API is internal. But here is how you would tests if all indexes (i.e SearchDatabase) are ready.

        public static IEnumerable SearchDatabases()
        {
            return (IEnumerable)s_SearchDatabase_Enumerate?.Invoke(null, Array.Empty<object>());
        }

        public static bool CheckThatSearchDatabaseIsReady(object searchDB)
        {
            return s_SearchDatabase_Ready == null || s_SearchDatabase_Updating == null || searchDB == null ? false :
                (bool)s_SearchDatabase_Ready.GetValue(searchDB) && (bool)s_SearchDatabase_Updating.GetValue(searchDB) == false;
        }

        public static bool SearchDatabaseIsReady()
        {
            if (s_SearchDatabase_Enumerate == null || s_SearchDatabase_Ready == null || s_SearchDatabase_Updating == null)
                // fallback if reflection breaks
                return true;
            var reflectionInfoForValidatingIndexingOptionsIsAvailable =
                s_SearchDatabase_Settings != null && s_SearchDatabase_Settings_Options != null
                && s_SearchDatabase_Options_Disabled != null && s_SearchDatabase_Options_Types != null;

            // the default assumption (because that's the default configuration and what makes the most sense if the options for this are to ever change)
            // is that this should be possible
            // only disable this option if we have confirmation that it is not possible
            bool quickSearchCanUseNameFilter = true;

            foreach (var db in SearchDatabases())
            {
                if (reflectionInfoForValidatingIndexingOptionsIsAvailable)
                {
                    var dbSettings = s_SearchDatabase_Settings.GetValue(db);
                    if (dbSettings != null)
                    {
                        var dbOptions = s_SearchDatabase_Settings_Options.GetValue(dbSettings);
                        if (dbOptions != null)
                        {
                            var disabled = s_SearchDatabase_Options_Disabled.GetValue(dbOptions);
                            var types = s_SearchDatabase_Options_Types.GetValue(dbOptions);
                            if (disabled != null && disabled is bool && types != null && types is bool)
                            {
                                if ((bool)disabled || !(bool)types)
                                {
                                    quickSearchCanUseNameFilter = false;
                                }
                            }
                        }
                    }
                }
                if (!CheckThatSearchDatabaseIsReady(db))
                    return false;
            }
            QuickSearchCanUseNameFilter = quickSearchCanUseNameFilter;
            return true;
        }
    }

    internal static class EditorAssetFinderUtility
    {
        public struct Findings : IDisposable
        {
            public Findings(Object foundObject, int degreeOfCertainty, SearchItem searchItem, SearchContext searchContext, bool isDynamicObject = false)
            {
                FoundObject = foundObject;
                DegreeOfCertainty = degreeOfCertainty;
                FailReason = SearchFailReason.Found;
                SearchItem = searchItem;
                SearchContext = searchContext;
                IsDynamicObject = isDynamicObject;
            }

            public Object FoundObject;
            public int DegreeOfCertainty;
            public SearchFailReason FailReason;
            public SearchItem SearchItem;
            public SearchContext SearchContext;
            public bool IsDynamicObject;

            public void Dispose()
            {
                SearchContext?.Dispose();
            }
        }

        public struct PreviewImageResult : IDisposable
        {
            public readonly Texture PreviewImage;
            public bool PreviewImageNeedsCleanup { get; private set; }

            public PreviewImageResult(Texture previewImage, bool previewImageNeedsCleanup)
            {
                PreviewImage = previewImage;
                // Do not destroy preview image if is corresponds to the real asset in the AssedDatabase.
                PreviewImageNeedsCleanup = previewImageNeedsCleanup && (PreviewImage != null && !AssetDatabase.Contains(PreviewImage));
            }

            public void Dispose()
            {
                if (PreviewImage && PreviewImageNeedsCleanup && !PreviewImage.hideFlags.HasFlag(HideFlags.NotEditable))
                    Object.DestroyImmediate(PreviewImage);
                PreviewImageNeedsCleanup = false;
            }
        }

        public enum SearchFailReason
        {
            Found,
            NotFound,
            FoundTooMany,
            FoundTooManyToProcess,
            TypeIssues,
            SearchCanceled,
            SearchTimeout,
        }


        static MethodInfo s_SetProjectSearch = null;
        static MethodInfo s_SetSceneHiearchySearch = null;
        static Type s_SceneHieararchyWindowType = null;
        static Type s_ProjectBrowserWindowType = null;

        static EditorAssetFinderUtility()
        {
            var editorAssembly = typeof(EditorGUIUtility).Assembly;
            s_SceneHieararchyWindowType = editorAssembly.GetType("UnityEditor.SceneHierarchyWindow");
            s_SetSceneHiearchySearch = s_SceneHieararchyWindowType.GetMethod("SetSearchFilter", BindingFlags.Instance | BindingFlags.NonPublic);
            s_ProjectBrowserWindowType = editorAssembly.GetType("UnityEditor.ProjectBrowser");
            var projectMethods = s_ProjectBrowserWindowType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            foreach (var method in projectMethods)
            {
                if (method.Name == "SetSearch")
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string))
                    {
                        s_SetProjectSearch = method;
                        break;
                    }
                }
            }
            QuickSearchUtility.InitializeQuickSearch(async: true);
        }

        static EditorWindow GetProjectBrowserWindow(bool focusIt = false)
        {
            EditorWindow projectWindow = EditorWindow.GetWindow(s_ProjectBrowserWindowType);
            if (focusIt)
                projectWindow.Focus();
            return projectWindow;
        }

        static void SetProjectSearch(string searchString)
        {
            var projectWindow = GetProjectBrowserWindow(true);
            s_SetProjectSearch.Invoke(projectWindow, new object[] { searchString });
        }

        static void SetSceneSearch(string searchString)
        {
            var sceneHierarchy = EditorWindow.GetWindow(s_SceneHieararchyWindowType);
            sceneHierarchy.Focus();
            s_SetSceneHiearchySearch.Invoke(sceneHierarchy, new object[] { searchString, SearchableEditorWindow.SearchMode.All, true, false });
        }

        public static bool CanPingByInstanceId(uint sessionId)
        {
            return EditorSessionUtility.InstanceIdPingingSupportedByUnityVersion && EditorSessionUtility.CurrentSessionId == sessionId;
        }

        public static void Ping(InstanceID instanceId, uint sessionId, bool logWarning = true)
        {
            if (!EditorSessionUtility.InstanceIdPingingSupportedByUnityVersion)
            {
                if (logWarning)
                    UnityEngine.Debug.LogWarning(TextContent.InstanceIdPingingOnlyWorksInNewerUnityVersions);
                return;
            }

            if (CanPingByInstanceId(sessionId))
            {
                ClearInstanceIdSelection();
                SetActiveInstanceId(instanceId);
                PingByInstanceId(instanceId);
            }
            else if (logWarning)
                UnityEngine.Debug.LogWarningFormat(TextContent.InstanceIdPingingOnlyWorksInSameSessionMessage, EditorSessionUtility.CurrentSessionId, sessionId);
        }

        static void ClearInstanceIdSelection()
        {
#if !INSTANCE_ID_CHANGED
            Selection.instanceIDs = new int[0];
#else
            Selection.instanceIDs = new InstanceID[0];
#endif
        }

        static void SetActiveInstanceId(InstanceID instanceId)
        {
#if !INSTANCE_ID_CHANGED
            Selection.activeInstanceID = (int)(ulong)instanceId;
#else
            Selection.activeInstanceID = instanceId;
#endif
        }

        static void PingByInstanceId(InstanceID instanceId)
        {
#if !INSTANCE_ID_CHANGED
            EditorGUIUtility.PingObject((int)(ulong)instanceId);
#else
            EditorGUIUtility.PingObject(instanceId);
#endif
        }

        public static async Task<Findings> FindObject(CachedSnapshot snapshot, UnifiedUnityObjectInfo unifiedUnityObjectInfo, CancellationToken cancellationToken)
        {
            // If the object belongs to the same session there is a chance that it is still loaded and can be used based on the InstanceID
            // (e.g. Texture)
            if (snapshot.MetaData.SessionGUID == EditorSessionUtility.CurrentSessionId)
            {
                var findings = FindByInstanceID(snapshot, unifiedUnityObjectInfo);
                if (findings.FailReason != SearchFailReason.NotFound)
                    return findings;
            }
            if (snapshot.HasTargetAndMemoryInfo && snapshot.MetaData.ProductName != PlayerSettings.productName)
            {
                // Could be the Project have been renamed since the capture? Sure.
                // But chances are, this is a different Project than the one the captured was made in so, don't risk sounding too sure.
                return new Findings() { FailReason = SearchFailReason.NotFound };
            }
            if (unifiedUnityObjectInfo.IsSceneObject && !string.IsNullOrEmpty(unifiedUnityObjectInfo.NativeObjectName))
            {
                return await FindSceneObject(snapshot, unifiedUnityObjectInfo, cancellationToken);
            }
            if (unifiedUnityObjectInfo.IsRuntimeCreated)
            {
                // no dice, if it was a runtime created asset, and we couldn't find it as part of this session, there are no guarantees, not even close hits
                return new Findings() { FailReason = SearchFailReason.NotFound };
            }
            if (unifiedUnityObjectInfo.IsPersistentAsset && !string.IsNullOrEmpty(unifiedUnityObjectInfo.NativeObjectName))
            {
                return await FindAsset(snapshot, unifiedUnityObjectInfo, cancellationToken);
            }
            return new Findings() { FailReason = SearchFailReason.NotFound };
        }

        static Findings FindByInstanceID(CachedSnapshot snapshot, UnifiedUnityObjectInfo unifiedUnityObjectInfo)
        {
            var oldSelection = Selection.instanceIDs;
            var oldActiveSelection = Selection.activeInstanceID;
            ClearInstanceIdSelection();
            SetActiveInstanceId(unifiedUnityObjectInfo.InstanceId);
            if (Selection.activeObject != null)
            {
                var typeMismatch = CheckTypeMismatch(Selection.activeObject, unifiedUnityObjectInfo, snapshot);
                if (!typeMismatch)
                {
                    var obj = Selection.activeObject;
                    // maybe use Undo for this
                    Selection.instanceIDs = oldSelection;
                    Selection.activeInstanceID = oldActiveSelection;
                    return new Findings(obj, 100, null, null, isDynamicObject: true);
                }
            }
            Selection.instanceIDs = oldSelection;
            Selection.activeInstanceID = oldActiveSelection;
            return new Findings() { FailReason = SearchFailReason.NotFound };
        }

        static async Task<Findings> FindAsset(CachedSnapshot snapshot, UnifiedUnityObjectInfo unifiedUnityObjectInfo, CancellationToken cancellationToken)
        {
            var searchString = ConstructSearchString(unifiedUnityObjectInfo, unifiedUnityObjectInfo.Type.IsSceneObjectType);

            var failReason = SearchFailReason.NotFound;
            Object foundObject = null;
            //maybe eventually join the contexts and filter later.
            SearchItem searchItem = null;
            SearchContext context = SearchService.CreateContext(
                providerIds: QuickSearchUtility.AssetSearchProviders,
                searchText: searchString);

            using var asyncSearch = new AsyncSearchHelper(context, $"Memory Profiler/Searching Project for {searchString}");

            var search = await asyncSearch.RequestSearchAndAwaitResults(cancellationToken);
            if (asyncSearch.State == AsyncSearchHelper.SearchState.Canceled)
                return new Findings { FailReason = SearchFailReason.SearchCanceled };
            if (asyncSearch.State != AsyncSearchHelper.SearchState.FinishedSuccessfully)
                return new Findings { FailReason = SearchFailReason.SearchTimeout };
            if (search.Count == 1)
            {
                searchItem = search[0];
                if (searchItem != null)
                {
                    foundObject = searchItem.ToObject(unifiedUnityObjectInfo);
                    if (!CheckTypeMismatch(foundObject, unifiedUnityObjectInfo, snapshot))
                    {
                        failReason = SearchFailReason.Found;
                    }
                }
            }
            else if (search.Count > 0 && search.Count < 5)
            {
                // Asset database search for e.g. "Guard t:Mesh" also finds "Guard" Mesh and "Guard.fbx" Mesh, so, try trimming it down if its only a small set of results
                searchItem = null;
                int likelyCandidateCount = 0;
                foreach (var item in search)
                {
                    if (item != null)
                    {
                        var obj = item.ToObject(unifiedUnityObjectInfo);
                        if (obj != null && obj.name == unifiedUnityObjectInfo.NativeObjectName && !CheckTypeMismatch(obj, unifiedUnityObjectInfo, snapshot))
                        {
                            if (foundObject == null && foundObject != obj)
                            {
                                foundObject = obj;
                                searchItem = item;
                            }
                            ++likelyCandidateCount;
                        }
                    }
                }
                if (searchItem != null && foundObject != null && likelyCandidateCount == 1)
                {
                    failReason = SearchFailReason.Found;
                }
                else
                {
                    if (likelyCandidateCount > 0)
                        failReason = SearchFailReason.FoundTooMany;
                    else if (likelyCandidateCount == 0)
                        failReason = SearchFailReason.NotFound;
                    searchItem = null;
                    foundObject = null;
                }
            }
            else
            {
                context.Dispose();
                if (search.Count > 1)
                    failReason = SearchFailReason.FoundTooMany;
            }

            if (failReason == SearchFailReason.Found && foundObject != null && context != null)
            {
                return new Findings(foundObject, 80, searchItem, context);
            }
            context.Dispose();
            return new Findings() { FailReason = failReason };
        }

        static async Task<Findings> FindSceneObject(CachedSnapshot snapshot, UnifiedUnityObjectInfo unifiedUnityObjectInfo, CancellationToken cancellationToken)
        {
            if (snapshot.HasSceneRootsAndAssetbundles)
            {
                // TODO: with captured Scene Roots changes, double check the open scene names
                // if they mismatch, return;
            }
            using var context = SearchService.CreateContext(providerId: QuickSearchUtility.SearchProviderIdScene, searchText: ConstructSearchString(unifiedUnityObjectInfo));
            using var asyncSearch = new AsyncSearchHelper(context, $"Memory Profiler/Searching Scenes for {context.searchQuery}");
            var search = await asyncSearch.RequestSearchAndAwaitResults(cancellationToken);
            if (asyncSearch.State == AsyncSearchHelper.SearchState.Canceled)
                return new Findings { FailReason = SearchFailReason.SearchCanceled };
            if (asyncSearch.State != AsyncSearchHelper.SearchState.FinishedSuccessfully)
                return new Findings { FailReason = SearchFailReason.SearchTimeout };

            if (search.Count > 1)
                return new Findings() { FailReason = SearchFailReason.FoundTooMany };
            if (search.Count == 1)
            {
                var foundObject = search[0].ToObject(unifiedUnityObjectInfo);
                if (foundObject != null && !CheckTypeMismatch(foundObject, unifiedUnityObjectInfo, snapshot))
                {
                    return new Findings(foundObject, 80, search[0], context);
                }
            }
            return new Findings() { FailReason = SearchFailReason.NotFound };
        }

        static Object ToObject(this SearchItem searchItem, UnifiedUnityObjectInfo unifiedUnityObjectInfo)
        {
            var obj = searchItem.ToObject();
            if (unifiedUnityObjectInfo.Type.IsSceneObjectType && obj is GameObject && !unifiedUnityObjectInfo.IsGameObject)
            {
                var go = obj as GameObject;
                // Managed Type Name is more specific instead of e.g. MonoBehaviou, but it may fail
                obj = go.GetComponent(unifiedUnityObjectInfo.ManagedTypeName);
                if (obj == null) // try native type name in that case
                    obj = go.GetComponent(unifiedUnityObjectInfo.NativeTypeName);
            }
            return obj;
        }

        public static PreviewImageResult GetPreviewImage(Findings findings)
        {
            if (findings.IsDynamicObject)
            {
                return new PreviewImageResult(
                    TryObtainingPreviewForDynamicObject(findings.FoundObject, out var previewImageNeedsCleanup),
                    previewImageNeedsCleanup);
            }

            var previewImage = TryObtainingPreviewWithEditor(findings.FoundObject);
            if (previewImage == null && findings.SearchItem != null)
                previewImage = findings.SearchItem.GetPreview(findings.SearchContext, new Vector2(500, 500), FetchPreviewOptions.Large);
            return new PreviewImageResult(previewImage, true);
        }

        static Texture TryObtainingPreviewForDynamicObject(Object obj, out bool previewImageNeedsCleanup)
        {
            previewImageNeedsCleanup = false;
            if (obj is Texture2D || obj is RenderTexture)
            {
                if (obj is RenderTexture && (obj as RenderTexture).antiAliasing > 1)
                    return null;
                return obj as Texture;
            }

            previewImageNeedsCleanup = true;
            return TryObtainingPreviewWithEditor(obj);
        }

        static Texture TryObtainingPreviewWithEditor(Object obj)
        {
            if (obj == null)
                return null;

            // Prefab Importer fails in Awake when initialized via UnityEditor.Editor.CreateEditor
            // there is also no preview for Asset Importers in general
            if (obj is AssetImporter)
                return null;

            var editor = UnityEditor.Editor.CreateEditor(obj);
            long fileId;
            string assetPath = null;
            Texture2D preview = null;
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out var guid, out fileId))
            {
                assetPath = AssetDatabase.GUIDToAssetPath(guid);
            }
            if (!string.IsNullOrEmpty(assetPath))
            {
                preview = editor.RenderStaticPreview(assetPath, new Object[] { obj }, 500, 500);
            }
            Object.DestroyImmediate(editor);
            return preview;
        }

        static bool CheckTypeMismatch(Object obj, UnifiedUnityObjectInfo unifiedUnityObjectInfo, CachedSnapshot snapshot)
        {
            var typeMismatch = false;
            var selectedObjectType = obj.GetType().FullName;
            if (unifiedUnityObjectInfo.Type.HasManagedType)
            {
                typeMismatch = unifiedUnityObjectInfo.Type.ManagedTypeName != selectedObjectType;
            }
            else
            {
                for (int i = 0; i < snapshot.TypeDescriptions.TypeDescriptionName.Length; i++)
                {
                    if (snapshot.TypeDescriptions.TypeDescriptionName[i] == selectedObjectType)
                    {
                        int nativeTypeIndex;
                        if (snapshot.TypeDescriptions.UnityObjectTypeIndexToNativeTypeIndex.TryGetValue(i, out nativeTypeIndex)
                            && nativeTypeIndex >= 0)
                        {
                            typeMismatch = nativeTypeIndex != unifiedUnityObjectInfo.NativeTypeIndex;
                        }
                        break;
                    }
                }
            }
            return typeMismatch;
        }

        static string ConstructSearchString(UnifiedUnityObjectInfo unifiedUnityObjectInfo, bool quickSearchSceneObjectSearch = false, bool searchAllInProjectBrowser = false, bool forQuickSearch = true)
        {
            var name = unifiedUnityObjectInfo.NativeObjectName;
            // only QuickSearch can filter to exact names via the "name=" filter
            var searchString = forQuickSearch && QuickSearchUtility.QuickSearchCanUseNameFilter ? $"name=\"{name}\"" : name;

            // take paths name parts out of the equation, they are e.g. used for shaders
            var lastNameSeparator = searchString.LastIndexOf('/');
            if (lastNameSeparator++ > 0)
                searchString = searchString.Substring(lastNameSeparator, searchString.Length - lastNameSeparator);
            if (quickSearchSceneObjectSearch)
            {
                // Quick Search can't search for components directly
                searchString += " t:GameObject";
            }
            else if (unifiedUnityObjectInfo.Type.HasManagedType)
            {
                var managedTypeName = unifiedUnityObjectInfo.Type.ManagedTypeName;
                var lastSeparator = managedTypeName.LastIndexOf('.');
                if (lastSeparator++ > 0)
                    managedTypeName = managedTypeName.Substring(lastSeparator, managedTypeName.Length - lastSeparator);
                searchString += " t:" + managedTypeName;
            }
            else if (unifiedUnityObjectInfo.Type.HasNativeType)
                searchString += " t:" + unifiedUnityObjectInfo.Type.NativeTypeName;

            // Set Project Browser search to search in Assets and Package folders with "a:all"
            if (searchAllInProjectBrowser)
                searchString += " a:all";
            return searchString;
        }

        public static void SelectObject(Object obj)
        {
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
        }

        public static GUIContent GetSearchButtonLabel(CachedSnapshot snapshot, UnifiedUnityObjectInfo selectedUnityObject)
        {
            if (selectedUnityObject.IsSceneObject)
            {
                if (snapshot.HasSceneRootsAndAssetbundles)
                {
                    // TODO: with captured Scene Roots changes, double check the open scene names, if they do: return "Search In Scene"
                    // if they missmatch, see if you can find a scene of that name in the project";
                    // if yes, return "Search In <Scene Name>";
                    // else return null;
                }
                return TextContent.SearchInSceneButton;
            }
            if (selectedUnityObject.IsPersistentAsset)
                return TextContent.SearchInProjectButton;
            return TextContent.SearchButtonCantSearch;
        }

        public static void SetEditorSearchFilterForObject(CachedSnapshot snapshot, UnifiedUnityObjectInfo selectedUnityObject)
        {
            if (selectedUnityObject.IsSceneObject)
            {
                SetSceneSearch(ConstructSearchString(selectedUnityObject, forQuickSearch: false));
            }
            if (selectedUnityObject.IsPersistentAsset)
            {
                SetProjectSearch(ConstructSearchString(selectedUnityObject, searchAllInProjectBrowser: true, forQuickSearch: false));
            }
        }

        public static SearchContext BuildContext(CachedSnapshot snapshot, UnifiedUnityObjectInfo selectedUnityObject)
        {
            return SearchService.CreateContext(
                selectedUnityObject.IsSceneObject ? QuickSearchUtility.SceneObjectSearchProviders : QuickSearchUtility.AssetSearchProviders,
                searchText: ConstructSearchString(selectedUnityObject, selectedUnityObject.Type.IsSceneObjectType));
        }

        public static ISearchView OpenQuickSearch(CachedSnapshot snapshot, UnifiedUnityObjectInfo selectedUnityObject)
        {
            var context = BuildContext(snapshot, selectedUnityObject);
            var state = new SearchViewState(context);
            state.group = selectedUnityObject.IsSceneObject ? "scene" : "asset";
            return SearchService.ShowWindow(state);
        }
    }
}

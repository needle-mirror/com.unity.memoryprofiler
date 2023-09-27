using System;
using System.Reflection;
using Unity.MemoryProfiler.Editor.UI;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEditor.Search;
using UnityEngine;
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
        public const string SearchProviderIdScene = "scene";
        public const string SearchProviderIdAsset = "asset";
        public const string SearchProviderIdAssetDatabase = "adb";

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
            using var context = SearchService.CreateContext(providerIds: new[] { SearchProviderIdScene, SearchProviderIdAsset, SearchProviderIdAssetDatabase }, searchText: $"t:{nameof(MemoryProfilerWindow)}");
            if (async)
            {
                // Initialize Async, preferred path, should be triggered by e.g. opening the Memory Profiler window
                SearchService.Request(context, (context, items) => QuickSearchInitialized = true);
            }
            else
            {
                // Initialize Synchronously should ideally only fire for search tests during their initialization
                using var search = SearchService.Request(context, SearchFlags.Synchronous);
            }
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
            QuickSearchUtility.InitializeQuickSearch(async: false);
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

        public static void Ping(int instanceId, uint sessionId, bool logWarning = true)
        {
            if (!EditorSessionUtility.InstanceIdPingingSupportedByUnityVersion)
            {
                if (logWarning)
                    UnityEngine.Debug.LogWarning(TextContent.InstanceIdPingingOnlyWorksInNewerUnityVersions);
                return;
            }

            if (CanPingByInstanceId(sessionId))
            {
                Selection.instanceIDs = new int[0];
                Selection.activeInstanceID = instanceId;
                EditorGUIUtility.PingObject(instanceId);
            }
            else if (logWarning)
                UnityEngine.Debug.LogWarningFormat(TextContent.InstanceIdPingingOnlyWorksInSameSessionMessage, EditorSessionUtility.CurrentSessionId, sessionId);
        }

        public static Findings FindObject(CachedSnapshot snapshot, UnifiedUnityObjectInfo unifiedUnityObjectInfo)
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
                return FindSceneObject(snapshot, unifiedUnityObjectInfo);
            }
            if (unifiedUnityObjectInfo.IsRuntimeCreated)
            {
                // no dice, if it was a runtime created asset, and we couldn't find it as part of this session, there are no guarantees, not even close hits
                return new Findings() { FailReason = SearchFailReason.NotFound };
            }
            if (unifiedUnityObjectInfo.IsPersistentAsset && !string.IsNullOrEmpty(unifiedUnityObjectInfo.NativeObjectName))
            {
                return FindAsset(snapshot, unifiedUnityObjectInfo);
            }
            return new Findings() { FailReason = SearchFailReason.NotFound };
        }

        static Findings FindByInstanceID(CachedSnapshot snapshot, UnifiedUnityObjectInfo unifiedUnityObjectInfo)
        {
            var oldSelection = Selection.instanceIDs;
            var oldActiveSelection = Selection.activeInstanceID;
            Selection.instanceIDs = new int[0];
            Selection.activeInstanceID = unifiedUnityObjectInfo.InstanceId;
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

        static Findings FindAsset(CachedSnapshot snapshot, UnifiedUnityObjectInfo unifiedUnityObjectInfo)
        {
            var searchString = ConstructSearchString(unifiedUnityObjectInfo, unifiedUnityObjectInfo.Type.IsSceneObjectType);

            var failReason = SearchFailReason.NotFound;
            Object foundObject = null;
            //maybe eventually join the contexts and filter later.
            SearchItem searchItem = null;
            SearchContext context = SearchService.CreateContext(
                providerIds: new string[] { QuickSearchUtility.SearchProviderIdAsset, QuickSearchUtility.SearchProviderIdAssetDatabase },
                searchText: searchString);
            using (var search = SearchService.Request(context, SearchFlags.Synchronous))
            {
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
                else if (search.Count < 5)
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
            }

            if (failReason == SearchFailReason.Found && foundObject != null && context != null)
            {
                return new Findings(foundObject, 80, searchItem, context);
            }
            return new Findings() { FailReason = failReason };
        }

        static Findings FindSceneObject(CachedSnapshot snapshot, UnifiedUnityObjectInfo unifiedUnityObjectInfo)
        {
            if (snapshot.HasSceneRootsAndAssetbundles)
            {
                // TODO: with captured Scene Roots changes, double check the open scene names
                // if they mismatch, return;
            }
            using (var context = SearchService.CreateContext(providerId: QuickSearchUtility.SearchProviderIdScene, searchText: ConstructSearchString(unifiedUnityObjectInfo)))
            {
                using (var search = SearchService.Request(context, SearchFlags.Synchronous))
                {
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
            string guid;
            long fileId;
            string assetPath = null;
            Texture2D preview = null;
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out guid, out fileId))
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

        static string ConstructSearchString(UnifiedUnityObjectInfo unifiedUnityObjectInfo, bool quickSearchSceneObjectSearch = false, bool searchAllInProjectBrowser = false)
        {
            var searchString = unifiedUnityObjectInfo.NativeObjectName;

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
                SetSceneSearch(ConstructSearchString(selectedUnityObject));
            }
            if (selectedUnityObject.IsPersistentAsset)
            {
                SetProjectSearch(ConstructSearchString(selectedUnityObject, searchAllInProjectBrowser: true));
            }
        }

        public static ISearchView OpenQuickSearch(CachedSnapshot snapshot, UnifiedUnityObjectInfo selectedUnityObject)
        {
            var context = SearchService.CreateContext(searchText: ConstructSearchString(selectedUnityObject, selectedUnityObject.Type.IsSceneObjectType));
            var state = new SearchViewState(context);
            state.group = selectedUnityObject.IsSceneObject ? "scene" : "asset";
            return SearchService.ShowWindow(state);
        }
    }
}

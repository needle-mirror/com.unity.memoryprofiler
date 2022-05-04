using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.MemoryProfiler.Editor.UI;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
#if UNITY_2021_2_OR_NEWER
using UnityEditor.Search;
#endif
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.MemoryProfiler.Editor
{
    internal static class EditorAssetFinderUtility
    {
        public struct Findings
        {
            public Object FoundObject;
            public int DegreeOfCertainty;
            public SearchFailReason FailReason;
            public Texture PreviewImage;
            public bool PreviewImageNeedsCleanup;
        }
        public enum SearchFailReason
        {
            Found,
            NotFound,
            FoundTooMany,
            FoundTooManyToProcess,
            TypeIssues,
        }

        const string k_SearchProviderIdScene = "scene";
        const string k_SearchProviderIdAsset = "asset";
        const string k_SearchProviderIdAssetDatabase = "adb";

        static MethodInfo s_GetLocalGuid = null;
        static MethodInfo s_SetProjectSearch = null;
        static MethodInfo s_SetSceneHiearchySearch = null;
        static Type s_SceneHieararchyWindowType = null;
        static Type s_ProjectBrowserWindowType = null;

        public static bool InstanceIdPingingSupportedByUnityVersion => s_GetLocalGuid != null;
        public static uint CurrentSessionId => InstanceIdPingingSupportedByUnityVersion ? (uint)s_GetLocalGuid.Invoke(null, null) : uint.MaxValue;

        static EditorAssetFinderUtility()
        {
            var editorAssembly = typeof(EditorGUIUtility).Assembly;
            s_GetLocalGuid = editorAssembly.GetType("UnityEditor.EditorConnectionInternal").GetMethod("GetLocalGuid");
            s_SceneHieararchyWindowType = editorAssembly.GetType("UnityEditor.SceneHierarchyWindow");
            s_SetSceneHiearchySearch = s_SceneHieararchyWindowType.GetMethod("SetSearchFilter", BindingFlags.Instance | BindingFlags.NonPublic);
            s_ProjectBrowserWindowType = editorAssembly.GetType("UnityEditor.ProjectBrowser");
            var projectMethods = s_ProjectBrowserWindowType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            foreach (var method in projectMethods)
            {
                if (method.Name == "SetSearch")
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length > 1)
                        continue;
                    foreach (var param in parameters)
                    {
                        if (param.ParameterType == typeof(string))
                        {
                            s_SetProjectSearch = method;
                            break;
                        }
                    }
                    if (s_SetProjectSearch != null)
                        break;
                }
            }


#if UNITY_2021_2_OR_NEWER // TODO: || QUICK_SEARCH_AVAILABLE
            // Initialize quick search
            using var context = SearchService.CreateContext(providerIds: new[] { k_SearchProviderIdScene, k_SearchProviderIdAsset, k_SearchProviderIdAssetDatabase }, searchText: $"t:{nameof(MemoryProfilerWindow)}");
            using var search = SearchService.Request(context, SearchFlags.Synchronous);
#endif
        }

        static void SetProjectSearch(string searchString)
        {
            EditorUtility.FocusProjectWindow();
            var projectWindow = EditorWindow.focusedWindow;
            if (projectWindow == null || projectWindow.GetType() != s_ProjectBrowserWindowType)
            {
                projectWindow = EditorWindow.GetWindow(s_ProjectBrowserWindowType);
                projectWindow.Focus();
            }
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
            return InstanceIdPingingSupportedByUnityVersion && CurrentSessionId == sessionId;
        }

        public static void Ping(int instanceId, uint sessionId, bool logWarning = true)
        {
            if (!InstanceIdPingingSupportedByUnityVersion)
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
                UnityEngine.Debug.LogWarningFormat(TextContent.InstanceIdPingingOnlyWorksInSameSessionMessage, sessionId, CurrentSessionId);
        }

        public static Findings FindObject(CachedSnapshot snapshot, UnifiedUnityObjectInfo unifiedUnityObjectInfo)
        {
            if (snapshot.MetaData.SessionGUID == CurrentSessionId)
            {
                var findings = FindByInstanceID(snapshot, unifiedUnityObjectInfo);
                if (findings.FailReason != SearchFailReason.NotFound)
                    return findings;
            }
            if (unifiedUnityObjectInfo.IsRuntimeCreated)
            {
                // no dice, if it was runtime created, and we couldn't find it as part of this session, there are no guarantees, not even close hits
                return new Findings() { FailReason = SearchFailReason.NotFound };
            }
            if (snapshot.HasTargetAndMemoryInfo && snapshot.MetaData.ProductName != PlayerSettings.productName)
            {
                // Could be the Project have been renamed since the capture? Sure.
                // But chances are, this is a different Project than the one the captured was made in so, don't risk sounding too sure.
                return new Findings() { FailReason = SearchFailReason.NotFound };
            }
            if (unifiedUnityObjectInfo.IsAsset && !string.IsNullOrEmpty(unifiedUnityObjectInfo.NativeObjectName))
            {
                return FindAsset(snapshot, unifiedUnityObjectInfo);
            }
            if (unifiedUnityObjectInfo.IsSceneObject && !string.IsNullOrEmpty(unifiedUnityObjectInfo.NativeObjectName))
            {
                return FindSceneObject(snapshot, unifiedUnityObjectInfo);
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
                    bool previewImageNeedsCleanup;
                    var previewImage = TryObtainingPreview(obj, out previewImageNeedsCleanup);
                    return new Findings() { FoundObject = obj, DegreeOfCertainty = 100, PreviewImage = previewImage, PreviewImageNeedsCleanup = previewImageNeedsCleanup};
                }
            }
            Selection.instanceIDs = oldSelection;
            Selection.activeInstanceID = oldActiveSelection;
            return new Findings() { FailReason = SearchFailReason.NotFound };
        }

#if UNITY_2021_2_OR_NEWER // TODO: || QUICK_SEARCH_AVAILABLE // conditionally depend on Quick search package
        static Findings FindAsset(CachedSnapshot snapshot, UnifiedUnityObjectInfo unifiedUnityObjectInfo)
        {
            var searchString = ConstructSearchString(unifiedUnityObjectInfo);

            var failReason = SearchFailReason.NotFound;
            Object foundObject = null;
            //maybe eventually join the contexts and filter later.
            SearchItem searchItem = null;
            SearchContext succesfulContext = null;
            var assetContext = SearchService.CreateContext(providerId: k_SearchProviderIdAsset, searchText: searchString);
            using (var search = SearchService.Request(assetContext, SearchFlags.Synchronous))
            {
                if (search.Count == 1)
                {
                    succesfulContext = assetContext;
                    searchItem = search[0];
                    if (searchItem != null)
                    {
                        foundObject = searchItem.ToObject();
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
                            var obj = item.ToObject();
                            if (obj != null && obj.name == unifiedUnityObjectInfo.NativeObjectName && !CheckTypeMismatch(obj, unifiedUnityObjectInfo, snapshot))
                            {
                                if (foundObject == null && foundObject != obj)
                                    ++likelyCandidateCount;
                                foundObject = obj;
                                searchItem = item;
                            }
                        }
                    }
                    if (searchItem != null && foundObject != null && likelyCandidateCount == 1)
                    {
                        succesfulContext = assetContext;
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
                    assetContext.Dispose();
                    if (search.Count > 1)
                        failReason = SearchFailReason.FoundTooMany;
                }
            }

            if (failReason != SearchFailReason.Found)
            {
                var adbContext = SearchService.CreateContext(providerId: k_SearchProviderIdAssetDatabase, searchText: searchString);
                using (var search = SearchService.Request(adbContext, SearchFlags.Synchronous))
                {
                    if (search.Count == 1)
                    {
                        if (succesfulContext != null)
                            succesfulContext.Dispose();
                        succesfulContext = adbContext;
                        searchItem = search[0];
                        if (searchItem != null)
                        {
                            foundObject = searchItem.ToObject();
                            if (!CheckTypeMismatch(foundObject, unifiedUnityObjectInfo, snapshot))
                            {
                                failReason = SearchFailReason.Found;
                            }
                        }
                    }
                    else
                    {
                        adbContext.Dispose();
                        if (search.Count > 1)
                            failReason = SearchFailReason.FoundTooMany;
                    }
                }
            }

            if (failReason == SearchFailReason.Found && foundObject != null && succesfulContext != null)
            {
                bool previewImageNeedsCleanup;
                var previewImage = TryObtainingPreview(foundObject, out previewImageNeedsCleanup);
                if (previewImage == null)
                {
                    previewImage = searchItem.GetPreview(succesfulContext, new Vector2(500, 500), FetchPreviewOptions.Large);
                    previewImageNeedsCleanup = previewImage == null || !previewImage.hideFlags.HasFlag(HideFlags.NotEditable);
                }
                succesfulContext.Dispose();
                return new Findings() {
                    FoundObject = foundObject, DegreeOfCertainty = 80,
                    PreviewImage = previewImage, PreviewImageNeedsCleanup = previewImageNeedsCleanup
                };
            }
            return new Findings() { FailReason = failReason };
        }

#else
        static Findings FindAsset(CachedSnapshot snapshot, UnifiedUnityObjectInfo unifiedUnityObjectInfo)
        {
            var searchString = ConstructSearchString(unifiedUnityObjectInfo);

            var foundAssets = AssetDatabase.FindAssets(searchString);
            var assetType = unifiedUnityObjectInfo.Type.GetManagedSystemType(snapshot);
            if (assetType != null && foundAssets != null && foundAssets.Length == 1)
            {
                var path = AssetDatabase.GUIDToAssetPath(foundAssets[0]);
                if (!string.IsNullOrEmpty(path))
                {
                    var foundObj = AssetDatabase.LoadAssetAtPath(path, assetType);
                    // this is a guesstimate, don't trust this too much

                    bool previewImageNeedsCleanup;
                    var previewImage = TryObtainingPreview(foundObj, out previewImageNeedsCleanup);
                    return new Findings() { FoundObject = foundObj, DegreeOfCertainty = 70, PreviewImage = previewImage, PreviewImageNeedsCleanup = previewImageNeedsCleanup};
                }
            }
            return new Findings() { FailReason = foundAssets.Length == 0 ? SearchFailReason.TypeIssues : SearchFailReason.FoundTooMany };
        }

#endif

#if UNITY_2021_2_OR_NEWER // TODO: || QUICK_SEARCH_AVAILABLE
        static Findings FindSceneObject(CachedSnapshot snapshot, UnifiedUnityObjectInfo unifiedUnityObjectInfo)
        {
            if (snapshot.HasSceneRootsAndAssetbundles)
            {
                // TODO: with captured Scene Roots changes, double check the open scene names
                // if they mismatch, return;
            }
            using (var context = SearchService.CreateContext(providerId: k_SearchProviderIdScene, searchText: ConstructSearchString(unifiedUnityObjectInfo, true)))
            {
                using (var search = SearchService.Request(context, SearchFlags.Synchronous))
                {
                    if (search.Count > 1)
                        return new Findings() { FailReason = SearchFailReason.FoundTooMany };
                    if (search.Count == 1)
                    {
                        var foundObject = search[0].ToObject();
                        if (foundObject is GameObject && !unifiedUnityObjectInfo.IsGameObject)
                        {
                            var go = foundObject as GameObject;
                            // Managed Type Name is more specific instead of e.g. MonoBehaviou, but it may fail
                            foundObject = go.GetComponent(unifiedUnityObjectInfo.ManagedTypeName);
                            if (foundObject == null) // try native type name in that case
                                foundObject = go.GetComponent(unifiedUnityObjectInfo.NativeTypeName);
                        }
                        if (foundObject != null && !CheckTypeMismatch(foundObject, unifiedUnityObjectInfo, snapshot))
                        {
                            bool previewImageNeedsCleanup;
                            var previewImage = TryObtainingPreview(foundObject, out previewImageNeedsCleanup);
                            if (previewImage == null)
                            {
                                previewImage = search[0].GetPreview(context, new Vector2(500, 500));
                                previewImageNeedsCleanup = previewImage == null || !previewImage.hideFlags.HasFlag(HideFlags.NotEditable);
                            }
                            return new Findings() {
                                FoundObject = foundObject, DegreeOfCertainty = 80,
                                PreviewImage = previewImage, PreviewImageNeedsCleanup = previewImageNeedsCleanup
                            };
                        }
                    }
                }
            }
            return new Findings() { FailReason = SearchFailReason.NotFound };
        }

#else

        static Findings FindSceneObject(CachedSnapshot snapshot, UnifiedUnityObjectInfo unifiedUnityObjectInfo)
        {
            if (snapshot.HasSceneRootsAndAssetbundles)
            {
                // TODO: with captured Scene Roots changes, double check the open scene names
                // if they mismatch, return;
            }
            var assetType = unifiedUnityObjectInfo.Type.GetManagedSystemType(snapshot);
            if (assetType != null)
            {
#if UNITY_2020_1_OR_NEWER
                var objs = Object.FindObjectsOfType(assetType, true);
#else
                var objs = Object.FindObjectsOfType(assetType);
#endif

                if (objs.Length > 10000) // nope, that's gonna take too long
                    return new Findings() { FailReason = SearchFailReason.FoundTooManyToProcess };

                Object foundCandidate = null;
                foreach (var obj in objs)
                {
                    if (obj.name == unifiedUnityObjectInfo.NativeObjectName)
                    {
                        // TODO: Also check components on the same object and surrounding transforms for added certainty
                        if (foundCandidate != null)
                            return new Findings() { FailReason = SearchFailReason.FoundTooMany };
                        foundCandidate = obj;
                    }
                }
                if (foundCandidate)
                {
                    bool previewImageNeedsCleanup;
                    var previewImage = TryObtainingPreview(foundCandidate, out previewImageNeedsCleanup);
                    return new Findings() { FoundObject = foundCandidate, DegreeOfCertainty = 70, PreviewImage = previewImage, PreviewImageNeedsCleanup = previewImageNeedsCleanup};
                }
            }
            return new Findings() { FailReason = SearchFailReason.NotFound };
        }

#endif

        static Texture TryObtainingPreview(Object obj, out bool previewImageNeedsCleanup)
        {
            if (obj is Texture2D || obj is RenderTexture)
            {
                previewImageNeedsCleanup = false;
                if (obj is RenderTexture && (obj as RenderTexture).antiAliasing > 1)
                    return null;
                return obj as Texture;
            }
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
            previewImageNeedsCleanup = preview == null || !preview.hideFlags.HasFlag(HideFlags.NotEditable);
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

        static string ConstructSearchString(UnifiedUnityObjectInfo unifiedUnityObjectInfo, bool quickSearchSceneObjectSearch = false)
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
            if (selectedUnityObject.IsAsset)
                return TextContent.SearchInProjectButton;
#if UNITY_2021_2_OR_NEWER // TODO: || QUICK_SEARCH_AVAILABLE ?
            return new GUIContent("Search in Editor");
#else
            return null;
#endif
        }

        public static void SetEditorSearchFilterForObject(CachedSnapshot snapshot, UnifiedUnityObjectInfo selectedUnityObject)
        {
            if (selectedUnityObject.IsSceneObject)
            {
                SetSceneSearch(ConstructSearchString(selectedUnityObject));
            }
            if (selectedUnityObject.IsAsset)
            {
                SetProjectSearch(ConstructSearchString(selectedUnityObject));
                //EditorWindow.GetWindow<ProjectBrowser>
            }
        }

        public static void OpenQuickSearch(CachedSnapshot snapshot, UnifiedUnityObjectInfo selectedUnityObject)
        {
#if UNITY_2021_2_OR_NEWER // TODO: || QUICK_SEARCH_AVAILABLE ?
            // possible fall-back if Case 1400665 is never backported to 2021.1, or if we need something like this in earlier Unity versions with the package.
            //var providerIds = selectedUnityObject.IsSceneObject? new[]{ k_SearchProviderIdScene } : new[]{ k_SearchProviderIdAsset, k_SearchProviderIdAssetDatabase };

            var context = SearchService.CreateContext(/*providerIds: providerIds,*/ searchText: ConstructSearchString(selectedUnityObject, selectedUnityObject.IsSceneObject));
            var state = new SearchViewState(context);
            // Will only work once Case 1400665 is resolved and this trunk change-set landed: 25685e01ef1d
            state.group = selectedUnityObject.IsSceneObject ? "scene" : "asset";
            SearchService.ShowWindow(state);
#endif
        }
    }
}

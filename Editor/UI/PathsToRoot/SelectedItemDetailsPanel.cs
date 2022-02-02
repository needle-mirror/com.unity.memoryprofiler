using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.Database;
using Unity.MemoryProfiler.Editor.UI.PathsToRoot;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    [Flags]
    internal enum SelectedItemDynamicElementOptions
    {
        PlaceFirstInGroup = 1 << 0,
        SelectableLabel = 1 << 1,
        ShowTitle = 1 << 2,
    }
    internal interface ISelectedItemDetailsUI
    {
        VisualElement Root { get; }
        void SetItemName(string name);
        void SetItemName(ObjectData pureCSharpObject, UnifiedType typeInfo);
        void SetItemName(UnifiedType typeInfo);
        void SetItemName(UnifiedUnityObjectInfo unityObjectInfo);
        void SetDescription(string description);
        void AddDynamicElement(string title, string content);
        void AddDynamicElement(string groupName, string title, string content, string tooltip = null, SelectedItemDynamicElementOptions options = SelectedItemDynamicElementOptions.ShowTitle | SelectedItemDynamicElementOptions.SelectableLabel);
        void SetDocumentationURL(string url);
        void SetManagedObjectInspector(ObjectData objectInfo, int indexOfInspector = 0);
    }

    internal class SelectedItemDetailsPanel : ISelectedItemDetailsUI
    {
        static class Styles
        {
            public static readonly GUIStyle LinkTextLabel = new GUIStyle(EditorStyles.label);
            static Styles()
            {
                LinkTextLabel.richText = true;
            }
        }

        bool m_ShowDebugInfo = false;
        public bool ShowDebugInfo
        {
            get { return m_ShowDebugInfo; }
            set
            {
                if (value != m_ShowDebugInfo)
                {
                    m_ShowDebugInfo = value;
                    NewDetailItem(m_CurrentMemorySampleSelection);
                }
            }
        }

        List<ManagedObjectInspector> m_ManagedObjectInspectors = new List<ManagedObjectInspector>();
        List<VisualElement> m_ManagedObjectInspectorContainors = new List<VisualElement>();

        CachedSnapshot m_CachedSnapshot;
        MemorySampleSelection m_CurrentMemorySampleSelection;
        UnifiedUnityObjectInfo m_SelectedUnityObject;
        IUIStateHolder m_UiStateHolder;

        Label m_Title;
        ObjectOrTypeLabel m_UnityObjectTitle;

        VisualElement m_FindButtonsHolder;
        VisualElement m_SelectInEditorButtonHolder;
        VisualElement m_SearchInEditorButtonHolder;
        Button m_SelectInEditorButton;
        UnityEngine.Object m_FoundObjectInEditor;
        Button m_SearchInEditorButton;
        Button m_QuickSearchButton;

        TextField m_Description;
        Button m_DocumentationButton;
        string m_DocumentationURL = null;
        VisualElement m_DynamicElements;
        public VisualElement Root { get => m_DynamicElements; }

        VisualElement m_GroupedElements;
        VisualTreeAsset m_SelectedItemDetailsGroupUxmlPathViewTree;
        VisualTreeAsset m_SelectedItemDetailsGroupedItemUxmlPathViewTree;

        VisualElement m_PreviewFoldoutheader;
        Image m_Preview;
        bool m_PreviewNeedsCleaningUp;

        List<DetailsGroup> m_DetailsGroups = new List<DetailsGroup>();
        Dictionary<string, DetailsGroup> m_DetailsGroupsByGroupName = new Dictionary<string, DetailsGroup>();
        Dictionary<string, DetailsGroup> m_ActiveDetailsGroupsByGroupName = new Dictionary<string, DetailsGroup>();
        struct DetailsGroup
        {
            public VisualElement Root => Foldout;
            public Foldout Foldout;
            public VisualElement Content;

            public void Clear()
            {
                Content.Clear();
            }
        }
        public const string GroupNameBasic = "Basic";
        public const string GroupNameHelp = "Help";
        public const string GroupNameAdvanced = "Advanced";
        public const string GroupNameDebug = "Debug"; // Only useful for Memory Profiler developers or _maybe_ for bug reports


        public SelectedItemDetailsPanel(IUIStateHolder uiStateHolder, VisualElement detailsPanelRoot)
        {
            m_UiStateHolder = uiStateHolder;
            m_ManagedObjectInspectors.Add(new ManagedObjectInspector(uiStateHolder, 0, new TreeViewState(), new MultiColumnHeaderWithTruncateTypeName(ManagedObjectInspector.CreateDefaultMultiColumnHeaderState())));
            var managedFieldInspectorFoldout = detailsPanelRoot.Q<Foldout>("selected-item-details__managed-field-inspector__foldout");
            var detailsContainer = managedFieldInspectorFoldout.Q<IMGUIContainer>("selected-item-details__managed-field-inspector__imguicontainer");
            detailsContainer.onGUIHandler += () =>
            {
                detailsContainer.style.minHeight = m_ManagedObjectInspectors[0].totalHeight;
                m_ManagedObjectInspectors[0].DoGUI(detailsContainer.contentRect);
            };
            m_ManagedObjectInspectorContainors.Add(managedFieldInspectorFoldout);

            UIElementsHelper.SetVisibility(m_ManagedObjectInspectorContainors[0], false);

            m_Title = detailsPanelRoot.Q<Label>("selected-item-details__item-title");
            m_UnityObjectTitle = detailsPanelRoot.Q<ObjectOrTypeLabel>("selected-item-details__unity-item-title");
            NoItemSelected();

            m_Description = detailsPanelRoot.Q<TextField>("selected-item-details__item-description");
            m_Description.isReadOnly = true;
            UIElementsHelper.SetVisibility(m_Description, false);

            m_DocumentationButton = detailsPanelRoot.Q<Button>("selected-item-details__item-documentation-button");
            m_DocumentationButton.clickable.clicked += OpenDocumentation;
            m_DocumentationButton.tooltip = UIContentData.TextContent.OpenManualTooltip;
            m_DynamicElements = detailsPanelRoot.Q("selected-item-details__dynamic-elements");
            UIElementsHelper.SetVisibility(m_DocumentationButton, false);

            m_FindButtonsHolder = detailsPanelRoot.Q("selected-item-details__find-buttons");
            m_SelectInEditorButtonHolder = m_FindButtonsHolder.Q("selected-item-details__find-buttons__select-in-editor__holder");
            m_SearchInEditorButtonHolder = m_FindButtonsHolder.Q("selected-item-details__find-buttons__search-in-editor__holder");
            m_SelectInEditorButton = detailsPanelRoot.Q<Button>("selected-item-details__find-buttons__select-in-editor");
            m_SelectInEditorButton.clicked += OnSelectInEditor;
            m_SearchInEditorButton = detailsPanelRoot.Q<Button>("selected-item-details__find-buttons__search-in-editor");
            m_SearchInEditorButton.clicked += OnSearchEditor;

            m_QuickSearchButton = detailsPanelRoot.Q<Button>("selected-item-details__find-buttons__quick-search");
            m_QuickSearchButton.clicked += OnQuickSearch;

            UIElementsHelper.SetVisibility(m_SelectInEditorButton, false);
            UIElementsHelper.SetVisibility(m_SearchInEditorButton, false);
            UIElementsHelper.SetVisibility(m_QuickSearchButton, false);

            m_PreviewFoldoutheader = detailsPanelRoot.Q("selected-item-details__preview-area");
            m_Preview = m_PreviewFoldoutheader.Q<Image>("selected-item-details__preview");
            UIElementsHelper.SetVisibility(m_PreviewFoldoutheader, false);

            m_GroupedElements = detailsPanelRoot.Q("selected-item-details__grouped-elements");
            m_SelectedItemDetailsGroupUxmlPathViewTree = EditorGUIUtility.Load(ResourcePaths.SelectedItemDetailsGroupUxmlPath) as VisualTreeAsset;
            CreateDetailsGroup(GroupNameBasic);
            CreateDetailsGroup(GroupNameHelp);
            CreateDetailsGroup(GroupNameAdvanced).Foldout.value = false;
            CreateDetailsGroup(GroupNameDebug).Foldout.value = false;
            m_SelectedItemDetailsGroupedItemUxmlPathViewTree = EditorGUIUtility.Load(ResourcePaths.SelectedItemDetailsGroupedItemUxmlPath) as VisualTreeAsset;
        }

        DetailsGroup CreateDetailsGroup(string name)
        {
            var item = m_SelectedItemDetailsGroupUxmlPathViewTree.Clone();
            item.style.flexGrow = 1;
            var groupData = new DetailsGroup()
            {
                Foldout = item.Q<Foldout>("selected-item-details__group__header-foldout"),
                Content = item.Q("selected-item-details__group__content"),
            };
            groupData.Foldout.text = name;
            m_DetailsGroups.Add(groupData);
            m_DetailsGroupsByGroupName.Add(name, groupData);
            m_GroupedElements.Add(groupData.Root);
            UIElementsHelper.SetVisibility(groupData.Root, false);
            return groupData;
        }

        void OpenDocumentation()
        {
            Application.OpenURL(m_DocumentationURL);
        }

        void OnSelectInEditor()
        {
            if (m_FoundObjectInEditor != null)
            {
                EditorAssetFinderUtility.SelectObject(m_FoundObjectInEditor);
            }
        }

        void OnSearchEditor()
        {
            if (m_SelectedUnityObject.IsValid)
            {
                EditorAssetFinderUtility.SearchForObject(m_CachedSnapshot, m_SelectedUnityObject);
            }
        }

        void OnQuickSearch()
        {
            if (m_SelectedUnityObject.IsValid)
            {
                EditorAssetFinderUtility.OpenQuickSearch(m_CachedSnapshot, m_SelectedUnityObject);
            }
        }

        void NoItemSelected()
        {
            UIElementsHelper.SwitchVisibility(m_Title, m_UnityObjectTitle, true);
            m_Title.text = "No Item Selected";
        }

        internal void NewDetailItem(MemorySampleSelection memorySampleSelection)
        {
            var preciousSelectionType = m_CurrentMemorySampleSelection.Type;
            if (!memorySampleSelection.Valid)
            {
                m_CachedSnapshot = null;
                Clear();
                if (preciousSelectionType != MemorySampleSelectionType.None)
                    m_UiStateHolder.UIState.CustomSelectionDetailsFactory.Clear(preciousSelectionType, this);
                return;
            }

            m_CachedSnapshot = memorySampleSelection.GetSnapshotItemIsPresentIn(m_UiStateHolder.UIState);

            m_CurrentMemorySampleSelection = MemorySampleSelection.InvalidMainSelection;
            Clear();
            if (preciousSelectionType != MemorySampleSelectionType.None)
                m_UiStateHolder.UIState.CustomSelectionDetailsFactory.Clear(preciousSelectionType, this);

            if (m_UiStateHolder.UIState.CustomSelectionDetailsFactory.Produce(memorySampleSelection, this))
            {
                m_CurrentMemorySampleSelection = memorySampleSelection;
                return;
            }
            // This Selection type is apparently not yet implemented
            m_CurrentMemorySampleSelection = MemorySampleSelection.InvalidMainSelection;
            return;
        }

        public void AddDynamicElement(string title, string content)
        {
            var label = new Label($"{title} : {content}");
            label.AddToClassList("selected-item-details__dynamic-label");
            m_DynamicElements.Add(label);
        }

        public void AddDynamicElement(string groupName, string title, string content, string tooltip = null, SelectedItemDynamicElementOptions options = SelectedItemDynamicElementOptions.ShowTitle | SelectedItemDynamicElementOptions.SelectableLabel)
        {
            if (groupName == GroupNameDebug && !ShowDebugInfo)
                return;

            DetailsGroup groupItem;
            if (!m_ActiveDetailsGroupsByGroupName.TryGetValue(groupName, out groupItem))
            {
                if (!m_DetailsGroupsByGroupName.TryGetValue(groupName, out groupItem))
                {
                    groupItem = CreateDetailsGroup(groupName);
                }
                UIElementsHelper.SetVisibility(groupItem.Root, true);
                m_ActiveDetailsGroupsByGroupName.Add(groupName, groupItem);
            }
            var groupedItem = m_SelectedItemDetailsGroupedItemUxmlPathViewTree.Clone();
            if (options.HasFlag(SelectedItemDynamicElementOptions.PlaceFirstInGroup))
                groupItem.Content.Insert(0, groupedItem);
            else
                groupItem.Content.Add(groupedItem);
            var titleLabel = groupedItem.Q<Label>("selected-item-details__grouped-item__label");
            var contentLabel = groupedItem.Q<Label>("selected-item-details__grouped-item__content");
            var selectableLabel = groupedItem.Q<TextField>("selected-item-details__grouped-item__content_selectable-label");
            UIElementsHelper.SwitchVisibility(selectableLabel, contentLabel, options.HasFlag(SelectedItemDynamicElementOptions.SelectableLabel));
            if (options.HasFlag(SelectedItemDynamicElementOptions.ShowTitle))
                titleLabel.text = $"{title} :";
            else
            {
                UIElementsHelper.SetVisibility(titleLabel, false);
            }
            if (options.HasFlag(SelectedItemDynamicElementOptions.SelectableLabel))
            {
                selectableLabel.SetValueWithoutNotify(content);
                if (!options.HasFlag(SelectedItemDynamicElementOptions.ShowTitle))
                    selectableLabel.AddToClassList("selected-item-details__grouped-item__content-without-label");
            }
            else
            {
                contentLabel.text = content;
                if (!options.HasFlag(SelectedItemDynamicElementOptions.ShowTitle))
                    selectableLabel.AddToClassList("selected-item-details__grouped-item__content-without-label");
            }

            if (!string.IsNullOrEmpty(tooltip))
                groupedItem.tooltip = tooltip;
        }

        public void SetItemName(string name)
        {
            m_Title.text = name;
            UIElementsHelper.SwitchVisibility(m_Title, m_UnityObjectTitle, true);
        }

        public void SetItemName(ObjectData pureCSharpObject, UnifiedType typeInfo)
        {
            UIElementsHelper.SwitchVisibility(m_Title, m_UnityObjectTitle, false);
            m_UnityObjectTitle.SetLabelData(m_CachedSnapshot, pureCSharpObject, typeInfo);
        }

        public void SetItemName(UnifiedType typeInfo)
        {
            m_UnityObjectTitle.SetLabelData(m_CachedSnapshot, typeInfo);
            UIElementsHelper.SwitchVisibility(m_Title, m_UnityObjectTitle, false);
        }

        public void SetItemName(UnifiedUnityObjectInfo unityObjectInfo)
        {
            m_SelectedUnityObject = unityObjectInfo;
            var findings = EditorAssetFinderUtility.FindObject(m_CachedSnapshot, unityObjectInfo);
            m_FoundObjectInEditor = findings.FoundObject;

            var searchButtonLabel = EditorAssetFinderUtility.GetSearchButtonLabel(m_CachedSnapshot, unityObjectInfo);
            var canSearch = searchButtonLabel != null;
            if (canSearch)
            {
                m_SearchInEditorButton.text = searchButtonLabel.text;
                m_SearchInEditorButton.tooltip = searchButtonLabel.tooltip;
                UIElementsHelper.SetVisibility(m_SearchInEditorButton, true);
#if UNITY_2021_1_OR_NEWER || QUICK_SEARCH_AVAILABLE
                UIElementsHelper.SetVisibility(m_QuickSearchButton, true);
                m_SearchInEditorButton.SetEnabled(true);
#endif
                m_SearchInEditorButton.SetEnabled(true);
            }
            else
            {
                m_SearchInEditorButton.text = TextContent.SearchButtonCantSearch.text;
                UIElementsHelper.SetVisibility(m_SearchInEditorButton, true);
                m_SearchInEditorButton.SetEnabled(false);
#if UNITY_2021_1_OR_NEWER || QUICK_SEARCH_AVAILABLE
                UIElementsHelper.SetVisibility(m_QuickSearchButton, true);
                m_SearchInEditorButton.SetEnabled(false);
#endif
                // not all supported Unity versions show tool-tips for disabled controls yet. m_UI.Setting the tool-tip on the enclosing UI Element works as a workaround.
                m_SearchInEditorButtonHolder.tooltip = TextContent.SearchButtonCantSearch.tooltip;
            }

            if (m_FoundObjectInEditor != null)
            {
                UIElementsHelper.SetVisibility(m_SelectInEditorButton, true);
                if (findings.PreviewImage)
                {
                    UIElementsHelper.SetVisibility(m_PreviewFoldoutheader, true);
                    m_Preview.image = findings.PreviewImage;
                    m_PreviewNeedsCleaningUp = findings.PreviewImageNeedsCleanup;
                }
                m_SelectInEditorButton.SetEnabled(true);
                m_SelectInEditorButton.tooltip = (findings.DegreeOfCertainty < 100 ? TextContent.SelectInEditorButtonLessCertain : TextContent.SelectInEditorButton100PercentCertain).tooltip;
            }
            else
            {
                UIElementsHelper.SetVisibility(m_SelectInEditorButton, true);

                m_SelectInEditorButton.SetEnabled(false);
                switch (findings.FailReason)
                {
                    case EditorAssetFinderUtility.SearchFailReason.NotFound:
                        m_SelectInEditorButtonHolder.tooltip = TextContent.SelectInEditorButtonNotFound.tooltip;
                        break;
                    case EditorAssetFinderUtility.SearchFailReason.FoundTooMany:
                        m_SelectInEditorButtonHolder.tooltip = TextContent.SelectInEditorButtonFoundTooMany.tooltip +
                            (canSearch ? TextContent.SelectInEditorTooltipCanSearch : string.Empty);
                        break;
                    case EditorAssetFinderUtility.SearchFailReason.FoundTooManyToProcess:
                        m_SelectInEditorButtonHolder.tooltip = TextContent.SelectInEditorButtonFoundTooManyToProcess.tooltip +
                            (canSearch ? TextContent.SelectInEditorTooltipCanSearch : string.Empty);
                        break;
                    case EditorAssetFinderUtility.SearchFailReason.TypeIssues:
                        m_SelectInEditorButtonHolder.tooltip = TextContent.SelectInEditorButtonTypeMissmatch +
                            (canSearch ? TextContent.SelectInEditorTooltipTrySearch : string.Empty);
                        break;
                    case EditorAssetFinderUtility.SearchFailReason.Found:
                    default:
                        break;
                }
            }
            m_UnityObjectTitle.SetLabelData(m_CachedSnapshot, unityObjectInfo);
            UIElementsHelper.SwitchVisibility(m_Title, m_UnityObjectTitle, false);
        }

        public void SetDescription(string description)
        {
            if (!string.IsNullOrEmpty(description))
                AddDynamicElement(GroupNameHelp, "Description", description, tooltip: description, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel);
            // TODO: To be cleaned up as Selection Details stabilizes Keeping this for now in case we still want to use the description field for something.
            // UIElementsHelper.SetVisibility(m_Description, !string.IsNullOrEmpty(description))
            // m_Description.SetValueWithoutNotify(description);
        }

        public void SetDocumentationURL(string url)
        {
            m_DocumentationURL = url;
            UIElementsHelper.SetVisibility(m_DocumentationButton, !string.IsNullOrEmpty(url));
        }

        public void SetManagedObjectInspector(ObjectData objectInfo, int indexOfInspector = 0)
        {
            UIElementsHelper.SetVisibility(m_ManagedObjectInspectorContainors[indexOfInspector], true);
            m_ManagedObjectInspectors[0].SetupManagedObject(m_CachedSnapshot, objectInfo);
        }

        public void ManagedInspectorLinkWasClicked(int inspectorId, int treeViewId)
        {
            if (inspectorId >= 0 && inspectorId < m_ManagedObjectInspectors.Count)
            {
                m_ManagedObjectInspectors[inspectorId].LinkWasClicked(treeViewId);
            }
        }

        public void OnDisable()
        {
            foreach (var inspector in m_ManagedObjectInspectors)
            {
                inspector.OnDisable();
            }
            m_ManagedObjectInspectors.Clear();
            m_UnityObjectTitle.Dispose();
        }

        void Clear()
        {
            m_SelectedUnityObject = default;
            m_ManagedObjectInspectors[0].Clear();
            UIElementsHelper.SetVisibility(m_ManagedObjectInspectorContainors[0], false);
            m_DynamicElements.Clear();
            m_FoundObjectInEditor = null;
            m_SearchInEditorButton.tooltip = null;
            m_SelectInEditorButtonHolder.tooltip = null;
            m_SearchInEditorButtonHolder.tooltip = null;
            m_SelectInEditorButton.tooltip = null;
            UIElementsHelper.SetVisibility(m_SelectInEditorButton, false);
            UIElementsHelper.SetVisibility(m_SearchInEditorButton, false);
            UIElementsHelper.SetVisibility(m_QuickSearchButton, false);
            foreach (var item in m_GroupedElements.Children())
            {
                UIElementsHelper.SetVisibility(item, false);
            }
            foreach (var item in m_DetailsGroups)
            {
                item.Clear();
            }
            m_ActiveDetailsGroupsByGroupName.Clear();

            UIElementsHelper.SetVisibility(m_PreviewFoldoutheader, false);
            if (m_Preview.image && m_PreviewNeedsCleaningUp && !m_Preview.image.hideFlags.HasFlag(HideFlags.NotEditable))
            {
                //if(m_Preview.image.hideFlags == HideFlags.)
                UnityEngine.Object.DestroyImmediate(m_Preview.image);
            }
            m_Preview.image = null;
            NoItemSelected();
            SetDescription("");
            SetDocumentationURL(null);
        }
    }
}

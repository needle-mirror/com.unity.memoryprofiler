using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.UI.PathsToRoot;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine.UIElements.Experimental;
using UnityEngine.UIElements;
using UnityEngine;
using System.Threading;
using System.Threading.Tasks;


#if INSTANCE_ID_CHANGED
using TreeViewState = UnityEditor.IMGUI.Controls.TreeViewState<int>;
#else
using UnityEditor.IMGUI.Controls;
#endif

namespace Unity.MemoryProfiler.Editor.UI
{
    [Flags]
    internal enum SelectedItemDynamicElementOptions
    {
        PlaceFirstInGroup = 1 << 0,
        SelectableLabel = 1 << 1,
        ShowTitle = 1 << 2,
        EnableRichText = 1 << 3,
        Button = 1 << 5,
        Toggle = 1 << 6,
        ToggleOn = 1 << 7,
        SubFoldout = 1 << 8,
    }
    internal class SelectedItemDetailsPanel : IDisposable
    {
        const string k_UxmlAssetGuidSelectedItemDetailsGroup = "7b1ac4070dcbad341a293530f4f8e3d4";
        const string k_UxmlAssetGuidSelectedItemDetailsGroupedItem = "37daf4bdc1779c949b7295ac03f33c74";

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
                }
            }
        }

        List<ManagedObjectInspector> m_ManagedObjectInspectors = new List<ManagedObjectInspector>();
        List<VisualElement> m_ManagedObjectInspectorContainers = new List<VisualElement>();

        CachedSnapshot m_CachedSnapshot;
        UnifiedUnityObjectInfo m_SelectedUnityObject;

        Label m_Title;
        ObjectOrTypeLabel m_UnityObjectTitle;
        bool m_NonObjectTitle = true;
        bool NonObjectTitleShown
        {
            set
            {
                m_NonObjectTitle = value;
                UIElementsHelper.SwitchVisibility(m_Title, m_UnityObjectTitle, m_NonObjectTitle);
            }
        }

        VisualElement m_FindButtonsHolder;
        VisualElement m_SelectInEditorButtonHolder;
        VisualElement m_SearchInEditorButtonHolder;
        Button m_SelectInEditorButton;
        UnityEngine.Object m_FoundObjectInEditor;
        bool m_CanSearch;
        Button m_SearchInEditorButton;
        Button m_QuickSearchButton;

        CancellationTokenSource m_SearchCancellationTokenSource;

        // This button is hidden for the moment as
        // - It may cause confusion that it would copy the entire details section
        // - Will make it harder to discover new options added later, e.g. those to copy the entire details section
        // - The menu content might change from single items to flags in the final version.
        static readonly bool k_FeatureFlagCopyButtonActive = false;
        Button m_CopyButton;
        Button m_CopyButtonDropdown;
        public enum CopyDetailsOption
        {
            FullTitle,
            NativeObjectName,
            ManagedTypeName,
            NativeTypeName,
            // To be implemented:
            //ReferencesAsText,
            //ReferencesAsCSV,
            //SelectedItemDetailsAsText,
            //SelectedItemDetailsAsCSV,
            //FullDetailsAsText,
            //FullDetailsAsCSV,
        }

        public static string[] CopyDetailsOptionText = new string[]
        {
            TextContent.CopyButtonDropdownOptionFullTitle,
            TextContent.CopyButtonDropdownOptionObjectName,
            TextContent.CopyButtonDropdownOptionManagedTypeName,
            TextContent.CopyButtonDropdownOptionNativeTypeName,
        };

        public static GUIContent[] CopyDetailsOptionLabels = new GUIContent[]
        {
            new GUIContent(CopyDetailsOptionText[(int)CopyDetailsOption.FullTitle]),
            new GUIContent(CopyDetailsOptionText[(int)CopyDetailsOption.NativeObjectName]),
            new GUIContent(CopyDetailsOptionText[(int)CopyDetailsOption.ManagedTypeName]),
            new GUIContent(CopyDetailsOptionText[(int)CopyDetailsOption.NativeTypeName]),
        };

        TextField m_Description;
        Button m_DocumentationButton;
        string m_DocumentationURL = null;
        VisualElement m_DynamicElements;
        public VisualElement Root { get => m_DynamicElements; }

        VisualElement m_GroupedElements;
        VisualTreeAsset m_SelectedItemDetailsGroupUxmlPathViewTree;
        VisualTreeAsset m_SelectedItemDetailsGroupedItemUxmlPathViewTree;
        const string m_SelectedItemDetailsGroupedItemContainerElementName = "selected-item-details__grouped-item__container";

        VisualElement m_PreviewFoldoutHeader;
        Foldout m_PreviewFoldout;
        Image m_Preview;
        EditorAssetFinderUtility.PreviewImageResult m_PreviewImageResult;

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
                var foldoutState = Foldout.value;
                Content.Clear();

                var foldoutSessionStateKey = SelectedItemDetailsPanel.GetFoldoutSessionStateKey(Foldout.text);
                SessionState.SetBool(foldoutSessionStateKey, foldoutState);
            }
        }
        public const string GroupNameBasic = "Basic";
        public const string GroupNameMetaData = "MetaData";
        public const string GroupNameHelp = "Help";
        public const string GroupNameAdvanced = "Advanced";
        public const string GroupNameDebug = "Debug"; // Only useful for Memory Profiler developers or _maybe_ for bug reports
        public const string GroupNameCallStacks = "Call Stack Info"; // Only useful for captures from Players and Editors build from source code with RECORD_ALLOCATION_SITES and ENABLE_STACKS_ON_ALL_ALLOCS defined


        public SelectedItemDetailsPanel(CachedSnapshot cachedSnapshot, VisualElement detailsPanelRoot)
        {
            m_CachedSnapshot = cachedSnapshot;

            m_ManagedObjectInspectors.Add(new ManagedObjectInspector(0, new TreeViewState(), new MultiColumnHeaderWithTruncateTypeName(ManagedObjectInspector.CreateDefaultMultiColumnHeaderState())));
            var managedFieldInspectorFoldout = detailsPanelRoot.Q<Foldout>("selected-item-details__managed-field-inspector__foldout");
            var detailsContainer = managedFieldInspectorFoldout.Q<IMGUIContainer>("selected-item-details__managed-field-inspector__imguicontainer");
            detailsContainer.onGUIHandler += () =>
            {
                detailsContainer.style.minHeight = m_ManagedObjectInspectors[0].totalHeight;
                m_ManagedObjectInspectors[0].DoGUI(detailsContainer.contentRect);
            };
            m_ManagedObjectInspectorContainers.Add(managedFieldInspectorFoldout);

            var managedFieldInspectorFoldoutSessionStateKey = GetFoldoutSessionStateKey(managedFieldInspectorFoldout.text);
            managedFieldInspectorFoldout.SetValueWithoutNotify(SessionState.GetBool(managedFieldInspectorFoldoutSessionStateKey, true));
            managedFieldInspectorFoldout.RegisterValueChangedCallback((evt) =>
            {
                SessionState.SetBool(managedFieldInspectorFoldoutSessionStateKey, evt.newValue);
            });

            UIElementsHelper.SetVisibility(m_ManagedObjectInspectorContainers[0], false);

            m_Title = detailsPanelRoot.Q<Label>("selected-item-details__item-title");
            m_Title.AddManipulator(new ContextualMenuManipulator(evt => ShowCopyMenu(evt, contextMenu: true)));
            m_UnityObjectTitle = detailsPanelRoot.Q<ObjectOrTypeLabel>("selected-item-details__unity-item-title");
            m_UnityObjectTitle.ContextMenuOpening += evt => ShowCopyMenu(evt, contextMenu: true);
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

            m_CopyButton = detailsPanelRoot.Q<Button>("selected-item-details__find-buttons__copy");
            m_CopyButton.clickable.clicked += () => CopySelectedItemTitel(MemoryProfilerSettings.DefaultCopyDetailsOption, contextMenu: false, saveAsDefault: false);
            m_CopyButtonDropdown = m_CopyButton.Q<Button>("drop-down-button__drop-down-part");
            m_CopyButtonDropdown.clickable.clicked += () => ShowCopyMenu();

            m_QuickSearchButton = detailsPanelRoot.Q<Button>("selected-item-details__find-buttons__quick-search");
            m_QuickSearchButton.clicked += OnQuickSearch;

            UIElementsHelper.SetVisibility(m_SelectInEditorButton, false);
            UIElementsHelper.SetVisibility(m_SearchInEditorButton, false);
            UIElementsHelper.SetVisibility(m_QuickSearchButton, false);
            UIElementsHelper.SetVisibility(m_CopyButton, false);

            m_PreviewFoldoutHeader = detailsPanelRoot.Q("selected-item-details__preview-area");
            m_Preview = m_PreviewFoldoutHeader.Q<Image>("selected-item-details__preview");
            UIElementsHelper.SetVisibility(m_PreviewFoldoutHeader, false);

            m_PreviewFoldout = m_PreviewFoldoutHeader.Q<Foldout>();
            var previewFoldoutSessionStateKey = GetFoldoutSessionStateKey(m_PreviewFoldout.text);
            m_PreviewFoldout.SetValueWithoutNotify(SessionState.GetBool(previewFoldoutSessionStateKey, true));
            m_PreviewFoldout.RegisterValueChangedCallback((evt) =>
            {
                SessionState.SetBool(previewFoldoutSessionStateKey, evt.newValue);
            });

            m_GroupedElements = detailsPanelRoot.Q("selected-item-details__grouped-elements");
            m_SelectedItemDetailsGroupUxmlPathViewTree = UIElementsHelper.LoadAssetByGUID(k_UxmlAssetGuidSelectedItemDetailsGroup);
            CreateDetailsGroup(GroupNameBasic);
            CreateDetailsGroup(GroupNameMetaData);
            CreateDetailsGroup(GroupNameHelp);
            CreateDetailsGroup(GroupNameAdvanced, false);
            CreateDetailsGroup(GroupNameDebug, false);
            if (cachedSnapshot.NativeCallstackSymbols.Count > 0)
                CreateDetailsGroup(GroupNameCallStacks);
            m_SelectedItemDetailsGroupedItemUxmlPathViewTree = UIElementsHelper.LoadAssetByGUID(k_UxmlAssetGuidSelectedItemDetailsGroupedItem);
        }

        // UI TK version
        public void ShowCopyMenu(ContextualMenuPopulateEvent evt, bool contextMenu = true)
        {
            if (m_NonObjectTitle)
            {
                AddCopyOption(evt.menu, CopyDetailsOption.FullTitle, contextMenu: contextMenu, saveAsDefault: false);
                AddCopyOption(evt.menu, CopyDetailsOption.NativeObjectName, contextMenu: contextMenu, disabled: true);
                AddCopyOption(evt.menu, CopyDetailsOption.ManagedTypeName, contextMenu: contextMenu, disabled: true);
                AddCopyOption(evt.menu, CopyDetailsOption.NativeTypeName, contextMenu: contextMenu, disabled: true);
            }
            else
            {
                AddCopyOption(evt.menu, CopyDetailsOption.FullTitle, contextMenu: contextMenu);
                AddCopyOption(evt.menu, CopyDetailsOption.NativeObjectName, disabled: string.IsNullOrEmpty(m_UnityObjectTitle.NativeObjectName), contextMenu: contextMenu);
                AddCopyOption(evt.menu, CopyDetailsOption.ManagedTypeName, disabled: string.IsNullOrEmpty(m_UnityObjectTitle.ManagedTypeName), contextMenu: contextMenu);
                AddCopyOption(evt.menu, CopyDetailsOption.NativeTypeName, disabled: string.IsNullOrEmpty(m_UnityObjectTitle.NativeTypeName), contextMenu: contextMenu);
            }
        }

        void AddCopyOption(DropdownMenu menu, CopyDetailsOption option, bool contextMenu = false, bool saveAsDefault = true, bool disabled = false)
        {
            // the context menu ignores the default option
            AddCopyOption(menu, option, contextMenu, saveAsDefault,
                disabled, contextMenu || disabled ? false : MemoryProfilerSettings.DefaultCopyDetailsOption == option);
        }

        void AddCopyOption(DropdownMenu menu, CopyDetailsOption option, bool contextMenu, bool saveAsDefault, bool disabled, bool selected)
        {
            menu.AppendAction(
                contextMenu ? string.Format(TextContent.CopyTitleToClipboardContextClickItem, CopyDetailsOptionText[(int)option])
                : CopyDetailsOptionLabels[(int)option].text, (a) =>
                {
                    CopySelectedItemTitel(option, contextMenu, saveAsDefault);
                }, disabled ? DropdownMenuAction.Status.Disabled : selected ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
        }

        // IMGUI version
        void ShowCopyMenu()
        {
            var rect = m_CopyButtonDropdown.GetRect();
            var menu = new GenericMenu();
            if (m_NonObjectTitle)
            {
                AddCopyOption(menu, CopyDetailsOption.FullTitle, saveAsDefault: false);
                AddCopyOption(menu, CopyDetailsOption.NativeObjectName, disabled: true);
                AddCopyOption(menu, CopyDetailsOption.ManagedTypeName, disabled: true);
                AddCopyOption(menu, CopyDetailsOption.NativeTypeName, disabled: true);
            }
            else
            {
                AddCopyOption(menu, CopyDetailsOption.FullTitle);
                AddCopyOption(menu, CopyDetailsOption.NativeObjectName, disabled: string.IsNullOrEmpty(m_UnityObjectTitle.NativeObjectName));
                AddCopyOption(menu, CopyDetailsOption.ManagedTypeName, disabled: string.IsNullOrEmpty(m_UnityObjectTitle.ManagedTypeName));
                AddCopyOption(menu, CopyDetailsOption.NativeTypeName, disabled: string.IsNullOrEmpty(m_UnityObjectTitle.NativeTypeName));
            }
            menu.DropDown(rect);
        }

        void AddCopyOption(GenericMenu menu, CopyDetailsOption option, bool contextMenu = false, bool saveAsDefault = true, bool disabled = false)
        {
            // the context menu ignores the default option
            AddCopyOption(menu, option, contextMenu, saveAsDefault, disabled, contextMenu || disabled ? false : MemoryProfilerSettings.DefaultCopyDetailsOption == option);
        }

        void AddCopyOption(GenericMenu menu, CopyDetailsOption option, bool contextMenu, bool saveAsDefault, bool disabled, bool selected)
        {
            var content = contextMenu ? new GUIContent(string.Format(TextContent.CopyTitleToClipboardContextClickItem, CopyDetailsOptionText[(int)option]))
                : CopyDetailsOptionLabels[(int)option];
            if (disabled)
                menu.AddDisabledItem(content, selected);
            else
                menu.AddItem(content, selected, () => CopySelectedItemTitel(option, contextMenu, saveAsDefault));
        }

        void CopySelectedItemTitel(CopyDetailsOption option, bool contextMenu, bool saveAsDefault)
        {
            // the context menu doesn't safe a Default
            if (!contextMenu && saveAsDefault)
                MemoryProfilerSettings.DefaultCopyDetailsOption = option;
            if (m_NonObjectTitle)
                option = CopyDetailsOption.FullTitle;
            switch (option)
            {
                case CopyDetailsOption.FullTitle:
                    EditorGUIUtility.systemCopyBuffer = EditorGUIUtility.systemCopyBuffer =
                        m_NonObjectTitle ? m_Title.text : m_UnityObjectTitle.GetTitle(MemoryProfilerSettings.MemorySnapshotTruncateTypes);
                    break;
                case CopyDetailsOption.NativeObjectName:
                    EditorGUIUtility.systemCopyBuffer = EditorGUIUtility.systemCopyBuffer = m_UnityObjectTitle.NativeObjectName;
                    break;
                case CopyDetailsOption.ManagedTypeName:
                    EditorGUIUtility.systemCopyBuffer = EditorGUIUtility.systemCopyBuffer = m_UnityObjectTitle.ManagedTypeName;
                    break;
                case CopyDetailsOption.NativeTypeName:
                    EditorGUIUtility.systemCopyBuffer = EditorGUIUtility.systemCopyBuffer = m_UnityObjectTitle.NativeTypeName;
                    break;
                default:
                    break;
            }
        }

        static string GetFoldoutSessionStateKey(string foldoutName) => $"com.unity.memoryprofiler.{nameof(SelectedItemDetailsPanel)}.foldout.{foldoutName}";

        DetailsGroup CreateDetailsGroup(string name, bool expansionDefaultState = true)
        {
            var item = m_SelectedItemDetailsGroupUxmlPathViewTree.Clone();
            item.style.flexGrow = 1;
            var groupData = new DetailsGroup()
            {
                Foldout = item.Q<Foldout>("selected-item-details__group__header-foldout"),
                Content = item.Q("selected-item-details__group__content"),
            };
            var foldoutSessionStateKey = GetFoldoutSessionStateKey(name);
            groupData.Foldout.SetValueWithoutNotify(SessionState.GetBool(foldoutSessionStateKey, expansionDefaultState));
            groupData.Foldout.RegisterValueChangedCallback((evt) =>
            {
                SessionState.SetBool(foldoutSessionStateKey, evt.newValue);
            });
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
            // Track documentation open count in the details view
            MemoryProfilerAnalytics.AddSelectionDetailsViewInteraction(MemoryProfilerAnalytics.SelectionDetailsViewInteraction.DocumentationOpenCount);
        }

        void OnSelectInEditor()
        {
            if (m_FoundObjectInEditor != null)
            {
                EditorAssetFinderUtility.SelectObject(m_FoundObjectInEditor);

                // Track button click count
                MemoryProfilerAnalytics.AddSelectionDetailsViewInteraction(MemoryProfilerAnalytics.SelectionDetailsViewInteraction.SelectInEditorButtonClickCount);
            }
        }

        bool IsSceneObject(UnityEngine.Object obj)
        {
            return obj is GameObject || obj is Component;
        }

        void OnSearchEditor()
        {
            if (m_SelectedUnityObject.IsValid)
            {
                EditorAssetFinderUtility.SetEditorSearchFilterForObject(m_CachedSnapshot, m_SelectedUnityObject);

                // Track button click count
                MemoryProfilerAnalytics.AddSelectionDetailsViewInteraction(MemoryProfilerAnalytics.SelectionDetailsViewInteraction.SearchInEditorButtonClickCount);
            }
        }

        void OnQuickSearch()
        {
            if (m_SelectedUnityObject.IsValid)
            {
                EditorAssetFinderUtility.OpenQuickSearch(m_CachedSnapshot, m_SelectedUnityObject);

                // Track button click count
                MemoryProfilerAnalytics.AddSelectionDetailsViewInteraction(MemoryProfilerAnalytics.SelectionDetailsViewInteraction.QuickSearchButtonClickCount);
            }
        }

        void NoItemSelected()
        {
            UIElementsHelper.SetVisibility(m_CopyButton, false);
            NonObjectTitleShown = true;
            m_Title.text = "No details available";
        }

        public void AddDynamicElement(string title, string content)
        {
            var label = new Label($"{title}: {content}");
            label.AddToClassList("selected-item-details__dynamic-label");
            m_DynamicElements.Add(label);
        }

        public void AddInfoBox(string groupName, InfoBox infoBox)
        {
            if (groupName == GroupNameDebug && !ShowDebugInfo)
                return;

            var groupItem = AddGroupItem(groupName);
            groupItem.Content.Insert(0, infoBox);
        }

        DetailsGroup AddGroupItem(string groupName)
        {
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
            return groupItem;
        }

        public void AddDynamicElement(string groupName, string title, string content, string tooltip = null, SelectedItemDynamicElementOptions options = SelectedItemDynamicElementOptions.ShowTitle | SelectedItemDynamicElementOptions.SelectableLabel, Action onInteraction = null)
        {
            if (groupName == GroupNameDebug && !ShowDebugInfo)
                return;

            var groupItem = AddGroupItem(groupName);

            var groupedItem = m_SelectedItemDetailsGroupedItemUxmlPathViewTree.Clone();
            groupedItem.Q("selected-item-details__grouped-item__container").AddToClassList($"selected-item-details__grouped-item__{title.ToLower()}");

            var subFoldout = groupedItem.Q<Foldout>("selected-item-details__group__grouped-item__foldout");
            UIElementsHelper.SetVisibility(subFoldout, options.HasFlag(SelectedItemDynamicElementOptions.SubFoldout));
            if (options.HasFlag(SelectedItemDynamicElementOptions.SubFoldout))
            {
                // reparent everything under the foldout and make that the container item to be added to the group
                subFoldout.Add(groupedItem);
                groupedItem = subFoldout;
                subFoldout.text = title;
            }

            if (options.HasFlag(SelectedItemDynamicElementOptions.PlaceFirstInGroup))
                groupItem.Content.Insert(0, groupedItem);
            else
                groupItem.Content.Add(groupedItem);
            var titleLabel = groupedItem.Q<Label>("selected-item-details__grouped-item__label");
            var button = groupedItem.Q<Button>("selected-item-details__grouped-item__button");
            UIElementsHelper.SetVisibility(button, false);

            var toggle = groupedItem.Q<Toggle>("selected-item-details__grouped-item__toggle");
            UIElementsHelper.SetVisibility(toggle, false);

            var simpleTextContentLabel = groupedItem.Q<Label>("selected-item-details__grouped-item__content");
            var richTextContentLabel = groupedItem.Q<Label>("selected-item-details__grouped-item__content_rich-text-label");

            richTextContentLabel.RegisterCallback<PointerDownLinkTagEvent>(OnDescriptionLabelLinkSelected);
            richTextContentLabel.RegisterCallback<PointerOverLinkTagEvent>(OnLabelLinkPointerOver);
            richTextContentLabel.RegisterCallback<PointerOutLinkTagEvent>(OnLabelLinkPointerOut);
            var usedContentLabel = options.HasFlag(SelectedItemDynamicElementOptions.EnableRichText) ? richTextContentLabel : simpleTextContentLabel;

            var simpleTextSelectableLabel = groupedItem.Q<TextField>("selected-item-details__grouped-item__content_selectable-label");
            var richTextSelectableLabel = groupedItem.Q<TextField>("selected-item-details__grouped-item__content_rich-text-selectable-label");

            richTextSelectableLabel.RegisterCallback<PointerDownLinkTagEvent>(OnDescriptionLabelLinkSelected);
            richTextSelectableLabel.RegisterCallback<PointerOverLinkTagEvent>(OnLabelLinkPointerOver);
            richTextSelectableLabel.RegisterCallback<PointerOutLinkTagEvent>(OnLabelLinkPointerOut);
            var usedSelectableLabel = options.HasFlag(SelectedItemDynamicElementOptions.EnableRichText) ? richTextSelectableLabel : simpleTextSelectableLabel;
            if (options.HasFlag(SelectedItemDynamicElementOptions.SelectableLabel))
            {
                UIElementsHelper.SetVisibility(simpleTextContentLabel, false);
                UIElementsHelper.SetVisibility(richTextContentLabel, false);

                if (options.HasFlag(SelectedItemDynamicElementOptions.EnableRichText))
                {
                    UIElementsHelper.SetVisibility(simpleTextSelectableLabel, false);
                    UIElementsHelper.SetVisibility(richTextSelectableLabel, true);
                }
                else
                {
                    UIElementsHelper.SetVisibility(simpleTextSelectableLabel, true);
                    UIElementsHelper.SetVisibility(richTextSelectableLabel, false);
                }
            }
            else
            {
                UIElementsHelper.SwitchVisibility(richTextContentLabel, simpleTextContentLabel, options.HasFlag(SelectedItemDynamicElementOptions.EnableRichText));
                UIElementsHelper.SetVisibility(simpleTextSelectableLabel, false);
                UIElementsHelper.SetVisibility(richTextSelectableLabel, false);
            }

            if (options.HasFlag(SelectedItemDynamicElementOptions.ShowTitle))
                titleLabel.text = $"{title}:";
            else
            {
                UIElementsHelper.SetVisibility(titleLabel, false);
            }
            if (options.HasFlag(SelectedItemDynamicElementOptions.SelectableLabel))
            {
                usedSelectableLabel.SetValueWithoutNotify(content);
                if (!options.HasFlag(SelectedItemDynamicElementOptions.ShowTitle))
                    usedSelectableLabel.AddToClassList("selected-item-details__grouped-item__content-without-label");
            }
            else if (options.HasFlag(SelectedItemDynamicElementOptions.Button))
            {
                UIElementsHelper.SetVisibility(button, true);
                button.text = content;
                button.tooltip = tooltip;
                if (onInteraction != null)
                    button.clickable.clicked += onInteraction;
                else
                    Debug.LogError("No interaction defined for button");
            }
            else if (options.HasFlag(SelectedItemDynamicElementOptions.Toggle))
            {
                UIElementsHelper.SetVisibility(toggle, true);
                toggle.SetValueWithoutNotify(options.HasFlag(SelectedItemDynamicElementOptions.ToggleOn));
                toggle.text = content;
                toggle.tooltip = tooltip;
                if (onInteraction != null)
                    toggle.RegisterValueChangedCallback((v) => onInteraction());
                else
                    Debug.LogError("No interaction defined for toggle");
            }
            else if (options.HasFlag(SelectedItemDynamicElementOptions.Button))
            {
                button.text = content;
                button.tooltip = tooltip;
                if (onInteraction != null)
                    button.clickable.clicked += onInteraction;
                else
                    Debug.LogError("No interaction defined for button");
            }
            else
            {
                usedContentLabel.text = content;
                if (!options.HasFlag(SelectedItemDynamicElementOptions.ShowTitle))
                    usedContentLabel.AddToClassList("selected-item-details__grouped-item__content-without-label");
            }

            if (!string.IsNullOrEmpty(tooltip))
                groupedItem.tooltip = tooltip;
        }

        static ReadOnlySpan<Char> ParseOutLinkParameter(ReadOnlySpan<char> link, string parameterPrefeix)
        {
            var indexOfParameterStart = link.IndexOf(parameterPrefeix) + parameterPrefeix.Length;
            var param = link.Slice(indexOfParameterStart);
            var indexOfParameterEnd = param.IndexOf("'");
            return param.Slice(0, indexOfParameterEnd);
        }

        void OnDescriptionLabelLinkSelected(PointerDownLinkTagEvent evt)
        {
            var link = evt.linkID.AsSpan();
            const string k_linkPrefix = "href='";
            const string k_linePrefix = "line='";
            var lineNumber = int.Parse(ParseOutLinkParameter(link, k_linePrefix));
            var path = ParseOutLinkParameter(link, k_linkPrefix).ToString();
            switch (evt.button)
            {
                case 0:
                    if (!InternalEditorUtility.TryOpenErrorFileFromConsole(path, lineNumber))
                    {
                        if (!InternalEditorUtility.OpenFileAtLineExternal(path, lineNumber))
                        {
                            const string k_linkUrlPrefix = "file://";
                            Application.OpenURL(k_linkUrlPrefix + path);
                            Debug.LogError($"Failed to open file {path} at line {lineNumber}. Try rightclicking to copy to clipboard instead.");
                        }
                    }
                    break;
                case 1:
                {
                    GenericMenu menu = new GenericMenu();
                    menu.AddItem(new GUIContent("Copy File Path"), false, () => EditorGUIUtility.systemCopyBuffer = path);
                    menu.AddItem(new GUIContent("Copy File Path and Line"), false, () => EditorGUIUtility.systemCopyBuffer = $"{path}:{lineNumber}");
                    menu.AddItem(new GUIContent("Copy Line Number"), false, () => EditorGUIUtility.systemCopyBuffer = lineNumber.ToString());
                    menu.ShowAsContext();
                    break;
                }
                default:
                    break;
            }
        }

        void OnLabelLinkPointerOver(PointerOverLinkTagEvent evt)
        {
        }

        void OnLabelLinkPointerOut(PointerOutLinkTagEvent evt)
        {
        }

        public void SetItemName(string name)
        {
            UIElementsHelper.SetVisibility(m_CopyButton, k_FeatureFlagCopyButtonActive);
            NonObjectTitleShown = true;
            m_Title.text = name;
        }

        public void SetItemName(ObjectData pureCSharpObject, UnifiedType typeInfo)
        {
            UIElementsHelper.SetVisibility(m_CopyButton, k_FeatureFlagCopyButtonActive);
            NonObjectTitleShown = false;
            m_UnityObjectTitle.SetLabelData(m_CachedSnapshot, pureCSharpObject, typeInfo);
        }

        public void SetItemName(UnifiedType typeInfo)
        {
            UIElementsHelper.SetVisibility(m_CopyButton, k_FeatureFlagCopyButtonActive);
            NonObjectTitleShown = false;
            m_UnityObjectTitle.SetLabelData(m_CachedSnapshot, typeInfo);
        }

        public void SetItemName(CachedSnapshot.SourceIndex sourceIndex)
        {
            UIElementsHelper.SetVisibility(m_CopyButton, k_FeatureFlagCopyButtonActive);
            NonObjectTitleShown = false;
            m_UnityObjectTitle.SetLabelData(m_CachedSnapshot, sourceIndex);
        }

        public void SetItemName(UnifiedUnityObjectInfo unityObjectInfo)
        {
            UIElementsHelper.SetVisibility(m_CopyButton, k_FeatureFlagCopyButtonActive);
            m_SelectedUnityObject = unityObjectInfo;

            var searchButtonLabel = EditorAssetFinderUtility.GetSearchButtonLabel(m_CachedSnapshot, unityObjectInfo);
            m_CanSearch = searchButtonLabel != TextContent.SearchButtonCantSearch;
            if (m_CanSearch)
            {
                m_SearchInEditorButton.text = searchButtonLabel.text;
                m_SearchInEditorButton.tooltip = searchButtonLabel.tooltip;
                UIElementsHelper.SetVisibility(m_SearchInEditorButton, true);
                m_SearchInEditorButton.SetEnabled(true);
                UIElementsHelper.SetVisibility(m_QuickSearchButton, true);
                m_QuickSearchButton.SetEnabled(true);
            }
            else
            {
                m_SearchInEditorButton.text = TextContent.SearchButtonCantSearch.text;
                UIElementsHelper.SetVisibility(m_SearchInEditorButton, true);
                m_SearchInEditorButton.SetEnabled(false);
                UIElementsHelper.SetVisibility(m_QuickSearchButton, true);
                m_QuickSearchButton.SetEnabled(true);
                // not all supported Unity versions show tool-tips for disabled controls yet. m_UI.Setting the tool-tip on the enclosing UI Element works as a workaround.
                m_SearchInEditorButtonHolder.tooltip = TextContent.SearchButtonCantSearch.tooltip;
            }

            if (m_PreviewImageResult.PreviewImageNeedsCleanup)
                m_PreviewImageResult.Dispose();

            NonObjectTitleShown = false;
            m_UnityObjectTitle.SetLabelData(m_CachedSnapshot, unityObjectInfo);

            var cts = m_SearchCancellationTokenSource = new CancellationTokenSource();
            try
            {
                var findings = EditorAssetFinderUtility.FindObject(m_CachedSnapshot, unityObjectInfo, m_SearchCancellationTokenSource.Token);
                UIElementsHelper.SetVisibility(m_SelectInEditorButton, true);
                m_SelectInEditorButton.SetEnabled(false);
                m_SelectInEditorButton.tooltip = QuickSearchUtility.SearchDatabaseIsReady() ? TextContent.SelectInEditorButtonSearching : TextContent.SelectInEditorButtonSearchingWhileIndexIsBuilding;
                m_SelectInEditorButtonHolder.tooltip = String.Empty;

                findings
                    .ContinueWith(UpdateUIForSearchResults, m_SearchCancellationTokenSource.Token, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext())
                    .ContinueWith((t) => cts.Dispose());
            }
            catch (Exception e)
            {
                if (e is not OperationCanceledException)
                {
                    Debug.LogException(e);
                }
                cts.Dispose();
            }
        }

        void UpdateUIForSearchResults(Task<EditorAssetFinderUtility.Findings> findings)
        {
            if (findings.Exception != null)
            {
                if (findings.Exception.InnerException is not OperationCanceledException)
                {
                    Debug.LogException(findings.Exception);
                }
            }
            using var findingsResult = findings.Result;
            m_FoundObjectInEditor = findingsResult.FoundObject;
            if (m_FoundObjectInEditor != null)
            {
                m_PreviewImageResult = EditorAssetFinderUtility.GetPreviewImage(findingsResult);
                var previewFoldoutSessionStateKey = GetFoldoutSessionStateKey(m_PreviewFoldout.text);
                m_PreviewFoldout.SetValueWithoutNotify(SessionState.GetBool(previewFoldoutSessionStateKey, true));
                if (m_PreviewImageResult.PreviewImage)
                {
                    UIElementsHelper.SetVisibility(m_PreviewFoldoutHeader, true);
                    m_Preview.image = m_PreviewImageResult.PreviewImage;
                }
                m_SelectInEditorButton.SetEnabled(true);
                m_SelectInEditorButton.tooltip = (findingsResult.DegreeOfCertainty < 100 ? TextContent.SelectInEditorButtonLessCertain : TextContent.SelectInEditorButton100PercentCertain).tooltip;
            }
            else
            {
                UIElementsHelper.SetVisibility(m_SelectInEditorButton, true);

                m_SelectInEditorButton.SetEnabled(false);
                switch (findingsResult.FailReason)
                {
                    case EditorAssetFinderUtility.SearchFailReason.NotFound:
                        m_SelectInEditorButton.tooltip = m_SelectInEditorButtonHolder.tooltip = TextContent.SelectInEditorButtonNotFound.tooltip;
                        break;
                    case EditorAssetFinderUtility.SearchFailReason.FoundTooMany:
                        m_SelectInEditorButton.tooltip = m_SelectInEditorButtonHolder.tooltip = TextContent.SelectInEditorButtonFoundTooMany.tooltip +
                            (m_CanSearch ? TextContent.SelectInEditorTooltipCanSearch : string.Empty);
                        break;
                    case EditorAssetFinderUtility.SearchFailReason.FoundTooManyToProcess:
                        m_SelectInEditorButton.tooltip = m_SelectInEditorButtonHolder.tooltip = TextContent.SelectInEditorButtonFoundTooManyToProcess.tooltip +
                            (m_CanSearch ? TextContent.SelectInEditorTooltipCanSearch : string.Empty);
                        break;
                    case EditorAssetFinderUtility.SearchFailReason.TypeIssues:
                        m_SelectInEditorButton.tooltip = m_SelectInEditorButtonHolder.tooltip = TextContent.SelectInEditorButtonTypeMissmatch +
                            (m_CanSearch ? TextContent.SelectInEditorTooltipTrySearch : string.Empty);
                        break;
                    case EditorAssetFinderUtility.SearchFailReason.SearchTimeout:
                    case EditorAssetFinderUtility.SearchFailReason.SearchCanceled:
                        m_SelectInEditorButton.tooltip = m_SelectInEditorButtonHolder.tooltip = QuickSearchUtility.SearchDatabaseIsReady() ? TextContent.SelectInEditorButtonSearchTimeout : TextContent.SelectInEditorButtonSearchTimeoutSearchIndexWasNotYetBuild;
                        break;
                    case EditorAssetFinderUtility.SearchFailReason.Found:
                    default:
                        break;
                }
            }
        }

        public void SetDescription(string description)
        {
            if (!string.IsNullOrEmpty(description))
                AddDynamicElement(GroupNameHelp, "Description", description, tooltip: null, options: SelectedItemDynamicElementOptions.PlaceFirstInGroup | SelectedItemDynamicElementOptions.SelectableLabel);
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
            UIElementsHelper.SetVisibility(m_ManagedObjectInspectorContainers[indexOfInspector], true);
            m_ManagedObjectInspectors[0].SetupManagedObject(m_CachedSnapshot, objectInfo);
        }

        public void ManagedInspectorLinkWasClicked(int inspectorId, int treeViewId)
        {
            if (inspectorId >= 0 && inspectorId < m_ManagedObjectInspectors.Count)
            {
                m_ManagedObjectInspectors[inspectorId].LinkWasClicked(treeViewId);
            }
        }

        public void Dispose()
        {
            foreach (var inspector in m_ManagedObjectInspectors)
            {
                inspector.OnDisable();
            }
            m_ManagedObjectInspectors.Clear();
            m_UnityObjectTitle.Dispose();
            Clear();
        }

        public void ClearGroup(string groupName)
        {
            if (m_ActiveDetailsGroupsByGroupName.TryGetValue(groupName, out var group))
            {
                group.Clear();
            }
        }

        public void Clear()
        {
            m_SelectedUnityObject = default;
            if (m_ManagedObjectInspectors != null)
            {
                foreach (var inspector in m_ManagedObjectInspectors)
                {
                    inspector?.Clear();
                }
            }
            UIElementsHelper.SetVisibility(m_ManagedObjectInspectorContainers[0], false);
            m_DynamicElements.Clear();
            m_FoundObjectInEditor = null;
            m_SearchInEditorButton.tooltip = null;
            m_SelectInEditorButtonHolder.tooltip = null;
            m_SearchInEditorButtonHolder.tooltip = null;
            m_SelectInEditorButton.tooltip = null;
            UIElementsHelper.SetVisibility(m_SelectInEditorButton, false);
            UIElementsHelper.SetVisibility(m_SearchInEditorButton, false);
            UIElementsHelper.SetVisibility(m_QuickSearchButton, false);
            UIElementsHelper.SetVisibility(m_CopyButton, false);
            foreach (var item in m_GroupedElements.Children())
            {
                UIElementsHelper.SetVisibility(item, false);
            }
            foreach (var item in m_DetailsGroups)
            {
                item.Clear();
            }
            m_ActiveDetailsGroupsByGroupName.Clear();

            UIElementsHelper.SetVisibility(m_PreviewFoldoutHeader, false);
            if (m_PreviewImageResult.PreviewImageNeedsCleanup)
                m_PreviewImageResult.Dispose();
            m_Preview.image = null;
            NoItemSelected();
            SetDescription("");
            SetDocumentationURL(null);
            try
            {
                m_SearchCancellationTokenSource?.Cancel();
            }
            catch (ObjectDisposedException) { }
        }
    }
}

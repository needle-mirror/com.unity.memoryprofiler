using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class SnapshotListItem : VisualElement
    {
        static class Styling
        {
            public const string SnapshotMetaDataTextClassName = "snapshot__meta-data__text";

            public const string OpenClassName = "snapshot--open";
            public const string OpenAClassName = "snapshot--open-a";
            public const string OpenBClassName = "snapshot--open-b";
            public const string InViewClassName = "snapshot--in-view";
        }

        public bool RenamingFieldVisible
        {
            get
            {
                return m_RenamingFieldVisible;
            }
            set
            {
                if (value != m_RenamingFieldVisible)
                {
                    UIElementsHelper.SwitchVisibility(m_SnapshotRenameField, SnapshotNameLabel, value);

                    m_RenamingFieldVisible = value;
                    m_SnapshotRenameField.SetValueWithoutNotify(SnapshotNameLabel.text);
                    if (value)
                    {
                        EditorCoroutines.Editor.EditorCoroutineUtility.StartCoroutine(FocusRenamFieldDelayed(), this);
                    }
                }
            }
        }
        bool m_RenamingFieldVisible;

        IEnumerator FocusRenamFieldDelayed()
        {
            // wait for two frames, as the EditorWindow might still be getting it's focus back from the "close to rename" popup
            yield return null;
            yield return null;
            m_SnapshotRenameFieldTextInput.Focus();
        }

        Func<SnapshotFileData, string, bool> m_Rename;
        Action<SnapshotFileData> m_Open;
        Func<SnapshotFileData, bool> m_CanRenameSnaphot;
        Action<SnapshotFileData> m_Delete;

        VisualElement m_TemplateRoot;
        public VisualElement Root;
        VisualElement m_OptionDropdownButton;
        Button m_OpenButton;
        public Label SnapshotNameLabel;
        Label m_SnapshotDateLabel;
        Label m_OpenSnapshotTagLabel;
        VisualElement m_OpenSnapshotTagSpacer;
        TextField m_SnapshotRenameField;
        VisualElement m_SnapshotRenameFieldTextInput;
        public Image screenshot;

        VisualElement m_TotalRamBarHolder;
        VisualElement m_TotalRamBarUsed;
        Label m_TotalRamBarUsedLabel;
        Label m_TotalRamBarUsedLabelAlterative;
        Label m_TotalRamBarAvailableLabel;

        VisualTreeAsset m_SnapshotListItemTree;

        SnapshotFileData m_Snapshot;

        public SnapshotFileGUIData.State CurrentState
        {
            get
            {
                return m_CurrentState;
            }
            set
            {
                if (value != m_CurrentState)
                {
                    switch (m_CurrentState)
                    {
                        case SnapshotFileGUIData.State.Closed:
                            break;
                        case SnapshotFileGUIData.State.Open:
                            SnapshotNameLabel.RemoveFromClassList(Styling.OpenClassName);
                            Root.RemoveFromClassList(Styling.OpenClassName);
                            break;
                        case SnapshotFileGUIData.State.OpenA:
                            Root.RemoveFromClassList(Styling.OpenAClassName);
                            break;
                        case SnapshotFileGUIData.State.OpenB:
                            Root.RemoveFromClassList(Styling.OpenBClassName);
                            break;
                        case SnapshotFileGUIData.State.InView:
                            Root.RemoveFromClassList(Styling.InViewClassName);
                            break;
                        default:
                            break;
                    }

                    switch (value)
                    {
                        case SnapshotFileGUIData.State.Closed:
                            SetSnapshotTag(TagState.None);
                            break;
                        case SnapshotFileGUIData.State.Open:
                            SnapshotNameLabel.AddToClassList(Styling.OpenClassName);
                            Root.AddToClassList(Styling.OpenClassName);
                            SetSnapshotTag(TagState.None);
                            break;
                        case SnapshotFileGUIData.State.OpenA:
                            Root.AddToClassList(Styling.OpenAClassName);
                            SetSnapshotTag(TagState.A);
                            break;
                        case SnapshotFileGUIData.State.OpenB:
                            Root.AddToClassList(Styling.OpenBClassName);
                            SetSnapshotTag(TagState.B);
                            break;
                        case SnapshotFileGUIData.State.InView:
                            Root.AddToClassList(Styling.InViewClassName);
                            SetSnapshotTag(TagState.None);
                            break;
                        default:
                            break;
                    }
                    m_CurrentState = value;
                }
            }
        }
        SnapshotFileGUIData.State m_CurrentState = SnapshotFileGUIData.State.Closed;

        enum TagState
        {
            None,
            A,
            B,
        }

        void SetSnapshotTag(TagState tagState)
        {
            switch (tagState)
            {
                case TagState.None:
                    UIElementsHelper.SetVisibility(m_OpenSnapshotTagLabel, false);
                    UIElementsHelper.SetVisibility(m_OpenSnapshotTagSpacer, false);
                    break;
                case TagState.A:
                    m_OpenSnapshotTagLabel.text = "A";
                    m_OpenSnapshotTagLabel.SwitchClasses(classToAdd: GeneralStyles.ImageTintColorClassSnapshotA, classToRemove: GeneralStyles.ImageTintColorClassSnapshotB);
                    UIElementsHelper.SetVisibility(m_OpenSnapshotTagLabel, true);
                    UIElementsHelper.SetVisibility(m_OpenSnapshotTagSpacer, true);
                    break;
                case TagState.B:
                    m_OpenSnapshotTagLabel.text = "B";
                    m_OpenSnapshotTagLabel.SwitchClasses(classToAdd: GeneralStyles.ImageTintColorClassSnapshotB, classToRemove: GeneralStyles.ImageTintColorClassSnapshotA);
                    UIElementsHelper.SetVisibility(m_OpenSnapshotTagLabel, true);
                    UIElementsHelper.SetVisibility(m_OpenSnapshotTagSpacer, true);
                    break;
                default:
                    break;
            }
        }

        public SnapshotListItem() : base()
        {
            m_SnapshotListItemTree = AssetDatabase.LoadAssetAtPath(ResourcePaths.SessionListSnapshotItemUxmlPath, typeof(VisualTreeAsset)) as VisualTreeAsset;

            m_TemplateRoot = m_SnapshotListItemTree.Clone();

            style.flexShrink = 0;
            m_TemplateRoot.style.flexGrow = 1;

            hierarchy.Add(m_TemplateRoot);
            m_TemplateRoot.parent.style.flexDirection = FlexDirection.Row;


            InitVisualChildElements();
        }

        void Init()
        {
            InitVisualChildElements();
        }

        void InitVisualChildElements()
        {
            if (Root != null)
                return;

            Root = this.Q("snapshot-list__item");

            screenshot = Root.Q<Image>("preview-image", "preview-image");
            screenshot.image = Texture2D.blackTexture;
            screenshot.scaleMode = ScaleMode.ScaleToFit;

            SnapshotNameLabel = Root.Q<Label>("snapshot-name", Styling.SnapshotMetaDataTextClassName);
            SnapshotNameLabel.AddManipulator(new Clickable(() => RenameCapture()));
            m_SnapshotRenameField = Root.Q<TextField>("snapshot-name");
            m_SnapshotRenameField.isDelayed = true;
            m_SnapshotRenameFieldTextInput = m_SnapshotRenameField.Q("unity-text-input");
            m_SnapshotDateLabel = Root.Query<Label>("snapshot-date", Styling.SnapshotMetaDataTextClassName).First();

            m_OpenButton = Root.Q<Button>("snapshot-list__item__button", "full-visual-element-button");
            // TODO: Add to UXML and implement?
            m_OptionDropdownButton = Root.Q<Image>("optionButton");
            m_OpenSnapshotTagLabel = Root.Q<Label>("snapshot-list__item__open-snapshot__compare-tag");
            m_OpenSnapshotTagSpacer = Root.Q("snapshot-list__item__open-snapshot__compare-tag__spacer");

            m_TotalRamBarHolder = Root.Q("total-ram-usage-bar__holder");
            m_TotalRamBarUsed = Root.Q("total-ram-usage-bar__used-bar");
            m_TotalRamBarUsedLabel = m_TotalRamBarHolder.Q<Label>("total-ram-usage-bar__used-label");
            m_TotalRamBarUsedLabelAlterative = m_TotalRamBarHolder.Q<Label>("total-ram-usage-bar__used-label--light");
            m_TotalRamBarAvailableLabel = m_TotalRamBarHolder.Q<Label>("total-ram-usage-bar__available-label");

            UIElementsHelper.SetVisibility(m_OpenSnapshotTagLabel, false);
            UIElementsHelper.SetVisibility(m_OpenSnapshotTagSpacer, false);

            m_OpenButton.clickable.clicked += () => OpenCapture();
            m_OpenButton.AddManipulator(new ContextualMenuManipulator((binder) => PopulateOpenSnapshotOptionMenu(binder)));

            m_SnapshotRenameField.RegisterCallback<ChangeEvent<string>>((evt) =>
            {
                if (evt.newValue != evt.previousValue)
                {
                    TryRename();
                }
            });

            m_SnapshotRenameField.RegisterCallback<KeyDownEvent>((evt) =>
            {
                if (evt.keyCode == KeyCode.KeypadEnter || evt.keyCode == KeyCode.Return)
                {
                    TryRename();
                }
                if (evt.keyCode == KeyCode.Escape)
                {
                    AbortRename();
                }
            });
            m_SnapshotRenameField.RegisterCallback<BlurEvent>((evt) =>
            {
                TryRename();
            });
        }

        internal void AssignCallbacks(
            Func<SnapshotFileData, string, bool> rename,
            Action<SnapshotFileData> open,
            Func<SnapshotFileData, bool> canRenameSnaphot,
            Action<SnapshotFileData> deleteCapture
        )
        {
            m_Rename = rename;
            m_Open = open;
            m_CanRenameSnaphot = canRenameSnaphot;
            m_Delete = deleteCapture;
        }

        void PopulateOpenSnapshotOptionMenu(ContextualMenuPopulateEvent binder)
        {
            binder.menu.AppendAction(TextContent.SnapshotOptionMenuItemDelete.text, (a) =>
            {
                if (m_Snapshot != null)
                    m_Delete(m_Snapshot);
            });
            binder.menu.AppendAction(TextContent.SnapshotOptionMenuItemRename.text, (a) =>
            {
                if (m_Snapshot != null)
                    EditorApplication.delayCall += () => RenameCapture(m_Snapshot);
            });
            binder.menu.AppendAction(TextContent.SnapshotOptionMenuItemBrowse.text, (a) =>
            {
                if (m_Snapshot != null)
                    BrowseCaptureFolder(m_Snapshot);
            });
        }

        void BrowseCaptureFolder(SnapshotFileData snapshot)
        {
            EditorUtility.RevealInFinder(snapshot.FileInfo.FullName);
        }

        private void OpenCapture()
        {
            if (m_Snapshot != null)
                m_Open(m_Snapshot);
        }

        void RenameCapture()
        {
            if (m_Snapshot != null)
                RenameCapture(m_Snapshot);
        }

        void RenameCapture(SnapshotFileData snapshot)
        {
            if (m_CanRenameSnaphot(snapshot))
            {
                RenamingFieldVisible = true;
            }
        }

        void TryRename()
        {
            if (m_Snapshot != null)
            {
                if (!m_Rename(m_Snapshot, m_SnapshotRenameField.text))
                {
                    AbortRename();
                }
            }
        }

        void AbortRename()
        {
            if (m_Snapshot != null)
            {
                RenamingFieldVisible = false;
                m_Rename(m_Snapshot, m_Snapshot.GuiData.Name);
            }
        }

        public void AssignSnapshot(SnapshotFileData snapshotFileData)
        {
            m_Snapshot = snapshotFileData;
            if (snapshotFileData != null && snapshotFileData.GuiData != null)
            {
                snapshotFileData.GuiData.VisualElement = this;

                screenshot.image = snapshotFileData.GuiData.MetaScreenshot != null ? snapshotFileData.GuiData.MetaScreenshot : Texture2D.blackTexture;
                screenshot.tooltip = snapshotFileData.GuiData.MetaPlatform + " " + snapshotFileData.GuiData.MetaPlatformExtra;

                SnapshotsWindow.SetPlatformIcons(Root, snapshotFileData.GuiData);

                SnapshotNameLabel.text = snapshotFileData.GuiData.Name;
                SnapshotNameLabel.tooltip = snapshotFileData.FileInfo.FullName;
                m_SnapshotRenameField.SetValueWithoutNotify(SnapshotNameLabel.text);
                m_SnapshotDateLabel.text = snapshotFileData.GuiData.Date;
                m_SnapshotDateLabel.tooltip = snapshotFileData.GuiData.MetaContent;

                UIElementsHelper.SetVisibility(m_OpenSnapshotTagLabel, false);
                UIElementsHelper.SetVisibility(m_OpenSnapshotTagSpacer, false);

                // Assigning the snapshot open-state after the tag was hidden so it can be unhidden again if needed
                CurrentState = snapshotFileData.GuiData.CurrentState;

                if (snapshotFileData.GuiData.TargetInfo.HasValue && snapshotFileData.GuiData.MemoryStats.HasValue)
                {
                    UIElementsHelper.SetVisibility(m_TotalRamBarHolder, true);

                    var totalAvailableMemory = PlatformsHelper.GetPlatformSpecificTotalAvailableMemory(snapshotFileData.GuiData.TargetInfo.Value);
                    var totalUsedMemory = Math.Min(snapshotFileData.GuiData.MemoryStats.Value.TotalVirtualMemory, totalAvailableMemory);
                    if (totalUsedMemory == 0)
                    {
                        // Fallback for platforms where System Used Memory isn't implemented yet
                        totalUsedMemory = snapshotFileData.GuiData.MemoryStats.Value.TotalReservedMemory;
                    }
                    // if more is used than available, we might have a non unified device in a mixed platform or otherwise mis-accounted the available space
                    // assume that the apps memory still fitted in when the snapshot was taken.
                    totalAvailableMemory = Math.Max(totalAvailableMemory, totalUsedMemory);
                    // Set width in percent
                    var filledInPercent = ((float)totalUsedMemory / (float)totalAvailableMemory * 100f);
                    m_TotalRamBarUsed.style.SetBarWidthInPercent(filledInPercent);

                    m_TotalRamBarUsedLabel.text = EditorUtility.FormatBytes((long)totalUsedMemory);
                    m_TotalRamBarUsedLabelAlterative.text = EditorUtility.FormatBytes((long)totalUsedMemory);
                    m_TotalRamBarAvailableLabel.text = EditorUtility.FormatBytes((long)totalAvailableMemory);
                }
                else
                {
                    UIElementsHelper.SetVisibility(m_TotalRamBarHolder, false);
                }
            }
        }

        /// <summary>
        /// Instantiates a <see cref="SnapshotListItem"/> using the data read from a UXML file.
        /// </summary>
        public new class UxmlFactory : UxmlFactory<SnapshotListItem, UxmlTraits> {}

        /// <summary>
        /// Defines <see cref="UxmlTraits"/> for the <see cref="SnapshotListItem"/>.
        /// </summary>
        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription
            {
                get { yield break; }
            }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);

                ((SnapshotListItem)ve).Init();
            }
        }
    }
}

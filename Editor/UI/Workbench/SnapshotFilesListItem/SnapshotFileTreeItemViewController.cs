using System;
using System.IO;
using System.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.MemoryProfiler.Editor.UIContentData;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class SnapshotFileItemViewController : SnapshotFileBaseViewController
    {
        const string k_UxmlAssetGuid = "46bad64fdea94844ab28ccdf53f81d62";
        const string k_UssClass_Dark = "memory-profiler__dark";
        const string k_UssClass_Light = "memory-profiler__light";

        const string k_UxmlOpenButton = "memory-profile-snapshotfile__button";
        const string k_UxmlNameLabel = "memory-profile-snapshotfile__meta-data__name";
        const string k_UxmlOpenSnapshotTag = "memory-profile-snapshotfile__tag";
        const string k_UxmlRenameField = "memory-profile-snapshotfile__meta-data__rename";
        const string k_UxmlRenameFieldInputArea = "unity-text-input";
        const string k_UxmlTotalAllocatedInvertedLabel = "memory-profile-snapshotfile__bar__allocated-label-inverted";

        public enum State
        {
            None,
            Loaded,
            LoadedBase,
            LoadedCompare
        }

        // State
        State m_LoadedState;
        SnapshotDataService m_SnapshotDataService;

        // View
        Button m_OpenButton;
        Label m_Name;
        Label m_OpenSnapshotTag;
        Label m_TotalLabelAllocatedInverted;
        TextField m_RenameField;
        VisualElement m_RenameFieldInputArea;

        public SnapshotFileItemViewController(SnapshotFileModel model, SnapshotDataService snapshotDataService, ScreenshotsManager screenshotsManager) :
            base(model, screenshotsManager)
        {
            m_SnapshotDataService = snapshotDataService;
        }

        public State LoadedState
        {
            get => m_LoadedState;
            set
            {
                if (m_LoadedState == value)
                    return;

                m_LoadedState = value;
                if (IsViewLoaded)
                    RefreshLoadedState();
            }
        }

        protected override VisualElement LoadView()
        {
            var view = ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlAssetGuid);
            if (view == null)
                throw new InvalidOperationException("Unable to create view from Uxml. Uxml must contain at least one child element.");

            var themeUssClass = (EditorGUIUtility.isProSkin) ? k_UssClass_Dark : k_UssClass_Light;
            view.AddToClassList(themeUssClass);

            GatherReferencesInView(view);

            return view;
        }

        protected override void GatherReferencesInView(VisualElement view)
        {
            base.GatherReferencesInView(view);
            m_OpenButton = view.Q<Button>(k_UxmlOpenButton);
            m_Name = view.Q<Label>(k_UxmlNameLabel);
            m_OpenSnapshotTag = view.Q<Label>(k_UxmlOpenSnapshotTag);
            m_RenameField = view.Q<TextField>(k_UxmlRenameField);
            m_TotalLabelAllocatedInverted = view.Q<Label>(k_UxmlTotalAllocatedInvertedLabel);
        }

        protected override void RefreshView()
        {
            base.RefreshView();

            Debug.Assert(Model != null);

            m_OpenButton.clickable.clicked += () => OpenCapture();
            m_OpenButton.AddManipulator(new ContextualMenuManipulator((binder) => PopulateOpenSnapshotOptionMenu(binder)));

            m_Name.AddManipulator(new Clickable(() => RenameCapture()));

            m_RenameField.isDelayed = true;
            m_RenameField.SetValueWithoutNotify(Model.Name);
            m_RenameField.RegisterCallback<ChangeEvent<string>>((evt) =>
            {
                if (evt.newValue != evt.previousValue)
                    TryRename();
            });
            m_RenameField.RegisterCallback<KeyDownEvent>((evt) =>
            {
                if (evt.keyCode == KeyCode.KeypadEnter || evt.keyCode == KeyCode.Return)
                    TryRename();

                if (evt.keyCode == KeyCode.Escape)
                    ResetRenameState();
            });
            m_RenameField.RegisterCallback<BlurEvent>((evt) => TryRename());
            m_RenameFieldInputArea = m_RenameField.Q(k_UxmlRenameFieldInputArea);

            if (Model.MemoryInformationAvailable && Model.TotalResidentMemory > 0)
            {
                var residentLabel = EditorUtility.FormatBytes((long)Model.TotalResidentMemory);
                m_TotalLabelAllocatedInverted.text = residentLabel;
            }
            else
                UIElementsHelper.SetVisibility(m_TotalLabelAllocatedInverted, false);

            m_SnapshotDataService.LoadedSnapshotsChanged += RefreshLoadedState;
            RefreshLoadedState();
        }

        void RefreshLoadedState()
        {
            View.RemoveFromClassList("memory-profile-snapshotfile__state__base");
            View.RemoveFromClassList("memory-profile-snapshotfile__state__compare");
            View.RemoveFromClassList("memory-profile-snapshotfile__state__in-view");
            UIElementsHelper.SetVisibility(m_OpenSnapshotTag, false);

            if (m_LoadedState == State.None)
                return;

            View.AddToClassList("memory-profile-snapshotfile__state__in-view");

            switch (m_LoadedState)
            {
                case State.LoadedBase:
                {
                    m_OpenSnapshotTag.text = "A";
                    View.AddToClassList("memory-profile-snapshotfile__state__base");
                    UIElementsHelper.SetVisibility(m_OpenSnapshotTag, true);
                    break;
                }
                case State.LoadedCompare:
                {
                    m_OpenSnapshotTag.text = "B";
                    View.AddToClassList("memory-profile-snapshotfile__state__compare");
                    UIElementsHelper.SetVisibility(m_OpenSnapshotTag, true);
                    break;
                }
            }
        }

        void PopulateOpenSnapshotOptionMenu(ContextualMenuPopulateEvent binder)
        {
            binder.menu.AppendAction(TextContent.SnapshotOptionMenuItemDelete.text, (a) =>
            {
                DeleteCapture();
            });
            binder.menu.AppendAction(TextContent.SnapshotOptionMenuItemRename.text, (a) =>
            {
                RenameCapture();
            });
            binder.menu.AppendAction(TextContent.SnapshotOptionMenuItemBrowse.text, (a) =>
            {
                BrowseCaptureFolder();
            });
        }

        void BrowseCaptureFolder()
        {
            EditorUtility.RevealInFinder(Model.FullPath);
        }

        void OpenCapture()
        {
            m_SnapshotDataService.Load(Model.FullPath);
        }

        void RenameCapture()
        {
            if (m_SnapshotDataService.IsOpen(Model.FullPath))
            {
                if (!EditorUtility.DisplayDialog(TextContent.RenameSnapshotDialogTitle, TextContent.RenameSnapshotDialogMessage, TextContent.RenameSnapshotDialogAccept, TextContent.RenameSnapshotDialogCancel))
                    return;
            }

            UIElementsHelper.SwitchVisibility(m_RenameField, m_Name, true);
            m_RenameField.SetValueWithoutNotify(m_Name.text);
            EditorCoroutines.Editor.EditorCoroutineUtility.StartCoroutine(FocusRenamFieldDelayed(), this);
        }

        void DeleteCapture()
        {
            if (!EditorUtility.DisplayDialog(TextContent.DeleteSnapshotDialogTitle, TextContent.DeleteSnapshotDialogMessage, TextContent.DeleteSnapshotDialogAccept, TextContent.DeleteSnapshotDialogCancel))
                return;

            m_SnapshotDataService.Delete(Model.FullPath);
        }

        IEnumerator FocusRenamFieldDelayed()
        {
            // wait for two frames, as the EditorWindow might still be getting it's focus back from the "close to rename" popup
            yield return null;
            yield return null;
            m_RenameFieldInputArea.Focus();
        }

        void TryRename()
        {
            if (!m_SnapshotDataService.ValidateName(m_RenameField.text))
            {
                EditorUtility.DisplayDialog("Error", string.Format("Filename '{0}' contains invalid characters", m_RenameField.text), "OK");
                return;
            }

            m_SnapshotDataService.Rename(Model.FullPath, m_RenameField.text);

            ResetRenameState();
        }

        void ResetRenameState()
        {
            UIElementsHelper.SwitchVisibility(m_RenameField, m_Name, false);
        }
    }
}

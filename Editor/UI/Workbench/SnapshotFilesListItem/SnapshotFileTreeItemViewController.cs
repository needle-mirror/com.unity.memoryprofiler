using System;
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
        const string k_UxmlRenameFieldInput = "unity-text-input";
        const string k_UxmlRenameFieldWarning = "memory-profile-snapshotfile__warning";
        const string k_UxmlRenameFieldWarningMsg = "memory-profile__warning-msg";
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
        VisualElement m_Container;
        Label m_Name;
        Label m_OpenSnapshotTag;
        Label m_TotalLabelAllocatedInverted;
        TextField m_RenameField;
        TextElement m_RenameFieldInputArea;
        Label m_WarningMessage;

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

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            m_WarningMessage?.RemoveFromHierarchy();
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
            m_Container = view.Q(k_UxmlOpenButton);
            m_Name = view.Q<Label>(k_UxmlNameLabel);
            m_OpenSnapshotTag = view.Q<Label>(k_UxmlOpenSnapshotTag);
            m_RenameField = view.Q<TextField>(k_UxmlRenameField);
            m_RenameFieldInputArea = m_RenameField.Q<TextElement>();
            m_TotalLabelAllocatedInverted = view.Q<Label>(k_UxmlTotalAllocatedInvertedLabel);

            m_WarningMessage = new Label();
            m_WarningMessage.AddToClassList(k_UxmlRenameFieldWarningMsg);
        }

        protected override void RefreshView()
        {
            base.RefreshView();

            Debug.Assert(Model != null);

            m_Container.AddManipulator(new ContextualMenuManipulator((binder) => PopulateOpenSnapshotOptionMenu(binder)));
            m_Container.RegisterCallback<MouseUpEvent>((evt) =>
            {
                if ((MouseButton)evt.button == MouseButton.LeftMouse)
                {
                    OpenCapture();
                    evt.StopPropagation();
                }
            });

            m_Name.AddManipulator(new Clickable(() => RenameCapture()));

            m_RenameField.isDelayed = true;
            m_RenameField.SetValueWithoutNotify(Model.Name);
            m_RenameField.RegisterCallback<KeyDownEvent>((evt) =>
            {
                if ((evt.keyCode == KeyCode.Return) || (evt.keyCode == KeyCode.KeypadEnter) ||
                (evt.character == '\n') || (evt.character == '\r') || (evt.character == 0x10))
                {
                    if (!ValidateInput(m_RenameField.text))
                    {
                        // Don't allow input field to finish editing
                        // if input value is invalid
                        evt.StopImmediatePropagation();
#if UNITY_2023_2_OR_NEWER
                        m_RenameField.focusController.IgnoreEvent(evt);
#else
                        evt.PreventDefault();
#endif
                    }
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    ResetRenameState();
                    evt.StopImmediatePropagation();
                }
            }, TrickleDown.TrickleDown);
            m_RenameField.RegisterCallback<KeyUpEvent>((evt) =>
            {
                // We validate it separately, overwise m_RenameField.text
                // will have value before key input is applied
                ValidateInput(m_RenameField.text);
            });
            m_RenameField.RegisterCallback<MouseUpEvent>((evt) =>
            {
                // Block mouse events, so that it doesn't cause open when
                // we edit input field and click on it
                evt.StopImmediatePropagation();
            });
            m_RenameField.RegisterCallback<FocusOutEvent>((evt) =>
            {
                // We don't validate here, as otherwise we can end up
                // in sitation of invalid input and lost focus, which
                // is hard to exit (you'll need to focus and press `esc`)
                TryRename(m_RenameField.text);
                ResetRenameState();
            });

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
                DelayedAction(DeleteCapture);
            });
            binder.menu.AppendAction(TextContent.SnapshotOptionMenuItemRename.text, (a) =>
            {
                DelayedAction(RenameCapture);
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
            if (!ProgressBarDisplay.IsComplete())
                return;

            if (m_SnapshotDataService.IsOpen(Model.FullPath) && m_SnapshotDataService.Compared != null)
            {
                // Special case when it's open as "compared", which is in single mode can be considered as "cached"
                if (PathHelpers.NormalizePath(m_SnapshotDataService.Compared.FullPath) == PathHelpers.NormalizePath(Model.FullPath))
                    m_SnapshotDataService.Swap();

                return;
            }

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
            FocusRenameField();
        }

        void DeleteCapture()
        {
            if (!EditorUtility.DisplayDialog(TextContent.DeleteSnapshotDialogTitle, TextContent.DeleteSnapshotDialogMessage, TextContent.DeleteSnapshotDialogAccept, TextContent.DeleteSnapshotDialogCancel))
                return;

            m_SnapshotDataService.Delete(Model.FullPath);
        }

        bool ValidateInput(string newSnapshotName)
        {
            if (string.IsNullOrEmpty(newSnapshotName))
            {
                ShowRenameWarning("Name shouldn't be empty");
                return false;
            }

            if (!m_SnapshotDataService.ValidateName(newSnapshotName))
            {
                ShowRenameWarning("Name contains invalid characters");
                return false;
            }

            if (!m_SnapshotDataService.CanRename(Model.FullPath, newSnapshotName) && (Model.Name != newSnapshotName))
            {
                ShowRenameWarning("Snapshot with the same name already exist");
                return false;
            }

            HideRenameWarning();
            return true;
        }

        void TryRename(string newSnapshotName)
        {
            if (!m_SnapshotDataService.ValidateName(newSnapshotName))
                return;

            if (!m_SnapshotDataService.CanRename(Model.FullPath, newSnapshotName))
                return;

            m_SnapshotDataService.Rename(Model.FullPath, newSnapshotName);
        }

        void FocusRenameField()
        {
            // We need this because dialogs don't restore
            // EditorWindow focus, if it's a detached window
            EditorWindow.FocusWindowIfItsOpen<MemoryProfilerWindow>();

            //// Delay field re-focus so that EditorWindow has time to get focus
            DelayedAction(() => m_RenameFieldInputArea.Focus());
        }

        void ResetRenameState()
        {
            UIElementsHelper.SwitchVisibility(m_RenameField, m_Name, false);
            HideRenameWarning();
        }

        void DelayedAction(Action action, int framesDelay = 2)
        {
            EditorCoroutines.Editor.EditorCoroutineUtility.StartCoroutine(DelayedActionExecutor(action, framesDelay), this);
        }

        IEnumerator DelayedActionExecutor(Action action, int framesDelay)
        {
            for (int i = 0; i < framesDelay; i++)
                yield return null;

            action.Invoke();
        }

        void ShowRenameWarning(string message)
        {
            if (!m_RenameField.visible)
                return;

            m_RenameField.Q(k_UxmlRenameFieldInput).AddToClassList(k_UxmlRenameFieldWarning);

            var viewRoot = m_RenameField.panel.visualTree.Q("memory-profiler-view");
            var bounds = m_RenameField.ChangeCoordinatesTo(viewRoot, m_RenameField.contentRect);
            m_WarningMessage.RemoveFromHierarchy();
            viewRoot.Add(m_WarningMessage);
            m_WarningMessage.style.left = bounds.xMin;
            m_WarningMessage.style.top = bounds.yMax + 4;
            m_WarningMessage.text = message;
        }

        void HideRenameWarning()
        {
            m_RenameField.Q(k_UxmlRenameFieldInput).RemoveFromClassList(k_UxmlRenameFieldWarning);
            m_WarningMessage.RemoveFromHierarchy();
        }
    }
}

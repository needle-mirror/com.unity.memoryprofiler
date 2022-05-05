using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    class NoDataViewController : ViewController
    {
        const string k_UxmlAssetGuid = "c0fe38130e5999345864275870b04011";
        const string k_UssClass_Dark = "no-data-view__dark";
        const string k_UssClass_Light = "no-data-view__light";
        const string k_UxmlIdentifier_Label = "no-data-view__label";
        const string k_UxmlIdentifier_Button = "no-data-view__button";

        Label m_Label;
        Button m_Button;

        public event Action TakeSnapshotSelected;

        protected override VisualElement LoadView()
        {
            var view = ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlAssetGuid);
            if (view == null)
                throw new InvalidOperationException("Unable to create view from Uxml. Uxml must contain at least one child element.");
            view.style.flexGrow = 1;

            var themeUssClass = (EditorGUIUtility.isProSkin) ? k_UssClass_Dark : k_UssClass_Light;
            view.AddToClassList(themeUssClass);

            GatherReferencesInView(view);

            return view;
        }

        protected override void ViewLoaded()
        {
            m_Label.text = "To start inspection, select a Snapshot from the panel on the left, or capture a new Snapshot.\nThe Snapshot Panel can be toggled on/off from the toolbar.";
            m_Button.text = "Capture New Snapshot";
            m_Button.tooltip = "Take a new snapshot from the target specified in the target selection drop-down in the top left hand corner.";
            m_Button.clicked += TakeSnapshotSelected;
        }

        void GatherReferencesInView(VisualElement view)
        {
            m_Label = view.Q<Label>(k_UxmlIdentifier_Label);
            m_Button = view.Q<Button>(k_UxmlIdentifier_Button);
        }
    }
}

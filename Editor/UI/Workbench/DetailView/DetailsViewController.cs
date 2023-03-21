using System;
using System.Diagnostics;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class DetailsViewController : ViewController, ISelectionDetails
    {
        const string k_UxmlAssetGuid = "47e58f70d0330e54e8449ea200197ef4";
        const string k_UxmlIdentifier = "memory-profiler-selection";
        const string k_UxmlIdentifierContainer = k_UxmlIdentifier + "__container";
        const string k_UxmlIdentifierNoDetails = k_UxmlIdentifier + "__no-selection";

        // State
        bool m_Collapsed;
        ViewController m_SelectedController;

        // View
        VisualElement m_Container;
        Label m_NoDetailsLabel;

        public DetailsViewController()
        {
            m_Collapsed = false;
        }

        public void SetSelection(ViewController controller)
        {
            ClearSelection();

            AddChild(controller);
            m_SelectedController = controller;
            if (!m_Collapsed)
                m_Container.Add(m_SelectedController.View);

            UIElementsHelper.SetVisibility(m_NoDetailsLabel, false);

            UIElementsHelper.SetVisibility(m_NoDetailsLabel, false);
        }

        public void ClearSelection()
        {
            if (m_SelectedController == null)
                return;

            m_Container.Clear();

            RemoveChild(m_SelectedController);
            m_SelectedController.Dispose();
            m_SelectedController = null;

            UIElementsHelper.SetVisibility(m_NoDetailsLabel, true);
        }

        public void SetCollapsed(bool state)
        {
            if (m_Collapsed == state)
                return;

            m_Collapsed = state;

            if (m_SelectedController == null)
                return;

            if (m_Collapsed)
                m_SelectedController.View.RemoveFromHierarchy();
            else
                m_Container.schedule.Execute(() => m_Container.Add(m_SelectedController.View));
        }

        protected override VisualElement LoadView()
        {
            var view = ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlAssetGuid);
            if (view == null)
                throw new InvalidOperationException("Unable to create view from Uxml. Uxml must contain at least one child element.");

            GatherReferencesInView(view);

            return view;
        }

        void GatherReferencesInView(VisualElement view)
        {
            m_Container = view.Q(k_UxmlIdentifierContainer);
            m_NoDetailsLabel = view.Q<Label>(k_UxmlIdentifierNoDetails);
        }
    }
}

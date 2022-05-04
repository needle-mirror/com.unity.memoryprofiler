#if UNITY_2022_1_OR_NEWER
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    class ObjectBreakdownsViewController : ViewController
    {
        const string k_UxmlAssetGuid = "08806df351029ba40b47ea2e88e1f27f";
        const string k_UxmlIdentifier_SelectBreakdownButton = "object-breakdowns-view__select-breakdown__button";
        const string k_UxmlIdentifier_SelectBreakdownButtonLabel = "object-breakdowns-view__select-breakdown__button__label";
        const string k_UxmlIdentifier_BreakdownDescriptionLabel = "object-breakdowns-view__breakdown-description-label";
        const string k_UxmlIdentifier_BreakdownContainer = "object-breakdowns-view__breakdown-container";

        // Model.
        readonly ObjectBreakdownsModel m_Model;

        // View.
        VisualElement m_SelectBreakdownButton;
        Label m_SelectBreakdownButtonLabel;
        Label m_BreakdownDescriptionLabel;
        VisualElement m_BreakdownContainer;

        // Child view controllers.
        ViewController m_BreakdownViewController;

        public ObjectBreakdownsViewController(ObjectBreakdownsModel model)
        {
            m_Model = model;
        }

        protected override VisualElement LoadView()
        {
            var view = ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlAssetGuid);
            if (view == null)
                throw new System.InvalidOperationException("Unable to create view from Uxml. Uxml must contain at least one child element.");
            view.style.flexGrow = 1;
            return view;
        }

        protected override void ViewLoaded()
        {
            GatherViewReferences();

            // Configure breakdown selection menu.
            var manipulator = BuildContextualBreakdownMenuManipulator();
            m_SelectBreakdownButton.AddManipulator(manipulator);

            // Select the first breakdown.
            if (m_Model.Breakdowns != null && m_Model.Breakdowns.Count > 0)
                SelectBreakdown(m_Model.Breakdowns[0], true);
        }

        void GatherViewReferences()
        {
            m_SelectBreakdownButton = View.Q<VisualElement>(k_UxmlIdentifier_SelectBreakdownButton);
            m_SelectBreakdownButtonLabel = View.Q<Label>(k_UxmlIdentifier_SelectBreakdownButtonLabel);
            m_BreakdownDescriptionLabel = View.Q<Label>(k_UxmlIdentifier_BreakdownDescriptionLabel);
            m_BreakdownContainer = View.Q<VisualElement>(k_UxmlIdentifier_BreakdownContainer);
        }

        ContextualMenuManipulator BuildContextualBreakdownMenuManipulator()
        {
            var manipulator = new ContextualMenuManipulator((ContextualMenuPopulateEvent evt) =>
            {
                foreach (var breakdown in m_Model.Breakdowns)
                {
                    evt.menu.AppendAction(breakdown.DisplayName, (action) =>
                    {
                        SelectBreakdown(breakdown);
                    });
                }
            });

            manipulator.activators.Clear(); // ContextualMenuManipulator has right-click activation filter by default. Remove it.
            manipulator.activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });

            return manipulator;
        }

        void SelectBreakdown(ObjectBreakdownsModel.Option breakdown, bool defaultOpen = false)
        {
            MemoryProfilerAnalytics.StartEvent<MemoryProfilerAnalytics.OpenedViewEvent>();
            // Configure the select breakdown button's text.
            m_SelectBreakdownButtonLabel.text = breakdown.DisplayName;
            m_BreakdownDescriptionLabel.text = breakdown.Description;

            // Dispose of the previous breakdown's view controller.
            if (m_BreakdownViewController != null)
            {
                RemoveChild(m_BreakdownViewController);
                m_BreakdownViewController.Dispose();
            }

            // Create child view controller for the selected breakdown and add its view to the view hierarchy.
            m_BreakdownViewController = breakdown.CreateViewController(m_Model.Snapshot);
            AddChild(m_BreakdownViewController);
            m_BreakdownContainer.Add(m_BreakdownViewController.View);

            MemoryProfilerAnalytics.EndEvent(new MemoryProfilerAnalytics.OpenedViewEvent() { viewName = defaultOpen ? $"{breakdown.DisplayName}(Default)" : breakdown.ToString()});
        }
    }
}
#endif

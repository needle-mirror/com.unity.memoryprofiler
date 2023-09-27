using System;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    interface IViewControllerWithVisibilityEvents : IViewController
    {
        public void ViewWillBeDisplayed();
        public void ViewWillBeHidden();
    }

    // TabBarController is a container view controller designed for switching between content views in a tab-bar interface.
    abstract class TabBarController : ViewController
    {
        const int k_DefaultIndex = 0;
        const string k_UssClass_TabBarItemSelected = "tab-bar-controller__tab-bar-item--selected";

        int m_SelectedIndex;

        VisualElement[] m_TabBarItems;

        IViewControllerWithVisibilityEvents[] m_ViewControllers;

        protected TabBarController()
        {
            MakeTabBarItem = MakeDefaultTabBarItem;
        }

        public int SelectedIndex
        {
            get => m_SelectedIndex;
            set => SetSelectedIndex(value);
        }

        public IViewControllerWithVisibilityEvents[] ViewControllers
        {
            get => m_ViewControllers;
            set
            {
                if (value == null)
                    throw new ArgumentNullException(nameof(value));

                if (value.Length == 0)
                    throw new ArgumentException($"{nameof(value)} cannot be empty.", nameof(value));

                // Clean up old view controllers.
                if (m_ViewControllers != null)
                {
                    m_ViewControllers[m_SelectedIndex].ViewWillBeHidden();
                    foreach (var viewController in m_ViewControllers)
                    {
                        RemoveChild(viewController);
                        viewController.Dispose();
                    }
                }

                m_ViewControllers = value;

                // Add new view controllers.
                foreach (var viewController in m_ViewControllers)
                {
                    AddChild(viewController);
                }

                // If the view is loaded, select the first content view controller.
                if (IsViewLoaded)
                {
                    ReloadTabBarItems();
                    SetSelectedIndex(k_DefaultIndex, true);
                }
            }
        }

        // The tab bar controller's content view, where it places the selected content view controller's view. Must be assigned before ViewLoaded (in LoadView).
        protected abstract VisualElement ContentView { get; set; }

        // The tab bar controller's tab-bar view, where it places its tab bar items. Must be assigned before ViewLoaded (in LoadView).
        protected abstract VisualElement TabBarView { get; set; }

        // Override to provide custom tab bar items. One tab bar item will be created for each content view controller in ViewControllers and added as children of TabBarView.
        protected Func<IViewControllerWithVisibilityEvents, int, VisualElement> MakeTabBarItem { get; set; }

        // Invoked after the tab bar controller's selected index has changed.
        protected abstract void SelectedIndexChanged(int newSelectedIndex);


        protected override void ViewLoaded()
        {
            base.ViewLoaded();

            // Validate that derived implementations assigned required properties prior to ViewLoaded.
            ValidateRequiredPropertiesAreNonNull();

            CreateTabBarItems();

            // Set the selected index to load the selected content view. By default, this will be zero unless it was set prior to the view being loaded.
            if (IsValidViewControllerIndex(m_SelectedIndex))
                SetSelectedIndex(m_SelectedIndex, true);
        }

        void ValidateRequiredPropertiesAreNonNull()
        {
            if (ContentView == null)
            {
                throw new ArgumentNullException(
                    nameof(ContentView),
                    $"{nameof(ContentView)} must be assigned in {nameof(LoadView)} prior to {nameof(ViewLoaded)}.");
            }

            if (TabBarView == null)
            {
                throw new ArgumentNullException(
                    nameof(ContentView),
                    $"{nameof(TabBarView)} must be assigned in {nameof(LoadView)} prior to {nameof(ViewLoaded)}.");
            }
        }

        void SetSelectedIndex(int index, bool force = false)
        {
            if (!IsValidViewControllerIndex(index))
                throw new ArgumentException($"{nameof(index)} is not a valid view controller index.", nameof(index));

            if (!force && index == m_SelectedIndex)
                return;

            var previousIndex = m_SelectedIndex;
            m_SelectedIndex = index;
            if (IsViewLoaded)
            {
                SwitchContentViewController(previousIndex, m_SelectedIndex);
                SwitchSelectedTabBarItem(previousIndex, m_SelectedIndex);
            }

            if (previousIndex != m_SelectedIndex)
                SelectedIndexChanged(m_SelectedIndex);
        }

        bool IsValidViewControllerIndex(int index)
        {
            return (ViewControllers != null &&
                index >= 0 &&
                index < ViewControllers.Length);
        }

        void SwitchContentViewController(int previousViewControllerIndex, int newViewControllerIndex)
        {
            HideContentViewControllerAtIndex(previousViewControllerIndex);
            ShowContentViewControllerAtIndex(newViewControllerIndex);
        }

        void ShowContentViewControllerAtIndex(int index)
        {
            var contentViewController = ViewControllers[index];

            // If loading the view controller's view for the first time, it must be added to the content view.
            if (ContentView.IndexOf(contentViewController.View) == -1)
                ContentView.Add(contentViewController.View);

            contentViewController.ViewWillBeDisplayed();
            // Show the view controller's view.
            UIElementsHelper.SetElementDisplay(contentViewController.View, true);
        }

        void HideContentViewControllerAtIndex(int index)
        {
            var contentViewController = ViewControllers[index];

            // Nothing needs to be hidden if the content view controller's view hasn't been loaded yet.
            if (!contentViewController.IsViewLoaded)
                return;

            contentViewController.ViewWillBeHidden();
            // Hide the view controller's view.
            UIElementsHelper.SetElementDisplay(contentViewController.View, false);
        }

        void ReloadTabBarItems()
        {
            // Remove existing tab bar items.
            if (m_TabBarItems != null)
            {
                foreach (var tabBarItem in m_TabBarItems)
                {
                    tabBarItem.RemoveFromHierarchy();
                }
            }

            CreateTabBarItems();
        }

        void CreateTabBarItems()
        {
            // Create a new tab bar item for each content view controller.
            if (ViewControllers == null)
                return;

            var tabBarItems = new VisualElement[ViewControllers.Length];
            for (var i = 0; i < ViewControllers.Length; i++)
            {
                var viewController = ViewControllers[i];

                var tabBarItem = MakeTabBarItem.Invoke(viewController, i);
                tabBarItems[i] = tabBarItem;

                TabBarView.Add(tabBarItem);
            }

            m_TabBarItems = tabBarItems;
        }

        void SwitchSelectedTabBarItem(int previousViewControllerIndex, int newViewControllerIndex)
        {
            SetTabBarItemAtIndexSelected(previousViewControllerIndex, false);
            SetTabBarItemAtIndexSelected(newViewControllerIndex, true);
        }

        void SetTabBarItemAtIndexSelected(int index, bool selected)
        {
            if (index < 0 || index >= m_TabBarItems.Length)
                return;

            var tabBarItem = m_TabBarItems[index];
            if (selected)
                tabBarItem.AddToClassList(k_UssClass_TabBarItemSelected);
            else
                tabBarItem.RemoveFromClassList(k_UssClass_TabBarItemSelected);
        }

        VisualElement MakeDefaultTabBarItem(IViewControllerWithVisibilityEvents viewController, int viewControllerIndex)
        {
            return new Button(() =>
            {
                SelectedIndex = viewControllerIndex;
            })
            {
                text = $"{viewControllerIndex}",
                style =
                {
                    flexGrow = 1,
                }
            };
        }
    }
}

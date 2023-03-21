using System;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class PageDetailsViewController : ViewController
    {
        // State
        string m_AssetGuid;

        public PageDetailsViewController(string assetGuid)
        {
            m_AssetGuid = assetGuid;
        }

        protected override VisualElement LoadView()
        {
            var view = ViewControllerUtility.LoadVisualTreeFromUxml(m_AssetGuid);
            if (view == null)
                throw new InvalidOperationException("Unable to create view from Uxml. Uxml must contain at least one child element.");

            return view;
        }
    }
}

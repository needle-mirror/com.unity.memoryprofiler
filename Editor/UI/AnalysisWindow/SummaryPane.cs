using System.Collections;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEditor;
using Unity.MemoryProfiler.Editor.Format;
using System;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class SummaryPane : ViewPane
    {
        internal class History : HistoryEvent
        {
            protected override bool IsEqual(HistoryEvent evt)
            {
                return evt != null && evt is History;
            }
        }

        public override VisualElement[] VisualElements
        {
            get
            {
                return m_VisualElements;
            }
        }

        public override string ViewName { get { return TextContent.SummaryView.text; } }

        public SummaryPane(IUIStateHolder s, IViewPaneEventListener l):base(s, l)
        {
            VisualTreeAsset summaryViewTree;
            summaryViewTree = AssetDatabase.LoadAssetAtPath(ResourcePaths.SummaryPaneUxmlPath, typeof(VisualTreeAsset)) as VisualTreeAsset;

            m_VisualElements = new[] { summaryViewTree.Clone() };
        }

        public void OpenHistoryEvent(History e)
        {
            ////m_TreeMap.SelectItem(a);
            ////OpenMetricData(a._metric);
            //if (e == null) return;
            //m_EventListener.OnRepaint();
        }

        public override HistoryEvent GetCurrentHistoryEvent()
        {
            return new History();
        }

        public override void OnClose()
        {
            return;
        }

        public override void OnGUI(Rect r)
        {
            throw new System.InvalidOperationException();
        }
    }
}

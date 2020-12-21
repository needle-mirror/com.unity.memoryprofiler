using UnityEditor;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// A spreadsheet with text rendering util methods
    /// </summary>
    internal abstract class TextSpreadsheet : SpreadsheetLogic
    {
        EllipsisStyleMetric m_EllipsisStyleMetricData;
        EllipsisStyleMetric m_EllipsisStyleMetricHeader;
        GUIContent m_TextElipsisSwapContent = new GUIContent();

        protected EllipsisStyleMetric EllipsisStyleMetricData
        {
            get
            {
                if (m_EllipsisStyleMetricData == null)
                    m_EllipsisStyleMetricData = new EllipsisStyleMetric(Styles.General.NumberLabel);

                return m_EllipsisStyleMetricData;
            }
        }
        protected EllipsisStyleMetric EllipsisStyleMetricHeader
        {
            get
            {
                if (m_EllipsisStyleMetricHeader == null)
                    m_EllipsisStyleMetricHeader = new EllipsisStyleMetric(Styles.General.EntryEven);

                return m_EllipsisStyleMetricHeader;
            }
        }

        protected const float k_RowHeight = 16;

        public TextSpreadsheet(SplitterStateEx splitter, IViewEventListener listener)
            : base(splitter, listener)
        {
        }

        public TextSpreadsheet(IViewEventListener listener)
            : base(listener)
        {
        }

        protected override float GetRowHeight(long row)
        {
            return k_RowHeight;
        }

        protected override void DrawRow(long row, Rect r, long index, bool selected, ref GUIPipelineState pipe)
        {
            if (Event.current.type == EventType.Layout)
                GUILayout.Space(r.height);

            if (Event.current.type == EventType.Repaint)
            {
                // TODO: clean this up when refactoring views to something more reliable when there are multiple MemoryProfilerWindow instances allowed.
                bool focused = EditorWindow.focusedWindow is MemoryProfilerWindow;
#if UNITY_2019_3_OR_NEWER
                if (selected)
                    Styles.General.EntrySelected.Draw(r, false, false, true, focused);
                else if (index % 2 == 0)
                    Styles.General.EntryEven.Draw(r, GUIContent.none, false, false, false, focused);

#else
                var background = (index % 2 == 0 ? Styles.General.EntryEven : Styles.General.EntryOdd);
                background.Draw(r, GUIContent.none, false, false, selected, focused);
#endif
            }
        }

        protected void DrawTextEllipsis(string text, string tooltip, Rect r, GUIStyle textStyle, EllipsisStyleMetric ellipsisStyle, bool selected)
        {
            Vector2 tSize = Styles.General.NumberLabel.CalcSize(new GUIContent(text));
            m_TextElipsisSwapContent.text = text;
            m_TextElipsisSwapContent.tooltip = tooltip;

            if(tSize.x > r.width || tooltip != null)
            {
                //if we have resized our column to be smaller than the text, provide a tooltip
                if (tooltip == null)
                    m_TextElipsisSwapContent.tooltip = text;

                Rect rclipped = new Rect(r.x, r.y, r.width - ellipsisStyle.pixelSize.x, r.height);
                EditorGUI.LabelField(rclipped, m_TextElipsisSwapContent, textStyle);
                Rect rEllipsis = new Rect(r.xMax - ellipsisStyle.pixelSize.x, r.y, ellipsisStyle.pixelSize.x, r.height);
                ellipsisStyle.style.Draw(rEllipsis, ellipsisStyle.ellipsisString, false, false, false, false);
            }
            else
            {
                EditorGUI.LabelField(r, m_TextElipsisSwapContent, textStyle);
            }
        }
    }
}

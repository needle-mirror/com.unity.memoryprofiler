using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// UI component that displays a sequence of boxes that can be resized.
    /// Useful for headers of tables/spreadsheet
    /// </summary>
    [System.Serializable]
    internal class SplitterStateEx
    {
        public const int MinSplitterSize = 16;
        const int defaultSplitSize = 6;

        public int this[long index]
        {
            get
            {
                if (shown[index])
                    return realSizes[index];
                return 0;
            }
            set
            {
                realSizes[index] = value;
            }
        }
        public int Length => realSizes.Length;

        public bool AreColumnsHidden
        {
            get
            {
                for (int i = 0; i < Length; i++)
                {
                    if (!shown[i])
                        return true;
                }
                return false;
            }
        }

        int[] realSizes;
        bool[] shown;
        string[] columnNames;

        public int splitSize;

        public Vector2 m_TopLeft = Vector2.zero;
        double lastClickTime = 0;
        Vector2 lastClickPos = Vector2.zero;

        public int splitterInitialOffset;
        public int currentActiveSplitter = -1;

        public event Action<int, int> RealSizeChanged = delegate {};

        public SplitterStateEx(int[] realSizes, bool[] shown, string[] columnNames)
        {
            this.realSizes = realSizes;
            splitSize = defaultSplitSize;
            if (shown != null && shown.Length == Length && columnNames != null && columnNames.Length == Length)
            {
                this.shown = shown;
                this.columnNames = columnNames;
                return;
            }
            throw new InvalidOperationException();
        }

        public bool IsColumnShown(long index)
        {
            return shown[index];
        }

        internal bool CanHideColumns
        {
            get
            {
                int countOfNonHiddenColumns = 0;
                foreach (var shownItem in shown)
                {
                    if (shownItem)
                        ++countOfNonHiddenColumns;
                }
                return countOfNonHiddenColumns > 1;
            }
        }

        internal void HideColumn(long col)
        {
            if (!CanHideColumns)
                return;
            shown[col] = false;
        }

        internal void ShowColumn(long col)
        {
            shown[col] = true;
        }

        public struct HiddenColumnInfo
        {
            public int Index;
            public string Name;
        }

        public List<HiddenColumnInfo> GetHiddenColumnNames()
        {
            var hiddenColumns = new List<HiddenColumnInfo>();
            for (int i = 0; i < Length; i++)
            {
                if (!shown[i])
                    hiddenColumns.Add(new HiddenColumnInfo(){ Index = i, Name = columnNames[i] });
            }
            return hiddenColumns;
        }

        void DoSplitter(int i1, int i2, int diff)
        {
            this[i1] += diff;
            if (this[i1] < MinSplitterSize)
                this[i1] = MinSplitterSize;
            RealSizeChanged(i1, this[i1]);
        }

        Rect GetSpliterRect(int index, bool vertical, float offset)
        {
            var splitterRect = vertical ?
                new Rect(m_TopLeft.x, m_TopLeft.y + offset + this[index] - splitSize / 2, 100, splitSize) :
                new Rect(m_TopLeft.x + offset + this[index] - splitSize / 2, m_TopLeft.y, splitSize, 16);
            return splitterRect;
        }

        public void BeginSplit(GUIStyle style, bool vertical, float offset, params GUILayoutOption[] options)
        {
            int pos;

            if (vertical)
            {
                GUILayout.BeginVertical(options);
                GUILayout.Space(0);
                var rectPos = GUILayoutUtility.GetLastRect();
                m_TopLeft = new Vector2(rectPos.x, rectPos.y + offset);
            }
            else
            {
                GUILayout.BeginHorizontal(options);
                GUILayout.Space(0);
                var rectPos = GUILayoutUtility.GetLastRect();
                m_TopLeft = new Vector2(rectPos.x + offset, rectPos.y);
            }

            switch (Event.current.type)
            {
                case EventType.Layout:
                {
                    break;
                }
                case EventType.MouseDown:
                {
                    if ((Event.current.button == 0) && (Event.current.clickCount == 1))
                    {
                        int cursor = 0;
                        pos = vertical ? (int)Event.current.mousePosition.y : (int)Event.current.mousePosition.x;

                        for (int i = 0; i < Length; ++i)
                        {
                            var splitterRect = GetSpliterRect(i, vertical, cursor);
                            if (splitterRect.Contains(Event.current.mousePosition))
                            {
                                splitterInitialOffset = pos;
                                currentActiveSplitter = i;
                                Event.current.Use();
                                break;
                            }

                            cursor += (int)this[i];
                        }
                    }
                    break;
                }

                case EventType.MouseDrag:
                {
                    if (currentActiveSplitter >= 0)
                    {
                        pos = vertical ? (int)Event.current.mousePosition.y : (int)Event.current.mousePosition.x;
                        int diff = pos - splitterInitialOffset;

                        if (diff != 0)
                        {
                            splitterInitialOffset = pos;
                            DoSplitter(currentActiveSplitter, currentActiveSplitter + 1, diff);
                        }

                        Event.current.Use();
                    }
                    break;
                }
                case EventType.MouseUp:
                {
                    if (currentActiveSplitter >= 0)
                    {
                        currentActiveSplitter = -1;
                        Event.current.Use();
                    }
                    double deltaTime = EditorApplication.timeSinceStartup - lastClickTime;
                    if (deltaTime < 0.8f)
                    {
                        if ((Event.current.mousePosition - lastClickPos).SqrMagnitude() < 3 * 3)
                        {
                            //handle double click
                            //UnityEngine.Debug.LogWarning("double click");
                        }
                    }
                    lastClickTime = EditorApplication.timeSinceStartup;
                    lastClickPos = Event.current.mousePosition;
                    break;
                }
                case EventType.Repaint:
                {
                    int cursor = 0;

                    for (var i = 0; i < Length; ++i)
                    {
                        var splitterRect = GetSpliterRect(i, vertical, cursor);
                        EditorGUIUtility.AddCursorRect(splitterRect, vertical ? MouseCursor.ResizeVertical : MouseCursor.SplitResizeLeftRight);
                        cursor += this[i];
                    }
                }

                break;
            }
        }

        public void BeginHorizontalSplit(float offset, params GUILayoutOption[] options)
        {
            BeginSplit(GUIStyle.none, false, offset, options);
        }

        public void BeginVerticalSplit(float offset, params GUILayoutOption[] options)
        {
            BeginSplit(GUIStyle.none, true, offset, options);
        }

        public void BeginHorizontalSplit(float offset, GUIStyle style, params GUILayoutOption[] options)
        {
            BeginSplit(style, false, offset, options);
        }

        public void BeginVerticalSplit(float offset, GUIStyle style, params GUILayoutOption[] options)
        {
            BeginSplit(style, true, offset, options);
        }

        public void EndVerticalSplit()
        {
            GUILayout.EndVertical();
        }

        public void EndHorizontalSplit()
        {
            GUILayout.EndHorizontal();
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Unity.MemoryProfiler.Editor.Database.Operation.Filter
{
    /// <summary>
    /// A Table that filters entries using a string that must be present in a specified column
    /// </summary>
    internal class MatchTable : IndexedTable
    {
        public int m_columnIndex;
        public string m_matchString;
        bool m_MatchExactly;
        public ArrayRange m_Range;
        public MatchTable(Database.Table sourceTable, int columnIndex, string matchString, bool matchExactly, ArrayRange range)
            : base(sourceTable)
        {
            m_columnIndex = columnIndex;
            m_matchString = matchString;
            m_MatchExactly = matchExactly;
            m_Range = range;
            UpdateIndices();
        }

        public void UpdateIndices()
        {
            var metaCol = m_SourceTable.GetMetaData().GetColumnByIndex(m_columnIndex);
            var col = m_SourceTable.GetColumnByIndex(m_columnIndex);
            if (metaCol == null || col == null)
            {
                UnityEngine.Debug.LogError("No column index " + m_columnIndex + " on table '" + m_SourceTable.GetName() + "'");
                indices = new long[0];
            }

            Matcher m = null;

            string matchStr = m_matchString;

            switch (metaCol.Type.comparisonMethod)
            {
                case DataMatchMethod.AsString:
                    if(m_MatchExactly)
                        m = new ExactStringMatcher();
                    else
                        m = new SubStringMatcher();
                    break;
                case DataMatchMethod.AsEnum:
                    m = new NumericMatcher();
                    var enumType = typeof(DiffTable.DiffResult);
                    var parsed = (DiffTable.DiffResult)Enum.Parse(enumType, matchStr, true);
                    if (Enum.IsDefined(enumType, parsed))
                    {
                        matchStr = ((int)parsed).ToString();
                    }
                    else
                        matchStr = "0";

                    break;
                case DataMatchMethod.AsNumber:
                    m = new NumericMatcher();
                    break;
            }

            m.SetMatchPredicate(matchStr);

            var matchIndices = col.GetMatchIndex(m_Range, m);
            indices = matchIndices;
        }

        protected class IndexUpdater : IUpdater
        {
            public MatchTable m_Table;
            public long[] m_OldToNew;
            public long m_RowCount;
            long IUpdater.OldToNewRow(long a)
            {
                if (a < 0 || a >= m_OldToNew.Length) return -1;
                var subRow = m_OldToNew[a];
                var newIndex = System.Array.FindIndex(m_Table.indices, x => x == subRow);
                return newIndex;
            }

            long IUpdater.GetRowCount()
            {
                return m_RowCount;
            }
        }
        public override IUpdater BeginUpdate()
        {
            var oldRowCount = m_SourceTable.GetRowCount();
            var sourceUpdater = m_SourceTable.BeginUpdate();
            var updater = new IndexUpdater();
            updater.m_Table = this;
            updater.m_OldToNew = new long[indices.Length];
            for (int i = 0; i != indices.Length; ++i)
            {
                updater.m_OldToNew[i] = sourceUpdater.OldToNewRow(indices[i]);
            }
            if (m_Range.IsIndex)
            {
                for (int i = 0; i != m_Range.Array.Length; ++i)
                {
                    m_Range.Array[i] = sourceUpdater.OldToNewRow(m_Range.Array[i]);
                }
            }
            else
            {
                if (m_Range.Count == oldRowCount)
                {
                    m_Range = new ArrayRange(0, sourceUpdater.GetRowCount());
                }
                else
                {
                    long newFirst = 0;
                    long newLast = 0;
                    for (long i = m_Range.Sequence.First; i != m_Range.Sequence.Last; ++i)
                    {
                        var n = sourceUpdater.OldToNewRow(i);
                        if (n >= 0)
                        {
                            newFirst = n;
                            break;
                        }
                    }

                    for (long i = m_Range.Sequence.Last; i != m_Range.Sequence.First; --i)
                    {
                        var n = sourceUpdater.OldToNewRow(i - 1);
                        if (n >= 0)
                        {
                            newLast = n + 1;
                            break;
                        }
                    }
                    if (newFirst < newLast)
                    {
                        m_Range = new ArrayRange(newFirst, newLast);
                    }
                    else
                    {
                        m_Range = new ArrayRange(0, sourceUpdater.GetRowCount());
                    }
                }
            }

            //TODO should not call end update here
            m_SourceTable.EndUpdate(sourceUpdater);
            UpdateIndices();
            updater.m_RowCount = indices.Length;
            return updater;
        }

        public override void EndUpdate(IUpdater updater)
        {
        }
    }

    /// <summary>
    /// Filter that only keeps the entries which a specified column value includes a specified string.
    /// </summary>
    internal class Match : Filter
    {
        public string MatchString
        {
            get
            {
                return m_MatchString;
            }
        }

        public int ColumnIndex { get; private set; }
        public bool MatchExactly { get; private set; }

        enum StringMatchingLogic
        {
            Is,
            Contains,
        }

        string m_MatchString;
        readonly string k_MatchStringField;
        int m_SelectedPopup;
        bool m_ForceFocus;
        //just for good measure
        bool m_ForceFocusAgainOneFrameLater;
        public Match(int col, string matchString = "", bool forceFocus = true, bool matchExactly = false)
        {
            ColumnIndex = col;
            m_MatchString = matchString;
            k_MatchStringField = "MatchInputFieldColumn" + col;
            m_ForceFocus = forceFocus;
            MatchExactly = matchExactly;
        }

        public override Filter Clone(FilterCloning fc)
        {
            Match o = new Match(ColumnIndex);
            m_ForceFocus = false;
            m_ForceFocusAgainOneFrameLater = false;
            o.m_MatchString = m_MatchString;
            o.m_SelectedPopup = m_SelectedPopup;
            o.MatchExactly = MatchExactly;
            return o;
        }

        public override Database.Table CreateFilter(Database.Table tableIn)
        {
            return CreateFilter(tableIn, new ArrayRange(0, tableIn.GetRowCount()));
        }

        public override Database.Table CreateFilter(Database.Table tableIn, ArrayRange range)
        {
            if (String.IsNullOrEmpty(m_MatchString) && !MatchExactly)
            {
                return tableIn;
            }
            var tableOut = new MatchTable(tableIn, ColumnIndex, m_MatchString, MatchExactly, range);
            return tableOut;
        }

        public override IEnumerable<Filter> SubFilters()
        {
            yield break;
        }

        public string GetColumnName(Database.Table sourceTable)
        {
            return sourceTable.GetMetaData().GetColumnByIndex(ColumnIndex).Name;
        }

        Database.Table m_CacheSourceTable;
        string[] m_CachePopupSelection;
        public override bool OnGui(Database.Table sourceTable, ref bool dirty)
        {
            EditorGUILayout.BeginHorizontal();
            bool bRemove = OnGui_RemoveButton();
            var metaCol = sourceTable.GetMetaData().GetColumnByIndex(ColumnIndex);
            var t = metaCol.Type;
            const string k_MissdirectionControlLabel = "k_MissdirectionControlLabel";
            GUI.SetNextControlName(k_MissdirectionControlLabel);

            if (t.comparisonMethod == DataMatchMethod.AsString)
            {
                GUILayout.Label(string.Format("'{0}'", metaCol.DisplayName));
                var matchingLogic = MatchExactly ? StringMatchingLogic.Is : StringMatchingLogic.Contains;
                var newMatchingLogic = (StringMatchingLogic)EditorGUILayout.EnumPopup(matchingLogic, GUILayout.MaxWidth(75));
                if(matchingLogic != newMatchingLogic)
                {
                    MatchExactly = newMatchingLogic == StringMatchingLogic.Is;
                    dirty = true;
                }
            }
            else
                GUILayout.Label(string.Format("'{0}' is:", metaCol.DisplayName));
            if (t.scriptingType.IsEnum)
            {
                string[] popupSelection;
                if (m_CacheSourceTable == sourceTable)
                {
                    popupSelection = m_CachePopupSelection;
                }
                else
                {
                    var names = System.Enum.GetNames(t.scriptingType);
                    popupSelection = new string[names.Length];
                    popupSelection[0] = "All";
                    System.Array.Copy(names, 1, popupSelection, 1, names.Length - 1);

                    if (m_CacheSourceTable == null)
                    {
                        m_CacheSourceTable = sourceTable;
                        m_CachePopupSelection = popupSelection;
                    }
                }

                GUI.SetNextControlName(k_MatchStringField);
                int newSelectedPopup = EditorGUILayout.Popup(m_SelectedPopup, popupSelection, GUILayout.Width(75));
                var currentlyFocused = GUI.GetNameOfFocusedControl();
                if (m_ForceFocus)
                {
                    if(currentlyFocused != k_MatchStringField)
                    {
                        // Not a clue why this misdirection was necessary but in 2021.2.0a18, Summary view tables where setting the focus directly and correctly
                        // while Objects and Allocations tables would only do so the second time a match filter was added and when currentlyFocused == k_MatchStringField
                        GUI.FocusControl(k_MissdirectionControlLabel);
                        m_ForceFocusAgainOneFrameLater = true;
                    }
                    else
                        GUI.FocusControl(k_MatchStringField);
                    m_ForceFocus = false;
                }
                else if(m_ForceFocusAgainOneFrameLater && Event.current.type == EventType.Layout)
                {
                    m_ForceFocusAgainOneFrameLater = false;
                    GUI.FocusControl(k_MatchStringField);
                }

                if (m_SelectedPopup != newSelectedPopup)
                {
                    m_SelectedPopup = newSelectedPopup;
                    if (m_SelectedPopup == 0)
                    {
                        m_MatchString = "";
                    }
                    else
                    {
                        m_MatchString = popupSelection[m_SelectedPopup];
                    }
                    dirty = true;
                }
            }
            else
            {
                GUI.SetNextControlName(k_MatchStringField);
                var newMatchString = GUILayout.TextField(m_MatchString, GUILayout.MinWidth(250));
                var currentlyFocused = GUI.GetNameOfFocusedControl();
                if (m_ForceFocus && Event.current.type == EventType.Layout)
                {
                    if (currentlyFocused != k_MatchStringField)
                    {
                        // Not a clue why this misdirection was necessary but in 2021.2.0a18, Summary view tables where setting the focus directly and correctly
                        // while Objects and Allocations tables would only do so the second time a match filter was added and when currentlyFocused == k_MatchStringField
                        GUI.FocusControl(k_MissdirectionControlLabel);
                        m_ForceFocusAgainOneFrameLater = true;
                    }
                    else
                        GUI.FocusControl(k_MatchStringField);
                    m_ForceFocus = false;
                }
                else if (m_ForceFocusAgainOneFrameLater && Event.current.type == EventType.Layout)
                {
                    m_ForceFocusAgainOneFrameLater = false;
                    GUI.FocusControl(k_MatchStringField);
                }
                if (m_MatchString != newMatchString)
                {
                    m_MatchString = newMatchString;
                    dirty = true;
                }
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            return bRemove;
        }

        public override void UpdateColumnState(Database.Table sourceTable, ColumnState[] colState)
        {
        }

        public override bool Simplify(ref bool dirty)
        {
            return false;
        }
    }
}

using System;
using Unity.MemoryProfiler.Editor.Database;
using Unity.MemoryProfiler.Editor.Diagnostics;
using Unity.MemoryProfiler.Editor.EnumerationUtilities;
using Unity.MemoryProfiler.Editor.Format;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;
using Unity.Profiling;
using UnityEditor.Profiling.Memory.Experimental;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class FormattingOptions
    {
        public Editor.ObjectDataFormatter ObjectDataFormatter;
        System.Collections.Generic.Dictionary<string, IDataFormatter> m_DataFormatters = new System.Collections.Generic.Dictionary<string, IDataFormatter>();

        public void AddFormatter(string name, IDataFormatter formatter)
        {
            m_DataFormatters.Add(name, formatter);
        }

        public IDataFormatter GetFormatter(string name)
        {
            if (String.IsNullOrEmpty(name)) return DefaultDataFormatter.Instance;

            IDataFormatter formatter;
            if (m_DataFormatters.TryGetValue(name, out formatter))
            {
                return formatter;
            }
            return DefaultDataFormatter.Instance;
        }
    }

    enum FirstSnapshotAge
    {
        None,
        Older,
        Newer
    }

    /// <summary>
    /// Holds the current state of the UI such as:
    ///     current mode (snapshot / diff)
    ///     current panel (treemap / memory map / table)
    ///     current display options
    ///     history of passed actions
    /// </summary>
    internal class UIState
    {
        internal abstract class BaseMode
        {
            public string[] TableNames
            {
                get
                {
                    return m_TableNames;
                }
            }

            protected string[] m_TableNames = { "none" };
            Database.Table[] m_Tables = { null };

            public event Action<ViewPane> ViewPaneChanged = delegate {};

            public ViewPane CurrentViewPane { get; private set; }

            public BaseMode() {}

            protected BaseMode(BaseMode copy)
            {
                m_TableNames = copy.m_TableNames;
                m_Tables = copy.m_Tables;
            }

            public abstract ViewPane GetDefaultView(IUIStateHolder uiStateHolder, IViewPaneEventListener viewPaneEventListener);

            public int GetTableIndex(Database.Table tab)
            {
                int index = Array.FindIndex(m_Tables, x => x == tab);
                return index;
            }

            public abstract Database.Table GetTableByIndex(int index);
            public abstract void UpdateTableSelectionNames();

            public virtual HistoryEvent GetCurrentHistoryEvent()
            {
                if (CurrentViewPane == null) return null;
                return CurrentViewPane.GetCurrentHistoryEvent();
            }

            public void TransitPane(ViewPane newPane)
            {
                if (CurrentViewPane != newPane && CurrentViewPane != null)
                {
                    CurrentViewPane.OnClose();
                }
                CurrentViewPane = newPane;
                ViewPaneChanged(newPane);
            }

            public abstract Database.Schema GetSchema();
            protected void UpdateTableSelectionNamesFromSchema(ObjectDataFormatter objectDataFormatter, Database.Schema schema)
            {
                if (schema == null)
                {
                    m_TableNames = new string[1];
                    m_TableNames[0] = "none";
                    m_Tables = new Database.Table[1];
                    m_Tables[0] = null;
                    return;
                }
                m_TableNames = new string[schema.GetTableCount() + 1];
                m_Tables = new Database.Table[schema.GetTableCount() + 1];
                m_TableNames[0] = "none";
                m_Tables[0] = null;
                for (long i = 0; i != schema.GetTableCount(); ++i)
                {
                    var tab = schema.GetTableByIndex(i);
                    long rowCount = tab.GetRowCount();
                    m_TableNames[i + 1] = (objectDataFormatter.ShowPrettyNames ? tab.GetDisplayName() : tab.GetName()) + " (" + (rowCount >= 0 ? rowCount.ToString("#,###,###,###,###") : "?") + ")";
                    m_Tables[i + 1] = tab;
                }
            }

            public abstract void Clear();

            // return null if build failed
            public abstract BaseMode BuildViewSchemaClone(Database.View.ViewSchema.Builder builder);
        }

        internal class SnapshotMode : BaseMode
        {
            FileReader m_RawSnapshotReader;
            RawSchema m_RawSchema;


            public Database.View.ViewSchema ViewSchema;
            public Database.Schema SchemaToDisplay;
            public CachedSnapshot snapshot
            {
                get
                {
                    if (m_RawSchema == null)
                        return null;
                    return m_RawSchema.m_Snapshot;
                }
            }
            protected SnapshotMode(SnapshotMode copy)
                : base(copy)
            {
                m_RawSnapshotReader = copy.m_RawSnapshotReader;
                m_RawSchema = copy.m_RawSchema;
                ViewSchema = copy.ViewSchema;
                SchemaToDisplay = copy.SchemaToDisplay;
            }

            public SnapshotMode(ObjectDataFormatter objectDataFormatter, FileReader snapshot)
            {
                SetSnapshot(objectDataFormatter, snapshot);
            }

            public override Database.Schema GetSchema()
            {
                return SchemaToDisplay;
            }

            static ProfilerMarker s_CrawlManagedData = new ProfilerMarker("CrawlManagedData");

            void SetSnapshot(ObjectDataFormatter objectDataFormatter, FileReader snapshot)
            {
                //dispose previous snapshot reader
                m_RawSnapshotReader.Dispose();

                if (!snapshot.HasOpenFile)
                {
                    m_RawSchema = null;
                    SchemaToDisplay = null;
                    UpdateTableSelectionNames();
                    return;
                }

                m_RawSnapshotReader = snapshot;

                ProgressBarDisplay.ShowBar(string.Format("Opening snapshot: {0}", System.IO.Path.GetFileNameWithoutExtension(snapshot.FullPath)));

                var cachedSnapshot = new CachedSnapshot(snapshot);
                using (s_CrawlManagedData.Auto())
                {
                    var crawling = Crawler.Crawl(cachedSnapshot);
                    crawling.MoveNext(); //start execution

                    var status = crawling.Current as EnumerationStatus;
                    float progressPerStep = 1.0f / status.StepCount;
                    while (crawling.MoveNext())
                    {
                        ProgressBarDisplay.UpdateProgress(status.CurrentStep * progressPerStep, status.StepStatus);
                    }
                }
                ProgressBarDisplay.ClearBar();

                m_RawSchema = new RawSchema();
                m_RawSchema.SetupSchema(cachedSnapshot, objectDataFormatter);

                SchemaToDisplay = m_RawSchema;
                UpdateTableSelectionNames();
            }

            public override Database.Table GetTableByIndex(int index)
            {
                return SchemaToDisplay.GetTableByIndex(index);
            }

            public override void UpdateTableSelectionNames()
            {
                if (m_RawSchema != null)
                {
                    UpdateTableSelectionNamesFromSchema(m_RawSchema.formatter.BaseFormatter, SchemaToDisplay);
                }
            }

            public override ViewPane GetDefaultView(IUIStateHolder uiStateHolder, IViewPaneEventListener viewPaneEventListener)
            {
                //if (uiState.snapshotMode != null && uiState.snapshotMode.snapshot != null)
                //{
                //    return new SummaryPane(uiState, viewPaneEventListener);
                //}
                if (uiStateHolder.UIState.snapshotMode != null && uiStateHolder.UIState.snapshotMode.snapshot != null)
                {
                    return new TreeMapPane(uiStateHolder, viewPaneEventListener);
                }

                return null;
            }

            public override void Clear()
            {
                if (CurrentViewPane != null)
                    CurrentViewPane.OnClose();

                SchemaToDisplay = null;
                m_RawSchema.Clear();
                m_RawSchema = null;
                m_RawSnapshotReader.Dispose();
            }

            public override BaseMode BuildViewSchemaClone(Database.View.ViewSchema.Builder builder)
            {
                Database.View.ViewSchema vs;
                vs = builder.Build(m_RawSchema);
                if (vs != null)
                {
                    SnapshotMode copy = new SnapshotMode(this);
                    copy.ViewSchema = vs;
                    copy.SchemaToDisplay = vs;
                    copy.UpdateTableSelectionNames();
                    return copy;
                }
                return null;
            }
        }
        internal class DiffMode : BaseMode
        {
            public BaseMode modeA { get { return snapshotAIsOlder ? modeFirst : modeSecond; } }
            public BaseMode modeB { get { return snapshotAIsOlder ? modeSecond : modeFirst; } }
            public BaseMode modeFirst;
            public BaseMode modeSecond;
            Database.Schema m_SchemaFirst;
            Database.Schema m_SchemaSecond;
            Database.Operation.DiffSchema m_SchemaDiff;
            ObjectDataFormatter m_ObjectDataFormatter;

            private const string k_DefaultDiffViewTable = "All Object";

            public bool snapshotAIsOlder { get; private set; }
            public bool sameSessionDiff { get; private set; }

            public DiffMode(ObjectDataFormatter objectDataFormatter, BaseMode snapshotFirst, BaseMode snapshotSecond, bool snapshotAIsOlder, bool sameSessionDiff)
            {
                this.snapshotAIsOlder = snapshotAIsOlder;
                ProgressBarDisplay.ShowBar("Snapshot diff in progress");
                m_ObjectDataFormatter = objectDataFormatter;
                modeFirst = snapshotFirst;
                modeSecond = snapshotSecond;
                m_SchemaFirst = modeFirst.GetSchema();
                m_SchemaSecond = modeSecond.GetSchema();
                ProgressBarDisplay.UpdateProgress(0.1f, "Building diff schema.");
                m_SchemaDiff = new Database.Operation.DiffSchema(m_SchemaFirst, m_SchemaSecond, snapshotAIsOlder, sameSessionDiff, () => { ProgressBarDisplay.UpdateProgress(0.3f, "Computing table data"); });
                ProgressBarDisplay.UpdateProgress(0.85f, "Updating table selection.");
                UpdateTableSelectionNames();
                ProgressBarDisplay.ClearBar();
            }

            protected DiffMode(DiffMode copy)
            {
                m_ObjectDataFormatter = copy.m_ObjectDataFormatter;
                modeFirst = copy.modeFirst;
                modeSecond = copy.modeSecond;
                m_SchemaFirst = copy.m_SchemaFirst;
                m_SchemaSecond = copy.m_SchemaSecond;
                m_SchemaDiff = copy.m_SchemaDiff;
            }

            public override Database.Schema GetSchema()
            {
                return m_SchemaDiff;
            }

            public override Database.Table GetTableByIndex(int index)
            {
                return m_SchemaDiff.GetTableByIndex(index);
            }

            public override void UpdateTableSelectionNames()
            {
                UpdateTableSelectionNamesFromSchema(m_ObjectDataFormatter, m_SchemaDiff);
            }

            public override ViewPane GetDefaultView(IUIStateHolder uiStateHolder, IViewPaneEventListener viewPaneEventListener)
            {
                //if (uiState.diffMode != null)
                //{
                //    return new SummaryPane(uiState, viewPaneEventListener);
                //}
                //return null;

                //TODO: delete this method once the default for diff is treemap
                Database.Table table = null;
                for (int i = 1; i < uiStateHolder.UIState.CurrentMode.TableNames.Length; i++)
                {
                    if (uiStateHolder.UIState.CurrentMode.TableNames[i].Contains(k_DefaultDiffViewTable))
                    {
                        table = uiStateHolder.UIState.CurrentMode.GetTableByIndex(i - 1);
                    }
                }
                if (table == null)
                    table = uiStateHolder.UIState.CurrentMode.GetTableByIndex(Mathf.Min(0, m_TableNames.Length - 1));

                if (table.Update())
                {
                    UpdateTableSelectionNames();
                }

                var pane = new UI.SpreadsheetPane(uiStateHolder, viewPaneEventListener);
                pane.OpenTable(new Database.TableReference(table.GetName()), table);
                return pane;
            }

            public void OnSnapshotsSwapped()
            {
                snapshotAIsOlder = !snapshotAIsOlder;
                m_SchemaDiff.OnSnapshotsSwapped();
            }

            public override void Clear()
            {
                modeFirst.Clear();
                modeSecond.Clear();
            }

            public override BaseMode BuildViewSchemaClone(Database.View.ViewSchema.Builder builder)
            {
                var newModeFirst = modeFirst.BuildViewSchemaClone(builder);
                if (newModeFirst == null) return null;
                var newModeSecond = modeSecond.BuildViewSchemaClone(builder);
                if (newModeSecond == null) return null;

                DiffMode copy = new DiffMode(this);
                copy.modeFirst = newModeFirst;
                copy.modeSecond = newModeSecond;
                copy.m_SchemaFirst = copy.modeFirst.GetSchema();
                copy.m_SchemaSecond = copy.modeSecond.GetSchema();
                copy.m_SchemaDiff = new Database.Operation.DiffSchema(copy.m_SchemaFirst, copy.m_SchemaSecond, copy.snapshotAIsOlder, copy.sameSessionDiff);
                copy.UpdateTableSelectionNames();

                return copy;
            }
        }

        public History history = new History();

        public event Action<BaseMode, ViewMode> ModeChanged = delegate {};

        public FirstSnapshotAge SnapshotAge { get; private set; }

        public BaseMode CurrentMode
        {
            get
            {
                switch (m_CurrentViewMode)
                {
                    case ViewMode.ShowNone:
                        return noMode;
                    case ViewMode.ShowFirst:
                        return FirstMode;
                    case ViewMode.ShowSecond:
                        return SecondMode;
                    case ViewMode.ShowDiff:
                        return diffMode;
                    default:
                        throw new NotImplementedException();
                }
            }
        }

        public BaseMode FirstMode { get; private set; }
        public BaseMode SecondMode { get; private set; }

        public enum ViewMode
        {
            ShowNone = -1,
            ShowDiff,
            ShowFirst,
            ShowSecond,
        }
        ViewMode m_CurrentViewMode = ViewMode.ShowNone;
        public ViewMode CurrentViewMode
        {
            get
            {
                return m_CurrentViewMode;
            }
            set
            {
                if (m_CurrentViewMode != value)
                {
                    m_CurrentViewMode = value;
                    ModeChanged(CurrentMode, value);
                }
            }
        }

        public SnapshotMode snapshotMode { get { return CurrentMode as SnapshotMode; } }
        public DiffMode diffMode;

        public SnapshotMode noMode;

        public readonly DefaultHotKey HotKey = new DefaultHotKey();
        public readonly FormattingOptions FormattingOptions;

        public UIState()
        {
            FormattingOptions = new FormattingOptions();
            FormattingOptions.ObjectDataFormatter = new ObjectDataFormatter();
            var sizeDataFormatter = new Database.SizeDataFormatter();
            FormattingOptions.AddFormatter("size", sizeDataFormatter);
            // TODO add a format named "integer" that output in base 16,10,8,2
            //FormattingOptions.AddFormatter("integer", PointerFormatter);
            // TODO add a format named "pointer" that output in hex
            //FormattingOptions.AddFormatter("pointer", PointerFormatter);

            noMode = new SnapshotMode(FormattingOptions.ObjectDataFormatter, default(FileReader));
        }

        public void AddHistoryEvent(HistoryEvent he)
        {
            if (he != null)
            {
                history.AddEvent(he);
            }
        }

        public void ClearDiffMode()
        {
            diffMode = null;
            if (CurrentViewMode == ViewMode.ShowDiff)
            {
                if (FirstMode != null)
                    CurrentViewMode = ViewMode.ShowFirst;
                else if (SecondMode != null)
                    CurrentViewMode = ViewMode.ShowSecond;
                else
                    CurrentViewMode = ViewMode.ShowNone;
            }
        }

        public void ClearAllOpenModes()
        {
            if (SecondMode != null)
                SecondMode.Clear();
            SecondMode = null;
            if (FirstMode != null)
                FirstMode.Clear();
            FirstMode = null;
            CurrentViewMode = ViewMode.ShowNone;
            diffMode = null;
            history.Clear();
            SnapshotAge = FirstSnapshotAge.None;
        }

        public void ClearFirstMode()
        {
            if (FirstMode != null)
                FirstMode.Clear();
            FirstMode = null;

            if (diffMode != null)
            {
                ClearDiffMode();
                SnapshotAge = FirstSnapshotAge.None;
            }

            if (CurrentViewMode == ViewMode.ShowFirst)
            {
                history.Clear();
                if (SecondMode != null)
                    CurrentViewMode = ViewMode.ShowSecond;
                else
                    CurrentViewMode = ViewMode.ShowNone;
            }
        }

        public void ClearSecondMode()
        {
            if (SecondMode != null)
                SecondMode.Clear();
            SecondMode = null;

            if (diffMode != null)
            {
                ClearDiffMode();
                SnapshotAge = FirstSnapshotAge.None;
            }

            if (CurrentViewMode == ViewMode.ShowSecond)
            {
                history.Clear();
                if (FirstMode != null)
                    CurrentViewMode = ViewMode.ShowFirst;
                else
                    CurrentViewMode = ViewMode.ShowNone;
            }
        }

        public void SetSnapshot(FileReader snapshot, bool first)
        {
            Checks.CheckEquals(true, snapshot.HasOpenFile);
            history.Clear();
            var targetedMode = ViewMode.ShowFirst;
            if (first)
            {
                if (FirstMode != null)
                    FirstMode.Clear();
                FirstMode = new SnapshotMode(FormattingOptions.ObjectDataFormatter, snapshot);
            }
            else
            {
                if (SecondMode != null)
                    SecondMode.Clear();
                SecondMode = new SnapshotMode(FormattingOptions.ObjectDataFormatter, snapshot);
                targetedMode = ViewMode.ShowSecond;
            }

            // Make sure that the mode is shown and that ModeChanged (fired by ShownMode if set to something different) is fired.
            if (CurrentViewMode != targetedMode)
                CurrentViewMode = targetedMode;
            else
                ModeChanged(CurrentMode, CurrentViewMode);
            ClearDiffMode();
        }

        public void SwapLastAndCurrentSnapshot(bool keepCurrentSnapshotOpen = true)
        {
            // TODO: find out if we actually need to clear this or if it can be saved with the mode
            history.Clear();
            var temp = SecondMode;
            SecondMode = FirstMode;
            FirstMode = temp;

            if (SnapshotAge == FirstSnapshotAge.Newer)
                SnapshotAge = FirstSnapshotAge.Older;
            else if (SnapshotAge == FirstSnapshotAge.Older)
                SnapshotAge = FirstSnapshotAge.Newer;

            if (CurrentViewMode != ViewMode.ShowDiff)
            {
                if (keepCurrentSnapshotOpen)
                    CurrentViewMode = CurrentViewMode == ViewMode.ShowFirst ? ViewMode.ShowSecond : ViewMode.ShowFirst;
                else
                    // CurrentViewMode already triggers this event but if the snapshots where swapped without changing view mode, we need to trigger the event here
                    ModeChanged(CurrentMode, CurrentViewMode);
            }
        }

        public void DiffLastAndCurrentSnapshot(bool snapshotAIsOlder, bool sameSessionDiff)
        {
            history.Clear();
            diffMode = new DiffMode(FormattingOptions.ObjectDataFormatter, snapshotAIsOlder ? FirstMode : SecondMode , snapshotAIsOlder ? SecondMode : FirstMode, snapshotAIsOlder, sameSessionDiff);
            CurrentViewMode = ViewMode.ShowDiff;
            if (snapshotAIsOlder)
                SnapshotAge = FirstSnapshotAge.Older;
        }

        public void TransitModeToOwningTable(Table table)
        {
            if (diffMode != null)
            {
                //open the appropriate snapshot mode, the one the table is from.
                if (diffMode.modeFirst.GetSchema().OwnsTable(table))
                {
                    TransitMode(diffMode.modeFirst);
                }
                else if (diffMode.modeSecond.GetSchema().OwnsTable(table))
                {
                    TransitMode(diffMode.modeSecond);
                }
                else if (diffMode.GetSchema().OwnsTable(table))
                {
                    TransitMode(diffMode);
                }
            }
        }

        public void TransitMode(UIState.BaseMode newMode)
        {
            if (newMode == diffMode)
            {
                CurrentViewMode = ViewMode.ShowDiff;
            }
            else if (newMode == FirstMode)
            {
                CurrentViewMode = ViewMode.ShowFirst;
            }
            else if (newMode == SecondMode)
            {
                CurrentViewMode = ViewMode.ShowSecond;
            }
            else
            {
                FirstMode = newMode;
                CurrentViewMode = ViewMode.ShowFirst;
                ModeChanged(newMode, CurrentViewMode);
            }
        }
    }
}

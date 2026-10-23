using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Unity.MemoryProfiler.Editor.Format;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UIElements;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;

#if IMGUI_TREEVIEW_API_CHANGED
using TreeView = UnityEditor.IMGUI.Controls.TreeView<int>;
using TreeViewItem = UnityEditor.IMGUI.Controls.TreeViewItem<int>;
using TreeViewState = UnityEditor.IMGUI.Controls.TreeViewState<int>;
#else
using TreeView = UnityEditor.IMGUI.Controls.TreeView;
#endif

namespace Unity.MemoryProfiler.Editor.UI.PathsToRoot
{
    internal class PathsToRootDetailView : TreeView, IDisposable
    {
        internal static class Styles
        {
            public static readonly GUIContent CircularRefContent = TextContent.TrIconContent("console.warnicon.sml",
                "This is a circular reference, click to highlight original");
            //public static readonly Texture2D CSharpIcon = Icons.LoadIcon(Icons.IconFolder + "CSharpIcon@3x.png", false);
            //public static readonly Texture2D CPlusPlusIcon = Icons.LoadIcon(Icons.IconFolder + "CPlusPlusIcon@3x.png", false);
            public static readonly Texture2D CSharpIcon = IconUtility.LoadIconAtPath(Icons.IconFolder + "CSharpIcon.png", true);
            public static readonly Texture2D CPlusPlusIcon = IconUtility.LoadIconAtPath(Icons.IconFolder + "CPlusPlusIcon.png", true);
            public static readonly Texture2D UnifiedIcon = IconUtility.LoadIconAtPath(Icons.IconFolder + "UnityObjectIcon.png", true);
            public static readonly Texture2D UnifiedManagedHalfIcon = IconUtility.LoadIconAtPath(Icons.IconFolder + "UnityObjectIconManagedHalf.png", true);
            public static readonly Texture2D UnifiedNativeHalfIcon = IconUtility.LoadIconAtPath(Icons.IconFolder + "UnityObjectIconNativeHalf.png", true);
            public static readonly Texture2D LeakedShellIcon = IconUtility.LoadIconAtPath(Icons.IconFolder + "LeakedShellErrorIcon.png", true);
            public static readonly Texture2D LeakedDynamicAssetIcon = IconUtility.LoadIconAtPath(Icons.IconFolder + "LeakedDynamicAssetErrorIcon.png", true);
            public static readonly Texture2D LeakedDynamicAssetManagedHalfIcon = IconUtility.LoadIconAtPath(Icons.IconFolder + "LeakedDynamicAssetErrorIconManagedHalf.png", true);
            public static readonly Texture2D LeakedDynamicAssetNativeHalfIcon = IconUtility.LoadIconAtPath(Icons.IconFolder + "LeakedDynamicAssetErrorIconNativeHalf.png", true);
            public static readonly GUIContent SceneIcon = EditorGUIUtility.IconContent("SceneAsset Icon");
            public static readonly GUIContent CSharpIconContent = new GUIContent(CSharpIcon, TextContent.Tr("C# Object"));
            public static readonly GUIContent CPlusPlusIconContent = new GUIContent(CPlusPlusIcon, TextContent.Tr("C++ Object"));

            public static Texture2D GetDataTypeIcon(ObjectOrTypeLabel.DataType dataType) => dataType switch
            {
                ObjectOrTypeLabel.DataType.ManagedObject or ObjectOrTypeLabel.DataType.PureCSharpType or ObjectOrTypeLabel.DataType.GCHandle => CSharpIcon,
                ObjectOrTypeLabel.DataType.NativeObject or ObjectOrTypeLabel.DataType.NativeUnityType => CPlusPlusIcon,
                ObjectOrTypeLabel.DataType.UnifiedUnityObject or ObjectOrTypeLabel.DataType.UnifiedUnityType => UnifiedIcon,
                ObjectOrTypeLabel.DataType.UnifiedUnityObjectManagedHalf => UnifiedManagedHalfIcon,
                ObjectOrTypeLabel.DataType.UnifiedUnityObjectNativeHalf => UnifiedNativeHalfIcon,
                ObjectOrTypeLabel.DataType.LeakedShell => LeakedShellIcon,
                ObjectOrTypeLabel.DataType.LeakedDynamicAsset => LeakedDynamicAssetIcon,
                ObjectOrTypeLabel.DataType.LeakedDynamicAssetManagedHalf => LeakedDynamicAssetManagedHalfIcon,
                ObjectOrTypeLabel.DataType.LeakedDynamicAssetNativeHalf => LeakedDynamicAssetNativeHalfIcon,
                _ => CSharpIcon,
            };
            public static readonly string NoObjectSelected = TextContent.Tr("No Object Selected");
            public static readonly string SelectionHasNoReferences = TextContent.Tr("Selection has no references");
            public static readonly string NoInspectableObjectSelected = TextContent.Tr("No inspectable object selected.");
        }

        /// <summary>
        /// Kept apart from <see cref="Styles"/> so reading its strings doesn't require <see cref="EditorStyles"/> to be loaded yet.
        /// </summary>
        internal static class LabelStyles
        {
            public static readonly GUIStyle TypeLabel = new(EditorStyles.label) { richText = true };
            public static readonly GUIStyle TypeLabelBold = new(EditorStyles.boldLabel) { richText = true, fontStyle = FontStyle.Bold };
            public static readonly GUIStyle RightAlignedLabel = new(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleRight,
#if UNITY_2023_1_OR_NEWER
                clipping = TextClipping.Ellipsis,
#endif
            };
        }

        const int k_CurrentSelectionTreeViewItemId = Int32.MaxValue;
        static readonly Color k_CurrentSelectionTreeViewItemBackgroundColor = new Color(0.129f, 0.129f, 0.129f); // #212121

        int m_ProcessingStackSize = 0;
        int m_ObjectsProcessed = 0;

        bool truncateTypeNames = MemoryProfilerSettings.MemorySnapshotTruncateTypes;

        public event Action<CachedSnapshot.SourceIndex> SelectionChangedEvt;

        /// <summary>
        /// Internal for testing purposes.
        /// Enum order is left-to-right column order: MultiColumnHeader.ToggleVisibility
        /// sorts visibleColumns ascending, so every programmatic assignment must be sorted too.
        /// </summary>
        internal enum PathsToRootViewColumns
        {
            Depth,
            RefereeCount,
            ShareOfImpact,
            TotalImpact,
            Details,
            Flags
        }

        enum PathsToRootViewGUIState
        {
            NothingSelected,
            Searching,
            SearchComplete
        }

        public enum ActiveTree
        {
            ShortestPath,
            RawReferences,
            ReferencesTo
        }

        PathsToRootViewGUIState m_GUIState;

        readonly BreakdownTableId m_Table;
        readonly ITableItemLocator m_CreatingTableLocator;
        ITableItemLocator CurrentLocatorFor(CachedSnapshot.SourceIndex source) => TableRevealHelper.ResolveCurrentLocator(m_TabController, m_CreatingTableLocator, source, m_CachedSnapshot);
        readonly AnalysisTabBarController m_TabController;

        CachedSnapshot m_CachedSnapshot;
        ActiveTree m_ActiveTree;

        // BuildRoot's own m_Ribbon.CurrentOption assignment can be clamped away before the ribbon has laid
        // out its buttons (see the ctor's restore call), so this triggers a one-shot catch-up resync from
        // DoGUI, which runs after layout. Cleared once the ribbon assignment actually sticks.
        bool m_RiderNeedsRibbonSync;

        PathsToRootDetailTreeViewItem m_ShortestPathToRootReferenceTree;
        PathsToRootDetailTreeViewItem m_RawReferenceTree;
        PathsToRootDetailTreeViewItem m_ReferencesToTree;
        RibbonButton m_ShortestPathButton;
        RibbonButton m_RawConnectionButton;
        RibbonButton m_ReferencesToButton;
        Ribbon m_Ribbon;

        SourceIndex m_CurrentSelection;
        SourceIndex m_OtherPartOfSelectedUnityObject;
        HashSet<SourceIndex> m_SelectionTargets;
        HashSet<SourceIndex> m_OtherPartTargets;

        MultiColumnHeaderWithTruncateTypeName m_MultiColumnHeader;
        // Guards ApplyColumnVisibilityForActiveTree's own assignment against OnVisibleColumnsChanged, which
        // must only persist the user's OWN show/hide choices (made via the header's context menu).
        bool m_ApplyingColumnVisibility;

        Thread m_BackgroundThread;
        CancellationTokenSource m_BackgroundSearchCancellation;

        /// <summary>
        /// Internal for testing purposes.
        /// </summary>
        internal bool IsBackgroundSearchRunning => m_BackgroundThread != null && m_BackgroundThread.IsAlive;
#pragma warning disable 0649
        Selection CurrentSelection;
#pragma warning restore 0649
        internal enum BackGroundThreadState
        {
            None,
            Analyze,
            AnalyzeDone
        };

        BackGroundThreadState m_BackgroundThreadState;

        List<ObjectData> m_CachedObjectDataList = new List<ObjectData>();
        List<ObjectData> m_CachedObjectDataListInnerLoop = new List<ObjectData>();
        HashSet<SourceIndex> m_ReferenceSearchAccelerator = new HashSet<SourceIndex>();

        public PathsToRootDetailView(TreeViewState state, MultiColumnHeaderWithTruncateTypeName multiColumnHeaderWithTruncateTypeName, Ribbon ribbon, BreakdownTableId table,
            ITableItemLocator currentTableLocator = null, AnalysisTabBarController tabController = null)
            : base(state, multiColumnHeaderWithTruncateTypeName)
        {
            m_Table = table;
            m_CreatingTableLocator = currentTableLocator;
            m_TabController = tabController;
            m_MultiColumnHeader = multiColumnHeaderWithTruncateTypeName;
            columnIndexForTreeFoldouts = (int)PathsToRootViewColumns.Details;
            showAlternatingRowBackgrounds = true;
            showBorder = true;
            m_GUIState = PathsToRootViewGUIState.SearchComplete;
            m_BackgroundThreadState = BackGroundThreadState.None;
            rowHeight = 20f;
            RestoreColumnWidths(multiColumnHeaderWithTruncateTypeName.state);
            multiColumnHeaderWithTruncateTypeName.ResizeToFit();
            multiColumnHeaderWithTruncateTypeName.columnSettingsChanged += OnColumnSettingsChanged;
            multiColumnHeaderWithTruncateTypeName.visibleColumnsChanged += OnVisibleColumnsChanged;
            multiColumnHeaderWithTruncateTypeName.sortingChanged += OnSortingChanged;
            MemoryProfilerSettings.TruncateStateChanged += OnTruncateStateChanged;
            MemoryProfilerSettings.SortReferencesWithDifferentRootLastChanged += OnSortReferencesWithDifferentRootLastChanged;

            m_ShortestPathToRootReferenceTree = new PathsToRootDetailTreeViewItem();
            SetupDepthsFromParentsAndChildren(m_ShortestPathToRootReferenceTree);
            m_RawReferenceTree = new PathsToRootDetailTreeViewItem();
            SetupDepthsFromParentsAndChildren(m_RawReferenceTree);

            m_ReferencesToTree = new PathsToRootDetailTreeViewItem();
            SetupDepthsFromParentsAndChildren(m_ReferencesToTree);

            m_Ribbon = ribbon;
            m_ShortestPathButton = m_Ribbon.Q<RibbonButton>("shortest-path");
            UIElementsHelper.SetVisibility(m_ShortestPathButton, false);
            m_RawConnectionButton = m_Ribbon.Q<RibbonButton>("raw-connections");
            m_ReferencesToButton = m_Ribbon.Q<RibbonButton>("referencing-to");
            m_RawConnectionButton.text = string.Format(TextContent.PathsToRootReferencedByRiderCaption, m_RawReferenceTree.children.Count);
            m_Ribbon.Clicked += RibbonClicked;
            m_ReferencesToButton.text = string.Format(TextContent.PathsToRootReferencesToRiderCaption, 0);
            m_RiderNeedsRibbonSync = true;
            SetActiveTree(ReferencesRiderPreference.Get(m_Table), persist: false);
            Reload();
        }

        /// <summary>
        /// The ONLY writer of m_ActiveTree, so state, persistence and per-rider column visibility can never
        /// diverge. Must not call Reload() itself: BuildRoot runs from inside Reload(), so that would re-enter.
        /// Callers decide when to reload.
        /// </summary>
        void SetActiveTree(ActiveTree tree, bool persist)
        {
            m_ActiveTree = tree;
            if (persist)
                ReferencesRiderPreference.Set(m_Table, tree);
            ApplyColumnVisibilityForActiveTree();
        }

        static string ColumnVisibilityKey(ActiveTree rider)
            => $"com.unity.memoryprofiler.{nameof(PathsToRootDetailView)}.columns.v1.{rider}";

        internal static int[] GetDefaultVisibleColumns(ActiveTree rider) => rider switch
        {
            ActiveTree.ShortestPath => new[]
            {
                (int)PathsToRootViewColumns.TotalImpact,
                (int)PathsToRootViewColumns.Details,
            },
            ActiveTree.RawReferences => new[]
            {
                (int)PathsToRootViewColumns.Depth,
                (int)PathsToRootViewColumns.TotalImpact,
                (int)PathsToRootViewColumns.Details,
            },
            ActiveTree.ReferencesTo => new[]
            {
                (int)PathsToRootViewColumns.Depth,
                (int)PathsToRootViewColumns.RefereeCount,
                (int)PathsToRootViewColumns.ShareOfImpact,
                (int)PathsToRootViewColumns.TotalImpact,
                (int)PathsToRootViewColumns.Details,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(rider), rider, null),
        };

        /// <summary>
        /// Composes, in order: the stored set for this rider (or its default) -> drop the four impact/depth
        /// columns and disable toggling them when RootAndImpactInfo isn't built (four columns of em dashes is
        /// worse than not offering them) -> dedupe, sort ascending, force Details in (its setter throws on
        /// null/empty and columnIndexForTreeFoldouts assumes it is always visible), then re-derives the sorting
        /// for the resulting visible columns. Called only from
        /// SetActiveTree - i.e. the ctor restore, RibbonClicked, and BuildRoot's demotion - deliberately not
        /// from BuildRoot unconditionally, which runs on every Reload() and would stomp the user's own
        /// show/hide choices.
        /// </summary>
        void ApplyColumnVisibilityForActiveTree()
        {
            var impactInfoBuilt = ImpactInfoBuilt;
            foreach (var column in k_ImpactColumns)
                m_MultiColumnHeader.state.columns[column].allowToggleVisibility = impactInfoBuilt;

            m_ApplyingColumnVisibility = true;
            try
            {
                m_MultiColumnHeader.state.visibleColumns = ComposeVisibleColumns(StoredOrDefaultVisibleColumns(m_ActiveTree), impactInfoBuilt);
            }
            finally
            {
                m_ApplyingColumnVisibility = false;
            }
            ApplySortingForActiveTree();
        }

        static readonly int[] k_ImpactColumns =
        {
            (int)PathsToRootViewColumns.Depth,
            (int)PathsToRootViewColumns.RefereeCount,
            (int)PathsToRootViewColumns.ShareOfImpact,
            (int)PathsToRootViewColumns.TotalImpact,
        };

        bool ImpactInfoBuilt => m_CachedSnapshot?.RootAndImpactInfo.SuccessfullyBuilt ?? true;

        static int[] StoredOrDefaultVisibleColumns(ActiveTree rider)
        {
            var stored = SessionState.GetIntArray(ColumnVisibilityKey(rider), Array.Empty<int>());
            return stored.Length > 0 ? stored : GetDefaultVisibleColumns(rider);
        }

        internal static int[] ComposeVisibleColumns(int[] baseColumns, bool impactInfoBuilt)
        {
            var visible = new SortedSet<int>(baseColumns);
            if (!impactInfoBuilt)
                visible.ExceptWith(k_ImpactColumns);
            visible.Add((int)PathsToRootViewColumns.Details);
            return visible.ToArray();
        }

        /// <summary>
        /// While RootAndImpactInfo isn't built the impact columns are stripped from the header, so their
        /// visibility is carried over from <paramref name="previousColumns"/> rather than taken from the header.
        /// </summary>
        internal static int[] ColumnsToPersist(int[] visibleColumns, int[] previousColumns, bool impactInfoBuilt)
        {
            if (impactInfoBuilt)
                return visibleColumns;

            var persisted = new SortedSet<int>(visibleColumns);
            persisted.ExceptWith(k_ImpactColumns);
            foreach (var column in previousColumns)
            {
                if (Array.IndexOf(k_ImpactColumns, column) >= 0)
                    persisted.Add(column);
            }
            return persisted.ToArray();
        }

        void OnVisibleColumnsChanged(MultiColumnHeader header)
        {
            // MultiColumnHeader.visibleColumnsChanged fires only from ToggleVisibility (the header's own
            // show/hide context menu), never from a direct assignment - so this can't feedback-loop with
            // ApplyColumnVisibilityForActiveTree's own assignment above. The guard is kept anyway since that
            // no-feedback guarantee is an implementation detail of a protected virtual in Editor code.
            if (m_ApplyingColumnVisibility)
                return;
            SessionState.SetIntArray(ColumnVisibilityKey(m_ActiveTree),
                ColumnsToPersist(header.state.visibleColumns, StoredOrDefaultVisibleColumns(m_ActiveTree), ImpactInfoBuilt));
            ApplySortingForActiveTree();
            Reload();
        }

        static string ColumnWidthKey(int column)
            => $"com.unity.memoryprofiler.{nameof(PathsToRootDetailView)}.columnWidth.v1.{(PathsToRootViewColumns)column}";

        /// <summary>
        /// A new view and header are built for every selection, so widths the user dragged are restored from the session.
        /// Internal for testing purposes.
        /// </summary>
        internal static void RestoreColumnWidths(MultiColumnHeaderState state)
        {
            for (var i = 0; i < state.columns.Length; i++)
            {
                var column = state.columns[i];
                var stored = SessionState.GetFloat(ColumnWidthKey(i), -1f);
                if (stored > 0f)
                    column.width = Mathf.Clamp(stored, column.minWidth, column.maxWidth);
            }
        }

        void OnColumnSettingsChanged(int column)
        {
            SessionState.SetFloat(ColumnWidthKey(column), m_MultiColumnHeader.state.columns[column].width);
        }

        static string SortedColumnKey(ActiveTree rider)
            => $"com.unity.memoryprofiler.{nameof(PathsToRootDetailView)}.sortedColumn.v1.{rider}";

        static string SortedAscendingKey(ActiveTree rider)
            => $"com.unity.memoryprofiler.{nameof(PathsToRootDetailView)}.sortedAscending.v1.{rider}";

        void ApplySortingForActiveTree()
        {
            var state = m_MultiColumnHeader.state;
            m_MultiColumnHeader.canSort = IsSortable(m_ActiveTree);
            var stored = SessionState.GetInt(SortedColumnKey(m_ActiveTree), -1);
            var column = GetEffectiveSortedColumn(m_ActiveTree, stored, state.visibleColumns);
            state.sortedColumnIndex = column;
            if (column >= 0)
                state.columns[column].sortedAscending = GetEffectiveSortedAscending(column, stored, SessionState.GetBool(SortedAscendingKey(m_ActiveTree), DefaultSortedAscending(column)));
        }

        /// <summary>
        /// The stored direction when the stored column is the one sorted by, otherwise the column's declared default.
        /// Internal for testing purposes.
        /// </summary>
        internal static bool GetEffectiveSortedAscending(int column, int storedColumn, bool storedAscending)
            => column == storedColumn ? storedAscending : DefaultSortedAscending(column);

        internal static bool DefaultSortedAscending(int column)
            => (PathsToRootViewColumns)column is PathsToRootViewColumns.Depth or PathsToRootViewColumns.Details or PathsToRootViewColumns.Flags;

        /// <summary>
        /// Shortest Path To Root lists each path to a root as its own branch, which sorting would tear apart.
        /// Internal for testing purposes.
        /// </summary>
        internal static bool IsSortable(ActiveTree rider) => rider != ActiveTree.ShortestPath;

        /// <summary>
        /// The stored sort column while it is visible, otherwise Depth, otherwise Total Impact, otherwise unsorted.
        /// Internal for testing purposes.
        /// </summary>
        internal static int GetEffectiveSortedColumn(ActiveTree rider, int storedColumn, int[] visibleColumns)
        {
            if (!IsSortable(rider))
                return -1;
            if (storedColumn >= 0 && storedColumn != (int)PathsToRootViewColumns.Flags && Array.IndexOf(visibleColumns, storedColumn) >= 0)
                return storedColumn;
            if (Array.IndexOf(visibleColumns, (int)PathsToRootViewColumns.Depth) >= 0)
                return (int)PathsToRootViewColumns.Depth;
            if (Array.IndexOf(visibleColumns, (int)PathsToRootViewColumns.TotalImpact) >= 0)
                return (int)PathsToRootViewColumns.TotalImpact;
            return -1;
        }

        void OnSortingChanged(MultiColumnHeader header)
        {
            var column = header.sortedColumnIndex;
            SessionState.SetInt(SortedColumnKey(m_ActiveTree), column);
            if (column >= 0)
                SessionState.SetBool(SortedAscendingKey(m_ActiveTree), header.IsSortedAscending(column));
            Reload();
        }

        void OnSortReferencesWithDifferentRootLastChanged()
        {
            Reload();
        }

        protected override IList<TreeViewItem> BuildRows(TreeViewItem root)
        {
            SortVisibleChildren(root);
            return base.BuildRows(root);
        }

        void SortVisibleChildren(TreeViewItem root)
        {
            var sortedColumnIndex = m_MultiColumnHeader.sortedColumnIndex;
            if (m_CachedSnapshot == null || !CanSortRows(m_ActiveTree, m_BackgroundThreadState) || sortedColumnIndex < 0 || !m_MultiColumnHeader.IsColumnVisible(sortedColumnIndex))
                return;

            var column = (PathsToRootViewColumns)sortedColumnIndex;
            SortVisibleChildrenRecursive(root, column, m_MultiColumnHeader.IsSortedAscending(sortedColumnIndex), MemoryProfilerSettings.SortReferencesWithDifferentRootLast);
        }

        /// <summary>
        /// Sorting reorders child lists the background search enumerates, so rows stay unsorted until it has finished.
        /// Internal for testing purposes.
        /// </summary>
        internal static bool CanSortRows(ActiveTree tree, BackGroundThreadState searchState)
        {
            return IsSortable(tree) && searchState != BackGroundThreadState.Analyze;
        }

        void SortVisibleChildrenRecursive(TreeViewItem item, PathsToRootViewColumns column, bool ascending, bool differentRootLast)
        {
            if (!item.hasChildren)
                return;

            if (column != PathsToRootViewColumns.Details)
            {
                foreach (var child in item.children)
                    EnsureImpactInfo((PathsToRootDetailTreeViewItem)child);
            }
            SortChildren(item.children, column, ascending, differentRootLast, truncateTypeNames);

            foreach (var child in item.children)
            {
                if (IsExpanded(child.id))
                    SortVisibleChildrenRecursive(child, column, ascending, differentRootLast);
            }
        }

        /// <summary>
        /// Stable sort of sibling rows whose impact info is already resolved, by <see cref="CompareByColumn"/>.
        /// Internal for testing purposes.
        /// </summary>
        internal static void SortChildren(List<TreeViewItem> children, PathsToRootViewColumns column, bool ascending, bool differentRootLast, bool truncateTypeNames)
        {
            if (children.Count < 2)
                return;

            if (((PathsToRootDetailTreeViewItem)children[0]).SortOriginalOrder < 0)
            {
                for (var i = 0; i < children.Count; i++)
                    ((PathsToRootDetailTreeViewItem)children[i]).SortOriginalOrder = i;
            }

            children.Sort((x, y) =>
            {
                var a = (PathsToRootDetailTreeViewItem)x;
                var b = (PathsToRootDetailTreeViewItem)y;
                var result = CompareByColumn(a, b, column, ascending, differentRootLast, truncateTypeNames);
                if (!ascending)
                    result = -result;
                return result != 0 ? result : a.SortOriginalOrder.CompareTo(b.SortOriginalOrder);
            });
        }

        /// <summary>
        /// Ascending comparison of two rows by <paramref name="column"/>; with <paramref name="differentRootLast"/>,
        /// Depth keeps rows with a different root than the selection after all same-root rows in either
        /// <paramref name="ascending"/> direction.
        /// </summary>
        static int CompareByColumn(PathsToRootDetailTreeViewItem a, PathsToRootDetailTreeViewItem b, PathsToRootViewColumns column, bool ascending, bool differentRootLast, bool truncateTypeNames)
        {
            switch (column)
            {
                case PathsToRootViewColumns.Depth:
                    if (differentRootLast && a.HasDifferentRootThanSelection != b.HasDifferentRootThanSelection)
                    {
                        var differentRootGroup = a.HasDifferentRootThanSelection ? 1 : -1;
                        return ascending ? differentRootGroup : -differentRootGroup;
                    }
                    return a.RelativeDepth.CompareTo(b.RelativeDepth);
                case PathsToRootViewColumns.RefereeCount:
                    return a.ReferrerCount.CompareTo(b.ReferrerCount);
                case PathsToRootViewColumns.ShareOfImpact:
                    return a.ShareOfImpactBytes.CompareTo(b.ShareOfImpactBytes);
                case PathsToRootViewColumns.TotalImpact:
                    return a.TotalImpactBytes.CompareTo(b.TotalImpactBytes);
                case PathsToRootViewColumns.Details:
                {
                    var result = string.Compare(truncateTypeNames ? a.TruncatedTypeName : a.TypeName, truncateTypeNames ? b.TruncatedTypeName : b.TypeName, StringComparison.OrdinalIgnoreCase);
                    return result != 0 ? result : string.Compare(a.displayName, b.displayName, StringComparison.OrdinalIgnoreCase);
                }
                default:
                    return 0;
            }
        }

        void EnsureImpactInfo(PathsToRootDetailTreeViewItem item)
        {
            if (item.ImpactInfoResolved)
                return;
            m_SelectionTargets ??= CollectDirectlyReferencedTargets(m_CachedSnapshot, m_CurrentSelection);
            m_OtherPartTargets ??= CollectDirectlyReferencedTargets(m_CachedSnapshot, m_OtherPartOfSelectedUnityObject);
            item.EnsureImpactInfo(m_CachedSnapshot, m_CurrentSelection, m_OtherPartOfSelectedUnityObject, m_SelectionTargets, m_OtherPartTargets);
        }

        /// <summary>
        /// The items <paramref name="source"/> itself references, without merging in the other half of a Unity Object.
        /// Internal for testing purposes.
        /// </summary>
        internal static HashSet<SourceIndex> CollectDirectlyReferencedTargets(CachedSnapshot snapshot, SourceIndex source)
        {
            var targets = new HashSet<SourceIndex>();
            if (source.Valid)
                ObjectConnection.GenerateReferencesTo(snapshot, source, new List<ObjectData>(), treatUnityObjectsAsOneObject: false, foundSourceIndices: targets);
            return targets;
        }

        void RibbonClicked(int idx)
        {
            SetActiveTree((ActiveTree)idx, persist: true);

            // Report view change event
            using var openViewEvent = MemoryProfilerAnalytics.BeginOpenViewEvent(m_ActiveTree.ToString());
            Reload();
        }

        // Ribbon.CurrentOption's setter clamps to [0, button count - 1], and the buttons are only populated
        // from a GeometryChangedEvent — so at construction time (before layout) this assignment can get
        // clamped to 0 even though m_ActiveTree holds a restored, valid, non-zero rider. Comparing
        // m_Ribbon.CurrentOption to m_ActiveTree after the assignment tells us whether it actually stuck;
        // if it didn't, m_RiderNeedsRibbonSync stays set so DoGUI (which runs after layout) retries.
        void ResyncRibbonToActiveTree()
        {
            if (m_Ribbon.CurrentOption != (int)m_ActiveTree)
                m_Ribbon.CurrentOption = (int)m_ActiveTree;

            if (m_Ribbon.CurrentOption == (int)m_ActiveTree)
                m_RiderNeedsRibbonSync = false;
        }

        void OnTruncateStateChanged()
        {
            truncateTypeNames = MemoryProfilerSettings.MemorySnapshotTruncateTypes;
            if (m_MultiColumnHeader.sortedColumnIndex == (int)PathsToRootViewColumns.Details)
                Reload();
        }

        static PathsToRootDetailTreeViewItem DefaultItem()
        {
            return new PathsToRootDetailTreeViewItem()
            {
                id = 0,
                displayName = "No item selected"
            };
        }

        static PathsToRootDetailTreeViewItem CreateRootItem()
        {
            return new PathsToRootDetailTreeViewItem()
            {
                id = 0,
                displayName = "Root",
                depth = -1,
                children = new List<TreeViewItem>(),
            };
        }

        struct Selection
        {
#pragma warning disable 0649
            public ObjectData objectData;
            public CachedSnapshot CachedSnapshot;
#pragma warning restore 0649
        }

        public void SetRoot(CachedSnapshot snapshot, CachedSnapshot.SourceIndex source)
        {
            m_CachedSnapshot = snapshot;
            // ApplyColumnVisibilityForActiveTree is otherwise only called from SetActiveTree (ctor/
            // RibbonClicked/BuildRoot's ShortestPath-only demotion), none of which run when a snapshot without
            // RootAndImpactInfo is loaded while already on RawReferences/ReferencesTo. Re-derive here too, since
            // this runs once per constructed view (a new view is built per selection) rather than per Reload().
            ApplyColumnVisibilityForActiveTree();
            if (m_CachedSnapshot == null || !source.Valid)
                return;

            switch (source.Id)
            {
                case CachedSnapshot.SourceIndex.SourceId.NativeObject:
                case CachedSnapshot.SourceIndex.SourceId.NativeAllocation:
                case CachedSnapshot.SourceIndex.SourceId.ManagedObject:
                case CachedSnapshot.SourceIndex.SourceId.Prefab:
                case CachedSnapshot.SourceIndex.SourceId.Scene:
                    UpdateRootObjects(source);
                    break;
                case CachedSnapshot.SourceIndex.SourceId.GfxResource:
                    var objectData = ObjectData.FromSourceLink(m_CachedSnapshot, source);
                    // grab the references to the Native object, if it is valid
                    if (objectData.IsValid && objectData.nativeObjectIndex >= 0)
                        UpdateRootObjects(new SourceIndex(SourceIndex.SourceId.NativeObject, objectData.nativeObjectIndex));
                    break;
                default:
                    UpdateRootObjects(default);
                    break;
            }
        }

        public void UpdateToAllocation(long item)
        {
            m_ShortestPathToRootReferenceTree = CreateRootItem();
            m_RawReferenceTree = CreateRootItem();
            SetupDepthsFromParentsAndChildren(m_RawReferenceTree);
            Reload();
        }

        public void UpdateRootObjects(SourceIndex item)
        {
            if (item == m_CurrentSelection) return;

            m_CurrentSelection = item;
            m_OtherPartOfSelectedUnityObject = GetAlternativeUnityObjectIndex(item);
            m_SelectionTargets = null;
            m_OtherPartTargets = null;
            if (m_BackgroundThread != null)
            {
                StopBackgroundSearch(waitForExit: true);
                m_BackgroundThreadState = BackGroundThreadState.None;
                m_GUIState = PathsToRootViewGUIState.SearchComplete;
            }

            if (m_CurrentSelection.Valid)
            {
                if (CurrentSelection.CachedSnapshot != null && !CurrentSelection.CachedSnapshot.Valid)
                {
                    Debug.LogError($"a {nameof(PathsToRootDetailView)} was leaked");
                    return;
                }
                // Shortest path to root isn't really a search and multithreading
                // it is totally overkill, so we do it here synchronously.
                BuildShortestPath(item);
                if (m_ShortestPathToRootReferenceTree != null)
                {
                    SetupDepthsFromParentsAndChildren(m_ShortestPathToRootReferenceTree);
                }
                m_BackgroundThreadState = BackGroundThreadState.Analyze;
                m_BackgroundSearchCancellation = new CancellationTokenSource();
                var token = m_BackgroundSearchCancellation.Token;
                m_BackgroundThread = new Thread(() => AnalysisThread(item, token));
                m_BackgroundThread.Start();
            }
            else
            {
                UIElementsHelper.SetVisibility(m_ShortestPathButton, false);
                NoObject(m_CachedSnapshot, ref m_ShortestPathToRootReferenceTree);
                NoObject(m_CachedSnapshot, ref m_RawReferenceTree);
                NoObject(m_CachedSnapshot, ref m_ReferencesToTree);
                Reload();
            }
        }

        static PathsToRootDetailTreeViewItem GenerateTreeWithoutCircularRefs(PathsToRootDetailTreeViewItem data)
        {
            var pathsToRootTreeViewItem = new PathsToRootDetailTreeViewItem(data) { children = new List<TreeViewItem>() };
            SearchChildren(pathsToRootTreeViewItem, data);

            return pathsToRootTreeViewItem;
        }

        static void SearchChildren(PathsToRootDetailTreeViewItem pathsToRootDetailTreeViewItem, PathsToRootDetailTreeViewItem data)
        {
            pathsToRootDetailTreeViewItem.children = new List<TreeViewItem>();
            foreach (var child in data.children)
            {
                var viewItem = new PathsToRootDetailTreeViewItem((PathsToRootDetailTreeViewItem)child);
                if (viewItem.HasCircularReference) continue;

                pathsToRootDetailTreeViewItem.children.Add(viewItem);
                pathsToRootDetailTreeViewItem.children.Last().parent = pathsToRootDetailTreeViewItem;
                SearchChildren(viewItem, (PathsToRootDetailTreeViewItem)child);
            }
        }

        /// <summary>
        /// Cancels the running reference search. Thread.Abort is unsupported on CoreCLR, so the search stops at its next cancellation check.
        /// </summary>
        void StopBackgroundSearch(bool waitForExit)
        {
            if (m_BackgroundThread == null)
                return;
            m_BackgroundSearchCancellation.Cancel();
            if (waitForExit)
            {
                m_BackgroundThread.Join();
                m_BackgroundSearchCancellation.Dispose();
            }
            m_BackgroundSearchCancellation = null;
            m_BackgroundThread = null;
        }

        void AnalysisThread(SourceIndex item, CancellationToken token)
        {
            switch (m_BackgroundThreadState)
            {
                case BackGroundThreadState.None:
                    break;
                case BackGroundThreadState.Analyze:
                    SearchForRootObjects(item, token);
                    if (token.IsCancellationRequested)
                        return;
                    m_RawReferenceTree = GenerateTreeWithoutCircularRefs(m_RawReferenceTree);
                    SetupDepthsFromParentsAndChildren(m_RawReferenceTree);
                    if (CurrentSelection.CachedSnapshot != null && !CurrentSelection.CachedSnapshot.Valid)
                    {
                        Debug.Log("Aborting threaded Path To Roots calculation because the snapshot was unloaded");
                        return;
                    }
                    if (token.IsCancellationRequested)
                        return;
                    GatherReferencesTo(item);

                    if (token.IsCancellationRequested)
                        return;
                    m_BackgroundThreadState = BackGroundThreadState.AnalyzeDone;
                    break;
                case BackGroundThreadState.AnalyzeDone:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        void GatherReferencesTo(SourceIndex item)
        {
            if (m_CachedSnapshot == null)
            {
                Debug.Log(" no snapshot set");
                return;
            }

            if (!item.Valid)
            {
                m_ReferencesToTree = DefaultItem();
                return;
            }

            ObjectConnection.GenerateReferencesTo(m_CachedSnapshot, item, m_CachedObjectDataList,
                addManagedObjectsWithFieldInfo: true, foundSourceIndices: m_ReferenceSearchAccelerator);

            m_ReferencesToTree = CreateRootItem();
            foreach (var data in m_CachedObjectDataList)
            {
                m_ReferencesToTree.AddChild(new PathsToRootDetailTreeViewItem(data, m_CachedSnapshot, truncateTypeNames, treeType: ActiveTree.ReferencesTo));
            }
        }

        #region DebugHelpers
        /* we can use this to verify if the scene hierarchy is correctly being rebuilt from the snapshot data
        void SceneHierachyFromSceneRootsTransformTree()
        {
            if (m_CachedSnapshot == null)`
            {
                Debug.Log(" no snapshot set");
                return;
            }
            m_ObjectsProcessed = 0;
            m_ProcessingStackSize = 0;
            m_GUIState = PathsToRootViewGUIState.Searching;

            m_BackingData = CreateRootItem();

            for(int i = 0 ; i < m_CachedSnapshot.SceneRoots.SceneHierarchies.Length; i++)
            {
                var sceneRoot = new PathsToRootDetailTreeViewItem();
                sceneRoot.displayName = m_CachedSnapshot.SceneRoots.Name[i];
                m_BackingData.AddChild(sceneRoot);

                foreach (var child in m_CachedSnapshot.SceneRoots.SceneHierarchies[i].Children)
                {
                    var item = new PathsToRootDetailTreeViewItem(ObjectData.FromNativeObjectIndex(m_CachedSnapshot, m_CachedSnapshot.NativeObjects.instanceId2Index[child.GameObjectID]), m_CachedSnapshot);
                    sceneRoot.AddChild(item);
                    AddTransforms(child, item);
                }
            }

            m_GUIState = PathsToRootViewGUIState.SearchComplete;
        }

        void AddTransforms(CachedSnapshot.SceneRootEntriesCache.TransformTree transformTree, PathsToRootDetailTreeViewItem item)
        {
            foreach (var child in transformTree.Children)
            {
                var newChild = new PathsToRootDetailTreeViewItem(ObjectData.FromNativeObjectIndex(m_CachedSnapshot, m_CachedSnapshot.NativeObjects.instanceId2Index[child.GameObjectID]), m_CachedSnapshot);
                item.AddChild(newChild);
                AddTransforms(child, newChild);
            }
        }*/

        /*fun method that can be used to recreate the scene hierarchy when the snapshot has the scene roots data
        void ReconstructScene()
        {
            if (m_CachedSnapshot == null)
            {
                Debug.Log(" no snapshot set");
                return;
            }
            m_ObjectsProcessed = 0;
            m_ProcessingStackSize = 0;
            m_GUIState = PathsToRootViewGUIState.Searching;
            var processingStack = new Stack<PathsToRootDetailTreeViewItem>();

            m_BackingData = CreateRootItem();

            for (int i = 0; i < m_CachedSnapshot.SceneRoots.AllRootTransformInstanceIds.Count; i++)
            {
                var rootId = m_CachedSnapshot.SceneRoots.AllRootTransformInstanceIds[i];
                var current = new PathsToRootDetailTreeViewItem(ObjectData.FromNativeObjectIndex(m_CachedSnapshot, m_CachedSnapshot.NativeObjects.instanceId2Index[rootId]), m_CachedSnapshot);

                var allObjectConnectingTo = ObjectConnection.GetAllReferencedObjects(m_CachedSnapshot, current.Data);
                if (allObjectConnectingTo == null) continue;

                foreach (var objectData in allObjectConnectingTo)
                {
                    if (objectData.IsGameObject(m_CachedSnapshot) &&
                        objectData.isNative &&
                        m_CachedSnapshot.NativeObjects.ObjectName[objectData.nativeObjectIndex] == m_CachedSnapshot.NativeObjects.ObjectName[m_CachedSnapshot.NativeObjects.instanceId2Index[rootId]])
                    {
                        var go = new PathsToRootDetailTreeViewItem(objectData, m_CachedSnapshot);
                        m_BackingData.AddChild(go);
                        processingStack.Push(go);
                    }
                }
            }

            while (processingStack.Count > 0)
            {
                var current = processingStack.Pop();
                m_ObjectsProcessed++;

                var objData = ObjectConnection.GetAllReferencedObjects(m_CachedSnapshot, current.Data);
                if (objData == null) continue;

                //filter on transforms
                if (m_CachedSnapshot.NativeObjects.NativeTypeArrayIndex[current.Data.nativeObjectIndex] == m_CachedSnapshot.NativeTypes.GameObjectIdx)
                {
                    var tmp = new List<ObjectData>();
                    for (int i = 0; i < objData.Length; i++)
                    {
                        if (m_CachedSnapshot.NativeObjects.NativeTypeArrayIndex[objData[i].nativeObjectIndex] == m_CachedSnapshot.NativeTypes.GameObjectIdx)
                        {
                            tmp.Add(objData[i]);
                        }
                    }

                    objData = tmp.ToArray();
                }

                foreach (var objectData in objData)
                {
                    if (objectData.dataType == ObjectDataType.Unknown) continue;

                    var child = new PathsToRootDetailTreeViewItem(objectData, m_CachedSnapshot, current);
                    current.AddChild(child);

                    //global is root so dont push it
                    if (!child.HasCircularReference)
                    {
                        processingStack.Push(child);
                        m_ProcessingStackSize++;
                    }
                }
            }
            m_GUIState = PathsToRootViewGUIState.SearchComplete;
        }*/


        #endregion

        void SearchForRootObjects(SourceIndex item, CancellationToken token)
        {
            if (m_CachedSnapshot == null)
            {
                Debug.Log(" no snapshot set");
                return;
            }

            if (!item.Valid)
            {
                m_RawReferenceTree = DefaultItem();
                return;
            }

            m_ObjectsProcessed = 0;
            m_ProcessingStackSize = 0;
            m_GUIState = PathsToRootViewGUIState.Searching;
            m_RawReferenceTree = CreateRootItem();

            ObjectConnection.GetAllReferencingObjects(m_CachedSnapshot, item, m_CachedObjectDataList, m_ReferenceSearchAccelerator);

            if (m_CachedObjectDataList.Count == 0)
            {
                NoReferenceObject(m_RawReferenceTree);
                m_GUIState = PathsToRootViewGUIState.SearchComplete;
                return;
            }

            var processingQueue = new Queue<PathsToRootDetailTreeViewItem>();
            m_RawReferenceTree.children.Clear();

            foreach (var objectData in m_CachedObjectDataList)
            {
                m_RawReferenceTree.AddChild(new PathsToRootDetailTreeViewItem(objectData, m_CachedSnapshot, truncateTypeNames));
            }

            foreach (var treeViewItem in m_RawReferenceTree.children)
            {
                var objectTreeChild = (PathsToRootDetailTreeViewItem)treeViewItem;
                processingQueue.Enqueue(objectTreeChild);
            }

            m_ProcessingStackSize = processingQueue.Count;

            RawDataSearch(processingQueue, token);

            m_GUIState = PathsToRootViewGUIState.SearchComplete;
        }

        SourceIndex GetAlternativeUnityObjectIndex(SourceIndex index)
        {
            if (index.Id is SourceIndex.SourceId.NativeObject)
            {
                var managedIndex = m_CachedSnapshot.NativeObjects.ManagedObjectIndex[index.Index];
                if (managedIndex != NativeObjectEntriesCache.InvalidObjectIndex)
                {
                    return new SourceIndex(SourceIndex.SourceId.ManagedObject, managedIndex);
                }
            }
            else if (index.Id is SourceIndex.SourceId.ManagedObject)
            {
                var nativeIndex = m_CachedSnapshot.CrawledData.ManagedObjects[index.Index].NativeObjectIndex;
                if (nativeIndex != NativeObjectEntriesCache.InvalidObjectIndex)
                {
                    return new SourceIndex(SourceIndex.SourceId.NativeObject, nativeIndex);
                }
            }

            return default;
        }

        void BuildShortestPath(SourceIndex currentIndex)
        {
            m_ShortestPathToRootReferenceTree = CreateRootItem();
            if (!m_CachedSnapshot.RootAndImpactInfo.SuccessfullyBuilt)
            {
                return;
            }

            SourceIndex alternativeObject = GetAlternativeUnityObjectIndex(currentIndex);
            ref readonly var currentPathStep = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_CachedSnapshot.RootAndImpactInfo.ShortestPathInfo, currentIndex);

            if (alternativeObject.Valid)
            {
                ref readonly var alternativePathStep = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_CachedSnapshot.RootAndImpactInfo.ShortestPathInfo, alternativeObject);
                if (alternativePathStep.Valid && alternativePathStep.Root != currentPathStep.Root)
                {
                    BuildShortestPath(alternativeObject, in alternativePathStep);
                }
            }

            BuildShortestPath(currentIndex, in currentPathStep);
        }

        long BuildShortestPath(SourceIndex currentIndex, in ShortestRootPathInfo currentPathStep)
        {
            var shortestPathLength = -1L;
            if (currentPathStep.Valid)
            {
                shortestPathLength = currentPathStep.Depth;
                var currentTreeNode = m_ShortestPathToRootReferenceTree;
                // add the selected Item to the list
                var selectedItemTreeNode = new PathsToRootDetailTreeViewItem(ObjectData.FromSourceLink(m_CachedSnapshot, currentIndex), m_CachedSnapshot, currentTreeNode, truncateTypeNames, ActiveTree.ShortestPath);
                currentTreeNode.AddChild(selectedItemTreeNode);
                if (currentPathStep.IsRoot)
                    return shortestPathLength;
                // A Stack/Finalizer/Other Root wrapper always targets exactly one object (see
                // SeedManagedRootsOfType), so it can only ever appear as the immediate parent of that single
                // target — never several hops further up the chain. It has no status or identity of its own
                // to show once its target reports its own Held status directly, so skip its row entirely
                // when it's also the root (nothing beyond it).
                if (currentPathStep.Parent.Id is SourceIndex.SourceId.ManagedRoot)
                {
                    ref readonly var wrapperPathStep = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(
                        in m_CachedSnapshot.RootAndImpactInfo.ShortestPathInfo, currentPathStep.Parent);
                    if (wrapperPathStep.Valid && wrapperPathStep.IsRoot)
                        return shortestPathLength;
                }
                // Since this is a flat list to root, we don't need to nest the children
                // uncomment to get a more traditional tree view though
                // currentTreeNode = selectedItemTreeNode;
                while (currentPathStep.Valid)
                {
                    var parent = currentPathStep.Parent;
                    var data = ObjectData.FromSourceLink(m_CachedSnapshot, parent);

                    var parentTreeNode = new PathsToRootDetailTreeViewItem(data, m_CachedSnapshot, currentTreeNode, truncateTypeNames, ActiveTree.ShortestPath);
                    currentTreeNode.AddChild(parentTreeNode);
                    // Since this is a flat list to root, we don't need to nest the children
                    // uncomment to get a more traditional tree view though
                    // currentTreeNode = parentTreeNode;

                    // if the selected item already is the root, break
                    if (currentPathStep.IsRoot)
                        break;
                    currentPathStep = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_CachedSnapshot.RootAndImpactInfo.ShortestPathInfo, parent);
                    // if the parent is the root, we already added it, so break
                    if (currentPathStep.IsRoot)
                        break;
                }
            }
            return shortestPathLength;
        }

        void RawDataSearch(Queue<PathsToRootDetailTreeViewItem> processingQueue, CancellationToken token)
        {
            while (processingQueue.Count > 0 && m_ObjectsProcessed < 1000 && !token.IsCancellationRequested)
            {
                var current = processingQueue.Dequeue();
                m_ObjectsProcessed++;

                current.Data.GetAllReferencingObjects(m_CachedSnapshot, m_CachedObjectDataList, m_ReferenceSearchAccelerator);
                if (m_CachedObjectDataList.Count == 0) continue;

                foreach (var connection in m_CachedObjectDataList)
                {
                    if (connection.IsUnknownDataType()) continue;

                    var child = new PathsToRootDetailTreeViewItem(connection.displayObject, m_CachedSnapshot, current, truncateTypeNames);
                    current.AddChild(child);

                    if (!child.HasCircularReference)
                    {
                        processingQueue.Enqueue(child);
                        m_ProcessingStackSize++;
                    }
                }
            }
        }

        void SearchUsingFallBack(Stack<PathsToRootDetailTreeViewItem> processingStack)
        {
            while (processingStack.Count > 0 && m_ObjectsProcessed < 10000)
            {
                var current = processingStack.Pop();
                m_ObjectsProcessed++;

                ObjectConnection.GetAllReferencingObjects(m_CachedSnapshot, current.Data.GetSourceLink(m_CachedSnapshot), m_CachedObjectDataList, m_ReferenceSearchAccelerator);
                if (m_CachedObjectDataList.Count == 0) continue;


                if (ContainsGameObjects(m_CachedObjectDataList))
                {
                    Filter(ref m_CachedObjectDataList, new int[] { m_CachedSnapshot.NativeTypes.GameObjectIdx });
                    foreach (var objectData in m_CachedObjectDataList)
                    {
                        current.AddChild(new PathsToRootDetailTreeViewItem(objectData, m_CachedSnapshot, truncateTypeNames));
                    }
                    continue;
                }

                foreach (var objectData in m_CachedObjectDataList)
                {
                    if (objectData.dataType == ObjectDataType.Unknown) continue;

                    var child = new PathsToRootDetailTreeViewItem(objectData, m_CachedSnapshot, current, truncateTypeNames);
                    current.AddChild(child);

                    if (!child.HasCircularReference)
                    {
                        processingStack.Push(child);
                        m_ProcessingStackSize++;
                    }
                }
            }
        }

        void SearchUsingSceneRoots(long item, ObjectData itemObjectData, ObjectData[] objectsConnectingTo, Stack<PathsToRootDetailTreeViewItem> processingStack)
        {
            while (processingStack.Count > 0 && m_ObjectsProcessed < 10000)
            {
                var current = processingStack.Pop();
                m_ObjectsProcessed++;

                ObjectConnection.GetAllReferencingObjects(m_CachedSnapshot, current.Data.GetSourceLink(m_CachedSnapshot), m_CachedObjectDataList, m_ReferenceSearchAccelerator);
                FilterToOnlyTransforms(ref m_CachedObjectDataList);
                if (m_CachedObjectDataList.Count == 0) continue;

                foreach (var objectData in m_CachedObjectDataList)
                {
                    if (objectData.dataType == ObjectDataType.Unknown) continue;

                    var child = new PathsToRootDetailTreeViewItem(objectData, m_CachedSnapshot, current, truncateTypeNames);
                    current.AddChild(child);

                    if (child.Data.IsSceneRootTransform(m_CachedSnapshot))
                    {
                        ObjectConnection.GetAllReferencingObjects(m_CachedSnapshot, child.Data.GetSourceLink(m_CachedSnapshot), m_CachedObjectDataListInnerLoop, m_ReferenceSearchAccelerator);
                        foreach (var data in m_CachedObjectDataListInnerLoop)
                        {
                            if (data.IsGameObject(m_CachedSnapshot))
                                child.AddChild(new PathsToRootDetailTreeViewItem(data, m_CachedSnapshot, truncateTypeNames));
                        }
                    }
                    else if (!child.HasCircularReference)
                    {
                        processingStack.Push(child);
                        m_ProcessingStackSize++;
                    }
                }
            }
        }

        bool ContainsGameObjects(List<ObjectData> objData)
        {
            foreach (var objectData in objData)
            {
                if (objectData.IsGameObject(m_CachedSnapshot))
                    return true;
            }

            return false;
        }

        List<ObjectData> m_CachedObjectDataListFiltered = new List<ObjectData>();
        void FilterToOnlyTransforms(ref List<ObjectData> objectData)
        {
            if (objectData != null)
            {
                foreach (var od in objectData)
                {
                    if (od.IsTransform(m_CachedSnapshot))
                        m_CachedObjectDataListFiltered.Add(od);
                }
            }
            objectData.Clear();
            objectData.AddRange(m_CachedObjectDataListFiltered);
        }

        void Filter(ref List<ObjectData> listToFilter, int[] idToFilterOn)
        {
            foreach (var od in listToFilter)
            {
                var id = m_CachedSnapshot.NativeObjects.NativeTypeArrayIndex[od.nativeObjectIndex];
                if (idToFilterOn.Contains(id))
                    m_CachedObjectDataListFiltered.Add(od);
            }
            listToFilter.Clear();
            listToFilter.AddRange(m_CachedObjectDataListFiltered);
        }

        void NoReferenceObject(PathsToRootDetailTreeViewItem item)
        {
            item = CreateRootItem();
        }

        void NoObject(CachedSnapshot cs, ref PathsToRootDetailTreeViewItem tree)
        {
            CurrentSelection = default;
            m_CurrentSelection = default;
            m_OtherPartOfSelectedUnityObject = default;
            tree = CreateRootItem();
        }

        protected override TreeViewItem BuildRoot()
        {
            if (m_RawReferenceTree == null)
            {
                m_RawReferenceTree = new PathsToRootDetailTreeViewItem();
                SetupDepthsFromParentsAndChildren(m_RawReferenceTree);
            }

            if (m_ReferencesToTree == null)
            {
                m_ReferencesToTree = new PathsToRootDetailTreeViewItem();
                SetupDepthsFromParentsAndChildren(m_ReferencesToTree);
            }
            // The TreeView gets initialized before a snapshot is assigned. In that case, don't mess with the ActiveTree.
            if (m_CachedSnapshot?.RootAndImpactInfo.SuccessfullyBuilt ?? true)
            {
                UIElementsHelper.SetVisibility(m_ShortestPathButton, true);
            }
            else
            {
                // Only show the shortest path button when we have a shortest path
                UIElementsHelper.SetVisibility(m_ShortestPathButton, false);
                if (m_ActiveTree == ActiveTree.ShortestPath)
                {
                    SetActiveTree(ActiveTree.RawReferences, persist: false);
                }
            }
            ResyncRibbonToActiveTree();

            GenerateIcons(m_ShortestPathToRootReferenceTree);
            GenerateIcons(m_RawReferenceTree);
            GenerateIcons(m_ReferencesToTree);
            m_ShortestPathButton.text = TextContent.PathsToRootShortestPathToRootRiderCaption;
            m_RawConnectionButton.text = string.Format(TextContent.PathsToRootReferencedByRiderCaption, m_RawReferenceTree.children.Count);
            m_ReferencesToButton.text = string.Format(TextContent.PathsToRootReferencesToRiderCaption, m_ReferencesToTree.children.Count);
            switch (m_ActiveTree)
            {
                case ActiveTree.ShortestPath:
                    if (m_ShortestPathToRootReferenceTree.children.Count > 0)
                        // Select the currently selected item which is only added to the Shortest Path tree and none of the other trees
                        SetSelection(new List<int> { m_ShortestPathToRootReferenceTree.children[0].id });
                    return m_ShortestPathToRootReferenceTree;
                case ActiveTree.RawReferences:
                    return m_RawReferenceTree;
                case ActiveTree.ReferencesTo:
                    return m_ReferencesToTree;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        static PathsToRootDetailTreeViewItem InvertPathsToRoot(PathsToRootDetailTreeViewItem rootDetail)
        {
            var stack = new Stack<PathsToRootDetailTreeViewItem>();

            var viewItem = new PathsToRootDetailTreeViewItem((PathsToRootDetailTreeViewItem)rootDetail.children[0]);
            RecursiveFindAllLeafNodes(viewItem, stack);
            var id = 0;
            rootDetail.children = new List<TreeViewItem>();
            while (stack.Count > 0)
            {
                var pathsToRootDetailTreeViewItem = stack.Pop();

                var items = new List<PathsToRootDetailTreeViewItem>();
                while (pathsToRootDetailTreeViewItem.parent != null)
                {
                    var item = pathsToRootDetailTreeViewItem;
                    item.children = null;
                    items.Add(new PathsToRootDetailTreeViewItem(item));
                    pathsToRootDetailTreeViewItem = (PathsToRootDetailTreeViewItem)pathsToRootDetailTreeViewItem.parent;
                }

                // -2 to exclude the treeview root and the actual selected object
                for (var j = 0; j < items.Count - 2; j++)
                {
                    items[j].children = new List<TreeViewItem> { items[j + 1] };
                }

                foreach (var treeViewItem in items)
                {
                    treeViewItem.id = id++;
                }

                if (items.Count > 0)
                    rootDetail.children.Add(items[0]);
            }
            return rootDetail;
        }

        static void RecursiveFindAllLeafNodes(PathsToRootDetailTreeViewItem pathsToRootDetailTreeViewItem, Stack<PathsToRootDetailTreeViewItem> stack)
        {
            if (!pathsToRootDetailTreeViewItem.hasChildren)
            {
                stack.Push(pathsToRootDetailTreeViewItem);
                return;
            }

            foreach (var child in pathsToRootDetailTreeViewItem.children)
            {
                RecursiveFindAllLeafNodes((PathsToRootDetailTreeViewItem)child, stack);
            }
        }

        public static MultiColumnHeaderState CreateDefaultMultiColumnHeaderState()
        {
            // Order matches PathsToRootViewColumns: MultiColumnHeader.ToggleVisibility sorts visibleColumns
            // ascending, so enum order IS left-to-right column order.
            var columns = new[]
            {
                new MultiColumnHeaderState.Column
                {
                    headerContent = TextContent.PathsToRootColumnDepthHeader,
                    contextMenuText = TextContent.PathsToRootColumnDepthHeader.text,
                    headerTextAlignment = TextAlignment.Right,
                    sortedAscending = true,
                    sortingArrowAlignment = TextAlignment.Left,
                    width = 56,
                    minWidth = 56,
                    maxWidth = 80,
                    autoResize = false,
                    allowToggleVisibility = true,
                },
                new MultiColumnHeaderState.Column
                {
                    headerContent = TextContent.PathsToRootColumnReferrersHeader,
                    contextMenuText = TextContent.PathsToRootColumnReferrersMenuText,
                    headerTextAlignment = TextAlignment.Right,
                    sortedAscending = false,
                    sortingArrowAlignment = TextAlignment.Left,
                    width = 46,
                    minWidth = 46,
                    maxWidth = 80,
                    autoResize = false,
                    allowToggleVisibility = true,
                },
                new MultiColumnHeaderState.Column
                {
                    headerContent = TextContent.PathsToRootColumnShareOfImpactHeader,
                    contextMenuText = TextContent.PathsToRootColumnShareOfImpactMenuText,
                    headerTextAlignment = TextAlignment.Right,
                    sortedAscending = false,
                    sortingArrowAlignment = TextAlignment.Left,
                    width = 76,
                    minWidth = 76,
                    maxWidth = 140,
                    autoResize = false,
                    allowToggleVisibility = true,
                },
                new MultiColumnHeaderState.Column
                {
                    headerContent = TextContent.PathsToRootColumnTotalImpactHeader,
                    contextMenuText = TextContent.PathsToRootColumnTotalImpactMenuText,
                    headerTextAlignment = TextAlignment.Right,
                    sortedAscending = false,
                    sortingArrowAlignment = TextAlignment.Left,
                    width = 64,
                    minWidth = 64,
                    maxWidth = 140,
                    autoResize = false,
                    allowToggleVisibility = true,
                },
                new MultiColumnHeaderState.Column
                {
                    headerContent = TextContent.PathsToRootColumnDetailsHeader,
                    headerTextAlignment = TextAlignment.Left,
                    sortedAscending = true,
                    sortingArrowAlignment = TextAlignment.Center,
                    width = 300,
                    minWidth = 60,
                    autoResize = true,
                    allowToggleVisibility = false
                },
                new MultiColumnHeaderState.Column
                {
                    headerContent = TextContent.PathsToRootColumnFlagsHeader,
                    contextMenuText = TextContent.PathsToRootColumnFlagsHeader.text,
                    headerTextAlignment = TextAlignment.Left,
                    sortedAscending = true,
                    canSort = false,
                    sortingArrowAlignment = TextAlignment.Center,
                    width = 40,
                    minWidth = 40,
                    maxWidth = 40,
                    autoResize = false,
                    allowToggleVisibility = true,
                }
            };

            Assert.AreEqual(columns.Length, Enum.GetValues(typeof(PathsToRootViewColumns)).Length,
                "Number of columns should match number of enum values: You probably forgot to update one of them.");

            var state = new MultiColumnHeaderState(columns);
            // Real per-rider visibility is applied by ApplyColumnVisibilityForActiveTree once the view is
            // constructed; this is just a safe fallback state.
            state.visibleColumns = new[] { (int)PathsToRootViewColumns.Details };
            return state;
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            var item = args.item;

            var isMainSelection = item.id == k_CurrentSelectionTreeViewItemId;
            if (isMainSelection)
            {
                // Draw dark row background for current selection in main window.
                var color = GUI.color;
                GUI.color = k_CurrentSelectionTreeViewItemBackgroundColor;
                GUI.DrawTexture(args.rowRect, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, false);
                GUI.color = color;
            }

            for (int i = 0; i < args.GetNumVisibleColumns(); ++i)
            {
                CellGUI(args.GetCellRect(i), item, (PathsToRootViewColumns)args.GetColumn(i), ref args);
            }
        }

        public void DoGUI(Rect rect)
        {
            if (m_RiderNeedsRibbonSync)
                ResyncRibbonToActiveTree();

            switch (m_BackgroundThreadState)
            {
                case BackGroundThreadState.None:
                    break;
                case BackGroundThreadState.Analyze:
                    break;
                case BackGroundThreadState.AnalyzeDone:
                    StopBackgroundSearch(waitForExit: true);
                    Reload();
                    m_BackgroundThreadState = BackGroundThreadState.None;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            switch (m_GUIState)
            {
                case PathsToRootViewGUIState.NothingSelected:
                    break;
                case PathsToRootViewGUIState.Searching:
                    DoSearchingGUI(rect);
                    break;
                case PathsToRootViewGUIState.SearchComplete:
                    OnGUI(rect);
                    if (m_BackgroundThreadState == BackGroundThreadState.Analyze)
                        Repaint(); // Update UI while the BG thread finishes up - PROFB-153.
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        void GenerateIcons(PathsToRootDetailTreeViewItem item)
        {
            item.SetIcons(m_CachedSnapshot);
            if (!item.hasChildren) return;
            foreach (var child in item.children)
            {
                GenerateIcons(child as PathsToRootDetailTreeViewItem);
            }
        }

        void DoSearchingGUI(Rect rect)
        {
            Rect iconRect = new Rect(rect.x, rect.y + (rect.height / 2), 30, 30);
            Rect textRect = new Rect(rect.x + 35, rect.y + (rect.height / 2), rect.width - 35, 30);
            GUI.Label(textRect, "Processing " + m_ObjectsProcessed + " of " + m_ProcessingStackSize);
            Repaint();
        }

        static ObjectOrTypeLabel.DataType GetRowDataType(CachedSnapshot cs, ObjectData data)
        {
            if (data.dataType == ObjectDataType.GCHandle)
                return ObjectOrTypeLabel.DataType.GCHandle;
            var unityObjectInfo = new UnifiedUnityObjectInfo(cs, data);
            if (!unityObjectInfo.Type.IsUnityObjectType)
                return data.isManaged ? ObjectOrTypeLabel.DataType.ManagedObject : ObjectOrTypeLabel.DataType.NativeObject;
            var sourceIndex = data.GetSourceLink(cs);
            var dataType = ObjectOrTypeLabel.GetDataTypeFromStatus(cs, in unityObjectInfo, sourceIndex);
            if (unityObjectInfo.IsFullUnityObject)
                dataType = ObjectOrTypeLabel.ToHalfVariant(dataType, sourceIndex.Id);
            return dataType;
        }

        void CellGUI(Rect cellRect, TreeViewItem item, PathsToRootViewColumns column, ref RowGUIArgs args)
        {
            // Center cell rect vertically (makes it easier to place controls, icons etc in the cells)
            CenterRectUsingSingleLineHeight(ref cellRect);
            var pathsToRootDetailTreeViewItem = (PathsToRootDetailTreeViewItem)item;
            switch (column)
            {
                case PathsToRootViewColumns.Depth:
                case PathsToRootViewColumns.RefereeCount:
                case PathsToRootViewColumns.ShareOfImpact:
                case PathsToRootViewColumns.TotalImpact:
                {
                    if (m_CachedSnapshot == null)
                        break;

                    EnsureImpactInfo(pathsToRootDetailTreeViewItem);
                    var content = column switch
                    {
                        PathsToRootViewColumns.Depth => pathsToRootDetailTreeViewItem.DepthCellContent,
                        PathsToRootViewColumns.RefereeCount => pathsToRootDetailTreeViewItem.ReferrerCountCellContent,
                        PathsToRootViewColumns.ShareOfImpact => pathsToRootDetailTreeViewItem.ShareOfImpactCellContent,
                        PathsToRootViewColumns.TotalImpact => pathsToRootDetailTreeViewItem.TotalImpactCellContent,
                        _ => null,
                    };
                    if (content != null)
                        GUI.Label(cellRect, content, LabelStyles.RightAlignedLabel);
                }
                break;
                case PathsToRootViewColumns.Details:
                {
                    var indent = GetContentIndent(item);
                    cellRect.x += indent;
                    cellRect.width -= indent;

                    var isRootOnShortestPathToRoot = false;
                    var isCurrentSelectionOnShortestPathToRoot = false;
                    if (m_ActiveTree is ActiveTree.ShortestPath && m_CachedSnapshot.RootAndImpactInfo.SuccessfullyBuilt)
                    {
                        var sourceIndex = pathsToRootDetailTreeViewItem.Data.displayObject.GetSourceLink(m_CachedSnapshot);
                        ref readonly var rootStep = ref SourceIndexToRootAndImpactInfoMapper.GetNestedElement(in m_CachedSnapshot.RootAndImpactInfo.ShortestPathInfo, sourceIndex);
                        isRootOnShortestPathToRoot = rootStep.IsRoot;
                        if (m_CurrentSelection != sourceIndex && (!m_OtherPartOfSelectedUnityObject.Valid || m_OtherPartOfSelectedUnityObject != sourceIndex))
                        {
                            const int kWidth = 15;
                            var pathText = (isRootOnShortestPathToRoot ? "└" : "│");
                            GUI.Label(new Rect(cellRect.x, cellRect.y, kWidth, 20), pathText);
                            cellRect.x += kWidth;
                            cellRect.width -= kWidth;
                        }
                        else
                        {
                            isCurrentSelectionOnShortestPathToRoot = true;
                        }
                    }

                    if (pathsToRootDetailTreeViewItem.Data.IsValid)
                        GUI.DrawTexture(new Rect(cellRect.x, cellRect.y, 20, 20),
                            Styles.GetDataTypeIcon(GetRowDataType(m_CachedSnapshot, pathsToRootDetailTreeViewItem.Data)));
                    cellRect.x += 20;
                    cellRect.width -= 20;
                    if (pathsToRootDetailTreeViewItem.icon != null)
                        GUI.Label(new Rect(cellRect.x, cellRect.y, 20, 20), pathsToRootDetailTreeViewItem.TypeIcon);
                    var typeNameRect = new Rect(cellRect.x + 20, cellRect.y, cellRect.width - 20, cellRect.height);

                    var text = $"{(truncateTypeNames ? pathsToRootDetailTreeViewItem.TruncatedTypeName : pathsToRootDetailTreeViewItem.TypeName)} \"{pathsToRootDetailTreeViewItem.displayName}\"";

                    if (pathsToRootDetailTreeViewItem.IsSceneRoot(m_CachedSnapshot))
                        text = $"SceneRoot {text}";
                    if (isRootOnShortestPathToRoot)
                        text = $"<b>ROOT:</b> {text}";
                    var isMainSelection = item.id == k_CurrentSelectionTreeViewItemId;
                    if (isMainSelection || isRootOnShortestPathToRoot || isCurrentSelectionOnShortestPathToRoot)
                    {
                        GUI.Label(typeNameRect, text, LabelStyles.TypeLabelBold);
                    }
                    else
                        GUI.Label(typeNameRect, text, LabelStyles.TypeLabel);

                    if (m_ActiveTree is not ActiveTree.ShortestPath && pathsToRootDetailTreeViewItem.IsSceneRoot(m_CachedSnapshot) || pathsToRootDetailTreeViewItem.Data.dataType is ObjectDataType.Scene)
                    {
                        var size = GUI.skin.label.CalcSize(new GUIContent(text));
                        GUI.DrawTexture(new Rect(typeNameRect.x + size.x, typeNameRect.y, size.y, size.y), Styles.SceneIcon.image);
                    }
                }
                break;
                case PathsToRootViewColumns.Flags:
                {
                    if (pathsToRootDetailTreeViewItem.HasCircularReference)
                    {
                        if (GUI.Button(cellRect, Styles.CircularRefContent))
                        {
                            SetSelection(new[] { pathsToRootDetailTreeViewItem.CircularRefId }, TreeViewSelectionOptions.FireSelectionChanged | TreeViewSelectionOptions.RevealAndFrame);
                        }
                    }

                    if (pathsToRootDetailTreeViewItem.HasFlags())
                    {
                        if (GUI.Button(cellRect, pathsToRootDetailTreeViewItem.ObjectFlags))
                        {
                            EditorUtilityCompatibilityHelper.DisplayAlertDialog("Flags info", pathsToRootDetailTreeViewItem.FlagsInfo, "OK");
                        }
                    }
                }
                break;
                default:
                    throw new Exception("Unknown or not implemented PathsToRootColumns enum when doing cell gui");
            }
        }

        static readonly char[] k_GenericBracesChars = { '<', '>' };
        // if we hit something that doesnt truncate properly then use it as a test case in
        // TypeNameTruncationTests and fix it up
        public static string TruncateTypeName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;
            name = name.Replace(" ", "");
            if (name.Contains('.'))
            {
                if (name.Contains('<'))
                {
                    var pos = 0;
                    while (pos < name.Length && name.IndexOf('<', pos + 1) != -1)
                    {
                        pos = name.IndexOf('<', pos + 1);
                        var next = name.IndexOfAny(k_GenericBracesChars, pos + 1);
                        var replacee = name.Substring(pos + 1, next - (pos + 1));
                        if (replacee.Contains(','))
                        {
                            // Split on top-level commas only, respecting angle-bracket nesting, so a
                            // generic argument that is itself generic isn't split mid-type. (Spaces have
                            // already been stripped from 'name' above, so the trimming SplitTypeArgs does
                            // is a no-op here and the subsequent name.Replace(s, ...) still matches.)
                            var split = TypeNameParsingUtility.SplitTypeArgs(replacee);
                            foreach (var s in split)
                            {
                                if (string.IsNullOrEmpty(s))
                                    continue;// Skip empty generic type entries.
                                var truncated = TruncateTypeName(s);
                                pos += truncated.Length;
                                name = name.Replace(s, truncated);
                            }

                            continue;
                        }

                        if (!string.IsNullOrEmpty(replacee))
                        {
                            var truncatedReplacee = TruncateTypeName(replacee);
                            pos += truncatedReplacee.Length;
                            name = name.Replace(replacee, truncatedReplacee);
                        }
                    }
                }


                var nameSplt = name.Split('.');
                name = string.Empty;
                int offset = 1;
                while (offset < nameSplt.Length &&
                    (string.IsNullOrEmpty(nameSplt[nameSplt.Length - offset])
                    || nameSplt[nameSplt.Length - offset].StartsWith("<", StringComparison.Ordinal)))
                    offset++;
                var mainNamePart = nameSplt.Length - offset;
                for (int i = 0; i < nameSplt.Length; i++)
                {
                    if (i < mainNamePart)
                    {
                        if (nameSplt[i].IndexOfAny(k_GenericBracesChars, 0) != -1)
                        {
                            if (!string.IsNullOrEmpty(name))
                                name += $".{nameSplt[i]}";
                            else
                                name = nameSplt[i];
                        }
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(name))
                            name += $".{nameSplt[i]}";
                        else
                            name = nameSplt[i];
                    }
                }
            }

            return name;
        }

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            var item = this.FindRows(selectedIds);
            var source = ((PathsToRootDetailTreeViewItem)item[0]).Data.displayObject.GetSourceLink(m_CachedSnapshot);

            // invalid index means the no object selected object was selected making it safe to ignore the event.
            if (source.Id == CachedSnapshot.SourceIndex.SourceId.None)
                return;

            SelectionChangedEvt?.Invoke(source);

            // Silently ping the current table's row for the newly selected reference when it has one.
            CurrentLocatorFor(source)?.TryReveal(source, frame: true, m_CachedSnapshot);

            // Track selection count
            MemoryProfilerAnalytics.AddReferencePanelInteraction(MemoryProfilerAnalytics.ReferencePanelInteraction.SelectionInTableCount);
        }

        // Resolves the row's source the same way SelectionChanged does. Returns default (invalid) when the
        // "no object selected" placeholder row was clicked.
        CachedSnapshot.SourceIndex GetRowSource(int id)
        {
            var rows = FindRows(new List<int> { id });
            if (rows.Count == 0)
                return default;

            var source = ((PathsToRootDetailTreeViewItem)rows[0]).Data.displayObject.GetSourceLink(m_CachedSnapshot);
            return source.Id == CachedSnapshot.SourceIndex.SourceId.None ? default : source;
        }

        protected override void DoubleClickedItem(int id)
        {
            var source = GetRowSource(id);
            if (!source.Valid)
                return;

            var mouse = Event.current != null ? Event.current.mousePosition : Vector2.zero;
            TableRevealHelper.RevealWithChoice(CurrentLocatorFor(source), m_TabController, m_CachedSnapshot, source, frame: true, select: true, new Rect(mouse, Vector2.zero));
        }

        protected override void ContextClickedItem(int id)
        {
            var source = GetRowSource(id);
            var locator = CurrentLocatorFor(source);
            if (!source.Valid || (locator == null && m_TabController == null))
                return;

            var entries = SelectInMenuBuilder.BuildEntries(m_CachedSnapshot, source, locator, m_TabController, includeSelectInCurrentTable: true);
            if (entries.Count == 0)
                return;

            SelectInMenuBuilder.ShowAsGenericMenu(new Rect(Event.current.mousePosition, Vector2.zero), entries, underSelectInSubmenu: true);
        }

        protected override void ExpandedStateChanged()
        {
            base.ExpandedStateChanged();

            // Track unfolds count
            MemoryProfilerAnalytics.AddReferencePanelInteraction(MemoryProfilerAnalytics.ReferencePanelInteraction.TreeViewElementExpandCount);
        }

        protected override bool CanMultiSelect(TreeViewItem item)
        {
            return false;
        }

        public void Dispose()
        {
            StopBackgroundSearch(waitForExit: true);
            MemoryProfilerSettings.TruncateStateChanged -= OnTruncateStateChanged;
            MemoryProfilerSettings.SortReferencesWithDifferentRootLastChanged -= OnSortReferencesWithDifferentRootLastChanged;
            m_MultiColumnHeader.columnSettingsChanged -= OnColumnSettingsChanged;
            m_MultiColumnHeader.visibleColumnsChanged -= OnVisibleColumnsChanged;
            m_MultiColumnHeader.sortingChanged -= OnSortingChanged;
            SelectionChangedEvt = null;
        }

        public void ClearSecondarySelection()
        {
            SetSelection(new List<int>());
        }
    }
}

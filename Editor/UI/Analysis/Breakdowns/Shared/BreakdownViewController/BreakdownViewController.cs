#if UNITY_2022_1_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Abstract base view controller for memory breakdown views, such as 'Unity Objects' and 'All Of Memory'.
    abstract class BreakdownViewController : ViewController
    {
        const string k_UxmlAssetGuid = "01be1ca7b1544f246b5302f05d9e8c5e";
        const string k_UssClass_Dark = "breakdown-view__dark";
        const string k_UssClass_Light = "breakdown-view__light";
        const string k_UxmlIdentifier_SearchField = "breakdown-view__search-field";
        const string k_UxmlIdentifier_TableSizeBar = "breakdown-view__table-size-bar";
        const string k_UxmlIdentifier_TableContainer = "breakdown-view__table-container";
        const string k_UxmlIdentifier_TableModeDropdown = "breakdown-view__mode-dropdown";

        // Breakdown mode names for dropdown control and table context menu.
        static readonly List<(string title, AllTrackedMemoryTableMode mode, Func<CachedSnapshot, bool> available)> k_BreakdownModes = new()
        {
            ( "Allocated Memory", AllTrackedMemoryTableMode.OnlyCommitted, (x) => true ),
            ( "Resident Memory on Device", AllTrackedMemoryTableMode.OnlyResident, HasResidentMemoryInfo ),
            ( "Allocated and Resident Memory on Device", AllTrackedMemoryTableMode.CommittedAndResident, HasResidentMemoryInfo ),
        };

        // Model.
        readonly string m_Description;

        // View.
        DetailedSizeBar m_TableSizeBar;
        AllTrackedMemoryTableMode m_TableColumnsMode;

        public BreakdownViewController(CachedSnapshot snapshot, string description, ISelectionDetails selectionDetails)
        {
            Snapshot = snapshot;
            m_Description = description;
            SelectionDetails = selectionDetails;

            m_TableColumnsMode = AllTrackedMemoryTableMode.OnlyCommitted;
        }

        public AllTrackedMemoryTableMode TableColumnsMode
        {
            get => m_TableColumnsMode;
            set
            {
                if (m_TableColumnsMode == value)
                    return;

                m_TableColumnsMode = value;
                RefreshTableColumnsDropdown();
                TableColumnsModeChanged?.Invoke(m_TableColumnsMode);
            }
        }
        public event Action<AllTrackedMemoryTableMode> TableColumnsModeChanged;

        // Model.
        protected CachedSnapshot Snapshot { get; }
        protected ISelectionDetails SelectionDetails { get; }

        // View.
        protected ToolbarSearchField SearchField { get; private set; }
        protected VisualElement TableContainer { get; private set; }
        protected DropdownField TableColumnsDropdown { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                SelectionDetails?.ClearSelection();

            base.Dispose(disposing);
        }

        protected override VisualElement LoadView()
        {
            var view = ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlAssetGuid);
            if (view == null)
                throw new InvalidOperationException("Unable to create view from Uxml. Uxml must contain at least one child element.");
            view.style.flexGrow = 1;

            var themeUssClass = (EditorGUIUtility.isProSkin) ? k_UssClass_Dark : k_UssClass_Light;
            view.AddToClassList(themeUssClass);

            GatherReferencesInView(view);

            return view;
        }

        protected override void ViewLoaded()
        {
            TableColumnsDropdown.label = m_Description;
            TableColumnsDropdown.choices = k_BreakdownModes.Select((x) => x.title).ToList();
            TableColumnsDropdown.RegisterValueChangedCallback(OnBreakdownModeDropdown);
            TableColumnsDropdown.SetEnabled(HasResidentMemoryInfo(Snapshot));
            if (!HasResidentMemoryInfo(Snapshot))
            {
                if (IsResidentMemoryIdenticalToAllocatedOnThisPlatform(Snapshot))
                    TableColumnsDropdown.tooltip = TextContent.ResidentAndAllocatedMemoryAreIdentical;
                else if (!HasSystemMemoryResidentPages(Snapshot))
                    TableColumnsDropdown.tooltip = TextContent.NoResidentMemoryInformationOldSnapshot;
                else
                    TableColumnsDropdown.tooltip = TextContent.NoResidentMemoryInformationNotSupported;
            }
            RefreshTableColumnsDropdown();
        }

        protected void RefreshTableSizeBar(
            MemorySize totalMemorySize,
            MemorySize totalGraphicsMemorySize,
            MemorySize totalSnapshotMemorySize)
        {
            ulong total, memoryInTable;
            string sizeText, totalSizeText, sizeTooltip = null, totalSizeTooltip = null;
            MemoryBarElement.VisibilityMode mode;
            if ((m_TableColumnsMode == AllTrackedMemoryTableMode.OnlyCommitted) || (m_TableColumnsMode == AllTrackedMemoryTableMode.CommittedAndResident))
            {
                mode = MemoryBarElement.VisibilityMode.CommittedOnly;
                total = totalSnapshotMemorySize.Committed;
                memoryInTable = totalMemorySize.Committed;
                var totalMemorySizeText = EditorUtility.FormatBytes((long)memoryInTable);
                var totalSnapshotMemorySizeText = EditorUtility.FormatBytes((long)total);
                sizeText = $"Allocated Memory In Table: {totalMemorySizeText}";
                totalSizeText = $"Total Memory In Snapshot: {totalSnapshotMemorySizeText}";
            }
            else
            {
                mode = MemoryBarElement.VisibilityMode.ResidentOnly;
                total = totalSnapshotMemorySize.Resident;
                memoryInTable = totalMemorySize.Resident;
                var totalMemorySizeText = EditorUtility.FormatBytes((long)memoryInTable);
                var totalSnapshotMemorySizeText = EditorUtility.FormatBytes((long)total);
                sizeText = $"Resident Memory In Table: {totalMemorySizeText}";
                sizeTooltip = $"{memoryInTable:N0} B";
                totalSizeText = $"Total Resident Memory In Snapshot: {totalSnapshotMemorySizeText}";
                totalSizeTooltip = $"{total:N0} B";
            }

            m_TableSizeBar.Bar.Mode = mode;
            m_TableSizeBar.SetValue(totalMemorySize, total, total);
            m_TableSizeBar.SetSizeText(sizeText, sizeTooltip);
            m_TableSizeBar.SetTotalText(totalSizeText, totalSizeTooltip);
        }

        protected void RefreshTableSizeBar(
            MemorySize totalMemorySize,
            MemorySize totalSnapshotMemorySize)
        {
            RefreshTableSizeBar(totalMemorySize, new MemorySize(0, 0), totalSnapshotMemorySize);
        }

        protected void GenerateTreeViewContextMenu(ContextualMenuPopulateEvent evt)
        {
            // We want to override menu
            evt.menu.ClearItems();

            void AddMenuItem(DropdownMenu menu, string name, AllTrackedMemoryTableMode mode, bool optionAvailable)
            {
                menu.AppendAction(name,
                    (a) => TableColumnsMode = mode,
                    (a) =>
                    {
                        var disabledFlag = optionAvailable ? DropdownMenuAction.Status.None : DropdownMenuAction.Status.Disabled;
                        if (TableColumnsMode == mode)
                            return DropdownMenuAction.Status.Checked | disabledFlag;
                        else
                            return DropdownMenuAction.Status.Normal | disabledFlag;
                    });
            }

            // Add all modes
            foreach (var item in k_BreakdownModes)
                AddMenuItem(evt.menu, item.title, item.mode, item.available(Snapshot));
        }

        void RefreshTableColumnsDropdown()
        {
            var pair = k_BreakdownModes.First(x => x.mode == m_TableColumnsMode);
            TableColumnsDropdown.value = pair.title;
        }

        void OnBreakdownModeDropdown(ChangeEvent<string> evt)
        {
            var newModeIndex = k_BreakdownModes.FindIndex((x) => x.title == evt.newValue);
            if (newModeIndex == -1)
            {
                Debug.Log("Invalid dropdown value");
                return;
            }

            TableColumnsMode = k_BreakdownModes[newModeIndex].mode;
        }

        static bool HasResidentMemoryInfo(CachedSnapshot snapshot)
        {
            if (IsResidentMemoryIdenticalToAllocatedOnThisPlatform(snapshot))
                return false;

            return HasSystemMemoryResidentPages(snapshot);
        }

        static bool HasSystemMemoryResidentPages(CachedSnapshot snapshot)
        {
            return snapshot?.HasSystemMemoryResidentPages ?? false;
        }

        static bool IsResidentMemoryIdenticalToAllocatedOnThisPlatform(CachedSnapshot snapshot)
        {
            var platform = snapshot?.MetaData.TargetInfo?.RuntimePlatform ?? (RuntimePlatform)~0;
            return PlatformsHelper.IsResidentMemoryBlacklistedPlatform(platform);
        }

        void GatherReferencesInView(VisualElement view)
        {
            SearchField = view.Q<ToolbarSearchField>(k_UxmlIdentifier_SearchField);
            m_TableSizeBar = view.Q<DetailedSizeBar>(k_UxmlIdentifier_TableSizeBar);
            TableContainer = view.Q<VisualElement>(k_UxmlIdentifier_TableContainer);
            TableColumnsDropdown = view.Q<DropdownField>(k_UxmlIdentifier_TableModeDropdown);
        }
    }
}
#endif

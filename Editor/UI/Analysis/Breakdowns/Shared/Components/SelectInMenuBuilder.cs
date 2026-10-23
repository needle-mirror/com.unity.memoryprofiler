using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// One entry in the "Select In..." menu (see <see cref="SelectInMenuBuilder"/>).
    /// </summary>
    struct SelectInMenuEntry
    {
        public string Label;

        /// <summary>Shown for a disabled entry; also carries the Select entry's double-click hint.</summary>
        public string Tooltip;
        public bool Enabled;
        public bool SeparatorBefore;

        /// <summary>Null when <see cref="Enabled"/> is false.</summary>
        public Action OnClick;
    }

    /// <summary>
    /// Builds the ordered "Select In &lt;table&gt;" menu entries for a given object, and renders them
    /// either as an IMGUI <see cref="GenericMenu"/> or appended to a UI Toolkit <see cref="DropdownMenu"/>.
    /// Shared by every place that can act on a <see cref="CachedSnapshot.SourceIndex"/>: the Selected Item
    /// Details panel's single "Select In..." button, table row context menus, and title-label context menus.
    /// </summary>
    static class SelectInMenuBuilder
    {
        /// <summary>
        /// Builds the ordered entry list. When <paramref name="includeSelectInCurrentTable"/> is true, "Select in &lt;current table&gt;"
        /// is prepended (separated from the other tables' entries), acting on <paramref name="currentLocator"/>. Selecting in a comparison
        /// always selects the object in its Base or Compared table, so those are only offered from each other, for the same object.
        /// <paramref name="activeTable"/> stands in for the shown table when there is no <paramref name="tabController"/>, for testing purposes.
        /// </summary>
        public static List<SelectInMenuEntry> BuildEntries(
            CachedSnapshot snapshot,
            CachedSnapshot.SourceIndex source,
            ITableItemLocator currentLocator,
            AnalysisTabBarController tabController,
            bool includeSelectInCurrentTable,
            ITableItemLocator activeTable = null)
        {
            var entries = new List<SelectInMenuEntry>();

            var host = (tabController?.ActiveLocator ?? activeTable) as IComparisonSubTables;
            var hostTable = host as ITableItemLocator;
            var currentIsInHost = host != null && currentLocator != null
                && (currentLocator == hostTable || currentLocator == host.BaseTable || currentLocator == host.ComparedTable);
            if (!currentIsInHost)
                host = null;

            if (includeSelectInCurrentTable && currentLocator != null)
            {
                entries.Add(new SelectInMenuEntry
                {
                    Label = string.Format(TextContent.SelectInCurrentTableFormat, TableRevealHelper.GetTableName(currentLocator, host)),
                    Tooltip = TextContent.SelectMenuEntryTooltip,
                    Enabled = source.Valid,
                    OnClick = source.Valid
                        ? () => TableRevealHelper.RevealWithPrompt(currentLocator, source, frame: true, select: true, snapshot)
                        : null,
                });
            }

            if (host != null && source.Valid && currentLocator != hostTable)
            {
                var otherSide = currentLocator == host.BaseTable ? host.ComparedTable : host.BaseTable;
                var otherSideName = TableRevealHelper.GetTableName(otherSide, host);
                var matched = host.TryGetCounterpart(source, snapshot, out var counterpart, out var counterpartOwner, out var matchedByAddress);
                var suffix = !matched ? TextContent.SelectInNoCounterpartSuffix : matchedByAddress ? TextContent.SelectInMatchedByAddressSuffix : null;
                entries.Add(new SelectInMenuEntry
                {
                    Label = suffix == null ? otherSideName : $"{otherSideName} ({suffix})",
                    Tooltip = suffix == null ? null : string.Format("{0} ({1})", otherSideName, suffix),
                    Enabled = matched,
                    OnClick = matched
                        ? () => TableRevealHelper.RevealWithPrompt(hostTable, counterpart, frame: true, select: true, counterpartOwner)
                        : null,
                });
            }

            if (tabController == null)
                return entries;

            var hasCurrentTableEntries = entries.Count > 0;
            var currentTableId = host != null ? hostTable.TableId : currentLocator?.TableId;
            var firstOtherTableEntry = true;
            foreach (BreakdownTableId table in Enum.GetValues(typeof(BreakdownTableId)))
            {
                if (table == currentTableId)
                    continue;

                if (!tabController.CanRevealIn(table, snapshot))
                    continue;

                var displayName = table.GetDisplayName();
                // Prefer a live, already-built locator when one exists (free and fully accurate); otherwise
                // fall back to a static, build-free check so opening this menu never builds other tables.
                var loadedLocator = tabController.TryGetLoadedLocator(table);
                var canEverContain = loadedLocator != null
                    ? loadedLocator.CanEverContain(source)
                    : table.CanEverContainSourceId(snapshot, source);

                var reason = canEverContain ? null : table.LacksInstancesOfType(snapshot, source) ? TextContent.SelectInNoInstancesSuffix : TextContent.SelectInNeverContainsSuffix;
                entries.Add(new SelectInMenuEntry
                {
                    Label = canEverContain ? displayName : $"{displayName} ({reason})",
                    Tooltip = canEverContain ? null : string.Format("{0} ({1})", displayName, reason),
                    Enabled = canEverContain,
                    SeparatorBefore = hasCurrentTableEntries && firstOtherTableEntry,
                    OnClick = canEverContain
                        ? () => tabController.TryRevealInTable(table, source, frame: true, select: true, snapshot)
                        : null,
                });
                firstOtherTableEntry = false;
            }

            return entries;
        }

        /// <summary>Windows' native menus treat '&amp;' as a mnemonic marker, so a literal one has to be doubled there.</summary>
        public static string EscapeForGenericMenu(string label)
            => Application.platform == RuntimePlatform.WindowsEditor ? label.Replace("&", "&&") : label;

        /// <summary>
        /// Shows an IMGUI <see cref="GenericMenu"/> at <paramref name="guiRect"/>, which must be in the current GUI view's
        /// coordinates (e.g. <c>element.GetRect()</c> or the IMGUI mouse position), not screen coordinates.
        /// </summary>
        public static void ShowAsGenericMenu(Rect guiRect, List<SelectInMenuEntry> entries, bool underSelectInSubmenu)
        {
            var prefix = underSelectInSubmenu ? $"{TextContent.SelectInMenuPath}/" : string.Empty;
            var menu = new GenericMenu();
            foreach (var entry in entries)
            {
                if (entry.SeparatorBefore)
                    menu.AddSeparator(prefix);

                var content = new GUIContent(prefix + EscapeForGenericMenu(entry.Label));
                if (entry.Enabled)
                    menu.AddItem(content, false, () => entry.OnClick());
                else
                    menu.AddDisabledItem(content, false);
            }
            menu.DropDown(guiRect);
        }

        /// <summary>Appends the given entries to a UI Toolkit context menu, under a "Select in" submenu.</summary>
        public static void PopulateDropdownMenu(DropdownMenu menu, List<SelectInMenuEntry> entries)
        {
            foreach (var entry in entries)
            {
                if (entry.SeparatorBefore)
                    menu.AppendSeparator($"{TextContent.SelectInMenuPath}/");

                menu.AppendAction($"{TextContent.SelectInMenuPath}/{entry.Label}", a => entry.OnClick(),
                    entry.Enabled ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            }
        }
    }
}

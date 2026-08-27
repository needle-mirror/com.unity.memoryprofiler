using System.Text;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// A table cell that displays an object's status with an optional warning indicator.
    /// Layout: [warning icon?] [primary white label: status name or "Mixed"] [secondary grey label: counts].
    /// The warning icon appears when there are warnings; the secondary label shows the distinct-status
    /// count "(N)" and/or a warning count, and the full details are mirrored into the tooltip of every part.
    /// <para>
    /// Roots &amp; Impact uses <see cref="SetStatusForRootsAndImpact"/> instead: that view is structured as
    /// group nodes with reference chains beneath them, so non-group rows show their OWN status as the white
    /// label and use the warning icon/secondary text to report warnings among their referenced objects.
    /// </para>
    /// </summary>
    class StatusCell : VisualElement
    {
        const string k_UssClass = "status-cell";
        const string k_IconUssClass = "status-cell__warning-icon";
        const string k_LabelUssClass = "status-cell__label";
        const string k_SecondaryLabelUssClass = "status-cell__label--secondary";

        readonly VisualElement m_Icon;
        readonly Label m_Label;
        readonly Label m_SecondaryLabel;

        /// <summary>Bit for <see cref="ObjectStatus.None"/> (bit 0); masked out before display so a "None" status never renders.</summary>
        const ulong k_NoneBit = 1ul << (int)ObjectStatus.None;

        public StatusCell()
        {
            AddToClassList(k_UssClass);
            m_Icon = new VisualElement();
            m_Icon.AddToClassList(k_IconUssClass);
            Add(m_Icon);
            m_Label = new Label();
            m_Label.AddToClassList(k_LabelUssClass);
            Add(m_Label);
            m_SecondaryLabel = new Label();
            m_SecondaryLabel.AddToClassList(k_LabelUssClass);
            m_SecondaryLabel.AddToClassList(k_SecondaryLabelUssClass);
            m_SecondaryLabel.displayTooltipWhenElided = false;
            Add(m_SecondaryLabel);
        }

        /// <summary>Populates a cell in a single-snapshot table.</summary>
        /// <param name="totalChildCount">0 for a leaf row, &gt;0 for a group row; selects leaf vs group presentation.</param>
        public void SetStatus(ObjectStatusWarningInfo info, int totalChildCount)
        {
            var nonNoneBits = info.Bitmask & ~k_NoneBit;
            if (nonNoneBits == 0)
            {
                ClearCell();
                return;
            }
            ObjectStatusExtensions.GetStatusDisplayInfo(nonNoneBits, info.WarningCount, out var primaryText, out var tooltip);
            SetCellContent(info, primaryText, tooltip);
        }

        /// <summary>Populates a cell in a comparison table (the "Status in A" / "Status in B" columns).</summary>
        /// <param name="objectCount">The row's CountInA or CountInB; used only to detect absence (0 ⇒ the row has no objects on that side, so the cell is cleared).</param>
        public void SetStatusForComparison(ObjectStatusWarningInfo info, int objectCount)
        {
            if (objectCount == 0 || info.Bitmask == 0)
            {
                ClearCell();
                return;
            }
            var nonNoneBits = info.Bitmask & ~k_NoneBit;
            ObjectStatusExtensions.GetStatusDisplayInfo(nonNoneBits, info.WarningCount, out var primaryText, out var tooltip);
            SetCellContent(info, primaryText, tooltip);
        }

        /// <summary>
        /// Roots &amp; Impact rendering for the status cell. Pure aggregator rows (OwnershipBucket headers,
        /// whose own status is <see cref="ObjectStatus.BucketGroup"/>) have no meaningful own status, so they
        /// describe the statuses found beneath them (a single status name or "Mixed"). Every other row —
        /// object leaves AND region/aggregate headers that carry a real status of their own (e.g. Reserved,
        /// Untracked, Executables, Unrooted) — shows that own status name, plus a bracketed "(N)" count of the
        /// distinct statuses present in its subtree and a "(M Warnings in references)" count for warnings among
        /// the objects it references (the row's own warning, if any, is excluded). The full description is set
        /// as the tooltip on every part so it stays discoverable when the column is too narrow to show the grey
        /// secondary text.
        /// </summary>
        /// <param name="ownStatus">The row's own status classification (BucketGroup for pure aggregator rows).</param>
        /// <param name="aggregateInfo">Warning info summed over the row and its referenced subtree.</param>
        /// <param name="isGroup">True for header rows that have children/are not leaves.</param>
        public void SetStatusForRootsAndImpact(ObjectStatus ownStatus, ObjectStatusWarningInfo aggregateInfo, bool isGroup)
        {
            var anyWarnings = aggregateInfo.WarningCount > 0;
            // Warnings attributable to referenced objects only — drop this row's own warning, if any.
            var warningsInReferences = aggregateInfo.WarningCount - (ownStatus.IsWarningStatus() ? 1 : 0);
            if (warningsInReferences < 0)
                warningsInReferences = 0;

            // Only pure aggregator headers (BucketGroup) lack a real own status and describe their children.
            // A header that carries a real status (a region/aggregate node such as Reserved, Untracked,
            // Executables or Unrooted) shows that status itself, like a leaf — otherwise its column is blank.
            if (isGroup && ownStatus == ObjectStatus.BucketGroup)
            {
                // The group row itself has no meaningful own status; describe the statuses beneath it.
                var childBits = aggregateInfo.Bitmask & ~k_NoneBit & ~(1ul << (int)ownStatus);
                if (childBits == 0)
                {
                    ClearCell();
                    return;
                }
                ObjectStatusExtensions.GetStatusDisplayInfo(childBits, 0, out var groupPrimary, out _);
                ApplyContent(
                    anyWarnings,
                    groupPrimary,
                    BuildReferencesSecondaryText(childBits, warningsInReferences),
                    BuildGroupTooltip(childBits, aggregateInfo.WarningCount));
                return;
            }

            if (ownStatus == ObjectStatus.None)
            {
                ClearCell();
                return;
            }

            // The own status is the white label, but the secondary still reports how many distinct statuses
            // the row's subtree holds (so e.g. Referenced Managed Objects beneath a Scene Object aren't
            // hidden), followed by the reference-warning count. The tooltip leads with this status' own
            // description and then breaks down the referenced statuses, split into the warning ("potentially
            // problematic") ones and the rest, so non-warning statuses aren't mislabelled.
            var maskBits = aggregateInfo.Bitmask & ~k_NoneBit;
            ApplyContent(
                anyWarnings,
                ownStatus.ToDisplayName(),
                BuildReferencesSecondaryText(maskBits, warningsInReferences),
                BuildNonGroupTooltip(ownStatus, maskBits, warningsInReferences));
        }

        /// <summary>
        /// Builds the grey secondary label for Roots &amp; Impact: a "(N)" count of the distinct statuses in
        /// <paramref name="statusBits"/> when there is more than one, followed by "(M Warnings in references)"
        /// when <paramref name="warningsInReferences"/> is non-zero (a single referenced warning still shows).
        /// </summary>
        static string BuildReferencesSecondaryText(ulong statusBits, int warningsInReferences)
            => BuildSecondaryText(
                statusBits,
                warningsInReferences,
                minWarningsToShow: 1,
                TextContent.RootsAndImpactMixedTypesAndWarningsInReferences,
                TextContent.RootsAndImpactWarningsInReferencesLabel);

        /// <summary>
        /// Tooltip for a non-group Roots &amp; Impact row: this status' own description first (the rich
        /// per-status tooltip from <see cref="ObjectStatusExtensions.GetStatusDisplayInfo"/>), followed by a
        /// breakdown of the statuses found among the objects it references (split into the warning,
        /// "potentially problematic", ones and the rest). A leaf with no referenced statuses — e.g. a single
        /// leaked object — shows just its own status description rather than a "(1 object(s))" warning line.
        /// </summary>
        static string BuildNonGroupTooltip(ObjectStatus ownStatus, ulong subtreeBits, int warningsInReferences)
        {
            ObjectStatusExtensions.GetStatusDisplayInfo(1ul << (int)ownStatus, 0, out _, out var ownTooltip);
            var referencedBits = subtreeBits & ~k_NoneBit & ~(1ul << (int)ownStatus);
            var breakdown = BuildStatusBreakdown(referencedBits, warningsInReferences, hasPrecedingContent: true);
            if (string.IsNullOrEmpty(breakdown))
                return ownTooltip;
            return string.IsNullOrEmpty(ownTooltip) ? breakdown : ownTooltip + "\n" + breakdown;
        }

        /// <summary>
        /// Tooltip for a group (OwnershipBucket) Roots &amp; Impact row, which has no meaningful own status:
        /// describes the statuses found beneath it. A single non-warning child status shows that status' own
        /// descriptive tooltip; otherwise the statuses are split into the warning ("potentially problematic")
        /// ones and the rest.
        /// </summary>
        static string BuildGroupTooltip(ulong childBits, int warningObjectCount)
        {
            childBits &= ~k_NoneBit;
            if (childBits == 0)
                return string.Empty;

            var otherBits = childBits & ~ObjectStatusExtensions.WarningStatusesBitmask;
            // A single non-warning child status: show that status' own descriptive tooltip rather than a list.
            if ((childBits & ObjectStatusExtensions.WarningStatusesBitmask) == 0 && (otherBits & (otherBits - 1)) == 0)
                return SingleBitStatus(otherBits).GetTooltip();

            return BuildStatusBreakdown(childBits, warningObjectCount, hasPrecedingContent: false);
        }

        /// <summary>
        /// Builds the status breakdown lines for <paramref name="statusBits"/>: a "Potentially problematic
        /// ({count} object(s)): {names}" line for the warning statuses, then a list of the remaining statuses.
        /// <paramref name="hasPrecedingContent"/> is true when this breakdown is appended after other tooltip
        /// text (e.g. a row's own status description), selecting the "Other statuses present:" heading over the
        /// standalone "Statuses present:" heading. Returns an empty string when no status is set.
        /// </summary>
        static string BuildStatusBreakdown(ulong statusBits, int warningObjectCount, bool hasPrecedingContent)
        {
            statusBits &= ~k_NoneBit;
            if (statusBits == 0)
                return string.Empty;

            var warningBits = statusBits & ObjectStatusExtensions.WarningStatusesBitmask;
            var otherBits = statusBits & ~ObjectStatusExtensions.WarningStatusesBitmask;

            var sb = new StringBuilder();
            if (warningBits != 0)
                sb.AppendFormat(TextContent.RootsAndImpactProblematicStatusesTooltip, warningObjectCount, ObjectStatusExtensions.JoinStatusNames(warningBits));
            if (otherBits != 0)
            {
                if (sb.Length > 0)
                    sb.Append('\n');
                var useOtherHeading = hasPrecedingContent || warningBits != 0;
                sb.AppendFormat(
                    useOtherHeading ? TextContent.RootsAndImpactOtherStatusesTooltip : TextContent.RootsAndImpactStatusesPresentTooltip,
                    ObjectStatusExtensions.JoinStatusNames(otherBits));
            }
            return sb.ToString();
        }

        /// <summary>Recovers the <see cref="ObjectStatus"/> from a bitmask known to have exactly one bit set.</summary>
        static ObjectStatus SingleBitStatus(ulong singleBit)
        {
            int bitIndex = 0;
            ulong b = singleBit;
            while ((b >>= 1) != 0) bitIndex++;
            return (ObjectStatus)bitIndex;
        }

        void ApplyContent(bool showIcon, string primaryText, string secondaryText, string tooltip)
        {
            UIElementsHelper.SetVisibility(m_Icon, showIcon);
            m_Icon.tooltip = tooltip;

            m_Label.text = primaryText;
            m_Label.tooltip = tooltip;
            m_Label.displayTooltipWhenElided = string.IsNullOrEmpty(tooltip) || tooltip == primaryText;

            m_SecondaryLabel.text = secondaryText;
            m_SecondaryLabel.tooltip = tooltip;
            UIElementsHelper.SetVisibility(m_SecondaryLabel, secondaryText.Length > 0);
        }

        void ClearCell()
            => ApplyContent(showIcon: false, string.Empty, string.Empty, string.Empty);

        void SetCellContent(ObjectStatusWarningInfo info, string primaryText, string tooltip)
            => ApplyContent(info.WarningCount > 0, primaryText, BuildSecondaryText(info.Bitmask, info.WarningCount), tooltip);

        /// <summary>
        /// Builds the grey secondary label for the non-Roots-and-Impact tables: a "(N)" distinct-status
        /// count when there is more than one status, plus a "(M Warnings)" count once there is more than one
        /// warning (a single warning is conveyed by the icon alone).
        /// </summary>
        static string BuildSecondaryText(ulong bitmask, int warningCount)
            => BuildSecondaryText(
                bitmask,
                warningCount,
                minWarningsToShow: 2,
                TextContent.ObjectStatusMixedTypesAndWarningCount,
                TextContent.ObjectStatusWarningCountLabel);

        /// <summary>
        /// Shared secondary-label builder. Emits "(N)" for the distinct-status count when
        /// <paramref name="statusBits"/> holds more than one status, and the warning count via
        /// <paramref name="warningsFormat"/> (or <paramref name="mixedAndWarningsFormat"/> when both apply)
        /// once <paramref name="warningCount"/> reaches <paramref name="minWarningsToShow"/>.
        /// </summary>
        static string BuildSecondaryText(ulong statusBits, int warningCount, int minWarningsToShow, string mixedAndWarningsFormat, string warningsFormat)
        {
            var typeCount = CountSetBits(statusBits & ~k_NoneBit);
            var isMixed = typeCount > 1;
            var showWarnings = warningCount >= minWarningsToShow;

            if (isMixed && showWarnings)
                return string.Format(mixedAndWarningsFormat, typeCount, warningCount);
            if (isMixed)
                return string.Format(TextContent.ObjectStatusMixedTypeCount, typeCount);
            if (showWarnings)
                return string.Format(warningsFormat, warningCount);
            return string.Empty;
        }

        static int CountSetBits(ulong value)
        {
            int count = 0;
            while (value != 0) { value &= value - 1; count++; }
            return count;
        }
    }
}

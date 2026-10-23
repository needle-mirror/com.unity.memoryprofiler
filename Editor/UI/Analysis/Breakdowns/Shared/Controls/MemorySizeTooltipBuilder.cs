using System.Text;
using Unity.MemoryProfiler.Editor.UIContentData;
using UnityEditor;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Tooltip builder for memory size elements (bars, legend)
    /// </summary>
    static class MemorySizeTooltipBuilder
    {
        static readonly string k_TooltipAllocatedFormat = TextContent.Tr("{0}: {1} ({2:0.0}% of total)");
        static readonly string k_TooltipResidentFormat = TextContent.Tr("{0}: {1} ({2:0.0}% of {3})");
        static readonly string k_TooltipResidentOnlyFormat = TextContent.Tr("{0}: {1} ({2:0.0}% of total)");
        static readonly string k_TooltipResidentUnknown = TextContent.Tr("Resident memory size for this element can't be determined");

        public static string MakeTooltip(string title, MemorySize size, ulong total, bool allocatedVisible, bool residentVisible, string linePrefix,
            in MemorySummaryDataOverrides dataOverrides = default)
        {
            var tooltip = new StringBuilder(title, 200);

            // Allocated part
            if (allocatedVisible)
            {
                if (tooltip.Length > 0)
                    tooltip.AppendLine();

                tooltip.Append(linePrefix);

                var committedOfTotal = total > 0 ? (float)size.Committed / total : 0;
                tooltip.AppendFormat(k_TooltipAllocatedFormat, dataOverrides.PrimaryValueName, EditorUtility.FormatBytes((long)size.Committed), committedOfTotal * 100);
            }

            // Resident part
            if (residentVisible)
            {
                if (tooltip.Length > 0)
                    tooltip.AppendLine();

                tooltip.Append(linePrefix);

                // If resident is determined
                if (allocatedVisible)
                {
                    // and allocated is present - clarify how big resident portion is in terms of allocated
                    var residentOfTotal = size.Committed > 0 ? (float)size.Resident / size.Committed : 0;
                    tooltip.AppendFormat(k_TooltipResidentFormat, dataOverrides.SecondaryValueName, EditorUtility.FormatBytes((long)size.Resident), residentOfTotal * 100,
                        dataOverrides.SecondaryValueRelativeTotalName);
                }
                else
                {
                    // clarify how big resident portion is in terms of total resident
                    var residentOfTotal = total > 0 ? (float)size.Resident / total : 0;
                    tooltip.AppendFormat(k_TooltipResidentOnlyFormat, dataOverrides.SecondaryValueName, EditorUtility.FormatBytes((long)size.Resident), residentOfTotal * 100);
                }
            }

            return tooltip.ToString();
        }
    }
}

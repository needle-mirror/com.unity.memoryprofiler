using Unity.MemoryProfiler.Editor.UIContentData;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// What the two size channels of a memory summary hold. A <see cref="MemorySize"/> always carries a primary
    /// (<see cref="MemorySize.Committed"/>) and a secondary (<see cref="MemorySize.Resident"/>) value; this names them.
    /// </summary>
    internal enum MemorySummaryValueClassification : byte
    {
        /// <summary>Allocated memory, and the part of it that is resident.</summary>
        AllocatedAndResident,
        /// <summary>Total impact, and the part of it that is exclusively owned.</summary>
        TotalAndExclusiveImpact,
    }

    /// <summary>
    /// Overrides of how a memory summary widget presents its data, passed down to its bars, legend and tooltips.
    /// The default presents allocated and resident memory.
    /// </summary>
    internal readonly struct MemorySummaryDataOverrides
    {
        public readonly MemorySummaryValueClassification Classification;

        public MemorySummaryDataOverrides(MemorySummaryValueClassification classification)
        {
            Classification = classification;
        }

        static readonly string k_AllocatedName = TextContent.Tr("Allocated");
        static readonly string k_ResidentName = TextContent.Tr("Resident");
        static readonly string k_AllocatedRelativeTotalName = TextContent.Tr("allocated");

        /// <summary>Label of the primary (<see cref="MemorySize.Committed"/>) value.</summary>
        public string PrimaryValueName => Classification switch
        {
            MemorySummaryValueClassification.TotalAndExclusiveImpact => TextContent.TotalImpactRowLabel,
            _ => k_AllocatedName,
        };

        /// <summary>Label of the secondary (<see cref="MemorySize.Resident"/>) value.</summary>
        public string SecondaryValueName => Classification switch
        {
            MemorySummaryValueClassification.TotalAndExclusiveImpact => TextContent.PotentialSavingsExclusiveImpactLabel,
            _ => k_ResidentName,
        };

        /// <summary>What the secondary value is a percentage of, as it reads in a tooltip ("… of allocated").</summary>
        public string SecondaryValueRelativeTotalName => Classification switch
        {
            MemorySummaryValueClassification.TotalAndExclusiveImpact => TextContent.PotentialSavingsTotalIncludingSharedImpactLabel,
            _ => k_AllocatedRelativeTotalName,
        };
    }
}

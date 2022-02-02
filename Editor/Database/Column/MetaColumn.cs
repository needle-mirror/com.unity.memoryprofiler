using System;

namespace Unity.MemoryProfiler.Editor.Database
{
    public enum DataMatchMethod
    {
        AsString, //default
        AsNumber,
        AsEnum
    }

    internal struct MetaType
    {
        public Type scriptingType;
        public DataMatchMethod comparisonMethod;

        public MetaType(Type st, DataMatchMethod mm = DataMatchMethod.AsString)
        {
            scriptingType = st;
            comparisonMethod = mm;
        }
    }
    /// <summary>
    /// Holds information about the structure and significance of a column as part of a MetaTable
    /// </summary>
    internal class MetaColumn
    {
        public MetaType Type { get; set; }

        public int Index { get; set; }
        public readonly string Name;
        public readonly string DisplayName;

        const int k_DefaultDisplayWidth = 100;
        public readonly int DefaultDisplayWidth;

        public readonly bool IsPrimaryKey;
        public bool ShownByDefault = true;
        public string FormatName { get; set; }

        public readonly Operation.Grouping.IGroupAlgorithm DefaultGroupAlgorithm;
        public Operation.Grouping.IMergeAlgorithm DefaultMergeAlgorithm { get; set; }

        public MetaColumn(string name, string displayName, MetaType type, bool isPrimaryKey, Operation.Grouping.IGroupAlgorithm groupAlgo, Operation.Grouping.IMergeAlgorithm mergeAlgo, string formatName = "", int displayDefaultWidth = k_DefaultDisplayWidth)
        {
            Index = 0;
            Name = name;
            DisplayName = displayName;
            Type = type;
            IsPrimaryKey = isPrimaryKey;
            FormatName = formatName;
            DefaultMergeAlgorithm = mergeAlgo;
            DefaultGroupAlgorithm = groupAlgo;
            DefaultDisplayWidth = displayDefaultWidth;
        }

        public MetaColumn(string name, string displayName, MetaType type, Operation.Grouping.IGroupAlgorithm groupAlgo, Operation.Grouping.IMergeAlgorithm mergeAlgo, string formatName = "", int displayDefaultWidth = k_DefaultDisplayWidth)
        {
            Index = 0;
            Name = name;
            DisplayName = displayName;
            Type = type;
            FormatName = formatName;
            DefaultMergeAlgorithm = mergeAlgo;
            DefaultGroupAlgorithm = groupAlgo;
            DefaultDisplayWidth = displayDefaultWidth;
        }

        public MetaColumn(MetaColumn mc)
        {
            Index = 0;
            Name = mc.Name;
            IsPrimaryKey = mc.IsPrimaryKey;
            ShownByDefault = mc.ShownByDefault;
            DisplayName = mc.DisplayName;
            Type = mc.Type;
            FormatName = mc.FormatName;
            DefaultMergeAlgorithm = mc.DefaultMergeAlgorithm;
            DefaultGroupAlgorithm = mc.DefaultGroupAlgorithm;
            DefaultDisplayWidth = mc.DefaultDisplayWidth;
        }
    }
}

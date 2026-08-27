using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    // Data model representing the 'Unity Objects' comparison breakdown.
    class UnityObjectsComparisonModel
        : ComparisonTableModel<UnityObjectsModel, UnityObjectsModel.ItemData, UnityObjectsComparisonModel.ExtraData>
    {
        public readonly struct ExtraData
        {
            public string NativeTypeName { get; }
            /// <summary>Aggregated warning info for all objects in the group from snapshot A.</summary>
            public ObjectStatusWarningInfo WarningInfoInA { get; }
            /// <summary>Aggregated warning info for all objects in the group from snapshot B.</summary>
            public ObjectStatusWarningInfo WarningInfoInB { get; }
            public ExtraData(string nativeTypeName, ObjectStatusWarningInfo warningInfoInA = default, ObjectStatusWarningInfo warningInfoInB = default)
            {
                NativeTypeName = nativeTypeName;
                WarningInfoInA = warningInfoInA;
                WarningInfoInB = warningInfoInB;
            }
        }

        public UnityObjectsComparisonModel(
            UnityObjectsModel baseModel,
            UnityObjectsModel comparedModel,
            List<TreeViewItemData<ComparisonData>> rootNodes,
            MemorySize totalSnapshotAMemorySize,
            MemorySize totalSnapshotBMemorySize,
            long largestAbsoluteSizeDelta)
            : base(rootNodes, baseModel, totalSnapshotAMemorySize, comparedModel, totalSnapshotBMemorySize, largestAbsoluteSizeDelta)
        {
        }

        /// <summary>Flattened, paginated model — see <see cref="ComparisonTableModel{TBaseModel,TBaseModelTreeItemData,TModelSpecificData}"/>'s paged constructor.</summary>
        public UnityObjectsComparisonModel(
            UnityObjectsModel baseModel,
            UnityObjectsModel comparedModel,
            ComparisonRowSource<ExtraData> rowSource,
            int pageSize,
            MemorySize totalSnapshotAMemorySize,
            MemorySize totalSnapshotBMemorySize,
            long largestAbsoluteSizeDelta,
            MemorySize totalSizeA,
            MemorySize totalSizeB)
            : base(rowSource, pageSize, baseModel, totalSnapshotAMemorySize, comparedModel, totalSnapshotBMemorySize, largestAbsoluteSizeDelta, totalSizeA, totalSizeB)
        {
        }
    }
}

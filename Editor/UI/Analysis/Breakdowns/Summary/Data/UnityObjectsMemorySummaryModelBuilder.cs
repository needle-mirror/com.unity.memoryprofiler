using System;
using System.Collections.Generic;
using System.Linq;
using Unity.MemoryProfiler.Editor.UIContentData;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Top N Unity objects memory usage summary model builder for table view controller
    /// Collects data from a captured memory snapshot and builds a model representation.
    /// </summary>
    internal class UnityObjectsMemorySummaryModelBuilder : IMemorySummaryModelBuilder<MemorySummaryModel>
    {
        readonly string[] k_UxmlCategoryStyleIds = {
            "grp-1",
            "grp-2",
            "grp-3",
            "grp-4",
            "grp-5"
        };
        readonly string k_UxmlCategoryStyleOther = "other";

        CachedSnapshot m_SnapshotA;
        CachedSnapshot m_SnapshotB;

        public UnityObjectsMemorySummaryModelBuilder(CachedSnapshot snapshotA, CachedSnapshot snapshotB)
        {
            m_SnapshotA = snapshotA;
            m_SnapshotB = snapshotB;
        }

        public MemorySummaryModel Build()
        {
            // Calculate joint total for snapshots *A* and *B*
            // Nb! We can't use type index, as it might be different for
            // different snapshots that's why name is used instead
            ulong totalA = 0, totalB = 0;
            var typeToSizeMap = new Dictionary<string, (ulong sizeA, ulong sizeB)>();
            CalculateTotals(m_SnapshotA, ref totalA, typeToSizeMap, (ulong size, (ulong sizeA, ulong sizeB) val) => { return (val.sizeA + size, val.sizeB); });
            if (m_SnapshotB != null)
                CalculateTotals(m_SnapshotB, ref totalB, typeToSizeMap, (ulong size, (ulong sizeA, ulong sizeB) val) => { return (val.sizeA, val.sizeB + size); });

            // Sort
            var list = typeToSizeMap.ToList();
            list.Sort((l, r) =>
            {
                var totalL = l.Value.sizeA + l.Value.sizeB;
                var totalR = r.Value.sizeA + r.Value.sizeB;
                return -totalL.CompareTo(totalR);
            });

            // Pick top k_MaxTopElements and make table rows
            ulong totalTop10A = 0, totalTop10B = 0;
            var rows = new List<MemorySummaryModel.Row>();
            for (int i = 0; i < list.Count && i < k_UxmlCategoryStyleIds.Length; i++)
            {
                var item = list[i];
                rows.Add(new MemorySummaryModel.Row(item.Key, item.Value.sizeA, 0, item.Value.sizeB, 0, k_UxmlCategoryStyleIds[i], TextContent.ManagedObjectsDescription, null));

                totalTop10A += item.Value.sizeA;
                totalTop10B += item.Value.sizeB;
            }

            // Add "other" item for everything outside of top k_MaxTopElements
            if (list.Count > k_UxmlCategoryStyleIds.Length)
                rows.Add(new MemorySummaryModel.Row(SummaryTextContent.kUnityObjectsCategoryOther, totalA - totalTop10A, 0, totalB - totalTop10B, 0, k_UxmlCategoryStyleOther, TextContent.ManagedObjectsDescription, null) { SortPriority = MemorySummaryModel.RowSortPriority.ShowLast });

            bool compareMode = m_SnapshotB != null;
            return new MemorySummaryModel(
                SummaryTextContent.kUnityObjectsTitle,
                SummaryTextContent.kUnityObjectsDescription,
                compareMode,
                totalA,
                totalB,
                rows,
                null);
        }

        void CalculateTotals(CachedSnapshot cs, ref ulong totalValue, Dictionary<string, (ulong a, ulong b)> typeIndexToSizeMap, Func<ulong, (ulong, ulong), (ulong, ulong)> updater)
        {
            if (cs.HasSystemMemoryRegionsInfo)
            {
                CalculateTotalsNew(cs, ref totalValue, typeIndexToSizeMap, updater);
                return;
            }

            // Build a map of type index <-> size for Unity Objects
            var nativeObjects = cs.NativeObjects;
            var nativeTypes = cs.NativeTypes;
            for (var i = 0L; i < nativeObjects.Count; i++)
            {
                // Get native object size
                var objectSize = nativeObjects.Size[i];

                // Add managed object size native object is linked to
                var managedObjectIndex = nativeObjects.ManagedObjectIndex[i];
                if (managedObjectIndex >= 0)
                {
                    var managedObject = cs.CrawledData.ManagedObjects[managedObjectIndex];
                    objectSize += Convert.ToUInt64(managedObject.Size);
                }

                // Get the type index
                var typeIndex = nativeObjects.NativeTypeArrayIndex[i];
                var typeName = nativeTypes.TypeName[typeIndex];

                // Add size to corresponding type index
                if (typeIndexToSizeMap.TryGetValue(typeName, out var accumulatedSize))
                    typeIndexToSizeMap[typeName] = updater(objectSize, accumulatedSize);
                else
                    typeIndexToSizeMap[typeName] = updater(objectSize, (0, 0));

                totalValue += objectSize;
            }
        }

        static void CalculateTotalsNew(CachedSnapshot cs, ref ulong _totalValue, Dictionary<string, (ulong a, ulong b)> typeIndexToSizeMap, Func<ulong, (ulong, ulong), (ulong, ulong)> updater)
        {
            var data = cs.EntriesMemoryMap.Data;
            var nativeTypes = cs.NativeTypes;
            var nativeObjects = cs.NativeObjects;
            var nativeAllocations = cs.NativeAllocations;
            ulong totalValue = 0;
            cs.EntriesMemoryMap.ForEachFlat((_, address, size, source) =>
            {
                void AddNativeObject(long index)
                {
                    var typeIndex = nativeObjects.NativeTypeArrayIndex[index];
                    var typeName = nativeTypes.TypeName[typeIndex];
                    if (typeIndexToSizeMap.TryGetValue(typeName, out var accumulatedSize))
                        typeIndexToSizeMap[typeName] = updater(size, accumulatedSize);
                    else
                        typeIndexToSizeMap[typeName] = updater(size, (0, 0));

                    totalValue += size;
                }

                switch (source.Id)
                {
                    case SourceIndex.SourceId.NativeObject:
                    {
                        AddNativeObject(source.Index);
                        return;
                    }
                    case SourceIndex.SourceId.NativeAllocation:
                    {
                        var rootReferenceId = nativeAllocations.RootReferenceId[source.Index];
                        if (cs.NativeObjects.RootReferenceIdToIndex.TryGetValue(rootReferenceId, out var objectIndex))
                            AddNativeObject(objectIndex);
                        return;
                    }
                    case SourceIndex.SourceId.ManagedObject:
                    {
                        var managedObjects = cs.CrawledData.ManagedObjects;
                        var nativeObjectIndex = managedObjects[source.Index].NativeObjectIndex;
                        if (nativeObjectIndex > 0)
                            AddNativeObject(nativeObjectIndex);
                        return;
                    }
                    default:
                        return;
                }
            });

            _totalValue = totalValue;
        }
    }
}

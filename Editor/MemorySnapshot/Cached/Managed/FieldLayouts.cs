using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Editor.Containers;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor.Managed
{
    /// <summary>
    /// Storing field layout information relevant for crawling and providing an easy access to register and look up layout info per type.
    /// When using consider storing static field layouts in a FieldLayouts instance separate from the one holding instance field layouts.
    ///
    /// Note: Writing data to this is not threadsafe so _either_:
    /// - CrawlRawObject on a job,
    /// - OR register new field layouts,
    /// but don't do both simultaneously.
    /// </summary>
    struct FieldLayouts : IDisposable
    {
        readonly DynamicArray<long> m_TypeIndexToFieldLayoutInfoIndex;

        public DynamicArray<FieldLayoutInfo> FieldLayoutInfo;

        public readonly DynamicArrayRef<FieldLayoutInfo> this[int typeIndex]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                var index = m_TypeIndexToFieldLayoutInfoIndex[typeIndex];
                if (index >= 0)
                {
                    unsafe
                    {
                        var start = FieldLayoutInfo.GetUnsafeTypedPtr();
                        if (index >= 0)
                        {
                            return DynamicArrayRef<FieldLayoutInfo>.ConvertExistingDataToDynamicArrayRef(start + index, start[index].RemainingFieldCountForThisType + 1);
                        }
                        else
                            // No fields for this type, create an empty list
                            return DynamicArrayRef<FieldLayoutInfo>.ConvertExistingDataToDynamicArrayRef(start, 0);
                    }
                }
                return default;
            }
        }

        public FieldLayouts(int typeCount, long assumedReferenceFieldCountPerType)
        {
            m_TypeIndexToFieldLayoutInfoIndex = new DynamicArray<long>(typeCount, Allocator.Persistent);

            if (typeCount > 0)
            {
                unsafe
                {
                    // Initialize all values to -1
                    UnsafeUtility.MemSet(m_TypeIndexToFieldLayoutInfoIndex.GetUnsafePtr(), 0xFF, m_TypeIndexToFieldLayoutInfoIndex.Count * sizeof(long));
                }
                Debug.Assert(m_TypeIndexToFieldLayoutInfoIndex[0] == -1);
            }
            // I guess we could try to pre calculate the amount of fields needed more accurately,
            // but there are nested fields and not all fields can hold references so getting that number means we'd have done half
            // the work for building the field layout already.
            // We want to lazy build the FieldLayout, also since not all types may have actual live objects,
            // so we can also just lazy grow the arrays if we need to instead.
            var initialSizeForStaticFieldLayouts = Math.Max(1, typeCount * assumedReferenceFieldCountPerType);
            FieldLayoutInfo = new DynamicArray<FieldLayoutInfo>(0, initialSizeForStaticFieldLayouts, Allocator.Persistent);
        }

        public void AddType(int typeIndex, DynamicArray<FieldLayoutInfo> fieldInfos, bool memClearAllAboveCount = false)
        {
            // no fields or layout already set? ignore this.
            if (fieldInfos.Count == 0 || m_TypeIndexToFieldLayoutInfoIndex[typeIndex] >= 0)
            {
                return;
            }
            m_TypeIndexToFieldLayoutInfoIndex[typeIndex] = FieldLayoutInfo.Count;
            FieldLayoutInfo.PushRange(fieldInfos, memClearForExcessExpansion: memClearAllAboveCount);
        }

        public long GetFieldLayoutInfoIndex(int typeIndex)
        {
            return m_TypeIndexToFieldLayoutInfoIndex[typeIndex];
        }

        public void Dispose()
        {
            if (FieldLayoutInfo.IsCreated)
                FieldLayoutInfo.Dispose();
        }
    }
}

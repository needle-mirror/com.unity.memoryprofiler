using System;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// A native, growable buffer of blittable per-row records for a flattened table view. Holds every matching
    /// row (potentially millions) in a <see cref="DynamicArray{T}"/>, so the full set lives off the managed
    /// single-array ceiling that an all-rows <c>List&lt;TreeViewItemData&gt;</c> would hit. Display names and
    /// item data are derived on demand (per visible page) by the owning model, which keeps
    /// <typeparamref name="TRow"/> unmanaged. The owning model disposes the store.
    /// </summary>
    sealed class FlattenedRowStore<TRow> : IDisposable where TRow : unmanaged
    {
        DynamicArray<TRow> m_Rows;

        public FlattenedRowStore(long initialCapacity = 0)
        {
            m_Rows = new DynamicArray<TRow>(0, Math.Max(0, initialCapacity), Allocator.Persistent);
        }

        public long Count => m_Rows.Count;

        public ref readonly TRow this[long index] => ref m_Rows[index];

        /// <summary>Mutable access to an already-appended row, for patching aggregates computed after the row was reserved.</summary>
        public ref TRow ElementAt(long index) => ref m_Rows[index];

        /// <summary>Raw pointer to the row buffer, for building a native sort comparer. Valid until the next Add/resize.</summary>
        public unsafe TRow* GetUnsafeTypedPtr() => m_Rows.GetUnsafeTypedPtr();

        /// <summary>Appends a row and returns its index.</summary>
        public long Add(in TRow row)
        {
            m_Rows.Push(row);
            return m_Rows.Count - 1;
        }

        /// <summary>Drops every row from <paramref name="newCount"/> onward.</summary>
        public void Truncate(long newCount) => m_Rows.Resize(newCount, false);

        public void Dispose()
        {
            if (m_Rows.IsCreated)
                m_Rows.Dispose();
        }
    }
}

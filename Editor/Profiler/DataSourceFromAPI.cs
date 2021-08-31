using System;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Format;

namespace Unity.MemoryProfiler.Editor.DataAdapters
{

    /// <summary>
    /// Represent a source of data that can be requested in chunks
    /// </summary>
    /// <typeparam name="DataT"></typeparam>
    internal abstract class DataSource<DataT>
    {
        public abstract DataT this[long idx] { get; set; }
        public abstract long Length { get; }
    }

    internal class AdaptorManagedArray<DataT> : DataSource<DataT>
    {
        private DataT[] m_Array;
        public AdaptorManagedArray(DataT[] array)
        {
            m_Array = array;
        }

        public override DataT this[long idx]
        {
            get { return m_Array[idx]; }
            set { m_Array[idx] = value; }
        }

        public override long Length { get { return m_Array.LongLength; } }
    }

    internal class AdaptorCombinedManagedArray<DataT> : DataSource<DataT>
    {
        private DataT[] m_Array1;
        private DataT[] m_Array2;
        long firstIndexInSource2;
        long totalCount;
        public AdaptorCombinedManagedArray(DataT[] array1, long array1Count, DataT[] array2, long array2Count)
        {
            if (array1 != null && array1.Length == array1Count)
                m_Array1 = array1;
            firstIndexInSource2 = array1Count;

            if (array2 != null && array2.Length == array2Count)
                m_Array2 = array2;
            totalCount = array1Count + array2Count;
        }

        public override DataT this[long idx]
        {
            get
            {
                if (idx < firstIndexInSource2)
                {
                    if (m_Array1 != null)
                        return m_Array1[idx];
                    else
                        return default(DataT);
                }
                else if (idx < totalCount)
                {
                    if (m_Array2 != null)
                        return m_Array2[idx - firstIndexInSource2];
                    else
                        return default(DataT);
                }
                else
                    throw new IndexOutOfRangeException();
            }
            set
            {
                if (idx < firstIndexInSource2)
                {
                    if (m_Array1 != null)
                        m_Array1[idx] = value;
                    else
                        throw new IndexOutOfRangeException();
                }
                else if (idx < totalCount)
                {
                    if (m_Array2 != null)
                        m_Array2[idx - firstIndexInSource2] = value;
                    else
                        throw new IndexOutOfRangeException();
                }
                else
                    throw new IndexOutOfRangeException();
            }
        }

        public override long Length { get { return totalCount; } }
    }

    internal class AdaptorDynamicArray<DataT> : DataSource<DataT> where DataT : unmanaged

    {
        private DynamicArray<DataT> m_Array;
        public AdaptorDynamicArray(DynamicArray<DataT> array)
        {
            m_Array = array;
        }

        public override DataT this[long idx]
        {
            get { return m_Array[idx]; }
            set { m_Array[idx] = value; }
        }

        public override long Length { get { return m_Array.Count; } }
    }

    internal class AdaptorCombinedDynamicArray<DataT> : DataSource<DataT> where DataT : unmanaged
    {
        private DynamicArray<DataT> m_Array1;
        private DynamicArray<DataT> m_Array2;
        long firstIndexInSource2;
        long totalCount;
        public AdaptorCombinedDynamicArray(DynamicArray<DataT> array1, long array1Count, DynamicArray<DataT> array2, long array2Count)
        {
            if (array1.IsCreated && array1.Count == array1Count)
                m_Array1 = array1;
            firstIndexInSource2 = array1Count;

            if (array2.IsCreated && array2.Count == array2Count)
                m_Array2 = array2;
            totalCount = array1Count + array2Count;
        }

        public override DataT this[long idx]
        {
            get
            {
                if (idx < firstIndexInSource2)
                {
                    if (m_Array1.IsCreated)
                        return m_Array1[idx];
                    else
                        return default(DataT);
                }
                else if (idx < totalCount)
                {
                    if (m_Array2.IsCreated)
                        return m_Array2[idx - firstIndexInSource2];
                    else
                        return default(DataT);
                }
                else
                    throw new IndexOutOfRangeException();
            }
            set
            {
                if (idx < firstIndexInSource2)
                {
                    if (m_Array1.IsCreated)
                        m_Array1[idx] = value;
                    else
                        throw new IndexOutOfRangeException();
                }
                else if (idx < totalCount)
                {
                    if (m_Array2.IsCreated)
                        m_Array2[idx - firstIndexInSource2] = value;
                    else
                        throw new IndexOutOfRangeException();
                }
                else
                    throw new IndexOutOfRangeException();
            }
        }

        public override long Length { get { return totalCount; } }
    }
}

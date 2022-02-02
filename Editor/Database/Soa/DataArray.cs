using System;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.DataAdapters;

namespace Unity.MemoryProfiler.Editor.Database.Soa
{
    /// <summary>
    /// Provides a way to create columns using a `Struct-of-Array` data structure as input data. Will use SoaDataSet to represent data source.
    /// </summary>
    internal class DataArray
    {
        public static SimpleColumn<DataT> MakeColumnUnmanaged<DataT>(DynamicArray<DataT> source, bool hexdisplay) where DataT : unmanaged, IComparable
        {
            var cache = new Cache<DataT>(new AdaptorDynamicArray<DataT>(source));
            switch (Type.GetTypeCode(typeof(DataT)))
            {
                case TypeCode.Int64:
                    return new ColumnLong(cache as Cache<long>, hexdisplay ? SimpleColumnDisplay.Hex : SimpleColumnDisplay.Default) as SimpleColumn<DataT>;
                case TypeCode.Int32:
                    return new ColumnInt(cache as Cache<int>, hexdisplay ? SimpleColumnDisplay.Hex : SimpleColumnDisplay.Default) as SimpleColumn<DataT>;
                case TypeCode.UInt64:
                    return new ColumnULong(cache as Cache<ulong>, hexdisplay ? SimpleColumnDisplay.Hex : SimpleColumnDisplay.Default) as SimpleColumn<DataT>;

                default:
                    return new SimpleColumn<DataT>(cache, SimpleColumnDisplay.Default);
            }
        }

        public static SimpleColumn<DataT> MakeColumnUnmanaged<DataT>(DynamicArray<DataT> source1, long source1Count, DynamicArray<DataT> source2, long source2Count, bool hexdisplay) where DataT : unmanaged, IComparable
        {
            var cache = new Cache<DataT>(new AdaptorCombinedDynamicArray<DataT>(source1, source1Count, source2, source2Count));
            switch (Type.GetTypeCode(typeof(DataT)))
            {
                case TypeCode.Int64:
                    return new ColumnLong(cache as Cache<long>, hexdisplay ? SimpleColumnDisplay.Hex : SimpleColumnDisplay.Default) as SimpleColumn<DataT>;
                case TypeCode.Int32:
                    return new ColumnInt(cache as Cache<int>, hexdisplay ? SimpleColumnDisplay.Hex : SimpleColumnDisplay.Default) as SimpleColumn<DataT>;
                case TypeCode.UInt64:
                    return new ColumnULong(cache as Cache<ulong>, hexdisplay ? SimpleColumnDisplay.Hex : SimpleColumnDisplay.Default) as SimpleColumn<DataT>;

                default:
                    return new SimpleColumn<DataT>(cache, SimpleColumnDisplay.Default);
            }
        }

        public static SimpleColumn<DataT> MakeColumnManaged<DataT>(DataT[] source, bool hexdisplay)
            where DataT : IComparable
        {
            var cache = new Cache<DataT>(new AdaptorManagedArray<DataT>(source));
            return new SimpleColumn<DataT>(cache, SimpleColumnDisplay.Default);
        }

        public static SimpleColumn<DataT> MakeColumnManaged<DataT>(DataT[] source1, long source1Count, DataT[] source2, long source2Count, bool hexdisplay)
            where DataT : IComparable
        {
            var cache = new Cache<DataT>(new AdaptorCombinedManagedArray<DataT>(source1, source1Count, source2, source2Count));
            return new SimpleColumn<DataT>(cache, SimpleColumnDisplay.Default);
        }

        public static ColumnArray<DataT> MakeColumn<DataT>(DataT[] source) where DataT : System.IComparable
        {
            return new ColumnArray<DataT>(source);
        }

        public static Column_Transform<DataOutT, DataInT> MakeColumn_Transform<DataOutT, DataInT>(DynamicArray<DataInT> source, Column_Transform<DataOutT, DataInT>.Transformer transform, Column_Transform<DataOutT, DataInT>.Untransformer untransform) where DataInT : unmanaged, IComparable

            where DataOutT : IComparable
        {
            var cache = new Cache<DataInT>(new AdaptorDynamicArray<DataInT>(source));
            return new Column_Transform<DataOutT, DataInT>(cache, transform, untransform);
        }

        public static Column_Transform<DataOutT, DataInT> MakeColumn_ManagedTransform<DataOutT, DataInT>(DataInT[] source, Column_Transform<DataOutT, DataInT>.Transformer transform, Column_Transform<DataOutT, DataInT>.Untransformer untransform)

            where DataOutT : IComparable
        {
            var cache = new Cache<DataInT>(new AdaptorManagedArray<DataInT>(source));
            return new Column_Transform<DataOutT, DataInT>(cache, transform, untransform);
        }

        /// <summary>
        /// Upon request of a data value, it will request data from a DataSource in chunks and store it for later requests.
        /// </summary>
        /// <typeparam name="DataT"></typeparam>
        public class Cache<DataT>
        {
            public Cache(DataSource<DataT> source)
            {
                m_DataSource = source;
            }

            DataSource<DataT> m_DataSource;

            public DataT this[long i]
            {
                get
                {
                    return m_DataSource[i];
                }
                set
                {
                    m_DataSource[i] = value;
                }
            }
            public long Length
            {
                get
                {
                    return m_DataSource.Length;
                }
            }
        }

        public enum SimpleColumnDisplay
        {
            Default = 0,
            Hex = 1
        }

        public class SimpleColumn<DataT> : ColumnTyped<DataT> where DataT : IComparable
        {
            protected Cache<DataT> m_Cache;
            SimpleColumnDisplay m_Display;
            public SimpleColumn(Cache<DataT> cache, SimpleColumnDisplay colDisplay)
            {
                m_Cache = cache;
                type = typeof(DataT);
                m_Display = colDisplay;
            }

            public override long GetRowCount()
            {
                return m_Cache.Length;
            }

            public int LowerBoundIndex(long[] index, int first, int count, IComparable v)
            {
                while (count > 0)
                {
                    int it = first;
                    int step = count / 2;
                    it += step;
                    if (m_Cache[index[it]].CompareTo(v) < 0)
                    {
                        first = it + 1;
                        count -= step + 1;
                    }
                    else
                    {
                        count = step;
                    }
                }
                return first;
            }

            public int UpperBoundIndex(long[] index, int first, int count, IComparable v)
            {
                while (count > 0)
                {
                    int it = first;
                    int step = count / 2;
                    it += step;
                    if (v.CompareTo(m_Cache[index[it]]) >= 0)
                    {
                        first = it + 1;
                        count -= step + 1;
                    }
                    else
                    {
                        count = step;
                    }
                }
                return first;
            }

            public override long[] GetMatchIndex(ArrayRange rowRange, Operation.Operator operation, Operation.Expression expression, long expressionRowFirst, bool rowToRow)
            {
                Update();
                long count = rowRange.Count;
                var matchedIndices = new List<long>(128);
                Operation.Operation.ComparableComparator comparator = Operation.Operation.GetComparator(type, expression.type);
                if (rowToRow)
                {
                    for (long i = 0; i != count; ++i)
                    {
                        var leftValue = m_Cache[rowRange[i]];
                        if (Operation.Operation.Match(operation, comparator, leftValue, expression, expressionRowFirst + i))
                        {
                            matchedIndices.Add(rowRange[i]);
                        }
                    }
                }
                else
                {
                    if (Operation.Operation.IsOperatorOneToMany(operation))
                    {
                        for (int i = 0; i != count; ++i)
                        {
                            var leftValue = m_Cache[rowRange[i]];
                            if (Operation.Operation.Match(operation, comparator, leftValue, expression, expressionRowFirst))
                            {
                                matchedIndices.Add(rowRange[i]);
                            }
                        }
                    }
                    else
                    {
                        var valueRight = expression.GetComparableValue(expressionRowFirst);
                        //Optimization for equal operation when querying on all data
                        if (rowRange.IsSequence && operation == Operation.Operator.Equal)
                        {
                            //use the sorted index to trim down invalid values
                            long[] sortedIndex = GetSortIndexAsc();
                            int lowerIndexIndex = LowerBoundIndex(sortedIndex, (int)rowRange.Sequence.First, (int)rowRange.Count, valueRight);
                            int upperIndexIndex = (int)rowRange.Sequence.Last;
                            for (int i = lowerIndexIndex; i < upperIndexIndex; ++i)
                            {
                                if (m_Cache[sortedIndex[i]].CompareTo(valueRight) == 0)
                                {
                                    matchedIndices.Add(sortedIndex[i]);
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }
                        else
                        {
                            for (int i = 0; i != count; ++i)
                            {
                                var leftValue = m_Cache[rowRange[i]];
                                if (Operation.Operation.Match(operation, comparator, leftValue, valueRight))
                                {
                                    matchedIndices.Add(rowRange[i]);
                                }
                            }
                        }
                    }
                }

                var matchedIndicesArray = matchedIndices.ToArray();

                return matchedIndicesArray;
            }

            protected override long[] GetSortIndex(IComparer<DataT> comparer, ArrayRange indices, bool relativeIndex)
            {
                if (indices.IsIndex)
                {
                    return base.GetSortIndex(comparer, indices, relativeIndex);
                }
                long count = indices.Count;
                DataT[] k = new DataT[count];

                //create index array
                long[] index = new long[count];
                if (relativeIndex)
                {
                    for (long i = 0; i != count; ++i)
                    {
                        index[i] = i;
                        k[i] = GetRowValue(i + indices.Sequence.First);
                    }
                }
                else
                {
                    for (long i = 0; i != count; ++i)
                    {
                        index[i] = i + indices.Sequence.First;
                        k[i] = GetRowValue(i + indices.Sequence.First);
                    }
                }
                System.Array.Sort(k, index, comparer);
                return index;
            }

            public override string GetRowValueString(long row, IDataFormatter formatter)
            {
                switch (m_Display)
                {
                    case SimpleColumnDisplay.Hex:
                        return DefaultDataFormatter.Instance.FormatPointer(Convert.ToUInt64(m_Cache[row]));
                    case SimpleColumnDisplay.Default:
                        return formatter.Format(m_Cache[row]);
                    default:
                        throw new Exception("Unable to convert data type.");
                }
            }

            public override DataT GetRowValue(long row)
            {
                return m_Cache[row];
            }

            public override void SetRowValue(long row, DataT value)
            {
                m_Cache[row] = value;
            }

            //public override bool VisitRows(Visitor v, long[] indices, long firstIndex, long lastIndex)
            public override System.Collections.Generic.IEnumerable<DataT> VisitRows(ArrayRange indices)
            {
                for (long i = 0; i != indices.Count; ++i)
                {
                    yield return m_Cache[indices[i]];
                }
            }
        }

        /// <summary>
        /// `Struct-of-Array` column for `long` value type. duplicated from column<DataT> to improve performances
        /// </summary>
        public class ColumnULong : SimpleColumn<ulong>
        {
            public ColumnULong(Cache<ulong> cache, SimpleColumnDisplay display)
                : base(cache, display)
            {
            }

            public int LowerBoundIndex(long[] index, int first, int count, ulong v)
            {
                while (count > 0)
                {
                    int it = first;
                    int step = count / 2;
                    it += step;
                    if (m_Cache[index[it]] < v)
                    {
                        first = it + 1;
                        count -= step + 1;
                    }
                    else
                    {
                        count = step;
                    }
                }
                return first;
            }

            public int UpperBoundIndex(long[] index, int first, int count, ulong v)
            {
                while (count > 0)
                {
                    int it = first;
                    int step = count / 2;
                    it += step;
                    if (v >= m_Cache[index[it]])
                    {
                        first = it + 1;
                        count -= step + 1;
                    }
                    else
                    {
                        count = step;
                    }
                }
                return first;
            }

            public override long[] GetMatchIndex(ArrayRange rowRange, Operation.Operator operation, Operation.Expression expression, long expressionRowFirst, bool rowToRow)
            {
                if (expression.type != type)
                {
                    return base.GetMatchIndex(rowRange, operation, expression, expressionRowFirst, rowToRow);
                }

                Update();
                Operation.TypedExpression<ulong> typedExpression = expression as Operation.TypedExpression<ulong>;
                long count = rowRange.Count;
                var matchedIndices = new List<long>(128);
                if (rowToRow)
                {
                    for (long i = 0; i != count; ++i)
                    {
                        var lhs = m_Cache[rowRange[i]];
                        var rhs = typedExpression.GetValue(expressionRowFirst + i);
                        if (Operation.Operation.Match(operation, lhs, rhs))
                        {
                            matchedIndices.Add(rowRange[i]);
                        }
                    }
                }
                else
                {
                    if (Operation.Operation.IsOperatorOneToMany(operation))
                    {
                        for (int i = 0; i != count; ++i)
                        {
                            var leftValue = m_Cache[rowRange[i]];
                            if (Operation.Operation.Match(operation, leftValue, typedExpression, expressionRowFirst))
                            {
                                matchedIndices.Add(rowRange[i]);
                            }
                        }
                    }
                    else
                    {
                        var valueRight = typedExpression.GetValue(expressionRowFirst);
                        //Optimization for equal operation when querying on all data
                        if (rowRange.IsSequence && operation == Operation.Operator.Equal)
                        {
                            //use the sorted index to trim down invalid values
                            long[] sortedIndex = GetSortIndexAsc();
                            int lowerIndexIndex = LowerBoundIndex(sortedIndex, (int)rowRange.Sequence.First, (int)rowRange.Count, valueRight);
                            int upperIndexIndex = (int)rowRange.Sequence.Last;
                            for (int i = lowerIndexIndex; i < upperIndexIndex; ++i)
                            {
                                if (m_Cache[sortedIndex[i]] == valueRight)
                                {
                                    matchedIndices.Add(sortedIndex[i]);
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }
                        else
                        {
                            for (int i = 0; i != count; ++i)
                            {
                                var leftValue = m_Cache[rowRange[i]];
                                if (Operation.Operation.Match(operation, leftValue, valueRight))
                                {
                                    matchedIndices.Add(rowRange[i]);
                                }
                            }
                        }
                    }
                }
                var matchedIndicesArray = matchedIndices.ToArray();
                return matchedIndicesArray;
            }
        }


        /// <summary>
        /// `Struct-of-Array` column for `long` value type. duplicated from column<DataT> to improve performances
        /// </summary>
        public class ColumnLong : SimpleColumn<long>
        {
            public ColumnLong(Cache<long> cache, SimpleColumnDisplay display)
                : base(cache, display)
            {
            }

            public int LowerBoundIndex(long[] index, int first, int count, long v)
            {
                while (count > 0)
                {
                    int it = first;
                    int step = count / 2;
                    it += step;
                    if (m_Cache[index[it]] < v)
                    {
                        first = it + 1;
                        count -= step + 1;
                    }
                    else
                    {
                        count = step;
                    }
                }
                return first;
            }

            public int UpperBoundIndex(long[] index, int first, int count, long v)
            {
                while (count > 0)
                {
                    int it = first;
                    int step = count / 2;
                    it += step;
                    if (v >= m_Cache[index[it]])
                    {
                        first = it + 1;
                        count -= step + 1;
                    }
                    else
                    {
                        count = step;
                    }
                }
                return first;
            }

            public override long[] GetMatchIndex(ArrayRange rowRange, Operation.Operator operation, Operation.Expression expression, long expressionRowFirst, bool rowToRow)
            {
                if (expression.type != type)
                {
                    return base.GetMatchIndex(rowRange, operation, expression, expressionRowFirst, rowToRow);
                }

                Update();
                Operation.TypedExpression<long> typedExpression = expression as Operation.TypedExpression<long>;
                long count = rowRange.Count;
                var matchedIndices = new List<long>(128);
                if (rowToRow)
                {
                    for (long i = 0; i != count; ++i)
                    {
                        var lhs = m_Cache[rowRange[i]];
                        var rhs = typedExpression.GetValue(expressionRowFirst + i);
                        if (Operation.Operation.Match(operation, lhs, rhs))
                        {
                            matchedIndices.Add(rowRange[i]);
                        }
                    }
                }
                else
                {
                    if (Operation.Operation.IsOperatorOneToMany(operation))
                    {
                        for (int i = 0; i != count; ++i)
                        {
                            var leftValue = m_Cache[rowRange[i]];
                            if (Operation.Operation.Match(operation, leftValue, typedExpression, expressionRowFirst))
                            {
                                matchedIndices.Add(rowRange[i]);
                            }
                        }
                    }
                    else
                    {
                        var valueRight = typedExpression.GetValue(expressionRowFirst);
                        //Optimization for equal operation when querying on all data
                        if (rowRange.IsSequence && operation == Operation.Operator.Equal)
                        {
                            //use the sorted index to trim down invalid values
                            long[] sortedIndex = GetSortIndexAsc();
                            int lowerIndexIndex = LowerBoundIndex(sortedIndex, (int)rowRange.Sequence.First, (int)rowRange.Count, valueRight);
                            int upperIndexIndex = (int)rowRange.Sequence.Last;
                            for (int i = lowerIndexIndex; i < upperIndexIndex; ++i)
                            {
                                if (m_Cache[sortedIndex[i]] == valueRight)
                                {
                                    matchedIndices.Add(sortedIndex[i]);
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }
                        else
                        {
                            for (int i = 0; i != count; ++i)
                            {
                                var leftValue = m_Cache[rowRange[i]];
                                if (Operation.Operation.Match(operation, leftValue, valueRight))
                                {
                                    matchedIndices.Add(rowRange[i]);
                                }
                            }
                        }
                    }
                }
                var matchedIndicesArray = matchedIndices.ToArray();
                return matchedIndicesArray;
            }
        }

        /// <summary>
        /// `Struct-of-Array` column for `int` value type. duplicated from column<int> to improve performances
        /// </summary>
        public class ColumnInt : SimpleColumn<int>
        {
            public ColumnInt(Cache<int> cache, SimpleColumnDisplay display)
                : base(cache, display)
            {
            }

            public int LowerBoundIndex(long[] index, int first, int count, int v)
            {
                while (count > 0)
                {
                    int it = first;
                    int step = count / 2;
                    it += step;
                    if (m_Cache[index[it]] < v)
                    {
                        first = it + 1;
                        count -= step + 1;
                    }
                    else
                    {
                        count = step;
                    }
                }
                return first;
            }

            public int UpperBoundIndex(long[] index, int first, int count, int v)
            {
                while (count > 0)
                {
                    int it = first;
                    int step = count / 2;
                    it += step;
                    if (v >= m_Cache[index[it]])
                    {
                        first = it + 1;
                        count -= step + 1;
                    }
                    else
                    {
                        count = step;
                    }
                }
                return first;
            }

            public override long[] GetMatchIndex(ArrayRange rowRange, Operation.Operator operation, Operation.Expression expression, long expressionRowFirst, bool rowToRow)
            {
                if (expression.type != type)
                {
                    return base.GetMatchIndex(rowRange, operation, expression, expressionRowFirst, rowToRow);
                }
                Update();
                Operation.TypedExpression<int> typedExpression = expression as Operation.TypedExpression<int>;
                long count = rowRange.Count;
                var matchedIndices = new List<long>(128);
                if (rowToRow)
                {
                    for (long i = 0; i != count; ++i)
                    {
                        var lhs = m_Cache[rowRange[i]];
                        var rhs = typedExpression.GetValue(expressionRowFirst + i);
                        if (Operation.Operation.Match(operation, lhs, rhs))
                        {
                            matchedIndices.Add(rowRange[i]);
                        }
                    }
                }
                else
                {
                    if (Operation.Operation.IsOperatorOneToMany(operation))
                    {
                        for (int i = 0; i != count; ++i)
                        {
                            var leftValue = m_Cache[rowRange[i]];
                            if (Operation.Operation.Match(operation, leftValue, typedExpression, expressionRowFirst))
                            {
                                matchedIndices.Add(rowRange[i]);
                            }
                        }
                    }
                    else
                    {
                        var valueRight = typedExpression.GetValue(expressionRowFirst);
                        //Optimization for equal operation when querying on all data
                        if (rowRange.IsSequence && operation == Operation.Operator.Equal)
                        {
                            //use the sorted index to trim down invalid values
                            long[] sortedIndex = GetSortIndexAsc();
                            int lowerIndexIndex = LowerBoundIndex(sortedIndex, (int)rowRange.Sequence.First, (int)rowRange.Count, valueRight);
                            int upperIndexIndex = (int)rowRange.Sequence.Last;
                            for (int i = lowerIndexIndex; i < upperIndexIndex; ++i)
                            {
                                if (m_Cache[sortedIndex[i]] == valueRight)
                                {
                                    matchedIndices.Add(sortedIndex[i]);
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }
                        else
                        {
                            for (int i = 0; i != count; ++i)
                            {
                                var leftValue = m_Cache[rowRange[i]];
                                if (Operation.Operation.Match(operation, leftValue, valueRight))
                                {
                                    matchedIndices.Add(rowRange[i]);
                                }
                            }
                        }
                    }
                }

                var matchedIndicesArray = matchedIndices.ToArray();
                return matchedIndicesArray;
            }
        }

        public class Column_Transform<DataOutT, DataInT> : Database.ColumnTyped<DataOutT> where DataOutT : System.IComparable
        {
            protected Cache<DataInT> m_Cache;
            public delegate DataOutT Transformer(DataInT a);
            public delegate void Untransformer(ref DataInT a, DataOutT b);

            Transformer m_Transformer;
            Untransformer m_Untransformer;
            public Column_Transform(Cache<DataInT> cache, Transformer transformer, Untransformer untransformer)
            {
                m_Cache = cache;
                m_Transformer = transformer;
                m_Untransformer = untransformer;
                type = typeof(DataOutT);
            }

            public override long GetRowCount()
            {
                return m_Cache.Length;
            }

            public override string GetRowValueString(long row, IDataFormatter formatter)
            {
                return formatter.Format(m_Transformer(m_Cache[row]));
            }

            public override DataOutT GetRowValue(long row)
            {
                return m_Transformer(m_Cache[row]);
            }

            public override void SetRowValue(long row, DataOutT value)
            {
                if (m_Untransformer != null)
                {
                    var rVal = m_Cache[row];
                    m_Untransformer(ref rVal, value);
                    m_Cache[row] = rVal;
                }
            }

            public override System.Collections.Generic.IEnumerable<DataOutT> VisitRows(ArrayRange indices)
            {
                for (long i = 0; i != indices.Count; ++i)
                {
                    yield return m_Transformer(m_Cache[indices[i]]);
                }
            }
        }


        public class ColumnArray<DataT> : ColumnTyped<DataT> where DataT : IComparable
        {
            protected DataT[] m_Data;
            public ColumnArray(DataT[] data)
            {
                m_Data = data;
                type = typeof(DataT);
            }

            public override long GetRowCount()
            {
                return m_Data.LongLength;
            }

            protected override long[] GetSortIndex(IComparer<DataT> comparer, ArrayRange indices, bool relativeIndex)
            {
                if (indices.IsIndex)
                {
                    return base.GetSortIndex(comparer, indices, relativeIndex);
                }
                long count = indices.Count;
                DataT[] k = new DataT[count];
                System.Array.Copy(m_Data, indices.Sequence.First, k, 0, count);

                //create index array
                long[] index = new long[count];
                if (relativeIndex)
                {
                    for (long i = 0; i != count; ++i)
                    {
                        index[i] = i;
                    }
                }
                else
                {
                    for (long i = 0; i != count; ++i)
                    {
                        index[i] = i + indices.Sequence.First;
                    }
                }

                System.Array.Sort(k, index, comparer);
                return index;
            }

            public override string GetRowValueString(long row, IDataFormatter formatter)
            {
                return formatter.Format(m_Data[row]);
            }

            public override DataT GetRowValue(long row)
            {
                return m_Data[row];
            }

            public override void SetRowValue(long row, DataT value)
            {
                m_Data[row] = value;
            }

            public override IEnumerable<DataT> VisitRows(ArrayRange indices)
            {
                for (long i = 0; i != indices.Count; ++i)
                {
                    yield return m_Data[indices[i]];
                }
            }
        }
    }
}

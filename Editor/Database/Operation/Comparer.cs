namespace Unity.MemoryProfiler.Editor.Database.Operation
{
    internal class Comparer
    {
        public static System.Collections.Generic.IComparer<DataT> Ascending<DataT>() where DataT : System.IComparable
        {
            if (typeof(DataT).IsValueType)
            {
                return new AscendingComparerValueType<DataT>();
            }
            else
            {
                return new AscendingComparerReferenceType<DataT>();
            }
        }

        public static System.Collections.Generic.IComparer<DataT> Descending<DataT>() where DataT : System.IComparable
        {
            if (typeof(DataT).IsValueType)
            {
                return new DescendingComparerValueType<DataT>();
            }
            else
            {
                return new DescendingComparerReferenceType<DataT>();
            }
        }
    }

    internal class AscendingComparerValueType<DataT> : System.Collections.Generic.IComparer<DataT> where DataT : System.IComparable
    {
        public int Compare(DataT a, DataT b) { return a.CompareTo(b); }
    }

    internal class AscendingComparerReferenceType<DataT> : System.Collections.Generic.IComparer<DataT> where DataT : System.IComparable
    {
        public int Compare(DataT a, DataT b)
        {
            if (a == null)
            {
                if (b == null) return 0;
                return -1;
            }
            return a.CompareTo(b);
        }
    }

    internal class DescendingComparerValueType<DataT> : System.Collections.Generic.IComparer<DataT> where DataT : System.IComparable
    {
        public int Compare(DataT a, DataT b) { return b.CompareTo(a); }
    }

    internal class DescendingComparerReferenceType<DataT> : System.Collections.Generic.IComparer<DataT> where DataT : System.IComparable
    {
        public int Compare(DataT a, DataT b)
        {
            if (b == null)
            {
                if (a == null) return 0;
                return -1;
            }
            return b.CompareTo(a);
        }
    }
}

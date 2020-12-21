using System;
using Unity.MemoryProfiler.Editor.Format;

namespace Unity.MemoryProfiler.Editor
{
    internal class DataSourceFromAPI
    {
        public abstract class Adaptor<DataT> : Database.Soa.DataSource<DataT>
        {
            //crappy hack
        }

        public class AdaptorArray<DataT> : Adaptor<DataT>
        {
            private DataT[] m_Array;
            public AdaptorArray(DataT[] array)
            {
                m_Array = array;
            }

            public override void Get(Range range, ref DataT[] dataOut)
            {
                for (long i = range.First; i < range.Length; ++i)
                {
                    dataOut[i] = m_Array[i];
                }
            }
        }

        public class AdaptorAPIArray<DataT> : Adaptor<DataT>
        {
            private IArrayEntries<DataT> m_Array;
            public AdaptorAPIArray(IArrayEntries<DataT> array)
            {
                m_Array = array;
            }

            public override void Get(Range range, ref DataT[] dataOut)
            {
                m_Array.GetEntries((uint)range.First, (uint)range.Length, ref dataOut);
            }
        }

        public class Adaptor_String : Database.Soa.DataSource<string>
        {
            private IArrayEntries<string> m_Array;
            public Adaptor_String(IArrayEntries<string> array)
            {
                m_Array = array;
            }

            public override void Get(Range range, ref string[] dataOut)
            {
                if (dataOut.Length != range.Length)
                    throw new ArgumentException("DataOut should have the same amount of elements are the range requires");
                m_Array.GetEntries((uint)range.First, (uint)range.Length, ref dataOut);
            }
        }

        public class Adaptor_Array<DataT> : Database.Soa.DataSource<DataT[]> where DataT : IComparable
        {
            private IArrayEntries<DataT[]> m_Array;
            public Adaptor_Array(IArrayEntries<DataT[]> array)
            {
                m_Array = array;
            }

            public override void Get(Range range, ref DataT[][] dataOut)
            {
                dataOut = new DataT[range.Length][];
                m_Array.GetEntries((uint)range.First, (uint)range.Length, ref dataOut);
            }
        }
        public static Adaptor<DataT> ApiToDatabase<DataT>(IArrayEntries<DataT> array)
        {
            return new AdaptorAPIArray<DataT>(array);
        }

        public static Adaptor<DataT> ApiToDatabase<DataT>(DataT[] array)
        {
            return new AdaptorArray<DataT>(array);
        }

        public static Adaptor_String ApiToDatabase(IArrayEntries<string> array)
        {
            return new Adaptor_String(array);
        }

        public static Adaptor_Array<DataT> ApiToDatabase<DataT>(IArrayEntries<DataT[]> array) where DataT : IComparable
        {
            return new Adaptor_Array<DataT>(array);
        }
    }
}

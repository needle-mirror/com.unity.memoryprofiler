using System.Collections.Generic;
using System;

namespace Unity.MemoryProfiler.Editor.Database
{
    internal interface IDataFormatter
    {
        string Format(object obj);
    }

    internal interface IDataFormatter<T>
    {
        string Format(T obj);
    }

    internal class DefaultDataFormatter
        : IDataFormatter
        , IDataFormatter<object>
        , IDataFormatter<bool>
        , IDataFormatter<short>
        , IDataFormatter<int>
        , IDataFormatter<long>
        , IDataFormatter<ushort>
        , IDataFormatter<uint>
        , IDataFormatter<ulong>
        , IDataFormatter<float>
        , IDataFormatter<double>
        , IDataFormatter<string>
        , IDataFormatter<Operation.DiffTable.DiffResult>
    {
        static DefaultDataFormatter m_Instance;
        const string k_NullPtrAddr = "0x0000000000000000";

        public static DefaultDataFormatter Instance
        {
            get
            {
                if (m_Instance == null)
                    m_Instance = new DefaultDataFormatter();

                return m_Instance;
            }
        }

        // Formats "0x{ptr}" or "null" if ptr == 0
        public string FormatPointer(ulong ptr)
        {
            if (ptr == 0) return k_NullPtrAddr;
            return string.Format("0x{0:x16}", ptr);
        }

        string IDataFormatter.Format(object obj)
        {
            if (obj == null) return "";
            return obj.ToString();
        }

        string IDataFormatter<object>.Format(object obj)
        {
            if (obj == null) return "";
            return obj.ToString();
        }

        string IDataFormatter<bool>.Format(bool value) { return value.ToString(); }
        string IDataFormatter<short>.Format(short value) { return value.ToString(); }
        string IDataFormatter<int>.Format(int value) { return value.ToString(); }
        string IDataFormatter<long>.Format(long value) { return value.ToString(); }
        string IDataFormatter<ushort>.Format(ushort value) { return value.ToString(); }
        string IDataFormatter<uint>.Format(uint value) { return value.ToString(); }
        string IDataFormatter<ulong>.Format(ulong value) { return value.ToString(); }
        string IDataFormatter<float>.Format(float value) { return value.ToString(); }
        string IDataFormatter<double>.Format(double value) { return value.ToString(); }
        string IDataFormatter<string>.Format(string value) { return value.ToString(); }
        string IDataFormatter<Operation.DiffTable.DiffResult>.Format(Operation.DiffTable.DiffResult value) { return value.ToString(); }
    }
}

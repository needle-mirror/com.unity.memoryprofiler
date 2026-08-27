using System;
using System.Collections.Generic;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// ITableFilter implementation that matches an enum value against a set of allowed enum values.
    /// </summary>
    class EnumSetFilter<T> : ITableFilter<T>
        where T : struct, System.Enum
    {
        readonly HashSet<T> m_Statuses;
        readonly T m_NoneStatus;

        public T Value => default;

        public EnumSetFilter(HashSet<T> statuses, T noneStatus)
        {
            m_Statuses = statuses;
            m_NoneStatus = noneStatus;
        }

        public bool Passes(T status)
            => status.CompareTo(m_NoneStatus) == 0 || (m_Statuses?.Contains(status) ?? false);
    }

    /// <summary>
    /// Scoped filter for enum values, analogous to ScopedContainsTextFilter for strings.
    /// Created from a set of allowed enum values; returns null from Create() when the set is empty.
    /// </summary>
    class ScopedEnumFilter<T> : ScopedFilter<EnumSetFilter<T>, T>
        where T : struct, System.Enum
    {
        ScopedEnumFilter(EnumSetFilter<T> filter) : base(filter) { }

        public static ScopedEnumFilter<T> Create(HashSet<T> statuses, T noneStatus)
        {
            return statuses?.Count > 0 ? new ScopedEnumFilter<T>(new EnumSetFilter<T>(statuses, noneStatus)) : null;
        }
    }
}

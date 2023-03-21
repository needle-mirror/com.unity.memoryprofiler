#if UNITY_2022_1_OR_NEWER
using System;

namespace Unity.MemoryProfiler.Editor.UI
{
    abstract class ScopedFilter<TFilter, TType> : IScopedFilter<TType> where TFilter : ITableFilter<TType>
    {
        public TType FilterValue => m_Filter.Value;

        public bool CurrentScopePasses => m_CurrentScopePasses;

        TFilter m_Filter;
        bool m_CurrentScopePasses = false;

        protected ScopedFilter(TFilter filter)
        {
            m_Filter = filter;
        }


        public IScopedFilter< TType>.IFilterScope<TType> OpenScope(TType scopeFilterValue, CachedSnapshot cachedSnapshot = null)
        {
            return new FilterScope(this, scopeFilterValue, cachedSnapshot);
        }

        struct FilterScope : IScopedFilter<TType>.IFilterScope<TType>
        {
            ScopedFilter<TFilter, TType> m_ScopedFilter;
            bool m_ParentScopePasses;

            public bool ScopePasses { get; private set; }

            public TType Value => m_ScopedFilter.FilterValue;

            public FilterScope(ScopedFilter<TFilter, TType> scopedFilter, TType scopeFilterValue, CachedSnapshot cachedSnapshot = null)
            {
                m_ScopedFilter = scopedFilter;
                m_ParentScopePasses = m_ScopedFilter.m_CurrentScopePasses;
                ScopePasses = m_ScopedFilter.m_CurrentScopePasses;
                m_ScopedFilter.m_CurrentScopePasses = ScopePasses = Passes(scopeFilterValue, cachedSnapshot);
            }

            public bool Passes(TType value, CachedSnapshot cachedSnapshot = null)
            {
                if (ScopePasses)
                    return true;
                return m_ScopedFilter.m_Filter.Passes(value, cachedSnapshot);
            }

            public void Dispose()
            {
                m_ScopedFilter.m_CurrentScopePasses = m_ParentScopePasses;
            }
        }
    }
}
#endif

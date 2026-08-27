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


        public IScopedFilter<TType>.IFilterScope<TType> OpenScope(TType scopeFilterValue)
        {
            return new FilterScope(this, scopeFilterValue);
        }

        public IScopedFilter<TType>.IFilterScope<TType> OpenEmptyScope()
        {
            return FilterScope.OpenEmptyScope(this);
        }

        struct FilterScope : IScopedFilter<TType>.IFilterScope<TType>
        {
            ScopedFilter<TFilter, TType> m_ScopedFilter;
            bool m_ParentScopePasses;

            public bool ScopePasses { get; private set; }

            public TType Value => m_ScopedFilter.FilterValue;

            public FilterScope(ScopedFilter<TFilter, TType> scopedFilter, TType scopeFilterValue)
            {
                m_ScopedFilter = scopedFilter;
                m_ParentScopePasses = m_ScopedFilter.m_CurrentScopePasses;
                ScopePasses = m_ScopedFilter.m_CurrentScopePasses;
                m_ScopedFilter.m_CurrentScopePasses = ScopePasses = Passes(scopeFilterValue);
            }

            FilterScope(ScopedFilter<TFilter, TType> scopedFilter)
            {
                m_ScopedFilter = scopedFilter;
                m_ParentScopePasses = m_ScopedFilter.m_CurrentScopePasses;
                ScopePasses = m_ScopedFilter.m_CurrentScopePasses;
                m_ScopedFilter.m_CurrentScopePasses = ScopePasses;
            }
            public static FilterScope OpenEmptyScope(ScopedFilter<TFilter, TType> scopedFilter) => new FilterScope(scopedFilter);

            public bool Passes(TType value)
            {
                if (ScopePasses)
                    return true;
                return m_ScopedFilter.m_Filter.Passes(value);
            }

            public void Dispose()
            {
                m_ScopedFilter.m_CurrentScopePasses = m_ParentScopePasses;
            }
        }
    }
}

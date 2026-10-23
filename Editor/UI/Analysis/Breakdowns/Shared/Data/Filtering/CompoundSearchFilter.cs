using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Right now the search filtering is pretty basic, as there is no query language implemented yet.
    /// The search text either matches the name of an item, or it matches the status of an item,
    /// but there is no way to filter for any column values or element properties other than name and status.
    /// For status filtering there is some special handling to avoid having to parse status values by name every time.
    /// This adds some complexity that waranted having a separate class to encapsulate the logic, and to keep the code in the main view cleaner.
    /// This class could eventually be used for more complex search filtering when we have a query language.
    /// </summary>
    class CompoundSearchFilter
    {
        public IScopedFilter<string> SearchStringFilter { get; private set; }
        public IScopedFilter<ObjectStatus> StatusFilter { get; private set; }
        /// <summary>
        /// Bitmask of allowed <see cref="ObjectStatus"/> values (bit N = status N allowed).
        /// Unlike the search string filter, which can match either the name or the status, this is
        /// a hard restriction: only objects whose status bit is set in the mask are shown, regardless
        /// of the search string. <c>null</c> means no restriction — all statuses pass.
        /// </summary>
        public ulong? ExclusiveStatusMask { get; private set; }
        public bool CurrentScopePasses => m_CurrentScopePasses;

        ContainsTextFilter m_SearchStringFilterFlat;
        EnumSetFilter<ObjectStatus> m_StatusFilterBaseFlat;

        bool m_CurrentScopePasses = false;

        public CompoundSearchFilter(string searchText = null, HashSet<ObjectStatus> statusFilter = null, ulong? exclusiveStatusMask = null, ulong? inclusiveStatusMask = null)
        {
            SearchStringFilter = ScopedContainsTextFilter.Create(searchText);
            m_SearchStringFilterFlat = ContainsTextFilter.Create(searchText);

            HashSet<ObjectStatus> matchingStatuses;
            if (statusFilter != null)
            {
                matchingStatuses = statusFilter;
            }
            else if (inclusiveStatusMask.HasValue)
            {
                // Convert bitmask to a HashSet (bit N = ObjectStatus N).
                matchingStatuses = new HashSet<ObjectStatus>();
                var mask = inclusiveStatusMask.Value;
                foreach (var status in ObjectStatusExtensions.DisplayStatusValues)
                {
                    if ((mask & (1ul << (int)status)) != 0)
                        matchingStatuses.Add(status);
                }
            }
            else
            {
                matchingStatuses = ObjectStatusExtensions.GetMatchingStatuses(searchText);
            }

            StatusFilter = ScopedEnumFilter<ObjectStatus>.Create(matchingStatuses, ObjectStatus.None);
            m_StatusFilterBaseFlat = matchingStatuses != null ? new EnumSetFilter<ObjectStatus>(matchingStatuses, ObjectStatus.None) : null;
            ExclusiveStatusMask = exclusiveStatusMask;
        }

        /// <summary>
        /// Cannot create a text filter with a null filter string or an empty filter string.
        /// If this filter should match for any input, use <see cref="MatchesAllTextFilter"/> instead.
        /// </summary>
        public static CompoundSearchFilter Create(string text, ulong? exclusiveStatusMask = null, ulong? inclusiveStatusMask = null)
        {
            // Cannot create a text filter with a null filter string. Note that string.Empty is allowed, which will pass non-null strings (i.e. 'contains any text').
            if (string.IsNullOrEmpty(text) && exclusiveStatusMask == null && inclusiveStatusMask == null)
                return null;

            return new CompoundSearchFilter(text, exclusiveStatusMask: exclusiveStatusMask, inclusiveStatusMask: inclusiveStatusMask);
        }

        /// <summary>
        /// Tests <paramref name="text"/> against the text component of the filter only (ordinal, case-insensitive contains);
        /// passes everything when no text filter is set. Status filters are deliberately not consulted here — check them
        /// separately via <see cref="PassesFlat(ObjectStatus)"/> so callers can combine the two with the semantics they need.
        /// </summary>
        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public bool PassesFlat(string text)
        {
            // if no text filter is set, check if there are status filters.
            // The non exclusive status filter is likely to be only set if there is a text based search string but don't rely on it.
            return m_SearchStringFilterFlat?.Passes(text) ?? (m_StatusFilterBaseFlat == null && ExclusiveStatusMask == null);
        }

        /// <summary>
        /// True only when a non-empty search-text filter is set AND <paramref name="name"/> matches it.
        /// Unlike <see cref="PassesFlat(string)"/> — which passes everything when no text filter is set —
        /// this returns false in that case, so callers can detect a genuine text match on a group/bucket
        /// header name (and reveal that group) without a status-only or empty filter spuriously matching.
        /// </summary>
        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public bool MatchesTextName(string name) => m_SearchStringFilterFlat?.Passes(name) ?? false;

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public bool PassesFlat(ObjectStatus status)
        {
            // if no filters are set, pass everything. Otherwise just check the status filters, here and the string filter has to be checked separately.
            return ExclusiveStatusPasses(status)
                && (m_StatusFilterBaseFlat?.Passes(status) ?? m_SearchStringFilterFlat == null);
        }

        /// <summary>
        /// The search-text half of <see cref="PassesFlat(string, ObjectStatus)"/>: whether <paramref name="text"/> matches the
        /// search string or <paramref name="status"/> matches the statuses it names, ignoring the exclusive status mask.
        /// </summary>
        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public bool PassesTextOrStatusSearch(string text, ObjectStatus status)
        {
            return (m_SearchStringFilterFlat == null && m_StatusFilterBaseFlat == null)
                || (m_SearchStringFilterFlat?.Passes(text) ?? false) || (m_StatusFilterBaseFlat?.Passes(status) ?? false);
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public bool PassesFlat(string text, ObjectStatus status)
        {
            // Either the text must pass the text filter, or the status must pass the status filter, for the filter to pass, or no text filter was set.
            var passesTextFilter = (m_SearchStringFilterFlat == null && m_StatusFilterBaseFlat == null)
                || (m_SearchStringFilterFlat?.Passes(text) ?? false) || (m_StatusFilterBaseFlat?.Passes(status) ?? false);
            // If the ExclusiveStatusMask is given, the status must be in it for the filter to pass.
            return ExclusiveStatusPasses(status) && passesTextFilter;
        }

        /// <summary>
        /// Opens a combined scope for a name only (no status component).
        /// Use for structural/group nodes that have no associated object status.
        /// The returned <see cref="CompoundScope"/> propagates into deeper scopes opened on this filter.
        /// </summary>
        public CompoundScope OpenScope(string name)
        {
            return new CompoundScope(this, SearchStringFilter?.OpenScope(name), StatusFilter?.OpenEmptyScope(), false);
        }

        /// <summary>
        /// Opens a combined scope for both a name and a status in a single call, replacing two separate
        /// <c>SearchStringFilter.OpenScope(name)</c> + <c>StatusFilter.OpenScope(status)</c> calls.
        /// <see cref="CompoundScope.ScopePasses"/> is true when either constituent passes (OR semantics),
        /// including any parent scope propagation already accumulated on each underlying filter.
        /// Use for leaf-level nodes where both the object name and its status are known.
        /// </summary>
        public CompoundScope OpenScope(string name, ObjectStatus status)
        {
            return new CompoundScope(this, SearchStringFilter?.OpenScope(name), StatusFilter?.OpenScope(status), !ExclusiveStatusPasses(status));
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        bool ExclusiveStatusPasses(ObjectStatus status)
            => status == ObjectStatus.None || !ExclusiveStatusMask.HasValue || (ExclusiveStatusMask.Value & (1ul << (int)status)) != 0;

        /// <summary>
        /// A disposable scope that combines a string-filter scope and a status-filter scope.
        /// <see cref="ScopePasses"/> is true when either constituent scope passes (OR semantics),
        /// accounting for parent scope propagation from both underlying filters.
        /// Dispose restores both underlying filter states.
        /// </summary>
        public readonly struct CompoundScope : IDisposable
        {
            readonly IScopedFilter<string>.IFilterScope<string> m_NameScope;
            readonly IScopedFilter<ObjectStatus>.IFilterScope<ObjectStatus> m_StatusScope;
            readonly bool m_ExclusiveStatusExcludedScope;
            readonly CompoundSearchFilter m_ScopedFilter;

            /// <summary>
            /// In contrast to the underlying name/status scopes, this compound scope does not gate its
            /// ScopePasses on the parent scope's passing state directly (though it does so indirectly via
            /// the underlying scopes), so it must track and restore the parent scope's passing state
            /// separately. Used solely by Dispose to restore it when the scope closes.
            /// </summary>
            readonly bool m_ParentScopePasses;

            /// <summary>
            /// True when this scope is not hard-excluded by the exclusive status mask and either the name
            /// scope or the status scope passes. Parent-scope propagation (a matching ancestor keeps its
            /// descendants passing) is already carried by the underlying name/status scopes' own
            /// <c>CurrentScopePasses</c> chains, so it must NOT be re-gated on the compound filter's parent
            /// state here — doing so would make the root scope (which has no passing parent) never pass.
            /// </summary>
            public bool ScopePasses => !m_ExclusiveStatusExcludedScope &&
                ((m_NameScope?.ScopePasses ?? false) || (m_StatusScope?.ScopePasses ?? false));

            internal CompoundScope(
                CompoundSearchFilter scopedFilter,
                IScopedFilter<string>.IFilterScope<string> nameScope,
                IScopedFilter<ObjectStatus>.IFilterScope<ObjectStatus> statusScope, bool exclusiveStatusExcludedScope)
            {
                m_ScopedFilter = scopedFilter;
                m_NameScope = nameScope;
                m_StatusScope = statusScope;
                m_ExclusiveStatusExcludedScope = exclusiveStatusExcludedScope;
                m_ParentScopePasses = m_ScopedFilter.m_CurrentScopePasses;
                m_ScopedFilter.m_CurrentScopePasses = ScopePasses;
            }

            public bool Passes(string text)
            {
                return ((m_NameScope?.Passes(text) ?? false));
            }

            public bool Passes(string text, ObjectStatus status)
            {
                // If the ExclusiveStatusMask is given, the status must be in it for the filter to pass.
                // Then, either the name scope must pass for the text, or the status scope must pass for the status, for the filter to pass.
                // When both scopes are null (no search text — only ExclusiveStatusMask is active), treat the
                // scopes check as passing so that ExclusiveStatusMask alone determines the result.
                bool scopesPass = (m_NameScope == null && m_StatusScope == null)
                    || (m_NameScope?.Passes(text) ?? false)
                    || (m_StatusScope?.Passes(status) ?? false);
                return m_ScopedFilter.ExclusiveStatusPasses(status) && scopesPass;
            }

            public void Dispose()
            {
                m_NameScope?.Dispose();
                m_StatusScope?.Dispose();
                m_ScopedFilter.m_CurrentScopePasses = m_ParentScopePasses;
            }
        }
    }
}

using System;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Disposes the previously-owned model when a rebuild replaces it with a different instance. A justSort
    /// rebuild reuses the same model instance (<paramref name="current"/> == <paramref name="owned"/>), so it is
    /// left alone; only a full rebuild's new instance triggers the swap.
    /// </summary>
    static class DisposableModelSwap
    {
        public static void ReplaceIfChanged<TModel>(ref TModel owned, TModel current) where TModel : class, IDisposable
        {
            if (ReferenceEquals(owned, current))
                return;
            owned?.Dispose();
            owned = current;
        }
    }
}

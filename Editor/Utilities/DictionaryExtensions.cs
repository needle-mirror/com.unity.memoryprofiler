using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Unity.MemoryProfiler.Editor.Extensions
{
    internal static class DictionaryExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key) where TValue : class, new()
        {
            if (!dictionary.TryGetValue(key, out var value))
            {
                value = new TValue();
                dictionary.Add(key, value);
            }
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GetAndAddToListOrCreateList<TKey, TValue>(this Dictionary<TKey, List<TValue>> dictionary, TKey key, TValue listItemValue)
        {
            if (dictionary.TryGetValue(key, out var list))
            {
                list.Add(listItemValue);
            }
            else
            {
                list = new List<TValue>() { listItemValue };
                dictionary.Add(key, list);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.MemoryProfiler.Editor.Extensions
{
    static class NativeHashMapExtensions
    {
        /// <summary>
        /// Gets the value from the dictionary if it exists and returns true, otherwise initializes it with the provided value and returns false.
        /// This is similar to <see cref="CollectionExtensions.GetValueOrDefault{TKey, TValue}(IReadOnlyDictionary{TKey, TValue}, TKey, TValue)"/>
        /// but with an out variable for the value, making it more practical as part of a conditional expression.
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="dictionary"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="initializeAs">What to initialize the <paramref name="value"/> with if it is missing.</param>
        /// <param name="addToDictionaryIfMissing">(Optional, defaulting to false) if or if not the key should be added to the dictionary with the initialized value if it was missing.</param>
        /// <returns>If the key existed in the dictionary beforehand or not</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool GetOrInitializeValue<TKey, TValue>(
#if !UNMANAGED_NATIVE_HASHMAP_AVAILABLE
            this Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<TKey, TValue> dictionary,
#else
            this Unity.Collections.NativeHashMap<TKey, TValue> dictionary,
#endif
            TKey key, out TValue value, TValue initializeAs, bool addToDictionaryIfMissing = false)
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            if (!dictionary.TryGetValue(key, out value))
            {
                value = initializeAs;
                if (addToDictionaryIfMissing)
                    dictionary.Add(key, value);
                return false;
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TValue GetOrAdd<TKey, TValue>(

#if !UNMANAGED_NATIVE_HASHMAP_AVAILABLE
            this Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<TKey, TValue> dictionary,
#else
            this Unity.Collections.NativeHashMap<TKey, TValue> dictionary,
#endif
            TKey key)
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            if (!dictionary.TryGetValue(key, out var value))
            {
                value = default(TValue);
                dictionary.Add(key, value);
            }
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GetAndAddToListOrCreateList<TKey, TValue>(
#if !UNMANAGED_NATIVE_HASHMAP_AVAILABLE
            this Unity.MemoryProfiler.Editor.Containers.CollectionsCompatibility.NativeHashMap<TKey, UnsafeList<TValue>> dictionary,
#else
            this Unity.Collections.NativeHashMap<TKey, UnsafeList<TValue>> dictionary,
#endif
            TKey key, TValue listItemValue, Allocator allocator)
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            if (dictionary.TryGetValue(key, out var list))
            {
                list.Add(listItemValue);
                dictionary[key] = list;
            }
            else
            {
                list = new UnsafeList<TValue>(1, allocator)
                {
                    listItemValue
                };
                dictionary.Add(key, list);
            }
        }
    }
}

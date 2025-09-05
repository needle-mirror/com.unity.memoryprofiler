using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Unity.MemoryProfiler.Editor.Extensions
{
    static class DictionaryExtensions
    {
        // TODO: rename GetOrInitializeValue to something like GetOrInitializeAndAddValue to make it more explicit that the initialized value will be added to the dictionary

        /// <summary>
        /// Gets the value from the dictionary if it exists and returns true, otherwise adds it and initializes it with the provided value and returns false.
        /// This is similar to <see cref="CollectionExtensions.GetValueOrDefault{TKey, TValue}(IReadOnlyDictionary{TKey, TValue}, TKey, TValue)"/>
        /// but with an out variable for the value, making it more practical as part of a conditional expression.
        ///
        ///NOTE: When <paramref name="value"/> contains fields of a reference type, those reference will be shallow copied so latter access might lead to those referenced items to get changed for all initialized values.
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="dictionary"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="initializeAs"></param>
        /// <param name="addToDictionaryIfMissing">(Optional, defaulting to false) if or if not the key should be added to the dictionary with the initialized value if it was missing.</param>
        /// <returns>If the key existed in the dictionary beforehand or not</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool GetOrInitializeValue<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, out TValue value, TValue initializeAs, bool addToDictionaryIfMissing = false)
            where TValue : struct
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

        /// <summary>
        /// Gets the value from the dictionary if it exists and returns true, otherwise adds it and initializes it with the provided value and returns false.
        /// This is similar to <see cref="CollectionExtensions.GetValueOrDefault{TKey, TValue}(IReadOnlyDictionary{TKey, TValue}, TKey, TValue)"/>
        /// but with an out variable for the value, making it more practical as part of a conditional expression.
        ///
        /// NOTE: When <paramref name="value"/> is a reference type, its reference will be stored instead of a vopy of a value.
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="dictionary"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="initializeAs">What to initialize the <paramref name="value"/> with if it is missing. NOTE: if this is a reference value, its reference will be stored instead of a vopy of a value.</param>
        /// <param name="addToDictionaryIfMissing">(Optional, defaulting to false) if or if not the key should be added to the dictionary with the initialized value if it was missing.</param>
        /// <returns>If the key existed in the dictionary beforehand or not</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool GetOrInitializeValue<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, out TValue value, Func<TValue> initializeAs, bool addToDictionaryIfMissing = false)
            where TValue : class
        {
            if (!dictionary.TryGetValue(key, out value))
            {
                value = initializeAs();
                if (addToDictionaryIfMissing)
                    dictionary.Add(key, value);
                return false;
            }
            return true;
        }

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

        public static TElement First<TElement>(this IEnumerable<TElement> collection)
        {
            var enumerator = collection.GetEnumerator();
            if (enumerator.MoveNext())
            {
                return enumerator.Current;
            }
            throw new InvalidOperationException("The enumeration is empty");
        }

        public static TElement FirstOrDefault<TElement>(this IEnumerable<TElement> collection)
        {
            var enumerator = collection.GetEnumerator();
            if (enumerator.MoveNext())
            {
                return enumerator.Current;
            }
            return default;
        }
    }
}

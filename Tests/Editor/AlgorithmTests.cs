using NUnit.Framework;
using System;
using System.Collections;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Containers.Unsafe;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.MemoryProfiler.Editor.Tests
{
    [TestFixture]
    internal class NativeArrayAlgorithmsTests
    {
        NativeArray<int> nativeArray;

        int[] kDefaultArray = new int[16] 
        {
            102,
             1,
            -11,
            4,
            2,
            102,
            5,
            -16,
            -2,
            3,
            -10,
            0,
            6,
            90,
            -1,
            -32
        };

        int[] kDefaultArraySorted = new int[16]
        {
            -32,
            -16,
            -11,
            -10,
            -2,
            -1,
            0,
            1,
            2,
            3,
            4,
            5,
            6,
            90,
            102,
            102
        };

        [TearDown]
        public void TearDown()
        {
            if(nativeArray.IsCreated)
                nativeArray.Dispose();
        }

        [Test]
        public void IntrospectionSort_SortsNativeArray()
        {
            nativeArray = new NativeArray<int>(kDefaultArray, Allocator.Temp);

            NativeArrayAlgorithms.IntrospectiveSort(nativeArray, 0, nativeArray.Length);
            for(int i = 0; i < nativeArray.Length; ++i)
            {
                Assert.AreEqual(kDefaultArraySorted[i], nativeArray[i]);
            }
        }

        [Test]
        public void IntrospectionSort_SortsNativeArrayWith2MillionEntries()
        {
            const int itemCount = 2000000;
            nativeArray = new NativeArray<int>(itemCount, Allocator.Persistent);
            var rnd = new System.Random(0);

            for (int i = 0; i < nativeArray.Length; ++i)
            {
                nativeArray[i] = rnd.Next(0, itemCount);
            }

            var array = nativeArray.ToArray();

            float x = Time.realtimeSinceStartup;
            Array.Sort(array);
            Debug.Log("Managed: " + (UnityEngine.Time.realtimeSinceStartup - x));


            x = UnityEngine.Time.realtimeSinceStartup;
            NativeArrayAlgorithms.IntrospectiveSort(nativeArray, 0, nativeArray.Length);
            UnityEngine.Debug.Log("Native: " + (UnityEngine.Time.realtimeSinceStartup - x));

            for (int i = 0; i < nativeArray.Length; ++i)
            {
                Assert.AreEqual(array[i], nativeArray[i]);
            }
        }

        [UnityTest]
        public IEnumerator BinarySearch_WithSmallSizeArray_ReturnsIndexOfExisting([Values(new int[1] { 0 }, new int[2] { 0, 1 })] int[] values)
        {
            nativeArray = new NativeArray<int>(values, Allocator.Temp);

            for (int i = 0; i < values.Length; ++i)
            {
                int index = NativeArrayAlgorithms.BinarySearch(nativeArray, values[i]);
                Assert.AreEqual(values[i], nativeArray[index]);
            }
            yield return null;
        }

        [Test]
        public void BinarySearch_WithEvenLengthArray_ReturnsIndexOfExisting()
        {
            nativeArray = new NativeArray<int>(kDefaultArraySorted, Allocator.Temp);

            for(int i = 0; i < kDefaultArraySorted.Length; ++i)
            {
                int index = NativeArrayAlgorithms.BinarySearch(nativeArray, kDefaultArraySorted[i]);
                Assert.AreEqual(kDefaultArraySorted[i], nativeArray[index]);
            }
        }

        [Test]
        public void BinarySearch_WithOddLengthArray_ReturnsIndexOfExisting()
        {
            nativeArray = new NativeArray<int>(kDefaultArraySorted.Length - 1, Allocator.Temp);
            unsafe
            {
                fixed(int* src = kDefaultArraySorted)
                {
                    UnsafeUtility.MemCpy(nativeArray.GetUnsafePtr(), src, sizeof(int) * nativeArray.Length);

                }
            }

            for (int i = 0; i < nativeArray.Length; ++i)
            {
                int index = NativeArrayAlgorithms.BinarySearch(nativeArray, kDefaultArraySorted[i]);
                Assert.AreEqual(kDefaultArraySorted[i], nativeArray[index]);
            }
        }

        [Test]
        public void BinarySearch_ReturnsInvalidIndexForNonExistentValue()
        {
            const int overNineK = 9001;
            nativeArray = new NativeArray<int>(kDefaultArraySorted, Allocator.Temp);
            int index = NativeArrayAlgorithms.BinarySearch(nativeArray, overNineK);
            Assert.AreEqual(-1, index);
        }

        [Test]
        public void BinarySearch_ReturnsInvalidIndexForEmptyArray()
        {
            nativeArray = new NativeArray<int>(new int[0], Allocator.Temp);
            int index = NativeArrayAlgorithms.BinarySearch(nativeArray, 0);
            Assert.AreEqual(-1, index);
        }

        [Test]
        public void BinarySearch_ReturnsIndexToExistingItemFrom2MLongArray()
        {
            const int searchValueIndex = 65535;
            const int itemCount = 2000000;
            nativeArray = new NativeArray<int>(itemCount, Allocator.Persistent);
            var rnd = new System.Random(0);

            for (int i = 0; i < nativeArray.Length; ++i)
            {
                nativeArray[i] = rnd.Next(0, itemCount);
            }
            NativeArrayAlgorithms.IntrospectiveSort(nativeArray, 0, nativeArray.Length);

            var managedArr = nativeArray.ToArray();

            float x = Time.realtimeSinceStartup;
            Array.BinarySearch(managedArr, managedArr[searchValueIndex]);
            Debug.Log("Managed Binary Search: " + (Time.realtimeSinceStartup - x));

            x = Time.realtimeSinceStartup;
            NativeArrayAlgorithms.BinarySearch(nativeArray, nativeArray[searchValueIndex]);
            Debug.Log("Native Binary Search: " + (Time.realtimeSinceStartup - x));
        }

        [UnityTest]
        public IEnumerator IntrospectionSort_withMultipleSeeds_SortsNativeArrayWith500ThousandEntries([Values(-1,0,1)] int seed)
        {
            const int itemCount = 500000;
            nativeArray = new NativeArray<int>(itemCount, Allocator.Persistent);
            var rnd = new System.Random(seed);

            for (int i = 0; i < nativeArray.Length; ++i)
            {
                nativeArray[i] = rnd.Next(0, itemCount);
            }
            var array = nativeArray.ToArray();

            Array.Sort(array);
            NativeArrayAlgorithms.IntrospectiveSort(nativeArray, 0, nativeArray.Length);

            for (int i = 0; i < nativeArray.Length; ++i)
            {
                Assert.AreEqual(array[i], nativeArray[i]);
            }
            yield return null;
        }
    }
}
using System;
using UnityEngine;

using UnityEngine.Scripting;

#if UNITY_EDITOR
using UnityEditor;
#endif

#if MEMORY_PROFILER_API_PUBLIC
using UnityMemoryProfiler = Unity.Profiling.Memory.MemoryProfiler;
using UnityMetaData = Unity.Profiling.Memory.MemorySnapshotMetadata;
#else
using UnityEngine.Profiling.Memory.Experimental;
using UnityMemoryProfiler = UnityEngine.Profiling.Memory.Experimental.MemoryProfiler;
using UnityMetaData = UnityEngine.Profiling.Memory.Experimental.MetaData;
#endif

[assembly: Preserve]
namespace Unity.MemoryProfiler
{
#if !MEMPROFILER_DISABLE_METADATA_INJECTOR
    internal static class MetadataInjector
    {
        public static DefaultMetadataCollect DefaultCollector;
        public static long CollectorCount = 0;
        public static byte DefaultCollectorInjected = 0;
#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        static void EditorInitMetadata()
        {
            InitializeMetadataCollection();
        }

#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        static void PlayerInitMetadata()
        {
#if !UNITY_EDITOR
            InitializeMetadataCollection();
#endif
        }

        static void InitializeMetadataCollection()
        {
            DefaultCollector = new DefaultMetadataCollect();
        }
    }
#endif

    /// <summary>
    /// Abstract class for creating a metadata collector type to populate the `PackedMemorySnapshot.Metadata` member. You can add multiple collectors, but it is recommended to add only one. A collector instance will auto-register during construction.
    /// </summary>
    /// <remarks> Creating a collector instance will override the default metadata collection functionality. If you want to keep the default metadata, go to the `DefaultCollect` method in the file _com.unity.memoryprofiler\Runtime\MetadataInjector.cs_ and copy that code into your collector method.
    /// Removing a collector can be achieved by calling dispose on the collector instance you want to unregister.
    /// </remarks>
    public abstract class MetadataCollect : IDisposable
    {
        bool disposed = false;

        /// <summary>
        /// Default constructor of the `MetadataCollect`.
        /// </summary>
        /// <example>
        /// <code>
        /// public class MyMetadataCollect : MetadataCollect
        /// {
        ///     public MyMetadataCollect() : base()
        ///     {
        ///     }
        ///
        ///     public CollectMetadata(MetaData data)
        ///     {
        ///         // Metadata which is added by default.
        ///         data.content = $"Project name: { Application.productName }";
        ///         data.platform = string.Empty;
        ///     }
        /// }
        /// </code>
        /// </example>
        protected MetadataCollect()
        {
            if (MetadataInjector.DefaultCollector != null
                && MetadataInjector.DefaultCollector != this
                && MetadataInjector.DefaultCollectorInjected != 0)
            {
#if MEMORY_PROFILER_API_PUBLIC
                UnityMemoryProfiler.CreatingMetadata -= MetadataInjector.DefaultCollector.CollectMetadata;
#else
                UnityMemoryProfiler.createMetaData -= MetadataInjector.DefaultCollector.CollectMetadata;
#endif
                --MetadataInjector.CollectorCount;
                MetadataInjector.DefaultCollectorInjected = 0;
            }
#if MEMORY_PROFILER_API_PUBLIC
            UnityMemoryProfiler.CreatingMetadata += CollectMetadata;
#else
            UnityMemoryProfiler.createMetaData += CollectMetadata;
#endif
            ++MetadataInjector.CollectorCount;
        }

        /// <summary>
        /// The Memory Profiler will invoke this method during the capture process, to populate the metadata of the capture.
        /// </summary>
        /// <param name="data"> The data payload that will get written to the snapshot file. </param>
        public abstract void CollectMetadata(UnityMetaData data);

        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
#if MEMORY_PROFILER_API_PUBLIC
                UnityMemoryProfiler.CreatingMetadata -= CollectMetadata;
#else
                UnityMemoryProfiler.createMetaData -= CollectMetadata;
#endif
                --MetadataInjector.CollectorCount;
                if (MetadataInjector.DefaultCollector != null
                    && MetadataInjector.CollectorCount < 1
                    && MetadataInjector.DefaultCollector != this)
                {
                    MetadataInjector.DefaultCollectorInjected = 1;
#if MEMORY_PROFILER_API_PUBLIC
                    UnityMemoryProfiler.CreatingMetadata += MetadataInjector.DefaultCollector.CollectMetadata;
#else
                    UnityMemoryProfiler.createMetaData += MetadataInjector.DefaultCollector.CollectMetadata;
#endif
                    ++MetadataInjector.CollectorCount;
                }
            }
        }
    }

    internal class DefaultMetadataCollect : MetadataCollect
    {
        public DefaultMetadataCollect() : base()
        {
            MetadataInjector.DefaultCollectorInjected = 1;
        }

        public override void CollectMetadata(UnityMetaData data)
        {
#if MEMORY_PROFILER_API_PUBLIC
            data.Description = $"Project name: { Application.productName }";
#else
            data.content = $"Project name: { Application.productName }";
            data.platform = string.Empty;
#endif
        }
    }
}

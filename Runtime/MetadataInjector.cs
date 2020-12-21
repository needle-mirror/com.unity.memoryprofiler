using System;
using UnityEngine;
using UnityEngine.Profiling.Memory.Experimental;

#if UNITY_EDITOR
using UnityEditor;
#endif

using UnityMemoryProfiler = UnityEngine.Profiling.Memory.Experimental.MemoryProfiler;

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
#if UNITY_2019_1_OR_NEWER
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
#else
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
#endif
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
        public MetadataCollect()
        {
            if (MetadataInjector.DefaultCollector != null
                && MetadataInjector.DefaultCollector != this
                && MetadataInjector.DefaultCollectorInjected != 0)
            {
                UnityMemoryProfiler.createMetaData -= MetadataInjector.DefaultCollector.CollectMetadata;
                --MetadataInjector.CollectorCount;
                MetadataInjector.DefaultCollectorInjected = 0;
            }
            UnityMemoryProfiler.createMetaData += CollectMetadata;
            ++MetadataInjector.CollectorCount;
        }

        /// <summary>
        /// The Memory Profiler will invoke this method during the capture process, to populate the metadata of the capture.
        /// </summary>
        /// <param name="data"> The data payload that will get written to the snapshot file. </param>
        public abstract void CollectMetadata(MetaData data);

        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                UnityMemoryProfiler.createMetaData -= CollectMetadata;
                --MetadataInjector.CollectorCount;
                if (MetadataInjector.DefaultCollector != null
                    && MetadataInjector.CollectorCount < 1
                    && MetadataInjector.DefaultCollector != this)
                {
                    MetadataInjector.DefaultCollectorInjected = 1;
                    UnityMemoryProfiler.createMetaData += MetadataInjector.DefaultCollector.CollectMetadata;
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

        public override void CollectMetadata(MetaData data)
        {
            data.content = "Project name: " + Application.productName;
#if UNITY_EDITOR && !UNITY_2019_3_OR_NEWER
            data.content += "\nScripting Version: " + EditorApplication.scriptingRuntimeVersion.ToString();
#endif
            data.platform = Application.platform.ToString();
        }
    }
}

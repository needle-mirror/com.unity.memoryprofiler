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

[assembly: Preserve, AlwaysLinkAssembly]
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
            if (!Application.isEditor)
            {
                // initialize here to apeas Project Auditor
                DefaultCollector?.Dispose();
                DefaultCollector = null;
                DefaultCollectorInjected = 0;
                CollectorCount = 0;
            }
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
    /// <example>
    /// <code lang="cs"><![CDATA[
    /// using Unity.MemoryProfiler;
    /// using Unity.Profiling.Memory;
    /// using UnityEngine.Scripting;
    /// using UnityEngine;
    ///
    /// public class SnapshotMetadataProvider : MonoBehaviour
    /// {
    ///     public string levelName = "Default Level Name";
    ///     MyMetadataCollect m_MetadataCollector;
    ///
    ///     void Start()
    ///     {
    ///         m_MetadataCollector = new MyMetadataCollect(this);
    ///     }
    ///
    ///     void OnDestroy()
    ///     {
    ///         // Remember to dispose of the collector, so it won't leak.
    ///         m_MetadataCollector.Dispose();
    ///     }
    /// }
    ///
    /// public class MyMetadataCollect : MetadataCollect
    /// {
    ///     SnapshotMetadataProvider m_MetadataProvider;
    ///
    ///     // Make sure to call the base constructor, which will handle the subscription to the MemoryProfiler.CreatingMetadata event.
    ///     public MyMetadataCollect(SnapshotMetadataProvider metadataProvider) : base()
    ///     {
    ///         m_MetadataProvider = metadataProvider;
    ///     }
    ///
    ///     public CollectMetadata(MemorySnapshotMetadata data)
    ///     {
    ///         // This is what the metadata default implementation of the package sets as the destcription.
    ///         // data.Description = $"Project name: { Application.productName }";
    ///         // Implementing one or more MetadataCollect types, will cause the default implementation to no longer execute.
    ///         // If you want to retain that default description, you can do so by mirroring the code in your implementation.
    ///         // The product name is however already part of the general memory snapshot metadata so you won't lose that detail if you don't.
    ///
    ///         // Note that if there are multiple MetadataCollect instances, each one will be called in the order in which they were created.
    ///         // To avoid overwriting what previous instance added to the Description, only append.
    ///         // Description is initialized as empty string so there is no need to check against null (unless one of your implementation sets it to null).
    ///         data.Description += $"Captured in Level: {m_MetadataProvider.levelName}\n";
    ///     }
    /// }
    /// ]]></code>
    /// </example>
    /// <seealso cref="Unity.Profiling.Memory.MemoryProfiler.CreatingMetadata"/>
    /// <seealso cref="Unity.Profiling.Memory.MemorySnapshotMetadata"/>
    public abstract class MetadataCollect : IDisposable
    {
        bool disposed = false;

        /// <summary>
        /// Default constructor of the `MetadataCollect`.
        /// </summary>
        /// <remarks>
        /// When implementing your own version, remember to call the base constructor to ensure your instance is registered with <see cref="Unity.Profiling.Memory.MemoryProfiler.CreatingMetadata"/>.
        /// </remarks>
        /// <example>
        /// <para>You can create one or more instances of a MetadataCollector to be created by e.g. a MonoBehaviour in a scene, like this:</para>
        /// <code lang="cs"><![CDATA[
        /// using Unity.MemoryProfiler;
        /// using Unity.Profiling.Memory;
        /// using UnityEngine.Scripting;
        /// using UnityEngine;
        ///
        /// public class SnapshotMetadataProvider : MonoBehaviour
        /// {
        ///     public string levelName = "Default Level Name";
        ///     MyMetadataCollect m_MetadataCollector;
        ///
        ///     void Start()
        ///     {
        ///         m_MetadataCollector = new MyMetadataCollect(this);
        ///     }
        ///
        ///     void OnDestroy()
        ///     {
        ///         // Remember to dispose of the collector, so it won't leak.
        ///         m_MetadataCollector.Dispose();
        ///     }
        /// }
        ///
        /// public class MyMetadataCollect : MetadataCollect
        /// {
        ///     SnapshotMetadataProvider m_MetadataProvider;
        ///
        ///     // Make sure to call the base constructor, which will handle the subscription to the MemoryProfiler.CreatingMetadata event.
        ///     public MyMetadataCollect(SnapshotMetadataProvider metadataProvider) : base()
        ///     {
        ///         m_MetadataProvider = metadataProvider;
        ///     }
        ///
        ///     public CollectMetadata(MemorySnapshotMetadata data)
        ///     {
        ///         // This is what the metadata default implementation of the package sets as the destcription.
        ///         // data.Description = $"Project name: { Application.productName }";
        ///         // Implementing one or more MetadataCollect types, will cause the default implementation to no longer execute.
        ///         // If you want to retain that default description, you can do so by mirroring the code in your implementation.
        ///         // The product name is however already part of the general memory snapshot metadata so you won't lose that detail if you don't.
        ///
        ///         // Note that if there are multiple MetadataCollect instances, each one will be called in the order in which they were created.
        ///         // To avoid overwriting what previous instance added to the Description, only append.
        ///         // Description is initialized as empty string so there is no need to check against null (unless one of your implementation sets it to null).
        ///         data.Description += $"Captured in Level: {m_MetadataProvider.levelName}\n";
        ///     }
        /// }
        /// ]]></code>
        /// <para>You can also inject a collector with InitializeOnLoadMethod or RuntimeInitializeOnLoadMethod to easily and globally add it to the project.</para>
        /// <code lang="cs"><![CDATA[
        /// using Unity.MemoryProfiler;
        /// using Unity.Profiling.Memory;
        /// using UnityEngine.Scripting;
        /// using UnityEngine;
        /// #if UNITY_EDITOR
        /// using UnityEditor;
        /// #endif
        ///
        /// public class MySingletonMetadataCollect : MetadataCollect
        /// {
        ///     static MySingletonMetadataCollect s_Instance;
        ///     #if UNITY_EDITOR
        ///     [InitializeOnLoadMethod]
        ///     static void EditorInitMetadata()
        ///     {
        ///         InitializeMetadataCollection();
        ///     }
        ///     #else
        ///     [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        ///     static void EditorInitMetadata()
        ///     {
        ///         InitializeMetadataCollection();
        ///     }
        ///     #endif
        ///
        ///     static void InitializeMetadataCollection()
        ///     {
        ///         // Dispose any potential previous instance.
        ///         s_Instance?.Dispose();
        ///         s_Instance = new MySingletonMetadataCollect();
        ///     }
        ///
        ///     // Make sure to call the base constructor, which will handle the subscription to the MemoryProfiler.CreatingMetadata event.
        ///     public MySingletonMetadataCollect() : base()
        ///     {
        ///     }
        ///
        ///     public CollectMetadata(MemorySnapshotMetadata data)
        ///     {
        ///         // This is what the metadata default implementation of the package sets as the destcription.
        ///         // data.Description = $"Project name: { Application.productName }";
        ///         // Implementing one or more MetadataCollect types, will cause the default implementation to no longer execute.
        ///         // If you want to retain that default description, you can do so by mirroring the code in your implementation.
        ///         // The product name is however already part of the general memory snapshot metadata so you won't lose that detail if you don't.
        ///
        ///         // Note that if there are multiple MetadataCollect instances, each one will be called in the order in which they were created.
        ///         // To avoid overwriting what previous instance added to the Description, only append.
        ///         // Description is initialized as empty string so there is no need to check against null (unless one of your implementation sets it to null).
        ///         data.Description += $"This is meta added by {nameof(MySingletonMetadataCollect)}\n";
        ///     }
        /// }
        /// ]]></code>
        /// </example>
        /// <seealso cref="Unity.Profiling.Memory.MemoryProfiler.CreatingMetadata"/>
        /// <seealso cref="Unity.Profiling.Memory.MemorySnapshotMetadata"/>
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
            // Suppress Projec Auditor warning. This is handled via MetadataInjector for the DefaultMetadataCollect
            // And user implementations need to solve this by Disposing
#pragma warning disable UDR0005 // Domain Reload Analyzer
            UnityMemoryProfiler.CreatingMetadata += CollectMetadata;
#pragma warning restore UDR0005 // Domain Reload Analyzer
#else
            UnityMemoryProfiler.createMetaData += CollectMetadata;
#endif
            ++MetadataInjector.CollectorCount;
        }

        /// <summary>
        /// The Memory Profiler will invoke this method during the capture process, to populate the metadata of the capture.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b> If there are multiple MetadataCollect instances, each one will be called in the order in which they were created.
        /// To avoid overwriting what previous instance added to <c>data.Description</c>, only append.
        /// Description is initialized as empty string so there is no need to check against null (unless one of your implementation sets it to null).
        /// </remarks>
        /// <param name="data">The data payload that will get written to the snapshot file. It is initialized as an empty string but may have been populated by previous metadata collectors.</param>
        /// <seealso cref="Unity.Profiling.Memory.MemorySnapshotMetadata"/>
        public abstract void CollectMetadata(UnityMetaData data);

        /// <summary>
        /// CollectMetadata implements an <see cref="IDisposable"/> pattern and therefore an Dispose method.
        /// </summary>
        /// <remarks>
        /// Disposing of a metadata collector unregisters it from <see cref="Unity.Profiling.Memory.MemoryProfiler.CreatingMetadata"/>.
        /// When overriding the Dispose method to dispose of any resources your implementation holds onto, remember to call <c>base.Dispose();</c> to avoid leaks.
        /// </remarks>
        /// <seealso cref="IDisposable"/>
        /// <seealso cref="Unity.Profiling.Memory.MemoryProfiler.CreatingMetadata"/>
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
            // Suppress Projec Auditor warning. This is handled via MetadataInjector
#pragma warning disable UDR0005 // Domain Reload Analyzer
                    UnityMemoryProfiler.CreatingMetadata += MetadataInjector.DefaultCollector.CollectMetadata;
#pragma warning restore UDR0005 // Domain Reload Analyzer
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

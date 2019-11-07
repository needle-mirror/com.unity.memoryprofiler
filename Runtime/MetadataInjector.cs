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
#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        static void EditorInitMetadata()
        {
            InitializeMetadataCollection();
        }

#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void PlayerInitMetadata()
        {
#if !UNITY_EDITOR
            InitializeMetadataCollection();
#endif
        }

        static void InitializeMetadataCollection()
        {
            var foundTypes = ReflectionUtility.GetTypesImplementingInterfaceFromCurrentDomain(typeof(IMetadataCollect));
            if (foundTypes.Count > 0)
            {
                for (int i = 0; i < foundTypes.Count; ++i)
                {
                    var metaCollector = Activator.CreateInstance(foundTypes[i]) as IMetadataCollect;
                    UnityMemoryProfiler.createMetaData += metaCollector.CollectMetadata;
                }
            }
            else
            {
                UnityMemoryProfiler.createMetaData += DefaultCollect;
            }
        }

        static void DefaultCollect(MetaData data)
        {
            data.content = "Project name: " + Application.productName;
#if UNITY_EDITOR && !UNITY_2019_3_OR_NEWER
            data.content += "\nScripting Version: " + EditorApplication.scriptingRuntimeVersion.ToString();
#endif
            data.platform = Application.platform.ToString();
        }
    }


#endif
    /// <summary>
    /// Interface for creating a metadata collector type to populate the `PackedMemorySnapshot.Metadata` member. You can add multiple collectors, but it is recommended to add only one.
    /// </summary>
    /// <remarks> Adding a collector will override the default metadata collection functionality. If you want to keep the default metadata, go to the `DefaultCollect` method in the file _com.unity.memoryprofiler\Runtime\MetadataInjector.cs_ and copy that code into your collector method.
    /// </remarks>
    public interface IMetadataCollect
    {
        /// <summary>
        /// The Memory Profiler will invoke this method during the capture process, to populate the metadata of the capture.
        /// </summary>
        /// <param name="data"> The data payload that will get written to the snapshot file. </param>
        void CollectMetadata(MetaData data);
    }
}

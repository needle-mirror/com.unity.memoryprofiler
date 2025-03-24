using System;
using Unity.Profiling.Memory;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor
{
    internal class SnapshotFileModel : IEquatable<SnapshotFileModel>
    {
        public SnapshotFileModel(
            string name,
            string fullPath,
            string productName,
            string metaDataDescription,
            uint sessionId,
            DateTime timestamp,
            RuntimePlatform platform,
            bool editorPlatform,
            string unityVersion,
            bool memoryInformationAvailable,
            ulong totalAllocated,
            ulong totalResident,
            ulong maxAvailable,
            CaptureFlags captureFlags,
            string scriptingImplementation)
        {
            Name = name;
            FullPath = fullPath;
            ProductName = productName;
            MetadataDescription = metaDataDescription;
            SessionId = sessionId;
            Timestamp = timestamp;
            Platform = platform;
            EditorPlatform = editorPlatform;
            UnityVersion = unityVersion;

            MemoryInformationAvailable = memoryInformationAvailable;
            TotalAllocatedMemory = totalAllocated;
            TotalResidentMemory = totalResident;
            MaxAvailableMemory = maxAvailable;
            CaptureFlags = captureFlags;
            ScriptingImplementation = scriptingImplementation;
        }

        public string Name { get; }
        public string FullPath { get; }
        public string ProductName { get; }
        public string MetadataDescription { get; }
        public uint SessionId { get; }
        public DateTime Timestamp { get; }
        public RuntimePlatform Platform { get; }
        public bool EditorPlatform { get; }
        public string UnityVersion { get; }

        public bool MemoryInformationAvailable { get; }
        public ulong TotalAllocatedMemory { get; }
        public ulong TotalResidentMemory { get; }
        public ulong MaxAvailableMemory { get; }
        public CaptureFlags CaptureFlags { get; }
        public string ScriptingImplementation { get; }

        public bool Equals(SnapshotFileModel other)
        {
            return other != null &&
                Name == other.Name &&
                FullPath == other.FullPath &&
                ProductName == other.ProductName &&
                MetadataDescription == other.MetadataDescription &&
                SessionId == other.SessionId &&
                Timestamp == other.Timestamp &&
                Platform == other.Platform &&
                EditorPlatform == other.EditorPlatform &&
                UnityVersion == other.UnityVersion &&
                MemoryInformationAvailable == other.MemoryInformationAvailable &&
                TotalAllocatedMemory == other.TotalAllocatedMemory &&
                TotalResidentMemory == other.TotalResidentMemory &&
                MaxAvailableMemory == other.MaxAvailableMemory &&
                CaptureFlags == other.CaptureFlags &&
                ScriptingImplementation == other.ScriptingImplementation;
        }
    }
}

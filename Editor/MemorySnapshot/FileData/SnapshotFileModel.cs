using System;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor
{
    internal class SnapshotFileModel : IEquatable<SnapshotFileModel>
    {
        public SnapshotFileModel(
            string name,
            string fullPath,
            string productName,
            uint sessionId,
            DateTime timestamp,
            RuntimePlatform platform,
            bool editorPlatform,
            string unityVersion,
            bool memoryInformationAvailable,
            ulong totalAllocated,
            ulong totalResident,
            ulong maxAvailable)
        {
            Name = name;
            FullPath = fullPath;
            ProductName = productName;
            SessionId = sessionId;
            Timestamp = timestamp;
            Platform = platform;
            EditorPlatform = editorPlatform;
            UnityVersion = unityVersion;

            MemoryInformationAvailable = memoryInformationAvailable;
            TotalAllocatedMemory = totalAllocated;
            TotalResidentMemory = totalResident;
            MaxAvailableMemory = maxAvailable;
        }

        public string Name { get; }
        public string FullPath { get; }
        public string ProductName { get; }
        public uint SessionId { get; }
        public DateTime Timestamp { get; }
        public RuntimePlatform Platform { get; }
        public bool EditorPlatform { get; }
        public string UnityVersion { get; }

        public bool MemoryInformationAvailable { get; }
        public ulong TotalAllocatedMemory { get; }
        public ulong TotalResidentMemory { get; }
        public ulong MaxAvailableMemory { get; }

        public bool Equals(SnapshotFileModel other)
        {
            return other != null &&
                Name == other.Name &&
                FullPath == other.FullPath &&
                ProductName == other.ProductName &&
                SessionId == other.SessionId &&
                Timestamp == other.Timestamp &&
                Platform == other.Platform &&
                EditorPlatform == other.EditorPlatform &&
                UnityVersion == other.UnityVersion &&
                MemoryInformationAvailable == other.MemoryInformationAvailable &&
                TotalAllocatedMemory == other.TotalAllocatedMemory &&
                TotalResidentMemory == other.TotalResidentMemory &&
                MaxAvailableMemory == other.MaxAvailableMemory;
        }
    }
}

using System;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor
{
    internal class SnapshotFileModel
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
            ulong maxAvailable,
            Texture screenshot)
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

            Screenshot = screenshot;
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

        public Texture Screenshot { get; }
    }
}

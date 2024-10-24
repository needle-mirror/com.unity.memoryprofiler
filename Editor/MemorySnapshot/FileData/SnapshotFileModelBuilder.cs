using System;
using System.IO;
using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.MemoryProfiler.Editor.Format;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;

namespace Unity.MemoryProfiler.Editor
{
    internal class SnapshotFileModelBuilder
    {
        string m_FileName;

        public SnapshotFileModelBuilder(string fileName)
        {
            m_FileName = fileName;
        }

        public SnapshotFileModel Build()
        {
            using var reader = new FileReader();

            ReadError error = reader.Open(m_FileName);
            if (error != ReadError.Success)
                return null;

            MetaData snapshotMetadata = new MetaData(reader);

            var totalResident = 0UL;
            var totalCommitted = 0UL;
            DateTime timestamp = DateTime.Now;
            unsafe
            {
                long ticks;
                reader.ReadUnsafe(EntryType.Metadata_RecordDate, &ticks, sizeof(long), 0, 1);
                timestamp = new DateTime(ticks);

                var count = reader.GetEntryCount(EntryType.SystemMemoryRegions_Address);
                using var regionSize = reader.Read(EntryType.SystemMemoryRegions_Size, 0, count, Allocator.TempJob).Result.Reinterpret<ulong>();
                using var regionResident = reader.Read(EntryType.SystemMemoryRegions_Resident, 0, count, Allocator.TempJob).Result.Reinterpret<ulong>();
                for (int i = 0; i < count; i++)
                {
                    totalResident += regionResident[i];
                    totalCommitted += regionSize[i];
                }
            }

            var maxAvailable = 0UL;
            if (snapshotMetadata.TargetInfo.HasValue)
                maxAvailable = snapshotMetadata.TargetInfo.Value.TotalPhysicalMemory;

            bool editorPlatform = snapshotMetadata.IsEditorCapture;
            var runtimePlatform = PlatformsHelper.GetRuntimePlatform(snapshotMetadata.Platform);

            return new SnapshotFileModel(
                Path.GetFileNameWithoutExtension(m_FileName),
                m_FileName,
                snapshotMetadata.ProductName,
                snapshotMetadata.SessionGUID,
                timestamp,
                runtimePlatform,
                editorPlatform,
                snapshotMetadata.UnityVersion,
                snapshotMetadata.TargetInfo.HasValue,
                totalCommitted,
                totalResident,
                maxAvailable);
        }
    }
}

namespace Unity.MemoryProfiler.Editor.Format.QueriedSnapshot
{
    enum EntryType : ushort
    {
        Metadata_Version = 0,
        Metadata_RecordDate,
        Metadata_UserMetadata,
        Metadata_CaptureFlags,
        Metadata_VirtualMachineInformation,
        NativeTypes_Name,
        NativeTypes_NativeBaseTypeArrayIndex,
        NativeObjects_NativeTypeArrayIndex,
        NativeObjects_HideFlags,
        NativeObjects_Flags,
        NativeObjects_InstanceId,
        NativeObjects_Name,
        NativeObjects_NativeObjectAddress,
        NativeObjects_Size,
        NativeObjects_RootReferenceId,
        GCHandles_Target,
        Connections_From,
        Connections_To,
        ManagedHeapSections_StartAddress,
        ManagedHeapSections_Bytes,
        ManagedStacks_StartAddress,
        ManagedStacks_Bytes,
        TypeDescriptions_Flags,
        TypeDescriptions_Name,
        TypeDescriptions_Assembly,
        TypeDescriptions_FieldIndices,
        // Retired for format v20+: no producer populates this from v20 on (see the native
        // MemorySnapshotFileEntryNames.h entry for why Mono/IL2CPP and CoreCLR differ historically).
        // Must stay version-gated when read — see this directory's AGENTS.md for why an absent vs.
        // zero-count chapter differ.
        TypeDescriptions_StaticFieldBytes,
        TypeDescriptions_BaseOrElementTypeIndex,
        TypeDescriptions_Size,
        TypeDescriptions_TypeInfoAddress,
        TypeDescriptions_TypeIndex,
        FieldDescriptions_Offset,
        FieldDescriptions_TypeIndex,
        FieldDescriptions_Name,
        FieldDescriptions_IsStatic,
        NativeRootReferences_Id,
        NativeRootReferences_AreaName,
        NativeRootReferences_ObjectName,
        NativeRootReferences_AccumulatedSize,
        NativeAllocations_MemoryRegionIndex,
        NativeAllocations_RootReferenceId,
        NativeAllocations_AllocationSiteId,
        NativeAllocations_Address,
        NativeAllocations_Size,
        NativeAllocations_OverheadSize,
        NativeAllocations_PaddingSize,
        NativeMemoryRegions_Name,
        NativeMemoryRegions_ParentIndex,
        NativeMemoryRegions_AddressBase,
        NativeMemoryRegions_AddressSize,
        NativeMemoryRegions_FirstAllocationIndex,
        NativeMemoryRegions_NumAllocations,
        NativeMemoryLabels_Name,
        NativeAllocationSites_Id,
        NativeAllocationSites_MemoryLabelIndex,
        NativeAllocationSites_CallstackSymbols,
        NativeCallstackSymbol_Symbol,
        NativeCallstackSymbol_ReadableStackTrace,
        NativeObjects_GCHandleIndex,
        ProfileTarget_Info,
        ProfileTarget_MemoryStats,
        NativeMemoryLabels_Size,
        SceneObjects_Name,
        SceneObjects_Path,
        SceneObjects_AssetPath,
        SceneObjects_BuildIndex,
        SceneObjects_RootIdCounts,
        SceneObjects_RootIdOffsets,
        SceneObjects_RootIds,
        // GfxResourceReferencesAndAllocatorsVersion = 14
        // Added gfx resource to root mapping and allocators information (including extra allocator information to memory labels)
        NativeMemoryLabels_AllocatorIdentifier,
        NativeGfxResourceReferences_Id,
        NativeGfxResourceReferences_Size,
        NativeGfxResourceReferences_RootId,
        NativeAllocatorInfo_AllocatorName,
        NativeAllocatorInfo_Identifier,
        NativeAllocatorInfo_UsedSize,
        NativeAllocatorInfo_ReservedSize,
        NativeAllocatorInfo_OverheadSize,
        NativeAllocatorInfo_PeakUsedSize,
        NativeAllocatorInfo_AllocationCount,
        NativeAllocatorInfo_Flags,
        // NativeObjectMetaDataVersion = 15
        // Adds meta data
        ObjectMetaData_MetaDataBufferIndicies,
        ObjectMetaData_MetaDataBuffer,
        // SystemMemoryRegionsVersion = 16
        // Adds system memory regions
        SystemMemoryRegions_Address,
        SystemMemoryRegions_Size,
        SystemMemoryRegions_Resident,
        SystemMemoryRegions_Type,
        SystemMemoryRegions_Name,
        // SystemMemoryResidentPagesVersion = 17
        // Adds system memory resident pages
        SystemMemoryResidentPages_Address,
        SystemMemoryResidentPages_FirstPageIndex,
        SystemMemoryResidentPages_LastPageIndex,
        SystemMemoryResidentPages_PagesState,
        SystemMemoryResidentPages_PageSize,
        // ManagedConnectionsVersion = 19
        // Adds the backend-provided managed object graph (objects, references, roots), a per-field
        // element-type column, and a per-native-object GC handle.
        // Slot 5 (NativeObjects_InstanceId) is named NativeObjects_EntityId natively; the reader keys
        // by ordinal, not name, so the historical name is kept here.
        ManagedObjects_Target,
        ManagedObjects_TypeIndex,
        ManagedRoots_ManagedObjectIndex,
        ManagedRoots_RootTypeAndFlags,
        ManagedRoots_Data,
        ManagedReferences_FromIndex,
        ManagedReferences_ToIndex,
        FieldDescriptions_ElementType,
        // TODO: switch native<->managed shell linking to this raw GC handle column instead of
        // NativeObjects_GCHandleIndex. Not yet consumed anywhere.
        NativeObjects_GCHandle,
        // StaticFieldAndNonGCHeapVersion = 20
        // Adds resolved GC-static field slots (CoreCLR; ordinary statics on all backends), a
        // consolidated per-field attribute-flags word, and consolidated heap-section chunk-type/VM flags.
        TypeDescriptions_StaticFieldSlots,
        FieldDescriptions_Flags,
        ManagedHeapSections_Flags,
        Count, //used to keep track of entry count, only add c++ matching entries above this one
    }
}

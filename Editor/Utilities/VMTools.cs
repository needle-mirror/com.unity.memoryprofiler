using Unity.MemoryProfiler.Editor.Format;

namespace Unity.MemoryProfiler.Editor
{
    internal static class VMTools
    {
        //supported archs
        public const uint X64ArchPtrSize = 8;
        public const uint X86ArchPtrSize = 4;

        public static bool ValidateVirtualMachineInfo(VirtualMachineInformationFileData vmInfo)
        {
            if (!(vmInfo.PointerSize == X64ArchPtrSize || vmInfo.PointerSize == X86ArchPtrSize))
                return false;

            // Mono/IL2CPP object headers are 2x pointer size (vtable pointer + sync-block index);
            // CoreCLR headers are 1x pointer size (just the MethodTable* slot). Accept either layout.
            var ptrSize = vmInfo.PointerSize;
            var isMonoOrIl2CppLayout = vmInfo.ObjectHeaderSize == 2 * ptrSize && vmInfo.AllocationGranularity == 2 * ptrSize;
            var isCoreClrLayout = vmInfo.ObjectHeaderSize == ptrSize && vmInfo.AllocationGranularity == ptrSize;

            return isMonoOrIl2CppLayout || isCoreClrLayout;
        }
    }
}

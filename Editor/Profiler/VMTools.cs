using Unity.MemoryProfiler.Editor.Format;

namespace Unity.MemoryProfiler.Editor
{
    internal static class VMTools
    {
        //supported archs
        public const int X64ArchPtrSize = 8;
        public const int X86ArchPtrSize = 4;

        public static bool ValidateVirtualMachineInfo(VirtualMachineInformation vmInfo)
        {
            if (!(vmInfo.PointerSize == X64ArchPtrSize || vmInfo.PointerSize == X86ArchPtrSize))
                return false;

            //partial checks to validate computations based on pointer size
            int expectedObjHeaderSize = 2 * vmInfo.PointerSize;

            if (expectedObjHeaderSize != vmInfo.ObjectHeaderSize)
                return false;

            if (expectedObjHeaderSize != vmInfo.AllocationGranularity)
                return false;

            return true;
        }
    }
}

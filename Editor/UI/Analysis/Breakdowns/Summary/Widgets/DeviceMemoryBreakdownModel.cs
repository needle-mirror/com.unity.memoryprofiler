using System.Collections.Generic;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Device (physical) memory usage status and warning levels
    /// breakdown model.
    /// </summary>
    internal class DeviceMemoryBreakdownModel : MemoryBreakdownModel
    {
        public DeviceMemoryBreakdownModel(string title, bool compareMode, State stateA, State stateB, List<Row> rows)
            : base(title, compareMode, stateA.MaximumAvailable, stateB.MaximumAvailable, rows)
        {
            StateA = stateA;
            StateB = stateB;
        }

        public State StateA { get; }
        public State StateB { get; }

        public struct State
        {
            public ulong MaximumAvailable;
            public ulong CriticalLevel;
            public ulong WarningLevel;

            public ulong Resident;
        }
    }
}

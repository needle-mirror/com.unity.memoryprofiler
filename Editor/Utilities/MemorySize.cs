using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Diagnostics;

namespace Unity.MemoryProfiler.Editor
{
    readonly struct MemorySize : IEquatable<MemorySize>
    {
        public readonly ulong Committed;
        public readonly ulong Resident;

        public MemorySize(ulong committed, ulong resident)
        {
            Committed = committed;
            Resident = resident;
        }

        public static bool operator ==(in MemorySize l, in MemorySize r)
        {
            return l.Equals(r);
        }

        public static bool operator !=(in MemorySize l, in MemorySize r)
        {
            return !(l == r);
        }

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public static MemorySize operator +(in MemorySize l, in MemorySize r) => new MemorySize(l.Committed + r.Committed, l.Resident + r.Resident);

        [MethodImpl(MethodImplementationHelper.AggressiveInlining)]
        public static MemorySize operator -(in MemorySize l, in MemorySize r) => new MemorySize(l.Committed - r.Committed, l.Resident - r.Resident);

        public static MemorySize Min(in MemorySize l, in MemorySize r)
        {
            var committed = Math.Min(l.Committed, r.Committed);
            var resident = Math.Min(l.Resident, r.Resident);
            return new MemorySize(committed, resident);
        }

        public static MemorySize Max(in MemorySize l, in MemorySize r)
        {
            var committed = Math.Max(l.Committed, r.Committed);
            var resident = Math.Max(l.Resident, r.Resident);
            return new MemorySize(committed, resident);
        }

        public bool Equals(MemorySize other)
        {
            return (Committed == other.Committed) && (Resident == other.Resident);
        }

        public override bool Equals(object obj) => obj is MemorySize other && Equals(other);

        public override int GetHashCode() => (Committed, Resident).GetHashCode();

        public override string ToString()
        {
            return $"(Allocated:{Committed} Resident:{Resident})";
        }

        public MemorySize Divide(ulong divisor, out MemorySize remainder)
        {
            Checks.IsTrue(divisor != 0, "Division by 0!");
            remainder = new MemorySize(Committed > 0 ? Committed % divisor : 0, Resident > 0 ? Resident % divisor : 0);
            return new MemorySize(Committed > 0 ? Committed / divisor : 0, Resident > 0 ? Resident / divisor : 0);
        }
    }
}

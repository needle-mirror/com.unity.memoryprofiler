using System;

namespace Unity.MemoryProfiler.Editor
{
    struct MemorySize : IEquatable<MemorySize>
    {
        public readonly ulong Committed;
        public readonly ulong Resident;

        public MemorySize(ulong committed, ulong resident)
        {
            Committed = committed;
            Resident = resident;
        }

        public static bool operator ==(MemorySize l, MemorySize r)
        {
            return l.Equals(r);
        }

        public static bool operator !=(MemorySize l, MemorySize r)
        {
            return !(l == r);
        }

        public static MemorySize operator +(MemorySize l, MemorySize r) => new MemorySize(l.Committed + r.Committed, l.Resident + r.Resident);

        public static MemorySize operator -(MemorySize l, MemorySize r) => new MemorySize(l.Committed - r.Committed, l.Resident - r.Resident);

        public static MemorySize Min(MemorySize l, MemorySize r)
        {
            var committed = Math.Min(l.Committed, r.Committed);
            var resident = Math.Min(l.Resident, r.Resident);
            return new MemorySize(committed, resident);
        }

        public static MemorySize Max(MemorySize l, MemorySize r)
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
    }
}

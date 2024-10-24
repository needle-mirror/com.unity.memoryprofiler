using System;

namespace Unity.MemoryProfiler.Editor.Managed
{
    readonly struct FieldLayoutInfo : IComparable<FieldLayoutInfo>
    {
        public readonly long RemainingFieldCountForThisType;
        public readonly int IndexOfTheTypeOwningTheActualFieldIndex;
        public readonly int IndexOfTheTypeOfTheReferenceOwner;
        public readonly int OffsetFromPreviousAddress;
        public readonly int FieldIndexOnReferenceOwner;
        // Field Size is always PointerSize, so not part of this struct.
        // Can't be static as comparing snapshots from a 64 bit and a 32 bit platform could happen!
        // Check VM info instead.

        public readonly int ActualFieldIndexOnPotentialNestedValueType;
        public readonly int OffsetFromReferenceOwnerHeaderStartToFieldOnValueType;

        public FieldLayoutInfo(
            long remainingFieldCountForThisType,
            int indexOfTheTypeOfTheReferenceOwner,
            int indexOfTheTypeOwningTheActualFieldIndex,
            int offsetFromPreviousAddress,
            int fieldIndexOnReferenceOwner,
            int actualFieldIndexOnPotentialNestedValueType,
            int additionalFieldOffsetFromFieldOnReferenceOwnerToFieldOnValueType)
        {
            RemainingFieldCountForThisType = remainingFieldCountForThisType;
            IndexOfTheTypeOfTheReferenceOwner = indexOfTheTypeOfTheReferenceOwner;
            IndexOfTheTypeOwningTheActualFieldIndex = indexOfTheTypeOwningTheActualFieldIndex;
            OffsetFromPreviousAddress = offsetFromPreviousAddress;
            FieldIndexOnReferenceOwner = fieldIndexOnReferenceOwner;
            ActualFieldIndexOnPotentialNestedValueType = actualFieldIndexOnPotentialNestedValueType;
            OffsetFromReferenceOwnerHeaderStartToFieldOnValueType = additionalFieldOffsetFromFieldOnReferenceOwnerToFieldOnValueType;
        }

        public FieldLayoutInfo(FieldLayoutInfo copyFrom, int previousFieldsOffset, long remainingFieldCount) :
            this(
                remainingFieldCountForThisType: remainingFieldCount,
                indexOfTheTypeOfTheReferenceOwner: copyFrom.IndexOfTheTypeOfTheReferenceOwner,
                indexOfTheTypeOwningTheActualFieldIndex: copyFrom.IndexOfTheTypeOwningTheActualFieldIndex,
                offsetFromPreviousAddress: copyFrom.OffsetFromPreviousAddress - previousFieldsOffset,
                fieldIndexOnReferenceOwner: copyFrom.FieldIndexOnReferenceOwner,
                actualFieldIndexOnPotentialNestedValueType: copyFrom.ActualFieldIndexOnPotentialNestedValueType,
                additionalFieldOffsetFromFieldOnReferenceOwnerToFieldOnValueType: copyFrom.OffsetFromReferenceOwnerHeaderStartToFieldOnValueType
            )
        { }

        /// <summary>
        /// Only proper comparison while building the field layout,
        /// as <see cref="OffsetFromPreviousAddress"/> is the actual offset from the start of the object then.
        /// After sorting, the previous field's <see cref="OffsetFromPreviousAddress"/> is will be subtracted from this one's.
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        int IComparable<FieldLayoutInfo>.CompareTo(FieldLayoutInfo other)
        {
            return OffsetFromPreviousAddress.CompareTo(other.OffsetFromPreviousAddress);
        }
    }
}

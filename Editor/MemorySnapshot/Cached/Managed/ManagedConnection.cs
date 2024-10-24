using System;
using Unity.MemoryProfiler.Editor.Diagnostics;
using UnityEngine;
using static Unity.MemoryProfiler.Editor.CachedSnapshot;

namespace Unity.MemoryProfiler.Editor
{
    readonly struct ManagedConnection
    {
        public readonly SourceIndex IndexFrom;
        public readonly SourceIndex IndexTo;

        /// <summary>
        /// The index of a field on a ManagedObject or Managed Type that owns this reference.
        /// If that field is a value type field, the actual field holding the reference is nested in that value type.
        /// In those cases, <see cref="HeldViaValueTypeField"/> is true and <see cref="ValueTypeFieldFrom"/>,
        /// <see cref="ValueTypeFieldOwningITypeDescription"/> and <see cref="OffsetFromReferenceOwnerHeaderStartToFieldOnValueType"/> hold
        /// additional data to reoncstruct the entire chain of value types and field names
        /// that leads to the actual field (<see cref="ValueTypeFieldFrom"/>) that holds the reference to <see cref="IndexTo"/>.
        /// Check <see cref="ObjectData.GetFieldByFieldDescriptionsIndex(CachedSnapshot, int, bool, int, int, int)"/> for details on the reconstruction
        ///
        /// Make sure to check if an <see cref="ArrayIndexFrom"/> is set before checking if <see cref="FieldFrom"/> is set if you want to get the full field reference chain.
        /// </summary>
        public readonly int FieldFrom;

        public readonly int ValueTypeFieldFrom;
        public readonly int ValueTypeFieldOwningITypeDescription;
        /// <summary>
        /// Non-boxed value type fields have their data included as part of the static or instance field bytes of the reference type object they are owned by.
        /// In those cases <see cref="ValueTypeFieldFrom"/> is set to something bigger than -1
        /// and that field index then refers to a field that is not present as part of the type of the
        /// ManagedObject or ManagedType refered to via <see cref="IndexFrom"/>, because that field index belongs to a Value Type.
        /// When trying to read out the field data from the holding Object or Type, use the <seealso cref="CachedSnapshot.FieldDescriptionEntriesCache.Offset"/>
        /// value for <see cref="ValueTypeFieldFrom"/> and add <see cref="OffsetFromReferenceOwnerHeaderStartToFieldOnValueType"/> to get to the correct field bytes.
        /// </summary>
        public readonly int OffsetFromReferenceOwnerHeaderStartToFieldOnValueType;

        /// <summary>
        /// if >= 0, this reference is stored in an Array. If the Array is an Array of value types, <see cref="FieldFrom"/> is also set.
        ///
        /// Make sure to check if an <see cref="ArrayIndexFrom"/> is set before checking if <see cref="FieldFrom"/> is set if you want to get the full field reference chain.
        /// </summary>
        public readonly long ArrayIndexFrom;

        public bool HeldViaValueTypeField => ValueTypeFieldOwningITypeDescription >= 0;

        public ManagedConnection(SourceIndex from, SourceIndex to, int fieldFrom = -1,
            int valueTypeFieldOwningITypeDescription = -1, int valueTypeFieldFrom = -1, int offsetFromReferenceOwnerHeaderStartToFieldOnValueType = 0,
            long arrayIndexFrom = -1)
        {
            IndexFrom = from;
            IndexTo = to;
            FieldFrom = fieldFrom;
            ArrayIndexFrom = arrayIndexFrom;
            // No mix and matching allowed! Either the connection is going via a value Type field and we need valid additional data, or not.
            if (valueTypeFieldOwningITypeDescription >= 0)
            {
                Checks.IsTrue(valueTypeFieldOwningITypeDescription > -1);
                Checks.IsTrue(valueTypeFieldFrom > -1 || ArrayIndexFrom > -1);
            }
            else
            {
                Checks.CheckEquals(valueTypeFieldOwningITypeDescription, -1);
                valueTypeFieldFrom = -1;
                offsetFromReferenceOwnerHeaderStartToFieldOnValueType = 0;
            }
            ValueTypeFieldFrom = valueTypeFieldFrom;
            ValueTypeFieldOwningITypeDescription = valueTypeFieldOwningITypeDescription;
            OffsetFromReferenceOwnerHeaderStartToFieldOnValueType = offsetFromReferenceOwnerHeaderStartToFieldOnValueType;
        }

        public int FromManagedObjectIndex
        {
            get
            {
                if (IndexFrom.Id == SourceIndex.SourceId.ManagedObject)
                    return Convert.ToInt32(IndexFrom.Index);
                return -1;
            }
        }

        public int ToManagedObjectIndex
        {
            get
            {
                if (IndexTo.Id == SourceIndex.SourceId.ManagedObject)
                    return Convert.ToInt32(IndexTo.Index);
                return -1;
            }
        }

        public int FromManagedType
        {
            get
            {
                if (IndexFrom.Id == SourceIndex.SourceId.ManagedType)
                    return Convert.ToInt32(IndexFrom.Index);
                return -1;
            }
        }

        /// <summary>
        /// Unity Objects that have a Managed and a Native object have these two reference each other.
        /// If a Managed Object is found while crawling that has an m_CachedPtr that points to a valid Native Object,
        /// a <see cref="ManagedConnection"/> is created, but only for the direction Native -> Managed.
        /// This represents the only <see cref="ManagedConnection"/> with a Native Object -> Managed Object connection.
        /// Any other Native Object -> Managed Object connection would only be reported via <seealso cref="CachedSnapshot.Connections"/>.
        ///
        /// Do not create other Native -> Managed connections than the ones discribed above.
        /// </summary>
        /// <param name="nativeIndex"></param>
        /// <param name="managedIndex"></param>
        /// <returns></returns>
        public static ManagedConnection MakeUnityEngineObjectConnection(int nativeIndex, long managedIndex)
        {
            return new ManagedConnection(
                new SourceIndex(SourceIndex.SourceId.NativeObject, nativeIndex),
                new SourceIndex(SourceIndex.SourceId.ManagedObject, managedIndex));
        }

        public static ManagedConnection MakeConnection(
            CachedSnapshot snapshot, SourceIndex fromIndex, long toIndex,
            int fromField, int valueTypeFieldOwningITypeDescription, int valueTypeFieldFrom, int offsetFromReferenceOwnerHeaderStartToFieldOnValueType, long arrayIndexFrom)
        {
            if (!fromIndex.Valid)
                throw new InvalidOperationException("Tried to add a Managed Connection without a valid source.");
#if DEBUG_VALIDATION
            if (fromField >= 0)
            {
                switch (fromIndex.Id)
                {
                    case SourceIndex.SourceId.ManagedObject:
                        //from an object
                        if (snapshot.FieldDescriptions.IsStatic[fromField] == 1)
                        {
                            Debug.LogError($"Cannot form a connection from a object (managed object index {fromIndex.Index} of type {snapshot.TypeDescriptions.TypeDescriptionName[snapshot.CrawledData.ManagedObjects[fromIndex.Index].ITypeDescription]}) using a static field {snapshot.FieldDescriptions.FieldDescriptionName[fromField]}"
                                + (valueTypeFieldOwningITypeDescription >= 0?$", held by {snapshot.TypeDescriptions.TypeDescriptionName[valueTypeFieldOwningITypeDescription]}.{snapshot.FieldDescriptions.FieldDescriptionName[valueTypeFieldFrom]} ":"")
                                + (arrayIndexFrom >= 0?$" at array index {arrayIndexFrom}": "."));
                        }
                        break;
                    case SourceIndex.SourceId.ManagedType:
                        //from a type static data
                        if (snapshot.FieldDescriptions.IsStatic[fromField] == 0)
                        {
                            Debug.LogError($"Cannot form a connection from a type ({snapshot.TypeDescriptions.TypeDescriptionName[fromIndex.Index]}) using a non-static field {snapshot.FieldDescriptions.FieldDescriptionName[fromField]}"
                                + (valueTypeFieldOwningITypeDescription >= 0?$", held by {snapshot.TypeDescriptions.TypeDescriptionName[valueTypeFieldOwningITypeDescription]}.{snapshot.FieldDescriptions.FieldDescriptionName[valueTypeFieldFrom]} ":"")
                                + (arrayIndexFrom >= 0?$" at array index {arrayIndexFrom}": "."));
                        }
                        break;
                    default:
                        ref readonly var managedObject = ref snapshot.CrawledData.ManagedObjects[toIndex];
                        unsafe
                        {
                            Debug.LogError($"Trying to form a connection from the field of a {fromIndex.Id} (index: {fromIndex.Index}, raw:{*(ulong*)(&fromIndex)}) to a managed Object (index: {toIndex}) of type {snapshot.TypeDescriptions.TypeDescriptionName[managedObject.ITypeDescription]}.");
                        }
                        break;
                }
            }
#endif
            return new ManagedConnection(fromIndex, new SourceIndex(SourceIndex.SourceId.ManagedObject, toIndex), fromField, valueTypeFieldOwningITypeDescription, valueTypeFieldFrom, offsetFromReferenceOwnerHeaderStartToFieldOnValueType, arrayIndexFrom);
        }
    }
}

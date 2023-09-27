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
        /// <see cref="ValueTypeFieldOwningITypeDescription"/> and <see cref="AddionalValueTypeFieldOffset"/> hold
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
        /// value for <see cref="ValueTypeFieldFrom"/> and add <see cref="AddionalValueTypeFieldOffset"/> to get to the correct field bytes.
        /// </summary>
        public readonly int AddionalValueTypeFieldOffset;

        /// <summary>
        /// if >= 0, this reference is stored in an Array. If the Array is an Array of value types, <see cref="FieldFrom"/> is also set.
        ///
        /// Make sure to check if an <see cref="ArrayIndexFrom"/> is set before checking if <see cref="FieldFrom"/> is set if you want to get the full field reference chain.
        /// </summary>
        public readonly int ArrayIndexFrom;

        public bool HeldViaValueTypeField => ValueTypeFieldOwningITypeDescription >= 0;

        public ManagedConnection(SourceIndex from, SourceIndex to, int fieldFrom = -1,
            int valueTypeFieldOwningITypeDescription = -1, int valueTypeFieldFrom = -1, int addionalValueTypeFieldOffset = 0,
            int arrayIndexFrom = -1)
        {
            IndexFrom = from;
            IndexTo = to;
            FieldFrom = fieldFrom;
            ArrayIndexFrom = arrayIndexFrom;
            // No mix and matching allowed! Either the connection is going via a value Type field and we need valid additional data, or not.
#if ENABLE_MEMORY_PROFILER_DEBUG
            if(valueTypeFieldOwningITypeDescription >= 0)
            {
                Checks.IsTrue(valueTypeFieldOwningITypeDescription > -1);
                Checks.IsTrue(valueTypeFieldFrom > -1);
            }
            else
            {
                Checks.CheckEquals(valueTypeFieldOwningITypeDescription, -1);
                Checks.CheckEquals(valueTypeFieldFrom, -1);
                Checks.CheckEquals(addionalValueTypeFieldOffset, 0);
            }
#endif
            ValueTypeFieldFrom = valueTypeFieldFrom;
            ValueTypeFieldOwningITypeDescription = valueTypeFieldOwningITypeDescription;
            AddionalValueTypeFieldOffset = addionalValueTypeFieldOffset;
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
        /// This property returns the native object index of this connection,
        /// or -1 if this connection does not originate from a Native Object
        /// or it Asserts if it is not actually such a connection.
        /// </summary>
        public int UnityEngineNativeObjectIndex
        {
            get
            {
                if (IndexFrom.Id == SourceIndex.SourceId.NativeObject)
                {
                    Debug.Assert(IndexTo.Id == SourceIndex.SourceId.ManagedObject);
                    return Convert.ToInt32(IndexFrom.Index);
                }
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
        /// This property returns the managed object index of this connection,
        /// or -1 if this connection does not point to a Managed Object
        /// or it Asserts if it is not actually such a connection.
        /// </summary>
        public int UnityEngineManagedObjectIndex
        {
            get
            {
                if (IndexTo.Id == SourceIndex.SourceId.ManagedObject)
                {
                    Debug.Assert(IndexFrom.Id == SourceIndex.SourceId.NativeObject);
                    return Convert.ToInt32(IndexTo.Index);
                }
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
        public static ManagedConnection MakeUnityEngineObjectConnection(int nativeIndex, int managedIndex)
        {
            return new ManagedConnection(
                new SourceIndex(SourceIndex.SourceId.NativeObject, nativeIndex),
                new SourceIndex(SourceIndex.SourceId.ManagedObject, managedIndex));
        }

        public static ManagedConnection MakeConnection(
            CachedSnapshot snapshot, SourceIndex fromIndex, int toIndex,
            int fromField, int valueTypeFieldOwningITypeDescription, int valueTypeFieldFrom, int addionalValueTypeFieldOffset, int arrayIndexFrom)
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
                            Debug.LogError("Cannot form a connection from an object using a static field.");
                        }
                        break;
                    case SourceIndex.SourceId.ManagedType:
                        //from a type static data
                        if (snapshot.FieldDescriptions.IsStatic[fromField] == 0)
                        {
                            Debug.LogError("Cannot form a connection from a type using a non-static field.");
                        }
                        break;
                    default:
                        Debug.LogError($"Trying to form a connection from the field of a {fromIndex.Id} to a managed Object.");
                        break;
                }
            }
#endif
            return new ManagedConnection(fromIndex, new SourceIndex(SourceIndex.SourceId.ManagedObject, toIndex), fromField, valueTypeFieldOwningITypeDescription, valueTypeFieldFrom, addionalValueTypeFieldOffset, arrayIndexFrom);
        }
    }
}

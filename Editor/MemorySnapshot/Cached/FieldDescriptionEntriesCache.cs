using System;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Format;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;

namespace Unity.MemoryProfiler.Editor
{
    internal partial class CachedSnapshot
    {
        /// <summary>
        /// Per-field attribute and static-flavor flags, mirroring the native
        /// <c>ScriptingFieldFlags</c> (Modules/Scripting/Include/Scripting/ScriptingBackend/ScriptingProfilerApi.h)
        /// bit-for-bit. Populated on all three backends from format v20
        /// (<see cref="Format.FormatVersion.StaticFieldAndNonGCHeapVersion"/>) onward; only CoreCLR sets
        /// <see cref="AppDomainStatic"/>/<see cref="ThreadStatic"/>/<see cref="ContextStatic"/>/<see cref="RVAStatic"/> —
        /// Mono/IL2CPP set only <see cref="IsStatic"/> among the static bits.
        /// </summary>
        [Flags]
        public enum FieldFlags : uint
        {
            None = 0,
            IsStatic = 0x1,
            AppDomainStatic = 0x2,
            ThreadStatic = 0x4,
            ContextStatic = 0x8, // dead post-.NET-Core; always 0
            RVAStatic = 0x10,
            InitOnly = 0x20, // readonly
            Literal = 0x40, // compile-time const
            VisibilityMask = 0x380, // 3 bits, ECMA-335 fdFieldAccessMask values 0-6, shift 7
            SpecialName = 0x400,
        }

        public class FieldDescriptionEntriesCache : IDisposable
        {
            public long Count;
            public string[] FieldDescriptionName;
            public DynamicArray<int> Offset = default;
            public DynamicArray<int> TypeIndex = default;
            public DynamicArray<byte> IsStatic = default;
            /// <summary>
            /// Low byte = <c>CorElementType</c> (ECMA-335 §II.23.1.16), high byte = pointer indirection level.
            /// 0 for every row on Mono/IL2CPP, which synthesise a per-pointer-element-type class instead so
            /// <see cref="TypeIndex"/> already resolves; CoreCLR emits PTR/FNPTR here since it does not. A
            /// non-zero value alone doesn't prove a CoreCLR row — see <see cref="IsPointerLikeElementType"/>.
            /// </summary>
            public DynamicArray<ushort> ElementType = default;

            /// <summary>
            /// Consolidated static-flavor + ECMA-335 attribute bits (<see cref="FieldFlags"/>), parallel to
            /// <see cref="TypeIndex"/>. Left uncreated (not zero-filled — check <see cref="DynamicArray{T}.IsCreated"/>
            /// or gate on <see cref="CachedSnapshot.HasStaticFieldSlotsAndFlags"/> before indexing) on older
            /// files (&lt; format v20, <see cref="Format.FormatVersion.StaticFieldAndNonGCHeapVersion"/>) —
            /// use <see cref="IsStatic"/> instead for those.
            /// </summary>
            public DynamicArray<FieldFlags> Flags = default;

            /// <summary>
            /// <c>CorElementType</c> (ECMA-335 §II.23.1.16) value the native CoreCLR writer can put in the
            /// low byte of <c>FieldDescriptions_ElementType</c>. Mono/IL2CPP always write 0 (regular type)
            /// since they synthesise a per-pointer-element-type class instead, so <see cref="TypeIndex"/>
            /// is always valid for those two backends.
            /// </summary>
            const byte k_ElementTypeFnPtr = 0x1B;

            /// <summary>
            /// True for a CoreCLR PTR/FNPTR field, where <see cref="TypeIndex"/> may legitimately be -1 and
            /// must not be used to index into <c>TypeDescriptions</c>. High byte of <paramref name="elementType"/>
            /// is the pointer indirection level (T* = 1, T** = 2, ...); FNPTR fields carry indirection level 0
            /// with the FnPtr low byte instead. Requires <paramref name="hasBackendManagedGraph"/>
            /// (<see cref="CachedSnapshot.HasBackendManagedGraph"/>) since <see cref="ElementType"/> is only
            /// gated on file format version, not backend, so a format-v19+ Mono/IL2CPP file could otherwise
            /// be misread.
            /// </summary>
            public static bool IsPointerLikeElementType(ushort elementType, bool hasBackendManagedGraph) =>
                hasBackendManagedGraph &&
                (elementType & 0x00FF) != k_ElementTypeSzArray &&
                ((elementType & 0xFF00) != 0 || (elementType & 0x00FF) == k_ElementTypeFnPtr);

            /// <summary>
            /// <c>CorElementType</c> value the native CoreCLR writer puts in the low byte of
            /// <c>FieldDescriptions_ElementType</c> for an array-shaped field whose element type had
            /// no live ClassID (see the array-shaped-miss design). High byte carries the array rank.
            /// </summary>
            const byte k_ElementTypeSzArray = 0x1D;

            /// <summary>
            /// True for a CoreCLR array-shaped field where <see cref="TypeIndex"/> names the array's
            /// ELEMENT type, not the array type itself. PTR/FNPTR and SZARRAY shapes are mutually
            /// exclusive by construction in the native signature parser (see
            /// <see cref="IsPointerLikeElementType"/>'s remarks for the <paramref name="hasBackendManagedGraph"/>
            /// gating rationale, which applies identically here).
            /// </summary>
            public static bool IsArrayShapedElementType(ushort elementType, bool hasBackendManagedGraph) =>
                hasBackendManagedGraph && (elementType & 0x00FF) == k_ElementTypeSzArray;

            /// <summary>
            /// Decodes the array rank carried in the high byte of an <see cref="IsArrayShapedElementType"/> value.
            /// </summary>
            public static int GetArrayRankFromElementType(ushort elementType) => elementType >> 8;

            /// <summary>
            /// Byte width of <paramref name="fieldIndex"/>'s slot in <see cref="TypeDescriptionEntriesCache.StaticFieldSlots"/>.
            /// A CoreCLR PTR/FNPTR or array-shaped field stores the pointee/element type in
            /// <see cref="TypeIndex"/>, not the field's own type, so those must resolve to pointer size
            /// via <see cref="ElementType"/> before falling back to the <see cref="TypeIndex"/>-based
            /// value-type/reference-type check that is correct for every other field shape. Gated on
            /// <see cref="CachedSnapshot.HasStaticFieldSlotsAndFlags"/> (v20+) — every caller of this
            /// method already only runs in that context, and v20 implies v19, so <see cref="ElementType"/>
            /// is always allocated here; unlike <see cref="CachedSnapshot.HasBackendManagedGraph"/>, this
            /// doesn't depend on whether any managed objects were found, which matters since static-field
            /// slot data is per-type, not per-instance. Passes a literal <see langword="true"/> for the
            /// helpers' backend-gate parameter rather than <see cref="CachedSnapshot.HasBackendManagedGraph"/>:
            /// the native writer serializes <c>elementType = 0</c> unconditionally for every Mono/IL2CPP
            /// field (never anything else), so the bit checks below are correct regardless of which
            /// backend wrote the file — no backend signal is needed for this specific check.
            /// </summary>
            public static int GetStaticFieldSlotByteSize(CachedSnapshot snapshot, int fieldIndex)
            {
                if (snapshot.HasStaticFieldSlotsAndFlags)
                {
                    var elementType = snapshot.FieldDescriptions.ElementType[fieldIndex];
                    if (IsPointerLikeElementType(elementType, true) ||
                        IsArrayShapedElementType(elementType, true))
                        return (int)snapshot.VirtualMachineInformation.PointerSize;
                }

                var fieldTypeIndex = snapshot.FieldDescriptions.TypeIndex[fieldIndex];
                return fieldTypeIndex >= 0 && snapshot.TypeDescriptions.HasFlag(fieldTypeIndex, TypeFlags.kValueType)
                    ? snapshot.TypeDescriptions.Size[fieldTypeIndex]
                    : (int)snapshot.VirtualMachineInformation.PointerSize;
            }

            unsafe public FieldDescriptionEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.FieldDescriptions_Name);
                FieldDescriptionName = new string[Count];

                if (Count == 0)
                    return;

                Offset = new DynamicArray<int>(Count, Allocator.Persistent);
                TypeIndex = new DynamicArray<int>(Count, Allocator.Persistent);
                IsStatic = new DynamicArray<byte>(Count, Allocator.Persistent);

                Offset = reader.Read(EntryType.FieldDescriptions_Offset, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                TypeIndex = reader.Read(EntryType.FieldDescriptions_TypeIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                IsStatic = reader.Read(EntryType.FieldDescriptions_IsStatic, 0, Count, Allocator.Persistent).Result.Reinterpret<byte>();
                // FieldDescriptions_ElementType only exists from format v19 onward; older files leave
                // ElementType default (empty), which IsPointerLikeElementType treats as "regular type".
                if (reader.FormatVersion >= FormatVersion.ManagedConnectionsVersion)
                {
                    ElementType = reader.Read(EntryType.FieldDescriptions_ElementType, 0, Count, Allocator.Persistent).Result.Reinterpret<ushort>();
                }

                // FieldDescriptions_Flags only exists from format v20 onward; older files leave Flags
                // zero-filled — use IsStatic instead for those.
                if (reader.FormatVersion >= FormatVersion.StaticFieldAndNonGCHeapVersion)
                    Flags = reader.Read(EntryType.FieldDescriptions_Flags, 0, Count, Allocator.Persistent).Result.Reinterpret<FieldFlags>();

                using (var tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpBufferSize = reader.GetSizeForEntryRange(EntryType.FieldDescriptions_Name, 0, Count);
                    tmp.Resize(tmpBufferSize, false);
                    reader.Read(EntryType.FieldDescriptions_Name, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref FieldDescriptionName);
                }
            }

            public void Dispose()
            {
                Count = 0;
                Offset.Dispose();
                TypeIndex.Dispose();
                IsStatic.Dispose();
                ElementType.Dispose();
                Flags.Dispose();
                FieldDescriptionName = null;
            }
        }

    }
}

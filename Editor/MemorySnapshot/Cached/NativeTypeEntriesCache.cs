using System;
using Unity.Collections;
using Unity.MemoryProfiler.Editor.Containers;
using Unity.MemoryProfiler.Editor.Format;
using Unity.MemoryProfiler.Editor.Format.QueriedSnapshot;

namespace Unity.MemoryProfiler.Editor
{
    internal partial class CachedSnapshot
    {
        public unsafe class NativeTypeEntriesCache : IDisposable
        {
            public const int FirstValidTypeIndex = 0;
            public const int InvalidTypeIndex = -1;

            public long Count;
            public string[] TypeName;
            public DynamicArray<int> NativeBaseTypeArrayIndex = default;
            const string k_Transform = "Transform";
            public int TransformIdx { get; private set; } = InvalidTypeIndex;
            const string k_RectTransform = "RectTransform";
            public int RectTransformIdx { get; private set; } = InvalidTypeIndex;

            /// <summary>
            /// Technically, <see cref="IsOrDerivesFrom"/>(typeIndex, <see cref="TransformIdx"/>) could be used instead of this method,
            /// but since that approach would have to check the entire inheritance chain, this method is more efficient,
            /// and finding Transforms is enough of a hot path to warrant this explicit shorthand.
            /// </summary>
            /// <param name="typeIndex"></param>
            /// <returns></returns>
            public bool IsTransformOrRectTransform(long typeIndex) => (typeIndex >= 0) && (typeIndex == TransformIdx || typeIndex == RectTransformIdx);

            const string k_NamedObject = "NamedObject";
            public int NamedObjectIdx { get; private set; } = InvalidTypeIndex;

            const string k_BaseObject = "Object";
            public int BaseObjectIdx { get; private set; } = InvalidTypeIndex;

            const string k_GameObject = "GameObject";
            public int GameObjectIdx { get; private set; } = InvalidTypeIndex;

            const string k_MonoBehaviour = "MonoBehaviour";
            public int MonoBehaviourIdx { get; private set; } = InvalidTypeIndex;

            const string k_MonoScript = "MonoScript";
            public int MonoScriptIdx { get; private set; } = InvalidTypeIndex;

            const string k_Component = "Component";
            public int ComponentIdx { get; private set; } = InvalidTypeIndex;

            const string k_ScriptableObject = "ScriptableObject";
            const int k_ScriptableObjectDefaultTypeArrayIndexOffsetFromEnd = 2;
            public int ScriptableObjectIdx { get; private set; } = InvalidTypeIndex;

            const string k_EditorScriptableObject = "EditorScriptableObject";
            public int EditorScriptableObjectIdx { get; private set; } = InvalidTypeIndex;
            const int k_EditorScriptableObjectDefaultTypeArrayIndexOffsetFromEnd = 1;

            const string k_AssetBundle = "AssetBundle";
            public int AssetBundleIdx { get; private set; } = InvalidTypeIndex;

            const string k_Prefab = "Prefab";
            /// <summary>
            /// Only exists for snapshots where <see cref="MetaData.IsEditorCapture"/> is true.
            /// </summary>
            public int PrefabIdx { get; private set; } = InvalidTypeIndex;

            public NativeTypeEntriesCache(ref IFileReader reader)
            {
                Count = reader.GetEntryCount(EntryType.NativeTypes_Name);
                TypeName = new string[Count];

                if (Count == 0)
                    return;

                NativeBaseTypeArrayIndex = reader.Read(EntryType.NativeTypes_NativeBaseTypeArrayIndex, 0, Count, Allocator.Persistent).Result.Reinterpret<int>();
                using (DynamicArray<byte> tmp = new DynamicArray<byte>(0, Allocator.TempJob))
                {
                    var tmpSize = reader.GetSizeForEntryRange(EntryType.NativeTypes_Name, 0, Count);
                    tmp.Resize(tmpSize, false);
                    reader.Read(EntryType.NativeTypes_Name, tmp, 0, Count);
                    ConvertDynamicArrayByteBufferToManagedArray(tmp, ref TypeName);
                }

                BaseObjectIdx = Array.FindIndex(TypeName, x => x == k_BaseObject);
                NamedObjectIdx = Array.FindIndex(TypeName, x => x == k_NamedObject);

                TransformIdx = Array.FindIndex(TypeName, x => x == k_Transform);
                RectTransformIdx = Array.FindIndex(TypeName, x => x == k_RectTransform);
                GameObjectIdx = Array.FindIndex(TypeName, x => x == k_GameObject);
                MonoBehaviourIdx = Array.FindIndex(TypeName, x => x == k_MonoBehaviour);
                MonoScriptIdx = Array.FindIndex(TypeName, x => x == k_MonoScript);
                ComponentIdx = Array.FindIndex(TypeName, x => x == k_Component);
                AssetBundleIdx = Array.FindIndex(TypeName, x => x == k_AssetBundle);
                PrefabIdx = Array.FindIndex(TypeName, x => x == k_Prefab);

                // for the fakable types ScriptableObject and EditorScriptable Objects, with the current backend, Array.FindIndex is always going to hit the worst case
                // in the current format, these types are always added last. Assume that for speed, keep Array.FindIndex as fallback in case the format changes
                ScriptableObjectIdx = FindTypeWithHint(k_ScriptableObject, Count - k_ScriptableObjectDefaultTypeArrayIndexOffsetFromEnd);
                EditorScriptableObjectIdx = FindTypeWithHint(k_EditorScriptableObject, Count - k_EditorScriptableObjectDefaultTypeArrayIndexOffsetFromEnd);
                if (EditorScriptableObjectIdx >= 0 && ScriptableObjectIdx >= 0)
                {
                    // ScriptableObject is more a variation than a base type of EditorScriptableObject, but for the purpose of this tool, we'll treat it as a base type
                    // Especially since the EditorScriptableObject is a fake type and its Managed Type is the same as ScriptableObject.
                    NativeBaseTypeArrayIndex[EditorScriptableObjectIdx] = ScriptableObjectIdx;
                }
            }

            int FindTypeWithHint(string typeName, long hintAtLikelyIndex)
            {
                if (TypeName[hintAtLikelyIndex] == typeName)
                    return (int)hintAtLikelyIndex;
                else
                    return Array.FindIndex(TypeName, x => x == typeName);
            }

            public bool IsOrDerivesFrom(int typeIndexToCheck, int baseTypeToCheckAgainst)
            {
                while (typeIndexToCheck != baseTypeToCheckAgainst && NativeBaseTypeArrayIndex[typeIndexToCheck] >= 0)
                {
                    typeIndexToCheck = NativeBaseTypeArrayIndex[typeIndexToCheck];
                }
                return typeIndexToCheck == baseTypeToCheckAgainst;
            }

            public void Dispose()
            {
                Count = 0;
                NativeBaseTypeArrayIndex.Dispose();
                TypeName = null;
            }
        }
    }
}

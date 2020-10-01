using UnityEngine;
using System.Collections.Generic;
using Unity.MemoryProfiler.Editor.Database;
using Unity.MemoryProfiler.Editor.Database.Operation;
using Unity.MemoryProfiler.Editor.Database.Soa;
using Unity.Profiling;
using Unity.MemoryProfiler.Editor.Format;

namespace Unity.MemoryProfiler.Editor
{
    internal class RawSchema : Schema
    {
        public static string kPrefixTableName = "Raw";
        public static string kPrefixTableDisplayName = "Raw ";

        public CachedSnapshot m_Snapshot;
        public SnapshotObjectDataFormatter formatter;
        APITable[] m_Tables;
        Table[] m_ExtraTable;

        public class TypeBase
        {
            public int[] TypeIndex;
            public int[] BaseIndex;
            public TypeBase(CachedSnapshot snapshot)
            {
                ComputeTypeBases(snapshot);
            }

            public void ComputeTypeBases(CachedSnapshot snapshot)
            {
                var typeIndex = new List<int>();
                var baseIndex = new List<int>();
                for (int i = 0; i != snapshot.nativeTypes.Count; ++i)
                {
                    var currentBase = snapshot.nativeTypes.nativeBaseTypeArrayIndex[i];
                    while (currentBase >= 0)
                    {
                        typeIndex.Add(i);
                        baseIndex.Add(currentBase);
                        currentBase = snapshot.nativeTypes.nativeBaseTypeArrayIndex[currentBase];
                    }
                }
                TypeIndex = typeIndex.ToArray();
                BaseIndex = baseIndex.ToArray();
            }
        };
        TypeBase m_TypeBase;

        Dictionary<string, Table> m_TablesByName = new Dictionary<string, Table>();

        static ProfilerMarker s_CreateSnapshotSchema = new ProfilerMarker("CreateSnapshotSchema");

        public void SetupSchema(CachedSnapshot snapshot, ObjectDataFormatter objectDataFormatter)
        {
            using (s_CreateSnapshotSchema.Auto())
            {
                m_Snapshot = snapshot;
                formatter = new SnapshotObjectDataFormatter(objectDataFormatter, m_Snapshot);
                CreateTables(m_Snapshot.CrawledData);
            }
        }

        private void CreateTables(ManagedData crawledData)
        {
            List<APITable> tables = new List<APITable>();
            CreateTable_RootReferences(tables);
            CreateTable_NativeAllocations(tables);
            CreateTable_NativeAllocationSites(tables);
            CreateTable_NativeCallstackSymbols(tables);
            CreateTable_NativeMemoryLabels(tables);
            CreateTable_NativeMemoryRegions(tables);
            CreateTable_NativeObjects(tables);
            CreateTable_NativeTypes(tables);
            CreateNativeTable_NativeTypeBase(tables);
            CreateTable_NativeConnections(tables);
            CreateTable_TypeDescriptions(tables);
            m_Tables = tables.ToArray();


            List<Table> extraTable = new List<Table>();

            extraTable.Add(new ObjectAllManagedTable(this, formatter, m_Snapshot, crawledData, ObjectTable.ObjectMetaType.Managed));
            extraTable.Add(new ObjectAllNativeTable(this, formatter, m_Snapshot, crawledData, ObjectTable.ObjectMetaType.Native));
            extraTable.Add(new ObjectAllTable(this, formatter, m_Snapshot, crawledData, ObjectTable.ObjectMetaType.All));

            m_ExtraTable = extraTable.ToArray();
            foreach (var t in m_ExtraTable)
            {
                m_TablesByName.Add(t.GetName(), t);
            }
        }

        public void Clear()
        {
            m_Snapshot = null;
            formatter.Clear();
        }

        public override string GetDisplayName()
        {
            return m_Snapshot.packedMemorySnapshot.recordDate.ToString();
        }

        public override bool OwnsTable(Table table)
        {
            if (table.Schema == this) return true;
            if (System.Array.IndexOf(m_Tables, table) >= 0) return true;
            if (System.Array.IndexOf(m_ExtraTable, table) >= 0) return true;
            return false;
        }

        public override long GetTableCount()
        {
            return m_Tables.Length + m_ExtraTable.Length;
        }

        public override Table GetTableByName(string name)
        {
            Table t;
            if (m_TablesByName.TryGetValue(name, out t))
            {
                if (t is ExpandTable)
                {
                    var et = (ExpandTable)t;
                    et.ResetAllGroup();
                }
                return t;
            }
            return null;
        }

        public override Table GetTableByIndex(long index)
        {
            if (index < 0) return null;
            Table t = null;
            if (index >= m_Tables.Length)
            {
                index -= m_Tables.Length;
                if (index >= m_ExtraTable.Length) return null;
                t = m_ExtraTable[index];
            }
            else
            {
                t = m_Tables[index];
            }

            if (t is ExpandTable)
            {
                var et = (ExpandTable)t;
                et.ResetAllGroup();
            }
            return t;
        }

        private bool TryGetParam(ParameterSet param, string name, out ulong value)
        {
            if (param == null)
            {
                value = default(ulong);
                return false;
            }
            Expression expObj;
            param.TryGet(ObjectTable.ObjParamName, out expObj);
            if (expObj == null)
            {
                value = 0;
                return false;
            }
            if (expObj is TypedExpression<ulong>)
            {
                TypedExpression<ulong> e = (TypedExpression<ulong>)expObj;
                value = e.GetValue(0);
            }
            else
            {
                if (!ulong.TryParse(expObj.GetValueString(0, DefaultDataFormatter.Instance), out value))
                {
                    return false;
                }
            }
            return true;
        }

        private bool TryGetParam(ParameterSet param, string name, out int value)
        {
            if (param == null)
            {
                value = default(int);
                return false;
            }
            Expression expObj;
            param.TryGet(ObjectTable.ObjParamName, out expObj);
            if (expObj == null)
            {
                value = 0;
                return false;
            }
            if (expObj is TypedExpression<int>)
            {
                TypedExpression<int> e = (TypedExpression<int>)expObj;
                value = e.GetValue(0);
            }
            else
            {
                if (!int.TryParse(expObj.GetValueString(0, DefaultDataFormatter.Instance), out value))
                {
                    return false;
                }
            }
            return true;
        }

        public override Table GetTableByName(string name, ParameterSet param)
        {
            if (name == ObjectTable.TableName)
            {
                ulong obj;
                if (!TryGetParam(param, ObjectTable.ObjParamName, out obj))
                {
                    return null;
                }
                int iType;
                if (!TryGetParam(param, ObjectTable.TypeParamName, out iType))
                {
                    iType = -1;
                }

                ObjectData od = ObjectData.FromManagedPointer(m_Snapshot, obj, iType);
                var table = new ObjectSingleTable(this, formatter, m_Snapshot, m_Snapshot.CrawledData, od, od.isNative ? ObjectTable.ObjectMetaType.Native : ObjectTable.ObjectMetaType.Managed);
                return table;
            }
            else if (name == ObjectReferenceTable.kObjectReferenceTableName)
            {
                int objUnifiedIndex;
                if (!TryGetParam(param, ObjectTable.ObjParamName, out objUnifiedIndex))
                {
                    return null;
                }
                var od = ObjectData.FromUnifiedObjectIndex(m_Snapshot, objUnifiedIndex);
                var table = new ObjectReferenceTable(this, formatter, m_Snapshot, m_Snapshot.CrawledData, od, ObjectTable.ObjectMetaType.All); //, od.isNative ? ObjectTable.ObjectMetaType.Native : ObjectTable.ObjectMetaType.Managed);
                return table;
                //ObjectReferenceTable
            }
            else
            {
                return GetTableByName(name);
            }
        }

        private bool HasBit(ObjectFlags bitfield, ObjectFlags bit)
        {
            return (bitfield & bit) == bit;
        }

        private void SetBit(ref ObjectFlags bitfield, ObjectFlags bit, bool value)
        {
            bitfield = bitfield & ~bit | (value ? bit : 0);
        }

        private bool HasBit(HideFlags bitfield, HideFlags bit)
        {
            return (bitfield & bit) == bit;
        }

        private void SetBit(ref HideFlags bitfield, HideFlags bit, bool value)
        {
            bitfield = bitfield & ~bit | (value ? bit : 0);
        }

        private bool HasBit(TypeFlags bitfield, TypeFlags bit)
        {
            return (bitfield & bit) == bit;
        }

        private void SetBit(ref TypeFlags bitfield, TypeFlags bit, bool value)
        {
            bitfield = bitfield & ~bit | (value ? bit : 0);
        }

        private int GetBits(TypeFlags bitfield, TypeFlags mask, int shift)
        {
            return (int)(bitfield & mask) >> shift;
        }

        private void SetBits(ref TypeFlags bitfield, TypeFlags mask, int shift, int value)
        {
            bitfield = bitfield & ~mask | (TypeFlags)((value << shift) & (int)mask);
        }

        private void CreateTable_RootReferences(List<APITable> tables)
        {
            APITable table = new APITable(this, m_Snapshot, m_Snapshot.nativeRootReferences.dataSet);
            table.AddColumn(
                new MetaColumn("id", "Id", new MetaType(typeof(long), DataMatchMethod.AsNumber), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeRootReferences.id)
            );
            table.AddColumn(
                new MetaColumn("areaName", "Area Name", new MetaType(typeof(string)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeRootReferences.areaName)
            );
            table.AddColumn(
                new MetaColumn("objectName", "Object Name", new MetaType(typeof(string)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeRootReferences.objectName)
            );
            table.AddColumn(
                new MetaColumn("accumulatedSize", "Accumulated Size", new MetaType(typeof(ulong), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate
                    , Grouping.GetMergeAlgo(Grouping.MergeAlgo.sum, typeof(ulong)), "size")
                , DataArray.MakeColumn(m_Snapshot.nativeRootReferences.accumulatedSize)
            );
            table.CreateTable(kPrefixTableName + "RootReference", kPrefixTableDisplayName + "Root Reference");
            AddTable(table, tables);
        }

        private void CreateTable_NativeAllocationSites(List<APITable> tables)
        {
            APITable table = new APITable(this, m_Snapshot, m_Snapshot.nativeAllocationSites.dataSet);
            table.AddColumn(
                new MetaColumn("id", "Id", new MetaType(typeof(long), DataMatchMethod.AsNumber), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeAllocationSites.id)
            );
            table.AddColumn(
                new MetaColumn("memoryLabelIndex", "Memory Label Index", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeAllocationSites.memoryLabelIndex)
            );

            table.CreateTable(kPrefixTableName + "NativeAllocationSite", kPrefixTableDisplayName + "Native Allocation Site");
            AddTable(table, tables);
        }

        private void CreateTable_NativeCallstackSymbols(List<APITable> tables)
        {
            APITable table = new APITable(this, m_Snapshot, m_Snapshot.nativeCallstackSymbols.dataSet);
            table.AddColumn(
                new MetaColumn("symbol", "Symbol", new MetaType(typeof(ulong), DataMatchMethod.AsNumber), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeCallstackSymbols.symbol)
            );
            table.AddColumn(
                new MetaColumn("readableStackTrace", "Readable Stack Trace", new MetaType(typeof(string)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeCallstackSymbols.readableStackTrace)
            );
            table.CreateTable(kPrefixTableName + "NativeCallstackSymbol", kPrefixTableDisplayName + "Native Callstack Symbol");
            AddTable(table, tables);
        }

        private void CreateTable_NativeMemoryLabels(List<APITable> tables)
        {
            APITable table = new APITable(this, m_Snapshot, m_Snapshot.nativeMemoryLabels.dataSet);
            table.AddColumn(
                new MetaColumn("name", "Name", new MetaType(typeof(string)), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeMemoryLabels.memoryLabelName)
            );
            table.CreateTable(kPrefixTableName + "NativeMemoryLabel", kPrefixTableDisplayName + "Native Memory Label");
            AddTable(table, tables);
        }

        private void CreateTable_NativeMemoryRegions(List<APITable> tables)
        {
            APITable table = new APITable(this, m_Snapshot, m_Snapshot.nativeMemoryRegions.dataSet);
            table.AddColumn(
                new MetaColumn("parentIndex", "Parent Index", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeMemoryRegions.parentIndex)
            );
            table.AddColumn(
                new MetaColumn("name", "Name", new MetaType(typeof(string)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeMemoryRegions.memoryRegionName)
            );
            table.AddColumn(
                new MetaColumn("addressBase", "Address Base", new MetaType(typeof(ulong), DataMatchMethod.AsNumber), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeMemoryRegions.addressBase)
            );
            table.AddColumn(
                new MetaColumn("addressSize", "Address Size", new MetaType(typeof(ulong), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate
                    , Grouping.GetMergeAlgo(Grouping.MergeAlgo.sum, typeof(ulong)), "size")
                , DataArray.MakeColumn(m_Snapshot.nativeMemoryRegions.addressSize)
            );
            table.AddColumn(
                new MetaColumn("firstAllocationIndex", "First Allocation Index", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeMemoryRegions.firstAllocationIndex)
            );
            table.AddColumn(
                new MetaColumn("numAllocations", "Number Of Allocations", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate
                    , Grouping.GetMergeAlgo(Grouping.MergeAlgo.sum, typeof(int)))
                , DataArray.MakeColumn(m_Snapshot.nativeMemoryRegions.numAllocations)
            );
            table.CreateTable(kPrefixTableName + "NativeMemoryRegion", kPrefixTableDisplayName + "Native Memory Region");
            AddTable(table, tables);
        }

        private void CreateTable_NativeObjects(List<APITable> tables)
        {
            APITable table = new APITable(this, m_Snapshot, m_Snapshot.nativeObjects.dataSet);
            table.AddColumn(
                new MetaColumn("name", "Name", new MetaType(typeof(string)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeObjects.objectName)
            );
            table.AddColumn(
                new MetaColumn("instanceId", "Instance Id", new MetaType(typeof(int), DataMatchMethod.AsNumber), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeObjects.instanceId)
            );
            table.AddColumn(
                new MetaColumn("size", "Size", new MetaType(typeof(ulong), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate
                    , Grouping.GetMergeAlgo(Grouping.MergeAlgo.sum, typeof(ulong)), "size")
                , DataArray.MakeColumn(m_Snapshot.nativeObjects.size)
            );

            table.AddColumn(
                new MetaColumn("nativeObjectAddress", "Native Object Address", new MetaType(typeof(ulong), DataMatchMethod.AsNumber), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeObjects.nativeObjectAddress)
            );
            table.AddColumn(
                new MetaColumn("rootReferenceId", "Root Reference Id", new MetaType(typeof(long), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeObjects.rootReferenceId)
            );

            table.AddColumn(
                new MetaColumn("nativeTypeArrayIndex", "Native Type Array Index", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeObjects.nativeTypeArrayIndex)
            );

            table.AddColumn(
                new MetaColumn("isPersistent", "Persistent", new MetaType(typeof(bool)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn_Transform(m_Snapshot.nativeObjects.flags, (a) => HasBit(a, ObjectFlags.IsPersistent) , (ref ObjectFlags o, bool v) => SetBit(ref o, ObjectFlags.IsPersistent, v))
            );
            table.AddColumn(
                new MetaColumn("isDontDestroyOnLoad", "Don't Destroy On Load", new MetaType(typeof(bool)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn_Transform(m_Snapshot.nativeObjects.flags, (a) => HasBit(a, ObjectFlags.IsDontDestroyOnLoad), (ref ObjectFlags o, bool v) => SetBit(ref o, ObjectFlags.IsDontDestroyOnLoad, v))
            );
            table.AddColumn(
                new MetaColumn("isManager", "Manager", new MetaType(typeof(bool)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn_Transform(m_Snapshot.nativeObjects.flags, (a) => HasBit(a, ObjectFlags.IsManager), (ref ObjectFlags o, bool v) => SetBit(ref o, ObjectFlags.IsManager, v))
            );

            table.AddColumn(
                new MetaColumn("HideInHierarchy", "Hide In Hierarchy", new MetaType(typeof(bool)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn_Transform(m_Snapshot.nativeObjects.hideFlags, (a) => HasBit(a, HideFlags.HideInHierarchy), (ref HideFlags o, bool v) => SetBit(ref o, HideFlags.HideInHierarchy, v))
            );
            table.AddColumn(
                new MetaColumn("HideInInspector", "Hide In Inspector", new MetaType(typeof(bool)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn_Transform(m_Snapshot.nativeObjects.hideFlags, (a) => HasBit(a, HideFlags.HideInInspector), (ref HideFlags o, bool v) => SetBit(ref o, HideFlags.HideInInspector, v))
            );
            table.AddColumn(
                new MetaColumn("DontSaveInEditor", "Don't Save In Editor", new MetaType(typeof(bool)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn_Transform(m_Snapshot.nativeObjects.hideFlags, (a) => HasBit(a, HideFlags.DontSaveInEditor), (ref HideFlags o, bool v) => SetBit(ref o, HideFlags.DontSaveInEditor, v))
            );
            table.AddColumn(
                new MetaColumn("NotEditable", "Not Editable", new MetaType(typeof(bool)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn_Transform(m_Snapshot.nativeObjects.hideFlags, (a) => HasBit(a, HideFlags.NotEditable), (ref HideFlags o, bool v) => SetBit(ref o, HideFlags.NotEditable, v))
            );
            table.AddColumn(
                new MetaColumn("DontSaveInBuild", "Don't Save In Build", new MetaType(typeof(bool)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn_Transform(m_Snapshot.nativeObjects.hideFlags, (a) => HasBit(a, HideFlags.DontSaveInBuild), (ref HideFlags o, bool v) => SetBit(ref o, HideFlags.DontSaveInBuild, v))
            );
            table.AddColumn(
                new MetaColumn("DontUnloadUnusedAsset", "Don't Unload Unused Asset", new MetaType(typeof(bool)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn_Transform(m_Snapshot.nativeObjects.hideFlags, (a) => HasBit(a, HideFlags.DontUnloadUnusedAsset), (ref HideFlags o, bool v) => SetBit(ref o, HideFlags.DontUnloadUnusedAsset, v))
            );

            table.CreateTable(kPrefixTableName + "NativeObject", kPrefixTableDisplayName + "Native Object");
            AddTable(table, tables);
        }

        private void CreateTable_NativeTypes(List<APITable> tables)
        {
            APITable table = new APITable(this, m_Snapshot, m_Snapshot.nativeTypes.dataSet);
            table.AddColumn(
                new MetaColumn("name", "Name", new MetaType(typeof(string)), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeTypes.typeName)
            );
            table.AddColumn(
                new MetaColumn("nativeBaseTypeArrayIndex", "Native Base Type Array Index", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeTypes.nativeBaseTypeArrayIndex)
            );
            table.CreateTable(kPrefixTableName + "NativeType", kPrefixTableDisplayName + "Native Type");
            AddTable(table, tables);
        }

        private void CreateTable_NativeAllocations(List<APITable> tables)
        {
            APITable table = new APITable(this, m_Snapshot, m_Snapshot.nativeAllocations.dataSet);
            table.AddColumn(
                new MetaColumn("rootReferenceId", "Root Reference Id", new MetaType(typeof(long), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeAllocations.rootReferenceId)
            );
            table.AddColumn(
                new MetaColumn("memoryRegionIndex", "Memory Region Index", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeAllocations.memoryRegionIndex)
            );
            table.AddColumn(
                new MetaColumn("allocationSiteId", "Allocation Site Id", new MetaType(typeof(long), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeAllocations.allocationSiteId)
            );
            table.AddColumn(
                new MetaColumn("address", "Address", new MetaType(typeof(ulong), DataMatchMethod.AsNumber), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeAllocations.address)
            );
            table.AddColumn(
                new MetaColumn("size", "Size", new MetaType(typeof(ulong), DataMatchMethod.AsNumber), true, Grouping.groupByDuplicate
                    , Grouping.GetMergeAlgo(Grouping.MergeAlgo.sum, typeof(ulong)), "size")
                , DataArray.MakeColumn(m_Snapshot.nativeAllocations.size)
            );
            table.AddColumn(
                new MetaColumn("overheadSize", "Overhead Size", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate
                    , Grouping.GetMergeAlgo(Grouping.MergeAlgo.sum, typeof(int)), "size")
                , DataArray.MakeColumn(m_Snapshot.nativeAllocations.overheadSize)
            );
            table.AddColumn(
                new MetaColumn("paddingSize", "Padding Size", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate
                    , Grouping.GetMergeAlgo(Grouping.MergeAlgo.sum, typeof(int)), "size")
                , DataArray.MakeColumn(m_Snapshot.nativeAllocations.paddingSize)
            );
            table.CreateTable(kPrefixTableName + "NativeAllocation", kPrefixTableDisplayName + "Native Allocation");
            AddTable(table, tables);
        }

        private void CreateTable_NativeConnections(List<APITable> tables)
        {
            APITable table = new APITable(this, m_Snapshot, m_Snapshot.connections.dataSet);
            table.AddColumn(
                new MetaColumn("from", "From", new MetaType(typeof(int), DataMatchMethod.AsNumber), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.connections.from)
            );
            table.AddColumn(
                new MetaColumn("to", "To", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.connections.to)
            );
            table.CreateTable(kPrefixTableName + "NativeConnection", kPrefixTableDisplayName + "Native Connection");
            AddTable(table, tables);
        }

        private void CreateTable_TypeDescriptions(List<APITable> tables)
        {
            APITable table = new APITable(this, m_Snapshot, m_Snapshot.typeDescriptions.dataSet);


            table.AddColumn(
                new MetaColumn("name", "Name", new MetaType(typeof(string)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.typeDescriptions.typeDescriptionName)
            );
            table.AddColumn(
                new MetaColumn("assembly", "Assembly", new MetaType(typeof(string)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.typeDescriptions.assembly)
            );

            table.AddColumn(
                new MetaColumn("isValueType", "Value Type", new MetaType(typeof(bool)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn_Transform(m_Snapshot.typeDescriptions.flags, (a) => HasBit(a, TypeFlags.kValueType), (ref TypeFlags o, bool v) => SetBit(ref o, TypeFlags.kValueType, v))
            );
            table.AddColumn(
                new MetaColumn("isArray", "Array", new MetaType(typeof(bool)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn_Transform(m_Snapshot.typeDescriptions.flags, (a) => HasBit(a, TypeFlags.kArray), (ref TypeFlags o, bool v) => SetBit(ref o, TypeFlags.kArray, v))
            );

            table.AddColumn(
                new MetaColumn("arrayRank", "Array Rank", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn_Transform(m_Snapshot.typeDescriptions.flags, (a) => GetBits(a, TypeFlags.kArrayRankMask, 16), (ref TypeFlags o, int v) => SetBits(ref o, TypeFlags.kArrayRankMask, 16, v))
            );
            table.AddColumn(
                new MetaColumn("baseOrElementTypeIndex", "Base Or Element Type Index", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.typeDescriptions.baseOrElementTypeIndex)
            );
            table.AddColumn(
                new MetaColumn("size", "Size", new MetaType(typeof(int), DataMatchMethod.AsNumber), true, Grouping.groupByDuplicate
                    , Grouping.GetMergeAlgo(Grouping.MergeAlgo.sum, typeof(int)), "size")
                , DataArray.MakeColumn(m_Snapshot.typeDescriptions.size)
            );
            table.AddColumn(
                new MetaColumn("typeInfoAddress", "Type Info Address", new MetaType(typeof(ulong), DataMatchMethod.AsNumber), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.typeDescriptions.typeInfoAddress)
            );
            table.AddColumn(
                new MetaColumn("typeIndex", "Type Index", new MetaType(typeof(int), DataMatchMethod.AsNumber), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.typeDescriptions.typeIndex)
            );
            table.CreateTable(kPrefixTableName + "ManagedType", kPrefixTableDisplayName + "Managed Type");
            AddTable(table, tables);
        }

        private void CreateNativeTable_NativeTypeBase(List<APITable> tables)
        {
            m_TypeBase = new TypeBase(m_Snapshot);
            APITable table = new APITable(this, m_Snapshot, m_TypeBase.TypeIndex.LongLength);

            table.AddColumn(
                new MetaColumn("typeIndex", "Type Index", new MetaType(typeof(int), DataMatchMethod.AsNumber), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_TypeBase.TypeIndex)
            );
            table.AddColumn(
                new MetaColumn("baseIndex", "Base Index", new MetaType(typeof(int), DataMatchMethod.AsNumber), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_TypeBase.BaseIndex)
            );
            table.CreateTable(kPrefixTableName + "NativeTypeBase", kPrefixTableDisplayName + "Native Type Base");
            AddTable(table, tables);
        }

        private void AddTable(APITable t, List<APITable> tables)
        {
            m_TablesByName.Add(t.GetName(), t);
            tables.Add(t);
        }
    }
}

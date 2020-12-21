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
            CreateAllObjectTables(crawledData);
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
                , DataArray.MakeColumn(m_Snapshot.nativeRootReferences.id, false)
            );
            table.AddColumn(
                new MetaColumn("areaName", "Area Name", new MetaType(typeof(string)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeRootReferences.areaName, false)
            );
            table.AddColumn(
                new MetaColumn("objectName", "Object Name", new MetaType(typeof(string)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeRootReferences.objectName, false)
            );
            table.AddColumn(
                new MetaColumn("accumulatedSize", "Accumulated Size", new MetaType(typeof(ulong), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate
                    , Grouping.GetMergeAlgo(Grouping.MergeAlgo.sum, typeof(ulong)), "size")
                , DataArray.MakeColumn(m_Snapshot.nativeRootReferences.accumulatedSize, false)
            );
            table.CreateTable(kPrefixTableName + "RootReference", kPrefixTableDisplayName + "Root Reference");
            table.NoDataMessage = "No Native Root References because they were not captured in this snapshot. Select the Native Allocations option in the drop-down of the Capture button or via CaptureFlags when using the API to take a capture.";

            AddTable(table, tables);
        }

        private void CreateTable_NativeAllocationSites(List<APITable> tables)
        {
            APITable table = new APITable(this, m_Snapshot, m_Snapshot.nativeAllocationSites.dataSet);
            table.AddColumn(
                new MetaColumn("id", "Id", new MetaType(typeof(long), DataMatchMethod.AsNumber), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeAllocationSites.id, false)
            );
            table.AddColumn(
                new MetaColumn("memoryLabelIndex", "Memory Label Index", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeAllocationSites.memoryLabelIndex, false)
            );

            table.CreateTable(kPrefixTableName + "NativeAllocationSite", kPrefixTableDisplayName + "Native Allocation Site");
            table.NoDataMessage = "No Native Allocation Sites because they were not captured in this snapshot. Native Allocation Site collection currently unavailable.";

            AddTable(table, tables);
        }

        private void CreateTable_NativeCallstackSymbols(List<APITable> tables)
        {
            APITable table = new APITable(this, m_Snapshot, m_Snapshot.nativeCallstackSymbols.dataSet);
            table.AddColumn(
                new MetaColumn("symbol", "Symbol", new MetaType(typeof(ulong), DataMatchMethod.AsNumber), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeCallstackSymbols.symbol, false)
            );
            table.AddColumn(
                new MetaColumn("readableStackTrace", "Readable Stack Trace", new MetaType(typeof(string)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeCallstackSymbols.readableStackTrace, false)
            );
            table.CreateTable(kPrefixTableName + "NativeCallstackSymbol", kPrefixTableDisplayName + "Native Callstack Symbol");
            table.NoDataMessage = "No Native Callstack Symbols because they were not captured in this snapshot. Native Callstack collection currently unavailable.";

            AddTable(table, tables);
        }

        private void CreateTable_NativeMemoryLabels(List<APITable> tables)
        {
            APITable table = new APITable(this, m_Snapshot, m_Snapshot.nativeMemoryLabels.dataSet);
            table.AddColumn(
                new MetaColumn("name", "Name", new MetaType(typeof(string)), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeMemoryLabels.memoryLabelName, false)
            );

            //todo: add column for label size
            table.CreateTable(kPrefixTableName + "NativeMemoryLabel", kPrefixTableDisplayName + "Native Memory Label");
            table.NoDataMessage = "No Native Memory Labels because they were not captured in this snapshot. Select the Native Allocations option in the drop-down of the Capture button or via CaptureFlags when using the API to take a capture.";

            AddTable(table, tables);
        }

        private void CreateTable_NativeMemoryRegions(List<APITable> tables)
        {
            APITable table = new APITable(this, m_Snapshot, m_Snapshot.nativeMemoryRegions.dataSet);
            table.AddColumn(
                new MetaColumn("parentIndex", "Parent Index", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeMemoryRegions.parentIndex, false)
            );
            table.AddColumn(
                new MetaColumn("name", "Name", new MetaType(typeof(string)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeMemoryRegions.memoryRegionName, false)
            );
            table.AddColumn(
                new MetaColumn("addressBase", "Address Base", new MetaType(typeof(ulong), DataMatchMethod.AsString), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeMemoryRegions.addressBase, true)
            );
            table.AddColumn(
                new MetaColumn("addressSize", "Address Size", new MetaType(typeof(ulong), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate
                    , Grouping.GetMergeAlgo(Grouping.MergeAlgo.sum, typeof(ulong)), "size")
                , DataArray.MakeColumn(m_Snapshot.nativeMemoryRegions.addressSize, false)
            );
            table.AddColumn(
                new MetaColumn("firstAllocationIndex", "First Allocation Index", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeMemoryRegions.firstAllocationIndex, false)
            );
            table.AddColumn(
                new MetaColumn("numAllocations", "Number Of Allocations", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate
                    , Grouping.GetMergeAlgo(Grouping.MergeAlgo.sum, typeof(int)))
                , DataArray.MakeColumn(m_Snapshot.nativeMemoryRegions.numAllocations, false)
            );
            table.CreateTable(kPrefixTableName + "NativeMemoryRegion", kPrefixTableDisplayName + "Native Memory Region");
            table.NoDataMessage = "No Native Memory Regions because they were not captured in this snapshot. Select the Native Allocations option in the drop-down of the Capture button or via CaptureFlags when using the API to take a capture.";

            AddTable(table, tables);
        }

        private void CreateTable_NativeObjects(List<APITable> tables)
        {
            APITable table = new APITable(this, m_Snapshot, m_Snapshot.nativeObjects.dataSet);
            table.AddColumn(
                new MetaColumn("name", "Name", new MetaType(typeof(string)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeObjects.objectName, false)
            );
            table.AddColumn(
                new MetaColumn("instanceId", "Instance Id", new MetaType(typeof(int), DataMatchMethod.AsNumber), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeObjects.instanceId, false)
            );
            table.AddColumn(
                new MetaColumn("size", "Size", new MetaType(typeof(ulong), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate
                    , Grouping.GetMergeAlgo(Grouping.MergeAlgo.sum, typeof(ulong)), "size")
                , DataArray.MakeColumn(m_Snapshot.nativeObjects.size, false)
            );

            table.AddColumn(
                new MetaColumn("nativeObjectAddress", "Native Object Address", new MetaType(typeof(ulong), DataMatchMethod.AsString), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeObjects.nativeObjectAddress, true)
            );
            table.AddColumn(
                new MetaColumn("rootReferenceId", "Root Reference Id", new MetaType(typeof(long), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeObjects.rootReferenceId, false)
            );

            table.AddColumn(
                new MetaColumn("nativeTypeArrayIndex", "Native Type Array Index", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeObjects.nativeTypeArrayIndex, false)
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
            table.NoDataMessage = "No Native Objects because they were not captured in this snapshot. Select the Native Objects option in the drop-down of the Capture button or via CaptureFlags when using the API to take a capture.";

            AddTable(table, tables);
        }

        private void CreateTable_NativeTypes(List<APITable> tables)
        {
            APITable table = new APITable(this, m_Snapshot, m_Snapshot.nativeTypes.dataSet);
            table.AddColumn(
                new MetaColumn("name", "Name", new MetaType(typeof(string)), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeTypes.typeName, false)
            );
            table.AddColumn(
                new MetaColumn("nativeBaseTypeArrayIndex", "Native Base Type Array Index", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeTypes.nativeBaseTypeArrayIndex, false)
            );
            table.CreateTable(kPrefixTableName + "NativeType", kPrefixTableDisplayName + "Native Type");
            table.NoDataMessage = "No Native Types because they were not captured in this snapshot. Select the Native Objects option in the drop-down of the Capture button or via CaptureFlags when using the API to take a capture.";

            AddTable(table, tables);
        }

        private void CreateTable_NativeAllocations(List<APITable> tables)
        {
            APITable table = new APITable(this, m_Snapshot, m_Snapshot.nativeAllocations.dataSet);
            table.AddColumn(
                new MetaColumn("rootReferenceId", "Root Reference Id", new MetaType(typeof(long), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeAllocations.rootReferenceId, false)
            );
            table.AddColumn(
                new MetaColumn("memoryRegionIndex", "Memory Region Index", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeAllocations.memoryRegionIndex, false)
            );
            table.AddColumn(
                new MetaColumn("allocationSiteId", "Allocation Site Id", new MetaType(typeof(long), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeAllocations.allocationSiteId, false)
            );
            table.AddColumn(
                new MetaColumn("address", "Address", new MetaType(typeof(ulong), DataMatchMethod.AsString), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.nativeAllocations.address, true)
            );
            table.AddColumn(
                new MetaColumn("size", "Size", new MetaType(typeof(ulong), DataMatchMethod.AsNumber), true, Grouping.groupByDuplicate
                    , Grouping.GetMergeAlgo(Grouping.MergeAlgo.sum, typeof(ulong)), "size")
                , DataArray.MakeColumn(m_Snapshot.nativeAllocations.size, false)
            );
            table.AddColumn(
                new MetaColumn("overheadSize", "Overhead Size", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate
                    , Grouping.GetMergeAlgo(Grouping.MergeAlgo.sum, typeof(int)), "size")
                , DataArray.MakeColumn(m_Snapshot.nativeAllocations.overheadSize, false)
            );
            table.AddColumn(
                new MetaColumn("paddingSize", "Padding Size", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate
                    , Grouping.GetMergeAlgo(Grouping.MergeAlgo.sum, typeof(int)), "size")
                , DataArray.MakeColumn(m_Snapshot.nativeAllocations.paddingSize, false)
            );
            table.CreateTable(kPrefixTableName + "NativeAllocation", kPrefixTableDisplayName + "Native Allocation");
            table.NoDataMessage = "No Native Allocations because they were not captured in this snapshot. Select the Native Allocations option in the drop-down of the Capture button or via CaptureFlags when using the API to take a capture.";

            AddTable(table, tables);
        }

        private void CreateTable_NativeConnections(List<APITable> tables)
        {
            APITable table = new APITable(this, m_Snapshot, m_Snapshot.connections.dataSet);
            table.AddColumn(
                new MetaColumn("from", "From", new MetaType(typeof(int), DataMatchMethod.AsNumber), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.connections.from, false)
            );
            table.AddColumn(
                new MetaColumn("to", "To", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.connections.to, false)
            );
            table.CreateTable(kPrefixTableName + "NativeConnection", kPrefixTableDisplayName + "Native Connection");
            table.NoDataMessage = "No Native Connections because they were not captured in this snapshot. Select the Native Objects option in the drop-down of the Capture button or via CaptureFlags when using the API to take a capture.";

            AddTable(table, tables);
        }

        private void CreateTable_TypeDescriptions(List<APITable> tables)
        {
            APITable table = new APITable(this, m_Snapshot, m_Snapshot.typeDescriptions.dataSet);


            table.AddColumn(
                new MetaColumn("name", "Name", new MetaType(typeof(string)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.typeDescriptions.typeDescriptionName, false)
            );
            table.AddColumn(
                new MetaColumn("assembly", "Assembly", new MetaType(typeof(string)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.typeDescriptions.assembly, false)
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
                , DataArray.MakeColumn(m_Snapshot.typeDescriptions.baseOrElementTypeIndex, false)
            );
            table.AddColumn(
                new MetaColumn("size", "Size", new MetaType(typeof(int), DataMatchMethod.AsNumber), true, Grouping.groupByDuplicate
                    , Grouping.GetMergeAlgo(Grouping.MergeAlgo.sum, typeof(int)), "size")
                , DataArray.MakeColumn(m_Snapshot.typeDescriptions.size, false)
            );
            table.AddColumn(
                new MetaColumn("typeInfoAddress", "Type Info Address", new MetaType(typeof(ulong), DataMatchMethod.AsString), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.typeDescriptions.typeInfoAddress, true)
            );
            table.AddColumn(
                new MetaColumn("typeIndex", "Type Index", new MetaType(typeof(int), DataMatchMethod.AsNumber), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn(m_Snapshot.typeDescriptions.typeIndex, false)
            );
            table.CreateTable(kPrefixTableName + "ManagedType", kPrefixTableDisplayName + "Managed Type");
            table.NoDataMessage = "No Managed Types because they were not captured in this snapshot. Select the Managed Objects option in the drop-down of the Capture button or via CaptureFlags when using the API to take a capture.";

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
            table.NoDataMessage = "No Native Type Bases because they were not captured in this snapshot. Select the Native Objects option in the drop-down of the Capture button or via CaptureFlags when using the API to take a capture.";

            AddTable(table, tables);
        }

        private void CreateAllObjectTables(ManagedData crawledData)
        {
            var allManaged = new ObjectAllManagedTable(this, formatter, m_Snapshot, crawledData, ObjectTable.ObjectMetaType.Managed);
            allManaged.NoDataMessage = "No Managed Objects because they were not captured in this snapshot. Select the Managed Objects option in the drop-down of the Capture button or via CaptureFlags when using the API to take a capture.";
            m_TablesByName.Add(allManaged.GetName(), allManaged);

            var allNative = new ObjectAllNativeTable(this, formatter, m_Snapshot, crawledData, ObjectTable.ObjectMetaType.Native);
            allNative.NoDataMessage = "No Native Objects because they were not captured in this snapshot. Select the Native Objects option in the drop-down of the Capture button or via CaptureFlags when using the API to take a capture.";
            m_TablesByName.Add(allNative.GetName(), allNative);

            var allObjects = new ObjectAllTable(this, formatter, m_Snapshot, crawledData, ObjectTable.ObjectMetaType.All);
            allObjects.NoDataMessage = "No Managed or Native Objects because they were not captured in this snapshot. Select the Managed and Native Objects options in the drop-down of the Capture button or via CaptureFlags when using the API to take a capture.";
            m_TablesByName.Add(allObjects.GetName(), allObjects);

            m_ExtraTable = new Table[3];
            m_ExtraTable[0] = allManaged;
            m_ExtraTable[1] = allNative;
            m_ExtraTable[2] = allObjects;
        }

        private void AddTable(APITable t, List<APITable> tables)
        {
            m_TablesByName.Add(t.GetName(), t);
            tables.Add(t);
        }
    }
}

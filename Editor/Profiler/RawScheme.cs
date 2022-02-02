//#define ALL_ALLOCATIONS_TABLE_DEBUG
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
                for (int i = 0; i != snapshot.NativeTypes.Count; ++i)
                {
                    var currentBase = snapshot.NativeTypes.NativeBaseTypeArrayIndex[i];
                    while (currentBase >= 0)
                    {
                        typeIndex.Add(i);
                        baseIndex.Add(currentBase);
                        currentBase = snapshot.NativeTypes.NativeBaseTypeArrayIndex[currentBase];
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
            CreateTable_NativeAllocationDetails(tables);
            if (m_Snapshot.NativeAllocationSites.Count > 0)
                CreateTable_NativeAllocationSites(tables);
            if (m_Snapshot.NativeCallstackSymbols.Count > 0)
                CreateTable_NativeCallstackSymbols(tables);
            CreateTable_NativeMemoryLabels(tables);
            CreateTable_NativeMemoryRegions(tables);
            CreateTable_NativeAndManagedMemoryRegions(tables);
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
            if (m_Snapshot != null)
            {
                m_Snapshot.Dispose();
                m_Snapshot = null;
                formatter.Clear();
            }
        }

        public override string GetDisplayName()
        {
            return m_Snapshot.TimeStamp.ToString();
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

        private bool TryGetParam(ParameterSet param, string name, out long value)
        {
            if (param == null)
            {
                value = default(long);
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
                TypedExpression<long> e = (TypedExpression<long>)expObj;
                value = e.GetValue(0);
            }
            else
            {
                if (!long.TryParse(expObj.GetValueString(0, DefaultDataFormatter.Instance), out value))
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
                long objUnifiedIndex;
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
            APITable table = new APITable(this, m_Snapshot, m_Snapshot.NativeRootReferences.Count);
            table.AddColumn(
                new MetaColumn("id", "Id", new MetaType(typeof(long), DataMatchMethod.AsNumber), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeRootReferences.Id, false)
            );
            table.AddColumn(
                new MetaColumn("areaName", "Area Name", new MetaType(typeof(string)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnManaged(m_Snapshot.NativeRootReferences.AreaName, false)
            );
            table.AddColumn(
                new MetaColumn("objectName", "Object Name", new MetaType(typeof(string)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnManaged(m_Snapshot.NativeRootReferences.ObjectName, false)
            );
            table.AddColumn(
                new MetaColumn("accumulatedSize", "Accumulated Size", new MetaType(typeof(ulong), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate
                    , Grouping.GetMergeAlgo(Grouping.MergeAlgo.sum, typeof(ulong)), "size")
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeRootReferences.AccumulatedSize, false)
            );
            table.CreateTable(kPrefixTableName + "RootReference", kPrefixTableDisplayName + "Root Reference");
            if (m_Snapshot.CaptureFlags.HasFlag(CaptureFlags.NativeAllocations))
                table.NoDataMessage = "The current selection or filtered list does not contain any Native Root References. Try a different selection or filter.";
            else
                table.NoDataMessage = "No Native Root References because they were not captured in this snapshot. Select the Native Allocations option in the drop-down of the Capture button or via CaptureFlags when using the API to take a capture.";

            AddTable(table, tables);
        }

        private void CreateTable_NativeAllocationSites(List<APITable> tables)
        {
            APITable table = new APITable(this, m_Snapshot, m_Snapshot.NativeAllocationSites.Count);
            table.AddColumn(
                new MetaColumn("id", "Id", new MetaType(typeof(long), DataMatchMethod.AsNumber), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeAllocationSites.id, false)
            );
            table.AddColumn(
                new MetaColumn("memoryLabelIndex", "Memory Label Index", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeAllocationSites.memoryLabelIndex, false)
            );

            table.CreateTable(kPrefixTableName + "NativeAllocationSite", kPrefixTableDisplayName + "Native Allocation Site");
            //if (m_Snapshot.CaptureFlags.HasFlag(CaptureFlags.NativeAllocationSites))
            //    table.NoDataMessage = "The current selection or filtered list does not contain any Native Allocation Sites. Try a different selection or filter.";
            //else
            table.NoDataMessage = "No Native Allocation Sites because they were not captured in this snapshot. Native Allocation Site collection currently unavailable.";

            AddTable(table, tables);
        }

        private void CreateTable_NativeCallstackSymbols(List<APITable> tables)
        {
            APITable table = new APITable(this, m_Snapshot, m_Snapshot.NativeCallstackSymbols.Count);
            table.AddColumn(
                new MetaColumn("symbol", "Symbol", new MetaType(typeof(ulong), DataMatchMethod.AsNumber), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeCallstackSymbols.Symbol, false)
            );
            table.AddColumn(
                new MetaColumn("readableStackTrace", "Readable Stack Trace", new MetaType(typeof(string)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnManaged(m_Snapshot.NativeCallstackSymbols.ReadableStackTrace, false)
            );
            table.CreateTable(kPrefixTableName + "NativeCallstackSymbol", kPrefixTableDisplayName + "Native Callstack Symbol");
            //if (m_Snapshot.CaptureFlags.HasFlag(CaptureFlags.NativeStackTraces))
            //    table.NoDataMessage = "The current selection or filtered list does not contain any Native Callstack Symbols. Try a different selection or filter.";
            //else
            table.NoDataMessage = "No Native Callstack Symbols because they were not captured in this snapshot. Native Callstack collection currently unavailable.";

            AddTable(table, tables);
        }

        private void CreateTable_NativeMemoryLabels(List<APITable> tables)
        {
            APITable table = new APITable(this, m_Snapshot, m_Snapshot.NativeMemoryLabels.Count);
            table.AddColumn(
                new MetaColumn("name", "Name", new MetaType(typeof(string)), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnManaged(m_Snapshot.NativeMemoryLabels.MemoryLabelName, false)
            );

            //todo: add column for label size
            table.CreateTable(kPrefixTableName + "NativeMemoryLabel", kPrefixTableDisplayName + "Native Memory Label");
            if (m_Snapshot.CaptureFlags.HasFlag(CaptureFlags.NativeAllocations))
                table.NoDataMessage = "The current selection or filtered list does not contain any Native Memory Labels. Try a different selection or filter.";
            else
                table.NoDataMessage = "No Native Memory Labels because they were not captured in this snapshot. Select the Native Allocations option in the drop-down of the Capture button or via CaptureFlags when using the API to take a capture.";

            AddTable(table, tables);
        }

        private void CreateTable_NativeMemoryRegions(List<APITable> tables)
        {
            APITable table = new APITable(this, m_Snapshot, m_Snapshot.NativeMemoryRegions.Count);
            table.AddColumn(
                new MetaColumn("parentIndex", "Parent Index", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeMemoryRegions.ParentIndex, false)
            );
            table.AddColumn(
                new MetaColumn("name", "Name", new MetaType(typeof(string)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnManaged(m_Snapshot.NativeMemoryRegions.MemoryRegionName, false)
            );
            table.AddColumn(
                new MetaColumn("addressBase", "Address Base", new MetaType(typeof(ulong), DataMatchMethod.AsString), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeMemoryRegions.AddressBase, true)
            );
            table.AddColumn(
                new MetaColumn("addressSize", "Address Size", new MetaType(typeof(ulong), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate
                    , Grouping.GetMergeAlgo(Grouping.MergeAlgo.sum, typeof(ulong)), "size")
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeMemoryRegions.AddressSize, false)
            );
            table.AddColumn(
                new MetaColumn("firstAllocationIndex", "First Allocation Index", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeMemoryRegions.FirstAllocationIndex, false)
            );
            table.AddColumn(
                new MetaColumn("numAllocations", "Number Of Allocations", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate
                    , Grouping.GetMergeAlgo(Grouping.MergeAlgo.sum, typeof(int)))
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeMemoryRegions.NumAllocations, false)
            );
            table.CreateTable(kPrefixTableName + "NativeMemoryRegions", kPrefixTableDisplayName + "Native Memory Regions");

            if (m_Snapshot.CaptureFlags.HasFlag(CaptureFlags.NativeAllocations))
                table.NoDataMessage = "The current selection or filtered list does not contain any Native Memory Regions. Try a different selection or filter.";
            else
                table.NoDataMessage = "No Native Memory Regions because they were not captured in this snapshot. Select the Native Allocations option in the drop-down of the Capture button or via CaptureFlags when using the API to take a capture.";

            AddTable(table, tables);
        }

        private void CreateTable_NativeAndManagedMemoryRegions(List<APITable> tables)
        {
            long nativeCount = m_Snapshot.NativeMemoryRegions.Count;
            long managedeCount = m_Snapshot.ManagedHeapSections.Count;
            APITable table = new APITable(this, m_Snapshot, nativeCount + managedeCount);
            table.AddColumn(
                new MetaColumn("parentIndex", "Parent Index", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeMemoryRegions.ParentIndex, nativeCount, default(Containers.DynamicArray<int>), managedeCount, false)
            );
            table.AddColumn(
                new MetaColumn("name", "Name", new MetaType(typeof(string)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnManaged(m_Snapshot.NativeMemoryRegions.MemoryRegionName, nativeCount, m_Snapshot.ManagedHeapSections.SectionName, managedeCount, false)
            );
            table.AddColumn(
                new MetaColumn("addressBase", "Address Base", new MetaType(typeof(ulong), DataMatchMethod.AsString), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeMemoryRegions.AddressBase, nativeCount, m_Snapshot.ManagedHeapSections.StartAddress, managedeCount, true)
            );
            table.AddColumn(
                new MetaColumn("addressSize", "Address Size", new MetaType(typeof(ulong), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate
                    , Grouping.GetMergeAlgo(Grouping.MergeAlgo.sum, typeof(ulong)), "size")
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeMemoryRegions.AddressSize, nativeCount, m_Snapshot.ManagedHeapSections.SectionSize, managedeCount, false)
            );
            table.AddColumn(
                new MetaColumn("firstAllocationIndex", "First Allocation Index", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeMemoryRegions.FirstAllocationIndex, nativeCount, default(Containers.DynamicArray<int>), managedeCount, false)
            );
            table.AddColumn(
                new MetaColumn("numAllocations", "Number Of Allocations", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate
                    , Grouping.GetMergeAlgo(Grouping.MergeAlgo.sum, typeof(int)))
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeMemoryRegions.NumAllocations, nativeCount, default(Containers.DynamicArray<int>), managedeCount, false)
            );
            table.CreateTable(kPrefixTableName + "AllMemoryRegions", kPrefixTableDisplayName + "All Memory Regions");

            if (m_Snapshot.CaptureFlags.HasFlag(CaptureFlags.NativeAllocations))
                table.NoDataMessage = "The current selection or filtered list does not contain any Memory Regions. Try a different selection or filter.";
            else
                table.NoDataMessage = "The current selection or filtered list does not contain any Managed Memory Regions. Try a different selection or filter. Native Memory Regions weren't in this snapshot. Select the Native Allocations option in the drop-down of the Capture button or via CaptureFlags when using the API to take a capture.";

            AddTable(table, tables);
        }

        private void CreateTable_NativeObjects(List<APITable> tables)
        {
            APITable table = new APITable(this, m_Snapshot, m_Snapshot.NativeObjects.Count);
            table.AddColumn(
                new MetaColumn("name", "Name", new MetaType(typeof(string)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnManaged(m_Snapshot.NativeObjects.ObjectName, false)
            );
            table.AddColumn(
                new MetaColumn("instanceId", "Instance Id", new MetaType(typeof(int), DataMatchMethod.AsNumber), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeObjects.InstanceId, false)
            );
            table.AddColumn(
                new MetaColumn("size", "Size", new MetaType(typeof(ulong), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate
                    , Grouping.GetMergeAlgo(Grouping.MergeAlgo.sum, typeof(ulong)), "size")
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeObjects.Size, false)
            );

            table.AddColumn(
                new MetaColumn("nativeObjectAddress", "Native Object Address", new MetaType(typeof(ulong), DataMatchMethod.AsString), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeObjects.NativeObjectAddress, true)
            );
            table.AddColumn(
                new MetaColumn("rootReferenceId", "Root Reference Id", new MetaType(typeof(long), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeObjects.RootReferenceId, false)
            );

            table.AddColumn(
                new MetaColumn("nativeTypeArrayIndex", "Native Type Array Index", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeObjects.NativeTypeArrayIndex, false)
            );

            table.AddColumn(
                new MetaColumn("isPersistent", "Persistent", new MetaType(typeof(bool)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn_Transform(m_Snapshot.NativeObjects.Flags, (a) => HasBit(a, ObjectFlags.IsPersistent) , (ref ObjectFlags o, bool v) => SetBit(ref o, ObjectFlags.IsPersistent, v))
            );
            table.AddColumn(
                new MetaColumn("isDontDestroyOnLoad", "Don't Destroy On Load", new MetaType(typeof(bool)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn_Transform(m_Snapshot.NativeObjects.Flags, (a) => HasBit(a, ObjectFlags.IsDontDestroyOnLoad), (ref ObjectFlags o, bool v) => SetBit(ref o, ObjectFlags.IsDontDestroyOnLoad, v))
            );
            table.AddColumn(
                new MetaColumn("isManager", "Manager", new MetaType(typeof(bool)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn_Transform(m_Snapshot.NativeObjects.Flags, (a) => HasBit(a, ObjectFlags.IsManager), (ref ObjectFlags o, bool v) => SetBit(ref o, ObjectFlags.IsManager, v))
            );

            table.AddColumn(
                new MetaColumn("HideInHierarchy", "Hide In Hierarchy", new MetaType(typeof(bool)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn_Transform(m_Snapshot.NativeObjects.HideFlags, (a) => HasBit(a, HideFlags.HideInHierarchy), (ref HideFlags o, bool v) => SetBit(ref o, HideFlags.HideInHierarchy, v))
            );
            table.AddColumn(
                new MetaColumn("HideInInspector", "Hide In Inspector", new MetaType(typeof(bool)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn_Transform(m_Snapshot.NativeObjects.HideFlags, (a) => HasBit(a, HideFlags.HideInInspector), (ref HideFlags o, bool v) => SetBit(ref o, HideFlags.HideInInspector, v))
            );
            table.AddColumn(
                new MetaColumn("DontSaveInEditor", "Don't Save In Editor", new MetaType(typeof(bool)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn_Transform(m_Snapshot.NativeObjects.HideFlags, (a) => HasBit(a, HideFlags.DontSaveInEditor), (ref HideFlags o, bool v) => SetBit(ref o, HideFlags.DontSaveInEditor, v))
            );
            table.AddColumn(
                new MetaColumn("NotEditable", "Not Editable", new MetaType(typeof(bool)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn_Transform(m_Snapshot.NativeObjects.HideFlags, (a) => HasBit(a, HideFlags.NotEditable), (ref HideFlags o, bool v) => SetBit(ref o, HideFlags.NotEditable, v))
            );
            table.AddColumn(
                new MetaColumn("DontSaveInBuild", "Don't Save In Build", new MetaType(typeof(bool)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn_Transform(m_Snapshot.NativeObjects.HideFlags, (a) => HasBit(a, HideFlags.DontSaveInBuild), (ref HideFlags o, bool v) => SetBit(ref o, HideFlags.DontSaveInBuild, v))
            );
            table.AddColumn(
                new MetaColumn("DontUnloadUnusedAsset", "Don't Unload Unused Asset", new MetaType(typeof(bool)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn_Transform(m_Snapshot.NativeObjects.HideFlags, (a) => HasBit(a, HideFlags.DontUnloadUnusedAsset), (ref HideFlags o, bool v) => SetBit(ref o, HideFlags.DontUnloadUnusedAsset, v))
            );

            table.CreateTable(kPrefixTableName + "NativeObject", kPrefixTableDisplayName + "Native Object");
            if (m_Snapshot.CaptureFlags.HasFlag(CaptureFlags.NativeObjects))
                table.NoDataMessage = "The current selection or filtered list does not contain any Native Objects. Try a different selection or filter.";
            else
                table.NoDataMessage = "No Native Objects because they were not captured in this snapshot. Select the Native Objects option in the drop-down of the Capture button or via CaptureFlags when using the API to take a capture.";

            AddTable(table, tables);
        }

        private void CreateTable_NativeTypes(List<APITable> tables)
        {
            APITable table = new APITable(this, m_Snapshot, m_Snapshot.NativeTypes.Count);
            table.AddColumn(
                new MetaColumn("name", "Name", new MetaType(typeof(string)), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnManaged(m_Snapshot.NativeTypes.TypeName, false)
            );
            table.AddColumn(
                new MetaColumn("nativeBaseTypeArrayIndex", "Native Base Type Array Index", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeTypes.NativeBaseTypeArrayIndex, false)
            );
            table.CreateTable(kPrefixTableName + "NativeType", kPrefixTableDisplayName + "Native Type");
            if (m_Snapshot.CaptureFlags.HasFlag(CaptureFlags.NativeObjects))
                table.NoDataMessage = "The current selection or filtered list does not contain any Native Types. Try a different selection or filter.";
            else
                table.NoDataMessage = "No Native Types because they were not captured in this snapshot. Select the Native Objects option in the drop-down of the Capture button or via CaptureFlags when using the API to take a capture.";

            AddTable(table, tables);
        }

        private void CreateTable_NativeAllocations(List<APITable> tables)
        {
            APITable table = new APITable(this, m_Snapshot, m_Snapshot.NativeAllocations.Count);
            table.AddColumn(
                new MetaColumn("rootReferenceId", "Root Reference Id", new MetaType(typeof(long), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeAllocations.RootReferenceId, false)
            );
            table.AddColumn(
                new MetaColumn("memoryRegionIndex", "Memory Region Index", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeAllocations.MemoryRegionIndex, false)
            );
            if (m_Snapshot.NativeAllocations.AllocationSiteId.Count > 0)
            {
                // no point in having that column if we don't have any allocation sides captured
                table.AddColumn(
                    new MetaColumn("allocationSiteId", "Allocation Site Id", new MetaType(typeof(long), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                    , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeAllocations.AllocationSiteId, false));
            }
            table.AddColumn(
                new MetaColumn("address", "Address", new MetaType(typeof(ulong), DataMatchMethod.AsString), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeAllocations.Address, true)
            );
            table.AddColumn(
                new MetaColumn("size", "Size", new MetaType(typeof(ulong), DataMatchMethod.AsNumber), true, Grouping.groupByDuplicate
                    , Grouping.GetMergeAlgo(Grouping.MergeAlgo.sum, typeof(ulong)), "size")
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeAllocations.Size, false)
            );
            table.AddColumn(
                new MetaColumn("overheadSize", "Overhead Size", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate
                    , Grouping.GetMergeAlgo(Grouping.MergeAlgo.sum, typeof(int)), "size")
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeAllocations.OverheadSize, false)
            );
            table.AddColumn(
                new MetaColumn("paddingSize", "Padding Size", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate
                    , Grouping.GetMergeAlgo(Grouping.MergeAlgo.sum, typeof(int)), "size")
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeAllocations.PaddingSize, false)
            );
            table.CreateTable(kPrefixTableName + "NativeAllocation", kPrefixTableDisplayName + "Native Allocation");
            if (m_Snapshot.CaptureFlags.HasFlag(CaptureFlags.NativeAllocations))
                table.NoDataMessage = "The current selection or filtered list does not contain any Native Allocations. Try a different selection or filter.";
            else
                table.NoDataMessage = "No Native Allocations because they were not captured in this snapshot. Select the Native Allocations option in the drop-down of the Capture button or via CaptureFlags when using the API to take a capture.";

            AddTable(table, tables);
        }

        private void CreateTable_NativeAllocationDetails(List<APITable> tables)
        {
            APITable table = new APITable(this, m_Snapshot, m_Snapshot.NativeAllocations.Count);
#if ALL_ALLOCATIONS_TABLE_DEBUG
            table.AddColumn(
                new MetaColumn("memoryRegionIndex", "Memory Region Index", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeAllocations.MemoryRegionIndex, false)
            );
#endif
            table.AddColumn(
                new MetaColumn("memoryRegionName", "Memory Region Name", new MetaType(typeof(string), DataMatchMethod.AsString), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn_Transform(m_Snapshot.NativeAllocations.MemoryRegionIndex, (idx) =>
                {
                    if (idx >= 0 && idx < m_Snapshot.NativeMemoryRegions.Count)
                        return m_Snapshot.NativeMemoryRegions.MemoryRegionName[idx];
                    return "Unknown";
                }, (ref int o, string v) =>
                    {
                        for (int i = 0; i < m_Snapshot.NativeMemoryRegions.Count; i++)
                        {
                            if (m_Snapshot.NativeMemoryRegions.MemoryRegionName[i] == v)
                            {
                                o = i;
                                return;
                            }
                        }
                        o = -1;
                    })
            );
#if ALL_ALLOCATIONS_TABLE_DEBUG
            table.AddColumn(
                new MetaColumn("rootReferenceId", "Root Reference Id", new MetaType(typeof(long), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeAllocations.RootReferenceId, false)
            );
#endif
            table.AddColumn(
                new MetaColumn("rootReferenceAreaName", "Root Reference Area Name", new MetaType(typeof(string), DataMatchMethod.AsString), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn_Transform(m_Snapshot.NativeAllocations.RootReferenceId, (id) =>
                {
                    if (id > 0)
                    {
                        long index;
                        if (m_Snapshot.NativeRootReferences.IdToIndex.TryGetValue(id, out index))
                        {
                            return m_Snapshot.NativeRootReferences.AreaName[index];
                        }
                    }
                    return "No Root Area";
                }, (ref long o, string v) =>
                    {
                        for (int i = 0; i < m_Snapshot.NativeRootReferences.Count; i++)
                        {
                            if (m_Snapshot.NativeRootReferences.AreaName[i] == v)
                            {
                                o = m_Snapshot.NativeRootReferences.Id[i];
                                return;
                            }
                        }
                        o = -1;
                    })
            );
            table.AddColumn(
                new MetaColumn("rootReferenceObjectName", "Root Reference Object Name", new MetaType(typeof(string), DataMatchMethod.AsString), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn_Transform(m_Snapshot.NativeAllocations.RootReferenceId, (id) =>
                {
                    if (id > 0)
                    {
                        long index;
                        if (m_Snapshot.NativeRootReferences.IdToIndex.TryGetValue(id, out index))
                        {
                            return m_Snapshot.NativeRootReferences.ObjectName[index];
                        }
                    }
                    return "No Root";
                }, (ref long o, string v) =>
                    {
                        for (int i = 0; i < m_Snapshot.NativeRootReferences.Count; i++)
                        {
                            if (m_Snapshot.NativeRootReferences.ObjectName[i] == v)
                            {
                                o = m_Snapshot.NativeRootReferences.Id[i];
                                return;
                            }
                        }
                        o = -1;
                    })
            );
            if (m_Snapshot.NativeAllocations.AllocationSiteId.Count > 0)
            {
                // no point in having that column if we don't have any allocation sides captured
                table.AddColumn(
                    new MetaColumn("allocationSiteId", "Allocation Site Id", new MetaType(typeof(long), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                    , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeAllocations.AllocationSiteId, false));
            }
            table.AddColumn(
                new MetaColumn("address", "Address", new MetaType(typeof(ulong), DataMatchMethod.AsString), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeAllocations.Address, true)
            );
            table.AddColumn(
                new MetaColumn("size", "Size", new MetaType(typeof(ulong), DataMatchMethod.AsNumber), true, Grouping.groupByDuplicate
                    , Grouping.GetMergeAlgo(Grouping.MergeAlgo.sum, typeof(ulong)), "size")
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeAllocations.Size, false)
            );
            table.AddColumn(
                new MetaColumn("overheadSize", "Overhead Size", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate
                    , Grouping.GetMergeAlgo(Grouping.MergeAlgo.sum, typeof(int)), "size")
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeAllocations.OverheadSize, false)
            );
            table.AddColumn(
                new MetaColumn("paddingSize", "Padding Size", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate
                    , Grouping.GetMergeAlgo(Grouping.MergeAlgo.sum, typeof(int)), "size")
                , DataArray.MakeColumnUnmanaged(m_Snapshot.NativeAllocations.PaddingSize, false)
            );
            table.CreateTable("AllNativeAllocations", "All Native Allocations");
            if (m_Snapshot.CaptureFlags.HasFlag(CaptureFlags.NativeAllocations))
                table.NoDataMessage = "The current selection or filtered list does not contain any Native Allocations. Try a different selection or filter.";
            else
                table.NoDataMessage = "No Native Allocations because they were not captured in this snapshot. Select the Native Allocations option in the drop-down of the Capture button or via CaptureFlags when using the API to take a capture.";

            AddTable(table, tables);
        }

        private void CreateTable_NativeConnections(List<APITable> tables)
        {
            APITable table = new APITable(this, m_Snapshot, m_Snapshot.Connections.Count);
            table.AddColumn(
                new MetaColumn("from", "From", new MetaType(typeof(int), DataMatchMethod.AsNumber), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnUnmanaged(m_Snapshot.Connections.From, false)
            );
            table.AddColumn(
                new MetaColumn("to", "To", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnUnmanaged(m_Snapshot.Connections.To, false)
            );
            table.CreateTable(kPrefixTableName + "NativeConnection", kPrefixTableDisplayName + "Native Connection");
            if (m_Snapshot.CaptureFlags.HasFlag(CaptureFlags.NativeObjects))
                table.NoDataMessage = "The current selection or filtered list does not contain any Native Connections. Try a different selection or filter.";
            else
                table.NoDataMessage = "No Native Connections because they were not captured in this snapshot. Select the Native Objects option in the drop-down of the Capture button or via CaptureFlags when using the API to take a capture.";

            AddTable(table, tables);
        }

        private void CreateTable_TypeDescriptions(List<APITable> tables)
        {
            APITable table = new APITable(this, m_Snapshot, m_Snapshot.TypeDescriptions.Count);


            table.AddColumn(
                new MetaColumn("name", "Name", new MetaType(typeof(string)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnManaged(m_Snapshot.TypeDescriptions.TypeDescriptionName, false)
            );
            table.AddColumn(
                new MetaColumn("assembly", "Assembly", new MetaType(typeof(string)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnManaged(m_Snapshot.TypeDescriptions.Assembly, false)
            );

            table.AddColumn(
                new MetaColumn("isValueType", "Value Type", new MetaType(typeof(bool)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn_Transform(m_Snapshot.TypeDescriptions.Flags, (a) => HasBit(a, TypeFlags.kValueType), (ref TypeFlags o, bool v) => SetBit(ref o, TypeFlags.kValueType, v))
            );
            table.AddColumn(
                new MetaColumn("isArray", "Array", new MetaType(typeof(bool)), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn_Transform(m_Snapshot.TypeDescriptions.Flags, (a) => HasBit(a, TypeFlags.kArray), (ref TypeFlags o, bool v) => SetBit(ref o, TypeFlags.kArray, v))
            );

            table.AddColumn(
                new MetaColumn("arrayRank", "Array Rank", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumn_Transform(m_Snapshot.TypeDescriptions.Flags, (a) => GetBits(a, TypeFlags.kArrayRankMask, 16), (ref TypeFlags o, int v) => SetBits(ref o, TypeFlags.kArrayRankMask, 16, v))
            );
            table.AddColumn(
                new MetaColumn("baseOrElementTypeIndex", "Base Or Element Type Index", new MetaType(typeof(int), DataMatchMethod.AsNumber), false, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnUnmanaged(m_Snapshot.TypeDescriptions.BaseOrElementTypeIndex, false)
            );
            table.AddColumn(
                new MetaColumn("size", "Size", new MetaType(typeof(int), DataMatchMethod.AsNumber), true, Grouping.groupByDuplicate
                    , Grouping.GetMergeAlgo(Grouping.MergeAlgo.sum, typeof(int)), "size")
                , DataArray.MakeColumnUnmanaged(m_Snapshot.TypeDescriptions.Size, false)
            );
            table.AddColumn(
                new MetaColumn("staticSize", "Static Field Size", new MetaType(typeof(int), DataMatchMethod.AsNumber), true, Grouping.groupByDuplicate
                    , Grouping.GetMergeAlgo(Grouping.MergeAlgo.sum, typeof(int)), "size")
                , DataArray.MakeColumn_ManagedTransform(m_Snapshot.TypeDescriptions.StaticFieldBytes, (a) => a.Length, (ref byte[] o, int v) => o = new byte[v])
            );
            table.AddColumn(
                new MetaColumn("typeInfoAddress", "Type Info Address", new MetaType(typeof(ulong), DataMatchMethod.AsString), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnUnmanaged(m_Snapshot.TypeDescriptions.TypeInfoAddress, true)
            );
            table.AddColumn(
                new MetaColumn("typeIndex", "Type Index", new MetaType(typeof(int), DataMatchMethod.AsNumber), true, Grouping.groupByDuplicate, null)
                , DataArray.MakeColumnUnmanaged(m_Snapshot.TypeDescriptions.TypeIndex, false)
            );
            table.CreateTable(kPrefixTableName + "ManagedType", kPrefixTableDisplayName + "Managed Type");
            if (m_Snapshot.CaptureFlags.HasFlag(CaptureFlags.ManagedObjects))
                table.NoDataMessage = "The current selection or filtered list does not contain any Managed Objects. Try a different selection or filter.";
            else
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
            if (m_Snapshot.CaptureFlags.HasFlag(CaptureFlags.NativeObjects))
                table.NoDataMessage = "The current selection or filtered list does not contain any Native Type Bases. Try a different selection or filter.";
            else
                table.NoDataMessage = "No Native Type Bases because they were not captured in this snapshot. Select the Native Objects option in the drop-down of the Capture button or via CaptureFlags when using the API to take a capture.";

            AddTable(table, tables);
        }

        private void CreateAllObjectTables(ManagedData crawledData)
        {
            var allManaged = new ObjectAllManagedTable(this, formatter, m_Snapshot, crawledData, ObjectTable.ObjectMetaType.Managed);

            if (m_Snapshot.CaptureFlags.HasFlag(CaptureFlags.ManagedObjects))
                allManaged.NoDataMessage = "The current selection or filtered list does not contain any Managed Objects. Try a different selection or filter.";
            else
                allManaged.NoDataMessage = "No Managed Objects because they were not captured in this snapshot. Select the Managed Objects option in the drop-down of the Capture button or via CaptureFlags when using the API to take a capture.";
            m_TablesByName.Add(allManaged.GetName(), allManaged);

            var allNative = new ObjectAllNativeTable(this, formatter, m_Snapshot, crawledData, ObjectTable.ObjectMetaType.Native);
            if (m_Snapshot.CaptureFlags.HasFlag(CaptureFlags.NativeObjects))
                allNative.NoDataMessage = "The current selection or filtered list does not contain any Native Objects. Try a different selection or filter.";
            else
                allNative.NoDataMessage = "No Native Objects because they were not captured in this snapshot. Select the Native Objects option in the drop-down of the Capture button or via CaptureFlags when using the API to take a capture.";
            m_TablesByName.Add(allNative.GetName(), allNative);

            var allObjects = new ObjectAllTable(this, formatter, m_Snapshot, crawledData, ObjectTable.ObjectMetaType.All);
            if (m_Snapshot.CaptureFlags.HasFlag(CaptureFlags.ManagedObjects) || m_Snapshot.CaptureFlags.HasFlag(CaptureFlags.NativeObjects))
                allObjects.NoDataMessage = "The current selection or filtered list does not contain any Managed or Native Objects. Try a different selection or filter.";
            else
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

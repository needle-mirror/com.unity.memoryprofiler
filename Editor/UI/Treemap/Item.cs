using System;
using UnityEditor;
using UnityEngine;

namespace Unity.MemoryProfiler.Editor.UI.Treemap
{
    internal enum ObjectMetricType : byte
    {
        None = 0,
        Managed,
        Native
    }

    internal struct ObjectMetric : IEquatable<ObjectMetric>
    {
        const string k_UnknownNativeType = "<invalid native type>";
        const string k_UnknownManagedType = "<invalid managed type>";

        public ObjectMetricType MetricType { private set; get; }
        public int ObjectIndex { private set; get; }
        CachedSnapshot m_CachedSnapshot;
        long cachedSize;
        string cachedTypeName;

        public ObjectMetric(int objectIndex, CachedSnapshot cachedSnapshot, ObjectMetricType metricType)
        {
            MetricType = metricType;
            ObjectIndex = objectIndex;
            m_CachedSnapshot = cachedSnapshot;
            cachedSize = -1;
            switch (MetricType)
            {
                case ObjectMetricType.Managed:
                    // cache size
                    cachedSize = m_CachedSnapshot.CrawledData.ManagedObjects[ObjectIndex].Size;
                    // cache type name
                    var ITypeDesc = m_CachedSnapshot.CrawledData.ManagedObjects[ObjectIndex].ITypeDescription;
                    if (ITypeDesc >= 0)
                    {
                        cachedTypeName = m_CachedSnapshot.TypeDescriptions.TypeDescriptionName[ITypeDesc];
                    }
                    else
                    {
                        cachedTypeName = k_UnknownManagedType;
                    }
                    break;
                case ObjectMetricType.Native:
                    // cache size
                    cachedSize = (long)m_CachedSnapshot.NativeObjects.Size[ObjectIndex];
                    // cache type name
                    var INatTypeDesc = m_CachedSnapshot.NativeObjects.NativeTypeArrayIndex[ObjectIndex];
                    if (INatTypeDesc > 0)
                    {
                        cachedTypeName = m_CachedSnapshot.NativeTypes.TypeName[INatTypeDesc];
                    }
                    else
                    {
                        cachedTypeName = k_UnknownNativeType;
                    }
                    break;
                default:
                    cachedTypeName = null;
                    break;
            }
        }

        public string GetTypeName()
        {
            return cachedTypeName;
        }

        public int GetTypeIndex()
        {
            switch (MetricType)
            {
                case ObjectMetricType.Managed:
                    return m_CachedSnapshot.CrawledData.ManagedObjects[ObjectIndex].ITypeDescription;
                case ObjectMetricType.Native:
                    return m_CachedSnapshot.NativeObjects.NativeTypeArrayIndex[ObjectIndex];
                default:
                    return -1;
            }
        }

        static System.Text.StringBuilder sharedBuilder = new System.Text.StringBuilder();

        public string GetName()
        {
            sharedBuilder.Clear();
            switch (MetricType)
            {
                case ObjectMetricType.Managed:
                    var managedObj = m_CachedSnapshot.CrawledData.ManagedObjects[ObjectIndex];
                    if (managedObj.NativeObjectIndex >= 0)
                    {
                        string objName = m_CachedSnapshot.NativeObjects.ObjectName[managedObj.NativeObjectIndex];
                        if (objName.Length > 0)
                        {
                            sharedBuilder.Append(" \"");
                            sharedBuilder.Append(objName);
                            sharedBuilder.Append("\" <");
                            sharedBuilder.Append(GetTypeName());
                            sharedBuilder.Append('>');
                            return sharedBuilder.ToString();
                        }
                    }
                    sharedBuilder.AppendFormat("[0x{0:x16}]", managedObj.PtrObject);
                    sharedBuilder.Append(" < ");
                    sharedBuilder.Append(GetTypeName());
                    sharedBuilder.Append(" > ");
                    return sharedBuilder.ToString();
                case ObjectMetricType.Native:
                    string objectName = m_CachedSnapshot.NativeObjects.ObjectName[ObjectIndex];
                    if (objectName.Length > 0)
                    {
                        sharedBuilder.Append(" \"");
                        sharedBuilder.Append(objectName);
                        sharedBuilder.Append("\" <");
                        sharedBuilder.Append(GetTypeName());
                        sharedBuilder.Append('>');
                        return sharedBuilder.ToString();
                    }
                    return GetTypeName();
                default:
                    return null;
            }
        }

        public long GetObjectUID()
        {
            switch (MetricType)
            {
                case ObjectMetricType.Managed:
                    return m_CachedSnapshot.ManagedObjectIndexToUnifiedObjectIndex(ObjectIndex);
                case ObjectMetricType.Native:
                    return m_CachedSnapshot.NativeObjectIndexToUnifiedObjectIndex(ObjectIndex);
                default:
                    return -1;
            }
        }

        public long GetValue()
        {
            return cachedSize;
        }

        public bool Equals(ObjectMetric other)
        {
            if (MetricType == other.MetricType)
            {
                return ObjectIndex == other.ObjectIndex;
            }

            return false;
        }
    }

    internal class Item : IComparable<Item>
    {
        static System.Text.StringBuilder builder = new System.Text.StringBuilder();
        public Group Group { private set; get; }
        public Rect Position;
        //public int _index;

        public ObjectMetric Metric { private set; get; }

        public string Label { private set; get; }
        public long Value { get { return Metric.GetValue(); } }
        public Color Color { get { return Group.Color; } }

        public Item(ObjectMetric metric, Group group)
        {
            builder.Clear();
            Metric = metric;
            Group = group;
            builder.Append(Metric.GetName());
            builder.AppendLine();
            builder.Append(EditorUtility.FormatBytes(Value));
            Label = builder.ToString();
            //Label = Metric.GetName() + "\n" + EditorUtility.FormatBytes(Value);
        }

        public int CompareTo(Item other)
        {
            return (int)(Group != other.Group ? other.Group.TotalValue - Group.TotalValue : other.Value - Value);
        }
    }
}

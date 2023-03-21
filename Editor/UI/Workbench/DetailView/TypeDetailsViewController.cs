using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor.UI
{
    internal class TypeDetailsViewController : ViewController
    {
        const string k_UxmlAssetGuid = "28e8ee6a14a922648a9253b05a4fbb9d";
        const string k_UxmlIdentifier = "memory-profiler-type-details";
        const string k_UxmlIdentifierTitle = k_UxmlIdentifier + "__title";
        const string k_UxmlIdentifierNativeTypeLabel = k_UxmlIdentifier + "__desc__native-type";
        const string k_UxmlIdentifierManagedTypeLabel = k_UxmlIdentifier + "__desc__managed-type";
        const string k_UxmlIdentifierChildrenCount = k_UxmlIdentifier + "__desc__children-count";

        // State
        readonly CachedSnapshot m_Snapshot;
        readonly CachedSnapshot.SourceIndex m_DataSource;
        readonly int m_ChildrenCount;

        // View
        ObjectOrTypeLabel m_TitleLabel;
        Label m_NativeTypeLabel;
        Label m_ManagedTypeLabel;
        Label m_ChildrenCountLabel;

        public TypeDetailsViewController(CachedSnapshot snapshot, CachedSnapshot.SourceIndex source, int childrenCount)
        {
            m_Snapshot = snapshot;
            m_DataSource = source;
            m_ChildrenCount = childrenCount;
        }

        protected override VisualElement LoadView()
        {
            var view = ViewControllerUtility.LoadVisualTreeFromUxml(k_UxmlAssetGuid);
            if (view == null)
                throw new InvalidOperationException("Unable to create view from Uxml. Uxml must contain at least one child element.");

            GatherReferencesInView(view);

            return view;
        }

        protected override void ViewLoaded()
        {
            base.ViewLoaded();
            RefreshView();
        }

        void GatherReferencesInView(VisualElement view)
        {
            m_TitleLabel = view.Q<ObjectOrTypeLabel>(k_UxmlIdentifierTitle);
            m_NativeTypeLabel = view.Q<Label>(k_UxmlIdentifierNativeTypeLabel);
            m_ManagedTypeLabel = view.Q<Label>(k_UxmlIdentifierManagedTypeLabel);
            m_ChildrenCountLabel = view.Q<Label>(k_UxmlIdentifierChildrenCount);
        }

        void RefreshView()
        {
            m_TitleLabel.SetLabelData(m_Snapshot, m_DataSource);

            switch (m_DataSource.Id)
            {
                case CachedSnapshot.SourceIndex.SourceId.NativeType:
                {
                    m_NativeTypeLabel.text = m_Snapshot.NativeTypes.TypeName[m_DataSource.Index];
                    if (m_Snapshot.CrawledData.NativeUnityObjectTypeIndexToManagedBaseTypeIndex.TryGetValue((int)m_DataSource.Index, out var managedTypeIndex))
                        m_ManagedTypeLabel.text = m_Snapshot.TypeDescriptions.TypeDescriptionName[managedTypeIndex];
                    break;
                }
                case CachedSnapshot.SourceIndex.SourceId.ManagedType:
                {
                    m_ManagedTypeLabel.text = m_Snapshot.TypeDescriptions.TypeDescriptionName[m_DataSource.Index];
                    if (m_Snapshot.TypeDescriptions.UnityObjectTypeIndexToNativeTypeIndex.TryGetValue((int)m_DataSource.Index, out var nativeTypeIndex) && nativeTypeIndex >= 0)
                        m_NativeTypeLabel.text = m_Snapshot.NativeTypes.TypeName[nativeTypeIndex];
                    break;
                }
                case CachedSnapshot.SourceIndex.SourceId.NativeRootReference:
                    m_NativeTypeLabel.text = m_Snapshot.NativeRootReferences.ObjectName[m_DataSource.Index];
                    break;
                default:
                    break;
            }

            m_ChildrenCountLabel.text = m_ChildrenCount.ToString();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;

namespace Unity.MemoryProfiler.Editor.UI
{
    interface IModelBuilder<TModel, TBuildArgs>
        where TModel : class
    {
        TModel Build(CachedSnapshot snapshot, TBuildArgs args);
        TModel Build(TModel baseModel, List<int> treeNodeIdFilter);
    }

    class ModelBuildOrchestrator<TModel, TBuildArgs>
        where TModel : class
    {
        readonly CachedSnapshot m_Snapshot;
        readonly IModelBuilder<TModel, TBuildArgs> m_Builder;
        readonly string m_ModelTypeName;

        public ModelBuildOrchestrator(
            CachedSnapshot snapshot,
            IModelBuilder<TModel, TBuildArgs> builder,
            string modelTypeName)
        {
            m_Snapshot = snapshot;
            m_Builder = builder;
            m_ModelTypeName = modelTypeName;
        }

        public Func<TModel> CreateBuildTask(
            TBuildArgs args,
            TModel baseModel,
            List<int> treeNodeIdFilter,
            CancellationToken cancellationToken)
        {
            if (treeNodeIdFilter != null)
            {
                return () => BuildDerivedModel(baseModel, treeNodeIdFilter, cancellationToken);
            }

            return () => BuildFullModel(args, cancellationToken);
        }

        TModel BuildDerivedModel(
            TModel baseModel,
            List<int> treeNodeIdFilter,
            CancellationToken cancellationToken)
        {
            AsyncTaskHelper.DebugLogAsyncStep("Start Building (derived)                 " + m_ModelTypeName);
            cancellationToken.ThrowIfCancellationRequested();
            AsyncTaskHelper.DebugLogAsyncStep("Start Building (derived) (not canceled)                 " + m_ModelTypeName);

            var model = m_Builder.Build(baseModel, treeNodeIdFilter);

            cancellationToken.ThrowIfCancellationRequested();
            AsyncTaskHelper.DebugLogAsyncStep("Building Finished                 " + m_ModelTypeName);
            return model;
        }

        TModel BuildFullModel(
            TBuildArgs args,
            CancellationToken cancellationToken)
        {
            AsyncTaskHelper.DebugLogAsyncStep("Start Building                 " + m_ModelTypeName);
            cancellationToken.ThrowIfCancellationRequested();
            AsyncTaskHelper.DebugLogAsyncStep("Start Building (not canceled)                 " + m_ModelTypeName);

            var model = m_Builder.Build(m_Snapshot, args);

            cancellationToken.ThrowIfCancellationRequested();
            AsyncTaskHelper.DebugLogAsyncStep("Building Finished                 " + m_ModelTypeName);
            return model;
        }
    }
}

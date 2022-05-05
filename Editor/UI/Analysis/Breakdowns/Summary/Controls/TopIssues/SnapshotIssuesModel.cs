using System.Collections.Generic;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// </summary>
    internal class SnapshotIssuesModel
    {
        public SnapshotIssuesModel(List<Issue> issues)
        {
            Issues = issues;
        }

        public enum IssueLevel
        {
            Info,
            Warning,
            Error,
        }

        public List<Issue> Issues { get; }

        public struct Issue
        {
            public IssueLevel IssueLevel;
            public string Summary;
            public string Details;
        }
    }
}

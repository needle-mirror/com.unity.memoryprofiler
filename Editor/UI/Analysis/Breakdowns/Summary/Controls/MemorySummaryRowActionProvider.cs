using System;

namespace Unity.MemoryProfiler.Editor.UI
{
    /// <summary>
    /// Supplies the per-row "Inspect" action of a memory summary widget, together with the label and tooltip of
    /// the buttons that run it: the inline button in the legend and the button in the Selection Details panel.
    /// </summary>
    internal abstract class MemorySummaryRowActionProvider
    {
        /// <summary>Label of the Selection Details button.</summary>
        public abstract string Label { get; }

        /// <summary>Tooltip of both buttons.</summary>
        public abstract string Tooltip { get; }

        /// <summary>
        /// Whether <paramref name="row"/> has an action. Must agree with <see cref="GetAction"/> returning non-null,
        /// but without allocating the action.
        /// </summary>
        public abstract bool HasAction(in MemorySummaryModel.Row row);

        /// <summary>The action to run for <paramref name="row"/>, or null for a row with nothing to inspect.</summary>
        public abstract Action GetAction(MemorySummaryModel.Row row);
    }
}

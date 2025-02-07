using Unity.MemoryProfiler.Editor.UI.PathsToRoot;
using UnityEngine.Internal;

namespace UnityEditor
{
    internal static class EditorUtilityCompatibilityHelper
    {
        public enum DialogOptOutDecisionType
        {
            ForThisMachine,
            ForThisSession,
        }

        public static bool GetDialogOptOutDecision(DialogOptOutDecisionType dialogOptOutDecisionType, string dialogOptOutDecisionStorageKey)
        {
            return EditorUtility.GetDialogOptOutDecision((UnityEditor.DialogOptOutDecisionType)dialogOptOutDecisionType, dialogOptOutDecisionStorageKey);
        }

        public static void SetDialogOptOutDecision(DialogOptOutDecisionType dialogOptOutDecisionType, string dialogOptOutDecisionStorageKey, bool optOutDecision)
        {
            EditorUtility.SetDialogOptOutDecision((UnityEditor.DialogOptOutDecisionType)dialogOptOutDecisionType, dialogOptOutDecisionStorageKey, optOutDecision);
        }

        public static bool DisplayDecisionDialogWithOptOut(string title, string message, string ok, [DefaultValue("\"\"")] string cancel, DialogOptOutDecisionType dialogOptOutDecisionType, string dialogOptOutDecisionStorageKey)
        {
#if ENABLE_CORECLR
            return EditorDialog.DisplayDecisionDialogWithOptOut(title, message, ok, cancel, (UnityEditor.DialogOptOutDecisionType)(int)dialogOptOutDecisionType, dialogOptOutDecisionStorageKey);
#else
            return EditorUtility.DisplayDialog(title, message, ok, cancel, (UnityEditor.DialogOptOutDecisionType)dialogOptOutDecisionType, dialogOptOutDecisionStorageKey);
#endif
        }

        public static bool DisplayDecisionDialog(string title, string message, string okButton, string cancelButton = "")
        {
#if ENABLE_CORECLR
            return EditorDialog.DisplayDecisionDialog(title, message, okButton, cancelButton);
#else
            return EditorUtility.DisplayDialog(title, message, okButton, cancelButton);
#endif
        }

        public static void DisplayAlertDialog(string title, string message, string okButton)
        {
#if ENABLE_CORECLR
            EditorDialog.DisplayAlertDialog(title, message, okButton);
#else
            EditorUtility.DisplayDialog(title, message, okButton);
#endif
        }
    }
}

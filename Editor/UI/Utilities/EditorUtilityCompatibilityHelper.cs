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

        public static bool DisplayDialog(string title, string message, string ok, DialogOptOutDecisionType dialogOptOutDecisionType, string dialogOptOutDecisionStorageKey)
        {
            return DisplayDialog(title, message, ok, string.Empty, dialogOptOutDecisionType, dialogOptOutDecisionStorageKey);
        }

        public static bool DisplayDialog(string title, string message, string ok, [DefaultValue("\"\"")] string cancel, DialogOptOutDecisionType dialogOptOutDecisionType, string dialogOptOutDecisionStorageKey)
        {
            return EditorUtility.DisplayDialog(title, message, ok, cancel, (UnityEditor.DialogOptOutDecisionType)dialogOptOutDecisionType, dialogOptOutDecisionStorageKey);
        }
    }
}

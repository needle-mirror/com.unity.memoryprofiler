using UnityEngine.Internal;

namespace UnityEditor
{
    internal static class EditorUtilityCompatibilityHelper
    {
        public enum DialogOptOutDecisionType
        {
            ForThisSession = 0,
            ForThisUser = 1,
            ForThisMachine = 1
        }

        public static bool DisplayDecisionDialogWithOptOut(string title, string message, string ok, [DefaultValue("\"\"")] string cancel, DialogOptOutDecisionType dialogOptOutDecisionType, string dialogOptOutDecisionStorageKey)
        {
#if UNITY_6000_3_OR_NEWER
            return EditorDialog.DisplayDecisionDialogWithOptOut(title, message, ok, cancel, (UnityEditor.DialogOptOutDecisionType)(int)dialogOptOutDecisionType, dialogOptOutDecisionStorageKey);
#else
            return EditorUtility.DisplayDialog(title, message, ok, cancel, (UnityEditor.DialogOptOutDecisionType)dialogOptOutDecisionType, dialogOptOutDecisionStorageKey);
#endif
        }

        public static bool DisplayDecisionDialog(string title, string message, string okButton, string cancelButton = "")
        {
#if UNITY_6000_3_OR_NEWER
            return EditorDialog.DisplayDecisionDialog(title, message, okButton, cancelButton);
#else
            return EditorUtility.DisplayDialog(title, message, okButton, cancelButton);
#endif
        }

        public static void DisplayAlertDialog(string title, string message, string okButton)
        {
#if UNITY_6000_3_OR_NEWER
            EditorDialog.DisplayAlertDialog(title, message, okButton);
#else
            EditorUtility.DisplayDialog(title, message, okButton);
#endif
        }
    }
}

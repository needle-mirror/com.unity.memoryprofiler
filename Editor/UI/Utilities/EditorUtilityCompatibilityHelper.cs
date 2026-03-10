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

        public static void SetDialogOptOutDecision(DialogOptOutDecisionType dialogOptOutDecisionType, string dialogOptOutDecisionStorageKey, bool remember)
        {
#if UNITY_6000_3_OR_NEWER
            const string k_OptOutPrefix = "DialogOptOut.";
            dialogOptOutDecisionStorageKey = k_OptOutPrefix + dialogOptOutDecisionStorageKey;
            if (!remember)
            {
                EditorPrefs.DeleteKey(dialogOptOutDecisionStorageKey);
                SessionState.EraseInt(dialogOptOutDecisionStorageKey);
                return;
            }

            switch ((UnityEditor.DialogOptOutDecisionType)dialogOptOutDecisionType)
            {
                case UnityEditor.DialogOptOutDecisionType.ForThisSession:
                    // The trunk code checks EditorPrefs first and then SessionState, so we need to do the same to avoid issues with the decision not being stored in the right place and therefore not being respected.
                    if (EditorPrefs.HasKey(dialogOptOutDecisionStorageKey))
                    {
                        EditorPrefs.DeleteKey(dialogOptOutDecisionStorageKey);
                    }
                    SessionState.SetInt(dialogOptOutDecisionStorageKey, (int)DialogResult.DefaultAction);
                    break;
                case UnityEditor.DialogOptOutDecisionType.ForThisUser:
                    EditorPrefs.SetInt(dialogOptOutDecisionStorageKey, (int)DialogResult.DefaultAction);
                    break;
            }
#else
            EditorUtility.SetDialogOptOutDecision((UnityEditor.DialogOptOutDecisionType)dialogOptOutDecisionType, dialogOptOutDecisionStorageKey, remember);
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

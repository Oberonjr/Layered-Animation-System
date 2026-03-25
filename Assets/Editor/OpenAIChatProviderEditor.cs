using UnityEditor;
using LAS;

namespace LAS
{
    [CustomEditor(typeof(OpenAIChatProvider))]
    public class OpenAIChatProviderEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var presetProp        = serializedObject.FindProperty("preset");
            var customBaseUrlProp = serializedObject.FindProperty("customBaseUrl");
            var apiKeyNameProp    = serializedObject.FindProperty("apiKeyName");

            // Draw every field except customBaseUrl (shown conditionally) and apiKeyName (shown in key section).
            var prop = serializedObject.GetIterator();
            prop.NextVisible(true);

            while (prop.NextVisible(false))
            {
                if (prop.name == "customBaseUrl") continue;
                if (prop.name == "apiKeyName")    continue;
                EditorGUILayout.PropertyField(prop, true);
            }

            // Only show customBaseUrl when Custom preset is selected.
            if (presetProp.enumValueIndex == (int)OpenAIChatProvider.ProviderPreset.Custom)
                EditorGUILayout.PropertyField(customBaseUrlProp);

            // API key name field (which key to read from the external file)
            EditorGUILayout.PropertyField(apiKeyNameProp);

            serializedObject.ApplyModifiedProperties();

            // Show key file UI (shared with Claude editor)
            string keyName = apiKeyNameProp.stringValue;
            ClaudeProviderEditor.DrawApiKeySection(
                string.IsNullOrEmpty(keyName) ? "OPENAI_API_KEY" : keyName);
        }
    }
}

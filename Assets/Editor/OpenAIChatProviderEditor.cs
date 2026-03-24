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

            // Draw every field except customBaseUrl, which we handle manually.
            var prop = serializedObject.GetIterator();
            prop.NextVisible(true); // enter first child (m_Script)

            while (prop.NextVisible(false))
            {
                if (prop.name == "customBaseUrl") continue;
                EditorGUILayout.PropertyField(prop, true);
            }

            // Only show customBaseUrl when Custom preset is selected.
            if (presetProp.enumValueIndex == (int)OpenAIChatProvider.ProviderPreset.Custom)
                EditorGUILayout.PropertyField(customBaseUrlProp);

            serializedObject.ApplyModifiedProperties();
        }
    }
}

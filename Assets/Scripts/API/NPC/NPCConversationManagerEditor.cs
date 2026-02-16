using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(NPCConversationManager))]
public class NPCConversationManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        NPCConversationManager manager = (NPCConversationManager)target;
        
        // Draw default inspector
        DrawDefaultInspector();
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Model Selection", EditorStyles.boldLabel);
        
        // Get available models via reflection
        SerializedProperty availableModelsProp = serializedObject.FindProperty("availableModels");
        SerializedProperty selectedModelIndexProp = serializedObject.FindProperty("selectedModelIndex");
        
        if (availableModelsProp.arraySize > 0)
        {
            // Create array of model names
            string[] modelNames = new string[availableModelsProp.arraySize];
            for (int i = 0; i < availableModelsProp.arraySize; i++)
            {
                modelNames[i] = availableModelsProp.GetArrayElementAtIndex(i).stringValue;
            }
            
            // Show dropdown
            int newIndex = EditorGUILayout.Popup("Selected Model", selectedModelIndexProp.intValue, modelNames);
            
            if (newIndex != selectedModelIndexProp.intValue)
            {
                selectedModelIndexProp.intValue = newIndex;
                serializedObject.ApplyModifiedProperties();
                Debug.Log($"Model changed to: {modelNames[newIndex]}");
            }
            
            // Show current model info
            EditorGUILayout.HelpBox($"Currently using: {modelNames[selectedModelIndexProp.intValue]}", UnityEditor.MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("No models detected. Make sure Ollama is running and press Play to detect models.", UnityEditor.MessageType.Warning);
        }
        
        EditorGUILayout.Space();
        
        // Testing buttons
        EditorGUILayout.LabelField("Testing", EditorStyles.boldLabel);
        
        if (GUILayout.Button("Trigger NPC-to-NPC Conversation"))
        {
            if (Application.isPlaying)
            {
                manager.TriggerNPCToNPCConversation();
            }
            else
            {
                EditorGUILayout.HelpBox("Enter Play mode to test conversations.", UnityEditor.MessageType.Info);
            }
        }
    }
}

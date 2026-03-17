using UnityEngine;
using UnityEditor;
using LAS;

namespace LAS
{
    [CustomEditor(typeof(NPCManager))]
    public class NPCManagerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            NPCManager manager = (NPCManager)target;

            // Draw default inspector
            DrawDefaultInspector();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("=== Model Selection ===", EditorStyles.boldLabel);

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

            EditorGUILayout.Space(10);

            // NPC Registration Info
            EditorGUILayout.LabelField("=== Registered NPCs ===", EditorStyles.boldLabel);
            SerializedProperty registeredNPCsProp = serializedObject.FindProperty("registeredNPCs");

            if (registeredNPCsProp.arraySize > 0)
            {
                EditorGUILayout.HelpBox($"{registeredNPCsProp.arraySize} NPC(s) registered", UnityEditor.MessageType.Info);

                EditorGUI.indentLevel++;
                for (int i = 0; i < registeredNPCsProp.arraySize; i++)
                {
                    var npcProp = registeredNPCsProp.GetArrayElementAtIndex(i);
                    if (npcProp.objectReferenceValue != null)
                    {
                        NPCController npc = npcProp.objectReferenceValue as NPCController;
                        if (npc != null)
                        {
                            EditorGUILayout.LabelField($"[{i}] {npc.npcName} ({npc.characterRole})");
                        }
                    }
                }
                EditorGUI.indentLevel--;
            }
            else
            {
                EditorGUILayout.HelpBox("No NPCs registered yet. NPCs will register when Play mode starts.", UnityEditor.MessageType.Info);
            }

            EditorGUILayout.Space(10);

            // Progression Info
            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField("=== Progression ===", EditorStyles.boldLabel);
                SerializedProperty currentStepProp = serializedObject.FindProperty("currentProgressionStep");
                EditorGUILayout.LabelField($"Current Step: {currentStepProp.intValue}");

                if (GUILayout.Button("Advance to Next Step"))
                {
                    manager.AdvanceProgressionStep();
                }
            }

            EditorGUILayout.Space(10);

            // Testing buttons
            EditorGUILayout.LabelField("=== Testing ===", EditorStyles.boldLabel);

            if (GUILayout.Button("Trigger NPC-to-NPC Conversation"))
            {
                if (Application.isPlaying)
                {
                    manager.TriggerNPCToNPCConversation();
                }
                else
                {
                    EditorUtility.DisplayDialog("Not in Play Mode", "Enter Play mode to test conversations.", "OK");
                }
            }

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Testing features are only available in Play mode.", UnityEditor.MessageType.Info);
            }
        }
    }

}

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

            DrawDefaultInspector();

            EditorGUILayout.Space(10);

            // ── Active Provider ───────────────────────────────────────────────────
            EditorGUILayout.LabelField("=== Active Provider ===", EditorStyles.boldLabel);

            var providerProp = serializedObject.FindProperty("llmProvider");
            var provider = providerProp.objectReferenceValue as LLMProviderBase;

            if (provider == null)
            {
                EditorGUILayout.HelpBox(
                    "No LLM provider assigned.\n" +
                    "Create one via  Assets → Create → LAS → LLM Providers,\n" +
                    "then drag the asset into the LLM Provider slot above.",
                    UnityEditor.MessageType.Warning);
            }
            else
            {
                if (Application.isPlaying)
                {
                    if (provider.IsConnected)
                        EditorGUILayout.HelpBox(
                            $"Connected  ·  {provider.ProviderDisplayName}\nModel: {provider.ModelName}",
                            UnityEditor.MessageType.Info);
                    else
                        EditorGUILayout.HelpBox(
                            $"Not connected  ·  {provider.ProviderDisplayName}",
                            UnityEditor.MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        $"{provider.ProviderDisplayName}\nEnter Play mode to connect.",
                        UnityEditor.MessageType.None);
                }

                if (GUILayout.Button("Select Provider Asset"))
                    Selection.activeObject = provider;
            }

            EditorGUILayout.Space(10);

            // ── Registered NPCs ───────────────────────────────────────────────────
            EditorGUILayout.LabelField("=== Registered NPCs ===", EditorStyles.boldLabel);
            var registeredNPCsProp = serializedObject.FindProperty("registeredNPCs");

            if (registeredNPCsProp.arraySize > 0)
            {
                EditorGUILayout.HelpBox($"{registeredNPCsProp.arraySize} NPC(s) registered", UnityEditor.MessageType.Info);

                EditorGUI.indentLevel++;
                for (int i = 0; i < registeredNPCsProp.arraySize; i++)
                {
                    var npcProp = registeredNPCsProp.GetArrayElementAtIndex(i);
                    if (npcProp.objectReferenceValue is NPCController npc)
                        EditorGUILayout.LabelField($"[{i}]  {npc.npcName}  ({npc.characterRole})");
                }
                EditorGUI.indentLevel--;
            }
            else
            {
                EditorGUILayout.HelpBox("No NPCs registered yet — they register when Play mode starts.", UnityEditor.MessageType.Info);
            }

            EditorGUILayout.Space(10);

            // ── Progression ───────────────────────────────────────────────────────
            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField("=== Progression ===", EditorStyles.boldLabel);
                var currentStepProp = serializedObject.FindProperty("currentProgressionStep");
                EditorGUILayout.LabelField($"Current Step: {currentStepProp.intValue}");

                if (GUILayout.Button("Advance to Next Step"))
                    manager.AdvanceProgressionStep();

                EditorGUILayout.Space(10);
            }

            // ── Testing ───────────────────────────────────────────────────────────
            EditorGUILayout.LabelField("=== Testing ===", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Trigger NPC-to-NPC Conversation"))
                    manager.TriggerNPCToNPCConversation();
            }

            if (!Application.isPlaying)
                EditorGUILayout.HelpBox("Testing features are only available in Play mode.", UnityEditor.MessageType.Info);
        }
    }
}

using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
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

            // ── NPC Character Mapping ─────────────────────────────────────────────
            EditorGUILayout.LabelField("=== NPC Character Mapping ===", EditorStyles.boldLabel);

            var scenarioFileProp = serializedObject.FindProperty("scenarioConfigFile");
            var textAsset = scenarioFileProp?.objectReferenceValue as TextAsset;

            if (textAsset == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign a Scenario Config File above to enable explicit character mapping.\n" +
                    "Without a map, NPCs are assigned by registration order (first to Start = character[0]).",
                    UnityEditor.MessageType.Info);
            }
            else
            {
                if (GUILayout.Button("Extract Characters from JSON"))
                    ExtractCharactersFromJson(manager, textAsset);

                if (manager.npcCharacterMap != null && manager.npcCharacterMap.Count > 0)
                {
                    int total    = manager.npcCharacterMap.Count;
                    int assigned = manager.npcCharacterMap.Values.Count(v => v != null);
                    EditorGUILayout.HelpBox(
                        assigned == total
                            ? $"✓ All {total} character(s) assigned — explicit mapping active."
                            : $"⚠ {assigned}/{total} assigned — fill remaining slots, or leave all null for index-based fallback.",
                        assigned == total
                            ? UnityEditor.MessageType.Info
                            : UnityEditor.MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "Map is empty. Click 'Extract Characters from JSON' to populate keys,\n" +
                        "then drag the NPC Controller for each character into the value slots.",
                        UnityEditor.MessageType.Info);
                }
            }

            EditorGUILayout.Space(10);

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

        private static void ExtractCharactersFromJson(NPCManager manager, TextAsset textAsset)
        {
            try
            {
                var config = JsonUtility.FromJson<ScenarioConfig>(textAsset.text);
                if (config?.characters == null || config.characters.Length == 0)
                {
                    Debug.LogWarning("[NPCManagerEditor] No characters found in JSON.");
                    return;
                }

                Undo.RecordObject(manager, "Extract Characters from JSON");

                int added = 0;
                foreach (var character in config.characters)
                {
                    if (!manager.npcCharacterMap.ContainsKey(character.name))
                    {
                        manager.npcCharacterMap[character.name] = null;
                        added++;
                    }
                }

                EditorUtility.SetDirty(manager);
                Debug.Log($"[NPCManagerEditor] Extracted {config.characters.Length} character(s) from JSON " +
                          $"({added} new key(s) added). Assign NPC Controllers in the map below.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[NPCManagerEditor] Failed to parse scenario JSON: {e.Message}");
            }
        }
    }
}

using AYellowpaper.SerializedCollections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Routes LLM action commands to execution.
/// Dictionary maps ActionDefinition ScriptableObject → UnityEvent wired to ActionBridge method.
/// The SO's actionKey field is used for LLM lookup, but the SO itself is the dictionary key.
/// </summary>
public class NPCActionDispatcher : MonoBehaviour
{
    public static NPCActionDispatcher Instance { get; private set; }

    [Header("Action Handlers")]
    [Tooltip("Map ActionDefinition assets to ActionBridge methods. Wire the UnityEvent to the appropriate Execute* method.")]
    [SerializedDictionary("Action Definition", "Execution Method")]
    public SerializedDictionary<NPCActionDefinition, NPCActionCallback> actionHandlers
        = new SerializedDictionary<NPCActionDefinition, NPCActionCallback>();

    private Dictionary<string, NPCActionDefinition> keyLookup; // action_key → definition

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[Dispatcher] Duplicate dispatcher - destroying.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        BuildKeyLookup();
    }

    private void BuildKeyLookup()
    {
        keyLookup = new Dictionary<string, NPCActionDefinition>();

        foreach (var kvp in actionHandlers)
        {
            if (kvp.Key == null) continue;

            string key = kvp.Key.actionKey;
            if (keyLookup.ContainsKey(key))
            {
                Debug.LogWarning($"[Dispatcher] Duplicate action key '{key}' - only first will be used.");
                continue;
            }

            keyLookup[key] = kvp.Key;
        }

        Debug.Log($"[Dispatcher] Registered {keyLookup.Count} actions.");
    }

    public void Dispatch(NPCActionCommand command, List<NPCController> registeredNPCs)
    {
        if (command == null || string.IsNullOrEmpty(command.action_key) || command.action_key == "NONE")
            return;

        if (command.npc_index < 0 || command.npc_index >= registeredNPCs.Count)
        {
            Debug.LogWarning($"[Dispatcher] Invalid npc_index {command.npc_index}");
            return;
        }

        NPCController npcCtrl = registeredNPCs[command.npc_index];
        NPCBehaviourController behaviour = npcCtrl.GetComponent<NPCBehaviourController>();

        if (behaviour == null)
        {
            Debug.LogWarning($"[Dispatcher] {npcCtrl.npcName} has no NPCBehaviourController.");
            return;
        }

        // Look up ActionDefinition by action_key
        if (!keyLookup.TryGetValue(command.action_key, out NPCActionDefinition actionDef))
        {
            Debug.LogWarning($"[Dispatcher] No action definition for key: '{command.action_key}'");
            return;
        }

        // Look up the wired callback
        if (!actionHandlers.TryGetValue(actionDef, out NPCActionCallback callback))
        {
            Debug.LogWarning($"[Dispatcher] No callback wired for action: '{actionDef.displayName}'");
            return;
        }

        // Resolve targets
        Transform primaryTarget = string.IsNullOrEmpty(command.action_target)
            ? null
            : NPCActionTargetRegistry.Instance?.Resolve(command.action_target);

        Transform secondaryTarget = string.IsNullOrEmpty(command.action_secondary_target)
            ? null
            : NPCActionTargetRegistry.Instance?.Resolve(command.action_secondary_target);

        Debug.Log($"[Dispatcher] {npcCtrl.npcName} → {actionDef.displayName}" +
                  $"{(primaryTarget != null ? $" → {primaryTarget.name}" : "")}");

        // Invoke the callback (wired to ActionBridge method)
        callback?.Invoke(behaviour, primaryTarget, secondaryTarget);
    }

    public void DispatchDirect(NPCActionDefinition actionDef, NPCBehaviourController behaviour,
                                Transform primaryTarget, Transform secondaryTarget)
    {
        if (actionDef == null || behaviour == null)
        {
            Debug.LogWarning("[Dispatcher] DispatchDirect: null action or behaviour.");
            return;
        }

        if (!actionHandlers.TryGetValue(actionDef, out NPCActionCallback callback))
        {
            Debug.LogWarning($"[Dispatcher] No callback wired for action: '{actionDef.displayName}'");
            return;
        }

        callback?.Invoke(behaviour, primaryTarget, secondaryTarget);
    }

    public string BuildActionVocabularyPrompt()
    {
        if (actionHandlers == null || actionHandlers.Count == 0)
            return "";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== AVAILABLE ACTIONS ===");
        sb.AppendLine("You MAY include one action per response. Use EXACTLY these action_key values:");
        sb.AppendLine();

        foreach (var kvp in actionHandlers)
        {
            if (kvp.Key == null) continue;

            var def = kvp.Key;
            sb.AppendLine($"  action_key: \"{def.actionKey}\"");
            sb.AppendLine($"  → {def.llmDescription}");
            if (def.requiresTarget)
                sb.AppendLine($"    action_target: {def.targetDescription}");
            if (def.requiresSecondaryTarget)
                sb.AppendLine($"    action_secondary_target: {def.secondaryTargetDescription}");
            sb.AppendLine();
        }

        var registry = NPCActionTargetRegistry.Instance;
        if (registry != null)
        {
            sb.AppendLine("Valid target names in this scene:");
            foreach (string name in registry.GetAllTargetNames())
                sb.AppendLine($"  \"{name}\"");
        }

        sb.AppendLine();
        sb.AppendLine("If no physical action is needed, use action_key: \"NONE\".");
        return sb.ToString();
    }

    public IEnumerable<NPCActionDefinition> GetActiveActions()
    {
        foreach (var kvp in actionHandlers)
            if (kvp.Key != null)
                yield return kvp.Key;
    }
}
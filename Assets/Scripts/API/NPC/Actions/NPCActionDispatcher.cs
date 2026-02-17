using AYellowpaper.SerializedCollections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Scene-level dispatcher. Receives a parsed NPCActionCommand from NPCManager,
/// resolves the NPC and target, then routes directly to ActionBridge methods.
/// The SerializedDictionary maps action keys to ActionBridge method names,
/// avoiding the inspector serialization issue with generic UnityEvents.
/// </summary>
public class NPCActionDispatcher : MonoBehaviour
{
    public static NPCActionDispatcher Instance { get; private set; }

    [Header("Action Bridge")]
    [Tooltip("The ActionBridge component that executes actions on NPCs.")]
    public ActionBridge actionBridge;

    [Header("Action Key → Method Name")]
    [Tooltip("Maps action key (e.g. 'PICK_UP') to ActionBridge method name (e.g. 'ExecutePickUp'). " +
             "Method names must exactly match public methods on ActionBridge.")]
    [SerializedDictionary("Action Key", "Bridge Method Name")]
    public SerializedDictionary<string, string> actionHandlers
        = new SerializedDictionary<string, string>();

    [Header("Action Definitions (for prompt injection)")]
    [Tooltip("All NPCActionDefinition assets. Used to auto-build the LLM action vocabulary.")]
    public List<NPCActionDefinition> actionDefinitions = new List<NPCActionDefinition>();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[Dispatcher] Duplicate dispatcher - destroying.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (actionBridge == null)
            actionBridge = GetComponent<ActionBridge>();
    }

    /// <summary>
    /// Called by NPCManager after parsing an LLM response.
    /// Routes the action to the correct NPC via ActionBridge.
    /// </summary>
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

        Transform primaryTarget = string.IsNullOrEmpty(command.action_target)
            ? null
            : NPCActionTargetRegistry.Instance?.Resolve(command.action_target);

        Transform secondaryTarget = string.IsNullOrEmpty(command.action_secondary_target)
            ? null
            : NPCActionTargetRegistry.Instance?.Resolve(command.action_secondary_target);

        if (!actionHandlers.TryGetValue(command.action_key, out string methodName))
        {
            Debug.LogWarning($"[Dispatcher] No handler registered for action key: '{command.action_key}'");
            return;
        }

        Debug.Log($"[Dispatcher] {npcCtrl.npcName} → {command.action_key}" +
                  $"{(primaryTarget != null ? $" → {primaryTarget.name}" : "")}");

        actionBridge.Execute(methodName, behaviour, primaryTarget, secondaryTarget);
    }

    /// <summary>
    /// Direct dispatch from the Inspector (Editor test buttons on NPCBehaviourController).
    /// </summary>
    public void DispatchDirect(string actionKey, NPCBehaviourController behaviour,
                                Transform primaryTarget, Transform secondaryTarget)
    {
        if (!actionHandlers.TryGetValue(actionKey, out string methodName))
        {
            Debug.LogWarning($"[Dispatcher] No handler for '{actionKey}'");
            return;
        }
        actionBridge.Execute(methodName, behaviour, primaryTarget, secondaryTarget);
    }

    public string BuildActionVocabularyPrompt()
    {
        if (actionDefinitions == null || actionDefinitions.Count == 0)
            return "";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== AVAILABLE ACTIONS ===");
        sb.AppendLine("You MAY include one action per response. Use EXACTLY these action_key values:");
        sb.AppendLine();

        foreach (var def in actionDefinitions)
        {
            if (def == null) continue;
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
}
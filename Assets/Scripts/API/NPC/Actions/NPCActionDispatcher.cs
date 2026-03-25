using AYellowpaper.SerializedCollections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using LAS;


namespace LAS
{
    /// <summary>
    /// Routes LLM action commands to execution.
    /// Dictionary maps ActionDefinition ScriptableObject → UnityEvent wired to ActionBridge method.
    /// The SO's actionKey field is used for LLM lookup, but the SO itself is the dictionary key.
    /// This is the central routing hub for all NPC actions in the system.
    /// </summary>
    /// 
    public class NPCActionDispatcher : MonoBehaviour
    {
        /// <summary>
        /// Singleton instance of the NPCActionDispatcher, accessible from anywhere in the code.
        /// Used by NPCManager to dispatch actions from LLM responses.
        /// </summary>
        public static NPCActionDispatcher Instance { get; private set; }

        [Header("Action Handlers")]
        [Tooltip("Map ActionDefinition assets to ActionBridge methods. Wire the UnityEvent to the appropriate Execute* method in the Inspector. Each entry connects an action definition (what the LLM can request) to the code that executes it.")]
        [SerializedDictionary("Action Definition", "Execution Method")]
        public SerializedDictionary<NPCActionDefinition, NPCActionCallback> actionHandlers
            = new SerializedDictionary<NPCActionDefinition, NPCActionCallback>();

        /// <summary>
        /// Internal lookup dictionary mapping action_key strings (from LLM) to NPCActionDefinition objects.
        /// Built during Awake() from the actionHandlers dictionary.
        /// </summary>
        private Dictionary<string, NPCActionDefinition> keyLookup; // action_key → definition

        /// <summary>
        /// Initializes the singleton instance and builds the action key lookup table.
        /// Ensures only one dispatcher exists in the scene.
        /// </summary>
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

        /// <summary>
        /// Builds the keyLookup dictionary from actionHandlers.
        /// Maps each action's unique action_key (e.g., "PICK_UP") to its ActionDefinition object.
        /// Warns if duplicate action keys are found (only the first will be used).
        /// </summary>
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

        /// <summary>
        /// Main dispatch method called by NPCManager when an LLM response contains an action.
        /// Looks up the action by key, resolves the target NPC and any target transforms,
        /// then invokes the appropriate callback to execute the action.
        /// </summary>
        /// <param name="command">The parsed action command from the LLM response.</param>
        /// <param name="registeredNPCs">The list of all registered NPCs in the scene.</param>
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

            // Runtime target-type validation — guards against the LLM supplying an NPC name
            // as the target for an action that requires a physical object (e.g. PICK_UP).
            if (primaryTarget != null && actionDef.validTargetTypes != null && actionDef.validTargetTypes.Length > 0)
            {
                var primaryActionTarget = primaryTarget.GetComponent<IActionTarget>()
                                       ?? primaryTarget.GetComponentInParent<IActionTarget>();
                if (primaryActionTarget != null && !actionDef.IsValidTargetType(primaryActionTarget.Type))
                {
                    Debug.LogWarning($"[Dispatcher] Invalid target type for '{actionDef.displayName}': " +
                                     $"'{primaryTarget.name}' is {primaryActionTarget.Type}, expected " +
                                     $"[{string.Join(", ", actionDef.validTargetTypes)}]. Aborting action.");
                    return;
                }
            }

            Debug.Log($"[Dispatcher] {npcCtrl.npcName} → {actionDef.displayName}" +
                      $"{(primaryTarget != null ? $" → {primaryTarget.name}" : "")}");

            // Invoke the callback (wired to ActionBridge method)
            callback?.Invoke(behaviour, primaryTarget, secondaryTarget);
        }

        /// <summary>
        /// Executes an ordered sequence of action steps on the NPC at <paramref name="npcIndex"/>.
        /// Steps are queued on NPCBehaviourController and run one after another.
        /// Invalid or unresolvable steps are skipped with a warning; the rest still execute.
        /// </summary>
        public void DispatchSequence(int npcIndex, NPCActionSequence sequence, List<NPCController> registeredNPCs)
        {
            if (sequence?.actions == null || sequence.actions.Length == 0) return;
            if (npcIndex < 0 || npcIndex >= registeredNPCs.Count)
            {
                Debug.LogWarning($"[Dispatcher] DispatchSequence: invalid npc_index {npcIndex}");
                return;
            }

            NPCController npcCtrl  = registeredNPCs[npcIndex];
            NPCBehaviourController behaviour = npcCtrl.GetComponent<NPCBehaviourController>();
            if (behaviour == null)
            {
                Debug.LogWarning($"[Dispatcher] {npcCtrl.npcName} has no NPCBehaviourController.");
                return;
            }

            int queued = 0;
            var stepSummary = new System.Text.StringBuilder();

            foreach (var step in sequence.actions)
            {
                if (string.IsNullOrEmpty(step.action_key) || step.action_key == "NONE") continue;

                Transform primary   = NPCActionTargetRegistry.Instance?.Resolve(step.action_target);
                Transform secondary = NPCActionTargetRegistry.Instance?.Resolve(step.action_secondary_target);

                // Target-type validation (same guard as single-action Dispatch)
                if (primary != null && keyLookup.TryGetValue(step.action_key, out var def))
                {
                    if (def.validTargetTypes?.Length > 0)
                    {
                        var targetComp = primary.GetComponent<IActionTarget>()
                                      ?? primary.GetComponentInParent<IActionTarget>();
                        if (targetComp != null && !def.IsValidTargetType(targetComp.Type))
                        {
                            Debug.LogWarning($"[Dispatcher] Sequence step '{step.action_key}': " +
                                $"invalid target type for '{primary.name}' ({targetComp.Type}). Skipping.");
                            continue;
                        }
                    }
                }

                if (behaviour.TryEnqueueAction(step.action_key, primary, secondary))
                {
                    queued++;
                    string label = string.IsNullOrEmpty(step.action_target)
                        ? step.action_key
                        : $"{step.action_key}({step.action_target})";
                    stepSummary.Append(queued > 1 ? " → " : "").Append(label);
                }
            }

            if (queued > 0)
            {
                Debug.Log($"[Dispatcher] {npcCtrl.npcName} → sequence: {stepSummary}");
                behaviour.StartQueuedActions();
            }
        }

        /// <summary>
        /// Direct dispatch method for editor testing or manual action triggering.
        /// Bypasses LLM and action key lookup - directly executes an action on an NPC.
        /// Used primarily by the custom editor (NPCBehaviourControllerEditor) for testing actions in Play mode.
        /// </summary>
        /// <param name="actionDef">The action definition to execute.</param>
        /// <param name="behaviour">The NPC that should perform the action.</param>
        /// <param name="primaryTarget">The primary target transform (can be null if action doesn't require it).</param>
        /// <param name="secondaryTarget">The secondary target transform (can be null if action doesn't require it).</param>
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

        /// <summary>
        /// Generates a formatted string describing all available actions for inclusion in the LLM prompt.
        /// This "vocabulary" tells the LLM what actions it can command NPCs to perform,
        /// including descriptions, required parameters, and valid target names.
        /// Called by NPCManager when building the system prompt for the conversation.
        /// </summary>
        /// <returns>A formatted string listing all actions, their keys, descriptions, and parameters.</returns>
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
                sb.AppendLine("Valid target names in this scene (always use the PRIMARY name in action_target):");
                foreach (var target in registry.GetAllTargets())
                {
                    if (target is ActionTarget at)
                    {
                        var aliasNames = at.GetAllAliases().ToList();
                        if (aliasNames.Count > 0)
                            sb.AppendLine($"  \"{target.TargetName}\"  (also known as: {string.Join(", ", aliasNames.Select(a => $"\"{a}\""))})");
                        else
                            sb.AppendLine($"  \"{target.TargetName}\"");
                    }
                    else
                    {
                        sb.AppendLine($"  \"{target.TargetName}\"");
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine("If no physical action is needed, use action_key: \"NONE\".");
            return sb.ToString();
        }

        /// <summary>
        /// Returns all active action definitions registered in the dispatcher.
        /// Used by the editor UI to populate action selection dropdowns and buttons.
        /// </summary>
        /// <returns>An enumerable of all non-null action definitions in actionHandlers.</returns>
        public IEnumerable<NPCActionDefinition> GetActiveActions()
        {
            foreach (var kvp in actionHandlers)
                if (kvp.Key != null)
                    yield return kvp.Key;
        }
    }
}

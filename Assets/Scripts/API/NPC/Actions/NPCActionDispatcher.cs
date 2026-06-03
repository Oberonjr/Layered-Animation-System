using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using LAS;


namespace LAS
{
    /// <summary>
    /// Routes LLM action commands to execution.
    /// Maintains a list of NPCActionDefinition SOs; each definition's MakeState() method
    /// produces the appropriate INPCState which is handed directly to the NPC's FSM.
    /// This is the central routing hub for all NPC actions in the system.
    /// </summary>
    public class NPCActionDispatcher : MonoBehaviour
    {
        /// <summary>
        /// Singleton instance of the NPCActionDispatcher, accessible from anywhere in the code.
        /// Used by NPCManager to dispatch actions from LLM responses.
        /// </summary>
        public static NPCActionDispatcher Instance { get; private set; }

        [Header("Action Definitions")]
        [Tooltip("All NPCActionDefinition assets available to this dispatcher.")]
        [SerializeField] private List<NPCActionDefinition> actionDefinitions = new List<NPCActionDefinition>();

        /// <summary>
        /// Internal lookup dictionary mapping action_key strings (from LLM) to NPCActionDefinition objects.
        /// Built during Awake() from the actionHandlers dictionary.
        /// </summary>
        private Dictionary<string, NPCActionDefinition> keyLookup; // action_key → definition

        /// <summary>
        /// Returns true if <paramref name="target"/> is a valid target for <paramref name="def"/>.
        /// First checks the registered IActionTarget.Type; if that fails and the action accepts Location,
        /// falls back to a direct GetComponent check — handles objects that carry both an InteractableItem
        /// and a LocationTarget component (e.g. a tray that is also a placement surface).
        /// </summary>
        private static bool IsTargetTypeValid(NPCActionDefinition def, Transform target)
        {
            if (def.validTargetTypes == null || def.validTargetTypes.Length == 0) return true;

            var registered = target.GetComponent<IActionTarget>()
                          ?? target.GetComponentInParent<IActionTarget>();

            if (registered != null && def.IsValidTargetType(registered.Type)) return true;

            // Fallback: check actual components for each valid type the action accepts.
            foreach (var vt in def.validTargetTypes)
            {
                if (vt == TargetType.Location &&
                    (target.GetComponent<LocationTarget>() != null ||
                     target.GetComponentInParent<LocationTarget>() != null))
                    return true;
            }

            return false;
        }

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
        /// Builds the keyLookup dictionary from actionDefinitions.
        /// Maps each action's unique action_key (e.g., "PICK_UP") to its ActionDefinition object.
        /// Warns if duplicate action keys are found (only the first will be used).
        /// </summary>
        private void BuildKeyLookup()
        {
            keyLookup = new Dictionary<string, NPCActionDefinition>();

            foreach (var def in actionDefinitions)
            {
                if (def == null) continue;

                string key = def.actionKey;
                if (keyLookup.ContainsKey(key))
                {
                    Debug.LogWarning($"[Dispatcher] Duplicate action key '{key}' - only first will be used.");
                    continue;
                }

                keyLookup[key] = def;
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

            // Resolve targets
            Transform primaryTarget = string.IsNullOrEmpty(command.action_target)
                ? null
                : NPCActionTargetRegistry.Instance?.Resolve(command.action_target);

            Transform secondaryTarget = string.IsNullOrEmpty(command.action_secondary_target)
                ? null
                : NPCActionTargetRegistry.Instance?.Resolve(command.action_secondary_target);

            // Runtime target-type validation — guards against the LLM supplying an NPC name
            // as the target for an action that requires a physical object (e.g. PICK_UP).
            if (primaryTarget != null && !IsTargetTypeValid(actionDef, primaryTarget))
            {
                var registered = primaryTarget.GetComponent<IActionTarget>()
                              ?? primaryTarget.GetComponentInParent<IActionTarget>();
                Debug.LogWarning($"[Dispatcher] Invalid target type for '{actionDef.displayName}': " +
                                 $"'{primaryTarget.name}' is {registered?.Type.ToString() ?? "unknown"}, expected " +
                                 $"[{string.Join(", ", actionDef.validTargetTypes)}]. Aborting action.");
                return;
            }

            Debug.Log($"[Dispatcher] {npcCtrl.npcName} → {actionDef.displayName}" +
                      $"{(primaryTarget != null ? $" → {primaryTarget.name}" : "")}");

            var state = actionDef.MakeState(primaryTarget, secondaryTarget);
            if (state == null) return;

            if (actionDef.requiresApproach && primaryTarget != null)
            {
                behaviour.FSM.Interrupt(new GoToState(primaryTarget, actionDef.approachRangeType));
                behaviour.FSM.Enqueue(state);
            }
            else
            {
                behaviour.FSM.Interrupt(state);
            }
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

                if (!keyLookup.TryGetValue(step.action_key, out var def))
                {
                    Debug.LogWarning($"[Dispatcher] Sequence step: unknown key '{step.action_key}'.");
                    continue;
                }

                Transform primary   = NPCActionTargetRegistry.Instance?.Resolve(step.action_target);
                Transform secondary = NPCActionTargetRegistry.Instance?.Resolve(step.action_secondary_target);

                if (primary != null && !IsTargetTypeValid(def, primary))
                {
                    var targetComp = primary.GetComponent<IActionTarget>()
                                  ?? primary.GetComponentInParent<IActionTarget>();
                    Debug.LogWarning($"[Dispatcher] Sequence step '{step.action_key}': " +
                        $"invalid target type for '{primary.name}' ({targetComp?.Type.ToString() ?? "unknown"}). Skipping.");
                    continue;
                }

                if (def.requiresApproach && primary != null)
                {
                    Debug.Log($"[Dispatcher] Prepending GoToState({def.approachRangeType}) for {step.action_key} → '{primary.name}'");
                    behaviour.FSM.Enqueue(new GoToState(primary, def.approachRangeType));

                    if (primary.TryGetComponent(out NPCBehaviourController npcController))
                    {
                        npcController.Context.IKController.SetLookAtTarget(behaviour.transform);
                    }
                }

                behaviour.FSM.Enqueue(def.MakeState(primary, secondary));
                queued++;
                string label = string.IsNullOrEmpty(step.action_target)
                    ? step.action_key
                    : $"{step.action_key}({step.action_target})";
                stepSummary.Append(queued > 1 ? " → " : "").Append(label);
            }

            if (queued > 0)
                behaviour.StartQueuedActions();
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

            var state = actionDef.MakeState(primaryTarget, secondaryTarget);
            if (state == null) return;

            if (actionDef.requiresApproach && primaryTarget != null)
            {
                if (primaryTarget.TryGetComponent(out NPCBehaviourController npcController))
                {
                    npcController.Context.IKController.SetLookAtTarget(behaviour.transform);
                }
                
                behaviour.FSM.Interrupt(new GoToState(primaryTarget, actionDef.approachRangeType));
                behaviour.FSM.Enqueue(state);
            }
            else
            {
                behaviour.FSM.Interrupt(state);
            }
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
            sb.AppendLine("If no physical action is needed, use action_key: \"LOOK_AT_PLAYER\".");
            return sb.ToString();
        }

        /// <summary>
        /// Compact single-block vocabulary for the Step 2 action classification prompt.
        /// Avoids multi-line descriptions and alias lists — just keys, one-line hints, and primary target names.
        /// </summary>
        public string BuildCompactActionVocabulary()
        {
            var sb = new System.Text.StringBuilder();

            // One line per action key — key, target slot, first sentence of description
            sb.AppendLine("ACTION KEYS:");
            foreach (var def in actionDefinitions)
            {
                if (def == null) continue;
                string targetSlot = def.requiresTarget ? $" ({def.targetDescription})" : "";
                // First sentence of llmDescription only — keeps it short but preserves disambiguation
                string desc = def.llmDescription ?? "";
                int stop = desc.IndexOfAny(new[] { '.', '\n' });
                if (stop > 0) desc = desc.Substring(0, stop);
                sb.AppendLine($"  {def.actionKey}{targetSlot} — {desc}");
            }
            sb.AppendLine();

            // Primary target names grouped by type — no aliases
            var registry = NPCActionTargetRegistry.Instance;
            if (registry != null)
            {
                sb.AppendLine("VALID TARGET NAMES:");
                foreach (TargetType type in System.Enum.GetValues(typeof(TargetType)))
                {
                    var names = registry.GetTargetsByType(type)
                        .Select(t => $"\"{t.TargetName}\"")
                        .ToList();
                    if (names.Count > 0)
                        sb.AppendLine($"  [{type}] {string.Join(", ", names)}");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Returns all active action definitions registered in the dispatcher.
        /// Used by the editor UI to populate action selection dropdowns and buttons.
        /// </summary>
        /// <returns>An enumerable of all non-null action definitions in actionHandlers.</returns>
        public IEnumerable<NPCActionDefinition> GetActiveActions()
        {
            foreach (var def in actionDefinitions)
                if (def != null)
                    yield return def;
        }
    }
}

using UnityEngine;
using LAS;
/// <summary>
/// Defines one action type.
/// Subclasses override MakeState(primary, secondary) to produce the concrete INPCState
/// that the dispatcher hands to the NPC's FSM.
///</summary>
 

namespace LAS
{
    [CreateAssetMenu(fileName = "NewNPCAction", menuName = "NPC/Action Definition")]
    public class NPCActionDefinition : ScriptableObject
    {
        /// <summary>
        /// The action key used when the NPC performs no physical action.
        /// Reference this constant instead of the literal string "NONE" throughout the codebase.
        /// </summary>
        public const string NoneKey = "NONE";

        [Header("Identity")]
        [Tooltip("Exact string the LLM must output in the 'action_key' field. Uppercase, underscore-separated (e.g., 'PICK_UP', 'LOOK_AT_PLAYER'). This must be unique across all action definitions.")]
        public string actionKey = NoneKey;

        [Tooltip("Human-readable name for editor display and debugging. Shown in inspector buttons and log messages.")]
        public string displayName = "No Action";

        [Header("LLM Prompt")]
        [TextArea(2, 5)]
        [Tooltip("Description injected into the LLM system prompt - tells it when to use this action and what it accomplishes. Be specific about the action's purpose and appropriate usage context.")]
        public string llmDescription = "No action. Use when only dialogue is needed.";

        [Header("Parameters")]
        [Tooltip("Does this action require action_target field? If true, the LLM must provide a target name, and the action will be passed a Transform reference to that target.")]
        public bool requiresTarget = false;

        [Tooltip("Does this action require action_secondary_target field? Very rare - only for complex actions like transferring items between two targets.")]
        public bool requiresSecondaryTarget = false;

        [Tooltip("What the target should be (injected into the LLM prompt). Example: 'the name of the object to pick up' or 'the NPC to hand the object to'. Only relevant if requiresTarget is true.")]
        public string targetDescription = "";

        [Tooltip("What the secondary target should be (injected into the LLM prompt). Only relevant if requiresSecondaryTarget is true.")]
        public string secondaryTargetDescription = "";

        [Header("Target Compatibility")]
        [Tooltip("Which target types can this action use? Empty array = any type is valid. Use this to restrict actions to specific target types (e.g., only NPCs, only InteractableObjects).")]
        public TargetType[] validTargetTypes = new TargetType[0];

        [Header("Editor Visual")]
        [Tooltip("Color for this action in inspector buttons and UI. Helps visually distinguish different actions during testing.")]
        public Color editorColor = Color.white;

        /// <summary>
        /// Is the given target type valid for this action?
        /// If validTargetTypes is empty, all types are valid.
        /// </summary>
        public bool IsValidTargetType(TargetType type)
        {
            if (validTargetTypes == null || validTargetTypes.Length == 0)
                return true;

            foreach (var validType in validTargetTypes)
                if (validType == type)
                    return true;

            return false;
        }

        public virtual NPCActionState MakeState(Transform primary, Transform secondary)
        {
            return new NPCActionState(this);
        }
    }
}

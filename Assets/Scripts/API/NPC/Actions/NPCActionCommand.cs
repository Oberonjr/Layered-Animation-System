using System;

/// <summary>
/// Parsed action payload from an LLM response.
/// Produced by NPCManager after parsing the LLM's JSON output, consumed by NPCActionDispatcher for execution.
/// This struct represents a single action command that an NPC should perform, including which NPC,
/// what action to take, and what targets (if any) to act upon.
/// </summary>
[Serializable]
public class NPCActionCommand
{
    /// <summary>
    /// The zero-based index of the NPC that should perform this action.
    /// Corresponds to the NPC's position in the NPCManager's registeredNPCs list.
    /// Example: 0 = first NPC, 1 = second NPC, etc.
    /// </summary>
    public int npc_index;

    /// <summary>
    /// The action key identifier that matches an NPCActionDefinition.actionKey.
    /// Must be an exact match (case-sensitive, uppercase, underscore-separated).
    /// Examples: "PICK_UP", "LOOK_AT_PLAYER", "HAND_TO_NPC", "NONE"
    /// "NONE" indicates no physical action should be performed.
    /// </summary>
    public string action_key;

    /// <summary>
    /// The name of the primary target for this action.
    /// Resolved by NPCActionTargetRegistry to find the actual Transform.
    /// This should match a registered target's TargetName property.
    /// Examples: "Wrench", "Bob", "Workbench", "Player"
    /// Can be empty/null for actions that don't require a target.
    /// </summary>
    public string action_target;

    /// <summary>
    /// The name of the secondary target for actions requiring two targets.
    /// Resolved by NPCActionTargetRegistry to find the actual Transform.
    /// Used for complex actions like "transfer item from A to B".
    /// Can be empty/null for most actions (very few actions use this).
    /// </summary>
    public string action_secondary_target;
}

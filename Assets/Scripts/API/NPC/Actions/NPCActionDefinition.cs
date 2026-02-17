using UnityEngine;

/// <summary>
/// Defines a single action type that an NPC can perform.
/// One asset per action. The LLM prompt vocabulary is built from these at runtime.
/// </summary>
[CreateAssetMenu(fileName = "NewNPCAction", menuName = "NPC/Action Definition")]
public class NPCActionDefinition : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Exact string the LLM must output. Uppercase, underscore-separated.")]
    public string actionKey = "NONE";
    
    [Tooltip("Human-readable name shown in editor and debug.")]
    public string displayName = "No Action";
    
    [TextArea(2, 4)]
    [Tooltip("Description injected into the LLM prompt so it knows when to use this action.")]
    public string llmDescription = "No action. Use when only dialogue is needed.";
    
    [Header("Parameters")]
    [Tooltip("Does this action require an action_target field?")]
    public bool requiresTarget = false;
    
    [Tooltip("Does this action require an action_secondary_target field?")]
    public bool requiresSecondaryTarget = false;
    
    [Tooltip("Describe what the target should be (injected into prompt).")]
    public string targetDescription = "";
    
    [Tooltip("Describe what the secondary target should be (injected into prompt).")]
    public string secondaryTargetDescription = "";
}

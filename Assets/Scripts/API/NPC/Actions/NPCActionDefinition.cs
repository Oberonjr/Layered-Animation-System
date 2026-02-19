using UnityEngine;

/// <summary>
/// Defines one action type. This asset is the KEY in NPCActionDispatcher's dictionary.
/// The VALUE (UnityEvent) is wired to ActionBridge methods in the dispatcher inspector.
/// </summary>
[CreateAssetMenu(fileName = "NewNPCAction", menuName = "NPC/Action Definition")]
public class NPCActionDefinition : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Exact string the LLM must output. Uppercase, underscore-separated.")]
    public string actionKey = "NONE";

    [Tooltip("Human-readable name for editor display.")]
    public string displayName = "No Action";

    [Header("LLM Prompt")]
    [TextArea(2, 5)]
    [Tooltip("Description injected into LLM prompt - tells it when to use this action.")]
    public string llmDescription = "No action. Use when only dialogue is needed.";

    [Header("Parameters")]
    [Tooltip("Does this action require action_target?")]
    public bool requiresTarget = false;

    [Tooltip("Does this action require action_secondary_target?")]
    public bool requiresSecondaryTarget = false;

    [Tooltip("What the target should be (injected into prompt).")]
    public string targetDescription = "";

    [Tooltip("What the secondary target should be (injected into prompt).")]
    public string secondaryTargetDescription = "";

    [Header("Target Compatibility")]
    [Tooltip("Which target types can this action use? Empty = any type valid.")]
    public TargetType[] validTargetTypes = new TargetType[0];

    [Header("Editor Visual")]
    [Tooltip("Color for this action in inspector buttons.")]
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
}
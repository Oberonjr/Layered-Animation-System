using UnityEngine;

/// <summary>
/// Attach to GameObjects to make them targetable by NPCs.
/// Auto-registers with NPCActionTargetRegistry on Start.
/// The GameObject's name is used as the target name unless a custom name is provided.
/// This component implements IActionTarget, making the object visible to the NPC action system.
/// </summary>
public class ActionTarget : MonoBehaviour, IActionTarget
{
    [Header("Target Configuration")]
    [Tooltip("What type of target this is - determines which actions can use it. NPC: another agent, Player: the user, Location: a waypoint, InteractableObject: something that can be picked up/manipulated.")]
    public TargetType targetType = TargetType.InteractableObject;

    [Tooltip("Optional: override the GameObject name for the target name. This name will be used by the LLM when referencing this target in action commands. Leave empty to use the GameObject's name.")]
    public string customName = "";

    /// <summary>
    /// Returns the custom name if set, otherwise returns the GameObject's name.
    /// This is the identifier used by the LLM and action system to reference this target.
    /// </summary>
    public string TargetName => string.IsNullOrEmpty(customName) ? gameObject.name : customName;

    /// <summary>
    /// Returns the configured target type, determining which actions can interact with this object.
    /// </summary>
    public TargetType Type => targetType;

    /// <summary>
    /// Returns this GameObject's transform for spatial operations (navigation, positioning, etc.).
    /// </summary>
    public Transform Transform => transform;

    /// <summary>
    /// Registers this target with the NPCActionTargetRegistry on scene start.
    /// This makes the target visible to the action system and available for NPC interactions.
    /// </summary>
    void Start()
    {
        NPCActionTargetRegistry.Instance?.Register(this);
    }

    /// <summary>
    /// Unregisters this target from the NPCActionTargetRegistry when destroyed.
    /// Ensures the action system doesn't reference invalid targets.
    /// </summary>
    void OnDestroy()
    {
        NPCActionTargetRegistry.Instance?.Unregister(this);
    }

    /// <summary>
    /// Draws a colored wire sphere in the Scene view when this GameObject is selected.
    /// Provides visual feedback about the target type in the editor.
    /// </summary>
    void OnDrawGizmosSelected()
    {
        // Visual feedback in editor
        Gizmos.color = GetGizmoColor();
        Gizmos.DrawWireSphere(transform.position, 0.3f);
    }

    /// <summary>
    /// Returns a color based on the target type for editor visualization.
    /// Cyan = NPC, Green = Player, Yellow = Location, Magenta = InteractableObject.
    /// </summary>
    /// <returns>The gizmo color corresponding to this target's type.</returns>
    private Color GetGizmoColor()
    {
        return targetType switch
        {
            TargetType.NPC => Color.cyan,
            TargetType.Player => Color.green,
            TargetType.Location => Color.yellow,
            TargetType.InteractableObject => Color.magenta,
            _ => Color.white
        };
    }
}

using UnityEngine;

/// <summary>
/// Attach to GameObjects to make them targetable by NPCs.
/// Auto-registers with NPCActionTargetRegistry on Start.
/// The GameObject's name is used as the target name.
/// </summary>
public class ActionTarget : MonoBehaviour, IActionTarget
{
    [Header("Target Configuration")]
    [Tooltip("What type of target this is - determines which actions can use it.")]
    public TargetType targetType = TargetType.InteractableObject;

    [Tooltip("Optional: override the GameObject name for the target name.")]
    public string customName = "";

    public string TargetName => string.IsNullOrEmpty(customName) ? gameObject.name : customName;
    public TargetType Type => targetType;
    public Transform Transform => transform;

    void Start()
    {
        NPCActionTargetRegistry.Instance?.Register(this);
    }

    void OnDestroy()
    {
        NPCActionTargetRegistry.Instance?.Unregister(this);
    }

    void OnDrawGizmosSelected()
    {
        // Visual feedback in editor
        Gizmos.color = GetGizmoColor();
        Gizmos.DrawWireSphere(transform.position, 0.3f);
    }

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

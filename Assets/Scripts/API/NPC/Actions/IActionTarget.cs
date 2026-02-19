using UnityEngine;

/// <summary>
/// Marker interface for anything that can be an action target.
/// Implementations register themselves with NPCActionTargetRegistry on Start.
/// </summary>
public interface IActionTarget
{
    string TargetName { get; }
    TargetType Type { get; }
    Transform Transform { get; }
}

public enum TargetType
{
    NPC,
    Player,
    Location,
    InteractableObject
}

using UnityEngine;

/// <summary>
/// Singleton bridge that NPCActionDefinition callbacks target.
/// Each action's UnityEvent in the ScriptableObject calls a method here,
/// and this bridge receives the runtime targets from the dispatcher and forwards them.
/// 
/// Wire in ScriptableObject:
///   Action Definition → Execution → callback → ActionBridge.ExecutePickUp (or whatever method)
/// 
/// At runtime:
///   Dispatcher calls: actionDef.callback.Invoke(npc, primaryTarget, secondaryTarget)
///   → UnityEvent fires → calls ExecutePickUp(npc, primaryTarget, secondaryTarget)
///   → This bridge forwards to npc.PickUp(primaryTarget)
/// </summary>
public class ActionBridge : MonoBehaviour
{
    /// <summary>
    /// Singleton instance of the ActionBridge, accessible from anywhere in the code.
    /// Used by the NPCActionDispatcher to route action execution calls.
    /// </summary>
    public static ActionBridge Instance { get; private set; }

    /// <summary>
    /// Initializes the singleton instance on scene load.
    /// Ensures only one ActionBridge exists in the scene.
    /// </summary>
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // ────────────────────────────────────────────────────────────────────────
    // These methods are called by UnityEvents wired in ActionDefinition assets.
    // The dispatcher invokes the UnityEvent with (npc, primary, secondary).
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Makes the NPC look at the player.
    /// Called when the LLM requests a "LOOK_AT_PLAYER" action.
    /// </summary>
    /// <param name="npc">The NPC performing the action.</param>
    /// <param name="_">Unused primary target (player is found automatically).</param>
    /// <param name="__">Unused secondary target.</param>
    public void ExecuteLookAtPlayer(NPCBehaviourController npc, Transform _, Transform __)
    {
        if (npc == null) return;
        npc.LookAtPlayer();
    }

    /// <summary>
    /// Makes the NPC look at a specific target.
    /// The NPC will rotate to face the target's position.
    /// </summary>
    /// <param name="npc">The NPC performing the action.</param>
    /// <param name="target">The transform to look at (e.g., another NPC, object, or location).</param>
    /// <param name="_">Unused secondary target.</param>
    public void ExecuteLookAt(NPCBehaviourController npc, Transform target, Transform _)
    {
        if (npc == null || target == null) return;
        npc.LookAt(target);
    }

    /// <summary>
    /// Makes the NPC navigate to a specific target location.
    /// Uses Unity's NavMeshAgent for pathfinding and movement.
    /// </summary>
    /// <param name="npc">The NPC performing the action.</param>
    /// <param name="target">The transform to navigate to (can be a location, object, or other NPC).</param>
    /// <param name="_">Unused secondary target.</param>
    public void ExecuteGoTo(NPCBehaviourController npc, Transform target, Transform _)
    {
        if (npc == null || target == null) return;
        npc.GoTo(target);
    }

    /// <summary>
    /// Makes the NPC walk to and pick up an object.
    /// The object is parented to the NPC's item slot and physics are disabled while held.
    /// </summary>
    /// <param name="npc">The NPC performing the action.</param>
    /// <param name="target">The object to pick up (must be an InteractableObject).</param>
    /// <param name="_">Unused secondary target.</param>
    public void ExecutePickUp(NPCBehaviourController npc, Transform target, Transform _)
    {
        if (npc == null || target == null) return;
        npc.PickUp(target);
    }

    /// <summary>
    /// Makes the NPC hold out their currently held object for the player to take.
    /// The object is repositioned to an "offer" position in front of the NPC.
    /// In VR, the player can then grab the object using XR interactions.
    /// </summary>
    /// <param name="npc">The NPC performing the action (must be holding an object).</param>
    /// <param name="_">Unused primary target.</param>
    /// <param name="__">Unused secondary target.</param>
    public void ExecuteHandToPlayer(NPCBehaviourController npc, Transform _, Transform __)
    {
        if (npc == null) return;
        npc.HandToPlayer();
    }

    /// <summary>
    /// Makes the NPC walk to another NPC and transfer their held object to them.
    /// The object is moved from this NPC's item slot to the target NPC's item slot.
    /// </summary>
    /// <param name="npc">The NPC performing the action (must be holding an object).</param>
    /// <param name="target">The target NPC to hand the object to (must have NPCBehaviourController).</param>
    /// <param name="_">Unused secondary target.</param>
    public void ExecuteHandToNPC(NPCBehaviourController npc, Transform target, Transform _)
    {
        if (npc == null || target == null) return;
        var targetBehaviour = target.GetComponent<NPCBehaviourController>();
        if (targetBehaviour != null)
            npc.HandToNPC(targetBehaviour);
        else
            Debug.LogWarning($"[ActionBridge] HandToNPC: '{target.name}' has no NPCBehaviourController.");
    }

    /// <summary>
    /// Makes the NPC walk to another NPC and take the object they are holding.
    /// The object is transferred from the target NPC's item slot to this NPC's item slot.
    /// </summary>
    /// <param name="npc">The NPC performing the action.</param>
    /// <param name="target">The target NPC to grab from (must be holding an object and have NPCBehaviourController).</param>
    /// <param name="_">Unused secondary target.</param>
    public void ExecuteGrabFrom(NPCBehaviourController npc, Transform target, Transform _)
    {
        if (npc == null || target == null) return;
        var targetBehaviour = target.GetComponent<NPCBehaviourController>();
        if (targetBehaviour != null)
            npc.GrabFrom(targetBehaviour);
        else
            Debug.LogWarning($"[ActionBridge] GrabFrom: '{target.name}' has no NPCBehaviourController.");
    }

    /// <summary>
    /// Makes the NPC face the target and gesture toward them, requesting their held object.
    /// This is a non-forceful action - the target NPC decides whether to comply.
    /// Currently triggers a log message; can be extended with animations.
    /// </summary>
    /// <param name="npc">The NPC performing the request.</param>
    /// <param name="target">The target to request from (typically another NPC).</param>
    /// <param name="_">Unused secondary target.</param>
    public void ExecuteRequestFrom(NPCBehaviourController npc, Transform target, Transform _)
    {
        if (npc == null || target == null) return;
        npc.RequestFrom(target);
    }

    /// <summary>
    /// Makes the NPC return to their idle/home position and reset their state.
    /// Stops all current behaviors and clears the navigation path.
    /// </summary>
    /// <param name="npc">The NPC performing the action.</param>
    /// <param name="_">Unused primary target.</param>
    /// <param name="__">Unused secondary target.</param>
    public void ExecuteReturnToIdle(NPCBehaviourController npc, Transform _, Transform __)
    {
        if (npc == null) return;
        npc.ReturnToIdle();
    }
}
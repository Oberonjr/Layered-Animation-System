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
    public static ActionBridge Instance { get; private set; }

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

    public void ExecuteLookAtPlayer(NPCBehaviourController npc, Transform _, Transform __)
    {
        if (npc == null) return;
        npc.LookAtPlayer();
    }

    public void ExecuteLookAt(NPCBehaviourController npc, Transform target, Transform _)
    {
        if (npc == null || target == null) return;
        npc.LookAt(target);
    }

    public void ExecuteGoTo(NPCBehaviourController npc, Transform target, Transform _)
    {
        if (npc == null || target == null) return;
        npc.GoTo(target);
    }

    public void ExecutePickUp(NPCBehaviourController npc, Transform target, Transform _)
    {
        if (npc == null || target == null) return;
        npc.PickUp(target);
    }

    public void ExecuteHandToPlayer(NPCBehaviourController npc, Transform _, Transform __)
    {
        if (npc == null) return;
        npc.HandToPlayer();
    }

    public void ExecuteHandToNPC(NPCBehaviourController npc, Transform target, Transform _)
    {
        if (npc == null || target == null) return;
        var targetBehaviour = target.GetComponent<NPCBehaviourController>();
        if (targetBehaviour != null)
            npc.HandToNPC(targetBehaviour);
        else
            Debug.LogWarning($"[ActionBridge] HandToNPC: '{target.name}' has no NPCBehaviourController.");
    }

    public void ExecuteGrabFrom(NPCBehaviourController npc, Transform target, Transform _)
    {
        if (npc == null || target == null) return;
        var targetBehaviour = target.GetComponent<NPCBehaviourController>();
        if (targetBehaviour != null)
            npc.GrabFrom(targetBehaviour);
        else
            Debug.LogWarning($"[ActionBridge] GrabFrom: '{target.name}' has no NPCBehaviourController.");
    }

    public void ExecuteRequestFrom(NPCBehaviourController npc, Transform target, Transform _)
    {
        if (npc == null || target == null) return;
        npc.RequestFrom(target);
    }

    public void ExecuteReturnToIdle(NPCBehaviourController npc, Transform _, Transform __)
    {
        if (npc == null) return;
        npc.ReturnToIdle();
    }
}
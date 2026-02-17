using UnityEngine;
using System.Reflection;
using System.Collections.Generic;

/// <summary>
/// Executes NPC actions by method name via reflection.
/// Called by NPCActionDispatcher.Execute(methodName, ...).
/// All public Execute* methods are valid dispatch targets.
/// </summary>
public class ActionBridge : MonoBehaviour
{
    private Dictionary<string, MethodInfo> methodCache;

    void Awake()
    {
        // Cache all Execute* methods on this class for fast lookup
        methodCache = new Dictionary<string, MethodInfo>();
        var methods = GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
        foreach (var m in methods)
        {
            if (m.Name.StartsWith("Execute"))
                methodCache[m.Name] = m;
        }
        Debug.Log($"[ActionBridge] Cached {methodCache.Count} execute methods.");
    }

    /// <summary>
    /// Called by NPCActionDispatcher. Looks up the method by name and invokes it.
    /// </summary>
    public void Execute(string methodName, NPCBehaviourController npc, Transform primary, Transform secondary)
    {
        if (string.IsNullOrEmpty(methodName))
        {
            Debug.LogWarning("[ActionBridge] Execute called with empty method name.");
            return;
        }

        if (methodCache == null) Awake();

        if (!methodCache.TryGetValue(methodName, out MethodInfo method))
        {
            Debug.LogWarning($"[ActionBridge] No method named '{methodName}'. " +
                             $"Available: {string.Join(", ", methodCache.Keys)}");
            return;
        }

        method.Invoke(this, new object[] { npc, primary, secondary });
    }

    // ─── Action Implementations ───────────────────────────────────────────────

    public void ExecuteLookAtPlayer(NPCBehaviourController npc, Transform _, Transform __)
        => npc.LookAtPlayer();

    public void ExecuteLookAt(NPCBehaviourController npc, Transform target, Transform _)
    {
        if (target == null) { LogMissingTarget("LOOK_AT"); return; }
        npc.LookAt(target);
    }

    public void ExecuteGoTo(NPCBehaviourController npc, Transform target, Transform _)
    {
        if (target == null) { LogMissingTarget("GO_TO"); return; }
        npc.GoTo(target);
    }

    public void ExecutePickUp(NPCBehaviourController npc, Transform target, Transform _)
    {
        if (target == null) { LogMissingTarget("PICK_UP"); return; }
        npc.PickUp(target);
    }

    public void ExecuteHandToPlayer(NPCBehaviourController npc, Transform _, Transform __)
        => npc.HandToPlayer();

    public void ExecuteHandToNPC(NPCBehaviourController npc, Transform target, Transform _)
    {
        if (target == null) { LogMissingTarget("HAND_TO_NPC"); return; }
        var tb = target.GetComponent<NPCBehaviourController>();
        if (tb != null) npc.HandToNPC(tb);
        else Debug.LogWarning($"[ActionBridge] '{target.name}' has no NPCBehaviourController.");
    }

    public void ExecuteGrabFrom(NPCBehaviourController npc, Transform target, Transform _)
    {
        if (target == null) { LogMissingTarget("GRAB_FROM"); return; }
        var tb = target.GetComponent<NPCBehaviourController>();
        if (tb != null) npc.GrabFrom(tb);
        else Debug.LogWarning($"[ActionBridge] '{target.name}' has no NPCBehaviourController.");
    }

    public void ExecuteRequestFrom(NPCBehaviourController npc, Transform target, Transform _)
    {
        if (target == null) { LogMissingTarget("REQUEST_FROM"); return; }
        npc.RequestFrom(target);
    }

    public void ExecuteReturnToIdle(NPCBehaviourController npc, Transform _, Transform __)
        => npc.ReturnToIdle();

    private void LogMissingTarget(string actionKey)
        => Debug.LogWarning($"[ActionBridge] {actionKey}: target is null. " +
                            $"Check target name matches NPCActionTargetRegistry.");
}
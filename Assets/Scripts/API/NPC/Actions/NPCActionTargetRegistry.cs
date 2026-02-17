using UnityEngine;
using System.Collections.Generic;
using AYellowpaper.SerializedCollections;

/// <summary>
/// Scene-level registry of named interactable targets (objects, locations, NPCs).
/// NPCBehaviourControllers and the dispatcher look up targets here by string name.
/// Add entries in the Inspector - no code changes needed for new targets.
/// </summary>
public class NPCActionTargetRegistry : MonoBehaviour
{
    public static NPCActionTargetRegistry Instance { get; private set; }

    [Header("Registered Targets")]
    [Tooltip("Map a name string to a Transform in the scene. " +
             "Use the same names you want the LLM to reference.")]
    [SerializedDictionary("Target Name", "Transform")]
    public SerializedDictionary<string, Transform> targets = new SerializedDictionary<string, Transform>();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[TargetRegistry] Duplicate registry found - destroying.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// Returns the transform for a given name, or null if not found.
    /// Case-insensitive lookup.
    /// </summary>
    public Transform Resolve(string targetName)
    {
        if (string.IsNullOrEmpty(targetName))
            return null;

        // Exact match first
        if (targets.TryGetValue(targetName, out Transform t))
            return t;

        // Case-insensitive fallback
        foreach (var kvp in targets)
        {
            if (string.Equals(kvp.Key, targetName, System.StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }

        Debug.LogWarning($"[TargetRegistry] Target not found: '{targetName}'");
        return null;
    }

    /// <summary>
    /// Returns all registered target names - used to inject into LLM prompt.
    /// </summary>
    public IEnumerable<string> GetAllTargetNames() => targets.Keys;
}

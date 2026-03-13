using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Singleton registry for all action targets in the scene.
/// IActionTarget implementations register themselves here on Start.
/// Provides categorized lookups by TargetType and name-based resolution for the action system.
/// Maintains both a name-based lookup and a type-categorized list for efficient querying.
/// </summary>
public class NPCActionTargetRegistry : MonoBehaviour
{
    /// <summary>
    /// Singleton instance of the NPCActionTargetRegistry, accessible from anywhere in the code.
    /// Used by ActionTarget components, NPCActionDispatcher, and NPCBehaviourController.
    /// </summary>
    public static NPCActionTargetRegistry Instance { get; private set; }

    [Header("Runtime Registry (Read-Only)")]
    [Tooltip("Debug display showing all registered targets organized by type. This updates automatically in Play mode.")]
    [SerializeField][TextArea(10, 20)] private string registryContents = "Registry will populate at runtime...";

    /// <summary>Dictionary mapping target names to their IActionTarget implementations for O(1) name-based lookup.</summary>
    private Dictionary<string, IActionTarget> allTargets = new Dictionary<string, IActionTarget>();

    /// <summary>Dictionary organizing targets by their TargetType for efficient filtered queries.</summary>
    private Dictionary<TargetType, List<IActionTarget>> targetsByType = new Dictionary<TargetType, List<IActionTarget>>();

    /// <summary>
    /// Initializes the singleton instance and prepares categorized lists for each TargetType.
    /// Ensures only one registry exists in the scene.
    /// </summary>
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[TargetRegistry] Duplicate registry - destroying.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Initialize categorized lists
        foreach (TargetType type in System.Enum.GetValues(typeof(TargetType)))
            targetsByType[type] = new List<IActionTarget>();
    }

    /// <summary>
    /// Updates the inspector display text every frame during Play mode.
    /// Shows all registered targets organized by type for debugging.
    /// </summary>
    void Update()
    {
        // Update inspector display every frame in Play mode
        if (Application.isPlaying)
            UpdateRegistryDisplay();
    }

    /// <summary>
    /// Registers a target with the registry, making it available for NPC actions.
    /// Called automatically by IActionTarget implementations (e.g., ActionTarget component) on Start.
    /// Warns if a duplicate name is found (overwrites the previous entry).
    /// </summary>
    /// <param name="target">The IActionTarget implementation to register.</param>
    public void Register(IActionTarget target)
    {
        if (target == null) return;

        string name = target.TargetName;
        if (allTargets.ContainsKey(name))
        {
            Debug.LogWarning($"[TargetRegistry] Duplicate target name: '{name}'. Overwriting.");
        }

        allTargets[name] = target;
        targetsByType[target.Type].Add(target);

        Debug.Log($"[TargetRegistry] Registered {target.Type}: '{name}'");
        UpdateRegistryDisplay();
    }

    /// <summary>
    /// Removes a target from the registry.
    /// Called automatically by IActionTarget implementations (e.g., ActionTarget component) on OnDestroy.
    /// </summary>
    /// <param name="target">The IActionTarget implementation to unregister.</param>
    public void Unregister(IActionTarget target)
    {
        if (target == null) return;

        string name = target.TargetName;
        allTargets.Remove(name);
        targetsByType[target.Type].Remove(target);
        UpdateRegistryDisplay();
    }

    /// <summary>
    /// Builds a formatted string showing all registered targets organized by type.
    /// Updates the registryContents field for display in the Inspector.
    /// Called whenever targets are registered/unregistered and each frame during Play mode.
    /// </summary>
    private void UpdateRegistryDisplay()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"═══ Registered Targets ({allTargets.Count}) ═══\n");

        foreach (TargetType type in System.Enum.GetValues(typeof(TargetType)))
        {
            var targets = targetsByType[type];
            if (targets.Count == 0) continue;

            sb.AppendLine($"▼ {type} ({targets.Count})");
            foreach (var t in targets.OrderBy(x => x.TargetName))
            {
                sb.AppendLine($"  • {t.TargetName}");
            }
            sb.AppendLine();
        }

        registryContents = sb.ToString();
    }

    /// <summary>
    /// Resolves a target name string to its Transform component.
    /// Used by NPCActionDispatcher to convert LLM-provided target names into actual transforms.
    /// Performs case-insensitive matching if exact match fails.
    /// </summary>
    /// <param name="targetName">The name of the target to resolve.</param>
    /// <returns>The Transform of the matching target, or null if not found.</returns>
    public Transform Resolve(string targetName)
    {
        if (string.IsNullOrEmpty(targetName)) return null;

        // 1. Exact match (fastest path).
        if (allTargets.TryGetValue(targetName, out IActionTarget target))
            return target.Transform;

        // 2. Case-insensitive exact match.
        var match = allTargets.Values.FirstOrDefault(t =>
            string.Equals(t.TargetName, targetName, System.StringComparison.OrdinalIgnoreCase));
        if (match != null) return match.Transform;

        // 3. Contains match — handles LLM partial names (e.g. "scalpel" → "Scalpel_01").
        //    Prefer the registered name whose length is closest to the query to avoid over-broad matches.
        string lower = targetName.ToLower();
        var containsMatch = allTargets.Values
            .Where(t => t.TargetName.ToLower().Contains(lower) || lower.Contains(t.TargetName.ToLower()))
            .OrderBy(t => Mathf.Abs(t.TargetName.Length - targetName.Length))
            .FirstOrDefault();

        if (containsMatch != null)
        {
            Debug.Log($"[TargetRegistry] Fuzzy match: '{targetName}' → '{containsMatch.TargetName}'");
            return containsMatch.Transform;
        }

        Debug.LogWarning($"[TargetRegistry] Target not found: '{targetName}'");
        return null;
    }

    /// <summary>
    /// Gets all registered targets of a specific type.
    /// Used by NPCBehaviourController (e.g., to find Player-type targets) and editor tools.
    /// </summary>
    /// <param name="type">The TargetType to filter by.</param>
    /// <returns>An enumerable of all targets matching the specified type.</returns>
    public IEnumerable<IActionTarget> GetTargetsByType(TargetType type)
        => targetsByType.ContainsKey(type) ? targetsByType[type] : System.Linq.Enumerable.Empty<IActionTarget>();

    /// <summary>
    /// Gets all registered targets regardless of type.
    /// Used by the editor UI to populate target selection dropdowns.
    /// </summary>
    /// <returns>An enumerable of all registered IActionTarget implementations.</returns>
    public IEnumerable<IActionTarget> GetAllTargets() => allTargets.Values;

    /// <summary>
    /// Gets the names of all registered targets.
    /// Used by NPCActionDispatcher to build the action vocabulary prompt for the LLM.
    /// </summary>
    /// <returns>An enumerable of all target names currently in the registry.</returns>
    public IEnumerable<string> GetAllTargetNames() => allTargets.Keys;
}
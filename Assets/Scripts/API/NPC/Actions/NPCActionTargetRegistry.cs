using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Singleton registry. IActionTarget implementations register themselves here on Start.
/// Provides categorized lookups by TargetType.
/// </summary>
public class NPCActionTargetRegistry : MonoBehaviour
{
    public static NPCActionTargetRegistry Instance { get; private set; }

    [Header("Runtime Registry (Read-Only)")]
    [SerializeField][TextArea(10, 20)] private string registryContents = "Registry will populate at runtime...";

    private Dictionary<string, IActionTarget> allTargets = new Dictionary<string, IActionTarget>();
    private Dictionary<TargetType, List<IActionTarget>> targetsByType = new Dictionary<TargetType, List<IActionTarget>>();

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

    void Update()
    {
        // Update inspector display every frame in Play mode
        if (Application.isPlaying)
            UpdateRegistryDisplay();
    }

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

    public void Unregister(IActionTarget target)
    {
        if (target == null) return;

        string name = target.TargetName;
        allTargets.Remove(name);
        targetsByType[target.Type].Remove(target);
        UpdateRegistryDisplay();
    }

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

    public Transform Resolve(string targetName)
    {
        if (string.IsNullOrEmpty(targetName)) return null;

        if (allTargets.TryGetValue(targetName, out IActionTarget target))
            return target.Transform;

        var match = allTargets.Values.FirstOrDefault(t =>
            string.Equals(t.TargetName, targetName, System.StringComparison.OrdinalIgnoreCase));

        if (match != null)
            return match.Transform;

        Debug.LogWarning($"[TargetRegistry] Target not found: '{targetName}'");
        return null;
    }

    public IEnumerable<IActionTarget> GetTargetsByType(TargetType type)
        => targetsByType.ContainsKey(type) ? targetsByType[type] : System.Linq.Enumerable.Empty<IActionTarget>();

    public IEnumerable<IActionTarget> GetAllTargets() => allTargets.Values;

    public IEnumerable<string> GetAllTargetNames() => allTargets.Keys;
}
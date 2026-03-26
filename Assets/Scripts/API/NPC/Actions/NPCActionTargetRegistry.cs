using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using LAS;

namespace LAS
{
    /// <summary>
    /// Singleton registry for all action targets in the scene.
    /// ActionTarget components register themselves here on Start.
    ///
    /// Resolution order for Resolve(name):
    ///   1. Exact primary-name match
    ///   2. Case-insensitive primary-name match
    ///   3. Alias map (exact, then partial)
    ///   4. Substring match on primary names
    ///   5. Levenshtein fuzzy match (tolerates up to 2 character edits)
    /// </summary>
    public class NPCActionTargetRegistry : MonoBehaviour
    {
        public static NPCActionTargetRegistry Instance { get; private set; }

        [Header("Runtime Registry (Read-Only)")]
        [Tooltip("Debug display — all registered targets and their aliases. Updates at runtime.")]
        [SerializeField][TextArea(10, 25)] private string registryContents = "Registry will populate at runtime...";

        // Primary name → target
        private Dictionary<string, IActionTarget> allTargets = new Dictionary<string, IActionTarget>();

        // Alias (lower-case) → target  (populated by Register and RegisterAliases)
        private Dictionary<string, IActionTarget> aliasMap = new Dictionary<string, IActionTarget>();

        // Type-bucketed lookup
        private Dictionary<TargetType, List<IActionTarget>> targetsByType
            = new Dictionary<TargetType, List<IActionTarget>>();

        // ── Unity lifecycle ───────────────────────────────────────────────────────

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            foreach (TargetType type in System.Enum.GetValues(typeof(TargetType)))
                targetsByType[type] = new List<IActionTarget>();
        }

        void Update()
        {
            if (Application.isPlaying)
                UpdateRegistryDisplay();
        }

        // ── Registration ─────────────────────────────────────────────────────────

        /// <summary>
        /// Registers a target by its primary TargetName and indexes any inspector-defined aliases.
        /// </summary>
        public void Register(IActionTarget target)
        {
            if (target == null) return;

            string name = target.TargetName;
            if (allTargets.ContainsKey(name))
                Debug.LogWarning($"[TargetRegistry] Duplicate target name: '{name}'. Overwriting.");

            allTargets[name] = target;
            targetsByType[target.Type].Add(target);

            // Index all existing aliases (inspector-defined and any previously LLM-generated)
            if (target is ActionTarget at)
            {
                if (at.aliases.Count > 0)
                    IndexAliases(target, at.aliases);
                if (at.llmAliases.Count > 0)
                    IndexAliases(target, at.llmAliases);
            }
            UpdateRegistryDisplay();
        }

        /// <summary>Removes a target and all its aliases from the registry.</summary>
        public void Unregister(IActionTarget target)
        {
            if (target == null) return;

            allTargets.Remove(target.TargetName);
            targetsByType[target.Type].Remove(target);

            var keysToRemove = aliasMap
                .Where(kvp => kvp.Value == target)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (var k in keysToRemove) aliasMap.Remove(k);

            UpdateRegistryDisplay();
        }

        /// <summary>
        /// Registers additional aliases for an already-registered target.
        /// Called by AliasGenerator after LLM alias generation completes.
        /// </summary>
        public void RegisterAliases(IActionTarget target, IEnumerable<string> aliases)
        {
            if (target == null || aliases == null) return;
            IndexAliases(target, aliases);
            UpdateRegistryDisplay();
        }

        // ── Lookup ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves a name string to the matching target's Transform.
        /// Uses five successive strategies before giving up.
        /// </summary>
        public Transform Resolve(string targetName)
        {
            if (string.IsNullOrEmpty(targetName)) return null;

            // 1. Exact primary match (fastest path)
            if (allTargets.TryGetValue(targetName, out IActionTarget t)) return t.Transform;

            // 2. Case-insensitive primary match
            var ciMatch = allTargets.Values.FirstOrDefault(x =>
                string.Equals(x.TargetName, targetName, System.StringComparison.OrdinalIgnoreCase));
            if (ciMatch != null) return ciMatch.Transform;

            string lower = targetName.ToLowerInvariant();

            // 3a. Exact alias match (alias map is pre-lowercased)
            if (aliasMap.TryGetValue(lower, out IActionTarget aliasExact))
                return aliasExact.Transform;

            // 3b. Partial alias match
            var aliasPartial = aliasMap
                .Where(kvp => kvp.Key.Contains(lower) || lower.Contains(kvp.Key))
                .OrderBy(kvp => System.Math.Abs(kvp.Key.Length - lower.Length))
                .Select(kvp => kvp.Value)
                .FirstOrDefault();
            if (aliasPartial != null)
                return aliasPartial.Transform;

            // 4. Substring match on primary names
            var subMatch = allTargets.Values
                .Where(x => x.TargetName.ToLowerInvariant().Contains(lower) ||
                             lower.Contains(x.TargetName.ToLowerInvariant()))
                .OrderBy(x => System.Math.Abs(x.TargetName.Length - targetName.Length))
                .FirstOrDefault();
            if (subMatch != null)
                return subMatch.Transform;

            // 5. Levenshtein fuzzy match — tolerates up to 2 character edits (min name length 4)
            IActionTarget fuzzyBest = null;
            int fuzzyBestDist = int.MaxValue;

            foreach (var candidate in allTargets.Values)
            {
                string cname = candidate.TargetName.ToLowerInvariant();
                if (cname.Length < 4) continue;
                int dist = Levenshtein(lower, cname);
                if (dist <= 2 && dist < fuzzyBestDist) { fuzzyBestDist = dist; fuzzyBest = candidate; }
            }
            foreach (var kvp in aliasMap)
            {
                if (kvp.Key.Length < 4) continue;
                int dist = Levenshtein(lower, kvp.Key);
                if (dist <= 2 && dist < fuzzyBestDist) { fuzzyBestDist = dist; fuzzyBest = kvp.Value; }
            }

            if (fuzzyBest != null)
            {
                Debug.LogWarning($"[TargetRegistry] Fuzzy match (dist={fuzzyBestDist}): '{targetName}' → '{fuzzyBest.TargetName}'");
                return fuzzyBest.Transform;
            }

            Debug.LogWarning($"[TargetRegistry] Target not found: '{targetName}'");
            return null;
        }

        /// <summary>Returns the IActionTarget with the given primary name, or null.</summary>
        public IActionTarget GetByPrimaryName(string name)
        {
            allTargets.TryGetValue(name, out var t);
            return t;
        }

        public IEnumerable<IActionTarget> GetTargetsByType(TargetType type)
            => targetsByType.ContainsKey(type) ? targetsByType[type] : Enumerable.Empty<IActionTarget>();

        public IEnumerable<IActionTarget> GetAllTargets() => allTargets.Values;
        public IEnumerable<string> GetAllTargetNames() => allTargets.Keys;

        // ── Internal helpers ──────────────────────────────────────────────────────

        private void IndexAliases(IActionTarget target, IEnumerable<string> names)
        {
            foreach (string alias in names)
            {
                string key = alias?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(key)) continue;
                if (!aliasMap.ContainsKey(key))
                    aliasMap[key] = target;
            }
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
                foreach (var target in targets.OrderBy(x => x.TargetName))
                {
                    sb.Append($"  • {target.TargetName}");
                    if (target is ActionTarget at)
                    {
                        var allAliases = at.GetAllAliases().ToList();
                        if (allAliases.Count > 0)
                            sb.Append($"  [{string.Join(", ", allAliases)}]");
                    }
                    sb.AppendLine();
                }
                sb.AppendLine();
            }

            registryContents = sb.ToString();
        }

        /// <summary>
        /// Levenshtein edit distance — used as a last-resort fuzzy match.
        /// Early-exits if the length difference exceeds 3 (can't be within threshold).
        /// </summary>
        private static int Levenshtein(string s, string t)
        {
            int m = s.Length, n = t.Length;
            if (m == 0) return n;
            if (n == 0) return m;
            if (System.Math.Abs(m - n) > 3) return int.MaxValue;

            int[] prev = new int[n + 1];
            int[] curr = new int[n + 1];
            for (int j = 0; j <= n; j++) prev[j] = j;

            for (int i = 1; i <= m; i++)
            {
                curr[0] = i;
                for (int j = 1; j <= n; j++)
                {
                    int cost = s[i - 1] == t[j - 1] ? 0 : 1;
                    curr[j] = System.Math.Min(
                        System.Math.Min(curr[j - 1] + 1, prev[j] + 1),
                        prev[j - 1] + cost);
                }
                var tmp = prev; prev = curr; curr = tmp;
            }
            return prev[n];
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LAS
{
    /// <summary>
    /// Generates LLM-based alias lists for action targets at runtime startup.
    /// Aliases allow players to refer to targets by informal names, abbreviations, or
    /// common misspellings. Generated aliases are stored on the target and registered
    /// with NPCActionTargetRegistry for fuzzy resolution.
    ///
    /// Only InteractableItem and LocationTarget types are processed —
    /// NPC and Player names are already well-defined from the scenario config.
    /// </summary>
    public static class AliasGenerator
    {
        private static readonly LLMGenerationOptions AliasOpts = new LLMGenerationOptions
        {
            temperature   = 0.2f,
            topP          = 0.9f,
            topK          = 20f,
            maxTokens     = 120,
            repeatPenalty = 1.0f
        };

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Generates aliases for all eligible registered targets in sequence.
        /// Targets that already have inspector-defined aliases or LLM aliases are skipped.
        /// Non-blocking — call as a background coroutine from NPCManager.
        /// </summary>
        /// <param name="registry">The active target registry.</param>
        /// <param name="provider">LLM provider to use for generation.</param>
        /// <param name="scenarioTitle">Optional scenario name for context (improves alias quality).</param>
        /// <param name="host">MonoBehaviour used to start sub-coroutines.</param>
        public static IEnumerator GenerateAll(
            NPCActionTargetRegistry registry,
            LLMProviderBase provider,
            string scenarioTitle,
            MonoBehaviour host)
        {
            if (registry == null || provider == null) yield break;

            var targets = registry.GetAllTargets()
                .OfType<ActionTarget>()
                .Where(t => (t.Type == TargetType.InteractableObject ||
                             t.Type == TargetType.Location          ||
                             t.Type == TargetType.NPC)
                            && t.llmAliases.Count == 0) // only skip if already LLM-generated; custom aliases are fine
                .ToList();

            if (targets.Count == 0) yield break;

            Debug.Log($"[AliasGenerator] Generating aliases for {targets.Count} target(s) in background...");

            foreach (var target in targets)
            {
                yield return host.StartCoroutine(GenerateForTarget(target, provider, scenarioTitle, registry));
                yield return null; // spread load across frames
            }

            Debug.Log("[AliasGenerator] Alias generation complete.");
        }

        // ── Internal ──────────────────────────────────────────────────────────────

        private static IEnumerator GenerateForTarget(
            ActionTarget target,
            LLMProviderBase provider,
            string scenarioTitle,
            NPCActionTargetRegistry registry)
        {
            string name = target.TargetName;

            // For NPCs, include the character role for better alias quality
            string role = "";
            if (target.Type == TargetType.NPC)
            {
                var npcCtrl = target.GetComponent<NPCController>();
                role = npcCtrl?.characterRole ?? "";
            }

            string prompt = BuildPrompt(name, target.Type, role, scenarioTitle);

            string result = null;
            yield return provider.SendRequest(prompt, AliasOpts, r => result = r);

            if (string.IsNullOrEmpty(result)) yield break;

            var aliases = ParseJsonArray(result);
            if (aliases.Count == 0) yield break;

            target.llmAliases.AddRange(aliases);
            registry.RegisterAliases(target, aliases);
            Debug.Log($"[AliasGenerator] '{name}' → {aliases.Count} alias(es): {string.Join(", ", aliases)}");
        }

        private static string BuildPrompt(string name, TargetType type, string role, string scenarioTitle)
        {
            string context = string.IsNullOrEmpty(scenarioTitle)
                ? "a medical training simulation"
                : $"a medical training simulation: {scenarioTitle}";

            if (type == TargetType.NPC)
            {
                string roleHint = string.IsNullOrEmpty(role) ? "" : $" (role: {role})";
                return
                    $"In {context}, list 5-8 informal ways a student might refer to a person named \"{name}\"{roleHint}.\n" +
                    $"Include: shortened names, title variants, role-based references (e.g. 'the doctor'), and common informal nicknames.\n" +
                    $"Output ONLY a JSON array of lowercase strings — no explanations, no keys:\n" +
                    $"[\"alias1\", \"alias2\", \"alias3\"]";
            }

            string typeHint = type == TargetType.Location ? "location or area" : "item or object";
            return
                $"In {context}, list 5-8 informal ways a student might refer to the {typeHint} named \"{name}\".\n" +
                $"Include: common abbreviations, informal terms, and likely misspellings.\n" +
                $"Output ONLY a JSON array of lowercase strings — no explanations, no keys:\n" +
                $"[\"alias1\", \"alias2\", \"alias3\"]";
        }

        /// <summary>Parses a bare JSON string array from LLM output.</summary>
        private static List<string> ParseJsonArray(string raw)
        {
            var result = new List<string>();
            try
            {
                int start = raw.IndexOf('[');
                int end   = raw.LastIndexOf(']') + 1;
                if (start < 0 || end <= start) return result;

                string json = raw.Substring(start, end - start);
                // Wrap so JsonUtility can deserialise it
                var wrapper = JsonUtility.FromJson<StringArrayWrapper>($"{{\"items\":{json}}}");
                if (wrapper?.items != null)
                {
                    foreach (string item in wrapper.items)
                    {
                        string s = item?.Trim().ToLowerInvariant();
                        if (!string.IsNullOrEmpty(s))
                            result.Add(s);
                    }
                }
            }
            catch { /* non-fatal */ }
            return result;
        }

        [Serializable]
        private class StringArrayWrapper { public string[] items; }
    }
}

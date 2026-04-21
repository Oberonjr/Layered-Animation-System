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
    /// Only InteractableItem, LocationTarget, and NPC types are processed.
    /// Generation parameters and the prompt template are configured via
    /// <see cref="AliasGeneratorConfig"/> on NPCManager.
    /// </summary>
    public static class AliasGenerator
    {
        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Generates aliases for all eligible registered targets in sequence.
        /// Targets that already have LLM-generated aliases are skipped; targets
        /// with inspector-defined aliases are still processed (those are supplementary).
        /// Non-blocking — call as a background coroutine from NPCManager.
        /// </summary>
        /// <param name="registry">The active action target registry.</param>
        /// <param name="provider">LLM provider to use for generation.</param>
        /// <param name="config">Generation parameters and prompt template.</param>
        /// <param name="scenarioContextHint">
        /// Short description of the scenario context passed into the prompt
        /// (e.g. "a hospital training simulation"). Falls back to "an interactive simulation"
        /// if empty.
        /// </param>
        /// <param name="host">MonoBehaviour used to start sub-coroutines.</param>
        public static IEnumerator GenerateAll(
            NPCActionTargetRegistry registry,
            LLMProviderBase         provider,
            AliasGeneratorConfig    config,
            string                  scenarioContextHint,
            MonoBehaviour           host)
        {
            if (registry == null || provider == null || config == null) yield break;

            var targets = registry.GetAllTargets()
                .OfType<ActionTarget>()
                .Where(t => (t.Type == TargetType.InteractableObject ||
                             t.Type == TargetType.Location          ||
                             t.Type == TargetType.NPC)
                            && t.llmAliases.Count == 0)
                .ToList();

            if (targets.Count == 0) yield break;

            Debug.Log($"[AliasGenerator] Generating aliases for {targets.Count} target(s) in background...");

            foreach (var target in targets)
            {
                yield return host.StartCoroutine(
                    GenerateForTarget(target, provider, config, scenarioContextHint, registry));
                yield return null; // spread load across frames
            }

            Debug.Log("[AliasGenerator] Alias generation complete.");
        }

        // ── Internal ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Generates and registers aliases for a single target.
        /// </summary>
        private static IEnumerator GenerateForTarget(
            ActionTarget            target,
            LLMProviderBase         provider,
            AliasGeneratorConfig    config,
            string                  scenarioContextHint,
            NPCActionTargetRegistry registry)
        {
            string name = target.TargetName;

            // For NPCs, include the character role for better alias quality.
            string role = "";
            if (target.Type == TargetType.NPC)
            {
                var npcCtrl = target.GetComponent<NPCController>();
                role = npcCtrl?.characterRole ?? "";
            }

            string prompt = BuildPrompt(name, target.Type, role, scenarioContextHint, config);

            string result = null;
            yield return provider.SendRequest(prompt, config.options, r => result = r);

            if (string.IsNullOrEmpty(result)) yield break;

            var aliases = ParseJsonArray(result);
            if (aliases.Count == 0) yield break;

            target.llmAliases.AddRange(aliases);
            registry.RegisterAliases(target, aliases);
            Debug.Log($"[AliasGenerator] '{name}' → {aliases.Count} alias(es): {string.Join(", ", aliases)}");
        }

        /// <summary>
        /// Builds the alias-generation prompt by substituting placeholders in the config template.
        /// Placeholders: {context}, {typeHint}, {name}, {roleHint}.
        /// </summary>
        private static string BuildPrompt(
            string               name,
            TargetType           type,
            string               role,
            string               scenarioContextHint,
            AliasGeneratorConfig config)
        {
            string context  = string.IsNullOrEmpty(scenarioContextHint)
                ? "an interactive simulation"
                : scenarioContextHint;

            string typeHint = type switch
            {
                TargetType.NPC      => "person",
                TargetType.Location => "location or area",
                _                   => "item or object"
            };

            string roleHint = (!string.IsNullOrEmpty(role) && type == TargetType.NPC)
                ? $" (role: {role})"
                : "";

            return config.promptTemplate
                .Replace("{context}",  context)
                .Replace("{typeHint}", typeHint)
                .Replace("{name}",     name)
                .Replace("{roleHint}", roleHint);
        }

        /// <summary>Parses a bare JSON string array from LLM output, returning a trimmed lowercase list.</summary>
        private static List<string> ParseJsonArray(string raw)
        {
            var result = new List<string>();
            try
            {
                int start = raw.IndexOf('[');
                int end   = raw.LastIndexOf(']') + 1;
                if (start < 0 || end <= start) return result;

                string json = raw.Substring(start, end - start);
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

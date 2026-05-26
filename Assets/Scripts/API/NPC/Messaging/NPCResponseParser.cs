using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace LAS
{
    /// <summary>
    /// Stateless parser for raw LLM output strings.
    /// All methods are static; no scene state is held here.
    /// </summary>
    public static class NPCResponseParser
    {
        /// <summary>
        /// Tries to parse a Step 1 dialogue response from a potentially malformed LLM string.
        /// Strategy 1: Standard JSON parse via JsonUtility.
        /// Strategy 2: Regex extraction of npc_index + longest non-field quoted string as dialogue.
        /// Returns true if a usable NPCResponse was extracted.
        /// </summary>
        public static bool TryParseDialogueResponse(
            string                    raw,
            IReadOnlyList<NPCController> npcs,
            ScenarioConfig            scenario,
            out NPCResponse           result)
        {
            result = null;
            if (string.IsNullOrEmpty(raw)) return false;

            // Strategy 1: Standard JSON parse (preferred path).
            try
            {
                int si = raw.IndexOf('{');
                int ei = raw.LastIndexOf('}') + 1;
                if (si >= 0 && ei > si)
                {
                    var parsed = JsonUtility.FromJson<NPCResponse>(raw.Substring(si, ei - si));
                    if (parsed != null && !string.IsNullOrEmpty(parsed.dialogue))
                    {
                        result = parsed;
                        return true;
                    }
                }
            }
            catch { }

            // Strategy 2: Regex fallback.
            try
            {
                result = new NPCResponse();

                var indexMatch = Regex.Match(raw, "\"npc_index\"\\s*:\\s*(\\d+)");
                if (indexMatch.Success)
                    result.npc_index = int.Parse(indexMatch.Groups[1].Value);

                var thoughtMatch = Regex.Match(raw, "\"internal_thought\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                string extractedThought = thoughtMatch.Success ? thoughtMatch.Groups[1].Value : null;
                if (extractedThought != null)
                    result.internal_thought = extractedThought;

                var fieldNames = new HashSet<string>
                    { "npc_index", "dialogue", "internal_thought", "action_key", "action_target", "action_secondary_target" };

                var candidates = Regex
                    .Matches(raw, "\"((?:[^\"\\\\]|\\\\.){5,})\"")
                    .Cast<Match>()
                    .Select(m => m.Groups[1].Value)
                    .Where(v => !fieldNames.Contains(v) &&
                                (extractedThought == null || v != extractedThought))
                    .OrderBy(v => raw.IndexOf(v, StringComparison.Ordinal))
                    .ToList();

                if (candidates.Count == 0) return false;

                var metaPrefixSet = BuildFallbackMetaPrefixes(npcs, scenario);
                string[] metaPrefixes = new string[metaPrefixSet.Count];
                metaPrefixSet.CopyTo(metaPrefixes, 0);

                var cleaned = candidates.Select(c =>
                {
                    string s = c.TrimStart();
                    foreach (var prefix in metaPrefixes)
                    {
                        if (s.ToLower().StartsWith(prefix))
                        {
                            int colonPos = s.IndexOf(':');
                            if (colonPos > 0 && colonPos < s.Length - 5)
                                return s.Substring(colonPos + 1).Trim();
                        }
                    }
                    return s;
                }).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

                if (cleaned.Count > 0)
                {
                    result.dialogue = cleaned.OrderByDescending(s => s.Length).First();
                    Debug.LogWarning($"[NPCResponseParser] Fallback dialogue extraction used. Recovered: \"{result.dialogue}\"");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NPCResponseParser] Fallback parse also failed: {ex.Message}\nRaw: {raw}");
            }

            return false;
        }

        /// <summary>
        /// Tries to parse an NPCActionSequence from a Step 2 LLM response.
        /// Accepts the sequence format {"actions":[...]} and the legacy single-action format.
        /// Returns null if parsing fails or yields no usable steps.
        /// </summary>
        public static NPCActionSequence ParseActionSequence(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;

            int si = raw.IndexOf('{');
            int ei = raw.LastIndexOf('}') + 1;
            if (si < 0 || ei <= si) return null;
            string json = raw.Substring(si, ei - si);

            try
            {
                var seq = JsonUtility.FromJson<NPCActionSequence>(json);
                if (seq?.actions != null && seq.actions.Length > 0 &&
                    !string.IsNullOrEmpty(seq.actions[0].action_key))
                    return seq;
            }
            catch { }

            try
            {
                var single = JsonUtility.FromJson<NPCActionOnly>(json);
                if (single != null && !string.IsNullOrEmpty(single.action_key))
                {
                    return new NPCActionSequence
                    {
                        actions = new[]
                        {
                            new NPCActionStep
                            {
                                action_key              = single.action_key,
                                action_target           = single.action_target           ?? "",
                                action_secondary_target = single.action_secondary_target ?? ""
                            }
                        }
                    };
                }
            }
            catch { }

            Debug.LogWarning($"[NPCResponseParser] Could not parse action sequence (non-fatal). Raw: {raw}");
            return null;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static HashSet<string> BuildFallbackMetaPrefixes(
            IReadOnlyList<NPCController> npcs,
            ScenarioConfig               scenario)
        {
            var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "description of",
                "npc response:",
                "character says:",
                "response:",
                "reply:",
                "as she responds",
                "as he responds",
                "as she says",
                "as he says",
                "as they say"
            };

            if (npcs != null)
            {
                foreach (var npc in npcs)
                {
                    if (!string.IsNullOrWhiteSpace(npc.npcName))
                        prefixes.Add(npc.npcName.ToLower());
                    if (!string.IsNullOrWhiteSpace(npc.characterRole))
                        prefixes.Add(npc.characterRole.ToLower());
                }
            }

            if (scenario?.characters != null)
            {
                foreach (var ch in scenario.characters)
                {
                    if (!string.IsNullOrWhiteSpace(ch.name)) prefixes.Add(ch.name.ToLower());
                    if (!string.IsNullOrWhiteSpace(ch.role)) prefixes.Add(ch.role.ToLower());
                }
            }

            return prefixes;
        }
    }
}

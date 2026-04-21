using System;
using UnityEngine;

namespace LAS
{
    /// <summary>
    /// Inspector-exposed configuration for alias generation.
    /// Controls the LLM parameters and prompt template used when generating informal
    /// names for scene targets (items, locations, NPCs).
    ///
    /// Exposed on NPCManager under the "Alias Generation" header.
    /// Right-click NPCManager in the inspector and choose
    /// "Reset Alias Generator to Default" to restore the built-in prompt and parameters.
    /// </summary>
    [Serializable]
    public class AliasGeneratorConfig
    {
        [Tooltip("LLM parameters used during alias generation. Low temperature is recommended for consistent, predictable output.")]
        public LLMGenerationOptions options = new LLMGenerationOptions
        {
            temperature   = 0.2f,
            topP          = 0.9f,
            topK          = 20f,
            maxTokens     = 120,
            repeatPenalty = 1.0f
        };

        [Tooltip(
            "Template for the alias generation prompt. Available placeholders:\n" +
            "  {context}   — scenario context hint (from JSON 'context_hint' or title)\n" +
            "  {typeHint}  — 'person', 'item or object', or 'location or area'\n" +
            "  {name}      — the target's primary registered name\n" +
            "  {roleHint}  — role suffix for NPC targets, e.g. ' (role: Surgeon)'; empty for items/locations\n\n" +
            "The LLM must output a bare JSON array of lowercase strings.")]
        [TextArea(6, 14)]
        public string promptTemplate = DefaultPromptTemplate;

        // ── Defaults ──────────────────────────────────────────────────────────────

        /// <summary>
        /// The built-in prompt template restored by the 'Reset to Default' context-menu action.
        /// </summary>
        public const string DefaultPromptTemplate =
            "In {context}, list 5-8 informal ways someone might refer to the {typeHint} named \"{name}\"{roleHint}.\n" +
            "Include: shortened names, common abbreviations, informal terms, role-based references, and likely misspellings.\n" +
            "Output ONLY a JSON array of lowercase strings — no explanations, no keys:\n" +
            "[\"alias1\", \"alias2\", \"alias3\"]";

        /// <summary>
        /// Default LLM generation parameters restored by the 'Reset to Default' context-menu action.
        /// </summary>
        public static readonly LLMGenerationOptions DefaultOptions = new LLMGenerationOptions
        {
            temperature   = 0.2f,
            topP          = 0.9f,
            topK          = 20f,
            maxTokens     = 120,
            repeatPenalty = 1.0f
        };
    }
}

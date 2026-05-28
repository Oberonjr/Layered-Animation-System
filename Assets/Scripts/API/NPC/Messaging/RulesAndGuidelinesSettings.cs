using System;
using UnityEngine;

namespace LAS
{
    /// <summary>
    /// Data holder for the two rule/guideline blocks injected into every LLM prompt by NPCManager.
    ///
    /// <b>Critical Rules</b> are high-priority, conversation-flow constraints the LLM must obey
    /// regardless of scenario (e.g. "never answer your own question"). They are rendered first
    /// and phrased as hard prohibitions.
    ///
    /// <b>Behavior Guidelines</b> are softer, domain-specific instructions that shape NPC
    /// personality and interaction style. They are rendered after the critical rules and may
    /// vary between scenarios (teaching approach, tone, energy level, etc.).
    ///
    /// Both blocks are read from the scenario JSON first. The override bools force the
    /// inspector values to be used instead. The section label for behavior guidelines is also
    /// configurable so scenarios can name the section appropriately (e.g. "TEACHING BEHAVIOR",
    /// "ROLE-PLAY GUIDELINES", "TONE AND MANNER").
    ///
    /// Exposed on NPCManager under the "Rules and Guidelines" header.
    /// </summary>
    [Serializable]
    public class RulesAndGuidelinesSettings
    {
        // ── Defaults (referenced by NPCManager context-menu resets) ───────────────

        /// <summary>
        /// Built-in critical rules text. Scenario-neutral; applies to any NPC conversation.
        /// </summary>
        public const string DefaultCriticalRules =
            "• If YOU just asked a question, wait for someone else to answer — never answer your own question.\n" +
            "• Never have multiple back-and-forth exchanges with yourself.\n" +
            "• If the player uses a vague term that could refer to a known item, " +
              "ask for clarification using the actual item name.\n" +
            "• If the player says something completely unrecognisable or off-topic, " +
              "state clearly that you don't understand and redirect to the current task.";

        /// <summary>
        /// Built-in behavior guidelines text. Scenario-neutral defaults that improve
        /// NPC naturalness without making any domain assumptions.
        /// </summary>
        public const string DefaultBehaviorGuidelines =
            "• Stay in character at all times — your personality, role, and current state define " +
              "how you react, not just what you say.\n" +
            "• Keep responses natural and proportionate to the moment; " +
              "avoid unnecessary monologues unless the situation calls for it.\n" +
            "• Acknowledge what was just said before steering the conversation elsewhere.\n" +
            "• Express uncertainty in character — never break immersion to admit you don't know something.\n" +
            "• Match your energy to the situation: calm when things are routine, " +
              "more reactive and focused when they are not.\n" +
            "• Use the other person's name occasionally when addressing them directly, " +
              "but not in every single line.";

        // ── Critical Rules ────────────────────────────────────────────────────────

        [Tooltip("When enabled, the critical rules text below overrides the 'critical_rules' array in the scenario JSON. " +
                 "Leave disabled to let the JSON value take precedence (inspector text acts as fallback only).")]
        public bool overrideCriticalRules = false;

        [Tooltip("High-priority conversation-flow rules injected into every LLM prompt as a dedicated section. " +
                 "Used as fallback when the scenario JSON has no 'critical_rules' array.\n\n" +
                 "Right-click NPCManager and choose 'Extract Critical Rules from JSON' to pull them from the loaded scenario, " +
                 "or 'Reset Critical Rules to Default' to restore the built-in text.")]
        [TextArea(5, 12)]
        public string criticalRules = DefaultCriticalRules;

        // ── Behavior Guidelines ───────────────────────────────────────────────────

        [Tooltip("When enabled, the behavior guidelines text below overrides the 'behavior_guidelines' array in the scenario JSON. " +
                 "Leave disabled to let the JSON value take precedence (inspector text acts as fallback only).")]
        public bool overrideBehaviorGuidelines = false;

        [Tooltip("Overrides the section header label used in the LLM prompt (e.g. 'TEACHING BEHAVIOR', 'ROLE-PLAY GUIDELINES'). " +
                 "Leave empty to use the label from the scenario JSON, or 'BEHAVIOR GUIDELINES' if the JSON has none.")]
        public string behaviorSectionLabelOverride = "";

        [Tooltip("Domain-specific NPC behavior guidelines injected into every LLM prompt. " +
                 "Used as fallback when the scenario JSON has no 'behavior_guidelines' array.\n\n" +
                 "Right-click NPCManager and choose 'Extract Behavior Guidelines from JSON' to pull them from the loaded scenario, " +
                 "or 'Reset Behavior Guidelines to Default' to restore the built-in generic text.")]
        [TextArea(5, 12)]
        public string behaviorGuidelines = DefaultBehaviorGuidelines;
    }
}

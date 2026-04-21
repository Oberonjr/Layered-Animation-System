using System;
using UnityEngine;

namespace LAS
{
    /// <summary>
    /// Data holder for the two auto-conversation prompts used by NPCManager when
    /// no player input is present: the NPC-to-NPC follow-up prompt and the idle-player prompt.
    ///
    /// Both prompts are read from the scenario JSON first. The override bools force the
    /// inspector values to be used instead, which is useful when testing or prototyping
    /// without modifying the JSON file.
    ///
    /// Exposed on NPCManager under the "Conversation Prompts" header.
    /// Right-click NPCManager in the inspector and choose one of the
    /// "Reset … to Default" context-menu actions to restore built-in text.
    /// </summary>
    [Serializable]
    public class ConversationPromptsSettings
    {
        // ── Defaults (referenced by NPCManager context-menu resets) ───────────────

        /// <summary>
        /// Built-in NPC-to-NPC conversation prompt.
        /// Scenario-neutral — the loaded characters and history supply domain context.
        /// </summary>
        public const string DefaultNPCConversationPrompt =
            "Continue the conversation naturally — pick up on what was just said, " +
            "ask a follow-up question, or move the topic forward.";

        /// <summary>
        /// Built-in idle-player prompt.
        /// Scenario-neutral — does not assume role, domain, or activity type.
        /// </summary>
        public const string DefaultIdlePrompt =
            "The player has been quiet for a while. " +
            "One of you should check in, ask if they have questions, or invite them to participate.";

        // ── NPC-to-NPC prompt ─────────────────────────────────────────────────────

        [Tooltip("When enabled, the NPC conversation prompt below overrides whatever is in the scenario JSON. " +
                 "Leave disabled to let the JSON value take precedence (inspector text acts as fallback only).")]
        public bool overrideNPCConversationPrompt = false;

        [Tooltip("Prompt injected as a System turn when NPCs continue talking to each other in auto-conversation mode. " +
                 "Used as fallback when the scenario JSON has no 'npc_conversation_prompt' field. " +
                 "Should be scenario-neutral — NPC characters and conversation history supply the domain context.")]
        [TextArea(2, 5)]
        public string npcConversationPrompt = DefaultNPCConversationPrompt;

        // ── Idle-player prompt ────────────────────────────────────────────────────

        [Tooltip("When enabled, the idle prompt below overrides whatever is in the scenario JSON. " +
                 "Leave disabled to let the JSON value take precedence (inspector text acts as fallback only).")]
        public bool overrideIdlePrompt = false;

        [Tooltip("Prompt injected as a Room turn when the player has been silent past the idle threshold. " +
                 "Used as fallback when the scenario JSON has no 'idle_player_prompt' field.")]
        [TextArea(2, 5)]
        public string idlePrompt = DefaultIdlePrompt;
    }
}

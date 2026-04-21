using System;
using System.Collections.Generic;
using LAS;

namespace LAS
{
    [Serializable]
    public class ScenarioConfig
    {
        public ScenarioInfo scenario;
        public CharacterInfo[] characters;
        public PlayerCharacterInfo player_character;
        public ProgressionStep[] required_progression_steps;
        public SystemInstructions system_instructions;
        public ResponseFormatExample example_response_format;
        public ConversationInitialization conversation_initialization;
    }

    [Serializable]
    public class ScenarioInfo
    {
        public string title;
        public string description;
        public string setting;
        public string timeframe;

        /// <summary>
        /// Short phrase describing the scenario context for the alias generator
        /// (e.g. "a hospital emergency room simulation"). Falls back to the title if empty.
        /// </summary>
        public string context_hint;
    }

    [Serializable]
    public class CharacterInfo
    {
        public string name;
        public string role;
        public string personality;
        public string background;
        public string communication_style;
        public string current_state;
        public string teaching_approach;
    }

    [Serializable]
    public class PlayerCharacterInfo
    {
        public string role;
        public string description;
        public string expectations;
    }

    [Serializable]
    public class ProgressionStep
    {
        public int step_id;
        public string title;
        public string description;
        public string[] key_points;
        public string[] teaching_moments;
    }

    [Serializable]
    public class SystemInstructions
    {
        public string[] core_rules;
        public string[] response_format;
        public string[] interaction_guidelines;

        /// <summary>
        /// Rules about conversational flow injected as a dedicated high-priority section
        /// (e.g. "never answer your own question", "wait for a response before continuing").
        /// If absent, NPCManager falls back to the inspector critical-rules field.
        /// </summary>
        public string[] critical_rules;

        /// <summary>Domain-specific NPC behaviour guidelines (e.g. teaching approach, tone).</summary>
        public string[] behavior_guidelines;

        /// <summary>
        /// Legacy alias for <see cref="behavior_guidelines"/> — retained so that existing JSON files
        /// that use "teaching_behavior" continue to work. Prefer "behavior_guidelines" in new scenarios.
        /// NPCManager reads behavior_guidelines first; if empty, falls back to this field.
        /// </summary>
        public string[] teaching_behavior;

        /// <summary>
        /// Label used as the section header for behavior_guidelines in the LLM prompt
        /// (e.g. "TEACHING BEHAVIOR", "ROLE-PLAY GUIDELINES"). Defaults to "BEHAVIOR GUIDELINES"
        /// if absent.
        /// </summary>
        public string behavior_section_label;
    }

    [Serializable]
    public class ResponseFormatExample
    {
        public int npc_index;
        public string dialogue;
        public string action_key;
        public string action_target;
        public string action_secondary_target;
        public string internal_thought;
    }

    [Serializable]
    public class ConversationInitialization
    {
        public int first_speaker_index;
        public string opening_prompt;
        public string context;

        /// <summary>
        /// Prompt used when NPCs continue talking to each other unprompted
        /// (auto-conversation mode). If absent, NPCManager falls back to its
        /// inspector fallback field.
        /// </summary>
        public string npc_conversation_prompt;

        /// <summary>
        /// Prompt used when the player has been idle and an NPC should check in.
        /// If absent, NPCManager falls back to its inspector fallback field.
        /// </summary>
        public string idle_player_prompt;
    }

}

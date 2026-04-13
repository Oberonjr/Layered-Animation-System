using System;
using LAS;


namespace LAS
{
    /// <summary>
    /// Game-level data structures shared across the NPC system.
    /// Ollama-specific wire types (OllamaRequest, OllamaOptions, etc.) live in OllamaProvider.
    /// OpenAI wire types live in OpenAIChatProvider.
    /// </summary>

    /// <summary>
    /// Parsed NPC response from LLM JSON output.
    /// Contains dialogue, action command, and internal thought.
    /// </summary>
    [Serializable]
    public class NPCResponse
    {
        /// <summary>Index of the NPC that should speak/act (0-based, matches NPCManager's registered NPC list).</summary>
        public int npc_index;

        /// <summary>What the NPC says out loud (displayed in chat UI).</summary>
        public string dialogue;

        /// <summary>The action to perform (e.g., "PICK_UP", "GO_TO", "NONE"). Must match an NPCActionDefinition.actionKey.</summary>
        public string action_key;              // e.g. "PICK_UP", "GO_TO", "NONE"

        /// <summary>Primary target name from registry (e.g., "Wrench", "Player", "Workbench"). Empty if action doesn't require a target.</summary>
        public string action_target;           // primary target name from registry

        /// <summary>Secondary target name for complex two-target actions. Rarely used.</summary>
        public string action_secondary_target; // for two-party actions

        /// <summary>The NPC's internal reasoning (not displayed to player, used for debugging and consistency).</summary>
        public string internal_thought;
    }

    /// <summary>
    /// Action-only response from the second LLM classification step.
    /// Contains only the physical action fields — no dialogue or identity fields.
    /// Parsed from the action classification prompt (Step 2 of the two-step LLM pipeline).
    /// </summary>
    [Serializable]
    public class NPCActionOnly
    {
        /// <summary>The action to perform. Must match an NPCActionDefinition.actionKey, or "NONE".</summary>
        public string action_key;

        /// <summary>Primary target name from registry. Empty if the action requires no target.</summary>
        public string action_target;

        /// <summary>Secondary target name for two-party actions (e.g. hand object from one NPC to another).</summary>
        public string action_secondary_target;
    }

    /// <summary>
    /// One step in a multi-step action sequence returned by Step 2 (action classification).
    /// </summary>
    [Serializable]
    public class NPCActionStep
    {
        /// <summary>Action key matching an NPCActionDefinition. "NONE" is valid and skipped by the dispatcher.</summary>
        public string action_key;

        /// <summary>Primary target name from the registry. Empty when the action requires no target.</summary>
        public string action_target;

        /// <summary>Secondary target name for two-party actions. Empty for most actions.</summary>
        public string action_secondary_target;
    }

    /// <summary>
    /// Ordered sequence of action steps produced by Step 2 (action classification).
    /// Supports single-step actions as well as multi-step chains
    /// (e.g. PICK_UP → HAND_TO_PLAYER when the player asks "give me the scalpel").
    /// </summary>
    [Serializable]
    public class NPCActionSequence
    {
        /// <summary>Ordered list of steps to execute. Dispatcher processes them in index order.</summary>
        public NPCActionStep[] actions;
    }

    /// <summary>
    /// Enum defining the type of chat message for UI display formatting.
    /// </summary>
    public enum MessageType
    {
        /// <summary>Message sent by the player/user.</summary>
        Player,

        /// <summary>Message (dialogue) from an NPC.</summary>
        NPC,

        /// <summary>System message (scenario descriptions, errors, etc.).</summary>
        System
    }

}

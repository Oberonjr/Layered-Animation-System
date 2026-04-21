using System;

namespace LAS
{
    /// <summary>
    /// Identifies the type of entity that initiated a conversation turn.
    /// Drives how NPCManager frames the LLM prompt and which NPC should respond.
    /// </summary>
    public enum SpeakerType
    {
        /// <summary>The human player typed or spoke a message.</summary>
        Player,

        /// <summary>
        /// An NPC in the scene spoke — used for NPC-to-NPC exchanges where one NPC's
        /// dialogue feeds directly into the next NPC's response.
        /// </summary>
        NPC,

        /// <summary>
        /// An internal system directive (conversation initialization, error recovery).
        /// Never displayed as spoken dialogue; used to give the LLM a stage direction
        /// without attributing it to any character.
        /// </summary>
        System,

        /// <summary>
        /// An ambient scene event or game-world trigger with no specific speaker.
        /// Use this for timed events, environment changes, or any non-character prompt
        /// that should cause NPCs to react without implying a person initiated it.
        /// </summary>
        Room
    }

    /// <summary>
    /// Describes one stimulus in the conversation pipeline — who spoke (or what happened),
    /// who they were addressing, and what was said or occurred.
    ///
    /// Passed to <see cref="NPCManager.GetNPCResponse"/> instead of a raw (string, int) pair
    /// so the prompt builder can frame each LLM request correctly depending on whether the
    /// input came from the player, another NPC, a system directive, or a scene event.
    /// </summary>
    [Serializable]
    public struct ConversationTurn
    {
        /// <summary>What kind of entity initiated this turn.</summary>
        public SpeakerType speakerType;

        /// <summary>
        /// Index into <c>NPCManager.registeredNPCs</c> for the entity that produced this turn.
        /// Set to <c>-1</c> for Player, System, and Room turns.
        /// </summary>
        public int speakerIndex;

        /// <summary>
        /// Index into <c>NPCManager.registeredNPCs</c> for the intended audience.
        /// <c>-1</c> means the turn is addressed to the room, the player, or no specific NPC.
        /// For NPC-to-NPC turns this is the NPC who should respond next.
        /// For initialization turns this is the NPC who should speak first.
        /// </summary>
        public int targetIndex;

        /// <summary>
        /// The text content of this turn — what was said, the scene event description,
        /// or the system directive. Never empty.
        /// </summary>
        public string content;

        // ── Factory helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Creates a turn representing a player message.
        /// </summary>
        /// <param name="message">What the player typed or said.</param>
        /// <param name="targetNPCIndex">
        /// Which NPC the player is addressing (-1 = undirected; NPCManager will auto-detect).
        /// </param>
        public static ConversationTurn FromPlayer(string message, int targetNPCIndex = -1)
            => new ConversationTurn
            {
                speakerType  = SpeakerType.Player,
                speakerIndex = -1,
                targetIndex  = targetNPCIndex,
                content      = message
            };

        /// <summary>
        /// Creates a turn representing an NPC speaking, used to trigger another NPC's response.
        /// </summary>
        /// <param name="speakerNPCIndex">Index of the NPC who just spoke.</param>
        /// <param name="dialogue">The NPC's dialogue that the target should respond to.</param>
        /// <param name="targetNPCIndex">Which NPC should respond (-1 = auto-select).</param>
        public static ConversationTurn FromNPC(int speakerNPCIndex, string dialogue, int targetNPCIndex = -1)
            => new ConversationTurn
            {
                speakerType  = SpeakerType.NPC,
                speakerIndex = speakerNPCIndex,
                targetIndex  = targetNPCIndex,
                content      = dialogue
            };

        /// <summary>
        /// Creates a turn representing an internal system directive (e.g. conversation initialization).
        /// The directive is framed as a stage direction — never shown as spoken dialogue.
        /// </summary>
        /// <param name="directive">The internal instruction or opening prompt text.</param>
        /// <param name="targetNPCIndex">Which NPC should act on the directive (-1 = auto-select).</param>
        public static ConversationTurn FromSystem(string directive, int targetNPCIndex = -1)
            => new ConversationTurn
            {
                speakerType  = SpeakerType.System,
                speakerIndex = -1,
                targetIndex  = targetNPCIndex,
                content      = directive
            };

        /// <summary>
        /// Creates a turn representing an ambient scene event or game-world trigger.
        /// Use when the environment or game logic (not a character) needs to prompt NPC reactions.
        /// </summary>
        /// <param name="eventDescription">Description of the scene event or situation.</param>
        /// <param name="targetNPCIndex">Which NPC should react (-1 = any available NPC).</param>
        public static ConversationTurn FromRoom(string eventDescription, int targetNPCIndex = -1)
            => new ConversationTurn
            {
                speakerType  = SpeakerType.Room,
                speakerIndex = -1,
                targetIndex  = targetNPCIndex,
                content      = eventDescription
            };
    }
}

namespace LAS
{
    /// <summary>
    /// Provider-agnostic structured prompt passed to every LLMProviderBase.SendRequest call.
    /// Separating system context, conversation history, and the current user turn lets each
    /// provider serialize them in its native API format, and keeps the system content
    /// identical across turns so provider-side KV/prompt caches can reuse it.
    /// </summary>
    public class LLMRequest
    {
        /// <summary>
        /// Static scenario context: setting, characters, rules, response format.
        /// Must not change between turns within a session so that prompt caching kicks in.
        /// </summary>
        public string systemContent;

        /// <summary>
        /// Prior conversation turns in chronological order, already mapped to API roles.
        /// Player turns use role "user"; NPC turns use role "assistant" with a speaker-name prefix.
        /// System/scenario messages are excluded.
        /// </summary>
        public LLMMessage[] history;

        /// <summary>
        /// The current turn: player input (or NPC/room trigger) plus any per-turn instructions.
        /// This is the only part that changes every request.
        /// </summary>
        public string userContent;
    }

    /// <summary>A single role-labelled message in an LLM conversation turn.</summary>
    public class LLMMessage
    {
        /// <summary>"user" or "assistant"</summary>
        public string role;
        public string content;
    }
}

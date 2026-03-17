using System;
using LAS;


namespace LAS
{
    /// <summary>
    /// Data structures for Ollama API communication and NPC action system.
    /// Contains serializable classes for API requests/responses and action system callbacks.
    /// </summary>

    /// <summary>
    /// Request structure for Ollama LLM API calls.
    /// </summary>
    /// 
    [Serializable]
    public class OllamaRequest
    {
        /// <summary>The name of the Ollama model to use (e.g., "llama2", "mistral").</summary>
        public string model;

        /// <summary>The prompt text to send to the LLM, including system instructions and conversation history.</summary>
        public string prompt;

        /// <summary>Whether to use streaming response (true) or wait for complete response (false).</summary>
        public bool stream;

        /// <summary>Generation parameters controlling LLM behavior (temperature, top_p, etc.).</summary>
        public OllamaOptions options;
    }

    /// <summary>
    /// LLM generation parameters for controlling response quality and creativity.
    /// </summary>
    [Serializable]
    public class OllamaOptions
    {
        /// <summary>Controls randomness (0.0 = deterministic, 2.0 = very random). Typical range: 0.1-1.0.</summary>
        public float temperature = 0.7f;

        /// <summary>Nucleus sampling threshold (0.0-1.0). Lower = more focused, higher = more diverse.</summary>
        public float top_p = 0.9f;

        /// <summary>Top-K sampling limit (1-100). Limits vocabulary to K most likely tokens.</summary>
        public float top_k = 40f;

        /// <summary>Maximum tokens to generate (-1 = unlimited, positive number = limit).</summary>
        public int num_predict = -1; // -1 = unlimited

        /// <summary>Penalty for repeating tokens (1.0 = no penalty, higher = stronger penalty against repetition).</summary>
        public float repeat_penalty = 1.1f;
    }

    /// <summary>
    /// Response structure from Ollama streaming API.
    /// Received multiple times per request when streaming is enabled.
    /// </summary>
    [Serializable]
    public class OllamaStreamResponse
    {
        /// <summary>The model name that generated this response.</summary>
        public string model;

        /// <summary>ISO timestamp of when this response chunk was created.</summary>
        public string created_at;

        /// <summary>The response text (partial chunk when streaming, complete when done=true).</summary>
        public string response;

        /// <summary>Whether this is the final chunk of the response.</summary>
        public bool done;

        /// <summary>Total generation time in nanoseconds (only set when done=true).</summary>
        public long total_duration;

        /// <summary>Number of tokens in the prompt (only set when done=true).</summary>
        public int prompt_eval_count;

        /// <summary>Number of tokens generated in the response (only set when done=true).</summary>
        public int eval_count;
    }

    /// <summary>
    /// Response structure from Ollama's /api/tags endpoint.
    /// Contains list of available models on the server.
    /// </summary>
    [Serializable]
    public class OllamaTagsResponse
    {
        /// <summary>Array of available models on the Ollama server.</summary>
        public OllamaModelInfo[] models;
    }

    /// <summary>
    /// Information about a single Ollama model.
    /// </summary>
    [Serializable]
    public class OllamaModelInfo
    {
        /// <summary>The model name (e.g., "llama2:latest", "mistral:7b").</summary>
        public string name;

        /// <summary>Size of the model in bytes.</summary>
        public long size;

        /// <summary>SHA256 digest/hash of the model.</summary>
        public string digest;
    }

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

    /// <summary>
    /// Serializable UnityEvent for NPC action execution.
    /// Signature: void(NPCBehaviourController npc, Transform primaryTarget, Transform secondaryTarget)
    /// Used as the VALUE in NPCActionDispatcher's actionHandlers dictionary.
    /// Wired in the Inspector to connect ActionDefinition ScriptableObjects to ActionBridge methods.
    /// </summary>
    [System.Serializable]
    public class NPCActionCallback : UnityEngine.Events.UnityEvent<NPCBehaviourController, UnityEngine.Transform, UnityEngine.Transform> { }
}

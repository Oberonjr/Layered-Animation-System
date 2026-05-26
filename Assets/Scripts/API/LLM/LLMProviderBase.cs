using System;
using System.Collections;
using UnityEngine;

namespace LAS
{
    /// <summary>
    /// Abstract ScriptableObject base for all LLM providers.
    /// Create provider assets via Assets → Create → LAS → LLM Providers, then drag the asset
    /// into the LLM Provider slot on NPCManager — no scene component swapping needed.
    ///
    /// Subclasses handle API-specific formatting, authentication, streaming, and response parsing.
    /// Coroutines are started by NPCManager (a MonoBehaviour); the provider just returns IEnumerators.
    /// NPCManager calls only the three abstract members: Connect, SendRequest, Interrupt.
    /// </summary>
    public abstract class LLMProviderBase : ScriptableObject
    {
        [Header("Model")]
        [Tooltip("The model identifier to use for generation. Check your provider's model list (e.g. 'llama3.2', 'gpt-4o', 'deepseek-chat').")]
        [SerializeField] protected string modelName;

        /// <summary>The currently configured model name.</summary>
        public string ModelName => modelName;

        /// <summary>True after a successful Connect() call.</summary>
        public bool IsConnected { get; protected set; }

        /// <summary>Human-readable provider name for UI display and debug logging.</summary>
        public abstract string ProviderDisplayName { get; }

        /// <summary>
        /// Sends a structured prompt to the LLM and returns the complete accumulated response via callback.
        /// Calls onComplete(responseText) on success, or skips the call on failure or interrupt.
        /// This is a coroutine — yield return it from NPCManager.
        /// </summary>
        /// <param name="request">Structured prompt: static system context, conversation history, and current user turn.</param>
        /// <param name="options">Generation parameters (temperature, tokens, etc.).</param>
        /// <param name="onComplete">Callback receiving the full response text. Not called on failure.</param>
        public abstract IEnumerator SendRequest(LLMRequest request, LLMGenerationOptions options, Action<string> onComplete);

        /// <summary>
        /// Tests connectivity and discovers available models.
        /// Calls onResult(success, statusMessage) when done.
        /// NPCManager calls this during initialization and displays the status message to the player.
        /// </summary>
        public abstract IEnumerator Connect(Action<bool, string> onResult);

        /// <summary>
        /// Fetches the list of models available on this provider.
        /// Default implementation returns the currently configured model name.
        /// Override to populate from an API endpoint (e.g. Ollama /api/tags, OpenAI /v1/models).
        /// </summary>
        public virtual IEnumerator FetchModels(Action<string[]> onResult)
        {
            onResult?.Invoke(string.IsNullOrEmpty(modelName) ? new string[0] : new[] { modelName });
            yield break;
        }

        /// <summary>
        /// Immediately aborts any in-progress web request.
        /// Called by NPCManager.InterruptCurrentGeneration() when the player sends a new message.
        /// </summary>
        public abstract void Interrupt();
    }
}

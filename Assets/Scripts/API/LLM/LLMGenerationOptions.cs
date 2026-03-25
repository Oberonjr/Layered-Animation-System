using System;
using UnityEngine;

namespace LAS
{
    /// <summary>
    /// Provider-agnostic LLM generation parameters.
    /// Each LLMProviderBase implementation maps these to its own API format.
    /// Used by NPCManager to configure dialogue (high temperature) and action classification (low temperature).
    /// </summary>
    [Serializable]
    public class LLMGenerationOptions
    {
        [Tooltip("Controls randomness. 0 = deterministic, 2 = very random. Keep low (~0.1) for action classification.")]
        [Range(0.0f, 2.0f)] public float temperature = 0.7f;

        [Tooltip("Nucleus sampling threshold (0–1). Lower = more focused, higher = more diverse.")]
        [Range(0.1f, 1.0f)] public float topP = 0.9f;

        [Tooltip("Top-K sampling limit. Restricts output to the K most likely tokens. Provider-specific support varies.")]
        [Range(1f, 100f)] public float topK = 40f;

        [Tooltip("Maximum number of tokens to generate. Use low values (e.g. 80) for classification steps.")]
        public int maxTokens = 150;

        [Tooltip("Penalty for repeating tokens (1.0 = no penalty). Higher values discourage loops.")]
        [Range(1.0f, 2.0f)] public float repeatPenalty = 1.1f;
    }
}

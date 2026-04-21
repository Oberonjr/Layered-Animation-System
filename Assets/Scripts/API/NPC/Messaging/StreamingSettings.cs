using System;
using UnityEngine;

namespace LAS
{
    /// <summary>
    /// Base streaming output settings: whether to stream at all, base speed, and adaptive toggle.
    /// Nested inside <see cref="StreamingSettings"/>.
    /// </summary>
    [Serializable]
    public class StreamingOutputSettings
    {
        [Tooltip("Reveal NPC dialogue word-by-word in the UI rather than all at once.")]
        public bool enableSimulatedStreaming = true;

        [Tooltip("Base character reveal speed in characters per second. " +
                 "Scaled up or down at runtime when Adaptive Streaming is enabled.")]
        [Range(10f, 200f)]
        public float charactersPerSecond = 50f;

        [Tooltip("Automatically adjust streaming speed based on how long the LLM took to respond, " +
                 "so that slow generations don't feel like long silences.")]
        public bool adaptiveStreaming = true;
    }

    /// <summary>
    /// Parameters that control how the streaming speed adapts to LLM response time.
    /// Only active when <see cref="StreamingOutputSettings.adaptiveStreaming"/> is enabled.
    /// Nested inside <see cref="StreamingSettings"/>.
    /// </summary>
    [Serializable]
    public class StreamingAdaptiveSettings
    {
        [Tooltip("If the LLM took longer than this many seconds, streaming speed is increased to compensate.")]
        public float slowThreshold = 2f;

        [Tooltip("If the LLM responded faster than this many seconds, streaming speed is slightly decreased for readability.")]
        public float fastThreshold = 0.5f;

        [Tooltip("Speed multiplier applied when the LLM responded faster than Fast Threshold. " +
                 "Values below 1 slow the reveal so very fast responses don't feel jarring.")]
        [Range(0.1f, 1f)]
        public float fastSpeedMultiplier = 0.7f;

        [Tooltip("Maximum speed multiplier applied when the LLM responded slowly. " +
                 "Caps the speed-up so text never appears unrealistically fast.")]
        [Range(1f, 5f)]
        public float maxMultiplier = 2f;
    }

    /// <summary>
    /// Per-character delay multipliers that give streamed dialogue a natural reading pace.
    /// Applied on top of the base delay derived from Characters Per Second.
    /// Nested inside <see cref="StreamingSettings"/>.
    /// </summary>
    [Serializable]
    public class StreamingPacingSettings
    {
        [Tooltip("Delay multiplier after sentence-ending punctuation (. ! ?). " +
                 "Higher values add a dramatic pause before the next sentence begins.")]
        [Range(1f, 10f)]
        public float pauseAfterSentenceEnd = 3f;

        [Tooltip("Delay multiplier after mid-sentence punctuation (, ; :). Creates a natural breath or beat.")]
        [Range(1f, 5f)]
        public float pauseAfterComma = 2f;

        [Tooltip("Delay multiplier after spaces (between words). " +
                 "Lower values let words flow quickly; higher values add a word-by-word rhythm.")]
        [Range(0.05f, 1f)]
        public float pauseAfterSpace = 0.5f;
    }

    /// <summary>
    /// Top-level data holder for all streaming-related settings on NPCManager.
    /// Groups output, adaptive-speed, and pacing concerns into collapsible sub-sections
    /// so the inspector stays organised.
    ///
    /// Exposed on NPCManager under the "Streaming Settings" header.
    /// </summary>
    [Serializable]
    public class StreamingSettings
    {
        [Tooltip("Core streaming output settings: enable/disable, base speed, adaptive toggle.")]
        public StreamingOutputSettings output = new StreamingOutputSettings();

        [Tooltip("Parameters controlling how streaming speed adapts to LLM response time.")]
        public StreamingAdaptiveSettings adaptiveSpeed = new StreamingAdaptiveSettings();

        [Tooltip("Per-character delay multipliers for natural reading pacing.")]
        public StreamingPacingSettings pacing = new StreamingPacingSettings();
    }
}

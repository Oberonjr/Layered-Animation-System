using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;
using LAS;

namespace LAS
{
    public enum InputMode
    {
        Typing,
        Voice
    }

    /// <summary>
    /// Manages natural conversation flow between NPCs and player with interruption support
    /// </summary>
    public class ConversationFlowController : MonoBehaviour
    {
        [Header("Flow Settings")]
        [SerializeField] private bool enableAutoConversation = true;
        [SerializeField][Range(0f, 1f)] private float npcFollowUpProbability = 0.6f;
        [SerializeField] private int maxConsecutiveNPCMessages = 3;
        [SerializeField] private float minTimeBetweenNPCMessages = 1.5f;
        [SerializeField] private float maxTimeBetweenNPCMessages = 4f;

        [Header("Player Interaction")]
        [SerializeField] private InputMode inputMode = InputMode.Typing;
        [SerializeField] private float playerIdleTimeBeforePrompt = 15f;
        [SerializeField] private float typingDetectionDelay = 0.3f; // Reduced for faster response

        [Header("Context Awareness")]
        [SerializeField] private bool useContextualDecisions = true;
        [SerializeField] private float questionResponseProbability = 0.9f; // High chance to respond to questions
        [SerializeField] private float directAddressProbability = 0.95f; // Very high chance if addressed directly

        [Header("Debug")]
        [SerializeField] private bool showDebugLogs = true;

        private NPCManager npcManager;
        private bool isWaitingForNPCResponse = false;
        private bool playerIsInterrupting = false; // Player wants to speak
        private float lastPlayerInputTime;
        private float lastNPCMessageTime;
        private int consecutiveNPCMessages = 0;
        private string lastSpeakerName = "";
        private Coroutine autoConversationCoroutine;
        private Coroutine idleCheckCoroutine;

        public bool IsProcessing => isWaitingForNPCResponse;
        public bool PlayerWantsToSpeak => playerIsInterrupting;

        void Start()
        {
            npcManager = GetComponent<NPCManager>();

            if (npcManager == null)
            {
                Debug.LogError("ConversationFlowController requires NPCManager on same GameObject!");
                enabled = false;
                return;
            }

            // Subscribe to events
            NPCEventBus.OnNPCStartedSpeaking += HandleNPCStartedSpeaking;
            NPCEventBus.OnNPCFinishedSpeaking += HandleNPCFinishedSpeaking;
            NPCEventBus.OnPlayerMessage += HandlePlayerMessage;

            lastPlayerInputTime = Time.time;

            // Start idle check
            if (enableAutoConversation)
            {
                idleCheckCoroutine = StartCoroutine(CheckPlayerIdleRoutine());
            }
        }

        void OnDestroy()
        {
            NPCEventBus.OnNPCStartedSpeaking -= HandleNPCStartedSpeaking;
            NPCEventBus.OnNPCFinishedSpeaking -= HandleNPCFinishedSpeaking;
            NPCEventBus.OnPlayerMessage -= HandlePlayerMessage;
        }

        void Update()
        {
            // Detect player wanting to speak based on input mode
            if (inputMode == InputMode.Typing)
            {
                // Use New Input System - compatible with both keyboard and VR
                if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame && !playerIsInterrupting)
                {
                    // Count how many non-modifier keys were pressed this frame
                    // Modifier-only presses (lone Shift, Ctrl, Alt) don't count as intent to type
                    bool shiftOnly = Keyboard.current.shiftKey.wasPressedThisFrame
                                      && !Keyboard.current.ctrlKey.wasPressedThisFrame
                                      && !Keyboard.current.altKey.wasPressedThisFrame;
                    bool ctrlOnly = Keyboard.current.ctrlKey.wasPressedThisFrame
                                      && !Keyboard.current.shiftKey.wasPressedThisFrame
                                      && !Keyboard.current.altKey.wasPressedThisFrame;
                    bool altOnly = Keyboard.current.altKey.wasPressedThisFrame
                                      && !Keyboard.current.shiftKey.wasPressedThisFrame
                                      && !Keyboard.current.ctrlKey.wasPressedThisFrame;

                    bool isLoneModifier = shiftOnly || ctrlOnly || altOnly;

                    if (!isLoneModifier)
                    {
                        StartCoroutine(DetectPlayerInterruptIntent());
                    }
                }

                // Clear interruption flag if input field is empty and not focused
                var inputField = npcManager?.GetPlayerInputField();
                if (inputField != null && !inputField.isFocused && string.IsNullOrEmpty(inputField.text))
                {
                    playerIsInterrupting = false;
                }
            }
            else if (inputMode == InputMode.Voice)
            {
                // Voice mode: Will integrate VAD (Voice Activity Detection) later
                // TODO: Replace with voice activity detection
            }
        }

        private IEnumerator DetectPlayerInterruptIntent()
        {
            yield return new WaitForSeconds(typingDetectionDelay);

            // After delay, check if player is actually typing a message
            var inputField = npcManager?.GetPlayerInputField();
            if (inputField != null && (inputField.isFocused || !string.IsNullOrEmpty(inputField.text)))
            {
                playerIsInterrupting = true;
                lastPlayerInputTime = Time.time; // Reset idle timer immediately when typing starts

                if (showDebugLogs)
                {
                    Debug.Log("[Flow] Player is typing - signaling intent to speak");
                }

                // Cancel any pending NPC responses
                if (autoConversationCoroutine != null)
                {
                    StopCoroutine(autoConversationCoroutine);
                    autoConversationCoroutine = null;

                    if (showDebugLogs)
                    {
                        Debug.Log("[Flow] Cancelled pending NPC response - player wants to speak");
                    }
                }
            }
        }

        private void HandleNPCStartedSpeaking(int npcIndex, string dialogue)
        {
            isWaitingForNPCResponse = true;
            lastNPCMessageTime = Time.time;

            // Stop auto-conversation while NPC is speaking
            if (autoConversationCoroutine != null)
            {
                StopCoroutine(autoConversationCoroutine);
                autoConversationCoroutine = null;
            }
        }

        private void HandleNPCFinishedSpeaking(int npcIndex)
        {
            isWaitingForNPCResponse = false;

            if (npcIndex >= 0 && npcIndex < npcManager.GetRegisteredNPCCount())
            {
                var npc = npcManager.GetNPCByIndex(npcIndex);
                if (npc != null)
                {
                    lastSpeakerName = npc.npcName;
                }
            }

            // Update consecutive message counter
            consecutiveNPCMessages++;

            if (showDebugLogs)
            {
                Debug.Log($"[Flow] NPC finished speaking. Consecutive: {consecutiveNPCMessages}/{maxConsecutiveNPCMessages}");
            }

            // Decide if another NPC should respond
            if (enableAutoConversation && ShouldNPCRespond())
            {
                float delay = Random.Range(minTimeBetweenNPCMessages, maxTimeBetweenNPCMessages);
                autoConversationCoroutine = StartCoroutine(TriggerNPCResponseAfterDelay(delay));
            }
        }

        private void HandlePlayerMessage(string message)
        {
            lastPlayerInputTime = Time.time;
            consecutiveNPCMessages = 0; // Reset NPC message counter
            playerIsInterrupting = false; // Clear interruption flag

            // Cancel any pending auto-conversation
            if (autoConversationCoroutine != null)
            {
                StopCoroutine(autoConversationCoroutine);
                autoConversationCoroutine = null;
            }

            if (showDebugLogs)
            {
                Debug.Log($"[Flow] Player spoke - reset consecutive NPC counter");
            }
        }

        private bool ShouldNPCRespond()
        {
            // Don't respond if player is trying to speak
            if (playerIsInterrupting)
            {
                if (showDebugLogs)
                {
                    Debug.Log($"[Flow] Player wants to speak - holding back NPC response");
                }
                return false;
            }

            // Don't respond if we've hit consecutive message limit
            if (consecutiveNPCMessages >= maxConsecutiveNPCMessages)
            {
                if (showDebugLogs)
                {
                    Debug.Log($"[Flow] Max consecutive NPC messages reached - waiting for player");
                }
                return false;
            }

            // Check time since last message
            float timeSinceLastMessage = Time.time - lastNPCMessageTime;
            if (timeSinceLastMessage < minTimeBetweenNPCMessages)
            {
                return false;
            }

            // Use contextual decision making
            if (useContextualDecisions)
            {
                float probability = CalculateResponseProbability();
                bool shouldRespond = Random.value < probability;

                if (showDebugLogs)
                {
                    Debug.Log($"[Flow] Response probability: {probability:F2}, Rolling: {shouldRespond}");
                }

                return shouldRespond;
            }

            // Simple probability check
            return Random.value < npcFollowUpProbability;
        }

        private float CalculateResponseProbability()
        {
            // Start with base probability
            float probability = npcFollowUpProbability;

            // Reduce probability with each consecutive message
            float consecutivePenalty = consecutiveNPCMessages * 0.15f;
            probability -= consecutivePenalty;

            // Get recent conversation context
            var recentMessages = npcManager.GetRecentMessages(3);

            if (recentMessages.Count > 0)
            {
                var lastMessage = recentMessages[recentMessages.Count - 1];

                // Increase probability if last message was a question
                if (lastMessage.message.Contains("?"))
                {
                    probability = Mathf.Max(probability, questionResponseProbability);

                    if (showDebugLogs)
                    {
                        Debug.Log($"[Flow] Question detected - boosting probability to {probability:F2}");
                    }
                }

                // Check if someone was directly addressed
                foreach (int i in System.Linq.Enumerable.Range(0, npcManager.GetRegisteredNPCCount()))
                {
                    var npc = npcManager.GetNPCByIndex(i);
                    if (npc != null && lastMessage.message.Contains(npc.npcName))
                    {
                        probability = Mathf.Max(probability, directAddressProbability);

                        if (showDebugLogs)
                        {
                            Debug.Log($"[Flow] Direct address detected - boosting probability to {probability:F2}");
                        }
                        break;
                    }
                }
            }

            return Mathf.Clamp01(probability);
        }

        private IEnumerator TriggerNPCResponseAfterDelay(float delay)
        {
            if (showDebugLogs)
            {
                Debug.Log($"[Flow] Scheduling NPC response in {delay:F1} seconds");
            }

            yield return new WaitForSeconds(delay);

            // Double-check we should still respond
            if (!isWaitingForNPCResponse && npcManager != null && !npcManager.IsProcessing)
            {
                if (showDebugLogs)
                {
                    Debug.Log($"[Flow] Triggering NPC-to-NPC conversation");
                }

                npcManager.TriggerNPCToNPCConversation();
            }

            autoConversationCoroutine = null;
        }

        private IEnumerator CheckPlayerIdleRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(5f); // Check every 5 seconds

                float idleTime = Time.time - lastPlayerInputTime;

                if (idleTime > playerIdleTimeBeforePrompt && !isWaitingForNPCResponse && !npcManager.IsProcessing)
                {
                    if (showDebugLogs)
                    {
                        Debug.Log($"[Flow] Player idle for {idleTime:F1}s - prompting NPCs");
                    }

                    // Reset timer
                    lastPlayerInputTime = Time.time;
                    consecutiveNPCMessages = 0;

                    // Trigger NPC to address the player
                    npcManager.TriggerNPCPromptForPlayer();
                }
            }
        }

        public void ResetConversationFlow()
        {
            consecutiveNPCMessages = 0;
            lastPlayerInputTime = Time.time;
            playerIsInterrupting = false;

            if (autoConversationCoroutine != null)
            {
                StopCoroutine(autoConversationCoroutine);
                autoConversationCoroutine = null;
            }

            if (showDebugLogs)
            {
                Debug.Log("[Flow] Conversation flow reset");
            }
        }

        // Public methods for external control
        public void PauseAutoConversation()
        {
            enableAutoConversation = false;

            if (autoConversationCoroutine != null)
            {
                StopCoroutine(autoConversationCoroutine);
                autoConversationCoroutine = null;
            }
        }

        public void ResumeAutoConversation()
        {
            enableAutoConversation = true;
        }

        public void ForceNPCResponse()
        {
            if (!isWaitingForNPCResponse && npcManager != null && !npcManager.IsProcessing)
            {
                consecutiveNPCMessages = 0; // Reset to allow response
                npcManager.TriggerNPCToNPCConversation();
            }
        }
    }
}

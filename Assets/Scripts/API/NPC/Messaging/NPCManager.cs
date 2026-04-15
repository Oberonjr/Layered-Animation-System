using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using AYellowpaper.SerializedCollections;
using LAS;

namespace LAS {

    public class NPCManager : MonoBehaviour
    {
        [Header("UI Elements")]
        [Tooltip("Input field where the player types messages.")]
        [SerializeField] private TMP_InputField playerInputField;
        [Tooltip("Button that submits the player's typed message.")]
        [SerializeField] private Button sendButton;
        [Tooltip("Scroll view content transform where chat message prefabs are instantiated.")]
        [SerializeField] private Transform chatContentParent;
        [Tooltip("Prefab used to display each line of chat in the UI.")]
        [SerializeField] private GameObject chatMessagePrefab;

        [Header("Scenario Configuration")]
        [Tooltip("JSON file describing the scenario: setting, characters, rules, and progression steps.")]
        [SerializeField] private TextAsset scenarioConfigFile;

        [Header("LLM Provider")]
        [Tooltip("Assign an OllamaProvider, OpenAIChatProvider, or other LLMProviderBase component here.")]
        [SerializeField] private LLMProviderBase llmProvider;

        [Header("Dialogue Generation Options")]
        [Tooltip("Parameters for Step 1 (dialogue). Higher temperature = more natural / creative.")]
        [SerializeField] private LLMGenerationOptions dialogueOptions = new LLMGenerationOptions
        {
            temperature   = 0.7f,
            topP          = 0.9f,
            topK          = 40f,
            maxTokens     = 150,
            repeatPenalty = 1.1f
        };

        [Header("Action Classification Options")]
        [Tooltip("Parameters for Step 2 (action selection). Low temperature = more deterministic output.")]
        [SerializeField] private LLMGenerationOptions actionOptions = new LLMGenerationOptions
        {
            temperature   = 0.1f,
            topP          = 0.9f,
            topK          = 10f,
            maxTokens     = 80,
            repeatPenalty = 1.0f
        };

        [Header("Streaming Settings")]
        [Tooltip("Reveal NPC dialogue word-by-word in the UI rather than all at once.")]
        [SerializeField] private bool enableSimulatedStreaming = true;
        [Tooltip("How many characters per second appear when streaming dialogue. Higher = faster reveal.")]
        [SerializeField] [Range(10f, 200f)] private float charactersPerSecond = 50f;
        [Tooltip("Speed up or slow down the streaming reveal based on how long the LLM took to respond.")]
        [SerializeField] private bool adaptiveStreaming = true;

        [Header("Context Management")]
        [Tooltip("How many past messages are included in each LLM request. Higher = more context, higher cost.")]
        [SerializeField] private int contextHistoryLimit = 15;
        [Tooltip("Include the current scenario progression step in the prompt sent to the LLM.")]
        [SerializeField] private bool includeProgressionContext = true;

        [Header("Runtime Info")]
        [Tooltip("Read-only. Tracks the current scenario progression step at runtime.")]
        [SerializeField] private int currentProgressionStep = 0;
        [Tooltip("Read-only. All NPCControllers currently registered with this manager.")]
        [SerializeField] private List<NPCController> registeredNPCs = new List<NPCController>();

        [Header("NPC Character Mapping")]
        [Tooltip("Explicit character-to-NPC assignment. Click 'Extract Characters from JSON' in the Inspector " +
                 "to populate the keys from your scenario file, then drag the correct NPC Controller into each slot. " +
                 "When all slots are filled, this overrides the default index-based assignment. " +
                 "Leave empty to fall back to registration order (first NPC registered = character[0]).")]
        [SerializedDictionary("Character Name", "NPC Controller")]
        public SerializedDictionary<string, NPCController> npcCharacterMap
            = new SerializedDictionary<string, NPCController>();

        [Header("Alias Generation")]
        [Tooltip("Automatically generate LLM-based aliases for InteractableItem and LocationTarget objects on startup. " +
                 "Aliases allow players to refer to targets by informal names or misspellings.")]
        [SerializeField] private bool generateAliasesOnStartup = true;

        [Header("Debug")]
        [Tooltip("Log verbose internal state (NPC assignments, initialization steps) to the console.")]
        [SerializeField] public bool showDetailedDebug = false;
        [Tooltip("Print the full prompt sent to the LLM before each request.")]
        [SerializeField] public bool logPrompts = false;
        [Tooltip("Print the raw LLM response text after each request.")]
        [SerializeField] public bool logLLMResponses = false;
    
        private ConversationFlowController flowController;
        private NPCActionDispatcher actionDispatcher;
        private ScenarioConfig scenarioConfig;
        private List<ChatMessage> conversationHistory = new List<ChatMessage>();
        private List<string> recentActionLog = new List<string>(); // rolling log of dispatched action sequences
        private const int ActionLogLimit = 6;
        private bool isProcessing = false;

        private bool _interruptGenerationFlag = false;
        private Coroutine _streamingTextCoroutine;
        private NPCController _activeSpeaker;
        private int _lastAutoSpeakerIndex = 0; // Tracks last NPC that spoke in auto-conversation to ensure alternation

        public bool IsProcessing => isProcessing;

        // Public accessors for ConversationFlowController
        public TMP_InputField GetPlayerInputField() => playerInputField;
        public int GetRegisteredNPCCount() => registeredNPCs.Count;
        public NPCController GetNPCByIndex(int index) => index >= 0 && index < registeredNPCs.Count ? registeredNPCs[index] : null;
        public List<ChatMessage> GetRecentMessages(int count) 
        {
            int startIndex = Mathf.Max(0, conversationHistory.Count - count);
            return conversationHistory.GetRange(startIndex, conversationHistory.Count - startIndex);
        }

        void OnEnable()
        {
            NPCEventBus.OnNPCRegistered   += HandleNPCRegistration;
            NPCEventBus.OnNPCUnregistered += HandleNPCUnregistration;
            NPCEventBus.OnPlayerMessage   += HandlePlayerMessage;
            NPCEventBus.OnActionImpossible += HandleActionImpossible;
        }

        void OnDisable()
        {
            NPCEventBus.OnNPCRegistered   -= HandleNPCRegistration;
            NPCEventBus.OnNPCUnregistered -= HandleNPCUnregistration;
            NPCEventBus.OnPlayerMessage   -= HandlePlayerMessage;
            NPCEventBus.OnActionImpossible -= HandleActionImpossible;
        }

        void Start()
        {
            Initialize();
        }

        public void Initialize()
        {
            flowController = GetComponent<ConversationFlowController>();
            actionDispatcher = NPCActionDispatcher.Instance;
        
            if (sendButton != null)
            {
                sendButton.onClick.AddListener(OnPlayerSendMessage);
            }
        
            if (playerInputField != null)
            {
                playerInputField.onSubmit.AddListener(delegate { OnPlayerSendMessage(); });
            }
        
            StartCoroutine(InitializeSystem());
        }

        private void HandleNPCRegistration(NPCController npc)
        {
            if (!registeredNPCs.Contains(npc))
            {
                int index = registeredNPCs.Count;
                registeredNPCs.Add(npc);
                npc.AssignIndex(index);
            
                Debug.Log($"[NPCManager] Registered NPC {index}: {npc.npcName}");
            }
        }

        private void HandleNPCUnregistration(NPCController npc)
        {
            if (registeredNPCs.Contains(npc))
            {
                registeredNPCs.Remove(npc);
                Debug.Log($"[NPCManager] Unregistered NPC: {npc.npcName}");
            }
        }

        private void HandlePlayerMessage(string message)
        {
            // This can be used for additional player message processing
        }

        private IEnumerator InitializeSystem()
        {
            // Wait a frame for all NPCs to register
            yield return null;
        
            // Load scenario configuration
            if (scenarioConfigFile != null)
            {
                try
                {
                    scenarioConfig = JsonUtility.FromJson<ScenarioConfig>(scenarioConfigFile.text);
                    Debug.Log($"Loaded scenario: {scenarioConfig.scenario.title}");
                
                    // Validate we have enough NPCs
                    if (scenarioConfig.characters.Length > registeredNPCs.Count)
                    {
                        Debug.LogError($"Scenario requires {scenarioConfig.characters.Length} NPCs but only {registeredNPCs.Count} are registered in the scene!");
                        AddSystemMessage($"ERROR: Not enough NPCs! Need {scenarioConfig.characters.Length}, have {registeredNPCs.Count}");
                        yield break;
                    }
                
                    // ── NPC Character Assignment ───────────────────────────────
                    // If npcCharacterMap has at least one assigned entry, use it to
                    // reorder registeredNPCs so that characters[i] → registeredNPCs[i].
                    // Otherwise fall back to registration-order (index-based).
                    bool useCharacterMap = npcCharacterMap != null &&
                                          npcCharacterMap.Count > 0 &&
                                          npcCharacterMap.Values.Any(v => v != null);

                    if (useCharacterMap)
                    {
                        var orderedNPCs = new List<NPCController>();
                        foreach (var character in scenarioConfig.characters)
                        {
                            if (npcCharacterMap.TryGetValue(character.name, out NPCController mapped) &&
                                mapped != null)
                            {
                                if (!orderedNPCs.Contains(mapped))
                                    orderedNPCs.Add(mapped);
                            }
                            else
                            {
                                Debug.LogWarning($"[NPCManager] Character '{character.name}' has no NPC assigned " +
                                                 "in npcCharacterMap and will be skipped.");
                            }
                        }
                        // Append any registered NPCs not covered by the map
                        foreach (var npc in registeredNPCs)
                            if (!orderedNPCs.Contains(npc))
                                orderedNPCs.Add(npc);

                        registeredNPCs = orderedNPCs;

                        // Re-assign indices to match new order
                        for (int i = 0; i < registeredNPCs.Count; i++)
                            registeredNPCs[i].AssignIndex(i);
                    }

                    // Assign character data — works for both map-based and index-based ordering
                    for (int i = 0; i < scenarioConfig.characters.Length && i < registeredNPCs.Count; i++)
                    {
                        CharacterInfo character = scenarioConfig.characters[i];
                        NPCController npc = registeredNPCs[i];
                        npc.AssignCharacterData(
                            character.name,
                            character.role,
                            GetFullCharacterDescription(character));
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to load scenario config: {e.Message}");
                    AddSystemMessage("ERROR: Failed to load scenario configuration!");
                    yield break;
                }
            }
        
            // Connect to LLM provider
            if (llmProvider == null)
            {
                Debug.LogError("[NPCManager] No LLM provider assigned! Assign one in the Inspector.");
                AddSystemMessage("ERROR: No LLM provider assigned!");
                if (sendButton != null) sendButton.interactable = false;
                yield break;
            }

            bool connectionSuccess = false;
            string connectionMessage = "";
            yield return llmProvider.Connect((success, message) =>
            {
                connectionSuccess = success;
                connectionMessage = message;
            });

            if (!connectionSuccess)
            {
                AddSystemMessage($"ERROR: {connectionMessage}");
                if (sendButton != null) sendButton.interactable = false;
                yield break;
            }

            AddSystemMessage(connectionMessage);

            // ── Alias generation (background — non-blocking) ──────────────────────
            if (generateAliasesOnStartup)
            {
                string title = scenarioConfig?.scenario?.title ?? "";
                StartCoroutine(AliasGenerator.GenerateAll(
                    NPCActionTargetRegistry.Instance, llmProvider, title, this));
            }

            // Add initial scenario messages
            if (scenarioConfig != null)
            {
                AddSystemMessage($"=== {scenarioConfig.scenario.title} ===");
                AddSystemMessage(scenarioConfig.scenario.description);

                if (scenarioConfig.player_character != null)
                {
                    AddSystemMessage($"Your Role: {scenarioConfig.player_character.role}");
                    AddSystemMessage(scenarioConfig.player_character.description);
                }

                // Initialize conversation with first NPC
                if (scenarioConfig.conversation_initialization != null)
                {
                    yield return new WaitForSeconds(1f);
                    StartCoroutine(InitiateConversation());
                }
            }
        }

        private string GetFullCharacterDescription(CharacterInfo character)
        {
            StringBuilder desc = new StringBuilder();
            desc.AppendLine($"Role: {character.role}");
            desc.AppendLine($"Personality: {character.personality}");
            desc.AppendLine($"Background: {character.background}");
            desc.AppendLine($"Communication: {character.communication_style}");
            if (!string.IsNullOrEmpty(character.teaching_approach))
            {
                desc.AppendLine($"Teaching Approach: {character.teaching_approach}");
            }
            return desc.ToString();
        }


        private IEnumerator InitiateConversation()
        {
            if (scenarioConfig.conversation_initialization == null)
                yield break;
            
            yield return GetNPCResponse(
                scenarioConfig.conversation_initialization.opening_prompt,
                scenarioConfig.conversation_initialization.first_speaker_index
            );
        }

        /// <summary>
        /// Displays an in-character refusal from the NPC when a requested action is impossible.
        /// Called via NPCEventBus.OnActionImpossible — no LLM call; instant feedback.
        /// </summary>
        private void HandleActionImpossible(int npcIndex, string reason)
        {
            if (npcIndex < 0 || npcIndex >= registeredNPCs.Count) return;
            var npc = registeredNPCs[npcIndex];
            AddChatMessage(npc.npcName, reason, MessageType.NPC, npc);
        }

        public void OnPlayerSendMessage()
        {
            if (string.IsNullOrEmpty(playerInputField?.text))
                return;

            string message = playerInputField.text.Trim();
            playerInputField.text = "";

            // ── Command intercept ──────────────────────────────────────────────────
            if (message.StartsWith("/"))
            {
                ProcessCommand(message);
                return;
            }

            if (isProcessing)
            {
                // Interrupt any in-progress generation and queue the player message.
                InterruptCurrentGeneration();
                StartCoroutine(SendPlayerMessageAfterInterrupt(message));
            }
            else
            {
                AddChatMessage("Player", message, MessageType.Player);
                NPCEventBus.BroadcastPlayerMessage(message);
                StartCoroutine(GetNPCResponse(message, -1));
            }
        }

        public void TriggerNPCToNPCConversation()
        {
            if (isProcessing)
                return;
            
            StartCoroutine(NPCConversationRoutine());
        }
    
        public void TriggerNPCPromptForPlayer()
        {
            if (isProcessing)
                return;
            
            StartCoroutine(NPCPromptPlayerRoutine());
        }

        private IEnumerator NPCConversationRoutine()
        {
            // Alternate between NPCs so auto-conversation doesn't get dominated by NPC 0.
            int nextSpeaker = registeredNPCs.Count > 1
                ? (_lastAutoSpeakerIndex + 1) % registeredNPCs.Count
                : -1;
            if (nextSpeaker >= 0) _lastAutoSpeakerIndex = nextSpeaker;

            string prompt = "Continue the teaching session naturally. Address the next aspect of preparation, ask a follow-up question, or respond to what was just said.";
            yield return GetNPCResponse(prompt, nextSpeaker);
        }

        private IEnumerator NPCPromptPlayerRoutine()
        {
            // Alternate between NPCs for idle prompts as well.
            int nextSpeaker = registeredNPCs.Count > 1
                ? (_lastAutoSpeakerIndex + 1) % registeredNPCs.Count
                : -1;
            if (nextSpeaker >= 0) _lastAutoSpeakerIndex = nextSpeaker;

            string prompt = "The student has been quiet for a while. One of you should check in with them, ask if they have questions, or prompt them to participate.";
            yield return GetNPCResponse(prompt, nextSpeaker);
        }

        private IEnumerator GetNPCResponse(string userInput, int specificNPCIndex)
        {
            if (llmProvider == null || !llmProvider.IsConnected)
            {
                Debug.LogError("[NPCManager] LLM provider not available.");
                yield break;
            }

            isProcessing = true;
            _interruptGenerationFlag = false;
            float generationStartTime = Time.time;

            // Auto-detect which NPC the player is directly addressing (e.g. "Dr. Chen, can you...")
            // This must happen before BuildDialoguePrompt so the prompt locks in the correct speaker.
            if (specificNPCIndex < 0)
                specificNPCIndex = DetectAddressedNPC(userInput);

            // ── STEP 1: Dialogue Generation (full context, temperature 0.7) ──────────

            string step1Prompt = BuildDialoguePrompt(userInput, specificNPCIndex);

            if (logPrompts)
                Debug.Log($"[NPCManager] === LLM REQUEST (Dialogue) ===\n{step1Prompt}");

            string step1Raw = null;
            yield return StartCoroutine(llmProvider.SendRequest(step1Prompt, dialogueOptions, r => step1Raw = r));

            if (_interruptGenerationFlag || step1Raw == null)
            {
                isProcessing = false;
                yield break;
            }

            if (logLLMResponses)
                Debug.Log($"[NPCManager] === LLM RESPONSE (Dialogue) ===\n{step1Raw}");

            NPCResponse step1Response = null;
            if (!TryParseDialogueResponse(step1Raw, out step1Response))
            {
                AddSystemMessage("[Error: Could not parse NPC dialogue response]");
                isProcessing = false;
                yield break;
            }

            // Resolve which NPC spoke (respect specificNPCIndex if supplied).
            int npcIndex = (specificNPCIndex >= 0 && specificNPCIndex < registeredNPCs.Count)
                ? specificNPCIndex
                : step1Response.npc_index;

            // ── STEP 2: Action Classification (short prompt, temperature 0.1) ────────

            string step2Prompt = BuildActionClassificationPrompt(npcIndex, userInput, step1Response.dialogue, step1Response.internal_thought);

            if (logPrompts)
                Debug.Log($"[NPCManager] === LLM REQUEST (Action) ===\n{step2Prompt}");

            string step2Raw = null;
            yield return StartCoroutine(llmProvider.SendRequest(step2Prompt, actionOptions, r => step2Raw = r));

            // Step 2 failure is non-fatal — NPC simply performs no physical action.
            NPCActionSequence actionSequence = null;
            if (!_interruptGenerationFlag && step2Raw != null)
            {
                // Always log the raw Step 2 output — it's short (one JSON line) and is the
                // primary diagnostic for whether the problem is LLM-side or parse/dispatch-side.
                Debug.Log($"[Action raw] {step2Raw.Trim()}");

                actionSequence = ParseActionSequence(step2Raw);

                if (actionSequence == null)
                    Debug.LogWarning($"[NPCManager] Step 2 parse failed — no action dispatched. Raw: {step2Raw.Trim()}");
            }
            else if (!_interruptGenerationFlag)
            {
                Debug.LogWarning("[NPCManager] Step 2 returned null — LLM may have timed out or returned empty.");
            }

            if (_interruptGenerationFlag)
            {
                isProcessing = false;
                yield break;
            }

            // ── COMBINE: merge dialogue + action into a single NPCResponse ───────────

            string actionSummary = actionSequence?.actions?.Length > 0
                ? string.Join(" → ", actionSequence.actions
                    .Where(a => a.action_key != "NONE")
                    .Select(a => string.IsNullOrEmpty(a.action_target)
                        ? a.action_key : $"{a.action_key}({a.action_target})"))
                : "NONE";

            NPCResponse finalResponse = new NPCResponse
            {
                npc_index             = npcIndex,
                dialogue              = step1Response.dialogue,
                internal_thought      = step1Response.internal_thought,
                action_key            = actionSummary,
                action_target         = actionSequence?.actions?.Length > 0
                    ? actionSequence.actions[0].action_target ?? "" : "",
                action_secondary_target = ""
            };

            // Always-on compact summary — one line per NPC turn.
            {
                string speakerName = (npcIndex >= 0 && npcIndex < registeredNPCs.Count)
                    ? registeredNPCs[npcIndex].npcName : $"NPC {npcIndex}";
                string actionPart = string.IsNullOrEmpty(actionSummary) || actionSummary == "NONE"
                    ? "NONE"
                    : actionSummary;
                Debug.Log($"[NPC] {speakerName} → {actionPart} | \"{finalResponse.dialogue}\"");
            }

            if (showDetailedDebug || logLLMResponses)
            {
                string speakerName = (npcIndex >= 0 && npcIndex < registeredNPCs.Count)
                    ? registeredNPCs[npcIndex].npcName : $"NPC {npcIndex}";
                Debug.Log($"[NPCManager] Full response — Speaker: {speakerName} (NPC {npcIndex})\n" +
                          $"Action Sequence: {actionSummary}\n" +
                          $"Dialogue: \"{finalResponse.dialogue}\"\n" +
                          $"Internal Thought: \"{finalResponse.internal_thought}\"");
            }

            // ── RESPONSE HANDLING (identical to original) ─────────────────────────────

            float generationTime = Time.time - generationStartTime;
            ChatMessage tempMessage = null;
            NPCController currentSpeaker = null;

            if (finalResponse.npc_index >= 0 && finalResponse.npc_index < registeredNPCs.Count)
            {
                currentSpeaker = registeredNPCs[finalResponse.npc_index];
                _activeSpeaker = currentSpeaker;
                currentSpeaker.SetSpeaking(true);

                tempMessage = AddChatMessage(
                    currentSpeaker.npcName,
                    "",
                    MessageType.NPC,
                    currentSpeaker
                );

                NPCEventBus.BroadcastNPCStartedSpeaking(finalResponse.npc_index, "");

                // Dispatch action sequence (single-step and multi-step both handled uniformly)
                if (actionSequence?.actions?.Length > 0)
                {
                    if (actionDispatcher == null)
                        actionDispatcher = NPCActionDispatcher.Instance;

                    if (actionDispatcher == null)
                        Debug.LogWarning("[NPCManager] No NPCActionDispatcher found — action will not execute.");
                    else
                        actionDispatcher.DispatchSequence(npcIndex, actionSequence, registeredNPCs);

                    // Record for action classification context in future turns
                    string npcLabel = currentSpeaker?.npcName ?? $"NPC {npcIndex}";
                    string steps = string.Join(" → ", actionSequence.actions
                        .Where(a => !string.IsNullOrEmpty(a.action_key) && a.action_key != "NONE")
                        .Select(a => string.IsNullOrEmpty(a.action_target) ? a.action_key : $"{a.action_key}({a.action_target})"));
                    if (!string.IsNullOrEmpty(steps))
                    {
                        recentActionLog.Add($"{npcLabel}: {steps}");
                        if (recentActionLog.Count > ActionLogLimit)
                            recentActionLog.RemoveAt(0);
                    }
                }
            }

            // Stream dialogue text to the chat message.
            if (tempMessage != null && !string.IsNullOrEmpty(finalResponse.dialogue))
            {
                if (enableSimulatedStreaming)
                {
                    float streamSpeed = charactersPerSecond;
                    if (adaptiveStreaming)
                    {
                        if (generationTime > 2f)
                            streamSpeed *= Mathf.Min(2f, generationTime / 2f);
                        else if (generationTime < 0.5f)
                            streamSpeed *= 0.7f;
                    }
                    _streamingTextCoroutine = StartCoroutine(StreamTextToMessage(tempMessage, finalResponse.dialogue, streamSpeed));
                    yield return _streamingTextCoroutine;
                    _streamingTextCoroutine = null;
                }
                else
                {
                    tempMessage.UpdateText(finalResponse.dialogue);
                }
            }

            if (currentSpeaker != null)
            {
                currentSpeaker.SetSpeaking(false);
                NPCEventBus.BroadcastNPCFinishedSpeaking(currentSpeaker.AssignedIndex);
            }

            _activeSpeaker = null;
            isProcessing = false;
        }

            private IEnumerator StreamTextToMessage(ChatMessage message, string fullText, float charsPerSecond)
        {
            if (message == null || string.IsNullOrEmpty(fullText))
                yield break;
        
            float charDelay = 1f / charsPerSecond;
            StringBuilder currentText = new StringBuilder();
        
            // Stream character by character
            for (int i = 0; i < fullText.Length; i++)
            {
                currentText.Append(fullText[i]);
                message.UpdateText(currentText.ToString());
            
                // Variable delay based on punctuation for more natural feel
                char currentChar = fullText[i];
                float delay = charDelay;
            
                if (currentChar == '.' || currentChar == '!' || currentChar == '?')
                {
                    delay *= 3f; // Longer pause after sentence endings
                }
                else if (currentChar == ',' || currentChar == ';' || currentChar == ':')
                {
                    delay *= 2f; // Medium pause after commas
                }
                else if (currentChar == ' ')
                {
                    delay *= 0.5f; // Shorter pause for spaces
                }
            
                yield return new WaitForSeconds(delay);
            }
        }

        // ─── Two-step LLM helpers ─────────────────────────────────────────────────

        /// <summary>
        /// Interrupts any in-progress LLM generation and dialogue streaming.
        /// Safe to call from outside the coroutine (e.g. when player sends a message mid-stream).
        /// </summary>
        public void InterruptCurrentGeneration()
        {
            _interruptGenerationFlag = true;
            llmProvider?.Interrupt();

            if (_streamingTextCoroutine != null)
            {
                StopCoroutine(_streamingTextCoroutine);
                _streamingTextCoroutine = null;
            }

            if (_activeSpeaker != null)
            {
                _activeSpeaker.SetSpeaking(false);
                NPCEventBus.BroadcastNPCFinishedSpeaking(_activeSpeaker.AssignedIndex);
                _activeSpeaker = null;
            }

            isProcessing = false;
        }

        /// <summary>
        /// Waits for an interrupted generation to fully stop, then sends the player's message.
        /// </summary>
        private IEnumerator SendPlayerMessageAfterInterrupt(string message)
        {
            float elapsed = 0f;
            while (isProcessing && elapsed < 2f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            AddChatMessage("Player", message, MessageType.Player);
            NPCEventBus.BroadcastPlayerMessage(message);
            StartCoroutine(GetNPCResponse(message, -1));
        }

        /// <summary>
        /// Detects which NPC the player is directly addressing by checking for name/role at the
        /// start of the message or before a comma (e.g. "Dr. Chen, can you..." / "Nurse, please...").
        /// Returns the NPC index, or -1 if no direct address is detected.
        /// </summary>
        private int DetectAddressedNPC(string userInput)
        {
            if (registeredNPCs == null || string.IsNullOrEmpty(userInput)) return -1;

            string lower = userInput.ToLower().TrimStart();

            // Pre-compute the text before the first comma (the "address zone").
            int commaPos = lower.IndexOf(',');
            string addressZone = commaPos > 0 ? lower.Substring(0, commaPos) : lower;

            for (int i = 0; i < registeredNPCs.Count; i++)
            {
                var npc = registeredNPCs[i];

                string name = npc.npcName?.ToLower() ?? "";
                string role = npc.characterRole?.ToLower() ?? "";

                // Direct address: message starts with the name/role.
                if (!string.IsNullOrEmpty(name) && lower.StartsWith(name)) return i;
                if (!string.IsNullOrEmpty(role) && lower.StartsWith(role)) return i;

                // Classic address pattern: "Dr. Chen, please..." — name/role before the comma.
                if (!string.IsNullOrEmpty(name) && addressZone.Contains(name)) return i;
                if (!string.IsNullOrEmpty(role) && addressZone.Contains(role)) return i;
            }

            return -1;
        }

        /// <summary>
        /// Tries to parse a Step 1 dialogue response from a potentially malformed LLM output.
        /// Strategy 1: Standard JSON parse via JsonUtility.
        /// Strategy 2: Regex extraction of npc_index + longest non-field quoted string as dialogue.
        /// Returns true if a usable NPCResponse was extracted.
        /// </summary>
        private bool TryParseDialogueResponse(string raw, out NPCResponse result)
        {
            result = null;
            if (string.IsNullOrEmpty(raw)) return false;

            // Strategy 1: Standard JSON parse (preferred path).
            try
            {
                int si = raw.IndexOf('{');
                int ei = raw.LastIndexOf('}') + 1;
                if (si >= 0 && ei > si)
                {
                    var parsed = JsonUtility.FromJson<NPCResponse>(raw.Substring(si, ei - si));
                    if (parsed != null && !string.IsNullOrEmpty(parsed.dialogue))
                    {
                        result = parsed;
                        return true;
                    }
                }
            }
            catch { }

            // Strategy 2: Regex fallback — handles cases where the LLM forgets a field name key.
            try
            {
                result = new NPCResponse();

                // Extract npc_index.
                var indexMatch = Regex.Match(raw, "\"npc_index\"\\s*:\\s*(\\d+)");
                if (indexMatch.Success)
                    result.npc_index = int.Parse(indexMatch.Groups[1].Value);

                // Extract internal_thought if present.
                var thoughtMatch = Regex.Match(raw, "\"internal_thought\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                string extractedThought = thoughtMatch.Success ? thoughtMatch.Groups[1].Value : null;
                if (extractedThought != null)
                    result.internal_thought = extractedThought;

                // Collect all quoted strings, skip known JSON field names and the already-extracted thought.
                var fieldNames = new HashSet<string>
                    { "npc_index", "dialogue", "internal_thought", "action_key", "action_target", "action_secondary_target" };

                var candidates = Regex
                    .Matches(raw, "\"((?:[^\"\\\\]|\\\\.){5,})\"")
                    .Cast<Match>()
                    .Select(m => m.Groups[1].Value)
                    .Where(v => !fieldNames.Contains(v) &&
                                (extractedThought == null || v != extractedThought))
                    .OrderBy(v => raw.IndexOf(v, StringComparison.Ordinal))
                    .ToList();

                if (candidates.Count == 0) return false;

                // Strip meta-description prefixes that the LLM wraps around dialogue.
                // e.g. "description of Dr. Chen's dialogue: I'll grab that now" → "I'll grab that now"
                string[] metaPrefixes = { "description of ", "as she responds", "as he responds",
                                           "npc response:", "character says:", "response:", "reply:",
                                           "as she says", "as he says", "dr.", "nurse", "michael" };

                var cleaned = candidates.Select(c =>
                {
                    string s = c.TrimStart();
                    foreach (var prefix in metaPrefixes)
                    {
                        if (s.ToLower().StartsWith(prefix))
                        {
                            int colonPos = s.IndexOf(':');
                            // Only strip if there's real content after the colon
                            if (colonPos > 0 && colonPos < s.Length - 5)
                                return s.Substring(colonPos + 1).Trim();
                        }
                    }
                    return s;
                }).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

                // Pick the longest candidate as the most likely full dialogue sentence —
                // do NOT join all candidates as that causes internal_thought and other strings to bleed in.
                if (cleaned.Count > 0)
                {
                    result.dialogue = cleaned.OrderByDescending(s => s.Length).First();
                    Debug.LogWarning($"[NPCManager] Fallback dialogue extraction used. Recovered: \"{result.dialogue}\"");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NPCManager] Fallback parse also failed: {ex.Message}\nRaw: {raw}");
            }

            return false;
        }

        /// <summary>
        /// Builds the Step 1 dialogue prompt — full context with conversation history.
        /// Response format asks only for npc_index, dialogue, and internal_thought (no action fields).
        /// </summary>
        private string BuildDialoguePrompt(string currentInput, int specificNPCIndex)
        {
            StringBuilder prompt = new StringBuilder();

            if (scenarioConfig != null)
            {
                prompt.AppendLine("=== SCENARIO CONTEXT ===");
                prompt.AppendLine($"Setting: {scenarioConfig.scenario.setting}");
                prompt.AppendLine($"Timeframe: {scenarioConfig.scenario.timeframe}");
                if (scenarioConfig.conversation_initialization != null)
                    prompt.AppendLine($"Context: {scenarioConfig.conversation_initialization.context}");
                prompt.AppendLine();

                if (scenarioConfig.player_character != null)
                {
                    prompt.AppendLine("=== PLAYER CHARACTER ===");
                    prompt.AppendLine($"Role: {scenarioConfig.player_character.role}");
                    prompt.AppendLine($"Description: {scenarioConfig.player_character.description}");
                    prompt.AppendLine();
                }

                prompt.AppendLine("=== CORE RULES (FOLLOW STRICTLY) ===");
                foreach (var rule in scenarioConfig.system_instructions.core_rules)
                    prompt.AppendLine($"• {rule}");
                prompt.AppendLine();

                prompt.AppendLine("=== CHARACTERS (BY INDEX) ===");
                for (int i = 0; i < scenarioConfig.characters.Length && i < registeredNPCs.Count; i++)
                {
                    var ch = scenarioConfig.characters[i];
                    prompt.AppendLine($"NPC {i}: {ch.name} ({ch.role})");
                    prompt.AppendLine($"  Personality: {ch.personality}");
                    prompt.AppendLine($"  Background: {ch.background}");
                    prompt.AppendLine($"  Communication: {ch.communication_style}");
                    prompt.AppendLine($"  Teaching: {ch.teaching_approach}");
                    prompt.AppendLine($"  Current: {ch.current_state}");
                }
                prompt.AppendLine();

                if (includeProgressionContext && scenarioConfig.required_progression_steps != null)
                {
                    prompt.AppendLine("=== REQUIRED PROGRESSION STEPS ===");
                    prompt.AppendLine("Guide the student through these steps in order:");
                    for (int i = 0; i < scenarioConfig.required_progression_steps.Length; i++)
                    {
                        var step = scenarioConfig.required_progression_steps[i];
                        string status = i < currentProgressionStep ? "[COMPLETED]" :
                                       i == currentProgressionStep ? "[CURRENT]" : "[UPCOMING]";
                        prompt.AppendLine($"{status} Step {step.step_id}: {step.title}");
                        if (i == currentProgressionStep)
                        {
                            prompt.AppendLine($"  Description: {step.description}");
                            prompt.AppendLine($"  Teaching moments: {string.Join(", ", step.teaching_moments)}");
                        }
                    }
                    prompt.AppendLine();
                }

                prompt.AppendLine("=== CRITICAL RULES ===");
                prompt.AppendLine("• If YOU just asked a question, WAIT for someone else to answer");
                prompt.AppendLine("• NEVER answer your own questions");
                prompt.AppendLine("• NEVER have multiple exchanges by yourself");
                prompt.AppendLine("• If the player uses a vague term that could refer to a known item (e.g. 'that thing', 'the sharp one'), ask for clarification using the actual item name.");
                prompt.AppendLine("• If the player asks about something completely unrecognizable or unrelated to this scenario, clearly state that you don't know what they mean and redirect to the current training task.");
                prompt.AppendLine();

                prompt.AppendLine("=== INTERACTION GUIDELINES ===");
                foreach (var g in scenarioConfig.system_instructions.interaction_guidelines)
                    prompt.AppendLine($"• {g}");
                prompt.AppendLine();

                if (scenarioConfig.system_instructions.teaching_behavior != null)
                {
                    prompt.AppendLine("=== TEACHING BEHAVIOR ===");
                    foreach (var b in scenarioConfig.system_instructions.teaching_behavior)
                        prompt.AppendLine($"• {b}");
                    prompt.AppendLine();
                }
            }

            if (conversationHistory.Count > 0)
            {
                prompt.AppendLine("=== RECENT CONVERSATION ===");
                int startIndex = Mathf.Max(0, conversationHistory.Count - contextHistoryLimit);
                for (int i = startIndex; i < conversationHistory.Count; i++)
                    prompt.AppendLine($"{conversationHistory[i].speaker}: {conversationHistory[i].message}");
                prompt.AppendLine();
            }

            prompt.AppendLine("=== CURRENT INPUT ===");
            prompt.AppendLine(currentInput);
            prompt.AppendLine();

            if (specificNPCIndex >= 0 && specificNPCIndex < registeredNPCs.Count)
            {
                prompt.AppendLine($"=== YOU ARE NPC {specificNPCIndex} ===");
                prompt.AppendLine($"Respond as {registeredNPCs[specificNPCIndex].npcName}");
            }
            else
            {
                prompt.AppendLine("=== DETERMINE WHO SHOULD RESPOND ===");
                prompt.AppendLine("Based on context and who was addressed, decide which NPC responds.");
            }

            prompt.AppendLine();
            prompt.AppendLine("=== RESPONSE FORMAT ===");
            prompt.AppendLine("Output ONLY a JSON object — no descriptions, no prose, no text before or after the braces.");
            prompt.AppendLine("Do NOT write 'description of', 'as she says', or any wrapper text. Just the JSON.");
            prompt.AppendLine("You MUST use EXACTLY these three field names:");
            prompt.AppendLine("  \"npc_index\"       : integer — who speaks (0 or 1)");
            prompt.AppendLine("  \"dialogue\"        : string  — the EXACT words spoken aloud");
            prompt.AppendLine("  \"internal_thought\": string  — the physical action you intend to take, if any (e.g. \"picking up the scalpel\", \"walking to the supply table\", \"handing the item to the player\"). Write \"none\" if no physical action is needed.");
            prompt.AppendLine("Do NOT add action_key or action_target — those are handled separately.");
            prompt.AppendLine("Correct output:");
            prompt.AppendLine("{\"npc_index\": 1, \"dialogue\": \"Let me grab that for you.\", \"internal_thought\": \"picking up the scalpel and handing it to the player\"}");
            prompt.AppendLine("{\"npc_index\": 0, \"dialogue\": \"Good question — the key thing to remember is...\", \"internal_thought\": \"none\"}");
            prompt.AppendLine("WRONG (never do this):");
            prompt.AppendLine("description of NPC response: {\"npc_index\": 1 ...");
            prompt.AppendLine();
            prompt.AppendLine("Output ONLY the JSON object starting with { and ending with }:");

            return prompt.ToString();
        }

        /// <summary>
        /// Builds a compact scene-state snapshot for the Step 2 prompt.
        /// Lists every interactable item's status (free / held by X / placed at Y)
        /// and every NPC's held-object status so the LLM can pick the right action
        /// (e.g. PICK_UP from a surface vs GRAB_FROM an NPC who is already holding it).
        /// </summary>
        private string BuildSceneStateForActionPrompt()
        {
            var sb = new StringBuilder();
            var registry = NPCActionTargetRegistry.Instance;
            if (registry == null) return "";

            // Interactable items
            var items = registry.GetTargetsByType(TargetType.InteractableObject)
                .OfType<InteractableItem>()
                .ToList();
            if (items.Count > 0)
            {
                sb.Append("  Items: ");
                sb.AppendLine(string.Join(", ",
                    items.Select(i => $"{i.TargetName}({i.GetStateDescription()})")));
            }

            // NPC hold states
            var npcLines = new List<string>();
            foreach (var npc in registeredNPCs)
            {
                var b = npc.GetComponent<NPCBehaviourController>();
                if (b == null) continue;
                string held = b.IsHoldingObject
                    ? $"holding {b.HeldObject?.name ?? "object"}"
                    : "empty-handed";
                npcLines.Add($"{npc.npcName}({held})");
            }
            if (npcLines.Count > 0)
            {
                sb.Append("  NPCs:  ");
                sb.AppendLine(string.Join(", ", npcLines));
            }

            // Location slot states (only locations with something notable)
            var locLines = new List<string>();
            foreach (var target in registry.GetTargetsByType(TargetType.Location).OfType<LocationTarget>())
            {
                string desc = target.GetStateDescription();
                if (desc != "empty") locLines.Add($"{target.TargetName}: {desc}");
            }
            if (locLines.Count > 0)
            {
                sb.AppendLine("  Locations:");
                foreach (var l in locLines) sb.AppendLine($"    {l}");
            }

            return sb.Length > 0 ? "SCENE STATE:\n" + sb : "";
        }

        /// <summary>
        /// Builds the Step 2 action classification prompt — short, focused, low-temperature.
        /// Only includes the triggering exchange and available actions (no full conversation history).
        /// </summary>
        private string BuildActionClassificationPrompt(int npcIndex, string playerInput, string npcDialogue, string npcActionIntent = null)
        {
            StringBuilder prompt = new StringBuilder();

            string npcName = (npcIndex >= 0 && npcIndex < registeredNPCs.Count)
                ? registeredNPCs[npcIndex].npcName : $"NPC {npcIndex}";

            // ── Context (kept minimal — only what's needed for action selection) ─────
            prompt.AppendLine($"PLAYER: \"{playerInput}\"");
            prompt.AppendLine($"{npcName}: \"{npcDialogue}\"");

            // Action intent from Step 1 — the primary signal for action classification.
            if (!string.IsNullOrEmpty(npcActionIntent) &&
                !npcActionIntent.Equals("none", StringComparison.OrdinalIgnoreCase))
                prompt.AppendLine($"{npcName} intends to: {npcActionIntent}");

            // Scene state — items, NPC hold status, location slots
            string sceneState = BuildSceneStateForActionPrompt();
            if (!string.IsNullOrEmpty(sceneState))
                prompt.AppendLine(sceneState);

            if (recentActionLog.Count > 0)
                prompt.AppendLine($"Recent: {string.Join(" | ", recentActionLog)}");
            prompt.AppendLine();

            // ── Compact vocabulary ────────────────────────────────────────────────────
            if (actionDispatcher == null) actionDispatcher = NPCActionDispatcher.Instance;
            if (actionDispatcher != null)
                prompt.Append(actionDispatcher.BuildCompactActionVocabulary());

            // ── Task ──────────────────────────────────────────────────────────────────
            prompt.AppendLine("RULES:");
            prompt.AppendLine("• Physical task (move/pick up/hand/place)? → use those keys. Do NOT add LOOK_AT_PLAYER.");
            prompt.AppendLine("• No physical task? → always use LOOK_AT_PLAYER as default.");
            prompt.AppendLine("• Chain steps in order. Use exact primary names from VALID TARGET NAMES.");
            prompt.AppendLine();
            prompt.AppendLine("Output ONLY JSON:");
            prompt.AppendLine("{\"actions\":[{\"action_key\":\"KEY\",\"action_target\":\"name_or_empty\",\"action_secondary_target\":\"\"}]}");
            prompt.AppendLine();
            prompt.AppendLine("Examples:");
            prompt.AppendLine("talking/no action → {\"actions\":[{\"action_key\":\"LOOK_AT_PLAYER\",\"action_target\":\"\",\"action_secondary_target\":\"\"}]}");
            prompt.AppendLine("go to table       → {\"actions\":[{\"action_key\":\"GO_TO\",\"action_target\":\"Supply Table\",\"action_secondary_target\":\"\"}]}");
            prompt.AppendLine("give scalpel      → {\"actions\":[{\"action_key\":\"PICK_UP\",\"action_target\":\"Scalpel\",\"action_secondary_target\":\"\"},{\"action_key\":\"HAND_TO_PLAYER\",\"action_target\":\"\",\"action_secondary_target\":\"\"}]}");

            return prompt.ToString();
        }


        /// <summary>
        /// Tries to parse an NPCActionSequence from a Step 2 LLM response.
        /// Accepts the new sequence format {"actions":[...]} as well as the old single-action
        /// format {"action_key":...} for backward compatibility.
        /// Returns null if parsing fails or yields no usable steps.
        /// </summary>
        private NPCActionSequence ParseActionSequence(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;

            int si = raw.IndexOf('{');
            int ei = raw.LastIndexOf('}') + 1;
            if (si < 0 || ei <= si) return null;
            string json = raw.Substring(si, ei - si);

            // Primary: sequence format {"actions":[...]}
            try
            {
                var seq = JsonUtility.FromJson<NPCActionSequence>(json);
                if (seq?.actions != null && seq.actions.Length > 0 &&
                    !string.IsNullOrEmpty(seq.actions[0].action_key))
                    return seq;
            }
            catch { }

            // Fallback: single-action format {"action_key":"...","action_target":"..."}
            try
            {
                var single = JsonUtility.FromJson<NPCActionOnly>(json);
                if (single != null && !string.IsNullOrEmpty(single.action_key))
                {
                    return new NPCActionSequence
                    {
                        actions = new[]
                        {
                            new NPCActionStep
                            {
                                action_key              = single.action_key,
                                action_target           = single.action_target           ?? "",
                                action_secondary_target = single.action_secondary_target ?? ""
                            }
                        }
                    };
                }
            }
            catch { }

            Debug.LogWarning($"[NPCManager] Could not parse action sequence (non-fatal). Raw: {raw}");
            return null;
        }

        private ChatMessage AddChatMessage(string speaker, string message, MessageType type, NPCController npc = null)
        {
            if (chatMessagePrefab == null || chatContentParent == null)
            {
                Debug.LogWarning("Chat UI not configured!");
                return null;
            }

            GameObject messageObj = Instantiate(chatMessagePrefab, chatContentParent, false);
        
            ChatMessage chatMessage = messageObj.GetComponent<ChatMessage>();
        
            if (chatMessage != null)
            {
                // Use color from NPC if provided
                if (type == MessageType.NPC && npc != null)
                {
                    chatMessage.InitializeWithColor(speaker, message, type, npc.HighlightColor);
                }
                else
                {
                    chatMessage.Initialize(speaker, message, type);
                }
            
                conversationHistory.Add(chatMessage);
            }
        
            Canvas.ForceUpdateCanvases();
            StartCoroutine(ScrollToBottom());
        
            return chatMessage;
        }

        private void AddSystemMessage(string message)
        {
            AddChatMessage("System", message, MessageType.System);
        }

        // ── Command system ────────────────────────────────────────────────────────

        private void ProcessCommand(string input)
        {
            string cmd = input.Substring(1).Trim().ToLower();
            switch (cmd)
            {
                case "help": ShowHelpMessage(); break;
                default:
                    AddSystemMessage($"Unknown command '{input}'. Type /help for available commands.");
                    break;
            }
        }

        private void ShowHelpMessage()
        {
            if (actionDispatcher == null) actionDispatcher = NPCActionDispatcher.Instance;

            var sb = new StringBuilder();
            sb.AppendLine("=== NPC ACTIONS ===");

            if (actionDispatcher != null)
            {
                foreach (var def in actionDispatcher.GetActiveActions())
                {
                    sb.Append($"  {def.actionKey}");
                    if (def.requiresTarget)         sb.Append($"  →  target: {def.targetDescription}");
                    if (def.requiresSecondaryTarget) sb.Append($"  /  secondary: {def.secondaryTargetDescription}");
                    sb.AppendLine();
                    sb.AppendLine($"    {def.llmDescription}");
                }
            }
            else
            {
                sb.AppendLine("  (No dispatcher found — NPCActionDispatcher not present in scene.)");
            }

            sb.AppendLine();
            sb.AppendLine("=== REGISTERED TARGETS ===");

            var registry = NPCActionTargetRegistry.Instance;
            if (registry != null)
            {
                foreach (TargetType type in Enum.GetValues(typeof(TargetType)))
                {
                    var targets = new System.Collections.Generic.List<IActionTarget>(registry.GetTargetsByType(type));
                    if (targets.Count == 0) continue;
                    sb.AppendLine($"  [{type}]");
                    foreach (var t in targets)
                        sb.AppendLine($"    • {t.TargetName}");
                }
            }
            else
            {
                sb.AppendLine("  (No registry found.)");
            }

            AddSystemMessage(sb.ToString());
        }

        private IEnumerator ScrollToBottom()
        {
            yield return new WaitForEndOfFrame();
        
            ScrollRect scrollRect = chatContentParent.GetComponentInParent<ScrollRect>();
            if (scrollRect != null)
            {
                scrollRect.verticalNormalizedPosition = 0f;
            }
        }

        public void AdvanceProgressionStep()
        {
            if (scenarioConfig?.required_progression_steps != null)
            {
                if (currentProgressionStep < scenarioConfig.required_progression_steps.Length - 1)
                {
                    currentProgressionStep++;
                    var step = scenarioConfig.required_progression_steps[currentProgressionStep];
                    AddSystemMessage($"=== Step {step.step_id}: {step.title} ===");
                    Debug.Log($"Advanced to step {step.step_id}: {step.title}");
                }
            }
        }
    }
}

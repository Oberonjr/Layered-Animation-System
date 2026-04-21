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
        [Tooltip("All streaming settings: output (enable/speed), adaptive speed, and per-character pacing. " +
                 "Each sub-section is collapsible in the inspector.")]
        [SerializeField] private StreamingSettings streaming = new StreamingSettings();

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
        [Tooltip("Automatically generate LLM-based aliases for scene targets on startup. " +
                 "Aliases allow players to refer to targets by informal names or misspellings.")]
        [SerializeField] private bool generateAliasesOnStartup = true;
        [Tooltip("Parameters and prompt template for the alias generator. Right-click this component and choose " +
                 "'Reset Alias Generator to Default' to restore built-in values.")]
        [SerializeField] private AliasGeneratorConfig aliasConfig = new AliasGeneratorConfig();

        [Header("Conversation Prompts")]
        [Tooltip("Fallback prompts for NPC-to-NPC auto-conversation and player idle re-engagement. " +
                 "Each prompt is read from the scenario JSON first; the inspector values act as fallbacks " +
                 "or overrides depending on the toggle inside.")]
        [SerializeField] private ConversationPromptsSettings conversationPrompts = new ConversationPromptsSettings();

        [Header("Rules and Guidelines")]
        [Tooltip("Critical conversation-flow rules and domain-specific behavior guidelines injected into every LLM prompt. " +
                 "Both blocks are read from the scenario JSON first; the inspector values act as fallbacks or overrides.")]
        [SerializeField] private RulesAndGuidelinesSettings rulesAndGuidelines = new RulesAndGuidelinesSettings();

        [Header("Interrupt Settings")]
        [Tooltip("Maximum seconds to wait for an in-progress generation to stop before sending the player's queued message. " +
                 "If this elapses before the generation stops, the queued message is sent anyway.")]
        [SerializeField] private float interruptWaitTimeout = 2f;

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

                // Ensure the text caret is visible and blinking when the field is focused.
                // TMP_InputField defaults can vary by prefab; set explicit values here as a guarantee.
                playerInputField.caretWidth     = 2;
                playerInputField.caretBlinkRate = 0.85f;
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
                string contextHint = scenarioConfig?.scenario?.context_hint
                    ?? scenarioConfig?.scenario?.title
                    ?? "";
                StartCoroutine(AliasGenerator.GenerateAll(
                    NPCActionTargetRegistry.Instance, llmProvider, aliasConfig, contextHint, this));
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

            yield return GetNPCResponse(ConversationTurn.FromSystem(
                scenarioConfig.conversation_initialization.opening_prompt,
                scenarioConfig.conversation_initialization.first_speaker_index));
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

            // Keep the input field focused so the player can immediately type their next message.
            playerInputField.ActivateInputField();

            // ── Command intercept ──────────────────────────────────────────────────
            if (message.StartsWith("/"))
            {
                ProcessCommand(message);
                return;
            }

            // Address detection happens here so the turn carries the result.
            int addressedNPC = DetectAddressedNPC(message);
            var turn = ConversationTurn.FromPlayer(message, addressedNPC);

            if (isProcessing)
            {
                // Interrupt any in-progress generation and queue the player message.
                InterruptCurrentGeneration();
                StartCoroutine(SendPlayerMessageAfterInterrupt(turn));
            }
            else
            {
                AddChatMessage("Player", message, MessageType.Player);
                NPCEventBus.BroadcastPlayerMessage(message);
                StartCoroutine(GetNPCResponse(turn));
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

            // Find the last NPC who spoke and their dialogue so we can build a true
            // NPC-to-NPC turn rather than a generic director prompt.
            int    lastNPCSpeakerIndex = -1;
            string lastNPCDialogue     = "";
            for (int i = conversationHistory.Count - 1; i >= 0; i--)
            {
                if (conversationHistory[i].type == MessageType.NPC)
                {
                    string speakerName = conversationHistory[i].speaker;
                    lastNPCSpeakerIndex = registeredNPCs.FindIndex(n => n.npcName == speakerName);
                    lastNPCDialogue     = conversationHistory[i].message;
                    break;
                }
            }

            ConversationTurn turn = (lastNPCSpeakerIndex >= 0 && !string.IsNullOrEmpty(lastNPCDialogue))
                ? ConversationTurn.FromNPC(lastNPCSpeakerIndex, lastNPCDialogue, nextSpeaker)
                : ConversationTurn.FromSystem(GetEffectiveNPCConversationPrompt(), nextSpeaker);

            yield return GetNPCResponse(turn);
        }

        private IEnumerator NPCPromptPlayerRoutine()
        {
            // Alternate between NPCs for idle prompts as well.
            int nextSpeaker = registeredNPCs.Count > 1
                ? (_lastAutoSpeakerIndex + 1) % registeredNPCs.Count
                : -1;
            if (nextSpeaker >= 0) _lastAutoSpeakerIndex = nextSpeaker;

            yield return GetNPCResponse(ConversationTurn.FromRoom(GetEffectiveIdlePrompt(), nextSpeaker));
        }

        private IEnumerator GetNPCResponse(ConversationTurn turn)
        {
            if (llmProvider == null || !llmProvider.IsConnected)
            {
                Debug.LogError("[NPCManager] LLM provider not available.");
                yield break;
            }

            isProcessing = true;
            _interruptGenerationFlag = false;
            float generationStartTime = Time.time;

            // ── STEP 1: Dialogue Generation (full context, temperature 0.7) ──────────

            string step1Prompt = BuildDialoguePrompt(turn);

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

            // Resolve which NPC spoke (respect turn.targetIndex if supplied).
            int npcIndex = (turn.targetIndex >= 0 && turn.targetIndex < registeredNPCs.Count)
                ? turn.targetIndex
                : step1Response.npc_index;

            // ── STEP 2: Action Classification (short prompt, temperature 0.1) ────────

            string step2Prompt = BuildActionClassificationPrompt(npcIndex, turn, step1Response.dialogue, step1Response.internal_thought);

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
                    .Where(a => a.action_key != NPCActionDefinition.NoneKey)
                    .Select(a => string.IsNullOrEmpty(a.action_target)
                        ? a.action_key : $"{a.action_key}({a.action_target})"))
                : NPCActionDefinition.NoneKey;

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
                string actionPart = string.IsNullOrEmpty(actionSummary) || actionSummary == NPCActionDefinition.NoneKey
                    ? NPCActionDefinition.NoneKey
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
                        .Where(a => !string.IsNullOrEmpty(a.action_key) && a.action_key != NPCActionDefinition.NoneKey)
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
                if (streaming.output.enableSimulatedStreaming)
                {
                    float streamSpeed = streaming.output.charactersPerSecond;
                    if (streaming.output.adaptiveStreaming)
                    {
                        if (generationTime > streaming.adaptiveSpeed.slowThreshold)
                            streamSpeed *= Mathf.Min(streaming.adaptiveSpeed.maxMultiplier,
                                                     generationTime / streaming.adaptiveSpeed.slowThreshold);
                        else if (generationTime < streaming.adaptiveSpeed.fastThreshold)
                            streamSpeed *= streaming.adaptiveSpeed.fastSpeedMultiplier;
                    }
                    _streamingTextCoroutine = StartCoroutine(StreamTextToMessage(tempMessage, finalResponse.dialogue, streamSpeed));
                    yield return _streamingTextCoroutine;
                    _streamingTextCoroutine = null;

                    // If InterruptCurrentGeneration() was called during streaming it already
                    // broadcast NPCFinishedSpeaking and reset state — bail out here to avoid
                    // a second broadcast that would leak an orphaned auto-conversation coroutine.
                    if (_interruptGenerationFlag)
                        yield break;
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
                    delay *= streaming.pacing.pauseAfterSentenceEnd;
                else if (currentChar == ',' || currentChar == ';' || currentChar == ':')
                    delay *= streaming.pacing.pauseAfterComma;
                else if (currentChar == ' ')
                    delay *= streaming.pacing.pauseAfterSpace;
            
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
        /// Waits for an interrupted generation to fully stop, then sends the player's queued turn.
        /// Respects <see cref="interruptWaitTimeout"/> — if the generation hasn't stopped within
        /// that window the message is sent anyway.
        /// </summary>
        private IEnumerator SendPlayerMessageAfterInterrupt(ConversationTurn turn)
        {
            float elapsed = 0f;
            while (isProcessing && elapsed < interruptWaitTimeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            AddChatMessage("Player", turn.content, MessageType.Player);
            NPCEventBus.BroadcastPlayerMessage(turn.content);
            StartCoroutine(GetNPCResponse(turn));
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

                // Strip meta-description prefixes that the LLM sometimes wraps around dialogue.
                // e.g. "description of Dr. Chen's dialogue: I'll grab that now" → "I'll grab that now"
                // The prefix list is built dynamically from current NPC names/roles so it stays
                // correct across scenarios without any hardcoded character names.
                var metaPrefixSet = BuildFallbackMetaPrefixes();
                string[] metaPrefixes = new string[metaPrefixSet.Count];
                metaPrefixSet.CopyTo(metaPrefixes, 0);

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
        /// Builds the Step 1 dialogue prompt from a <see cref="ConversationTurn"/>.
        /// Frames the "current input" section differently depending on whether the stimulus
        /// came from the player, another NPC, a system directive, or a scene event — ensuring
        /// the LLM generates dialogue appropriate to the actual conversation mode.
        /// </summary>
        private string BuildDialoguePrompt(ConversationTurn turn)
        {
            var prompt = new StringBuilder();

            if (scenarioConfig != null)
            {
                // ── Scenario context ──────────────────────────────────────────────
                prompt.AppendLine("=== SCENARIO CONTEXT ===");
                prompt.AppendLine($"Setting: {scenarioConfig.scenario.setting}");
                prompt.AppendLine($"Timeframe: {scenarioConfig.scenario.timeframe}");
                if (scenarioConfig.conversation_initialization != null)
                    prompt.AppendLine($"Context: {scenarioConfig.conversation_initialization.context}");
                prompt.AppendLine();

                // ── Player character ──────────────────────────────────────────────
                if (scenarioConfig.player_character != null)
                {
                    prompt.AppendLine("=== PLAYER CHARACTER ===");
                    prompt.AppendLine($"Role: {scenarioConfig.player_character.role}");
                    prompt.AppendLine($"Description: {scenarioConfig.player_character.description}");
                    prompt.AppendLine();
                }

                // ── Core rules (from JSON) ────────────────────────────────────────
                if (scenarioConfig.system_instructions?.core_rules?.Length > 0)
                {
                    prompt.AppendLine("=== CORE RULES (FOLLOW STRICTLY) ===");
                    foreach (var rule in scenarioConfig.system_instructions.core_rules)
                        prompt.AppendLine($"• {rule}");
                    prompt.AppendLine();
                }

                // ── Characters ────────────────────────────────────────────────────
                prompt.AppendLine("=== CHARACTERS (BY INDEX) ===");
                for (int i = 0; i < scenarioConfig.characters.Length && i < registeredNPCs.Count; i++)
                {
                    var ch = scenarioConfig.characters[i];
                    prompt.AppendLine($"NPC {i}: {ch.name} ({ch.role})");
                    prompt.AppendLine($"  Personality: {ch.personality}");
                    prompt.AppendLine($"  Background: {ch.background}");
                    prompt.AppendLine($"  Communication: {ch.communication_style}");
                    if (!string.IsNullOrEmpty(ch.teaching_approach))
                        prompt.AppendLine($"  Approach: {ch.teaching_approach}");
                    prompt.AppendLine($"  Current: {ch.current_state}");
                }
                prompt.AppendLine();

                // ── Progression steps (optional) ──────────────────────────────────
                if (includeProgressionContext && scenarioConfig.required_progression_steps != null)
                {
                    prompt.AppendLine("=== REQUIRED PROGRESSION STEPS ===");
                    for (int i = 0; i < scenarioConfig.required_progression_steps.Length; i++)
                    {
                        var step   = scenarioConfig.required_progression_steps[i];
                        string status = i < currentProgressionStep  ? "[COMPLETED]" :
                                        i == currentProgressionStep ? "[CURRENT]"   : "[UPCOMING]";
                        prompt.AppendLine($"{status} Step {step.step_id}: {step.title}");
                        if (i == currentProgressionStep)
                        {
                            prompt.AppendLine($"  Description: {step.description}");
                            if (step.key_points?.Length > 0)
                                prompt.AppendLine($"  Key points: {string.Join(", ", step.key_points)}");
                            if (step.teaching_moments?.Length > 0)
                                prompt.AppendLine($"  Teaching moments: {string.Join(", ", step.teaching_moments)}");
                        }
                    }
                    prompt.AppendLine();
                }

                // ── Critical rules (JSON → inspector fallback) ────────────────────
                var criticalRules = GetEffectiveCriticalRules();
                if (!string.IsNullOrWhiteSpace(criticalRules))
                {
                    prompt.AppendLine("=== CRITICAL RULES ===");
                    prompt.AppendLine(criticalRules.TrimEnd());
                    prompt.AppendLine();
                }

                // ── Interaction guidelines (from JSON) ────────────────────────────
                if (scenarioConfig.system_instructions?.interaction_guidelines?.Length > 0)
                {
                    prompt.AppendLine("=== INTERACTION GUIDELINES ===");
                    foreach (var g in scenarioConfig.system_instructions.interaction_guidelines)
                        prompt.AppendLine($"• {g}");
                    prompt.AppendLine();
                }

                // ── Behavior guidelines (JSON → inspector fallback, configurable label) ─
                var behaviorGuidelines = GetEffectiveBehaviorGuidelines();
                if (!string.IsNullOrWhiteSpace(behaviorGuidelines))
                {
                    string sectionLabel = GetEffectiveBehaviorSectionLabel();
                    prompt.AppendLine($"=== {sectionLabel} ===");
                    prompt.AppendLine(behaviorGuidelines.TrimEnd());
                    prompt.AppendLine();
                }
            }

            // ── Recent conversation history ───────────────────────────────────────
            if (conversationHistory.Count > 0)
            {
                prompt.AppendLine("=== RECENT CONVERSATION ===");
                int startIndex = Mathf.Max(0, conversationHistory.Count - contextHistoryLimit);
                for (int i = startIndex; i < conversationHistory.Count; i++)
                    prompt.AppendLine($"{conversationHistory[i].speaker}: {conversationHistory[i].message}");
                prompt.AppendLine();
            }

            // ── Current input (framed by turn type) ──────────────────────────────
            prompt.AppendLine("=== CURRENT INPUT ===");
            prompt.AppendLine(BuildCurrentInputSection(turn));

            // ── Speaker assignment ────────────────────────────────────────────────
            AppendSpeakerSection(prompt, turn);

            // ── Response format ───────────────────────────────────────────────────
            prompt.AppendLine();
            prompt.AppendLine("=== RESPONSE FORMAT ===");
            prompt.AppendLine("Output ONLY a JSON object — no descriptions, no prose, no text before or after the braces.");
            prompt.AppendLine("Do NOT write 'description of', 'as she says', or any wrapper text. Just the JSON.");
            prompt.AppendLine("You MUST use EXACTLY these three field names:");
            prompt.AppendLine("  \"npc_index\"       : integer — who speaks");
            prompt.AppendLine("  \"dialogue\"        : string  — the EXACT words spoken aloud");
            prompt.AppendLine("  \"internal_thought\": string  — intended physical action, e.g. \"picking up the scalpel\". Write \"none\" if no physical action.");
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
        /// Builds the text that follows the <c>=== CURRENT INPUT ===</c> marker, framed
        /// correctly for the turn's speaker type. This section becomes the "user" message
        /// in providers that split system/user roles at the marker.
        /// </summary>
        private string BuildCurrentInputSection(ConversationTurn turn)
        {
            var sb = new StringBuilder();
            switch (turn.speakerType)
            {
                case SpeakerType.Player:
                    sb.AppendLine(turn.content);
                    break;

                case SpeakerType.NPC:
                    string speakerName = IsValidNPCIndex(turn.speakerIndex)
                        ? registeredNPCs[turn.speakerIndex].npcName
                        : $"NPC {turn.speakerIndex}";
                    string targetName = IsValidNPCIndex(turn.targetIndex)
                        ? registeredNPCs[turn.targetIndex].npcName
                        : "";
                    sb.AppendLine($"{speakerName} just said: \"{turn.content}\"");
                    if (!string.IsNullOrEmpty(targetName))
                        sb.AppendLine($"You are {targetName}. Respond directly to {speakerName} by name — do not address the player in this turn.");
                    break;

                case SpeakerType.System:
                    sb.AppendLine($"[Stage direction — not spoken aloud]: {turn.content}");
                    break;

                case SpeakerType.Room:
                    sb.AppendLine($"[Scene situation]: {turn.content}");
                    if (IsValidNPCIndex(turn.targetIndex))
                        sb.AppendLine($"You are {registeredNPCs[turn.targetIndex].npcName}. React naturally to this situation.");
                    else
                        sb.AppendLine("One of you should react naturally to this situation.");
                    break;
            }
            return sb.ToString();
        }

        /// <summary>
        /// Appends the speaker-assignment block to the prompt.
        /// Uses the turn's targetIndex to lock a specific NPC, or asks the model to choose.
        /// </summary>
        private void AppendSpeakerSection(StringBuilder prompt, ConversationTurn turn)
        {
            if (IsValidNPCIndex(turn.targetIndex))
            {
                prompt.AppendLine($"=== YOU ARE NPC {turn.targetIndex} ===");
                prompt.AppendLine($"Respond as {registeredNPCs[turn.targetIndex].npcName}");
            }
            else
            {
                prompt.AppendLine("=== DETERMINE WHO SHOULD RESPOND ===");
                prompt.AppendLine("Based on context and who was addressed, decide which NPC responds.");
            }
        }

        /// <summary>Returns true if <paramref name="index"/> is a valid index into registeredNPCs.</summary>
        private bool IsValidNPCIndex(int index) => index >= 0 && index < registeredNPCs.Count;

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
        /// The trigger line is labelled correctly based on the turn's speaker type.
        /// </summary>
        private string BuildActionClassificationPrompt(int npcIndex, ConversationTurn turn, string npcDialogue, string npcActionIntent = null)
        {
            var prompt = new StringBuilder();

            string npcName = IsValidNPCIndex(npcIndex)
                ? registeredNPCs[npcIndex].npcName : $"NPC {npcIndex}";

            // ── Trigger line — labelled by speaker type ───────────────────────────
            string triggerLabel = turn.speakerType switch
            {
                SpeakerType.Player => "PLAYER",
                SpeakerType.NPC when IsValidNPCIndex(turn.speakerIndex)
                    => registeredNPCs[turn.speakerIndex].npcName,
                _ => "CONTEXT"
            };
            prompt.AppendLine($"{triggerLabel}: \"{turn.content}\"");
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

        // ── Effective-value helpers (JSON → inspector fallback → hard default) ───────

        /// <summary>
        /// Returns the NPC-to-NPC auto-conversation prompt.
        /// Priority: JSON field → inspector fallback (or inspector override if the flag is set).
        /// </summary>
        private string GetEffectiveNPCConversationPrompt()
        {
            if (!conversationPrompts.overrideNPCConversationPrompt)
            {
                string fromJson = scenarioConfig?.conversation_initialization?.npc_conversation_prompt;
                if (!string.IsNullOrWhiteSpace(fromJson)) return fromJson;
            }
            return conversationPrompts.npcConversationPrompt;
        }

        /// <summary>
        /// Returns the idle-player re-engagement prompt.
        /// Priority: JSON field → inspector fallback (or inspector override if the flag is set).
        /// </summary>
        private string GetEffectiveIdlePrompt()
        {
            if (!conversationPrompts.overrideIdlePrompt)
            {
                string fromJson = scenarioConfig?.conversation_initialization?.idle_player_prompt;
                if (!string.IsNullOrWhiteSpace(fromJson)) return fromJson;
            }
            return conversationPrompts.idlePrompt;
        }

        /// <summary>
        /// Returns the critical rules block as a ready-to-append string (each rule prefixed with "• ").
        /// Priority: JSON array → inspector fallback (or inspector override if the flag is set).
        /// </summary>
        private string GetEffectiveCriticalRules()
        {
            if (!rulesAndGuidelines.overrideCriticalRules)
            {
                var fromJson = scenarioConfig?.system_instructions?.critical_rules;
                if (fromJson != null && fromJson.Length > 0)
                    return string.Join("\n", System.Array.ConvertAll(fromJson, r => $"• {r}"));
            }
            return rulesAndGuidelines.criticalRules;
        }

        /// <summary>
        /// Returns the behavior guidelines block as a ready-to-append string (each item prefixed with "• ").
        /// Checks "behavior_guidelines" first, then "teaching_behavior" (legacy JSON alias).
        /// Priority: JSON array → inspector fallback (or inspector override if the flag is set).
        /// </summary>
        private string GetEffectiveBehaviorGuidelines()
        {
            if (!rulesAndGuidelines.overrideBehaviorGuidelines)
            {
                var fromJson = scenarioConfig?.system_instructions?.behavior_guidelines;
                if (fromJson == null || fromJson.Length == 0)
                    fromJson = scenarioConfig?.system_instructions?.teaching_behavior; // legacy alias
                if (fromJson != null && fromJson.Length > 0)
                    return string.Join("\n", System.Array.ConvertAll(fromJson, b => $"• {b}"));
            }
            return rulesAndGuidelines.behaviorGuidelines;
        }

        /// <summary>
        /// Returns the behavior guidelines section header label.
        /// Priority: inspector override field (if non-empty) → JSON field → "BEHAVIOR GUIDELINES".
        /// </summary>
        private string GetEffectiveBehaviorSectionLabel()
        {
            if (!string.IsNullOrWhiteSpace(rulesAndGuidelines.behaviorSectionLabelOverride))
                return rulesAndGuidelines.behaviorSectionLabelOverride.ToUpper();
            string fromJson = scenarioConfig?.system_instructions?.behavior_section_label;
            return string.IsNullOrWhiteSpace(fromJson) ? "BEHAVIOR GUIDELINES" : fromJson.ToUpper();
        }

        /// <summary>
        /// Builds the set of meta-prefixes used by the fallback dialogue parser to strip
        /// LLM-added wrapper text. Generic linguistic patterns are always included; NPC names
        /// and roles are derived from the currently loaded scenario so no character names
        /// are ever hardcoded.
        /// </summary>
        private HashSet<string> BuildFallbackMetaPrefixes()
        {
            var prefixes = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
            {
                "description of",
                "npc response:",
                "character says:",
                "response:",
                "reply:",
                "as she responds",
                "as he responds",
                "as she says",
                "as he says",
                "as they say"
            };

            // Add names and roles from registered NPCs (populated after scenario load).
            foreach (var npc in registeredNPCs)
            {
                if (!string.IsNullOrWhiteSpace(npc.npcName))
                    prefixes.Add(npc.npcName.ToLower());
                if (!string.IsNullOrWhiteSpace(npc.characterRole))
                    prefixes.Add(npc.characterRole.ToLower());
            }

            // Supplement with raw scenario character data in case registration hasn't happened yet.
            if (scenarioConfig?.characters != null)
            {
                foreach (var ch in scenarioConfig.characters)
                {
                    if (!string.IsNullOrWhiteSpace(ch.name))  prefixes.Add(ch.name.ToLower());
                    if (!string.IsNullOrWhiteSpace(ch.role))  prefixes.Add(ch.role.ToLower());
                }
            }

            return prefixes;
        }

        // ── Context-menu editor actions ───────────────────────────────────────────
        // All of these appear when right-clicking the NPCManager component in the inspector.

        /// <summary>
        /// Resets the alias generator prompt template to the built-in default.
        /// Does not touch the LLM generation parameters.
        /// </summary>
        [ContextMenu("Defaults/Reset Alias Prompt Template to Default")]
        private void ResetAliasPromptTemplateToDefault()
        {
            if (aliasConfig == null) aliasConfig = new AliasGeneratorConfig();
            aliasConfig.promptTemplate = AliasGeneratorConfig.DefaultPromptTemplate;
            Debug.Log("[NPCManager] Alias prompt template reset to default.");
        }

        /// <summary>
        /// Resets the alias generator LLM parameters to built-in defaults.
        /// Does not touch the prompt template.
        /// </summary>
        [ContextMenu("Defaults/Reset Alias Generator Parameters to Default")]
        private void ResetAliasGeneratorParametersToDefault()
        {
            if (aliasConfig == null) aliasConfig = new AliasGeneratorConfig();
            aliasConfig.options.temperature   = AliasGeneratorConfig.DefaultOptions.temperature;
            aliasConfig.options.topP          = AliasGeneratorConfig.DefaultOptions.topP;
            aliasConfig.options.topK          = AliasGeneratorConfig.DefaultOptions.topK;
            aliasConfig.options.maxTokens     = AliasGeneratorConfig.DefaultOptions.maxTokens;
            aliasConfig.options.repeatPenalty = AliasGeneratorConfig.DefaultOptions.repeatPenalty;
            Debug.Log("[NPCManager] Alias generator parameters reset to default.");
        }

        /// <summary>
        /// Resets the NPC-to-NPC conversation prompt to the built-in scenario-neutral default.
        /// </summary>
        [ContextMenu("Defaults/Reset NPC Conversation Prompt to Default")]
        private void ResetNPCConversationPromptToDefault()
        {
            conversationPrompts.npcConversationPrompt = ConversationPromptsSettings.DefaultNPCConversationPrompt;
            Debug.Log("[NPCManager] NPC conversation prompt reset to default.");
        }

        /// <summary>
        /// Resets the idle-player prompt to the built-in scenario-neutral default.
        /// </summary>
        [ContextMenu("Defaults/Reset Idle Prompt to Default")]
        private void ResetIdlePromptToDefault()
        {
            conversationPrompts.idlePrompt = ConversationPromptsSettings.DefaultIdlePrompt;
            Debug.Log("[NPCManager] Idle player prompt reset to default.");
        }

        /// <summary>
        /// Resets the critical rules text to the built-in default.
        /// </summary>
        [ContextMenu("Defaults/Reset Critical Rules to Default")]
        private void ResetCriticalRulesToDefault()
        {
            rulesAndGuidelines.criticalRules = RulesAndGuidelinesSettings.DefaultCriticalRules;
            Debug.Log("[NPCManager] Critical rules reset to default.");
        }

        /// <summary>
        /// Resets the behavior guidelines text to the built-in generic default.
        /// The default is scenario-neutral and works as a starting point for any NPC setup.
        /// </summary>
        [ContextMenu("Defaults/Reset Behavior Guidelines to Default")]
        private void ResetBehaviorGuidelinesToDefault()
        {
            rulesAndGuidelines.behaviorGuidelines = RulesAndGuidelinesSettings.DefaultBehaviorGuidelines;
            Debug.Log("[NPCManager] Behavior guidelines reset to default.");
        }

        /// <summary>
        /// Reads the 'critical_rules' array from the loaded scenario JSON and writes it into
        /// the inspector fallback field. Logs a warning if the JSON has no such array.
        /// </summary>
        [ContextMenu("Extract from JSON/Extract Critical Rules from JSON")]
        private void ExtractCriticalRulesFromJSON()
        {
            if (scenarioConfig == null)
            {
                Debug.LogWarning("[NPCManager] No scenario loaded — cannot extract critical rules.");
                return;
            }
            var rules = scenarioConfig.system_instructions?.critical_rules;
            if (rules == null || rules.Length == 0)
            {
                Debug.LogWarning("[NPCManager] The loaded scenario JSON has no 'critical_rules' array.");
                return;
            }
            rulesAndGuidelines.criticalRules = string.Join("\n", System.Array.ConvertAll(rules, r => $"• {r}"));
            Debug.Log($"[NPCManager] Extracted {rules.Length} critical rule(s) from JSON.");
        }

        /// <summary>
        /// Reads the 'behavior_guidelines' (or legacy 'teaching_behavior') array from the loaded
        /// scenario JSON and writes it into the inspector fallback field.
        /// Logs a warning if the JSON has neither array.
        /// </summary>
        [ContextMenu("Extract from JSON/Extract Behavior Guidelines from JSON")]
        private void ExtractBehaviorGuidelinesFromJSON()
        {
            if (scenarioConfig == null)
            {
                Debug.LogWarning("[NPCManager] No scenario loaded — cannot extract behavior guidelines.");
                return;
            }
            var guidelines = scenarioConfig.system_instructions?.behavior_guidelines;
            if (guidelines == null || guidelines.Length == 0)
                guidelines = scenarioConfig.system_instructions?.teaching_behavior;
            if (guidelines == null || guidelines.Length == 0)
            {
                Debug.LogWarning("[NPCManager] The loaded scenario JSON has no 'behavior_guidelines' or 'teaching_behavior' array.");
                return;
            }
            rulesAndGuidelines.behaviorGuidelines = string.Join("\n", System.Array.ConvertAll(guidelines, b => $"• {b}"));
            Debug.Log($"[NPCManager] Extracted {guidelines.Length} behavior guideline(s) from JSON.");
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

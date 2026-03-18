using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using LAS;

namespace LAS {

    public class NPCManager : MonoBehaviour
    {
        [Header("UI Elements")]
        [SerializeField] private TMP_InputField playerInputField;
        [SerializeField] private Button sendButton;
        [SerializeField] private Transform chatContentParent;
        [SerializeField] private GameObject chatMessagePrefab;
    
        [Header("Scenario Configuration")]
        [SerializeField] private TextAsset scenarioConfigFile;
    
        [Header("Ollama Connection")]
        [SerializeField] private string baseUrl = "";
        [SerializeField] private List<string> availableModels = new List<string>();
        [SerializeField] private int selectedModelIndex = 0;
    
        [Header("AI Parameters")]
        [SerializeField] [Range(0.1f, 2.0f)] private float temperature = 0.7f;
        [SerializeField] [Range(0.1f, 1.0f)] private float topP = 0.9f;
        [SerializeField] [Range(1f, 100f)] private float topK = 40f;
        [SerializeField] private int maxTokensPerMessage = 150;
        [SerializeField] [Range(1.0f, 2.0f)] private float repeatPenalty = 1.1f;
    
        [Header("Action Classification")]
        [Tooltip("Temperature for the action classification step. Keep low (0.05–0.2) for deterministic action selection.")]
        [SerializeField] [Range(0.0f, 0.5f)] private float actionTemperature = 0.1f;
        [Tooltip("Top-K for action classification. Low value (5–15) keeps output tightly constrained.")]
        [SerializeField] [Range(1f, 40f)] private float actionTopK = 10f;
        [Tooltip("Max tokens for the action classification response. 80 is sufficient for a short JSON action object.")]
        [SerializeField] private int actionMaxTokens = 80;

        [Header("Streaming Settings")]
        [SerializeField] private bool enableSimulatedStreaming = true;
        [SerializeField] [Range(10f, 200f)] private float charactersPerSecond = 50f;
        [SerializeField] private bool adaptiveStreaming = true; // Adjusts speed based on generation time
    
        [Header("Context Management")]
        [SerializeField] private int contextHistoryLimit = 15;
        [SerializeField] private bool includeProgressionContext = true;
    
        [Header("Runtime Info")]
        [SerializeField] private int currentProgressionStep = 0;
        [SerializeField] private List<NPCController> registeredNPCs = new List<NPCController>();

        [Header("Debug")]
        [SerializeField] public bool showDetailedDebug = false;
        [SerializeField] public bool logPrompts = false;
        [SerializeField] public bool logLLMResponses = false;
    
        private ConversationFlowController flowController;
        private NPCActionDispatcher actionDispatcher;
        private ScenarioConfig scenarioConfig;
        private List<ChatMessage> conversationHistory = new List<ChatMessage>();
        private bool isProcessing = false;
        private const int DEFAULT_PORT = 11434;

        private bool _interruptGenerationFlag = false;
        private UnityWebRequest _activeRequest;
        private Coroutine _streamingTextCoroutine;
        private NPCController _activeSpeaker;
        private int _lastAutoSpeakerIndex = 0; // Tracks last NPC that spoke in auto-conversation to ensure alternation

        public string SelectedModel => selectedModelIndex >= 0 && selectedModelIndex < availableModels.Count 
            ? availableModels[selectedModelIndex] 
            : "";
    
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
            NPCEventBus.OnNPCRegistered += HandleNPCRegistration;
            NPCEventBus.OnNPCUnregistered += HandleNPCUnregistration;
            NPCEventBus.OnPlayerMessage += HandlePlayerMessage;
        }

        void OnDisable()
        {
            NPCEventBus.OnNPCRegistered -= HandleNPCRegistration;
            NPCEventBus.OnNPCUnregistered -= HandleNPCUnregistration;
            NPCEventBus.OnPlayerMessage -= HandlePlayerMessage;
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
            
                Debug.Log($"NPC registered at index {index}: {npc.gameObject.name}");
            }
        }

        private void HandleNPCUnregistration(NPCController npc)
        {
            if (registeredNPCs.Contains(npc))
            {
                registeredNPCs.Remove(npc);
                Debug.Log($"NPC unregistered: {npc.gameObject.name}");
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
                
                    // Assign character data to NPCs based on index
                    for (int i = 0; i < scenarioConfig.characters.Length; i++)
                    {
                        if (i < registeredNPCs.Count)
                        {
                            CharacterInfo character = scenarioConfig.characters[i];
                            NPCController npc = registeredNPCs[i];
                        
                            npc.AssignCharacterData(
                                character.name,
                                character.role,
                                GetFullCharacterDescription(character)
                            );
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to load scenario config: {e.Message}");
                    AddSystemMessage("ERROR: Failed to load scenario configuration!");
                    yield break;
                }
            }
        
            // Detect Ollama
            yield return DetectOllamaSettings();
        
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

        private IEnumerator DetectOllamaSettings()
        {
            string[] possibleUrls = new string[]
            {
                $"http://localhost:{DEFAULT_PORT}",
                $"http://127.0.0.1:{DEFAULT_PORT}",
            };

            bool found = false;
        
            foreach (string url in possibleUrls)
            {
                using (UnityWebRequest www = UnityWebRequest.Get(url + "/api/tags"))
                {
                    www.timeout = 2;
                    yield return www.SendWebRequest();

                    if (www.result == UnityWebRequest.Result.Success)
                    {
                        baseUrl = url;
                        found = true;
                    
                        try
                        {
                            OllamaTagsResponse tagsResponse = JsonUtility.FromJson<OllamaTagsResponse>(www.downloadHandler.text);
                        
                            if (tagsResponse.models != null && tagsResponse.models.Length > 0)
                            {
                                availableModels.Clear();
                                foreach (var model in tagsResponse.models)
                                {
                                    availableModels.Add(model.name);
                                }
                            
                                selectedModelIndex = 0;
                            
                                Debug.Log($"Ollama detected at {baseUrl}");
                                Debug.Log($"Selected model: {SelectedModel}");
                            
                                AddSystemMessage($"Connected - Using: {SelectedModel}");
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogError("Failed to parse models: " + e.Message);
                        }
                    
                        break;
                    }
                }
            }

            if (!found)
            {
                Debug.LogError("Could not detect Ollama. Make sure it's running with 'ollama serve'");
                AddSystemMessage("ERROR: Ollama not detected! Run 'ollama serve'");
            
                if (sendButton != null)
                {
                    sendButton.interactable = false;
                }
            }
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

        public void OnPlayerSendMessage()
        {
            if (string.IsNullOrEmpty(playerInputField?.text))
                return;

            string message = playerInputField.text;
            playerInputField.text = "";

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
            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(SelectedModel))
            {
                Debug.LogError("Ollama not configured!");
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

            OllamaOptions dialogueOptions = new OllamaOptions
            {
                temperature = temperature,
                top_p = topP,
                top_k = topK,
                num_predict = maxTokensPerMessage,
                repeat_penalty = repeatPenalty
            };

            string step1Raw = null;
            yield return StartCoroutine(StreamOllamaRequest(step1Prompt, dialogueOptions, r => step1Raw = r));

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

            string step2Prompt = BuildActionClassificationPrompt(npcIndex, userInput, step1Response.dialogue);

            if (logPrompts)
                Debug.Log($"[NPCManager] === LLM REQUEST (Action) ===\n{step2Prompt}");

            OllamaOptions actionOptions = new OllamaOptions
            {
                temperature = actionTemperature,
                top_p = 0.9f,
                top_k = actionTopK,
                num_predict = actionMaxTokens,
                repeat_penalty = 1.0f
            };

            string step2Raw = null;
            yield return StartCoroutine(StreamOllamaRequest(step2Prompt, actionOptions, r => step2Raw = r));

            // Step 2 failure is non-fatal — NPC simply performs no physical action.
            NPCActionOnly actionResult = null;
            if (!_interruptGenerationFlag && step2Raw != null)
            {
                if (logLLMResponses)
                    Debug.Log($"[NPCManager] === LLM RESPONSE (Action) ===\n{step2Raw}");

                try
                {
                    int si = step2Raw.IndexOf('{');
                    int ei = step2Raw.LastIndexOf('}') + 1;
                    if (si >= 0 && ei > si)
                        actionResult = JsonUtility.FromJson<NPCActionOnly>(step2Raw.Substring(si, ei - si));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[NPCManager] Failed to parse action response (non-fatal): {ex.Message}");
                }
            }

            if (_interruptGenerationFlag)
            {
                isProcessing = false;
                yield break;
            }

            // ── COMBINE: merge dialogue + action into a single NPCResponse ───────────

            NPCResponse finalResponse = new NPCResponse
            {
                npc_index             = npcIndex,
                dialogue              = step1Response.dialogue,
                internal_thought      = step1Response.internal_thought,
                action_key            = actionResult?.action_key ?? "NONE",
                action_target         = actionResult?.action_target ?? "",
                action_secondary_target = actionResult?.action_secondary_target ?? ""
            };

            if (showDetailedDebug || logLLMResponses)
            {
                string speakerName = (npcIndex >= 0 && npcIndex < registeredNPCs.Count)
                    ? registeredNPCs[npcIndex].npcName : $"NPC {npcIndex}";
                Debug.Log($"[NPCManager] === FINAL PARSED ===\n" +
                          $"Speaker: {speakerName} (NPC {npcIndex})\n" +
                          $"Dialogue: \"{finalResponse.dialogue}\"\n" +
                          $"Action: {finalResponse.action_key} \u2192 {finalResponse.action_target}\n" +
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

                // Dispatch action — use PlanAndExecuteTask for multi-step actions (HAND_TO_PLAYER / HAND_TO_NPC),
                // otherwise route through the standard dispatcher.
                if (!string.IsNullOrEmpty(finalResponse.action_key) && finalResponse.action_key != "NONE")
                {
                    if (actionDispatcher == null)
                        actionDispatcher = NPCActionDispatcher.Instance;

                    var behaviour = currentSpeaker.GetComponent<NPCBehaviourController>();
                    bool isMultiStep = finalResponse.action_key == "HAND_TO_PLAYER"
                                    || finalResponse.action_key == "HAND_TO_NPC";

                    if (behaviour != null && isMultiStep)
                    {
                        behaviour.PlanAndExecuteTask(userInput, finalResponse);
                    }
                    else if (actionDispatcher != null)
                    {
                        var command = new NPCActionCommand
                        {
                            npc_index               = finalResponse.npc_index,
                            action_key              = finalResponse.action_key,
                            action_target           = finalResponse.action_target,
                            action_secondary_target = finalResponse.action_secondary_target
                        };
                        actionDispatcher.Dispatch(command, registeredNPCs);
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
            Debug.Log($"[NPCManager] isProcessing reset to false");
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

        // ─── Two-step LLM helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Streams a single Ollama request and returns the full accumulated response via callback.
        /// Respects _interruptGenerationFlag — exits early without calling onComplete if interrupted.
        /// </summary>
        private IEnumerator StreamOllamaRequest(string prompt, OllamaOptions options, System.Action<string> onComplete)
        {
            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(SelectedModel) || _interruptGenerationFlag)
                yield break;

            OllamaRequest requestData = new OllamaRequest
            {
                model = SelectedModel,
                prompt = prompt,
                stream = true,
                options = options
            };

            string jsonData = JsonUtility.ToJson(requestData);
            string url = baseUrl + "/api/generate";

            UnityWebRequest www = new UnityWebRequest(url, "POST");
            _activeRequest = www;
            www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonData));
            StreamingDownloadHandler downloadHandler = new StreamingDownloadHandler();
            www.downloadHandler = downloadHandler;
            www.SetRequestHeader("Content-Type", "application/json");
            www.timeout = 0;

            var operation = www.SendWebRequest();
            StringBuilder fullResponse = new StringBuilder();

            while (!operation.isDone)
            {
                if (_interruptGenerationFlag)
                {
                    www.Abort();
                    www.Dispose();
                    _activeRequest = null;
                    yield break;
                }

                if (downloadHandler.HasNewData())
                {
                    string newText = downloadHandler.GetNewText();
                    foreach (string line in newText.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        try
                        {
                            OllamaStreamResponse sr = JsonUtility.FromJson<OllamaStreamResponse>(line);
                            if (!string.IsNullOrEmpty(sr.response))
                                fullResponse.Append(sr.response);
                        }
                        catch { }
                    }
                }
                yield return null;
            }

            // Flush any remaining buffered data.
            if (downloadHandler.HasNewData())
            {
                string newText = downloadHandler.GetNewText();
                foreach (string line in newText.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    try
                    {
                        OllamaStreamResponse sr = JsonUtility.FromJson<OllamaStreamResponse>(line);
                        if (!string.IsNullOrEmpty(sr.response))
                            fullResponse.Append(sr.response);
                    }
                    catch { }
                }
            }

            bool success = www.result == UnityWebRequest.Result.Success;
            string error = www.error;
            www.Dispose();
            _activeRequest = null;

            if (!success)
            {
                isProcessing = false;
                Debug.LogError($"[NPCManager] Ollama request failed: {error} | interruptFlag={_interruptGenerationFlag}| isProcessing={isProcessing}");
                yield break;
            }

            onComplete?.Invoke(fullResponse.ToString());
        }

        /// <summary>
        /// Interrupts any in-progress LLM generation and dialogue streaming.
        /// Safe to call from outside the coroutine (e.g. when player sends a message mid-stream).
        /// </summary>
        public void InterruptCurrentGeneration()
        {
            _interruptGenerationFlag = true;
            _activeRequest?.Abort();

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
                if (thoughtMatch.Success)
                    result.internal_thought = thoughtMatch.Groups[1].Value;

                // Collect all quoted strings, skip known JSON field names, keep the rest as dialogue candidates.
                    var fieldNames = new HashSet<string>
                        { "npc_index", "dialogue", "internal_thought", "action_key", "action_target", "action_secondary_target" };

                    var candidates = Regex
                        .Matches(raw, "\"((?:[^\"\\\\]|\\\\.){5,})\"")
                        .Cast<Match>()
                        .Select(m => m.Groups[1].Value)
                        .Where(v => !fieldNames.Contains(v))
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

                    if (cleaned.Count > 0)
                    {
                        result.dialogue = string.Join(" ", cleaned);
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
            prompt.AppendLine("  \"internal_thought\": string  — brief private reasoning");
            prompt.AppendLine("Do NOT add action_key or action_target — those are handled separately.");
            prompt.AppendLine("Correct output:");
            prompt.AppendLine("{\"npc_index\": 1, \"dialogue\": \"Let me show you the technique.\", \"internal_thought\": \"Needs hands-on guidance.\"}");
            prompt.AppendLine("WRONG (never do this):");
            prompt.AppendLine("description of NPC response: {\"npc_index\": 1 ...");
            prompt.AppendLine();
            prompt.AppendLine("Output ONLY the JSON object starting with { and ending with }:");

            return prompt.ToString();
        }

        /// <summary>
        /// Builds the Step 2 action classification prompt — short, focused, low-temperature.
        /// Only includes the triggering exchange and available actions (no full conversation history).
        /// </summary>
        private string BuildActionClassificationPrompt(int npcIndex, string playerInput, string npcDialogue)
        {
            StringBuilder prompt = new StringBuilder();

            string npcName = (npcIndex >= 0 && npcIndex < registeredNPCs.Count)
                ? registeredNPCs[npcIndex].npcName : $"NPC {npcIndex}";

            var npcBehaviour = (npcIndex >= 0 && npcIndex < registeredNPCs.Count)
                ? registeredNPCs[npcIndex].GetComponent<NPCBehaviourController>() : null;
            string npcState = npcBehaviour != null
                ? (npcBehaviour.IsHoldingObject
                    ? $"Currently holding: {npcBehaviour.HeldObject?.name ?? "an object"}"
                    : "Not holding any object")
                : "Unknown state";

            prompt.AppendLine("=== ACTION CLASSIFICATION ===");
            prompt.AppendLine($"NPC {npcIndex} ({npcName}) just responded to this exchange:");
            prompt.AppendLine();
            prompt.AppendLine($"PLAYER: \"{playerInput}\"");
            prompt.AppendLine($"{npcName} REPLIED: \"{npcDialogue}\"");
            prompt.AppendLine();
            prompt.AppendLine($"NPC physical state: {npcState}");
            prompt.AppendLine();

            prompt.AppendLine("=== ENVIRONMENT STATE ===");
            var targetRegistry = NPCActionTargetRegistry.Instance;
            if (targetRegistry != null)
            {
                foreach (var target in targetRegistry.GetAllTargets())
                {
                    if (target is InteractableItem item)
                    {
                        prompt.AppendLine($"- {item.TargetName}: {item.GetStateDescription()}");
                    }
                    else if (target is LocationTarget loc)
                    {
                        prompt.AppendLine($"- {loc.TargetName}: {loc.GetStateDescription()}");
                    }
                }
            }
            prompt.AppendLine();

            if (actionDispatcher == null) actionDispatcher = NPCActionDispatcher.Instance;
            if (actionDispatcher != null)
            {
                string vocab = actionDispatcher.BuildActionVocabularyPrompt();
                if (!string.IsNullOrEmpty(vocab))
                    prompt.AppendLine(vocab);
            }

            prompt.AppendLine("=== TASK ===");
            prompt.AppendLine($"Select the physical action {npcName} should perform based on what they said.");
            prompt.AppendLine("RULES FOR action_target:");
            prompt.AppendLine("  • Use ONLY exact names from the Valid target names list above.");
            prompt.AppendLine("  • For PICK_UP or GO_TO: target must be an OBJECT name, never a person's name.");
            prompt.AppendLine("  • Match the target to what the PLAYER originally requested, not to names the NPC mentioned.");
            prompt.AppendLine("If no physical action is needed, use action_key: \"NONE\".");
            prompt.AppendLine();
            prompt.AppendLine("Respond with ONLY valid JSON (no other text):");
            prompt.AppendLine("{\"action_key\": \"ACTION_KEY_HERE\", \"action_target\": \"exact_registered_name_or_empty\", \"action_secondary_target\": \"\"}");

            return prompt.ToString();
        }

        private string BuildStructuredPrompt(string currentInput, int specificNPCIndex)
        {
            StringBuilder prompt = new StringBuilder();
        
            // Scenario context
            if (scenarioConfig != null)
            {
                prompt.AppendLine("=== SCENARIO CONTEXT ===");
                prompt.AppendLine($"Setting: {scenarioConfig.scenario.setting}");
                prompt.AppendLine($"Timeframe: {scenarioConfig.scenario.timeframe}");
                if (scenarioConfig.conversation_initialization != null)
                {
                    prompt.AppendLine($"Context: {scenarioConfig.conversation_initialization.context}");
                }
                prompt.AppendLine();
            
                // Player context
                if (scenarioConfig.player_character != null)
                {
                    prompt.AppendLine("=== PLAYER CHARACTER ===");
                    prompt.AppendLine($"Role: {scenarioConfig.player_character.role}");
                    prompt.AppendLine($"Description: {scenarioConfig.player_character.description}");
                    prompt.AppendLine();
                }
            
                // System instructions - CORE RULES
                prompt.AppendLine("=== CORE RULES (FOLLOW STRICTLY) ===");
                foreach (var rule in scenarioConfig.system_instructions.core_rules)
                {
                    prompt.AppendLine($"• {rule}");
                }
                prompt.AppendLine();
            
                // Characters
                prompt.AppendLine("=== CHARACTERS (BY INDEX) ===");
                for (int i = 0; i < scenarioConfig.characters.Length && i < registeredNPCs.Count; i++)
                {
                    var character = scenarioConfig.characters[i];
                    var npc = registeredNPCs[i];
                    prompt.AppendLine($"NPC {i}: {character.name} ({character.role})");
                    prompt.AppendLine($"  Personality: {character.personality}");
                    prompt.AppendLine($"  Teaching: {character.teaching_approach}");
                    prompt.AppendLine($"  Current: {character.current_state}");
                }
                prompt.AppendLine();
            
                // Progression steps if enabled
                if (includeProgressionContext && scenarioConfig.required_progression_steps != null)
                {
                    prompt.AppendLine("=== REQUIRED PROGRESSION STEPS ===");
                    prompt.AppendLine("You MUST guide the student through these steps in order:");
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
            }
        
            // Recent conversation history
            if (conversationHistory.Count > 0)
            {
                prompt.AppendLine("=== RECENT CONVERSATION ===");
                int startIndex = Mathf.Max(0, conversationHistory.Count - contextHistoryLimit);
                for (int i = startIndex; i < conversationHistory.Count; i++)
                {
                    var msg = conversationHistory[i];
                    prompt.AppendLine($"{msg.speaker}: {msg.message}");
                }
                prompt.AppendLine();
            }
        
            // Current input
            prompt.AppendLine("=== CURRENT INPUT ===");
            prompt.AppendLine(currentInput);
            prompt.AppendLine();
        
            // Response format instructions
            if (scenarioConfig != null)
            {
                prompt.AppendLine("=== REQUIRED RESPONSE FORMAT ===");
                foreach (var rule in scenarioConfig.system_instructions.response_format)
                {
                    prompt.AppendLine($"• {rule}");
                }
                prompt.AppendLine();
            
                // CRITICAL: Prevent self-conversation
                prompt.AppendLine("=== CRITICAL RULES ===");
                prompt.AppendLine("• If YOU just asked a question, WAIT for someone else to answer");
                prompt.AppendLine("• NEVER answer your own questions");
                prompt.AppendLine("• NEVER have multiple exchanges by yourself");
                prompt.AppendLine("• After you speak, someone else should respond next");
                prompt.AppendLine();
            
                // Interaction guidelines
                prompt.AppendLine("=== INTERACTION GUIDELINES ===");
                foreach (var guideline in scenarioConfig.system_instructions.interaction_guidelines)
                {
                    prompt.AppendLine($"• {guideline}");
                }
                prompt.AppendLine();
            
                // Teaching behavior
                if (scenarioConfig.system_instructions.teaching_behavior != null)
                {
                    prompt.AppendLine("=== TEACHING BEHAVIOR ===");
                    foreach (var behavior in scenarioConfig.system_instructions.teaching_behavior)
                    {
                        prompt.AppendLine($"• {behavior}");
                    }
                    prompt.AppendLine();
                }
            
                prompt.AppendLine("Example response format:");
                prompt.AppendLine(JsonUtility.ToJson(scenarioConfig.example_response_format, true));
                prompt.AppendLine();
            }
        
            // Action vocabulary (auto-built from dispatcher's action definitions)
            if (actionDispatcher == null)
                actionDispatcher = NPCActionDispatcher.Instance;
            if (actionDispatcher != null)
            {
                string vocab = actionDispatcher.BuildActionVocabularyPrompt();
                if (!string.IsNullOrEmpty(vocab))
                {
                    prompt.AppendLine(vocab);
                }
            }
        
            // Specific NPC instruction
            if (specificNPCIndex >= 0 && specificNPCIndex < registeredNPCs.Count)
            {
                prompt.AppendLine($"=== YOU ARE NPC {specificNPCIndex} ===");
                prompt.AppendLine($"Respond as {registeredNPCs[specificNPCIndex].npcName}");
            }
            else
            {
                prompt.AppendLine("=== DETERMINE WHO SHOULD RESPOND ===");
                prompt.AppendLine("Based on the context and who was addressed, decide which NPC should respond.");
                prompt.AppendLine("If the Player addressed a specific person, THAT person must respond.");
            }
        
            prompt.AppendLine();
            prompt.AppendLine("Respond NOW in valid JSON format (no extra text):");
        
            return prompt.ToString();
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

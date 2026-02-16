using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System;
using System.Linq;

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
    [SerializeField][Range(0.1f, 2.0f)] private float temperature = 0.7f;
    [SerializeField][Range(0.1f, 1.0f)] private float topP = 0.9f;
    [SerializeField][Range(1f, 100f)] private float topK = 40f;
    [SerializeField] private int maxTokensPerMessage = 150;
    [SerializeField][Range(1.0f, 2.0f)] private float repeatPenalty = 1.1f;

    [Header("Streaming Settings")]
    [SerializeField] private bool enableSimulatedStreaming = true;
    [SerializeField][Range(10f, 200f)] private float charactersPerSecond = 50f;
    [SerializeField] private bool adaptiveStreaming = true; // Adjusts speed based on generation time

    [Header("Context Management")]
    [SerializeField] private int contextHistoryLimit = 15;
    [SerializeField] private bool includeProgressionContext = true;

    [Header("Runtime Info")]
    [SerializeField] private int currentProgressionStep = 0;
    [SerializeField] private List<NPCController> registeredNPCs = new List<NPCController>();

    private ScenarioConfig scenarioConfig;
    private List<ChatMessage> conversationHistory = new List<ChatMessage>();
    private bool isProcessing = false;
    private const int DEFAULT_PORT = 11434;

    public string SelectedModel => selectedModelIndex >= 0 && selectedModelIndex < availableModels.Count
        ? availableModels[selectedModelIndex]
        : "";

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
        if (isProcessing || string.IsNullOrEmpty(playerInputField?.text))
            return;

        string message = playerInputField.text;
        playerInputField.text = "";

        AddChatMessage("Player", message, MessageType.Player);
        NPCEventBus.BroadcastPlayerMessage(message);

        StartCoroutine(GetNPCResponse(message, -1));
    }

    public void TriggerNPCToNPCConversation()
    {
        if (isProcessing)
            return;

        StartCoroutine(NPCConversationRoutine());
    }

    private IEnumerator NPCConversationRoutine()
    {
        string prompt = "Continue the teaching session. Address the next aspect of preparation that needs to be covered.";
        yield return GetNPCResponse(prompt, -1);

        yield return new WaitForSeconds(1.5f);

        string followUp = "Respond naturally to what was just said or demonstrated.";
        yield return GetNPCResponse(followUp, -1);
    }

    private IEnumerator GetNPCResponse(string userInput, int specificNPCIndex)
    {
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(SelectedModel))
        {
            Debug.LogError("Ollama not configured!");
            yield break;
        }

        isProcessing = true;

        string prompt = BuildStructuredPrompt(userInput, specificNPCIndex);

        ChatMessage tempMessage = null;
        NPCController currentSpeaker = null;
        StringBuilder fullResponse = new StringBuilder();
        float generationStartTime = Time.time;

        OllamaRequest requestData = new OllamaRequest
        {
            model = SelectedModel,
            prompt = prompt,
            stream = true,
            options = new OllamaOptions
            {
                temperature = temperature,
                top_p = topP,
                top_k = topK,
                num_predict = maxTokensPerMessage,
                repeat_penalty = repeatPenalty
            }
        };

        string jsonData = JsonUtility.ToJson(requestData);
        string url = baseUrl + "/api/generate";

        using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);

            StreamingDownloadHandler downloadHandler = new StreamingDownloadHandler();
            www.downloadHandler = downloadHandler;

            www.SetRequestHeader("Content-Type", "application/json");
            www.timeout = 0;

            var operation = www.SendWebRequest();

            // Collect all streaming data without displaying yet
            while (!operation.isDone)
            {
                if (downloadHandler.HasNewData())
                {
                    string newText = downloadHandler.GetNewText();
                    string[] lines = newText.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (string line in lines)
                    {
                        try
                        {
                            OllamaStreamResponse streamResponse = JsonUtility.FromJson<OllamaStreamResponse>(line);

                            if (!string.IsNullOrEmpty(streamResponse.response))
                            {
                                fullResponse.Append(streamResponse.response);
                            }
                        }
                        catch { }
                    }
                }

                yield return null;
            }

            // Final processing
            if (downloadHandler.HasNewData())
            {
                string newText = downloadHandler.GetNewText();
                string[] lines = newText.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string line in lines)
                {
                    try
                    {
                        OllamaStreamResponse streamResponse = JsonUtility.FromJson<OllamaStreamResponse>(line);
                        if (!string.IsNullOrEmpty(streamResponse.response))
                        {
                            fullResponse.Append(streamResponse.response);
                        }
                    }
                    catch { }
                }
            }

            // Calculate generation time
            float generationTime = Time.time - generationStartTime;

            // Parse final response
            string finalText = fullResponse.ToString();
            NPCResponse finalResponse = null;

            try
            {
                int startIndex = finalText.IndexOf('{');
                int endIndex = finalText.LastIndexOf('}') + 1;
                if (startIndex >= 0 && endIndex > startIndex)
                {
                    string jsonPart = finalText.Substring(startIndex, endIndex - startIndex);
                    finalResponse = JsonUtility.FromJson<NPCResponse>(jsonPart);

                    // Identify speaker
                    if (finalResponse.npc_index >= 0 && finalResponse.npc_index < registeredNPCs.Count)
                    {
                        currentSpeaker = registeredNPCs[finalResponse.npc_index];
                        currentSpeaker.SetSpeaking(true);

                        // Create message with empty text
                        tempMessage = AddChatMessage(
                            currentSpeaker.npcName,
                            "",
                            MessageType.NPC,
                            currentSpeaker
                        );

                        NPCEventBus.BroadcastNPCStartedSpeaking(finalResponse.npc_index, "");

                        // Log action if present
                        if (!string.IsNullOrEmpty(finalResponse.action))
                        {
                            Debug.Log($"[{currentSpeaker.npcName}] Action: {finalResponse.action}");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to parse final response: {e.Message}");
                Debug.LogError($"Response was: {finalText}");

                AddSystemMessage("[Error: Could not parse NPC response]");
            }

            // Handle network errors
            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Error: " + www.error);
                if (tempMessage != null)
                {
                    tempMessage.UpdateText("[Network error]");
                }
                else
                {
                    AddSystemMessage("[Network error]");
                }
            }
            else if (finalResponse != null && tempMessage != null && !string.IsNullOrEmpty(finalResponse.dialogue))
            {
                // Now stream the dialogue text with adaptive speed
                if (enableSimulatedStreaming)
                {
                    float streamSpeed = charactersPerSecond;

                    // Adaptive streaming: if generation was slow, stream faster to maintain flow
                    if (adaptiveStreaming)
                    {
                        // If generation took longer than 2 seconds, increase stream speed
                        if (generationTime > 2f)
                        {
                            float speedMultiplier = Mathf.Min(2f, generationTime / 2f);
                            streamSpeed *= speedMultiplier;
                        }
                        // If generation was very fast (< 0.5s), slow down streaming for realism
                        else if (generationTime < 0.5f)
                        {
                            streamSpeed *= 0.7f;
                        }
                    }

                    yield return StartCoroutine(StreamTextToMessage(tempMessage, finalResponse.dialogue, streamSpeed));
                }
                else
                {
                    // No streaming, just set the text immediately
                    tempMessage.UpdateText(finalResponse.dialogue);
                }
            }
        }

        if (currentSpeaker != null)
        {
            currentSpeaker.SetSpeaking(false);
            NPCEventBus.BroadcastNPCFinishedSpeaking(currentSpeaker.AssignedIndex);
        }

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
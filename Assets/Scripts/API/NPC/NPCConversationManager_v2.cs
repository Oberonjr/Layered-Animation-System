using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System;
using System.Linq;

public class NPCConversationManager : MonoBehaviour
{
    [Header("NPC References")]
    [SerializeField] private NPCController npcA;
    [SerializeField] private NPCController npcB;
    
    [Header("UI Elements")]
    [SerializeField] private TMP_InputField playerInputField;
    [SerializeField] private Button sendButton;
    [SerializeField] private Transform chatContentParent;
    [SerializeField] private GameObject chatMessagePrefab;
    
    [Header("Scenario Configuration")]
    [SerializeField] private TextAsset scenarioConfigFile;
    
    [Header("Ollama Settings")]
    [SerializeField] private string baseUrl = "";
    [SerializeField] private List<string> availableModels = new List<string>();
    [SerializeField] private int selectedModelIndex = 0;
    [SerializeField] private int contextHistoryLimit = 15;
    
    private ScenarioConfig scenarioConfig;
    private Dictionary<string, NPCController> characterMap = new Dictionary<string, NPCController>();
    private List<ChatMessage> conversationHistory = new List<ChatMessage>();
    private bool isProcessing = false;
    private const int DEFAULT_PORT = 11434;

    public string SelectedModel => selectedModelIndex >= 0 && selectedModelIndex < availableModels.Count 
        ? availableModels[selectedModelIndex] 
        : "";

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

    private IEnumerator InitializeSystem()
    {
        // Load scenario configuration
        if (scenarioConfigFile != null)
        {
            try
            {
                scenarioConfig = JsonUtility.FromJson<ScenarioConfig>(scenarioConfigFile.text);
                Debug.Log($"Loaded scenario: {scenarioConfig.scenario.title}");
                
                // Map characters to NPCs
                if (scenarioConfig.characters.Length >= 2)
                {
                    characterMap[scenarioConfig.characters[0].id] = npcA;
                    characterMap[scenarioConfig.characters[1].id] = npcB;
                    
                    // Update NPC names and descriptions
                    npcA.npcName = scenarioConfig.characters[0].name;
                    npcA.characterId = scenarioConfig.characters[0].id;
                    npcA.characterDescription = GetFullCharacterDescription(scenarioConfig.characters[0]);
                    
                    npcB.npcName = scenarioConfig.characters[1].name;
                    npcB.characterId = scenarioConfig.characters[1].id;
                    npcB.characterDescription = GetFullCharacterDescription(scenarioConfig.characters[1]);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load scenario config: {e.Message}");
            }
        }
        
        // Detect Ollama
        yield return DetectOllamaSettings();
        
        // Add initial scenario message
        if (scenarioConfig != null)
        {
            AddSystemMessage($"Scenario: {scenarioConfig.scenario.title}");
            AddSystemMessage(scenarioConfig.scenario.description);
            AddSystemMessage(scenarioConfig.initial_situation);
        }
    }

    private string GetFullCharacterDescription(CharacterInfo character)
    {
        StringBuilder desc = new StringBuilder();
        desc.AppendLine($"Role: {character.role}");
        desc.AppendLine($"Personality: {character.personality}");
        desc.AppendLine($"Background: {character.background}");
        desc.AppendLine($"Communication Style: {character.communication_style}");
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
                        TagsResponse tagsResponse = JsonUtility.FromJson<TagsResponse>(www.downloadHandler.text);
                        
                        if (tagsResponse.models != null && tagsResponse.models.Length > 0)
                        {
                            availableModels.Clear();
                            foreach (var model in tagsResponse.models)
                            {
                                availableModels.Add(model.name);
                            }
                            
                            selectedModelIndex = 0;
                            
                            Debug.Log($"Ollama detected at {baseUrl}");
                            Debug.Log($"Available models: {string.Join(", ", availableModels)}");
                            Debug.Log($"Selected model: {SelectedModel}");
                            
                            AddSystemMessage($"Connected - Model: {SelectedModel}");
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
            AddSystemMessage("Error: Ollama not detected!");
            
            if (sendButton != null)
            {
                sendButton.interactable = false;
            }
        }
    }

    public void OnPlayerSendMessage()
    {
        if (isProcessing || string.IsNullOrEmpty(playerInputField.text))
            return;

        string message = playerInputField.text;
        playerInputField.text = "";
        
        AddChatMessage("Player", message, MessageType.Player);
        
        StartCoroutine(RoutePlayerMessage(message));
    }

    private IEnumerator RoutePlayerMessage(string message)
    {
        yield return GetNPCResponse(message, null);
    }

    public void TriggerNPCToNPCConversation()
    {
        if (isProcessing)
            return;
            
        StartCoroutine(NPCConversationRoutine());
    }

    private IEnumerator NPCConversationRoutine()
    {
        string prompt = "The surgical team continues their preparation. One of you should naturally continue the conversation or address something that needs attention.";
        yield return GetNPCResponse(prompt, null);
        
        yield return new WaitForSeconds(1f);
        
        string followUpPrompt = "Respond naturally to what was just said or done.";
        yield return GetNPCResponse(followUpPrompt, null);
    }

    private IEnumerator GetNPCResponse(string userMessage, NPCController specificNPC)
    {
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(SelectedModel))
        {
            Debug.LogError("Ollama not configured!");
            yield break;
        }

        isProcessing = true;
        
        string prompt = BuildStructuredPrompt(userMessage, specificNPC);
        
        ChatMessage tempMessage = null;
        NPCController currentSpeaker = null;
        StringBuilder fullResponse = new StringBuilder();
        
        OllamaRequest requestData = new OllamaRequest
        {
            model = SelectedModel,
            prompt = prompt,
            stream = true,
            options = new OllamaOptions
            {
                temperature = 0.7f,
                top_p = 0.9f
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
                                
                                // Try to parse JSON response as it streams
                                string currentText = fullResponse.ToString();
                                if (currentText.Contains("{") && currentText.Contains("}"))
                                {
                                    try
                                    {
                                        int startIndex = currentText.IndexOf('{');
                                        int endIndex = currentText.LastIndexOf('}') + 1;
                                        if (startIndex >= 0 && endIndex > startIndex)
                                        {
                                            string jsonPart = currentText.Substring(startIndex, endIndex - startIndex);
                                            NPCResponse npcResponse = JsonUtility.FromJson<NPCResponse>(jsonPart);
                                            
                                            // Identify speaker if not yet set
                                            if (currentSpeaker == null && !string.IsNullOrEmpty(npcResponse.character_id))
                                            {
                                                if (characterMap.TryGetValue(npcResponse.character_id, out NPCController npc))
                                                {
                                                    currentSpeaker = npc;
                                                    currentSpeaker.SetSpeaking(true);
                                                    
                                                    tempMessage = AddChatMessage(
                                                        currentSpeaker.npcName, 
                                                        "", 
                                                        currentSpeaker.messageType
                                                    );
                                                }
                                            }
                                            
                                            // Update dialogue text
                                            if (tempMessage != null && !string.IsNullOrEmpty(npcResponse.dialogue))
                                            {
                                                tempMessage.UpdateText(npcResponse.dialogue);
                                            }
                                        }
                                    }
                                    catch
                                    {
                                        // JSON not complete yet
                                    }
                                }
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
                fullResponse.Append(newText);
            }

            // Parse final response
            string finalText = fullResponse.ToString();
            try
            {
                int startIndex = finalText.IndexOf('{');
                int endIndex = finalText.LastIndexOf('}') + 1;
                if (startIndex >= 0 && endIndex > startIndex)
                {
                    string jsonPart = finalText.Substring(startIndex, endIndex - startIndex);
                    NPCResponse finalResponse = JsonUtility.FromJson<NPCResponse>(jsonPart);
                    
                    if (currentSpeaker == null && !string.IsNullOrEmpty(finalResponse.character_id))
                    {
                        if (characterMap.TryGetValue(finalResponse.character_id, out NPCController npc))
                        {
                            currentSpeaker = npc;
                            tempMessage = AddChatMessage(
                                currentSpeaker.npcName, 
                                finalResponse.dialogue, 
                                currentSpeaker.messageType
                            );
                        }
                    }
                    else if (tempMessage != null)
                    {
                        tempMessage.UpdateText(finalResponse.dialogue);
                    }
                    
                    if (!string.IsNullOrEmpty(finalResponse.action))
                    {
                        Debug.Log($"[{currentSpeaker?.npcName}] Action: {finalResponse.action}");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to parse final response: {e.Message}");
                Debug.LogError($"Response was: {finalText}");
                
                if (tempMessage == null)
                {
                    AddSystemMessage("[Error parsing NPC response]");
                }
            }

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Error: " + www.error);
                if (tempMessage != null)
                {
                    tempMessage.UpdateText("[Error getting response]");
                }
                else
                {
                    AddSystemMessage("[Error getting response]");
                }
            }
        }

        if (currentSpeaker != null)
        {
            currentSpeaker.SetSpeaking(false);
        }
        
        isProcessing = false;
    }

    private string BuildStructuredPrompt(string currentInput, NPCController specificNPC)
    {
        StringBuilder prompt = new StringBuilder();
        
        // Add scenario context
        if (scenarioConfig != null)
        {
            prompt.AppendLine("=== SCENARIO CONTEXT ===");
            prompt.AppendLine($"Setting: {scenarioConfig.scenario.setting}");
            prompt.AppendLine($"Situation: {scenarioConfig.initial_situation}");
            prompt.AppendLine();
            
            // Add system instructions
            prompt.AppendLine("=== CORE RULES ===");
            foreach (var rule in scenarioConfig.system_instructions.core_rules)
            {
                prompt.AppendLine($"- {rule}");
            }
            prompt.AppendLine();
            
            // Add all characters
            prompt.AppendLine("=== CHARACTERS PRESENT ===");
            foreach (var character in scenarioConfig.characters)
            {
                prompt.AppendLine($"[{character.id}] {character.name} - {character.role}");
                prompt.AppendLine($"  Personality: {character.personality}");
                prompt.AppendLine($"  Current: {character.current_state}");
            }
            prompt.AppendLine();
        }
        
        // Add conversation history
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
        
        // Add current input
        prompt.AppendLine("=== CURRENT INPUT ===");
        prompt.AppendLine(currentInput);
        prompt.AppendLine();
        
        // Add response format
        if (scenarioConfig != null)
        {
            prompt.AppendLine("=== RESPONSE FORMAT REQUIRED ===");
            foreach (var rule in scenarioConfig.system_instructions.response_format)
            {
                prompt.AppendLine($"- {rule}");
            }
            prompt.AppendLine();
            prompt.AppendLine("Example format:");
            prompt.AppendLine(JsonUtility.ToJson(scenarioConfig.example_response_format, true));
            prompt.AppendLine();
        }
        
        if (specificNPC != null)
        {
            prompt.AppendLine($"=== YOU ARE: {specificNPC.characterId} ===");
        }
        else
        {
            prompt.AppendLine("=== DETERMINE WHO SHOULD RESPOND ===");
            prompt.AppendLine("Based on the context, decide which character should naturally respond.");
            prompt.AppendLine("Use the character_id field to indicate who is speaking.");
        }
        
        prompt.AppendLine();
        prompt.AppendLine("Respond now in valid JSON format:");
        
        return prompt.ToString();
    }

    private ChatMessage AddChatMessage(string speaker, string message, MessageType type)
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
            chatMessage.Initialize(speaker, message, type);
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

    private class StreamingDownloadHandler : DownloadHandlerScript
    {
        private StringBuilder receivedData = new StringBuilder();
        private int lastProcessedLength = 0;

        public StreamingDownloadHandler() : base(new byte[1024 * 1024])
        {
        }

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            if (data == null || dataLength == 0)
                return false;

            string text = Encoding.UTF8.GetString(data, 0, dataLength);
            receivedData.Append(text);
            
            return true;
        }

        public bool HasNewData()
        {
            return receivedData.Length > lastProcessedLength;
        }

        public string GetNewText()
        {
            if (!HasNewData())
                return string.Empty;

            string newText = receivedData.ToString(lastProcessedLength, receivedData.Length - lastProcessedLength);
            lastProcessedLength = receivedData.Length;
            return newText;
        }
    }

    [System.Serializable]
    private class OllamaRequest
    {
        public string model;
        public string prompt;
        public bool stream;
        public OllamaOptions options;
    }

    [System.Serializable]
    private class OllamaOptions
    {
        public float temperature;
        public float top_p;
    }

    [System.Serializable]
    private class OllamaStreamResponse
    {
        public string response;
        public bool done;
    }

    [System.Serializable]
    private class TagsResponse
    {
        public ModelInfo[] models;
    }

    [System.Serializable]
    private class ModelInfo
    {
        public string name;
    }
}

public enum MessageType
{
    Player,
    NPCA,
    NPCB,
    System
}

//using UnityEngine;
//using UnityEngine.UI;
//using UnityEngine.Networking;
//using TMPro;
//using System.Collections;
//using System.Collections.Generic;
//using System.Text;
//using System;
//using System.Linq;

//public class NPCConversationManager : MonoBehaviour
//{
//    [Header("NPC References")]
//    [SerializeField] private NPCController npcA;
//    [SerializeField] private NPCController npcB;

//    [Header("UI Elements")]
//    [SerializeField] private TMP_InputField playerInputField;
//    [SerializeField] private Button sendButton;
//    [SerializeField] private Transform chatContentParent; // Content of ScrollView
//    [SerializeField] private GameObject chatMessagePrefab;

//    [Header("Ollama Settings")]
//    [SerializeField] private string baseUrl = "";
//    [SerializeField] private List<string> availableModels = new List<string>();
//    [SerializeField] private int selectedModelIndex = 0;

//    private List<ChatMessage> conversationHistory = new List<ChatMessage>();
//    private bool isProcessing = false;
//    private const int DEFAULT_PORT = 11434;

//    public string SelectedModel => selectedModelIndex >= 0 && selectedModelIndex < availableModels.Count
//        ? availableModels[selectedModelIndex]
//        : "";

//    void Start()
//    {
//        if (sendButton != null)
//        {
//            sendButton.onClick.AddListener(OnPlayerSendMessage);
//        }

//        if (playerInputField != null)
//        {
//            playerInputField.onSubmit.AddListener(delegate { OnPlayerSendMessage(); });
//        }

//        StartCoroutine(DetectOllamaSettings());
//    }

//    private IEnumerator DetectOllamaSettings()
//    {
//        string[] possibleUrls = new string[]
//        {
//            $"http://localhost:{DEFAULT_PORT}",
//            $"http://127.0.0.1:{DEFAULT_PORT}",
//        };

//        bool found = false;

//        foreach (string url in possibleUrls)
//        {
//            using (UnityWebRequest www = UnityWebRequest.Get(url + "/api/tags"))
//            {
//                www.timeout = 2;
//                yield return www.SendWebRequest();

//                if (www.result == UnityWebRequest.Result.Success)
//                {
//                    baseUrl = url;
//                    found = true;

//                    try
//                    {
//                        TagsResponse tagsResponse = JsonUtility.FromJson<TagsResponse>(www.downloadHandler.text);

//                        if (tagsResponse.models != null && tagsResponse.models.Length > 0)
//                        {
//                            availableModels.Clear();
//                            foreach (var model in tagsResponse.models)
//                            {
//                                availableModels.Add(model.name);
//                            }

//                            selectedModelIndex = 0;

//                            Debug.Log($"Ollama detected at {baseUrl}");
//                            Debug.Log($"Available models: {string.Join(", ", availableModels)}");
//                            Debug.Log($"Selected model: {SelectedModel}");

//                            AddSystemMessage($"Connected to Ollama - Model: {SelectedModel}");
//                        }
//                    }
//                    catch (Exception e)
//                    {
//                        Debug.LogError("Failed to parse models: " + e.Message);
//                    }

//                    break;
//                }
//            }
//        }

//        if (!found)
//        {
//            Debug.LogError("Could not detect Ollama. Make sure it's running with 'ollama serve'");
//            AddSystemMessage("Error: Ollama not detected! Run 'ollama serve'");

//            if (sendButton != null)
//            {
//                sendButton.interactable = false;
//            }
//        }
//    }

//    public void OnPlayerSendMessage()
//    {
//        if (isProcessing || string.IsNullOrEmpty(playerInputField.text))
//            return;

//        string message = playerInputField.text;
//        playerInputField.text = "";

//        AddChatMessage("Player", message, MessageType.Player);

//        // For now, randomly choose an NPC to respond
//        NPCController respondingNPC = UnityEngine.Random.value > 0.5f ? npcA : npcB;
//        StartCoroutine(GetNPCResponse(respondingNPC, message));
//    }

//    public void TriggerNPCToNPCConversation()
//    {
//        if (isProcessing)
//            return;

//        StartCoroutine(NPCConversationRoutine());
//    }

//    private IEnumerator NPCConversationRoutine()
//    {
//        // NPC A starts
//        string promptA = BuildPromptForNPC(npcA, "Start a conversation with the other character.");
//        yield return GetNPCResponse(npcA, promptA);

//        yield return new WaitForSeconds(1f);

//        // NPC B responds
//        string promptB = BuildPromptForNPC(npcB, "Respond to what was just said.");
//        yield return GetNPCResponse(npcB, promptB);
//    }

//    private IEnumerator GetNPCResponse(NPCController npc, string userMessage)
//    {
//        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(SelectedModel))
//        {
//            Debug.LogError("Ollama not configured!");
//            yield break;
//        }

//        isProcessing = true;
//        npc.SetSpeaking(true);

//        string prompt = BuildPromptForNPC(npc, userMessage);

//        // Add temporary message
//        ChatMessage tempMessage = AddChatMessage(npc.npcName, "", npc.messageType);

//        OllamaRequest requestData = new OllamaRequest
//        {
//            model = SelectedModel,
//            prompt = prompt,
//            stream = true
//        };

//        string jsonData = JsonUtility.ToJson(requestData);
//        string url = baseUrl + "/api/generate";

//        using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
//        {
//            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
//            www.uploadHandler = new UploadHandlerRaw(bodyRaw);

//            StreamingDownloadHandler downloadHandler = new StreamingDownloadHandler();
//            www.downloadHandler = downloadHandler;

//            www.SetRequestHeader("Content-Type", "application/json");
//            www.timeout = 0;

//            var operation = www.SendWebRequest();

//            StringBuilder fullResponse = new StringBuilder();

//            while (!operation.isDone)
//            {
//                if (downloadHandler.HasNewData())
//                {
//                    string newText = downloadHandler.GetNewText();
//                    string[] lines = newText.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

//                    foreach (string line in lines)
//                    {
//                        try
//                        {
//                            OllamaStreamResponse streamResponse = JsonUtility.FromJson<OllamaStreamResponse>(line);

//                            if (!string.IsNullOrEmpty(streamResponse.response))
//                            {
//                                fullResponse.Append(streamResponse.response);
//                                tempMessage.UpdateText(fullResponse.ToString());
//                            }
//                        }
//                        catch { }
//                    }
//                }

//                yield return null;
//            }

//            // Process remaining data
//            if (downloadHandler.HasNewData())
//            {
//                string newText = downloadHandler.GetNewText();
//                string[] lines = newText.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

//                foreach (string line in lines)
//                {
//                    try
//                    {
//                        OllamaStreamResponse streamResponse = JsonUtility.FromJson<OllamaStreamResponse>(line);

//                        if (!string.IsNullOrEmpty(streamResponse.response))
//                        {
//                            fullResponse.Append(streamResponse.response);
//                            tempMessage.UpdateText(fullResponse.ToString());
//                        }
//                    }
//                    catch { }
//                }
//            }

//            if (www.result != UnityWebRequest.Result.Success)
//            {
//                Debug.LogError("Error: " + www.error);
//                tempMessage.UpdateText("[Error getting response]");
//            }
//        }

//        npc.SetSpeaking(false);
//        isProcessing = false;
//    }

//    private string BuildPromptForNPC(NPCController npc, string currentInput)
//    {
//        StringBuilder prompt = new StringBuilder();

//        // Add character context
//        prompt.AppendLine($"You are {npc.npcName}. {npc.characterDescription}");
//        prompt.AppendLine();

//        // Add recent conversation history (last 10 messages)
//        if (conversationHistory.Count > 0)
//        {
//            prompt.AppendLine("Recent conversation:");
//            int startIndex = Mathf.Max(0, conversationHistory.Count - 10);
//            for (int i = startIndex; i < conversationHistory.Count; i++)
//            {
//                var msg = conversationHistory[i];
//                prompt.AppendLine($"{msg.speaker}: {msg.message}");
//            }
//            prompt.AppendLine();
//        }

//        // Add current input
//        prompt.AppendLine($"Respond to: {currentInput}");
//        prompt.AppendLine($"Your response as {npc.npcName}:");

//        return prompt.ToString();
//    }

//    private ChatMessage AddChatMessage(string speaker, string message, MessageType type)
//    {
//        if (chatMessagePrefab == null || chatContentParent == null)
//        {
//            Debug.LogWarning("Chat UI not configured!");
//            return null;
//        }

//        // Instantiate with localPositionStays = false to prevent scale/rotation issues
//        GameObject messageObj = Instantiate(chatMessagePrefab, chatContentParent, false);

//        ChatMessage chatMessage = messageObj.GetComponent<ChatMessage>();

//        if (chatMessage != null)
//        {
//            chatMessage.Initialize(speaker, message, type);
//            conversationHistory.Add(chatMessage);
//        }

//        // Auto-scroll to bottom
//        Canvas.ForceUpdateCanvases();
//        StartCoroutine(ScrollToBottom());

//        return chatMessage;
//    }

//    private void AddSystemMessage(string message)
//    {
//        AddChatMessage("System", message, MessageType.System);
//    }

//    private IEnumerator ScrollToBottom()
//    {
//        yield return new WaitForEndOfFrame();

//        ScrollRect scrollRect = chatContentParent.GetComponentInParent<ScrollRect>();
//        if (scrollRect != null)
//        {
//            scrollRect.verticalNormalizedPosition = 0f;
//        }
//    }

//    private class StreamingDownloadHandler : DownloadHandlerScript
//    {
//        private StringBuilder receivedData = new StringBuilder();
//        private int lastProcessedLength = 0;

//        public StreamingDownloadHandler() : base(new byte[1024 * 1024])
//        {
//        }

//        protected override bool ReceiveData(byte[] data, int dataLength)
//        {
//            if (data == null || dataLength == 0)
//                return false;

//            string text = Encoding.UTF8.GetString(data, 0, dataLength);
//            receivedData.Append(text);

//            return true;
//        }

//        public bool HasNewData()
//        {
//            return receivedData.Length > lastProcessedLength;
//        }

//        public string GetNewText()
//        {
//            if (!HasNewData())
//                return string.Empty;

//            string newText = receivedData.ToString(lastProcessedLength, receivedData.Length - lastProcessedLength);
//            lastProcessedLength = receivedData.Length;
//            return newText;
//        }
//    }

//    [System.Serializable]
//    private class OllamaRequest
//    {
//        public string model;
//        public string prompt;
//        public bool stream;
//    }

//    [System.Serializable]
//    private class OllamaStreamResponse
//    {
//        public string response;
//        public bool done;
//    }

//    [System.Serializable]
//    private class TagsResponse
//    {
//        public ModelInfo[] models;
//    }

//    [System.Serializable]
//    private class ModelInfo
//    {
//        public string name;
//    }
//}

//public enum MessageType
//{
//    Player,
//    NPCA,
//    NPCB,
//    System
//}
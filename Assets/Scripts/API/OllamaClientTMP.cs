using System;
using System.Collections;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using LAS;

namespace LAS
{
    public class OllamaClientTMP : MonoBehaviour
    {
        [Header("UI Elements")]
        [SerializeField] private TMP_InputField promptInputField;
        [SerializeField] private Button sendButton;
        [SerializeField] private TMP_Text responseTextField;

        [Header("Auto-detected Settings (Read-only)")]
        [SerializeField] private string detectedUrl = "";
        [SerializeField] private string detectedModel = "";

        private bool isProcessing = false;
        private const int DEFAULT_PORT = 11434;
        private string baseUrl = "";

        void Start()
        {
            if (sendButton != null)
            {
                sendButton.onClick.AddListener(OnSendButtonClicked);
            }

            if (promptInputField != null)
            {
                promptInputField.onSubmit.AddListener(delegate { OnSendButtonClicked(); });
            }

            // Auto-detect Ollama on start
            StartCoroutine(DetectOllamaSettings());
        }

        private IEnumerator DetectOllamaSettings()
        {
            // Try common localhost URLs
            string[] possibleUrls = new string[]
            {
            $"http://localhost:{DEFAULT_PORT}",
            $"http://127.0.0.1:{DEFAULT_PORT}",
            $"http://0.0.0.0:{DEFAULT_PORT}"
            };

            bool found = false;

            foreach (string url in possibleUrls)
            {
                using (UnityWebRequest www = UnityWebRequest.Get(url + "/api/tags"))
                {
                    www.timeout = 2; // Short timeout for detection
                    yield return www.SendWebRequest();

                    if (www.result == UnityWebRequest.Result.Success)
                    {
                        baseUrl = url;
                        detectedUrl = url;
                        found = true;

                        // Parse the response to get available models
                        try
                        {
                            TagsResponse tagsResponse = JsonUtility.FromJson<TagsResponse>(www.downloadHandler.text);

                            if (tagsResponse.models != null && tagsResponse.models.Length > 0)
                            {
                                // Get the first model as default
                                detectedModel = tagsResponse.models[0].name;

                                Debug.Log($"Ollama detected at {baseUrl}");
                                Debug.Log($"Using model: {detectedModel}");
                                Debug.Log($"Available models: {string.Join(", ", tagsResponse.models.Select(m => m.name))}");

                                if (responseTextField != null)
                                {
                                    responseTextField.text = $"Connected to Ollama\nModel: {detectedModel}";
                                }
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
                if (responseTextField != null)
                {
                    responseTextField.text = "Error: Ollama not detected!\n\nMake sure Ollama is running:\n'ollama serve'";
                }

                if (sendButton != null)
                {
                    sendButton.interactable = false;
                }
            }
        }

        public void OnSendButtonClicked()
        {
            if (isProcessing)
            {
                Debug.Log("Already processing a request...");
                return;
            }

            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(detectedModel))
            {
                Debug.LogError("Ollama not properly configured!");
                if (responseTextField != null)
                {
                    responseTextField.text = "Error: Ollama connection not established!";
                }
                return;
            }

            string prompt = promptInputField.text;

            if (string.IsNullOrEmpty(prompt))
            {
                Debug.LogWarning("Prompt is empty!");
                return;
            }

            StartCoroutine(SendPromptToOllamaStreaming(prompt));
        }

        private IEnumerator SendPromptToOllamaStreaming(string prompt)
        {
            isProcessing = true;

            if (sendButton != null)
            {
                sendButton.interactable = false;
            }

            if (responseTextField != null)
            {
                responseTextField.text = "";
            }

            OllamaRequest requestData = new OllamaRequest
            {
                model = detectedModel,
                prompt = prompt,
                stream = true // Enable streaming!
            };

            string jsonData = JsonUtility.ToJson(requestData);
            string url = baseUrl + "/api/generate";

            using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
                www.uploadHandler = new UploadHandlerRaw(bodyRaw);

                // Use custom download handler for streaming
                StreamingDownloadHandler downloadHandler = new StreamingDownloadHandler();
                www.downloadHandler = downloadHandler;

                www.SetRequestHeader("Content-Type", "application/json");

                // No timeout - streaming can take a while
                www.timeout = 0;

                var operation = www.SendWebRequest();

                // Poll for streaming chunks
                while (!operation.isDone)
                {
                    if (downloadHandler.HasNewData())
                    {
                        string newText = downloadHandler.GetNewText();

                        // Parse each line as a separate JSON response
                        string[] lines = newText.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

                        foreach (string line in lines)
                        {
                            try
                            {
                                OllamaStreamResponse streamResponse = JsonUtility.FromJson<OllamaStreamResponse>(line);

                                if (!string.IsNullOrEmpty(streamResponse.response))
                                {
                                    if (responseTextField != null)
                                    {
                                        responseTextField.text += streamResponse.response;
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                Debug.LogWarning("Failed to parse stream chunk: " + e.Message);
                            }
                        }
                    }

                    yield return null;
                }

                // Process any remaining data
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
                                if (responseTextField != null)
                                {
                                    responseTextField.text += streamResponse.response;
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning("Failed to parse final stream chunk: " + e.Message);
                        }
                    }
                }

                if (www.result == UnityWebRequest.Result.ConnectionError ||
                    www.result == UnityWebRequest.Result.ProtocolError)
                {
                    Debug.LogError("Error: " + www.error);
                    if (responseTextField != null)
                    {
                        responseTextField.text = "Error: " + www.error;
                    }
                }
            }

            if (sendButton != null)
            {
                sendButton.interactable = true;
            }

            isProcessing = false;
        }

        // Custom download handler for streaming responses
        private class StreamingDownloadHandler : DownloadHandlerScript
        {
            private StringBuilder receivedData = new StringBuilder();
            private int lastProcessedLength = 0;

            public StreamingDownloadHandler() : base(new byte[1024 * 1024]) // 1MB buffer
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

            protected override void CompleteContent()
            {
                base.CompleteContent();
            }
        }

        [System.Serializable]
        private class OllamaRequest
        {
            public string model;
            public string prompt;
            public bool stream;
        }

        [System.Serializable]
        private class OllamaStreamResponse
        {
            public string model;
            public string created_at;
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
            public long size;
            public string digest;
        }
    }

}

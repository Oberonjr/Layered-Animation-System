using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace LAS
{
    /// <summary>
    /// LLM provider asset for a locally-running Ollama server (https://ollama.com).
    /// Create via: Assets → Create → LAS → LLM Providers → Ollama
    /// Auto-detects the Ollama URL and enumerates available models on Connect().
    /// Set baseUrl manually to target a remote Ollama instance.
    /// </summary>
    [CreateAssetMenu(fileName = "OllamaProvider", menuName = "LAS/LLM Providers/Ollama")]
    public class OllamaProvider : LLMProviderBase
    {
        [Header("Connection")]
        [Tooltip("Auto-detected on Connect(). Override to point at a remote Ollama instance (e.g. http://192.168.1.10:11434).")]
        [SerializeField] private string baseUrl = "";

        [Header("Available Models (Runtime)")]
        [Tooltip("Populated automatically after Connect(). Read-only at runtime.")]
        [SerializeField] private List<string> availableModels = new List<string>();

        [Tooltip("Index into availableModels for the active model. Change this to switch models at runtime.")]
        [SerializeField] private int selectedModelIndex = 0;

        [Header("Action Classification Model")]
        [Tooltip("Index into availableModels for Step 2 action classification. Set to -1 to use the same model as dialogue. " +
                 "Use a larger model here if the dialogue model misclassifies actions.")]
        [SerializeField] private int actionModelIndex = -1;

        private const int DEFAULT_PORT = 11434;
        private UnityWebRequest _activeRequest;

        public override string ProviderDisplayName => "Ollama (Local)";

        public override string ActionModelDisplayName =>
            EffectiveActionModel == EffectiveModel
                ? ProviderDisplayName
                : $"Ollama (Local) [{EffectiveActionModel}]";

        /// <summary>The model used for dialogue generation.</summary>
        public string EffectiveModel =>
            (selectedModelIndex >= 0 && selectedModelIndex < availableModels.Count)
                ? availableModels[selectedModelIndex]
                : modelName;

        /// <summary>The model used for action classification. Falls back to EffectiveModel if actionModelIndex is -1 or out of range.</summary>
        public string EffectiveActionModel =>
            (actionModelIndex >= 0 && actionModelIndex < availableModels.Count)
                ? availableModels[actionModelIndex]
                : EffectiveModel;

        /// <summary>
        /// Probes common localhost addresses for a running Ollama server and fetches its model list.
        /// Sets baseUrl and modelName on success.
        /// </summary>
        public override IEnumerator Connect(Action<bool, string> onResult)
        {
            string[] candidates =
            {
                $"http://localhost:{DEFAULT_PORT}",
                $"http://127.0.0.1:{DEFAULT_PORT}"
            };

            foreach (string url in candidates)
            {
                using var www = UnityWebRequest.Get(url + "/api/tags");
                www.timeout = 2;
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                    continue;

                baseUrl = url;

                try
                {
                    var tags = JsonUtility.FromJson<OllamaTagsResponse>(www.downloadHandler.text);
                    if (tags?.models != null && tags.models.Length > 0)
                    {
                        availableModels.Clear();
                        foreach (var m in tags.models)
                            availableModels.Add(m.name);

                        selectedModelIndex = 0;
                        modelName = availableModels[0];
                        IsConnected = true;
                        onResult?.Invoke(true, $"Connected to Ollama — using {modelName}");
                        yield break;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[OllamaProvider] Failed to parse model list: {e.Message}");
                }
            }

            IsConnected = false;
            onResult?.Invoke(false, "Ollama not detected. Run 'ollama serve'.");
        }

        public override IEnumerator FetchModels(Action<string[]> onResult)
        {
            onResult?.Invoke(availableModels.Count > 0 ? availableModels.ToArray() : new[] { modelName });
            yield break;
        }

        public override IEnumerator SendRequest(LLMRequest request, LLMGenerationOptions options, Action<string> onComplete)
        {
            return SendWithModel(EffectiveModel, request, options, onComplete);
        }

        public override IEnumerator SendActionRequest(LLMRequest request, LLMGenerationOptions options, Action<string> onComplete)
        {
            return SendWithModel(EffectiveActionModel, request, options, onComplete);
        }

        /// <summary>
        /// Streams a chat request to Ollama's /api/chat endpoint using the given model.
        /// Uses the structured message format so the local model's KV cache can reuse
        /// system prompt tokens across turns. Both SendRequest and SendActionRequest delegate here.
        /// </summary>
        private IEnumerator SendWithModel(string model, LLMRequest request, LLMGenerationOptions options, Action<string> onComplete)
        {
            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(model))
            {
                Debug.LogError("[OllamaProvider] Not configured. Call Connect() first.");
                yield break;
            }

            var requestData = new OllamaChatRequest
            {
                model    = model,
                messages = BuildMessages(request),
                stream   = true,
                options  = new OllamaOptions
                {
                    temperature    = options.temperature,
                    top_p          = options.topP,
                    top_k          = options.topK,
                    num_predict    = options.maxTokens,
                    repeat_penalty = options.repeatPenalty
                }
            };

            string json = JsonUtility.ToJson(requestData);
            string url  = baseUrl + "/api/chat";

            var www = new UnityWebRequest(url, "POST");
            _activeRequest = www;
            www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            var streamHandler = new StreamingDownloadHandler();
            www.downloadHandler = streamHandler;
            www.SetRequestHeader("Content-Type", "application/json");
            www.timeout = 0;

            var op = www.SendWebRequest();
            var sb = new StringBuilder();

            while (!op.isDone)
            {
                if (streamHandler.HasNewData())
                    ParseChunks(streamHandler.GetNewText(), sb);
                yield return null;
            }

            if (streamHandler.HasNewData())
                ParseChunks(streamHandler.GetNewText(), sb);

            bool   success    = www.result == UnityWebRequest.Result.Success;
            bool   wasAborted = www.result == UnityWebRequest.Result.ConnectionError;
            string error      = www.error;
            www.Dispose();
            _activeRequest = null;

            if (!success)
            {
                if (wasAborted && error != null && error.Contains("aborted"))
                    Debug.LogWarning("[OllamaProvider] Request interrupted by player.");
                else
                    Debug.LogError($"[OllamaProvider] Request failed: {error}");
                yield break;
            }

            onComplete?.Invoke(sb.ToString());
        }

        /// <summary>Aborts any in-progress request. Called by NPCManager on player interruption.</summary>
        public override void Interrupt()
        {
            _activeRequest?.Abort();
            _activeRequest = null;
        }

        /// <summary>
        /// Builds the Ollama message array from an LLMRequest.
        /// System content → role "system". History passes through. User content → role "user".
        /// </summary>
        private static OllamaMessage[] BuildMessages(LLMRequest request)
        {
            var messages = new List<OllamaMessage>();

            if (!string.IsNullOrEmpty(request.systemContent))
                messages.Add(new OllamaMessage { role = "system", content = request.systemContent });

            if (request.history != null)
                foreach (var msg in request.history)
                    messages.Add(new OllamaMessage { role = msg.role, content = msg.content });

            if (!string.IsNullOrEmpty(request.userContent))
                messages.Add(new OllamaMessage { role = "user", content = request.userContent });

            return messages.ToArray();
        }

        private static void ParseChunks(string raw, StringBuilder sb)
        {
            foreach (string line in raw.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var sr = JsonUtility.FromJson<OllamaChatStreamResponse>(line);
                    if (!string.IsNullOrEmpty(sr?.message?.content))
                        sb.Append(sr.message.content);
                }
                catch { }
            }
        }

        // ── Ollama-specific wire types (private to this provider) ────────────────

        [Serializable]
        private class OllamaChatRequest
        {
            public string          model;
            public OllamaMessage[] messages;
            public bool            stream;
            public OllamaOptions   options;
        }

        [Serializable]
        private class OllamaMessage
        {
            public string role;
            public string content;
        }

        [Serializable]
        private class OllamaOptions
        {
            public float temperature;
            public float top_p;
            public float top_k;
            public int   num_predict;
            public float repeat_penalty;
        }

        [Serializable]
        private class OllamaChatStreamResponse
        {
            public OllamaMessage message;
            public bool          done;
        }

        [Serializable]
        private class OllamaTagsResponse
        {
            public OllamaModelInfo[] models;
        }

        [Serializable]
        private class OllamaModelInfo
        {
            public string name;
            public long   size;
        }
    }
}

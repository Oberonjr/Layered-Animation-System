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

        private const int DEFAULT_PORT = 11434;
        private UnityWebRequest _activeRequest;

        public override string ProviderDisplayName => "Ollama (Local)";

        /// <summary>The model that will be used for generation requests.</summary>
        public string EffectiveModel =>
            (selectedModelIndex >= 0 && selectedModelIndex < availableModels.Count)
                ? availableModels[selectedModelIndex]
                : modelName;

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

        /// <summary>
        /// Streams a generation request to Ollama's /api/generate endpoint.
        /// Accumulates all response tokens and calls onComplete with the full string.
        /// If Interrupt() is called mid-stream the request is aborted and onComplete is not called.
        /// </summary>
        public override IEnumerator SendRequest(string prompt, LLMGenerationOptions options, Action<string> onComplete)
        {
            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(EffectiveModel))
            {
                Debug.LogError("[OllamaProvider] Not configured. Call Connect() first.");
                yield break;
            }

            var requestData = new OllamaRequest
            {
                model = EffectiveModel,
                prompt = prompt,
                stream = true,
                options = new OllamaOptions
                {
                    temperature  = options.temperature,
                    top_p        = options.topP,
                    top_k        = options.topK,
                    num_predict  = options.maxTokens,
                    repeat_penalty = options.repeatPenalty
                }
            };

            string json = JsonUtility.ToJson(requestData);
            string url  = baseUrl + "/api/generate";

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

            // Flush any remaining buffered data.
            if (streamHandler.HasNewData())
                ParseChunks(streamHandler.GetNewText(), sb);

            bool success = www.result == UnityWebRequest.Result.Success;
            string error = www.error;
            www.Dispose();
            _activeRequest = null;

            if (!success)
            {
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

        private static void ParseChunks(string raw, StringBuilder sb)
        {
            foreach (string line in raw.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var sr = JsonUtility.FromJson<OllamaStreamResponse>(line);
                    if (!string.IsNullOrEmpty(sr?.response))
                        sb.Append(sr.response);
                }
                catch { }
            }
        }

        // ── Ollama-specific wire types (private to this provider) ────────────────

        [Serializable]
        private class OllamaRequest
        {
            public string model;
            public string prompt;
            public bool   stream;
            public OllamaOptions options;
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
        private class OllamaStreamResponse
        {
            public string response;
            public bool   done;
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

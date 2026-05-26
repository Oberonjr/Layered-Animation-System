using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace LAS
{
    /// <summary>
    /// LLM provider asset for any OpenAI Chat Completions-compatible API.
    /// Create via: Assets → Create → LAS → LLM Providers → OpenAI Compatible
    /// Covers OpenAI (ChatGPT), DeepSeek, Mistral, and any provider that uses the
    /// POST /chat/completions endpoint with the OpenAI request/response schema.
    ///
    /// Select a preset to auto-fill the base URL, or choose Custom and enter your own.
    /// Leave apiKey empty to fall back to the OPENAI_API_KEY environment variable.
    /// </summary>
    [CreateAssetMenu(fileName = "OpenAIChatProvider", menuName = "LAS/LLM Providers/OpenAI Compatible")]
    public class OpenAIChatProvider : LLMProviderBase
    {
        // ── Preset base URLs ─────────────────────────────────────────────────────

        public enum ProviderPreset
        {
            OpenAI,     // https://api.openai.com/v1  — GPT-4o, GPT-4, GPT-3.5-Turbo, etc.
            DeepSeek,   // https://api.deepseek.com/v1
            Mistral,    // https://api.mistral.ai/v1
            Custom      // User-defined base URL
        }

        // ── Inspector fields ─────────────────────────────────────────────────────

        [Header("Provider")]
        [Tooltip("Select a preset to auto-fill the base URL, or choose Custom.")]
        [SerializeField] private ProviderPreset preset = ProviderPreset.OpenAI;

        [Tooltip("Used when Preset = Custom. Include the version path, e.g. https://my-llm.example.com/v1")]
        [SerializeField] private string customBaseUrl = "";

        [Header("Authentication")]
        [Tooltip("The key name to look up in ~/.las/api_keys.txt (e.g. OPENAI_API_KEY, DEEPSEEK_API_KEY). " +
                 "The actual key is read from that external file — it is never stored in this asset. " +
                 "Use the buttons below to open or locate the key file.")]
        [SerializeField] private string apiKeyName = "OPENAI_API_KEY";

        // ── Internal state ────────────────────────────────────────────────────────

        private UnityWebRequest _activeRequest;

        // ── Properties ────────────────────────────────────────────────────────────

        public override string ProviderDisplayName => $"{preset} ({modelName})";

        private string EffectiveBaseUrl => preset switch
        {
            ProviderPreset.OpenAI   => "https://api.openai.com/v1",
            ProviderPreset.DeepSeek => "https://api.deepseek.com/v1",
            ProviderPreset.Mistral  => "https://api.mistral.ai/v1",
            _                       => customBaseUrl
        };

        private string EffectiveApiKey => ApiKeyStore.GetKey(apiKeyName);

        // ── LLMProviderBase implementation ────────────────────────────────────────

        /// <summary>
        /// Verifies that an API key and base URL are configured, then tests connectivity
        /// by listing available models (GET /models). Sets IsConnected accordingly.
        /// </summary>
        public override IEnumerator Connect(Action<bool, string> onResult)
        {
            if (string.IsNullOrEmpty(EffectiveApiKey))
            {
                IsConnected = false;
                onResult?.Invoke(false, $"{preset}: API key not set. Add it to the Inspector or the OPENAI_API_KEY env var.");
                yield break;
            }

            if (string.IsNullOrEmpty(EffectiveBaseUrl))
            {
                IsConnected = false;
                onResult?.Invoke(false, $"{preset}: base URL not configured.");
                yield break;
            }

            using var www = UnityWebRequest.Get(EffectiveBaseUrl + "/models");
            www.SetRequestHeader("Authorization", $"Bearer {EffectiveApiKey}");
            www.timeout = 10;
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                IsConnected = true;
                onResult?.Invoke(true, $"Connected to {preset} — using {modelName}");
            }
            else
            {
                IsConnected = false;
                onResult?.Invoke(false, $"{preset} connection failed: {www.error}");
            }
        }

        /// <summary>
        /// Posts the structured request to the chat/completions endpoint with streaming.
        /// Maps LLMRequest directly to the OpenAI messages array: system → history → user.
        /// Calls onComplete with the accumulated string; skips the call on failure or interrupt.
        /// </summary>
        public override IEnumerator SendRequest(LLMRequest request, LLMGenerationOptions options, Action<string> onComplete)
        {
            if (string.IsNullOrEmpty(EffectiveApiKey) || string.IsNullOrEmpty(EffectiveBaseUrl))
            {
                Debug.LogError("[OpenAIChatProvider] Not configured. Check API key and base URL.");
                yield break;
            }

            if (string.IsNullOrEmpty(modelName))
            {
                Debug.LogError("[OpenAIChatProvider] Model name is empty. Set it on the provider asset (e.g. 'gpt-4o', 'deepseek-chat', 'mistral-medium').");
                yield break;
            }

            var requestBody = new ChatCompletionRequest
            {
                model       = modelName,
                messages    = BuildMessages(request),
                temperature = options.temperature,
                max_tokens  = options.maxTokens,
                top_p       = options.topP,
                // Map repeatPenalty (Ollama 1.0–2.0 scale) to frequency_penalty (OpenAI 0–2 scale).
                frequency_penalty = Mathf.Max(0f, options.repeatPenalty - 1.0f),
                stream      = true
            };

            string json = JsonUtility.ToJson(requestBody);
            string url  = EffectiveBaseUrl + "/chat/completions";

            var www = new UnityWebRequest(url, "POST");
            _activeRequest = www;
            www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            var streamHandler = new StreamingDownloadHandler();
            www.downloadHandler = streamHandler;
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", $"Bearer {EffectiveApiKey}");
            www.timeout = 0;

            var op = www.SendWebRequest();
            var sb    = new StringBuilder(); // parsed token content
            var raw   = new StringBuilder(); // full raw body (for error reporting)

            while (!op.isDone)
            {
                if (streamHandler.HasNewData())
                {
                    string chunk = streamHandler.GetNewText();
                    raw.Append(chunk);
                    ParseSSE(chunk, sb);
                }
                yield return null;
            }

            if (streamHandler.HasNewData())
            {
                string chunk = streamHandler.GetNewText();
                raw.Append(chunk);
                ParseSSE(chunk, sb);
            }

            bool   success = www.result == UnityWebRequest.Result.Success;
            string error   = www.error;
            long   code    = www.responseCode;
            www.Dispose();
            _activeRequest = null;

            if (!success)
            {
                bool wasAborted = www.result == UnityWebRequest.Result.ConnectionError
                                  && error != null && error.Contains("aborted");
                if (wasAborted)
                {
                    Debug.LogWarning("[OpenAIChatProvider] Request interrupted by player.");
                }
                else
                {
                    string body = raw.ToString().Trim();
                    Debug.LogError(
                        $"[OpenAIChatProvider] Request failed — HTTP {code} ({error})\n" +
                        $"  Provider : {preset}  Model: '{modelName}'\n" +
                        $"  URL      : {url}\n" +
                        (string.IsNullOrEmpty(body) ? "" : $"  Response : {body}"));
                }
                yield break;
            }

            onComplete?.Invoke(sb.ToString());
        }

        // ── Message construction ──────────────────────────────────────────────────

        /// <summary>
        /// Maps an LLMRequest to the OpenAI messages array.
        /// System content → role "system". History messages pass through as-is.
        /// User content → role "user" (always the final message).
        /// </summary>
        private static ChatMessage[] BuildMessages(LLMRequest request)
        {
            var messages = new List<ChatMessage>();

            if (!string.IsNullOrEmpty(request.systemContent))
                messages.Add(new ChatMessage { role = "system", content = request.systemContent });

            if (request.history != null)
                foreach (var msg in request.history)
                    messages.Add(new ChatMessage { role = msg.role, content = msg.content });

            if (!string.IsNullOrEmpty(request.userContent))
                messages.Add(new ChatMessage { role = "user", content = request.userContent });

            return messages.ToArray();
        }

        /// <summary>Aborts any in-progress request. Called by NPCManager on player interruption.</summary>
        public override void Interrupt()
        {
            _activeRequest?.Abort();
            _activeRequest = null;
        }

        // ── SSE parsing ───────────────────────────────────────────────────────────

        /// <summary>
        /// Parses OpenAI-style Server-Sent Events (SSE) data lines and appends token content to sb.
        /// Format per line: "data: {json}" or "data: [DONE]"
        /// </summary>
        private static void ParseSSE(string raw, StringBuilder sb)
        {
            foreach (string line in raw.Split('\n'))
            {
                if (!line.StartsWith("data: ")) continue;

                string data = line.Substring(6).Trim();
                if (data == "[DONE]") break;

                try
                {
                    var chunk = JsonUtility.FromJson<StreamChunk>(data);
                    if (chunk?.choices != null && chunk.choices.Length > 0)
                    {
                        string content = chunk.choices[0]?.delta?.content;
                        if (!string.IsNullOrEmpty(content))
                            sb.Append(content);
                    }
                }
                catch { }
            }
        }

        // ── OpenAI API wire types (private to this provider) ─────────────────────

        [Serializable]
        private class ChatCompletionRequest
        {
            public string        model;
            public ChatMessage[] messages;
            public float         temperature;
            public int           max_tokens;
            public float         top_p;
            public float         frequency_penalty;
            public bool          stream;
        }

        [Serializable]
        private class ChatMessage
        {
            public string role;
            public string content;
        }

        [Serializable]
        private class StreamChunk
        {
            public string         id;
            public StreamChoice[] choices;
        }

        [Serializable]
        private class StreamChoice
        {
            public StreamDelta delta;
            public int         index;
        }

        [Serializable]
        private class StreamDelta
        {
            public string content;
            public string role;
        }
    }
}

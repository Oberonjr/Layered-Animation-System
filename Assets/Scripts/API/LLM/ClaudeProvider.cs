using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace LAS
{
    /// <summary>
    /// LLM provider asset for Anthropic Claude models.
    /// Create via: Assets → Create → LAS → LLM Providers → Claude (Anthropic)
    ///
    /// Anthropic's API differs from the OpenAI schema in three ways:
    ///   1. Auth uses the "x-api-key" header (not "Authorization: Bearer").
    ///   2. The system prompt is a top-level field, not a message with role "system".
    ///   3. Streaming uses "content_block_delta" SSE events rather than "choices[].delta".
    ///
    /// Leave apiKey empty to fall back to the ANTHROPIC_API_KEY environment variable.
    ///
    /// Recommended models (set on the asset):
    ///   claude-opus-4-5          — most capable
    ///   claude-sonnet-4-5        — balanced capability / cost
    ///   claude-haiku-4-5-20251001 — fastest / cheapest
    /// </summary>
    [CreateAssetMenu(fileName = "ClaudeProvider", menuName = "LAS/LLM Providers/Claude (Anthropic)")]
    public class ClaudeProvider : LLMProviderBase
    {
        [Header("Authentication")]
        [Tooltip("The key name to look up in ~/.las/api_keys.txt (e.g. ANTHROPIC_API_KEY). " +
                 "The actual key is read from that external file — it is never stored in this asset. " +
                 "Use the buttons below to open or locate the key file.")]
        [SerializeField] private string apiKeyName = "ANTHROPIC_API_KEY";

        [Header("Action Classification Model")]
        [Tooltip("Model used for Step 2 action classification. Leave empty to use the dialogue model above. " +
                 "Use a more capable model here (e.g. 'claude-opus-4-7') if a cheaper dialogue model misclassifies actions.")]
        [SerializeField] private string actionModelName = "";

        private const string BASE_URL    = "https://api.anthropic.com/v1";
        private const string API_VERSION = "2023-06-01";

        private UnityWebRequest _activeRequest;

        public override string ProviderDisplayName => $"Claude ({modelName})";

        public override string ActionModelDisplayName =>
            string.IsNullOrWhiteSpace(actionModelName)
                ? ProviderDisplayName
                : $"Claude ({actionModelName})";

        private string EffectiveApiKey => ApiKeyStore.GetKey(apiKeyName);

        // ── LLMProviderBase implementation ────────────────────────────────────────

        /// <summary>
        /// Validates the API key and tests connectivity via the /v1/models endpoint.
        /// </summary>
        public override IEnumerator Connect(Action<bool, string> onResult)
        {
            if (string.IsNullOrEmpty(EffectiveApiKey))
            {
                IsConnected = false;
                onResult?.Invoke(false,
                    $"Claude: API key '{apiKeyName}' not found.\n" +
                    $"Open  {ApiKeyStore.KeyFilePath}  and fill in the key.");
                yield break;
            }

            using var www = UnityWebRequest.Get(BASE_URL + "/models");
            www.SetRequestHeader("x-api-key",         EffectiveApiKey);
            www.SetRequestHeader("anthropic-version",  API_VERSION);
            www.timeout = 10;
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                IsConnected = true;
                onResult?.Invoke(true, $"Connected to Anthropic — using {modelName}");
            }
            else
            {
                IsConnected = false;
                onResult?.Invoke(false, $"Claude connection failed: {www.error}");
            }
        }

        public override IEnumerator SendRequest(LLMRequest request, LLMGenerationOptions options, Action<string> onComplete)
        {
            return SendWithModel(modelName, request, options, onComplete);
        }

        public override IEnumerator SendActionRequest(LLMRequest request, LLMGenerationOptions options, Action<string> onComplete)
        {
            string model = string.IsNullOrWhiteSpace(actionModelName) ? modelName : actionModelName;
            return SendWithModel(model, request, options, onComplete);
        }

        /// <summary>
        /// Sends a structured request to /v1/messages with streaming using the given model.
        /// Both SendRequest and SendActionRequest delegate here.
        /// </summary>
        private IEnumerator SendWithModel(string model, LLMRequest request, LLMGenerationOptions options, Action<string> onComplete)
        {
            if (string.IsNullOrEmpty(EffectiveApiKey))
            {
                Debug.LogError("[ClaudeProvider] API key not set.");
                yield break;
            }

            if (string.IsNullOrEmpty(model))
            {
                Debug.LogError("[ClaudeProvider] Model name is empty. Set it on the provider asset (e.g. 'claude-sonnet-4-6').");
                yield break;
            }

            var requestBody = new AnthropicRequest
            {
                model       = model,
                max_tokens  = options.maxTokens,
                temperature = options.temperature,
                system      = request.systemContent ?? "",
                messages    = BuildMessages(request),
                stream      = true
            };

            string json = JsonUtility.ToJson(requestBody);
            string url  = BASE_URL + "/messages";

            var www = new UnityWebRequest(url, "POST");
            _activeRequest = www;
            www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            var streamHandler = new StreamingDownloadHandler();
            www.downloadHandler = streamHandler;
            www.SetRequestHeader("Content-Type",      "application/json");
            www.SetRequestHeader("x-api-key",         EffectiveApiKey);
            www.SetRequestHeader("anthropic-version",  API_VERSION);
            www.timeout = 0;

            var op  = www.SendWebRequest();
            var sb  = new StringBuilder();
            var raw = new StringBuilder();

            while (!op.isDone)
            {
                if (streamHandler.HasNewData())
                {
                    string chunk = streamHandler.GetNewText();
                    raw.Append(chunk);
                    ParseAnthropicSSE(chunk, sb);
                }
                yield return null;
            }

            if (streamHandler.HasNewData())
            {
                string chunk = streamHandler.GetNewText();
                raw.Append(chunk);
                ParseAnthropicSSE(chunk, sb);
            }

            bool   success = www.result == UnityWebRequest.Result.Success;
            string error   = www.error;
            long   code    = www.responseCode;
            www.Dispose();
            _activeRequest = null;

            if (!success)
            {
                string body = raw.ToString().Trim();
                Debug.LogError(
                    $"[ClaudeProvider] Request failed — HTTP {code} ({error})\n" +
                    $"  Model: '{model}'\n" +
                    (string.IsNullOrEmpty(body) ? "" : $"  Response: {body}"));
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

        // ── Message construction ──────────────────────────────────────────────────

        /// <summary>
        /// Maps LLMRequest history + userContent into Anthropic's messages array.
        /// systemContent is sent as the top-level "system" field, not as a message.
        /// </summary>
        private static AnthropicMessage[] BuildMessages(LLMRequest request)
        {
            var messages = new List<AnthropicMessage>();

            if (request.history != null)
                foreach (var msg in request.history)
                    messages.Add(new AnthropicMessage { role = msg.role, content = msg.content });

            if (!string.IsNullOrEmpty(request.userContent))
                messages.Add(new AnthropicMessage { role = "user", content = request.userContent });

            return messages.ToArray();
        }

        // ── Anthropic SSE parsing ─────────────────────────────────────────────────

        /// <summary>
        /// Parses Anthropic's SSE stream format.
        /// Only "content_block_delta" events with type "text_delta" carry token text.
        /// All other event types (message_start, content_block_start, etc.) are ignored.
        /// </summary>
        private static void ParseAnthropicSSE(string raw, StringBuilder sb)
        {
            foreach (string line in raw.Split('\n'))
            {
                if (!line.StartsWith("data: ")) continue;

                string data = line.Substring(6).Trim();
                if (string.IsNullOrEmpty(data) || data == "[DONE]") continue;

                try
                {
                    var chunk = JsonUtility.FromJson<StreamDelta>(data);
                    if (chunk?.type == "content_block_delta" &&
                        chunk.delta?.type == "text_delta" &&
                        !string.IsNullOrEmpty(chunk.delta.text))
                    {
                        sb.Append(chunk.delta.text);
                    }
                }
                catch { }
            }
        }

        // ── Anthropic API wire types (private to this provider) ──────────────────

        [Serializable]
        private class AnthropicRequest
        {
            public string            model;
            public int               max_tokens;
            public float             temperature;
            public string            system;
            public AnthropicMessage[] messages;
            public bool              stream;
        }

        [Serializable]
        private class AnthropicMessage
        {
            public string role;
            public string content;
        }

        /// <summary>
        /// Partial representation of a streaming SSE data payload.
        /// Only the fields needed for text extraction are mapped.
        /// </summary>
        [Serializable]
        private class StreamDelta
        {
            public string      type;  // e.g. "content_block_delta", "message_start", ...
            public InnerDelta  delta;
        }

        [Serializable]
        private class InnerDelta
        {
            public string type; // "text_delta"
            public string text;
        }
    }
}

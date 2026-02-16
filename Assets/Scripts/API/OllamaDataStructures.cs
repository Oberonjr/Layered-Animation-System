using System;

/// <summary>
/// Data structures for Ollama API communication
/// </summary>

[Serializable]
public class OllamaRequest
{
    public string model;
    public string prompt;
    public bool stream;
    public OllamaOptions options;
}

[Serializable]
public class OllamaOptions
{
    public float temperature = 0.7f;
    public float top_p = 0.9f;
    public float top_k = 40f;
    public int num_predict = -1; // -1 = unlimited
    public float repeat_penalty = 1.1f;
}

[Serializable]
public class OllamaStreamResponse
{
    public string model;
    public string created_at;
    public string response;
    public bool done;
    public long total_duration;
    public int prompt_eval_count;
    public int eval_count;
}

[Serializable]
public class OllamaTagsResponse
{
    public OllamaModelInfo[] models;
}

[Serializable]
public class OllamaModelInfo
{
    public string name;
    public long size;
    public string digest;
}

[Serializable]
public class NPCResponse
{
    public int npc_index; // Index instead of character_id
    public string dialogue;
    public string action;
    public string internal_thought;
}


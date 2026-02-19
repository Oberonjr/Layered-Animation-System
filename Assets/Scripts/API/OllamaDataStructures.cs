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
    public int npc_index;
    public string dialogue;
    public string action_key;              // e.g. "PICK_UP", "GO_TO", "NONE"
    public string action_target;           // primary target name from registry
    public string action_secondary_target; // for two-party actions
    public string internal_thought;
}

public enum MessageType
{
    Player,
    NPC,
    System
}

/// <summary>
/// Serializable UnityEvent for NPC action execution.
/// Passes: acting NPC behaviour, primary target, secondary target.
/// Used as the VALUE in NPCActionDispatcher's dictionary.
/// </summary>
[System.Serializable]
public class NPCActionCallback : UnityEngine.Events.UnityEvent<NPCBehaviourController, UnityEngine.Transform, UnityEngine.Transform> { }
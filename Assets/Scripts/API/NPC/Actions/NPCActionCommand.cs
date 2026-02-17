using System;

/// <summary>
/// Parsed action payload from an LLM response.
/// Produced by NPCManager, consumed by NPCActionDispatcher.
/// </summary>
[Serializable]
public class NPCActionCommand
{
    public int npc_index;
    public string action_key;
    public string action_target;
    public string action_secondary_target;
}

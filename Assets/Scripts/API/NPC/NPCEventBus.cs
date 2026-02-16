using System;
using System.Collections.Generic;

/// <summary>
/// Global event bus for NPC system communication
/// </summary>
public static class NPCEventBus
{
    // NPC Registration Events
    public static event Action<NPCController> OnNPCRegistered;
    public static event Action<NPCController> OnNPCUnregistered;
    
    // Conversation Events
    public static event Action<int, string> OnNPCStartedSpeaking; // NPC index, dialogue
    public static event Action<int> OnNPCFinishedSpeaking; // NPC index
    
    // Player Events
    public static event Action<string> OnPlayerMessage;
    
    public static void RegisterNPC(NPCController npc)
    {
        OnNPCRegistered?.Invoke(npc);
    }
    
    public static void UnregisterNPC(NPCController npc)
    {
        OnNPCUnregistered?.Invoke(npc);
    }
    
    public static void BroadcastNPCStartedSpeaking(int npcIndex, string dialogue)
    {
        OnNPCStartedSpeaking?.Invoke(npcIndex, dialogue);
    }
    
    public static void BroadcastNPCFinishedSpeaking(int npcIndex)
    {
        OnNPCFinishedSpeaking?.Invoke(npcIndex);
    }
    
    public static void BroadcastPlayerMessage(string message)
    {
        OnPlayerMessage?.Invoke(message);
    }
}

using System;
using System.Collections.Generic;
using LAS;

namespace LAS
{
    /// <summary>
    /// Global event bus for NPC system communication.
    /// Provides a central hub for NPC-related events without tight coupling between components.
    /// Use this for broadcasting NPC lifecycle, conversation, and player interaction events.
    /// </summary>
    public static class NPCEventBus
    {
        // NPC Registration Events
        /// <summary>Fired when an NPCController registers itself at Start(). Parameters: (NPCController instance).</summary>
        public static event Action<NPCController> OnNPCRegistered;

        /// <summary>Fired when an NPCController unregisters itself at OnDestroy(). Parameters: (NPCController instance).</summary>
        public static event Action<NPCController> OnNPCUnregistered;

        // Conversation Events
        /// <summary>Fired when an NPC starts speaking dialogue. Parameters: (NPC index, dialogue text).</summary>
        public static event Action<int, string> OnNPCStartedSpeaking; // NPC index, dialogue

        /// <summary>Fired when an NPC finishes speaking dialogue. Parameters: (NPC index).</summary>
        public static event Action<int> OnNPCFinishedSpeaking; // NPC index

        // Player Events
        /// <summary>Fired when the player sends a message. Parameters: (message text).</summary>
        public static event Action<string> OnPlayerMessage;

        /// <summary>
        /// Triggers the OnNPCRegistered event.
        /// Called by NPCController.Start() to notify NPCManager and other systems.
        /// </summary>
        /// <param name="npc">The NPCController being registered.</param>
        public static void RegisterNPC(NPCController npc)
        {
            OnNPCRegistered?.Invoke(npc);
        }

        /// <summary>
        /// Triggers the OnNPCUnregistered event.
        /// Called by NPCController.OnDestroy() to notify systems of NPC removal.
        /// </summary>
        /// <param name="npc">The NPCController being unregistered.</param>
        public static void UnregisterNPC(NPCController npc)
        {
            OnNPCUnregistered?.Invoke(npc);
        }

        /// <summary>
        /// Broadcasts that an NPC has started speaking.
        /// Used to update UI and trigger visual feedback (like NPC highlighting).
        /// </summary>
        /// <param name="npcIndex">The index of the NPC in NPCManager's registered list.</param>
        /// <param name="dialogue">The dialogue text being spoken.</param>
        public static void BroadcastNPCStartedSpeaking(int npcIndex, string dialogue)
        {
            OnNPCStartedSpeaking?.Invoke(npcIndex, dialogue);
        }

        /// <summary>
        /// Broadcasts that an NPC has finished speaking.
        /// Used to update UI and clear visual feedback.
        /// </summary>
        /// <param name="npcIndex">The index of the NPC in NPCManager's registered list.</param>
        public static void BroadcastNPCFinishedSpeaking(int npcIndex)
        {
            OnNPCFinishedSpeaking?.Invoke(npcIndex);
        }

        /// <summary>
        /// Broadcasts that the player has sent a message.
        /// Can be used for additional processing or analytics.
        /// </summary>
        /// <param name="message">The player's message text.</param>
        public static void BroadcastPlayerMessage(string message)
        {
            OnPlayerMessage?.Invoke(message);
        }
    }

}

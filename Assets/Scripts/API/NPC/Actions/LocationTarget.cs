using UnityEngine;
using System.Collections.Generic;
using AYellowpaper.SerializedCollections;
using LAS;

namespace LAS
{
    /// <summary>
    /// Represents a spatial location in the scene where agents can go or items can be placed.
    /// Tracks occupants and free items for LLM context.
    /// Supports named ItemSlots that NPCs can place specific items into via PUT_DOWN.
    /// </summary>
    public class LocationTarget : LAS.ActionTarget
    {
        [Header("Location State")]
        public List<string> occupants  = new List<string>();
        public List<string> itemsHere  = new List<string>();

        [Header("Item Slots")]
        [Tooltip("Maps each accepted InteractableItem to its ItemSlot component. " +
                 "Use the 'Populate Item Slots' button in the Inspector to populate keys from scene items, " +
                 "then drag the corresponding ItemSlot component into each value field.")]
        [SerializedDictionary("Accepted Item", "Slot Component")]
        public SerializedDictionary<InteractableItem, ItemSlot> itemSlots
            = new SerializedDictionary<InteractableItem, ItemSlot>();

        public override TargetType Type => TargetType.Location;

        // ── Slot queries ──────────────────────────────────────────────────────────

        /// <summary>Returns the ItemSlot for the given item, or null if this location has no slot for it.</summary>
        public ItemSlot GetSlotForItem(InteractableItem item)
        {
            if (item == null) return null;
            itemSlots.TryGetValue(item, out var slot);
            return slot;
        }

        /// <summary>Returns true if this location has a configured (non-null) slot for the given item.</summary>
        public bool HasSlotForItem(InteractableItem item) =>
            item != null && itemSlots.TryGetValue(item, out var s) && s != null;

        // ── LLM state description ─────────────────────────────────────────────────

        public string GetStateDescription()
        {
            var parts = new List<string>();
            if (occupants.Count > 0)  parts.Add($"Occupants: {string.Join(", ", occupants)}");
            if (itemsHere.Count > 0)  parts.Add($"Items: {string.Join(", ", itemsHere)}");

            // Describe configured slots so the LLM knows what can be placed here
            var slotDescriptions = new List<string>();
            foreach (var kvp in itemSlots)
            {
                if (kvp.Key == null || kvp.Value == null) continue;
                string status = kvp.Value.IsOccupied ? $"occupied by {kvp.Key.TargetName}" : "empty";
                slotDescriptions.Add($"{kvp.Key.TargetName}({status})");
            }
            if (slotDescriptions.Count > 0)
                parts.Add($"Slots: {string.Join(", ", slotDescriptions)}");

            return parts.Count > 0 ? string.Join("; ", parts) : "empty";
        }

        // ── Occupant / item tracking ──────────────────────────────────────────────

        public void AddOccupant(string occupantName)
        {
            if (!occupants.Contains(occupantName)) occupants.Add(occupantName);
        }

        public void RemoveOccupant(string occupantName) => occupants.Remove(occupantName);

        public void AddItem(string itemName)
        {
            if (!itemsHere.Contains(itemName)) itemsHere.Add(itemName);
        }

        public void RemoveItem(string itemName) => itemsHere.Remove(itemName);
    }
}

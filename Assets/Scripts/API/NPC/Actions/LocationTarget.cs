using UnityEngine;
using System.Collections.Generic;
using LAS;

namespace LAS
{
    /// <summary>
    /// Represents a spatial location in the scene where agents can go or items can be set down.
    /// Tracks what is currently here to provide context for the LLM.
    /// </summary>
    public class LocationTarget : LAS.ActionTarget
    {
        [Header("Location State")]
        public List<string> occupants = new List<string>();
        public List<string> itemsHere = new List<string>();

        void Awake()
        {
            targetType = TargetType.Location;
        }

        public string GetStateDescription()
        {
            if (occupants.Count == 0 && itemsHere.Count == 0)
                return "empty";

            List<string> parts = new List<string>();
            if (occupants.Count > 0) parts.Add($"Occupants: {string.Join(", ", occupants)}");
            if (itemsHere.Count > 0) parts.Add($"Items: {string.Join(", ", itemsHere)}");
            return string.Join("; ", parts);
        }
        
        public void AddOccupant(string occupantName)
        {
            if (!occupants.Contains(occupantName)) occupants.Add(occupantName);
        }
        
        public void RemoveOccupant(string occupantName)
        {
            occupants.Remove(occupantName);
        }
        
        public void AddItem(string itemName)
        {
            if (!itemsHere.Contains(itemName)) itemsHere.Add(itemName);
        }
        
        public void RemoveItem(string itemName)
        {
            itemsHere.Remove(itemName);
        }
    }
}

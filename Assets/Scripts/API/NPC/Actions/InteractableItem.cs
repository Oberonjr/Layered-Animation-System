using UnityEngine;
using LAS;

namespace LAS
{
    /// <summary>
    /// Represents an object in the scene that can be picked up, manipulated, or passed around.
    /// Tracks its current state (held by someone, free, or placed) to provide context for the LLM.
    /// </summary>
    public class InteractableItem : ActionTarget
    {
        [Header("Item State")]
        public bool isHeld = false;
        public string heldByNPC = "";
        public string currentLocation = "";

        void Awake()
        {
            targetType = TargetType.InteractableObject;
            
            // Optionally, try to determine starting location if sitting on something
            if (transform.parent != null)
            {
                var loc = transform.parent.GetComponent<LocationTarget>();
                if (loc != null)
                {
                    currentLocation = loc.TargetName;
                }
            }
        }

        public string GetStateDescription()
        {
            if (isHeld) return $"held by {heldByNPC}";
            if (!string.IsNullOrEmpty(currentLocation)) return $"placed at {currentLocation}";
            return "free";
        }
    }
}

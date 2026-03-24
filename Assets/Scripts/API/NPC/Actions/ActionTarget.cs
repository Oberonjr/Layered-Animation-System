using UnityEngine;
using LAS;

namespace LAS
{
    /// <summary>
    /// Abstract base for all action-targetable scene objects.
    /// Subclasses define their TargetType by overriding the Type property — no inspector field needed.
    /// Handles automatic registration/unregistration with NPCActionTargetRegistry.
    /// Do not add this component directly; use a concrete subclass such as
    /// InteractableItem, LocationTarget, NPCTarget, or PlayerTarget.
    /// </summary>
    public abstract class ActionTarget : MonoBehaviour, IActionTarget
    {
        [Header("Target Configuration")]
        [Tooltip("Optional: override the GameObject name for the target name. This name will be used by the LLM when referencing this target in action commands. Leave empty to use the GameObject's name.")]
        public string customName = "";

        /// <summary>
        /// Returns the custom name if set, otherwise returns the GameObject's name.
        /// This is the identifier used by the LLM and action system to reference this target.
        /// </summary>
        public virtual string TargetName => string.IsNullOrEmpty(customName) ? gameObject.name : customName;

        /// <summary>
        /// The category of this target. Implemented by each subclass as a constant — no inspector assignment required.
        /// </summary>
        public abstract TargetType Type { get; }

        /// <summary>
        /// Returns this GameObject's transform for spatial operations (navigation, positioning, etc.).
        /// </summary>
        public Transform Transform => transform;

        /// <summary>
        /// Registers this target with the NPCActionTargetRegistry on scene start.
        /// This makes the target visible to the action system and available for NPC interactions.
        /// </summary>
        protected virtual void Start()
        {
            NPCActionTargetRegistry.Instance?.Register(this);
        }

        /// <summary>
        /// Unregisters this target from the NPCActionTargetRegistry when destroyed.
        /// Ensures the action system doesn't reference invalid targets.
        /// </summary>
        protected virtual void OnDestroy()
        {
            NPCActionTargetRegistry.Instance?.Unregister(this);
        }

        /// <summary>
        /// Draws a colored wire sphere in the Scene view when this GameObject is selected.
        /// Provides visual feedback about the target type in the editor.
        /// </summary>
        void OnDrawGizmosSelected()
        {
            // Visual feedback in editor
            Gizmos.color = GetGizmoColor();
            Gizmos.DrawWireSphere(transform.position, 0.3f);
        }

        /// <summary>
        /// Returns a color based on the target type for editor visualization.
        /// Cyan = NPC, Green = Player, Yellow = Location, Magenta = InteractableObject.
        /// </summary>
        /// <returns>The gizmo color corresponding to this target's type.</returns>
        protected virtual Color GetGizmoColor()
        {
            return Type switch
            {
                TargetType.NPC => Color.cyan,
                TargetType.Player => Color.green,
                TargetType.Location => Color.yellow,
                TargetType.InteractableObject => Color.magenta,
                _ => Color.white
            };
        }
    }

}


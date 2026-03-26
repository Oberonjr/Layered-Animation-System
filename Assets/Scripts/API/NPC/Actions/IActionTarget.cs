using UnityEngine;
using LAS;

namespace LAS
{
    /// <summary>
    /// Marker interface for anything that can be an action target.
    /// Implementations register themselves with NPCActionTargetRegistry on Start.
    /// This interface enables objects to be referenced and interacted with by NPCs through the action system.
    /// Examples: NPCs, Players, Locations, Interactive Objects
    /// </summary>
    public interface IActionTarget
    {
        /// <summary>
        /// The display name for this target, used for identification in the action system.
        /// This name is used by the LLM to reference the target in action commands.
        /// </summary>
        string TargetName { get; }

        /// <summary>
        /// The category of this target, determining which actions can interact with it.
        /// Different target types may be compatible with different sets of actions.
        /// </summary>
        TargetType Type { get; }

        /// <summary>
        /// The Unity Transform component representing this target's position and rotation in 3D space.
        /// Used for navigation, positioning, and spatial interactions.
        /// </summary>
        Transform Transform { get; }
    }

    /// <summary>
    /// Defines the category of an action target.
    /// Each type determines which actions can be performed on the target and how NPCs interact with it.
    /// </summary>
    public enum TargetType
    {
        /// <summary>
        /// Non-player character - another AI-controlled agent in the scene.
        /// Can give/receive objects, be looked at, and participate in complex interactions.
        /// </summary>
        NPC,

        /// <summary>
        /// The player character (typically in VR).
        /// NPCs can look at, hand objects to, and interact with the player.
        /// </summary>
        Player,

        /// <summary>
        /// A spatial location or waypoint in the scene.
        /// NPCs can navigate to these positions but cannot interact physically with them.
        /// </summary>
        Location,

        /// <summary>
        /// Physical objects that can be picked up, moved, or manipulated.
        /// Examples: tools, props, items that NPCs can grasp and transfer.
        /// </summary>
        InteractableObject
    }

}

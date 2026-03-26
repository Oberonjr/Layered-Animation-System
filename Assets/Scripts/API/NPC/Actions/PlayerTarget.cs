using UnityEngine;

namespace LAS
{
    /// <summary>
    /// ActionTarget subclass for the Player GameObject.
    /// Attach to the player object to make it targetable by the NPC action system
    /// (e.g. for HAND_TO_PLAYER, LOOK_AT_PLAYER actions).
    /// Uses customName or the GameObject's name as the target identifier.
    /// </summary>
    public class PlayerTarget : ActionTarget
    {
        public override TargetType Type => TargetType.Player;
    }
}

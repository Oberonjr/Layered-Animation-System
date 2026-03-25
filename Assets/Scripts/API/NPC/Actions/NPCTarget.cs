using UnityEngine;

namespace LAS
{
    /// <summary>
    /// ActionTarget subclass for NPC GameObjects.
    /// Automatically resolves TargetName from the sibling NPCController's npcName field,
    /// so the LLM always references the character by their assigned scenario name.
    /// NPCController adds this component automatically in Awake — no manual placement needed.
    /// </summary>
    public class NPCTarget : ActionTarget
    {
        public override TargetType Type => TargetType.NPC;

        /// <summary>
        /// Returns the NPC's scenario name if an NPCController is present on this GameObject,
        /// otherwise falls back to customName or the GameObject's name.
        /// </summary>
        public override string TargetName
        {
            get
            {
                var npc = GetComponent<NPCController>();
                return (npc != null && !string.IsNullOrEmpty(npc.npcName))
                    ? npc.npcName
                    : base.TargetName;
            }
        }
    }
}

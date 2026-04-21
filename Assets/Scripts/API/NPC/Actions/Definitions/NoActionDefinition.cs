using UnityEngine;

namespace LAS
{
    /// <summary>
    /// Action definition for the no-op / "do nothing" case.
    /// Used when the LLM determines the NPC only speaks with no physical action.
    ///
    /// Keeping this as a proper registered definition ensures the dispatcher has a
    /// consistent, first-class entry for the idle case rather than special-casing a
    /// magic string literal. Reference the key via <see cref="NPCActionDefinition.NoneKey"/>
    /// rather than the string "NONE" directly.
    ///
    /// Create the asset via: Assets → Create → NPC → Actions → No Action
    /// </summary>
    [CreateAssetMenu(fileName = "NoActionDefinition", menuName = "NPC/Actions/No Action")]
    public class NoActionDefinition : NPCActionDefinition
    {
        /// <summary>
        /// Resets this asset to sensible defaults when first created.
        /// </summary>
        private void Reset()
        {
            actionKey          = NPCActionDefinition.NoneKey;
            displayName        = "No Action";
            llmDescription     = "No physical action. Use when the NPC only speaks with no movement or object interaction.";
            requiresTarget     = false;
            requiresSecondaryTarget = false;
        }
    }
}

using UnityEngine;

namespace LAS
{
    [CreateAssetMenu(fileName = "LookAtPlayerActionDefinition", menuName = "NPC/Action Definitions/Look At Player")]
    public class LookAtPlayerActionDefinition : NPCActionDefinition
    {
        public override NPCActionState MakeState(Transform primary, Transform secondary)
        {
            return new LookAtPlayerState();
        }
    }
}

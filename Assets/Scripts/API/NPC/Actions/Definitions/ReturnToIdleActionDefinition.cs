using UnityEngine;

namespace LAS
{
    [CreateAssetMenu(fileName = "ReturnToIdleActionDefinition", menuName = "NPC/Action Definitions/Return To Idle")]
    public class ReturnToIdleActionDefinition : NPCActionDefinition
    {
        public override NPCActionState MakeState(Transform primary, Transform secondary)
        {
            return new ReturnToIdleState();
        }
    }
}

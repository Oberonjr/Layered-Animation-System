using UnityEngine;

namespace LAS
{
    [CreateAssetMenu(fileName = "HandToNPCActionDefinition", menuName = "NPC/Action Definitions/Hand To NPC")]
    public class HandToNPCActionDefinition : NPCActionDefinition
    {
        public override NPCActionState MakeState(Transform primary, Transform secondary)
        {
            return new HandToNPCState(primary?.GetComponent<NPCBehaviourController>());
        }
    }
}

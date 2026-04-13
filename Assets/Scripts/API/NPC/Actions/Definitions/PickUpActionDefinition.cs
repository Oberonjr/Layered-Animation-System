using UnityEngine;

namespace LAS
{
    [CreateAssetMenu(fileName = "PickUpActionDefinition", menuName = "NPC/Action Definitions/Pick Up")]
    public class PickUpActionDefinition : NPCActionDefinition
    {
        public override NPCActionState MakeState(Transform primary, Transform secondary)
        {
            return new PickUpState(primary);
        }
    }
}

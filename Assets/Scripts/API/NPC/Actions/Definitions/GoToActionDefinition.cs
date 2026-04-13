using UnityEngine;

namespace LAS
{
    [CreateAssetMenu(fileName = "GoToActionDefinition", menuName = "NPC/Action Definitions/Go To")]
    public class GoToActionDefinition : NPCActionDefinition
    {
        public override NPCActionState MakeState(Transform primary, Transform secondary)
        {
            return new GoToState(primary);
        }
    }
}

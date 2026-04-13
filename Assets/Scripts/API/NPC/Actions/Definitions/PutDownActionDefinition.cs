using UnityEngine;

namespace LAS
{
    [CreateAssetMenu(fileName = "PutDownActionDefinition", menuName = "NPC/Action Definitions/Put Down")]
    public class PutDownActionDefinition : NPCActionDefinition
    {
        public override NPCActionState MakeState(Transform primary, Transform secondary)
        {
            return new PutDownState(primary);
        }
    }
}

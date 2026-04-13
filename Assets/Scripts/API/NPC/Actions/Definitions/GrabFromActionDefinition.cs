using UnityEngine;

namespace LAS
{
    [CreateAssetMenu(fileName = "GrabFromActionDefinition", menuName = "NPC/Action Definitions/Grab From")]
    public class GrabFromActionDefinition : NPCActionDefinition
    {
        public override NPCActionState MakeState(Transform primary, Transform secondary)
        {
            return new GrabFromState(primary?.GetComponent<NPCBehaviourController>());
        }
    }
}

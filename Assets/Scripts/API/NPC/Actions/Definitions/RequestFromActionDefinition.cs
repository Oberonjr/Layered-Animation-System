using UnityEngine;

namespace LAS
{
    [CreateAssetMenu(fileName = "RequestFromActionDefinition", menuName = "NPC/Action Definitions/Request From")]
    public class RequestFromActionDefinition : NPCActionDefinition
    {
        public override NPCActionState MakeState(Transform primary, Transform secondary)
        {
            return new RequestFromState(primary);
        }
    }
}

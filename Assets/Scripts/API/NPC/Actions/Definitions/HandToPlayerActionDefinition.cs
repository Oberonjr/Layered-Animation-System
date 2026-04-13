using UnityEngine;

namespace LAS
{
    [CreateAssetMenu(fileName = "HandToPlayerActionDefinition", menuName = "NPC/Action Definitions/Hand To Player")]
    public class HandToPlayerActionDefinition : NPCActionDefinition
    {
        public override NPCActionState MakeState(Transform primary, Transform secondary)
        {
            return new HandToPlayerState();
        }
    }
}

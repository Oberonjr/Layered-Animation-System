using UnityEngine;

namespace LAS
{
    [CreateAssetMenu(fileName = "LookAtActionDefinition", menuName = "NPC/Action Definitions/Look At")]
    public class LookAtActionDefinition : NPCActionDefinition
    {
        public override NPCActionState MakeState(Transform primary, Transform secondary)
        {
            return new LookAtState(primary);
        }
    }
}

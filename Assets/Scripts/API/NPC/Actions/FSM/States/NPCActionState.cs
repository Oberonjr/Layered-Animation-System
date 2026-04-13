using UnityEngine;

namespace LAS
{
    public class NPCActionState : INPCState
    {
        protected readonly NPCActionDefinition Definition;

        public NPCActionState(NPCActionDefinition definition)
        {
            Definition = definition;
        }

        public virtual void EnterState(NPCBehaviourContext ctx) { }
        public virtual bool UpdateState(NPCBehaviourContext ctx) => true;
        public virtual void ExitState(NPCBehaviourContext ctx) { }
    }
}

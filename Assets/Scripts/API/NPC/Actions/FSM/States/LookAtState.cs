using UnityEngine;

namespace LAS
{
    /// <summary>
    /// Sets the NPC's look-at target and completes immediately.
    /// NPCLookAtController tracks the target continuously until another state changes it.
    /// </summary>
    public class LookAtState : NPCActionState
    {
        private readonly Transform _target;

        public LookAtState(Transform target) : base(null) => _target = target;
        public LookAtState(NPCActionDefinition def) : base(def) { }

        public override void EnterState(NPCBehaviourContext ctx)
        {
            if (_target != null)
                ctx.LookAt.SetTarget(_target);
        }

        public override bool UpdateState(NPCBehaviourContext ctx) => true;

        public override void ExitState(NPCBehaviourContext ctx) { }
    }
}

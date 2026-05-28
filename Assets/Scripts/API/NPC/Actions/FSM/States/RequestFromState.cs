using UnityEngine;

namespace LAS
{
    /// <summary>
    /// Faces a target and signals a request for their held object.
    /// Completes immediately. Extend EnterState with a gesture animation trigger when available.
    /// </summary>
    public class RequestFromState : NPCActionState
    {
        private readonly Transform _target;

        public RequestFromState(Transform target) : base(null) => _target = target;
        public RequestFromState(NPCActionDefinition def) : base(def) { }

        public override void EnterState(NPCBehaviourContext ctx)
        {
            if (_target == null) return;
            ctx.IKController.SetLookAtTarget(_target);
            // TODO: trigger a "requesting" gesture animation here.
            Debug.Log($"[{ctx.NPCName}] Requesting object from '{_target.name}'.");
        }

        public override bool UpdateState(NPCBehaviourContext ctx) => true;

        public override void ExitState(NPCBehaviourContext ctx) { }
    }
}

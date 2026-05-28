using UnityEngine;

namespace LAS
{
    /// <summary>
    /// Triggers the IK grab animation on the target item.
    /// Expects GoToState to have already navigated the NPC within PickupRange.
    /// </summary>
    public class PickUpState : NPCActionState
    {
        private readonly Transform _target;

        public PickUpState(Transform target) : base(null) => _target = target;
        public PickUpState(NPCActionDefinition def) : base(def) { }

        public override void EnterState(NPCBehaviourContext ctx)
        {
            if (_target == null) return;

            if (ctx.IsHoldingObject)
            {
                Debug.LogWarning($"[{ctx.NPCName}] PickUp: already holding '{ctx.HeldObject.name}'.");
                NPCEventBus.BroadcastActionImpossible(ctx.NPCIndex,
                    $"I'm already holding {ctx.HeldObject.name} — I can't pick up {_target.name} at the same time.");
                return;
            }

            ctx.IKController.SetGrabTarget(_target);
            ctx.IKController.Grab();
        }

        public override bool UpdateState(NPCBehaviourContext ctx)
        {
            if (ctx.IKController.hasGrabbed)
            {
                ctx.HeldObject = _target.gameObject;
                return true;
            }

            return false;
        }

        public override void ExitState(NPCBehaviourContext ctx) { }
    }
}

using UnityEngine;

namespace LAS
{
    /// <summary>
    /// Navigates the NPC back to their idle position (if set), then resets gaze to the player.
    /// Delegates movement to GoToState so animation blend and leg IK are handled consistently.
    /// If no idle position is assigned, completes immediately.
    /// </summary>
    public class ReturnToIdleState : NPCActionState
    {
        private GoToState _goTo;

        public ReturnToIdleState() : base(null) { }
        public ReturnToIdleState(NPCActionDefinition def) : base(def) { }

        public override void EnterState(NPCBehaviourContext ctx)
        {
            ctx.IsOffering = false;

            if (ctx.IdlePosition != null)
            {
                _goTo = new GoToState(ctx.IdlePosition);
                _goTo.EnterState(ctx);
            }
        }

        public override bool UpdateState(NPCBehaviourContext ctx)
        {
            if (_goTo == null) return Complete(ctx);

            if (_goTo.UpdateState(ctx))
            {
                _goTo.ExitState(ctx);
                _goTo = null;
                return Complete(ctx);
            }
            return false;
        }

        public override void ExitState(NPCBehaviourContext ctx)
        {
            if (_goTo != null)
            {
                _goTo.ExitState(ctx);
                _goTo = null;
            }
        }

        private bool Complete(NPCBehaviourContext ctx)
        {
            var player = ctx.GetPlayerTransform();
            if (player != null) ctx.IKController.SetLookAtTarget(player);
            else ctx.IKController.ClearLookAtTarget();
            ctx.FireReturnedToIdle();
            return true;
        }
    }
}

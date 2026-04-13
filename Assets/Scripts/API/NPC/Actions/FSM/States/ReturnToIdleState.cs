using UnityEngine;
using UnityEngine.AI;

namespace LAS
{
    /// <summary>
    /// Navigates the NPC back to their idle position (if set), then resets gaze to the player.
    /// If no idle position is assigned, completes immediately.
    /// </summary>
    public class ReturnToIdleState : NPCActionState
    {
        private float _startTime;
        private bool _navigating;

        public ReturnToIdleState() : base(null) { }
        public ReturnToIdleState(NPCActionDefinition def) : base(def) { }

        public override void EnterState(NPCBehaviourContext ctx)
        {
            ctx.IsOffering = false;

            if (ctx.IdlePosition != null)
            {
                ctx.LookAt.SetTarget(ctx.IdlePosition);
                ctx.Animator?.SetTrigger("StartWalking");
                ctx.Agent.stoppingDistance = 0f;
                ctx.Agent.SetDestination(ctx.IdlePosition.position);
                _startTime  = Time.time;
                _navigating = true;
            }
            else
            {
                _navigating = false;
            }
        }

        public override bool UpdateState(NPCBehaviourContext ctx)
        {
            if (!_navigating) return Complete(ctx);

            if (Time.time - _startTime > ctx.GoToTimeoutSeconds)
            {
                Debug.LogWarning($"[{ctx.NPCName}] ReturnToIdle: navigation timed out.");
                return Complete(ctx);
            }

            if (ctx.Agent.pathPending) return false;

            if (ctx.Agent.pathStatus == NavMeshPathStatus.PathInvalid
                || ctx.Agent.remainingDistance <= ctx.ArrivalDistance)
                return Complete(ctx);

            return false;
        }

        public override void ExitState(NPCBehaviourContext ctx)
        {
            ctx.Animator?.SetTrigger("StopWalking");
            if (ctx.Agent.hasPath) ctx.Agent.ResetPath();
        }

        private bool Complete(NPCBehaviourContext ctx)
        {
            var player = ctx.GetPlayerTransform();
            if (player != null) ctx.LookAt.SetTarget(player);
            else ctx.LookAt.Clear();
            ctx.FireReturnedToIdle();
            return true;
        }
    }
}

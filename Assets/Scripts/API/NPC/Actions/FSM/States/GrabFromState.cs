using UnityEngine;
using UnityEngine.AI;

namespace LAS
{
    /// <summary>
    /// Walks to another NPC and takes the object they are holding.
    /// </summary>
    public class GrabFromState : NPCActionState
    {
        private readonly NPCBehaviourController _targetNPC;
        private float _startTime;

        public GrabFromState(NPCBehaviourController targetNPC) : base(null) => _targetNPC = targetNPC;
        public GrabFromState(NPCActionDefinition def) : base(def) { }

        public override void EnterState(NPCBehaviourContext ctx)
        {
            if (_targetNPC == null) return;

            ctx.LookAt.SetTarget(_targetNPC.transform);
            ctx.Animator?.SetTrigger("StartWalking");
            ctx.Agent.stoppingDistance = 0f;
            ctx.Agent.SetDestination(_targetNPC.transform.position);
            _startTime = Time.time;
        }

        public override bool UpdateState(NPCBehaviourContext ctx)
        {
            if (_targetNPC == null) return true;

            if (Time.time - _startTime > ctx.GoToTimeoutSeconds)
            {
                Debug.LogWarning($"[{ctx.NPCName}] GrabFrom: timed out.");
                return true;
            }

            if (ctx.Agent.pathPending) return false;

            if (ctx.Agent.pathStatus == NavMeshPathStatus.PathInvalid)
                return true;

            if (ctx.Agent.remainingDistance <= ctx.HandOverRange)
            {
                Grab(ctx);
                return true;
            }

            return false;
        }

        public override void ExitState(NPCBehaviourContext ctx)
        {
            ctx.Animator?.SetTrigger("StopWalking");
            if (ctx.Agent.hasPath) ctx.Agent.ResetPath();
        }

        private void Grab(NPCBehaviourContext ctx)
        {
            var targetCtx = _targetNPC.Context;
            if (targetCtx == null || !targetCtx.IsHoldingObject)
            {
                Debug.LogWarning($"[{ctx.NPCName}] GrabFrom: '{_targetNPC.name}' is not holding anything.");
                return;
            }

            var grabbed = targetCtx.HeldObject;
            targetCtx.HeldObject = null;
            ctx.HeldObject = grabbed;

            if (ctx.ItemSlot != null)
            {
                grabbed.transform.SetParent(ctx.ItemSlot);
                grabbed.transform.localPosition = Vector3.zero;
                grabbed.transform.localRotation = Quaternion.identity;
            }

            ctx.FirePickedUp(grabbed);
        }
    }
}

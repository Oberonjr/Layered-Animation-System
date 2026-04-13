using UnityEngine;
using UnityEngine.AI;

namespace LAS
{
    /// <summary>
    /// Navigates the NPC to a target transform using NavMeshAgent.
    /// Completes on arrival, path failure, or timeout.
    /// </summary>
    public class GoToState : NPCActionState
    {
        private readonly Transform _target;
        private readonly float _rangeOverride; // -1 = use ctx.ArrivalDistance

        private float _startTime;
        private float _range;
        private bool _ready;

        public GoToState(Transform target, float rangeOverride = -1f) : base(null)
        {
            _target        = target;
            _rangeOverride = rangeOverride;
        }

        public GoToState(NPCActionDefinition def) : base(def) { }

        public override void EnterState(NPCBehaviourContext ctx)
        {
            if (_target == null) return;

            if (_target == ctx.Agent.transform)
            {
                Debug.LogWarning($"[{ctx.NPCName}] GoTo: target is self — ignoring.");
                NPCEventBus.BroadcastActionImpossible(ctx.NPCIndex, "I can't walk to myself.");
                return;
            }

            _range = _rangeOverride > 0f ? _rangeOverride : ctx.ArrivalDistance;
            ctx.LookAt.SetTarget(_target);
            ctx.Animator?.SetTrigger("StartWalking");
            ctx.Agent.stoppingDistance = 0f;
            ctx.Agent.SetDestination(_target.position);
            _startTime = Time.time;
            _ready = true;
        }

        public override bool UpdateState(NPCBehaviourContext ctx)
        {
            if (_target == null || !_ready) return true;

            if (Time.time - _startTime > ctx.GoToTimeoutSeconds)
            {
                Debug.LogWarning($"[{ctx.NPCName}] GoTo: timed out navigating to '{_target.name}'.");
                return true;
            }

            if (ctx.Agent.pathPending) return false;

            if (ctx.Agent.pathStatus == NavMeshPathStatus.PathInvalid)
            {
                Debug.LogWarning($"[{ctx.NPCName}] GoTo: no valid NavMesh path to '{_target.name}'.");
                return true;
            }

            if (ctx.Agent.remainingDistance <= _range)
            {
                ctx.FireArrivedAtTarget(_target);
                return true;
            }

            return false;
        }

        public override void ExitState(NPCBehaviourContext ctx)
        {
            ctx.Animator?.SetTrigger("StopWalking");
            if (ctx.Agent.hasPath) ctx.Agent.ResetPath();
        }
    }
}

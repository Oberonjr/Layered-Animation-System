using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.AI;

namespace LAS
{
    /// <summary>
    /// Navigates the NPC to a target transform using NavMeshAgent.
    /// Completes immediately if already within range, or on arrival, path failure, or timeout.
    /// When the target has a NavMeshAgent (another NPC), combined agent radii are added to the
    /// stopping range to prevent body-pushing.
    /// </summary>
    public class GoToState : NPCActionState
    {
        private readonly Transform _target;
        private readonly float _rangeOverride;   // used when _rangeType == Default
        private readonly ApproachRange _rangeType;

        private float _startTime;
        private float _range;
        private bool _ready;
        private bool _alreadyInRange;
        private bool _walkStarted;
        private bool _needsLookAt;

        private bool isDestinationSet;

        private const float BlendDuration = 0.3f;
        private Tweener _blendTween;


        public GoToState(Transform target, float rangeOverride = -1f) : base(null)
        {
            _target        = target;
            _rangeOverride = rangeOverride;
            _rangeType     = ApproachRange.Default;
        }

        public GoToState(Transform target, ApproachRange rangeType) : base(null)
        {
            _target        = target;
            _rangeOverride = -1f;
            _rangeType     = rangeType;
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

            _range = _rangeType switch
            {
                ApproachRange.Pickup   => ctx.PickupRange,
                ApproachRange.HandOver => ctx.HandOverRange,
                _                      => _rangeOverride > 0f ? _rangeOverride : ctx.ArrivalDistance
            };

            // Add combined agent radii when approaching another NPC to avoid body-pushing.
            var targetAgent = _target.GetComponent<NavMeshAgent>();
            if (targetAgent != null)
                _range += ctx.Agent.radius + targetAgent.radius;

            float dist = Vector3.Distance(ctx.Agent.transform.position, _target.position);
            if (dist <= _range)
            {
                Debug.Log($"[{ctx.NPCName}] GoTo: already within range of '{_target.name}' (dist={dist:F2}, range={_range:F2}) — skipping movement.");
                _alreadyInRange = true;
                return;
            }
            if(Vector3.Angle(_target.position - ctx.Agent.transform.position, ctx.Agent.transform.forward) > 15)
            {
                ctx.IKController.SetLookAtTarget(_target);
                _needsLookAt = true;
            }
            ctx.Agent.stoppingDistance = _range;
            _startTime = Time.time;
            _ready = true;
        }

        public override bool UpdateState(NPCBehaviourContext ctx)
        {
            if (_alreadyInRange) return true;
            if (_target == null || !_ready) return true;

            if (ctx.IKController.canStartAnim)
            {
                ctx.IKController.EnableLegIK(false);
                BlendWalkAnim(0, 1, ctx);
            }
            
            if(_needsLookAt && !ctx.IKController.isLookingAtTarget && !ctx.IKController.canStartAnim)
                return false;
            
            //Debug.Log("Start walking");
            
            // Wait until NPC is looking at target before starting to move
            if (!isDestinationSet)
            {
                if (!ctx.IKController.canStartAnim)
                {
                    ctx.IKController.EnableLegIK(false);
                    BlendWalkAnim(0, 1, ctx);
                }
                
                ctx.Agent.SetDestination(_target.position);
                isDestinationSet = true;
            }

            if (!isDestinationSet)
                return false;
            
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

            float euclideanDist = Vector3.Distance(ctx.Agent.transform.position, _target.position);
            bool pathArrived   = ctx.Agent.remainingDistance <= _range;
            bool stoppedInRange = ctx.Agent.velocity.sqrMagnitude < 0.01f && euclideanDist <= _range;

            if (pathArrived || stoppedInRange)
            {
                ctx.FireArrivedAtTarget(_target);
                return true;
            }

            return false;
        }

        public override void ExitState(NPCBehaviourContext ctx)
        {
            if (_alreadyInRange) return;

            ctx.IKController.EnableLegIK(true);
            BlendWalkAnim(1, 0, ctx);

            Debug.Log($"[{ctx.NPCName}] GoTo: stopped walking.");
            if (ctx.Agent.hasPath) ctx.Agent.ResetPath();
        }

        private void BlendWalkAnim(float start, float end, NPCBehaviourContext ctx)
        {
            _blendTween?.Kill();
            float currentBlend = ctx.Animator.GetFloat("Blend");
            if (Mathf.Approximately(currentBlend, end))
                return;

            _blendTween = DOVirtual.Float(currentBlend, end, BlendDuration, value =>
            {
                ctx.Animator.SetFloat("Blend", value);
            }).OnComplete(() => _blendTween = null);
        }
    }
}

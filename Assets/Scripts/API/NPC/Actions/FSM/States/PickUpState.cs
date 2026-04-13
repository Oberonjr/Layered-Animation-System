using UnityEngine;
using UnityEngine.AI;

namespace LAS
{
    /// <summary>
    /// Navigates to an item and picks it up.
    /// Phases: Navigating → complete (grab happens on arrival before returning true).
    /// </summary>
    public class PickUpState : NPCActionState
    {
        private readonly Transform _target;
        private float _startTime;

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

            ctx.LookAt.SetTarget(_target);
            ctx.Animator?.SetTrigger("StartWalking");
            ctx.Agent.stoppingDistance = 0f;
            ctx.Agent.SetDestination(_target.position);
            _startTime = Time.time;
        }

        public override bool UpdateState(NPCBehaviourContext ctx)
        {
            if (_target == null || ctx.IsHoldingObject) return true;

            if (Time.time - _startTime > ctx.GoToTimeoutSeconds)
            {
                Debug.LogWarning($"[{ctx.NPCName}] PickUp: timed out navigating to '{_target.name}'.");
                return true;
            }

            if (ctx.Agent.pathPending) return false;

            if (ctx.Agent.pathStatus == NavMeshPathStatus.PathInvalid)
            {
                Debug.LogWarning($"[{ctx.NPCName}] PickUp: no valid path to '{_target.name}'.");
                return true;
            }

            if (ctx.Agent.remainingDistance <= ctx.PickupRange)
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
            var obj = _target.gameObject;
            ctx.HeldObject = obj;

            if (ctx.ItemSlot != null)
            {
                obj.transform.SetParent(ctx.ItemSlot);
                obj.transform.localPosition = Vector3.zero;
                obj.transform.localRotation = Quaternion.identity;
            }

            var rb = obj.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            var interactable = obj.GetComponent<InteractableItem>();
            if (interactable != null)
            {
                interactable.isHeld          = true;
                interactable.heldByNPC       = ctx.NPCName;
                interactable.currentLocation = "";
            }

            ctx.FirePickedUp(obj);
        }
    }
}

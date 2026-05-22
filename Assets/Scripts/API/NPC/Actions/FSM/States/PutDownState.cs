using UnityEngine;
using UnityEngine.AI;

namespace LAS
{
    /// <summary>
    /// Walks to a LocationTarget and places the held item in the matching ItemSlot.
    /// </summary>
    public class PutDownState : NPCActionState
    {
        private readonly Transform _target;
        private LocationTarget _location;
        private ItemSlot _slot;
        private float _startTime;
        private bool _ready;

        public PutDownState(Transform target) : base(null) => _target = target;
        public PutDownState(NPCActionDefinition def) : base(def) { }

        public override void EnterState(NPCBehaviourContext ctx)
        {
            if (_target == null) return;

            if (!ctx.IsHoldingObject)
            {
                Debug.LogWarning($"[{ctx.NPCName}] PutDown: not holding anything.");
                NPCEventBus.BroadcastActionImpossible(ctx.NPCIndex, "I'm not holding anything to put down.");
                return;
            }

            _location = _target.GetComponent<LocationTarget>()
                     ?? _target.GetComponentInParent<LocationTarget>();

            if (_location == null)
            {
                Debug.LogWarning($"[{ctx.NPCName}] PutDown: '{_target.name}' has no LocationTarget.");
                NPCEventBus.BroadcastActionImpossible(ctx.NPCIndex, $"I can't place things at {_target.name}.");
                return;
            }

            var item = ctx.HeldObject.GetComponent<InteractableItem>();
            _slot = item != null ? _location.GetSlotForItem(item) : null;

            if (_slot == null)
            {
                string itemName = item != null ? item.TargetName : ctx.HeldObject.name;
                Debug.LogWarning($"[{ctx.NPCName}] PutDown: '{_location.TargetName}' has no slot for '{itemName}'.");
                NPCEventBus.BroadcastActionImpossible(ctx.NPCIndex,
                    $"There's no designated spot for {itemName} at {_location.TargetName}.");
                return;
            }

            ctx.LookAt.SetTarget(_target);
            ctx.Agent.stoppingDistance = 0f;
            ctx.Agent.SetDestination(_target.position);
            _startTime = Time.time;
            _ready = true;
        }

        public override bool UpdateState(NPCBehaviourContext ctx)
        {
            if (!_ready || _location == null || _slot == null) return true;
            if (!ctx.IsHoldingObject) return true;

            if (Time.time - _startTime > ctx.GoToTimeoutSeconds)
            {
                Debug.LogWarning($"[{ctx.NPCName}] PutDown: timed out.");
                return true;
            }

            if (ctx.Agent.pathPending) return false;

            if (ctx.Agent.pathStatus == NavMeshPathStatus.PathInvalid)
                return true;

            if (ctx.Agent.remainingDistance <= ctx.PickupRange)
            {
                Place(ctx);
                return true;
            }

            return false;
        }

        public override void ExitState(NPCBehaviourContext ctx)
        {
            if (ctx.Agent.hasPath) ctx.Agent.ResetPath();
        }

        private void Place(NPCBehaviourContext ctx)
        {
            var item = ctx.HeldObject.GetComponent<InteractableItem>();
            if (item == null) return;

            var placing = ctx.HeldObject;
            ctx.HeldObject = null;

            if (_slot.TryPlaceItem(item))
            {
                _location.AddItem(item.TargetName);
                ctx.FireHandedObject(placing);
                Debug.Log($"[{ctx.NPCName}] Placed '{item.TargetName}' at '{_location.TargetName}'.");
            }
            else
            {
                ctx.HeldObject = placing; // slot became occupied between navigation and arrival
                NPCEventBus.BroadcastActionImpossible(ctx.NPCIndex,
                    $"The slot at {_location.TargetName} is already occupied.");
            }
        }
    }
}

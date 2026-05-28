using UnityEngine;

namespace LAS
{
    /// <summary>
    /// Places the held item into the matching ItemSlot at a LocationTarget.
    /// Expects GoToState to have already navigated the NPC within PickupRange.
    /// </summary>
    public class PutDownState : NPCActionState
    {
        private readonly Transform _target;
        private LocationTarget _location;
        private ItemSlot _slot;
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

            _ready = true;
        }

        public override bool UpdateState(NPCBehaviourContext ctx)
        {
            if (!_ready || _location == null || _slot == null) return true;
            if (!ctx.IsHoldingObject) return true;

            Place(ctx);
            return true;
        }

        public override void ExitState(NPCBehaviourContext ctx) { }

        private void Place(NPCBehaviourContext ctx)
        {
            var item = ctx.HeldObject.GetComponent<InteractableItem>();
            if (item == null) return;

            var placing = ctx.HeldObject;
            ctx.HeldObject = null;

            ctx.IKController.PutDown();
            
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

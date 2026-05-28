using UnityEngine;
using UnityEngine.AI;

namespace LAS
{
    /// <summary>
    /// Walks to the player and hands over the held object.
    ///
    /// OfferForPickup: holds the object at OfferOffset and waits until the player calls
    ///   NPCBehaviourController.TakeOfferedObject() which clears IsOffering.
    ///
    /// DirectTransfer: parents the object directly to the player's transform on arrival.
    /// </summary>
    public class HandToPlayerState : NPCActionState
    {
        private Transform _player;
        private bool _navigating;
        private float _startTime;

        public HandToPlayerState() : base(null) { }
        public HandToPlayerState(NPCActionDefinition def) : base(def) { }

        public override void EnterState(NPCBehaviourContext ctx)
        {
            if (!ctx.IsHoldingObject)
            {
                Debug.LogWarning($"[{ctx.NPCName}] HandToPlayer: not holding anything.");
                NPCEventBus.BroadcastActionImpossible(ctx.NPCIndex, "I'm not holding anything to hand over.");
                _navigating = false;
                return;
            }

            _player = ctx.GetPlayerTransform();

            if (_player != null)
            {
                ctx.IKController.SetLookAtTarget(_player);
                ctx.Agent.stoppingDistance = 0f;
                ctx.Agent.SetDestination(_player.position);
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
            if (!ctx.IsHoldingObject) return true;

            if (_navigating)
            {
                if (Time.time - _startTime > ctx.GoToTimeoutSeconds)
                {
                    Debug.LogWarning($"[{ctx.NPCName}] HandToPlayer: navigation timed out.");
                    return Handoff(ctx);
                }

                if (ctx.Agent.pathPending) return false;

                if (ctx.Agent.pathStatus == NavMeshPathStatus.PathInvalid
                    || ctx.Agent.remainingDistance <= ctx.HandOverRange)
                    return Handoff(ctx);

                return false;
            }

            // Offering mode: wait for player to take
            if (ctx.PlayerHandoffMode == HandoffMode.OfferForPickup)
                return !ctx.IsOffering || ctx.HeldObject == null;

            return true;
        }

        public override void ExitState(NPCBehaviourContext ctx)
        {
            if (ctx.Agent.hasPath) ctx.Agent.ResetPath();
            ctx.IsOffering = false;
        }

        private bool Handoff(NPCBehaviourContext ctx)
        {
            _navigating = false;

            if (ctx.PlayerHandoffMode == HandoffMode.DirectTransfer)
            {
                if (ctx.HeldObject != null && _player != null)
                {
                    var obj = ctx.HeldObject;
                    ctx.HeldObject = null;

                    obj.transform.SetParent(_player);
                    obj.transform.localPosition = Vector3.zero;

                    var rb = obj.GetComponent<Rigidbody>();
                    if (rb != null) rb.isKinematic = false;

                    var interactable = obj.GetComponent<InteractableItem>();
                    if (interactable != null)
                    {
                        interactable.isHeld          = true;
                        interactable.heldByNPC       = "Player";
                        interactable.currentLocation = "";
                    }

                    ctx.FireHandedObject(obj);
                }
                return true;
            }
            else // OfferForPickup
            {
                if (ctx.HeldObject != null && ctx.ItemSlot != null)
                    ctx.HeldObject.transform.localPosition = ctx.OfferOffset;

                ctx.IsOffering = true;
                return false; // UpdateState will now wait for IsOffering to clear
            }
        }
    }
}

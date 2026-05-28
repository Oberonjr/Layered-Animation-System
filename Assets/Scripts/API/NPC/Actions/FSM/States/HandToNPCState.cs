using UnityEngine;
using UnityEngine.AI;

namespace LAS
{
    /// <summary>
    /// Walks to another NPC and transfers the held object to their item slot.
    /// </summary>
    public class HandToNPCState : NPCActionState
    {
        private readonly NPCBehaviourController _targetNPC;
        private float _startTime;

        public HandToNPCState(NPCBehaviourController targetNPC) : base(null) => _targetNPC = targetNPC;
        public HandToNPCState(NPCActionDefinition def) : base(def) { }

        public override void EnterState(NPCBehaviourContext ctx)
        {
            if (_targetNPC == null) return;

            if (!ctx.IsHoldingObject)
            {
                Debug.LogWarning($"[{ctx.NPCName}] HandToNPC: not holding anything.");
                return;
            }

            ctx.LookAt.SetTarget(_targetNPC.transform);
            ctx.Agent.stoppingDistance = 0f;
            ctx.Agent.SetDestination(_targetNPC.transform.position);
            _startTime = Time.time;
        }

        public override bool UpdateState(NPCBehaviourContext ctx)
        {
            if (_targetNPC == null || !ctx.IsHoldingObject) return true;

            if (Time.time - _startTime > ctx.GoToTimeoutSeconds)
            {
                Debug.LogWarning($"[{ctx.NPCName}] HandToNPC: timed out.");
                return true;
            }

            if (ctx.Agent.pathPending) return false;

            if (ctx.Agent.pathStatus == NavMeshPathStatus.PathInvalid)
                return true;

            if (ctx.Agent.remainingDistance <= ctx.HandOverRange)
            {
                Transfer(ctx);
                return true;
            }

            return false;
        }

        public override void ExitState(NPCBehaviourContext ctx)
        {
            if (ctx.Agent.hasPath) ctx.Agent.ResetPath();
        }

        private void Transfer(NPCBehaviourContext ctx)
        {
            if (ctx.HeldObject == null) return;

            var targetCtx = _targetNPC.Context;
            if (targetCtx == null || targetCtx.ItemSlot == null) return;

            var transferring = ctx.HeldObject;
            ctx.HeldObject = null;

            transferring.transform.SetParent(targetCtx.ItemSlot);
            transferring.transform.localPosition = Vector3.zero;
            transferring.transform.localRotation = Quaternion.identity;
            targetCtx.HeldObject = transferring;

            var interactable = transferring.GetComponent<InteractableItem>();
            if (interactable != null)
            {
                interactable.isHeld          = true;
                interactable.heldByNPC       = targetCtx.NPCName;
                interactable.currentLocation = "";
            }

            ctx.FireHandedObject(transferring);
        }
    }
}

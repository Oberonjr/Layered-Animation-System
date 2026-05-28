using UnityEngine;

namespace LAS
{
    /// <summary>
    /// Transfers the held object to another NPC's item slot.
    /// Expects GoToState to have already navigated the NPC within HandOverRange.
    /// </summary>
    public class HandToNPCState : NPCActionState
    {
        private readonly NPCBehaviourController _targetNPC;

        public HandToNPCState(NPCBehaviourController targetNPC) : base(null) => _targetNPC = targetNPC;
        public HandToNPCState(NPCActionDefinition def) : base(def) { }

        public override void EnterState(NPCBehaviourContext ctx)
        {
            if (!ctx.IsHoldingObject)
                Debug.LogWarning($"[{ctx.NPCName}] HandToNPC: not holding anything.");
        }

        public override bool UpdateState(NPCBehaviourContext ctx)
        {
            if (_targetNPC == null || !ctx.IsHoldingObject) return true;

            Transfer(ctx);
            return true;
        }

        public override void ExitState(NPCBehaviourContext ctx) { }

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

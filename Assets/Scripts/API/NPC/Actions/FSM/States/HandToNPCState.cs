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
            if (_targetNPC == null || !ctx.IsHoldingObject) 
                return true;

            Debug.Log(_targetNPC.transform.name);
            
            Transfer(ctx);
            return true;
        }

        public override void ExitState(NPCBehaviourContext ctx) { }

        private void Transfer(NPCBehaviourContext ctx)
        {
            if (ctx.HeldObject == null) 
                return;
            
            var targetCtx = _targetNPC.Context;
            if (targetCtx == null || targetCtx.ItemSlot == null)
            {
                Debug.LogWarning($"[{ctx.NPCName}] HandToNPC: no target context or item slot assigned.");
                return;
            }
            
            var transferring = ctx.HeldObject;

            targetCtx.IKController.SetGrabTarget(transferring.transform);
            
            ctx.IKController.HandOver(targetCtx.IKController);
            
            targetCtx.HeldObject = transferring;
            ctx.HeldObject = null;
            
            ctx.FireHandedObject(transferring);
        }
    }
}

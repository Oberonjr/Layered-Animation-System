using UnityEngine;

namespace LAS
{
    /// <summary>
    /// Takes the object held by another NPC.
    /// Expects GoToState to have already navigated the NPC within HandOverRange.
    /// </summary>
    public class GrabFromState : NPCActionState
    {
        private readonly NPCBehaviourController _targetNPC;

        public GrabFromState(NPCBehaviourController targetNPC) : base(null) => _targetNPC = targetNPC;
        public GrabFromState(NPCActionDefinition def) : base(def) { }

        public override void EnterState(NPCBehaviourContext ctx) { }

        public override bool UpdateState(NPCBehaviourContext ctx)
        {
            if (_targetNPC == null) return true;

            Grab(ctx);
            return true;
        }

        public override void ExitState(NPCBehaviourContext ctx) { }

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

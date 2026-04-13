namespace LAS
{
    /// <summary>
    /// Default resting state. Points the NPC's gaze at the player and completes immediately.
    /// </summary>
    public class IdleState : NPCActionState
    {
        public IdleState() : base(null) { }

        public override void EnterState(NPCBehaviourContext ctx)
        {
            var player = ctx.GetPlayerTransform();
            if (player != null)
                ctx.LookAt.SetTarget(player);
            else
                ctx.LookAt.Clear();
        }

        public override bool UpdateState(NPCBehaviourContext ctx) => true;

        public override void ExitState(NPCBehaviourContext ctx) { }
    }
}

namespace LAS
{
    /// <summary>
    /// Points the NPC's gaze at the player and completes immediately.
    /// NPCLookAtController keeps tracking the player until another state changes the target.
    /// </summary>
    public class LookAtPlayerState : NPCActionState
    {
        public LookAtPlayerState() : base(null) { }
        public LookAtPlayerState(NPCActionDefinition def) : base(def) { }

        public override void EnterState(NPCBehaviourContext ctx)
        {
            var player = ctx.GetPlayerTransform();
            if (player != null)
                ctx.LookAt.SetTarget(player);
        }

        public override bool UpdateState(NPCBehaviourContext ctx) => true;

        public override void ExitState(NPCBehaviourContext ctx) { }
    }
}

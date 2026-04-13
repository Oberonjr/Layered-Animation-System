namespace LAS
{
    /// <summary>
    /// Contract for every FSM action state.
    /// EnterState is called once when the state becomes active.
    /// UpdateState is called every frame; return true to signal completion.
    /// ExitState is called once on both normal completion and interruption.
    /// </summary>
    public interface INPCState
    {
        void EnterState(NPCBehaviourContext ctx);
        bool UpdateState(NPCBehaviourContext ctx);
        void ExitState(NPCBehaviourContext ctx);
    }
}

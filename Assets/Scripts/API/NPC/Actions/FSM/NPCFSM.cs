using System.Collections.Generic;
using UnityEngine;

namespace LAS
{
    /// <summary>
    /// Per-NPC finite state machine. Owns a queue of INPCState instances and ticks them
    /// each frame via Update(), called from NPCBehaviourController.Update().
    ///
    /// Two entry points:
    ///   Enqueue + StartQueuedActions — build a sequence, then let Update() run it.
    ///   Interrupt                    — cancel everything and run a state immediately.
    /// </summary>
    public class NPCFSM
    {
        private readonly NPCBehaviourContext _ctx;

        private readonly Queue<INPCState> _queue = new Queue<INPCState>();
        private INPCState _currentState;

        /// <summary>True while a state is active or states are waiting in the queue.</summary>
        public bool IsRunning => _currentState != null || _queue.Count > 0;

        /// <summary>Number of states waiting in the queue (not counting the currently running one).</summary>
        public int QueuedCount => _queue.Count;

        public NPCFSM(NPCBehaviourContext ctx)
        {
            _ctx = ctx;
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Tick the active state. Call from NPCBehaviourController.Update() every frame.
        /// Automatically advances to the next queued state when the current one completes.
        /// </summary>
        public void Update()
        {
            if (_currentState == null && _queue.Count > 0)
                ActivateNext();

            if (_currentState == null) return;

            if (_currentState.UpdateState(_ctx))
            {
                _currentState.ExitState(_ctx);
                _currentState = null;

                // Chain immediately — avoids a one-frame gap for instant states in sequences.
                if (_queue.Count > 0)
                    ActivateNext();
            }
        }

        /// <summary>Adds a state to the back of the queue without starting it.</summary>
        public void Enqueue(INPCState state)
        {
            _queue.Enqueue(state);
        }

        /// <summary>
        /// No-op — Update() advances the queue automatically.
        /// Kept for call-site compatibility with NPCBehaviourController.StartQueuedActions().
        /// </summary>
        public void Start() { }

        /// <summary>
        /// Cancels all current and queued states, then immediately enters the given state.
        /// Use for single actions that should override whatever the NPC is doing.
        /// </summary>
        public void Interrupt(INPCState state)
        {
            CancelAll();
            _currentState = state;
            _currentState.EnterState(_ctx);
        }

        /// <summary>
        /// Exits the active state, clears the queue, and resets locomotion to a clean ready state.
        /// </summary>
        public void CancelAll()
        {
            if (_currentState != null)
            {
                _currentState.ExitState(_ctx);
                _currentState = null;
            }

            _queue.Clear();

            if (_ctx.Agent != null && _ctx.Agent.isActiveAndEnabled)
            {
                _ctx.Agent.ResetPath();
                _ctx.Agent.isStopped = false;
            }

            _ctx.Animator?.SetTrigger("StopWalking");
        }

        // ── Internal ──────────────────────────────────────────────────────────────

        private void ActivateNext()
        {
            _currentState = _queue.Dequeue();
            _currentState.EnterState(_ctx);
        }
    }
}

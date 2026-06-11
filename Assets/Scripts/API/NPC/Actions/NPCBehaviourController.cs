using System;
using UnityEngine;
using UnityEngine.AI;
using LAS;

namespace LAS
{
    /// <summary>
    /// Thin MonoBehaviour host for the NPC action FSM.
    /// Owns NPCBehaviourContext (shared data) and NPCFSM (state queue runner).
    /// Forwards context events to its own public events so external subscribers are unaffected.
    /// All action logic lives in FSM state classes; this file only wires things together.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(NPCController))]
    public class NPCBehaviourController : MonoBehaviour
    {
        [SerializeField] private IKController ikController;
        
        [Tooltip("Animator component driving the NPC's animations.")]
        [SerializeField] private Animator animator;

        [Header("Item Slot")]
        [Tooltip("Transform where held objects are parented (e.g. right hand bone).")]
        [SerializeField] private Transform itemSlot;

        [Header("Navigation Settings")]
        [Tooltip("How close the NPC must get to its destination before the GoTo action completes (metres).")]
        [SerializeField] private float arrivalDistance    = 0.5f;
        [Tooltip("How close the NPC must be to an item to pick it up (metres).")]
        [SerializeField] private float pickupRange        = 1.2f;
        [Tooltip("How close two NPCs must be to hand an object between them (metres).")]
        [SerializeField] private float handOverRange      = 1.0f;
        [Tooltip("Seconds before a GoTo action times out if the NPC can't reach its destination.")]
        [SerializeField] private float goToTimeoutSeconds = 15f;
        [Tooltip("Where the NPC stands when idle. Used by ReturnToIdle. Falls back to spawn position if unset.")]
        [SerializeField] private Transform idlePosition;

        [Header("Hand-to-Player Settings")]
        [Tooltip("Whether the NPC physically places the object in the player's hand (DirectPlace) or holds it out for the player to take (OfferForPickup).")]
        [SerializeField] public HandoffMode playerHandoffMode = HandoffMode.OfferForPickup;
        [Tooltip("Position offset from the NPC where the object is held out in OfferForPickup mode (local space).")]
        [SerializeField] private Vector3 offerOffset = new Vector3(0f, 1f, 0.6f);

        // ── Runtime ───────────────────────────────────────────────────────────────

        /// <summary>Shared data container passed to every FSM state.</summary>
        public NPCBehaviourContext Context { get; private set; }

        /// <summary>The NPC's finite state machine — enqueue or interrupt states here.</summary>
        public NPCFSM FSM { get; private set; }

        // ── Public events (same signatures as before — external code unchanged) ───

        /// <summary>Fired when the NPC reaches its navigation destination.</summary>
        public event Action<NPCBehaviourController, Transform> OnArrivedAtTarget;
        /// <summary>Fired when the NPC successfully picks up an object.</summary>
        public event Action<NPCBehaviourController, GameObject> OnPickedUpObject;
        /// <summary>Fired when the NPC hands an object to a player or another NPC.</summary>
        public event Action<NPCBehaviourController, GameObject> OnHandedObject;
        /// <summary>Fired when the NPC finishes returning to its idle position.</summary>
        public event Action<NPCBehaviourController> OnReturnedToIdle;

        // ── Back-compat properties ────────────────────────────────────────────────

        /// <summary>True if the NPC is currently carrying an object.</summary>
        public bool IsHoldingObject  => Context.IsHoldingObject;
        /// <summary>True if the NPC is holding an object out for the player to take (OfferForPickup mode).</summary>
        public bool IsOffering       => Context.IsOffering;
        /// <summary>The object the NPC is currently holding, or null if empty-handed.</summary>
        public GameObject HeldObject => Context.HeldObject;
        /// <summary>True if the FSM has actions queued or currently executing.</summary>
        public bool IsExecutingQueue => FSM.IsRunning;
        /// <summary>True if the NPC's NavMesh agent is actively navigating toward a destination.</summary>
        public bool IsMoving         => Context.Agent != null
                                      && Context.Agent.hasPath
                                      && Context.Agent.remainingDistance > Context.ArrivalDistance;

        // ── Unity lifecycle ───────────────────────────────────────────────────────

        void Awake()
        {
            var agent      = GetComponent<NavMeshAgent>();
            var controller = GetComponent<NPCController>();

            Context = new NPCBehaviourContext(
                host:               this,
                agent:              agent,
                controller:         controller,
                ikController:       ikController,
                animator:           animator,
                itemSlot:           itemSlot,
                idlePosition:       idlePosition,
                arrivalDistance:    arrivalDistance,
                pickupRange:        pickupRange,
                handOverRange:      handOverRange,
                goToTimeoutSeconds: goToTimeoutSeconds,
                offerOffset:        offerOffset,
                playerHandoffMode:  playerHandoffMode
            );

            FSM = new NPCFSM(Context);

            // Forward context events to this MonoBehaviour's public events.
            Context.OnArrivedAtTarget += t   => OnArrivedAtTarget?.Invoke(this, t);
            Context.OnPickedUpObject  += obj => OnPickedUpObject?.Invoke(this, obj);
            Context.OnHandedObject    += obj => OnHandedObject?.Invoke(this, obj);
            Context.OnReturnedToIdle  += ()  => OnReturnedToIdle?.Invoke(this);
        }

        void Update()
        {
            FSM.Update();
        }

        public void AutoSetup(IKController controller)
        {
            ikController = controller;
        }

        // ── Public action API ─────────────────────────────────────────────────────

        /// <summary>Interrupts the current action and navigates to <paramref name="target"/>.</summary>
        public void GoTo(Transform target)
            => FSM.Interrupt(new GoToState(target));

        /// <summary>Interrupts the current action and picks up the object at <paramref name="target"/>.</summary>
        public void PickUp(Transform target)
            => FSM.Interrupt(new PickUpState(target));

        /// <summary>Interrupts the current action and places the held object at <paramref name="target"/>.</summary>
        public void PutDown(Transform target)
            => FSM.Interrupt(new PutDownState(target));

        /// <summary>Interrupts the current action and hands the held object to the player.</summary>
        public void HandToPlayer()
            => FSM.Interrupt(new HandToPlayerState());

        /// <summary>Interrupts the current action and hands the held object to <paramref name="targetNPC"/>.</summary>
        public void HandToNPC(NPCBehaviourController targetNPC)
            => FSM.Interrupt(new HandToNPCState(targetNPC));

        /// <summary>Interrupts the current action and takes the object held by <paramref name="targetNPC"/>.</summary>
        public void GrabFrom(NPCBehaviourController targetNPC)
            => FSM.Interrupt(new GrabFromState(targetNPC));

        /// <summary>Interrupts the current action and requests the object at <paramref name="target"/> (gesture/wait for handoff).</summary>
        public void RequestFrom(Transform target)
            => FSM.Interrupt(new RequestFromState(target));

        /// <summary>Interrupts the current action and turns the NPC's gaze toward <paramref name="target"/>.</summary>
        public void LookAt(Transform target)
            => FSM.Interrupt(new LookAtState(target));

        /// <summary>Interrupts the current action and turns the NPC's gaze toward the player.</summary>
        public void LookAtPlayer()
            => FSM.Interrupt(new LookAtPlayerState());

        /// <summary>Interrupts the current action and sends the NPC back to its idle position.</summary>
        public void ReturnToIdle()
            => FSM.Interrupt(new ReturnToIdleState());

        // ── Queue API ─────────────────────────────────────────────────────────────

        /// <summary>Starts executing the queued states. No-op if already running.</summary>
        public void StartQueuedActions() => FSM.Start();

        /// <summary>Cancels the queue and any running state.</summary>
        public void CancelQueue() => FSM.CancelAll();

        // ── VR interaction hook ───────────────────────────────────────────────────

        /// <summary>
        /// Called by VR interaction systems when the player takes an offered object.
        /// Clears IsOffering so HandToPlayerState (OfferForPickup mode) can complete.
        /// </summary>
        public void TakeOfferedObject()
        {
            if (!Context.IsOffering || Context.HeldObject == null) return;

            var obj = Context.HeldObject;
            Context.HeldObject = null;
            Context.IsOffering = false;

            obj.transform.SetParent(null);

            var rb = obj.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = false;

            var interactable = obj.GetComponent<InteractableItem>();
            if (interactable != null)
            {
                interactable.isHeld          = true;
                interactable.heldByNPC       = "Player";
                interactable.currentLocation = "";
            }

            Context.FireHandedObject(obj);
        }
        
        // ── Helpers ───────────────────────────────────────────────────────────────

        private int GetNPCIndex() => Context.NPCIndex;
    }
}

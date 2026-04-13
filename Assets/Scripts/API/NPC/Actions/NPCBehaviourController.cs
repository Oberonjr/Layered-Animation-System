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
        [SerializeField] private IKLookAt lookAtScript;
        [SerializeField] private Animator animator;

        [Header("Item Slot")]
        [Tooltip("Transform where held objects are parented (e.g. right hand bone).")]
        [SerializeField] private Transform itemSlot;

        [Header("Navigation Settings")]
        [SerializeField] private float arrivalDistance    = 0.5f;
        [SerializeField] private float pickupRange        = 1.2f;
        [SerializeField] private float handOverRange      = 1.0f;
        [SerializeField] private float goToTimeoutSeconds = 15f;
        [SerializeField] private Transform idlePosition;

        [Header("Hand-to-Player Settings")]
        [SerializeField] public HandoffMode playerHandoffMode = HandoffMode.OfferForPickup;
        [SerializeField] private Vector3 offerOffset = new Vector3(0f, 1f, 0.6f);

        // ── Runtime ───────────────────────────────────────────────────────────────

        /// <summary>Shared data container passed to every FSM state.</summary>
        public NPCBehaviourContext Context { get; private set; }

        /// <summary>The NPC's finite state machine — enqueue or interrupt states here.</summary>
        public NPCFSM FSM { get; private set; }

        // ── Public events (same signatures as before — external code unchanged) ───

        public event Action<NPCBehaviourController, Transform> OnArrivedAtTarget;
        public event Action<NPCBehaviourController, GameObject> OnPickedUpObject;
        public event Action<NPCBehaviourController, GameObject> OnHandedObject;
        public event Action<NPCBehaviourController> OnReturnedToIdle;

        // ── Back-compat properties ────────────────────────────────────────────────

        public bool IsHoldingObject  => Context.IsHoldingObject;
        public bool IsOffering       => Context.IsOffering;
        public GameObject HeldObject => Context.HeldObject;
        public bool IsExecutingQueue => FSM.IsRunning;
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
                ikLookAt:           lookAtScript,
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
            Context.LookAt.Tick();
            FSM.Update();
        }

        // ── Public action API ─────────────────────────────────────────────────────

        public void GoTo(Transform target)
            => FSM.Interrupt(new GoToState(target));

        public void PickUp(Transform target)
            => FSM.Interrupt(new PickUpState(target));

        public void PutDown(Transform target)
            => FSM.Interrupt(new PutDownState(target));

        public void HandToPlayer()
            => FSM.Interrupt(new HandToPlayerState());

        public void HandToNPC(NPCBehaviourController targetNPC)
            => FSM.Interrupt(new HandToNPCState(targetNPC));

        public void GrabFrom(NPCBehaviourController targetNPC)
            => FSM.Interrupt(new GrabFromState(targetNPC));

        public void RequestFrom(Transform target)
            => FSM.Interrupt(new RequestFromState(target));

        public void LookAt(Transform target)
            => FSM.Interrupt(new LookAtState(target));

        public void LookAtPlayer()
            => FSM.Interrupt(new LookAtPlayerState());

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

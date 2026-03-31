using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;
using System;
using LAS;

namespace LAS {
    /// <summary>
    /// Determines how the NPC transfers a held object to the player.
    /// </summary>
    public enum HandoffMode
    {
        /// <summary>NPC walks to the player and holds the object out at offerOffset, waiting for TakeOfferedObject().</summary>
        OfferForPickup,
        /// <summary>NPC walks to the player and directly parents the object to the player's transform.</summary>
        DirectTransfer
    }

    /// <summary>
    /// Per-NPC component. Handles all physical behaviour: movement, looking, 
    /// picking up and handing objects. Works alongside NPCController.
    /// </summary>


    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(NPCController))]
    public class NPCBehaviourController : MonoBehaviour
    {
        [SerializeField] private IKLookAt lookAtScript;
        [SerializeField] private Animator animator;

        private Coroutine lookAtCoroutine;
        
        [Header("Item Slot")]
        [Tooltip("The transform where held objects are attached (e.g. right hand bone or empty child). Objects will be parented to this transform and positioned at its local origin when picked up.")]
        [SerializeField] private Transform itemSlot;

        [Header("Look Settings")]
        [Tooltip("How fast the NPC rotates when looking at a target (degrees per second equivalent).")]
        [SerializeField] private float lookRotationSpeed = 5f;

        [Tooltip("The angle threshold (in degrees) at which the NPC is considered 'done' looking at the target. Smaller values = more precise alignment.")]
        [SerializeField] private float lookStopAngleThreshold = 2f;

        [Header("Navigation Settings")]
<<<<<<< HEAD
        [Tooltip("The default distance from the target at which the NPC is considered to have 'arrived' (in Unity units/meters). Used by GO_TO and RETURN_TO_IDLE.")]
        [SerializeField] private float arrivalDistance = 0.5f;
=======
        [Tooltip("The distance from the target at which the NPC is considered to have 'arrived' (in Unity units/meters).")]
        [SerializeField] private float arrivalDistance = 1.5f;
>>>>>>> 4bf6ac1 (Added animated characters to doctor and nurse NPCs)

        [Tooltip("How close the NPC must get before picking up an object. Keep slightly larger than the item's collision radius.")]
        [SerializeField] private float pickupRange = 1.2f;

        [Tooltip("How close the NPC must get before handing an object to a player or another NPC.")]
        [SerializeField] private float handOverRange = 1.0f;

        [Tooltip("Maximum seconds the NPC will try to navigate before giving up. Prevents infinite loops if the NavMesh agent stalls (B8 fix).")]
        [SerializeField] private float goToTimeoutSeconds = 15f;

        [Tooltip("The transform representing this NPC's idle/home position. NPC will return here when ReturnToIdle is called. Leave null if no specific idle position is needed.")]
        [SerializeField] private Transform idlePosition; // Where to return when idle

        [Header("Hand-to-Player Settings")]
        [Tooltip("OfferForPickup: walk to player, hold object at offerOffset, wait for TakeOfferedObject(). DirectTransfer: walk to player and parent object directly to their transform.")]
        [SerializeField] public HandoffMode playerHandoffMode = HandoffMode.OfferForPickup;

        [Tooltip("Local offset from NPC position to hold object out toward player (in meters). Typically forward and up from the NPC center.")]
        [SerializeField] private Vector3 offerOffset = new Vector3(0f, 1f, 0.6f);

        // Runtime state - private fields tracking current behavior
        /// <summary>Reference to the NavMeshAgent component for pathfinding and movement.</summary>
        private NavMeshAgent agent;

        /// <summary>Reference to the NPCController component for accessing NPC identity and state.</summary>
        private NPCController npcController;

        /// <summary>The GameObject currently being held by this NPC, or null if not holding anything.</summary>
        private GameObject heldObject;

        /// <summary>The currently running behavior coroutine, or null if no behavior is active.</summary>
        private Coroutine currentBehaviourCoroutine;

        /// <summary>Whether this NPC is currently offering an object to the player (holding it out).</summary>
        private bool isOffering = false; // Holding object out for player

        /// <summary>Queue of behaviour coroutines for multi-step action sequences (e.g. PICK_UP → HAND_TO_PLAYER).</summary>
        private Queue<IEnumerator> behaviourQueue = new Queue<IEnumerator>();

        /// <summary>The currently running action queue coroutine, or null if not executing a queue.</summary>
        private Coroutine queueCoroutine;

        // Events other systems can listen to
        /// <summary>Fired when the NPC arrives at a navigation target. Parameters: (this NPC, target transform).</summary>
        public event Action<NPCBehaviourController, Transform> OnArrivedAtTarget;

        /// <summary>Fired when the NPC picks up an object. Parameters: (this NPC, picked up GameObject).</summary>
        public event Action<NPCBehaviourController, GameObject> OnPickedUpObject;

        /// <summary>Fired when the NPC successfully hands an object to someone else. Parameters: (this NPC, handed GameObject).</summary>
        public event Action<NPCBehaviourController, GameObject> OnHandedObject;

        /// <summary>Fired when the NPC completes returning to idle position. Parameters: (this NPC).</summary>
        public event Action<NPCBehaviourController> OnReturnedToIdle;

        /// <summary>Gets the GameObject currently being held by this NPC, or null if not holding anything.</summary>
        public GameObject HeldObject => heldObject;

        /// <summary>Returns true if this NPC is currently holding an object.</summary>
        public bool IsHoldingObject => heldObject != null;

        /// <summary>Returns true if this NPC is currently offering an object to the player (holding it out for them to take).</summary>
        public bool IsOffering => isOffering;

        /// <summary>Returns true if this NPC is currently executing a multi-step action queue.</summary>
        public bool IsExecutingQueue => queueCoroutine != null;

        /// <summary>Returns true if this NPC is currently moving (has an active navigation path and hasn't reached the destination yet).</summary>
        public bool IsMoving => agent != null && agent.hasPath && agent.remainingDistance > arrivalDistance;

        /// <summary>
        /// Caches component references on initialization.
        /// </summary>
        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            npcController = GetComponent<NPCController>();
        }

        // ─── Public Behaviour Methods ─────────────────────────────────────────────

        /// <summary>
        /// Makes the NPC look at the player.
        /// Automatically finds a Player-type target from the registry and rotates toward them.
        /// Warns if multiple players are registered (uses first one).
        /// </summary>
        public void LookAtPlayer()
        {
            var registry = NPCActionTargetRegistry.Instance;
            if (registry == null) return;

            // Get all Player-type targets
            var players = registry.GetTargetsByType(TargetType.Player).ToList();

    /// <summary>
    /// Makes the NPC rotate to look at a specific transform.
    /// Uses smooth rotation (slerp) until facing the target within the angle threshold.
    /// Cancels any currently running behavior.
    /// </summary>
    /// <param name="target">The transform to look at.</param>
    public void LookAt(Transform target)
    {
        if (target == null) return;
        SwitchBehaviour(LookAtRoutine(target));
    }

    /// <summary>
    /// Makes the NPC navigate to a target position using NavMesh pathfinding.
    /// Fires OnArrivedAtTarget event when the NPC reaches the destination.
    /// Cancels any currently running behavior.
    /// </summary>
    /// <param name="target">The transform to navigate to.</param>
    public void GoTo(Transform target)
    {
        if (target == null) return;
        SwitchBehaviour(GoToRoutine(target));
    }

    /// <summary>
    /// Makes the NPC walk to an object and pick it up.
    /// The object is parented to the NPC's item slot and physics are disabled.
    /// Fires OnPickedUpObject event when complete.
    /// </summary>
    /// <param name="target">The object to pick up (should be an InteractableObject).</param>
    public void PickUp(Transform target)
    {
        if (target == null) return;
        SwitchBehaviour(PickUpRoutine(target));
    }

    /// <summary>
    /// Walk to an NPC and place held object in their item slot.
    /// </summary>
    public void HandToNPC(NPCBehaviourController targetNPC)
    {
        if (targetNPC == null) return;
        if (!IsHoldingObject)
        {
            Debug.LogWarning($"[{npcController.npcName}] HandToNPC: not holding anything.");
            return;
        }
        SwitchBehaviour(HandToNPCRoutine(targetNPC));
    }

    /// <summary>
    /// Hold the object out in front, waiting for the player to take it.
    /// In VR this is how a player receives an object.
    /// </summary>
    public void HandToPlayer()
    {
        if (!IsHoldingObject)
        {
            Debug.LogWarning($"[{npcController.npcName}] HandToPlayer: not holding anything.");
            return;
        }
        SwitchBehaviour(HandToPlayerRoutine());
    }

    /// <summary>
    /// Walk to a target NPC and take the object they are holding.
    /// The object is transferred from the target's item slot to this NPC's item slot.
    /// Warns if the target is not holding anything.
    /// </summary>
    /// <param name="targetNPC">The NPC to grab the object from.</param>
    public void GrabFrom(NPCBehaviourController targetNPC)
    {
        if (targetNPC == null) return;
        SwitchBehaviour(GrabFromRoutine(targetNPC));
    }

    /// <summary>
    /// Face the target and gesture toward them - signal you want their object.
    /// The target NPC decides whether to hand it over.
    /// </summary>
    public void RequestFrom(Transform target)
    {
        if (target == null) return;
        SwitchBehaviour(RequestFromRoutine(target));
    }

    /// <summary>
    /// Makes the NPC return to their idle/home position (if set) and reset their state.
    /// Clears the navigation path and stops offering any held objects.
    /// Fires OnReturnedToIdle event when complete.
    /// </summary>
    public void ReturnToIdle()
    {
        SwitchBehaviour(ReturnToIdleRoutine());
    }

    // ─── Coroutine Implementations ────────────────────────────────────────────

    /// <summary>
    /// Coroutine that smoothly rotates the NPC to face a target.
    /// Runs each frame until the angle to target is within the threshold.
    /// </summary>
    /// <param name="target">The transform to look at.</param>
    private IEnumerator LookAtRoutine(Transform target)
    {
        while (true)
        {
            Vector3 direction = (target.position - transform.position).normalized;
            direction.y = 0f;

            if (direction == Vector3.zero) yield break;
            
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * lookRotationSpeed);

            if (Quaternion.Angle(transform.rotation, targetRotation) < lookStopAngleThreshold)
                yield break;

            yield return null;
        }
    }

    /// <summary>
    /// Coroutine that navigates the NPC to a target position using NavMeshAgent.
    /// Waits until the NPC reaches the arrival distance, then fires OnArrivedAtTarget event.
    /// </summary>
    /// <param name="target">The transform to navigate to.</param>
    private IEnumerator GoToRoutine(Transform target)
    {
        agent.SetDestination(target.position);

        while (true)
        {
            if (!agent.pathPending && agent.remainingDistance <= arrivalDistance)
            {
                Debug.LogWarning($"[{npcController.npcName}] LookAtPlayer: No Player found in registry.");
                return;
            }

            if (players.Count > 1)
            {
                Debug.LogWarning($"[{npcController.npcName}] LookAtPlayer: Multiple Players registered ({players.Count}). Using first one: '{players[0].TargetName}'");
            }

            LookAt(players[0].Transform);
        }

        /// <summary>
        /// Makes the NPC rotate to look at a specific transform.
        /// Uses smooth rotation (slerp) until facing the target within the angle threshold.
        /// Cancels any currently running behavior.
        /// </summary>
        /// <param name="target">The transform to look at.</param>
        public void LookAt(Transform target)
        {
            if (target == null) return;
            SwitchBehaviour(LookAtRoutine(target));
        }

        /// <summary>
        /// Makes the NPC navigate to a target position using NavMesh pathfinding.
        /// Fires OnArrivedAtTarget event when the NPC reaches the destination.
        /// Cancels any currently running behavior.
        /// </summary>
        /// <param name="target">The transform to navigate to.</param>
        public void GoTo(Transform target)
        {
            if (target == null) return;
            SwitchBehaviour(GoToRoutine(target));
        }

        /// <summary>
        /// Makes the NPC walk to an object and pick it up.
        /// The object is parented to the NPC's item slot and physics are disabled.
        /// Fires OnPickedUpObject event when complete.
        /// </summary>
        /// <param name="target">The object to pick up (should be an InteractableObject).</param>
        public void PickUp(Transform target)
        {
            if (target == null) return;
            SwitchBehaviour(PickUpRoutine(target));
        }

        /// <summary>
        /// Walk to an NPC and place held object in their item slot.
        /// </summary>
        public void HandToNPC(NPCBehaviourController targetNPC)
        {
            if (targetNPC == null) return;
            if (!IsHoldingObject)
            {
                Debug.LogWarning($"[{npcController.npcName}] HandToNPC: not holding anything.");
                return;
            }
            SwitchBehaviour(HandToNPCRoutine(targetNPC));
        }

        /// <summary>
        /// Hold the object out in front, waiting for the player to take it.
        /// In VR this is how a player receives an object.
        /// </summary>
        public void HandToPlayer()
        {
            if (!IsHoldingObject)
            {
                Debug.LogWarning($"[{npcController.npcName}] HandToPlayer: not holding anything.");
                return;
            }
            SwitchBehaviour(HandToPlayerRoutine());
        }

        /// <summary>
        /// Walk to a target NPC and take the object they are holding.
        /// The object is transferred from the target's item slot to this NPC's item slot.
        /// Warns if the target is not holding anything.
        /// </summary>
        /// <param name="targetNPC">The NPC to grab the object from.</param>
        public void GrabFrom(NPCBehaviourController targetNPC)
        {
            if (targetNPC == null) return;
            SwitchBehaviour(GrabFromRoutine(targetNPC));
        }

        /// <summary>
        /// Walk to a LocationTarget and place the held item in its matching ItemSlot.
        /// Fires OnActionImpossible via NPCEventBus if no slot exists for the held item.
        /// </summary>
        public void PutDown(Transform locationTarget)
        {
            if (locationTarget == null) return;
            SwitchBehaviour(PutDownRoutine(locationTarget));
        }

        /// <summary>
        /// Face the target and gesture toward them - signal you want their object.
        /// The target NPC decides whether to hand it over.
        /// </summary>
        public void RequestFrom(Transform target)
        {
            if (target == null) return;
            SwitchBehaviour(RequestFromRoutine(target));
        }

        /// <summary>
        /// Makes the NPC return to their idle/home position (if set) and reset their state.
        /// Clears the navigation path and stops offering any held objects.
        /// Fires OnReturnedToIdle event when complete.
        /// </summary>
        public void ReturnToIdle()
        {
            SwitchBehaviour(ReturnToIdleRoutine());
        }

        // ─── Coroutine Implementations ────────────────────────────────────────────

        /// <summary>
        /// Coroutine that smoothly rotates the NPC to face a target.
        /// Runs each frame until the angle to target is within the threshold.
        /// </summary>
        /// <param name="target">The transform to look at.</param>
        private IEnumerator LookAtRoutine(Transform target)
        {
            while (true)
            {
                lookAtScript.LookAt(target);
                
                /*Vector3 direction = (target.position - transform.position).normalized;
                direction.y = 0f;

                if (direction == Vector3.zero) yield break;

                Quaternion targetRotation = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * lookRotationSpeed);

                if (Quaternion.Angle(transform.rotation, targetRotation) < lookStopAngleThreshold)
                    yield break;*/

                yield return null;
            }
        }

        /// <summary>
        /// Coroutine that navigates the NPC to a target position using NavMeshAgent.
        /// Waits until the NPC reaches the arrival distance, then fires OnArrivedAtTarget event.
        /// </summary>
        /// <param name="target">The transform to navigate to.</param>
        /// <param name="range">Stopping distance override. Pass a positive value to stop closer or
        /// farther than the default arrivalDistance (e.g. pickupRange, handOverRange).</param>
        private IEnumerator GoToRoutine(Transform target, float range = -1f)
        {
<<<<<<< HEAD
            float effectiveRange = range > 0f ? range : arrivalDistance;

            // Ensure the agent's own stoppingDistance doesn't fight our range check.
            agent.stoppingDistance = 0f;
=======
            animator.SetTrigger("StartWalking");
            
            lookAtCoroutine = StartCoroutine(LookAtRoutine(target));
            
>>>>>>> 4bf6ac1 (Added animated characters to doctor and nurse NPCs)
            agent.SetDestination(target.position);

            float startTime = Time.time;

            // Wait one frame — immediately after SetDestination, pathPending may still be false
            // and remainingDistance may be 0, which would trigger a false arrival.
            yield return null;

            while (true)
            {
                if (Time.time - startTime > goToTimeoutSeconds)
                {
                    animator.SetTrigger("StopWalking");
                    Debug.LogWarning($"[{npcController.npcName}] GoTo timed out after {goToTimeoutSeconds}s navigating to '{target.name}'.");
                    agent.ResetPath();
                    StopCoroutine(lookAtCoroutine);
                    yield break;
                }
<<<<<<< HEAD

                // Still calculating path — keep waiting.
                if (agent.pathPending)
                {
                    yield return null;
                    continue;
                }

                // No valid path exists (obstacle, off-mesh, etc.).
                if (agent.pathStatus == NavMeshPathStatus.PathInvalid)
                {
                    Debug.LogWarning($"[{npcController.npcName}] GoTo: no valid NavMesh path to '{target.name}'.");
                    agent.ResetPath();
                    yield break;
                }

                // Arrived within range.
                if (agent.remainingDistance <= effectiveRange)
=======
                
                if (!agent.pathPending && agent.remainingDistance <= arrivalDistance)
>>>>>>> 4bf6ac1 (Added animated characters to doctor and nurse NPCs)
                {
                    Debug.Log(agent.remainingDistance);
                    
                    animator.SetTrigger("StopWalking");
                    agent.ResetPath();
                    OnArrivedAtTarget?.Invoke(this, target);
                    StopCoroutine(lookAtCoroutine);
                    yield break;
                }

                yield return null;
            }
        }

        /// <summary>
        /// Coroutine that walks to an object, picks it up, and configures it for being held.
        /// Disables physics (makes rigidbody kinematic) and parents to item slot.
        /// Fires OnPickedUpObject event when complete.
        /// </summary>
        /// <param name="target">The object to pick up.</param>
        private IEnumerator PickUpRoutine(Transform target)
        {
            if (IsHoldingObject)
            {
                Debug.LogWarning($"[{npcController.npcName}] PickUp: already holding '{heldObject.name}'. Cannot pick up '{target.name}'.");
                int idx = GetNPCIndex();
                NPCEventBus.BroadcastActionImpossible(idx,
                    $"I'm already holding {heldObject.name} — I can't pick up {target.name} at the same time.");
                yield break;
            }

            // Walk to object using pickup-specific range
            yield return GoToRoutine(target, pickupRange);

            // Pick it up
            GameObject obj = target.gameObject;
            heldObject = obj;

            if (itemSlot != null)
            {
                obj.transform.SetParent(itemSlot);
                obj.transform.localPosition = Vector3.zero;
                obj.transform.localRotation = Quaternion.identity;
            }

            // Disable physics while held
            Rigidbody rb = obj.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            var interactable = obj.GetComponent<InteractableItem>();
            if (interactable != null)
            {
                interactable.isHeld = true;
                interactable.heldByNPC = npcController != null ? npcController.npcName : name;
                interactable.currentLocation = "";
            }

            OnPickedUpObject?.Invoke(this, obj);
        }

        /// <summary>
        /// Coroutine that walks to another NPC and transfers the held object to them.
        /// The object is re-parented to the target NPC's item slot.
        /// </summary>
        private IEnumerator HandToNPCRoutine(NPCBehaviourController targetNPC)
        {
            // Walk close enough to hand over
            yield return GoToRoutine(targetNPC.transform, handOverRange);

            // Transfer the object
            if (heldObject != null && targetNPC.itemSlot != null)
            {
                GameObject transferring = heldObject;
                heldObject = null;

                transferring.transform.SetParent(targetNPC.itemSlot);
                transferring.transform.localPosition = Vector3.zero;
                transferring.transform.localRotation = Quaternion.identity;

                targetNPC.heldObject = transferring;

                var interactable = transferring.GetComponent<InteractableItem>();
                if (interactable != null)
                {
                    interactable.isHeld = true;
                    interactable.heldByNPC = targetNPC.npcController != null ? targetNPC.npcController.npcName : targetNPC.name;
                    interactable.currentLocation = "";
                }

                OnHandedObject?.Invoke(this, transferring);
            }
        }

        /// <summary>
        /// Coroutine that positions the held object at the offer offset and waits for the player to take it.
        /// The offering state remains active until TakeOfferedObject() is called or the behavior is cancelled.
        /// </summary>
        private IEnumerator HandToPlayerRoutine()
        {
            var registry = NPCActionTargetRegistry.Instance;
            var players = registry?.GetTargetsByType(TargetType.Player).ToList();
            Transform playerTransform = (players != null && players.Count > 0) ? players[0].Transform : null;

            if (playerTransform != null)
            {
                yield return GoToRoutine(playerTransform, handOverRange);
                yield return LookAtRoutine(playerTransform);
            }

            isOffering = true;

            if (playerHandoffMode == HandoffMode.DirectTransfer)
            {
                // Parent the object directly to the player's transform.
                if (heldObject != null && playerTransform != null)
                {
                    heldObject.transform.SetParent(playerTransform);
                    heldObject.transform.localPosition = Vector3.zero;
                    Rigidbody rb = heldObject.GetComponent<Rigidbody>();
                    if (rb != null) rb.isKinematic = false;
                    
                    var interactable = heldObject.GetComponent<InteractableItem>();
                    if (interactable != null)
                    {
                        interactable.isHeld = true;
                        interactable.heldByNPC = "Player";
                        interactable.currentLocation = "";
                    }

                    OnHandedObject?.Invoke(this, heldObject);
                    heldObject = null;
                }
                isOffering = false;
            }
            else // OfferForPickup (default)
            {
                // Move held object to the offer position and wait for the player to take it.
                if (heldObject != null && itemSlot != null)
                {
                    heldObject.transform.localPosition = offerOffset;
                }

                while (isOffering && heldObject != null)
                {
                    yield return null;
                }

                isOffering = false;
            }
        }

        /// <summary>
        /// Coroutine that walks to another NPC and takes their held object.
        /// Warns if the target NPC is not holding anything.
        /// Fires OnPickedUpObject event when complete.
        /// </summary>
        /// <param name="targetNPC">The NPC to take the object from.</param>
        private IEnumerator GrabFromRoutine(NPCBehaviourController targetNPC)
        {
            yield return GoToRoutine(targetNPC.transform);

            if (targetNPC.IsHoldingObject)
            {
                GameObject grabbed = targetNPC.heldObject;
                targetNPC.heldObject = null;

                heldObject = grabbed;

                if (itemSlot != null)
                {
                    grabbed.transform.SetParent(itemSlot);
                    grabbed.transform.localPosition = Vector3.zero;
                    grabbed.transform.localRotation = Quaternion.identity;
                }

                OnPickedUpObject?.Invoke(this, grabbed);
            }
            else
            {
                Debug.LogWarning($"[{npcController.npcName}] GrabFrom: target has nothing to grab.");
            }
        }

        /// <summary>
        /// Coroutine that faces the target and gestures toward them (requesting their object).
        /// This is a non-forceful action. Currently logs a message; can be extended with animations.
        /// </summary>
        /// <param name="target">The target to request from.</param>
        private IEnumerator RequestFromRoutine(Transform target)
        {
            // Face the target
            yield return LookAtRoutine(target);

            // Gesture (can be replaced with animation trigger later)
            Debug.Log($"[{npcController.npcName}] Requesting object from {target.name}");

            // TODO: trigger "request" animation here
        }

        /// <summary>
        /// Coroutine that navigates the NPC back to their idle position (if set) and resets their state.
        /// Clears the navigation path, stops offering, and fires OnReturnedToIdle event.
        /// </summary>
        private IEnumerator ReturnToIdleRoutine()
        {
            if (idlePosition != null)
            {
                yield return GoToRoutine(idlePosition);
            }

            // Reset look direction and stop all movement
            agent.ResetPath();
            isOffering = false;
            OnReturnedToIdle?.Invoke(this);
        }

        /// <summary>
        /// Coroutine that walks to a LocationTarget and places the held item in the matching ItemSlot.
        /// Fires OnActionImpossible if not holding anything or if the location has no slot for the item.
        /// Fires OnHandedObject on success (item considered "delivered").
        /// </summary>
        private IEnumerator PutDownRoutine(Transform locationTransform)
        {
            if (!IsHoldingObject)
            {
                Debug.LogWarning($"[{npcController.npcName}] PutDown: not holding anything.");
                NPCEventBus.BroadcastActionImpossible(GetNPCIndex(), "I'm not holding anything to put down.");
                yield break;
            }

            var location = locationTransform?.GetComponent<LocationTarget>()
                        ?? locationTransform?.GetComponentInParent<LocationTarget>();
            if (location == null)
            {
                Debug.LogWarning($"[{npcController.npcName}] PutDown: target '{locationTransform?.name}' has no LocationTarget on it or its parents.");
                NPCEventBus.BroadcastActionImpossible(GetNPCIndex(), $"I can't place things at {locationTransform?.name}.");
                yield break;
            }

            var item = heldObject.GetComponent<InteractableItem>();
            var slot = item != null ? location.GetSlotForItem(item) : null;

            if (slot == null)
            {
                string itemName = item != null ? item.TargetName : heldObject.name;
                Debug.LogWarning($"[{npcController.npcName}] PutDown: '{location.TargetName}' has no slot for '{itemName}'.");
                NPCEventBus.BroadcastActionImpossible(GetNPCIndex(),
                    $"There's no designated spot for {itemName} at {location.TargetName}.");
                yield break;
            }

            // Navigate to the location
            yield return GoToRoutine(locationTransform, pickupRange);

            // Place item in slot
            var placing = heldObject;
            heldObject = null;

            if (slot.TryPlaceItem(item))
            {
                location.AddItem(item.TargetName);
                OnHandedObject?.Invoke(this, placing);
                Debug.Log($"[{npcController.npcName}] Placed '{item.TargetName}' at '{location.TargetName}'.");
            }
            else
            {
                // Slot rejected the item (e.g. became occupied between navigation and arrival)
                heldObject = placing; // reclaim
                NPCEventBus.BroadcastActionImpossible(GetNPCIndex(),
                    $"The slot at {location.TargetName} is already occupied.");
            }
        }

        /// <summary>Returns this NPC's index in NPCManager's registered list (via NPCController.AssignedIndex).</summary>
        private int GetNPCIndex() => npcController != null ? npcController.AssignedIndex : -1;

        // ─── Helper: called by player XR interaction to take offered object ────────

        /// <summary>
        /// Called by VR interaction systems when the player takes an offered object.
        /// Unparents the object, re-enables physics, and fires OnHandedObject event.
        /// This completes the HandToPlayer behavior.
        /// </summary>
        public void TakeOfferedObject()
        {
            if (!isOffering || heldObject == null) return;

            heldObject.transform.SetParent(null);
            Rigidbody rb = heldObject.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = false;
            
            var interactable = heldObject.GetComponent<InteractableItem>();
            if (interactable != null)
            {
                interactable.isHeld = true;
                interactable.heldByNPC = "Player";
                interactable.currentLocation = "";
            }

            OnHandedObject?.Invoke(this, heldObject);
            heldObject = null;
            isOffering = false;
        }

        // ─── Internal ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Stops the current behavior coroutine (if any) and starts a new one.
        /// Also cancels any running action queue to ensure clean state.
        /// </summary>
        /// <param name="newBehaviour">The new behavior coroutine to start.</param>
        private void SwitchBehaviour(IEnumerator newBehaviour)
        {
            if (queueCoroutine != null)
            {
                StopCoroutine(queueCoroutine);
                queueCoroutine = null;
                behaviourQueue.Clear();
            }

            if (currentBehaviourCoroutine != null)
                StopCoroutine(currentBehaviourCoroutine);

            currentBehaviourCoroutine = StartCoroutine(newBehaviour);
        }

        // ─── Action Queue (public API) ────────────────────────────────────────────

        /// <summary>
        /// Appends one step to the behaviour queue by mapping an action key to its coroutine.
        /// Returns true if the step was successfully queued, false if the key is unknown or
        /// a required target is missing.
        /// Call StartQueuedActions() after all steps have been enqueued.
        /// </summary>
        public bool TryEnqueueAction(string actionKey, Transform primary, Transform secondary)
        {
            IEnumerator routine = null;

            switch (actionKey)
            {
                case "GO_TO":
                    if (primary != null) routine = GoToRoutine(primary);
                    break;

                case "PICK_UP":
                    if (primary != null) routine = PickUpRoutine(primary);
                    break;

                case "LOOK_AT":
                    if (primary != null) routine = LookAtRoutine(primary);
                    break;

                case "LOOK_AT_PLAYER":
                    routine = LookAtPlayerCoroutine();
                    break;

                case "HAND_TO_PLAYER":
                    routine = HandToPlayerRoutine();
                    break;

                case "HAND_TO_NPC":
                {
                    // primary may be an item (if given) or the target NPC; secondary is the NPC
                    Transform npcTransform = secondary
                        ?? (primary?.GetComponent<NPCBehaviourController>() != null ? primary : null);
                    var targetNPC = npcTransform?.GetComponent<NPCBehaviourController>();
                    if (targetNPC != null) routine = HandToNPCRoutine(targetNPC);
                    break;
                }

                case "GRAB_FROM":
                {
                    var grabFrom = primary?.GetComponent<NPCBehaviourController>();
                    if (grabFrom != null) routine = GrabFromRoutine(grabFrom);
                    break;
                }

                case "REQUEST_FROM":
                    if (primary != null) routine = RequestFromRoutine(primary);
                    break;

                case "PUT_DOWN":
                    if (primary != null) routine = PutDownRoutine(primary);
                    break;

                case "RETURN_TO_IDLE":
                    routine = ReturnToIdleRoutine();
                    break;
            }

            if (routine == null)
            {
                Debug.LogWarning($"[{npcController.npcName}] TryEnqueueAction: could not queue '{actionKey}'" +
                    (primary == null ? " (primary target is null)" : ""));
                return false;
            }

            behaviourQueue.Enqueue(routine);
            return true;
        }

        /// <summary>
        /// Starts executing the current behaviourQueue.
        /// Cancels any running single behaviour or previous queue first.
        /// No-op if the queue is empty.
        /// </summary>
        public void StartQueuedActions()
        {
            if (behaviourQueue.Count > 0)
                ExecuteQueue();
        }

        /// <summary>
        /// Coroutine version of LookAtPlayer — usable inside the action queue.
        /// Finds the first registered Player-type target and smooth-rotates toward it.
        /// </summary>
        private IEnumerator LookAtPlayerCoroutine()
        {
            var registry = NPCActionTargetRegistry.Instance;
            if (registry == null) yield break;

            var players = registry.GetTargetsByType(TargetType.Player).ToList();
            if (players.Count == 0)
            {
                Debug.LogWarning($"[{npcController.npcName}] LookAtPlayerCoroutine: No Player found in registry.");
                yield break;
            }

            yield return LookAtRoutine(players[0].Transform);
        }

        /// <summary>
        /// Cancels the running action queue and any current single behaviour coroutine.
        /// </summary>
        public void CancelQueue()
        {
            behaviourQueue.Clear();
            if (queueCoroutine != null)
            {
                StopCoroutine(queueCoroutine);
                queueCoroutine = null;
            }
            if (currentBehaviourCoroutine != null)
            {
                StopCoroutine(currentBehaviourCoroutine);
                currentBehaviourCoroutine = null;
            }
        }

        /// <summary>
        /// Starts executing the current behaviourQueue sequentially.
        /// Cancels any running single behaviour or previous queue first.
        /// </summary>
        private void ExecuteQueue()
        {
            if (currentBehaviourCoroutine != null)
            {
                StopCoroutine(currentBehaviourCoroutine);
                currentBehaviourCoroutine = null;
            }
            if (queueCoroutine != null)
                StopCoroutine(queueCoroutine);

            queueCoroutine = StartCoroutine(ExecuteQueueRoutine());
        }

        /// <summary>
        /// Runs each queued behaviour step in order, waiting for each to complete before starting the next.
        /// </summary>
        private IEnumerator ExecuteQueueRoutine()
        {
            while (behaviourQueue.Count > 0)
            {
                currentBehaviourCoroutine = StartCoroutine(behaviourQueue.Dequeue());
                yield return currentBehaviourCoroutine;
            }
            queueCoroutine = null;
            currentBehaviourCoroutine = null;
        }

        /// <summary>
        /// Plans and executes a multi-step action sequence based on the LLM response and current NPC state.
        /// Automatically adds prerequisite steps (e.g. PICK_UP before HAND_TO_PLAYER if not already holding).
        /// For single-step actions, the caller should use NPCActionDispatcher instead.
        /// </summary>
        /// <param name="playerRequest">Original player request text, used for debug logging.</param>
        /// <param name="llmResponse">The combined LLM response containing action fields.</param>
        public void PlanAndExecuteTask(string playerRequest, NPCResponse llmResponse)
        {
            if (llmResponse == null || string.IsNullOrEmpty(llmResponse.action_key) || llmResponse.action_key == "NONE")
                return;

            string actionKey = llmResponse.action_key;
            var registry = NPCActionTargetRegistry.Instance;

            Transform primary = string.IsNullOrEmpty(llmResponse.action_target) ? null
                : registry?.Resolve(llmResponse.action_target);
            Transform secondary = string.IsNullOrEmpty(llmResponse.action_secondary_target) ? null
                : registry?.Resolve(llmResponse.action_secondary_target);

            behaviourQueue.Clear();

            switch (actionKey)
            {
                case "HAND_TO_PLAYER":
                {
                    bool needsPickup = !IsHoldingObject && primary != null;
                    if (needsPickup)
                        behaviourQueue.Enqueue(PickUpRoutine(primary));
                    behaviourQueue.Enqueue(HandToPlayerRoutine());
                    Debug.Log($"[{npcController.npcName}] Queue planned: {(needsPickup ? "PICK_UP → " : "")}HAND_TO_PLAYER (request: \"{playerRequest}\")" );
                    ExecuteQueue();
                    break;
                }

                case "HAND_TO_NPC":
                {
                    var targetNPC = secondary?.GetComponent<NPCBehaviourController>()
                                 ?? primary?.GetComponent<NPCBehaviourController>();
                    bool isItemTarget = primary != null && primary.GetComponent<NPCBehaviourController>() == null;
                    bool needsPickup = !IsHoldingObject && isItemTarget;
                    if (needsPickup)
                        behaviourQueue.Enqueue(PickUpRoutine(primary));
                    if (targetNPC != null)
                        behaviourQueue.Enqueue(HandToNPCRoutine(targetNPC));
                    Debug.Log($"[{npcController.npcName}] Queue planned: {(needsPickup ? "PICK_UP → " : "")}HAND_TO_NPC");
                    ExecuteQueue();
                    break;
                }

                default:
                    Debug.LogWarning($"[{npcController.npcName}] PlanAndExecuteTask: '{actionKey}' is single-step — use NPCActionDispatcher instead.");
                    break;
            }
        }
    }

}
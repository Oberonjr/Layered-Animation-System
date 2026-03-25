using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System;

/// <summary>
/// Per-NPC component. Handles all physical behaviour: movement, looking, 
/// picking up and handing objects. Works alongside NPCController.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(NPCController))]
public class NPCBehaviourController : MonoBehaviour
{
    [Header("Item Slot")]
    [Tooltip("The transform where held objects are attached (e.g. right hand bone or empty child). Objects will be parented to this transform and positioned at its local origin when picked up.")]
    [SerializeField] private Transform itemSlot;

    [Header("Look Settings")]
    [Tooltip("How fast the NPC rotates when looking at a target (degrees per second equivalent).")]
    [SerializeField] private float lookRotationSpeed = 5f;

    [Tooltip("The angle threshold (in degrees) at which the NPC is considered 'done' looking at the target. Smaller values = more precise alignment.")]
    [SerializeField] private float lookStopAngleThreshold = 2f;

    [Header("Navigation Settings")]
    [Tooltip("The distance from the target at which the NPC is considered to have 'arrived' (in Unity units/meters).")]
    [SerializeField] private float arrivalDistance = 0.5f;

    [Tooltip("The transform representing this NPC's idle/home position. NPC will return here when ReturnToIdle is called. Leave null if no specific idle position is needed.")]
    [SerializeField] private Transform idlePosition; // Where to return when idle

    [Header("Hand-to-Player Settings")]
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

        if (players.Count == 0)
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
                agent.ResetPath();
                OnArrivedAtTarget?.Invoke(this, target);
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
        // Walk to object
        yield return GoToRoutine(target);

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

        OnPickedUpObject?.Invoke(this, obj);
    }

    /// <summary>
    /// Coroutine that walks to another NPC and transfers the held object to them.
    /// The object is re-parented to the target NPC's item slot.
    /// Fires OnHandedObject event when complete.
    /// </summary>
    /// <param name="targetNPC">The NPC to hand the object to.</param>
    private IEnumerator HandToNPCRoutine(NPCBehaviourController targetNPC)
    {
        // Walk to the target NPC
        yield return GoToRoutine(targetNPC.transform);

        // Transfer the object
        if (heldObject != null && targetNPC.itemSlot != null)
        {
            GameObject transferring = heldObject;
            heldObject = null;

            transferring.transform.SetParent(targetNPC.itemSlot);
            transferring.transform.localPosition = Vector3.zero;
            transferring.transform.localRotation = Quaternion.identity;

            targetNPC.heldObject = transferring;

            OnHandedObject?.Invoke(this, transferring);
        }
    }

    /// <summary>
    /// Coroutine that positions the held object at the offer offset and waits for the player to take it.
    /// The offering state remains active until TakeOfferedObject() is called or the behavior is cancelled.
    /// </summary>
    private IEnumerator HandToPlayerRoutine()
    {
        isOffering = true;

        // Move held object to offer position
        if (heldObject != null && itemSlot != null)
        {
            heldObject.transform.localPosition = offerOffset;
        }

        // Wait for player to take it (detected externally via TakeOfferedObject)
        // or for the behaviour to be cancelled
        while (isOffering && heldObject != null)
        {
            yield return null;
        }

        isOffering = false;
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

        OnHandedObject?.Invoke(this, heldObject);
        heldObject = null;
        isOffering = false;
    }

    // ─── Internal ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Stops the current behavior coroutine (if any) and starts a new one.
    /// Ensures only one behavior runs at a time.
    /// </summary>
    /// <param name="newBehaviour">The new behavior coroutine to start.</param>
    private void SwitchBehaviour(IEnumerator newBehaviour)
    {
        if (currentBehaviourCoroutine != null)
            StopCoroutine(currentBehaviourCoroutine);

        currentBehaviourCoroutine = StartCoroutine(newBehaviour);
    }
}
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
    [Tooltip("The transform where held objects are attached (e.g. right hand bone or empty child).")]
    [SerializeField] private Transform itemSlot;

    [Header("Look Settings")]
    [SerializeField] private float lookRotationSpeed = 5f;
    [SerializeField] private float lookStopAngleThreshold = 2f;

    [Header("Navigation Settings")]
    [SerializeField] private float arrivalDistance = 0.5f;
    [SerializeField] private Transform idlePosition; // Where to return when idle

    [Header("Hand-to-Player Settings")]
    [Tooltip("Offset from NPC position to hold object out toward player.")]
    [SerializeField] private Vector3 offerOffset = new Vector3(0f, 1f, 0.6f);

    // Runtime state
    private NavMeshAgent agent;
    private NPCController npcController;
    private GameObject heldObject;
    private Coroutine currentBehaviourCoroutine;
    private bool isOffering = false; // Holding object out for player

    // Events other systems can listen to
    public event Action<NPCBehaviourController, Transform> OnArrivedAtTarget;
    public event Action<NPCBehaviourController, GameObject> OnPickedUpObject;
    public event Action<NPCBehaviourController, GameObject> OnHandedObject;
    public event Action<NPCBehaviourController> OnReturnedToIdle;

    public GameObject HeldObject => heldObject;
    public bool IsHoldingObject => heldObject != null;
    public bool IsOffering => isOffering;
    public bool IsMoving => agent != null && agent.hasPath && agent.remainingDistance > arrivalDistance;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        npcController = GetComponent<NPCController>();
    }

    // ─── Public Behaviour Methods ─────────────────────────────────────────────

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

    public void LookAt(Transform target)
    {
        if (target == null) return;
        SwitchBehaviour(LookAtRoutine(target));
    }

    public void GoTo(Transform target)
    {
        if (target == null) return;
        SwitchBehaviour(GoToRoutine(target));
    }

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
    /// Walk to a target and take the object they are holding.
    /// </summary>
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

    public void ReturnToIdle()
    {
        SwitchBehaviour(ReturnToIdleRoutine());
    }

    // ─── Coroutine Implementations ────────────────────────────────────────────

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

    private IEnumerator RequestFromRoutine(Transform target)
    {
        // Face the target
        yield return LookAtRoutine(target);

        // Gesture (can be replaced with animation trigger later)
        Debug.Log($"[{npcController.npcName}] Requesting object from {target.name}");

        // TODO: trigger "request" animation here
    }

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

    private void SwitchBehaviour(IEnumerator newBehaviour)
    {
        if (currentBehaviourCoroutine != null)
            StopCoroutine(currentBehaviourCoroutine);

        currentBehaviourCoroutine = StartCoroutine(newBehaviour);
    }
}
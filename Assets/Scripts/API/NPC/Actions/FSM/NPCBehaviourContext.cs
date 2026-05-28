using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace LAS
{
    /// <summary>
    /// Determines how the NPC hands an object to the player.
    /// Moved here from NPCBehaviourController so FSM states can reference it without a MonoBehaviour dependency.
    /// </summary>
    public enum HandoffMode
    {
        /// <summary>NPC walks to the player and holds the object out at OfferOffset, waiting for TakeOfferedObject().</summary>
        OfferForPickup,
        /// <summary>NPC walks to the player and parents the object directly to the player's transform.</summary>
        DirectTransfer
    }

    /// <summary>
    /// Shared data container passed to every FSM state.
    /// Holds component references, inspector settings, and mutable runtime state.
    /// States read and write this; NPCBehaviourController owns and initialises it.
    /// </summary>
    public class NPCBehaviourContext
    {
        // ── Component references ──────────────────────────────────────────────────

        /// <summary>Unity host MonoBehaviour — needed by states to start sub-coroutines.</summary>
        public readonly MonoBehaviour Host;
        public readonly NavMeshAgent Agent;
        public readonly Animator Animator;
        public readonly NPCController Controller;
        public readonly IKController IKController;

        /// <summary>Transform where held objects are parented (e.g. right hand bone).</summary>
        public readonly Transform ItemSlot;

        /// <summary>The NPC's home/idle position. May be null.</summary>
        public readonly Transform IdlePosition;

        // ── Settings (copied from inspector at construction) ──────────────────────

        public readonly float ArrivalDistance;
        public readonly float PickupRange;
        public readonly float HandOverRange;
        public readonly float GoToTimeoutSeconds;
        public readonly Vector3 OfferOffset;
        public readonly HandoffMode PlayerHandoffMode;

        // ── Mutable runtime state ─────────────────────────────────────────────────

        /// <summary>The GameObject currently held by this NPC, or null.</summary>
        public GameObject HeldObject { get; set; }

        /// <summary>True while the NPC is holding an object out for the player to take.</summary>
        public bool IsOffering { get; set; }

        // ── Computed helpers ──────────────────────────────────────────────────────

        public bool IsHoldingObject => HeldObject != null;
        public int NPCIndex => Controller?.AssignedIndex ?? -1;
        public string NPCName => Controller?.npcName ?? "NPC";

        // ── Events (forwarded by NPCBehaviourController to its own public events) ──

        public event Action<Transform> OnArrivedAtTarget;
        public event Action<GameObject> OnPickedUpObject;
        public event Action<GameObject> OnHandedObject;
        public event Action OnReturnedToIdle;

        public void FireArrivedAtTarget(Transform t) => OnArrivedAtTarget?.Invoke(t);
        public void FirePickedUp(GameObject obj)      => OnPickedUpObject?.Invoke(obj);
        public void FireHandedObject(GameObject obj)  => OnHandedObject?.Invoke(obj);
        public void FireReturnedToIdle()              => OnReturnedToIdle?.Invoke();

        // ── Scene queries ─────────────────────────────────────────────────────────

        /// <summary>Returns the first registered Player transform, or null if none found.</summary>
        public Transform GetPlayerTransform()
        {
            var players = NPCActionTargetRegistry.Instance
                ?.GetTargetsByType(TargetType.Player)
                .ToList();
            return players != null && players.Count > 0 ? players[0].Transform : null;
        }

        // ── Constructor ───────────────────────────────────────────────────────────

        public NPCBehaviourContext(
            MonoBehaviour host,
            NavMeshAgent agent,
            NPCController controller,
            IKController ikController,
            Animator animator,
            Transform itemSlot,
            Transform idlePosition,
            float arrivalDistance,
            float pickupRange,
            float handOverRange,
            float goToTimeoutSeconds,
            Vector3 offerOffset,
            HandoffMode playerHandoffMode)
        {
            Host               = host;
            Agent              = agent;
            Controller         = controller;
            IKController       = ikController;
            Animator           = animator;
            ItemSlot           = itemSlot;
            IdlePosition       = idlePosition;
            ArrivalDistance    = arrivalDistance;
            PickupRange        = pickupRange;
            HandOverRange      = handOverRange;
            GoToTimeoutSeconds = goToTimeoutSeconds;
            OfferOffset        = offerOffset;
            PlayerHandoffMode  = playerHandoffMode;
        }
    }
}

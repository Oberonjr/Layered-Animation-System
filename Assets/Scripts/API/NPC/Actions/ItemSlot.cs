using System;
using UnityEngine;

namespace LAS
{
    /// <summary>
    /// A designated placement slot for a specific InteractableItem at a LocationTarget.
    ///
    /// Features:
    ///   • Sphere trigger collider (adjustable radius) for proximity detection.
    ///   • Ghost preview mesh rendered with a highlight material so players can see
    ///     where the item belongs even before it is placed.
    ///   • Snaps any incoming item to a configured local position/rotation.
    ///
    /// Items can be placed via:
    ///   • NPC PUT_DOWN action (NPCBehaviourController.PutDownRoutine).
    ///   • Player interact input (call TryAcceptItem from your input controller).
    ///   • VR trigger release (call TryAcceptItem when the controller releases).
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public class ItemSlot : MonoBehaviour
    {
        [Header("Slot Configuration")]
        [Tooltip("The item this slot accepts. Leave null to accept any InteractableItem.")]
        public InteractableItem acceptedItem;

        [Tooltip("Trigger radius — items or players within this sphere can interact with the slot.")]
        [SerializeField] private float triggerRadius = 0.3f;

        [Header("Preview")]
        [Tooltip("Material applied to the ghost preview mesh. Use a semi-transparent or highlight shader.")]
        [SerializeField] private Material previewMaterial;

        [Header("Snap Transform")]
        [Tooltip("Local position where the accepted item is placed relative to this slot.")]
        [SerializeField] private Vector3 snapLocalPosition = Vector3.zero;

        [Tooltip("Local rotation (Euler) where the accepted item is placed relative to this slot.")]
        [SerializeField] private Vector3 snapLocalEuler = Vector3.zero;

        // ── Runtime state ─────────────────────────────────────────────────────────

        private SphereCollider triggerCollider;
        private GameObject previewInstance;
        private InteractableItem placedItem;

        public bool IsOccupied   => placedItem != null;
        public InteractableItem PlacedItem => placedItem;

        /// <summary>Fired when an item is successfully placed in this slot.</summary>
        public event Action<ItemSlot, InteractableItem> OnItemPlaced;

        /// <summary>Fired when the placed item is removed from this slot.</summary>
        public event Action<ItemSlot, InteractableItem> OnItemRemoved;

        // ── Unity lifecycle ───────────────────────────────────────────────────────

        void Awake()
        {
            triggerCollider = GetComponent<SphereCollider>();
            triggerCollider.isTrigger = true;
            triggerCollider.radius = triggerRadius;
        }

        void Start()
        {
            RebuildPreview();
        }

        void Update()
        {
            // Show/hide the preview based on whether the accepted item is within range.
            // Distance-based check avoids requiring a collider on the item itself.
            if (previewInstance == null || IsOccupied || acceptedItem == null) return;

            bool inRange = Vector3.Distance(transform.position, acceptedItem.transform.position) <= triggerRadius;
            if (previewInstance.activeSelf != inRange)
                previewInstance.SetActive(inRange);
        }

        void OnValidate()
        {
            // Keep collider radius in sync while editing in the Inspector.
            var col = GetComponent<SphereCollider>();
            if (col != null) col.radius = triggerRadius;
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns true if this slot is configured to accept the given item
        /// (either matches acceptedItem, or acceptedItem is null meaning any item is OK).
        /// </summary>
        public bool Accepts(InteractableItem item) =>
            item != null && !IsOccupied && (acceptedItem == null || item == acceptedItem);

        /// <summary>
        /// Attempts to place an item in this slot.
        /// Snaps the item to the configured position/rotation, freezes its physics,
        /// updates InteractableItem state, and hides the preview.
        /// Returns true on success.
        /// </summary>
        public bool TryPlaceItem(InteractableItem item)
        {
            if (!Accepts(item)) return false;

            placedItem = item;

            // Snap to slot transform
            item.transform.SetParent(transform, true);
            item.transform.localPosition = snapLocalPosition;
            item.transform.localRotation = Quaternion.Euler(snapLocalEuler);

            // Freeze physics
            var rb = item.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            // Update item state
            item.isHeld = false;
            item.heldByNPC = "";
            item.currentLocation = GetComponentInParent<LocationTarget>()?.TargetName ?? "";

            if (previewInstance != null) previewInstance.SetActive(false);

            OnItemPlaced?.Invoke(this, item);
            return true;
        }

        /// <summary>
        /// Removes the currently placed item, restores its physics, and shows the preview again.
        /// Returns the removed item, or null if the slot was empty.
        /// </summary>
        public InteractableItem RemoveItem()
        {
            if (placedItem == null) return null;

            var removed = placedItem;
            placedItem = null;

            removed.transform.SetParent(null, true);

            var rb = removed.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = false;

            removed.currentLocation = "";

            // Don't force-show the preview here — Update() will show it once the item re-enters range.

            OnItemRemoved?.Invoke(this, removed);
            return removed;
        }

        /// <summary>
        /// If an item is within the trigger zone and matches this slot, place it.
        /// Call this from a player interact input handler or VR trigger-release handler.
        /// </summary>
        public bool TryAcceptItem(InteractableItem item)
        {
            if (item == null || !Accepts(item)) return false;
            return TryPlaceItem(item);
        }

        // ── Preview ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the ghost preview mesh from the accepted item's first MeshFilter.
        /// Safe to call at edit time (used by the editor refresh button).
        /// </summary>
        public void RebuildPreview()
        {
            if (previewInstance != null)
            {
                DestroyImmediate(previewInstance);
                previewInstance = null;
            }

            if (acceptedItem == null || previewMaterial == null) return;

            var sourceMF = acceptedItem.GetComponentInChildren<MeshFilter>();
            if (sourceMF == null) return;

            previewInstance = new GameObject("__SlotPreview__") { hideFlags = HideFlags.DontSave };
            previewInstance.transform.SetParent(transform, false);
            previewInstance.transform.localPosition = snapLocalPosition;
            previewInstance.transform.localRotation = Quaternion.Euler(snapLocalEuler);

            // Match the world scale of the source mesh GO, expressed in this slot's local space.
            // Using lossyScale handles items nested inside parents with non-unit scale.
            Vector3 itemWorldScale   = sourceMF.transform.lossyScale;
            Vector3 slotWorldScale   = transform.lossyScale;
            previewInstance.transform.localScale = new Vector3(
                slotWorldScale.x != 0f ? itemWorldScale.x / slotWorldScale.x : 1f,
                slotWorldScale.y != 0f ? itemWorldScale.y / slotWorldScale.y : 1f,
                slotWorldScale.z != 0f ? itemWorldScale.z / slotWorldScale.z : 1f);

            previewInstance.AddComponent<MeshFilter>().sharedMesh      = sourceMF.sharedMesh;
            previewInstance.AddComponent<MeshRenderer>().sharedMaterial = previewMaterial;

            // Start hidden — Update() will show it when the item enters proximity.
            previewInstance.SetActive(false);
        }
    }
}

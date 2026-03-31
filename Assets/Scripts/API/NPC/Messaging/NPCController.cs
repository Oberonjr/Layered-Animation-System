using System.Collections.Generic;
using UnityEngine;
using LAS;
namespace LAS {
    /// <summary>
    /// Core NPC component managing identity, visual state, and event bus registration.
    /// Implements IActionTarget to make the NPC targetable by the action system.
    /// Works alongside NPCBehaviourController for physical actions.
    /// Character data (name, role, description) is assigned by NPCManager from scenario config.
    /// </summary>

    public class NPCController : MonoBehaviour
    {
        [Header("NPC Identity")]
        [Tooltip("The display name of this NPC. Assigned from scenario configuration at runtime.")]
        public string npcName = "NPC";

        [Tooltip("Optional aliases players might use to refer to this NPC (e.g. 'doc', 'the doctor', 'Dr. C'). " +
                 "These are forwarded to the NPCTarget component at runtime and registered alongside the character name. " +
                 "The LLM will also generate additional aliases automatically — add your own here for names that are especially important to recognize.")]
        public List<string> npcAliases = new List<string>();

        [Tooltip("The role/occupation of this character (e.g., 'Mechanic', 'Instructor'). Assigned from scenario configuration.")]
        public string characterRole = ""; // Assigned from scenario

        [Tooltip("Full character description including personality, background, and communication style. Assigned from scenario configuration and used in LLM prompts.")]
        [TextArea(3, 6)]
        public string characterDescription = ""; // Assigned from scenario

        [Header("Visual Indicator")]
        [Tooltip("The SkinnedMeshRenderer on a child object to add the speaking highlight overlay to. Auto-found in children if left empty.")]
        [SerializeField] private SkinnedMeshRenderer speakerRenderer;

        [Tooltip("Highlight color shown when this NPC is speaking.")]
        [SerializeField] private Color highlightColor = Color.green;

        [Tooltip("Minimum overlay alpha during the pulse (bottom of the wave).")]
        [SerializeField] [Range(0f, 1f)] private float minAlpha = 0f;

        [Tooltip("Maximum overlay alpha during the pulse (top of the wave).")]
        [SerializeField] [Range(0f, 1f)] private float maxAlpha = 0.35f;

        [Tooltip("Speed of the pulse effect while speaking (higher = faster).")]
        [SerializeField] private float pulseSpeed = 2f;

        [Header("Runtime Info")]
        [Tooltip("This NPC's index in the NPCManager's registered list. Assigned automatically by NPCManager.")]
        [SerializeField] private int assignedIndex = -1;

        /// <summary>Overlay material instance added to the mesh when speaking. Shader drives the pulse via _Time.</summary>
        private Material _overlayMaterial;

        /// <summary>Gets the index assigned to this NPC by NPCManager.</summary>
        public int AssignedIndex => assignedIndex;

        /// <summary>Gets the highlight color used when this NPC is speaking.</summary>
        public Color HighlightColor => highlightColor;

        /// <summary>
        /// Creates the overlay material and appends it to the skinned mesh renderer's materials array.
        /// The overlay starts inactive (_Active = 0) so it is invisible until SetSpeaking(true) is called.
        /// </summary>
        void Awake()
        {
            if (speakerRenderer == null)
                speakerRenderer = GetComponentInChildren<SkinnedMeshRenderer>();

            if (speakerRenderer != null)
            {
                var shader = Shader.Find("LAS/NPCSpeakingHighlight");
                if (shader == null)
                {
                    Debug.LogWarning("[NPCController] LAS/NPCSpeakingHighlight shader not found — speaking highlight disabled.");
                }
                else
                {
                    _overlayMaterial = new Material(shader);
                    _overlayMaterial.SetColor("_Color", highlightColor);
                    _overlayMaterial.SetFloat("_MinAlpha", minAlpha);
                    _overlayMaterial.SetFloat("_MaxAlpha", maxAlpha);
                    _overlayMaterial.SetFloat("_PulseSpeed", pulseSpeed);
                    _overlayMaterial.SetFloat("_Active", 0f);

                    var mats = speakerRenderer.sharedMaterials;
                    var newMats = new Material[mats.Length + 1];
                    mats.CopyTo(newMats, 0);
                    newMats[newMats.Length - 1] = _overlayMaterial;
                    speakerRenderer.materials = newMats;

                    // .materials setter instances all entries — read back the live reference
                    // so SetFloat calls in SetSpeaking() affect the renderer's actual instance.
                    _overlayMaterial = speakerRenderer.materials[newMats.Length - 1];
                }
            }

            // Ensure NPCTarget is present — it handles registration with the action target registry.
            if (GetComponent<NPCTarget>() == null)
            {
                var npcTarget = gameObject.AddComponent<NPCTarget>();
                if (npcAliases.Count > 0)
                    npcTarget.aliases.AddRange(npcAliases);
            }
        }

        /// <summary>
        /// Registers this NPC with the event bus.
        /// Action target registration is handled automatically by the NPCTarget component.
        /// </summary>
        void Start()
        {
            NPCEventBus.RegisterNPC(this);
        }

        /// <summary>
        /// Cleans up event bus subscription and destroys the overlay material instance.
        /// </summary>
        void OnDestroy()
        {
            NPCEventBus.UnregisterNPC(this);
            if (_overlayMaterial != null)
                Destroy(_overlayMaterial);
        }

        /// <summary>
        /// Assigns this NPC's index in the NPCManager's registered NPC list.
        /// Called by NPCManager during initialization.
        /// </summary>
        public void AssignIndex(int index)
        {
            assignedIndex = index;
            Debug.Log($"{gameObject.name} assigned as NPC {index}: {npcName}");
        }

        /// <summary>
        /// Assigns character data from the scenario configuration to this NPC.
        /// Called by NPCManager during initialization. Updates the target registry if already registered.
        /// </summary>
        public void AssignCharacterData(string name, string role, string description)
        {
            var target = GetComponent<NPCTarget>();

            if (target != null)
                NPCActionTargetRegistry.Instance?.Unregister(target);

            npcName = name;
            characterRole = role;
            characterDescription = description;

            if (target != null)
            {
                NPCActionTargetRegistry.Instance?.Register(target);
                if (npcAliases.Count > 0)
                    NPCActionTargetRegistry.Instance?.RegisterAliases(target, npcAliases);
            }
        }

        /// <summary>
        /// Activates or deactivates the speaking highlight overlay.
        /// When active, the shader pulses the overlay alpha using _Time — no per-frame C# update needed.
        /// </summary>
        public void SetSpeaking(bool speaking)
        {
            if (_overlayMaterial != null)
                _overlayMaterial.SetFloat("_Active", speaking ? 1f : 0f);
        }
    }

}

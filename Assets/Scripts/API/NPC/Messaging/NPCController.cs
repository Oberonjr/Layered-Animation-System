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
    /// 

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
        [Tooltip("The renderer component used to visualize when this NPC is speaking (typically a capsule or other mesh). Color will change when speaking.")]
        [SerializeField] private Renderer capsuleRenderer;

        [Tooltip("The color to lerp toward when this NPC is speaking. Creates a pulsing effect.")]
        [SerializeField] private Color highlightColor = Color.green; // Color when speaking

        [Tooltip("The base color when this NPC is not speaking.")]
        [SerializeField] private Color normalColor = Color.gray;

        [Tooltip("Speed of the pulsing effect when speaking (higher = faster pulse).")]
        [SerializeField] private float pulseSpeed = 2f;

        [Header("Runtime Info")]
        [Tooltip("This NPC's index in the NPCManager's registered list. Used by the LLM to reference specific NPCs. Assigned automatically by NPCManager.")]
        [SerializeField] private int assignedIndex = -1; // Assigned by NPCManager

        /// <summary>Whether this NPC is currently speaking (affects visual state).</summary>
        private bool isSpeaking = false;

        /// <summary>Material instance for this NPC's renderer (created at runtime to avoid shared material issues).</summary>
        private Material material;

        /// <summary>The color the material is lerping toward (either normal or highlight).</summary>
        private Color targetColor;

        /// <summary>Gets the index assigned to this NPC by NPCManager.</summary>
        public int AssignedIndex => assignedIndex;

        /// <summary>Gets the highlight color used when this NPC is speaking.</summary>
        public Color HighlightColor => highlightColor;

        /// <summary>
        /// Initializes the material and ensures an NPCTarget component is present for action system registration.
        /// </summary>
        void Awake()
        {
            if (capsuleRenderer == null)
                capsuleRenderer = GetComponent<Renderer>();

            if (capsuleRenderer != null)
            {
                material = capsuleRenderer.material;
                material.color = normalColor;
                targetColor = normalColor;
            }

            // Ensure NPCTarget is present — it handles registration with the action target registry.
            if (GetComponent<NPCTarget>() == null)
            {
                var npcTarget = gameObject.AddComponent<NPCTarget>();
                // Copy inspector-defined aliases so they are indexed when NPCTarget.Start() registers.
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
        /// Cleans up event bus subscription and destroys the material instance.
        /// Registry unregistration is handled automatically by the NPCTarget component.
        /// </summary>
        void OnDestroy()
        {
            NPCEventBus.UnregisterNPC(this);

            if (material != null)
                Destroy(material);
        }

        /// <summary>
        /// Handles smooth color transitions and pulsing effect when speaking.
        /// The material color lerps toward targetColor, and pulses between normal and highlight when speaking.
        /// </summary>
        void Update()
        {
            if (material == null)
                return;

            // Smooth color transition
            material.color = Color.Lerp(material.color, targetColor, Time.deltaTime * 5f);

            // Pulse effect when speaking
            if (isSpeaking)
            {
                float pulse = (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f;
                material.color = Color.Lerp(normalColor, highlightColor, pulse);
            }
        }

        /// <summary>
        /// Assigns this NPC's index in the NPCManager's registered NPC list.
        /// Called by NPCManager during initialization. The index is used by the LLM to specify which NPC should perform actions.
        /// </summary>
        /// <param name="index">The zero-based index in the NPCManager's registeredNPCs list.</param>
        public void AssignIndex(int index)
        {
            assignedIndex = index;
            Debug.Log($"{gameObject.name} assigned as NPC {index}: {npcName}");
        }

        /// <summary>
        /// Assigns character data from the scenario configuration to this NPC.
        /// Called by NPCManager during initialization. Updates the target registry if already registered.
        /// </summary>
        /// <param name="name">The character's name.</param>
        /// <param name="role">The character's role/occupation.</param>
        /// <param name="description">Full character description (personality, background, communication style).</param>
        public void AssignCharacterData(string name, string role, string description)
        {
            var target = GetComponent<NPCTarget>();

            // Unregister while TargetName still reflects the old npcName, then update, then re-register.
            if (target != null)
                NPCActionTargetRegistry.Instance?.Unregister(target);

            npcName = name;
            characterRole = role;
            characterDescription = description;

            if (target != null)
            {
                NPCActionTargetRegistry.Instance?.Register(target);
                // Re-register custom aliases (Register only indexes target.aliases, not npcAliases on this component).
                if (npcAliases.Count > 0)
                    NPCActionTargetRegistry.Instance?.RegisterAliases(target, npcAliases);
            }
        }

        /// <summary>
        /// Sets whether this NPC is currently speaking, which triggers visual feedback.
        /// When speaking, the renderer pulses between normal and highlight colors.
        /// Called by NPCManager or ConversationFlowController when dialogue is displayed/hidden.
        /// </summary>
        /// <param name="speaking">True if the NPC is speaking, false otherwise.</param>
        public void SetSpeaking(bool speaking)
        {
            isSpeaking = speaking;
            targetColor = speaking ? highlightColor : normalColor;
        }
    }

}

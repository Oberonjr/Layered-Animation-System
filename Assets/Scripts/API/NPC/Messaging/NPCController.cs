using UnityEngine;

public class NPCController : MonoBehaviour, IActionTarget
{
    [Header("NPC Identity")]
    public string npcName = "NPC";
    public string characterRole = ""; // Assigned from scenario
    [TextArea(3, 6)]
    public string characterDescription = ""; // Assigned from scenario

    [Header("Visual Indicator")]
    [SerializeField] private Renderer capsuleRenderer;
    [SerializeField] private Color highlightColor = Color.green; // Color when speaking
    [SerializeField] private Color normalColor = Color.gray;
    [SerializeField] private float pulseSpeed = 2f;

    [Header("Runtime Info")]
    [SerializeField] private int assignedIndex = -1; // Assigned by NPCManager

    private bool isSpeaking = false;
    private Material material;
    private Color targetColor;
    private bool hasRegisteredAsTarget = false;

    public int AssignedIndex => assignedIndex;
    public Color HighlightColor => highlightColor;

    // IActionTarget implementation
    public string TargetName => npcName;
    public TargetType Type => TargetType.NPC;
    public Transform Transform => transform;

    void Awake()
    {
        // Get or create material
        if (capsuleRenderer == null)
        {
            capsuleRenderer = GetComponent<Renderer>();
        }

        if (capsuleRenderer != null)
        {
            material = capsuleRenderer.material;
            material.color = normalColor;
            targetColor = normalColor;
        }

        // Remove ActionTarget component if mistakenly added
        var actionTarget = GetComponent<ActionTarget>();
        if (actionTarget != null)
        {
            Debug.LogWarning($"[{gameObject.name}] NPCController already implements IActionTarget. Removing duplicate ActionTarget component.");
            Destroy(actionTarget);
        }
    }

    void Start()
    {
        // Register with the event bus
        NPCEventBus.RegisterNPC(this);

        // Register as action target AFTER character data is assigned
        // (The NPCManager will call AssignCharacterData before registry registration happens)
        RegisterAsActionTarget();
    }

    private void RegisterAsActionTarget()
    {
        if (hasRegisteredAsTarget) return;

        NPCActionTargetRegistry.Instance?.Register(this);
        hasRegisteredAsTarget = true;
        Debug.Log($"[{gameObject.name}] Registered as action target with name: '{npcName}'");
    }

    void OnDestroy()
    {
        // Unregister from event bus
        NPCEventBus.UnregisterNPC(this);

        // Unregister from target registry
        if (hasRegisteredAsTarget)
        {
            NPCActionTargetRegistry.Instance?.Unregister(this);
        }

        // Clean up material
        if (material != null)
        {
            Destroy(material);
        }
    }

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

    public void AssignIndex(int index)
    {
        assignedIndex = index;
        Debug.Log($"{gameObject.name} assigned as NPC {index}: {npcName}");
    }

    public void AssignCharacterData(string name, string role, string description)
    {
        npcName = name;
        characterRole = role;
        characterDescription = description;

        // If already registered, update the registry with new name
        if (hasRegisteredAsTarget)
        {
            // Unregister with old name
            NPCActionTargetRegistry.Instance?.Unregister(this);
            hasRegisteredAsTarget = false;

            // Re-register with new name
            RegisterAsActionTarget();
        }
    }

    public void SetSpeaking(bool speaking)
    {
        isSpeaking = speaking;
        targetColor = speaking ? highlightColor : normalColor;
    }
}
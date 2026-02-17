using UnityEngine;

public class NPCController : MonoBehaviour
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

    public int AssignedIndex => assignedIndex;
    public Color HighlightColor => highlightColor;

    void Start()
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
        
        // Register with the event bus
        NPCEventBus.RegisterNPC(this);
    }

    void OnDestroy()
    {
        // Unregister from event bus
        NPCEventBus.UnregisterNPC(this);
        
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
    }

    public void SetSpeaking(bool speaking)
    {
        isSpeaking = speaking;
        targetColor = speaking ? highlightColor : normalColor;
    }
}

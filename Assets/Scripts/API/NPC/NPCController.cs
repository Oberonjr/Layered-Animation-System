using UnityEngine;

public class NPCController : MonoBehaviour
{
    [Header("NPC Identity")]
    public string npcName = "NPC";
    public string characterId = "npc"; // Used to match with scenario config
    [TextArea(3, 6)]
    public string characterDescription = "A helpful character.";
    public MessageType messageType = MessageType.NPCA;

    [Header("Visual Indicator")]
    [SerializeField] private Renderer capsuleRenderer;
    [SerializeField] private Color normalColor = Color.gray;
    [SerializeField] private Color speakingColor = Color.green;
    [SerializeField] private float pulseSpeed = 2f;

    private bool isSpeaking = false;
    private Material material;
    private Color targetColor;

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
            material.color = Color.Lerp(normalColor, speakingColor, pulse);
        }
    }

    public void SetSpeaking(bool speaking)
    {
        isSpeaking = speaking;
        targetColor = speaking ? speakingColor : normalColor;
    }

    void OnDestroy()
    {
        // Clean up material
        if (material != null)
        {
            Destroy(material);
        }
    }
}
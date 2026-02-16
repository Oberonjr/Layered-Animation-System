using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class ChatMessage : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Text speakerText;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private Image backgroundImage;

    [Header("Layout Settings")]
    [SerializeField] private float padding = 20f;
    [SerializeField] private float speakerHeight = 25f;
    [SerializeField] private float spacing = 5f;

    [Header("Color Settings")]
    [SerializeField] private Color playerColor = new Color(0.3f, 0.5f, 0.8f);
    [SerializeField] private Color npcAColor = new Color(0.5f, 0.8f, 0.5f);
    [SerializeField] private Color npcBColor = new Color(0.8f, 0.5f, 0.5f);
    [SerializeField] private Color systemColor = new Color(0.6f, 0.6f, 0.6f);

    [HideInInspector] public string speaker;
    [HideInInspector] public string message;
    [HideInInspector] public MessageType type;

    private RectTransform rectTransform;
    private RectTransform messageTextRect;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        if (messageText != null)
        {
            messageTextRect = messageText.GetComponent<RectTransform>();
        }
    }

    public void Initialize(string speakerName, string messageContent, MessageType messageType)
    {
        speaker = speakerName;
        message = messageContent;
        type = messageType;

        if (speakerText != null)
        {
            speakerText.text = speakerName + ":";
        }

        if (messageText != null)
        {
            messageText.text = messageContent;
        }

        SetColorByType(messageType);
        UpdateSize();
    }

    public void UpdateText(string newMessage)
    {
        message = newMessage;
        if (messageText != null)
        {
            messageText.text = newMessage;
            UpdateSize();
        }
    }

    private void UpdateSize()
    {
        if (rectTransform == null || messageText == null || messageTextRect == null)
            return;

        // Get parent width
        RectTransform parentRect = transform.parent.GetComponent<RectTransform>();
        if (parentRect == null)
            return;

        float parentWidth = parentRect.rect.width;

        // Set this object's width to match parent
        rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, parentWidth);

        // Calculate available width for text (minus padding)
        float textWidth = parentWidth - (padding * 2);

        // Force text to calculate its preferred height with the given width
        messageText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, textWidth);

        // Get the preferred height of the text
        float textHeight = messageText.preferredHeight;

        // Calculate total height: padding + speaker + spacing + message + padding
        float totalHeight = padding + speakerHeight + spacing + textHeight + padding;

        // Set this object's height
        rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, totalHeight);

        // Position speaker text
        if (speakerText != null)
        {
            RectTransform speakerRect = speakerText.GetComponent<RectTransform>();
            speakerRect.anchoredPosition = new Vector2(padding, -padding);
            speakerRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, textWidth);
            speakerRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, speakerHeight);
        }

        // Position message text
        messageTextRect.anchoredPosition = new Vector2(padding, -(padding + speakerHeight + spacing));
        messageTextRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, textHeight);
    }

    private void SetColorByType(MessageType messageType)
    {
        if (backgroundImage == null)
            return;

        Color color = systemColor;

        switch (messageType)
        {
            case MessageType.Player:
                color = playerColor;
                break;
            case MessageType.NPCA:
                color = npcAColor;
                break;
            case MessageType.NPCB:
                color = npcBColor;
                break;
            case MessageType.System:
                color = systemColor;
                break;
        }

        backgroundImage.color = color;
    }
}
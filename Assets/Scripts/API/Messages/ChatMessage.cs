using UnityEngine;
using TMPro;
using UnityEngine.UI;
using LAS;
/// <summary>
/// UI component for displaying a single message in the chat interface.
/// Handles dynamic sizing, color coding by message type, and speaker name display.
/// Instantiated from a prefab by NPCManager or ConversationFlowController.
/// </summary>
/// 

namespace LAS
{
    public class ChatMessage : MonoBehaviour
    {
        [Header("UI References")]
        [Tooltip("TextMeshPro component displaying the speaker name (e.g., 'Player:', 'Alice:').")]
        [SerializeField] private TMP_Text speakerText;

        [Tooltip("TextMeshPro component displaying the message content.")]
        [SerializeField] private TMP_Text messageText;

        [Tooltip("Background image component used for color coding message types.")]
        [SerializeField] private Image backgroundImage;

        [Header("Layout Settings")]
        [Tooltip("Padding around the message content in pixels.")]
        [SerializeField] private float padding = 20f;

        [Tooltip("Height reserved for the speaker name text in pixels.")]
        [SerializeField] private float speakerHeight = 25f;

        [Tooltip("Spacing between speaker name and message text in pixels.")]
        [SerializeField] private float spacing = 5f;

        [Header("Default Color Settings")]
        [Tooltip("Background color for player messages.")]
        [SerializeField] private Color playerColor = new Color(0.3f, 0.5f, 0.8f);

        [Tooltip("Background color for system messages (scenario descriptions, errors).")]
        [SerializeField] private Color systemColor = new Color(0.6f, 0.6f, 0.6f);

        [Tooltip("How much to desaturate/gray out NPC highlight colors for message backgrounds (0 = full color, 1 = completely gray).")]
        [SerializeField] private float npcColorGrayAmount = 0.3f; // How much to gray out NPC colors for messages

        /// <summary>The name of the speaker (e.g., "Player", "Alice", "System").</summary>
        [HideInInspector] public string speaker;

        /// <summary>The message content/text.</summary>
        [HideInInspector] public string message;

        /// <summary>The type of message (Player, NPC, or System) for color coding.</summary>
        [HideInInspector] public MessageType type;

        private RectTransform rectTransform;
        private RectTransform messageTextRect;

        /// <summary>
        /// Caches RectTransform references on instantiation.
        /// </summary>
        void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            if (messageText != null)
            {
                messageTextRect = messageText.GetComponent<RectTransform>();
            }
        }

        /// <summary>
        /// Initializes the message with default color based on message type.
        /// Used for Player and System messages. For NPC messages, use InitializeWithColor instead.
        /// </summary>
        /// <param name="speakerName">The name to display before the colon (e.g., "Player").</param>
        /// <param name="messageContent">The message text to display.</param>
        /// <param name="messageType">The type of message for color selection.</param>
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

        /// <summary>
        /// Initializes the message with a custom color (typically for NPC messages).
        /// The NPC's highlight color is desaturated by npcColorGrayAmount for readability.
        /// </summary>
        /// <param name="speakerName">The NPC's name to display.</param>
        /// <param name="messageContent">The dialogue text.</param>
        /// <param name="messageType">Should be MessageType.NPC.</param>
        /// <param name="npcColor">The NPC's highlight color (from NPCController).</param>
        public void InitializeWithColor(string speakerName, string messageContent, MessageType messageType, Color npcColor)
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

            SetColorDirect(npcColor);
            UpdateSize();
        }

        /// <summary>
        /// Updates the message text and recalculates size.
        /// Used for simulated streaming when dialogue is appended character-by-character.
        /// </summary>
        /// <param name="newMessage">The updated message text.</param>
        public void UpdateText(string newMessage)
        {
            message = newMessage;
            if (messageText != null)
            {
                messageText.text = newMessage;
                UpdateSize();
            }
        }

        /// <summary>
        /// Dynamically calculates and sets the size of the message box based on text content.
        /// Ensures the message box expands to fit multi-line text while matching parent width.
        /// </summary>
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

        /// <summary>
        /// Sets the background color based on message type using default colors.
        /// Player = blue, System = gray, NPC = gray (but NPC should use SetColorDirect instead).
        /// </summary>
        /// <param name="messageType">The type of message.</param>
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
                case MessageType.System:
                    color = systemColor;
                    break;
                default:
                    // For NPC types, this shouldn't be called - use InitializeWithColor instead
                    color = new Color(0.7f, 0.7f, 0.7f);
                    break;
            }

            backgroundImage.color = color;
        }

        /// <summary>
        /// Sets the background color directly using a custom color (typically NPC highlight color).
        /// Desaturates the color by npcColorGrayAmount and adds slight transparency for readability.
        /// </summary>
        /// <param name="baseColor">The base color to use (typically from NPCController.HighlightColor).</param>
        private void SetColorDirect(Color baseColor)
        {
            if (backgroundImage == null)
                return;

            // Make the message background more gray/muted than the capsule highlight
            Color messageColor = Color.Lerp(baseColor, Color.gray, npcColorGrayAmount);
            messageColor.a = 0.8f; // Slight transparency

            backgroundImage.color = messageColor;
        }
    }

}

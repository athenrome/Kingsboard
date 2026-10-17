using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kingsboard.Chat
{
    /// <summary>
    /// Handles all client-side chat in Unity: it owns the chat window (bottom-left of the screen, built with UI Toolkit)
    /// and talks to the server only through <see cref="ChatGrpcHandler"/>. All chat rules (validation, rate limiting,
    /// usernames) are enforced by the server.
    /// It creates itself when the game starts, so nothing needs adding to a scene.
    /// To set the username in the Inspector, add this component to a GameObject in the scene instead.
    /// Type a message and press Enter to send it.
    /// </summary>
    public class ChatController : MonoBehaviour
    {
        /// <summary>
        /// Longest message the input box accepts (matches the server's ChatSettings.MaxMessageLength)
        /// </summary>
        public const int MaxMessageLength = 256;

        /// <summary>
        /// Most lines kept in the chat window; the oldest are removed first
        /// </summary>
        public const int MaxLines = 200;

        [Tooltip("Name to join the chat with. The server may change it to keep names unique.")]
        [SerializeField] private string username = "Player";

        private static readonly Color PlayerColor = Color.white;
        private static readonly Color SystemColor = new Color(1f, 0.85f, 0.4f);

        private ChatGrpcHandler _handler;
        private UIDocument _document;
        private Label _status;
        private ScrollView _messages;
        private TextField _input;

        /// <summary>
        /// Our name in the chat (set by the server when we join), or null before joining
        /// </summary>
        public string Username { get; private set; }

        /// <summary>
        /// True while we are joined to the chat
        /// </summary>
        public bool IsConnected { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateIfMissing()
        {
            if (FindAnyObjectByType<ChatController>() == null)
            {
                GameObject chat = new GameObject("Chat");
                DontDestroyOnLoad(chat);
                chat.AddComponent<ChatController>();
            }
        }

        private void Awake()
        {
            PanelSettings panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("ChatTheme");
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            panelSettings.sortingOrder = 100;

            _document = gameObject.AddComponent<UIDocument>();
            _document.panelSettings = panelSettings;
        }

        private void Start()
        {
            VisualElement root = _document.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("[Chat] The chat window could not be created (UIDocument has no root element).");
                return;
            }

            BuildWindow(root);
            UpdateStatus();

            _handler = new ChatGrpcHandler();
            _handler.Joined += OnJoined;
            _handler.MessageReceived += AddLine;
            _handler.Disconnected += OnDisconnected;
            _handler.Connect(username);
        }

        private void OnDestroy()
        {
            if (_handler != null)
            {
                _handler.Joined -= OnJoined;
                _handler.MessageReceived -= AddLine;
                _handler.Disconnected -= OnDisconnected;
                _handler.Dispose();
                _handler = null;
            }
        }

        /// <summary>
        /// Sends a message to everyone in the chat. Blank messages are ignored.
        /// Our own message comes back from the server like everyone else's, so only failures are shown here.
        /// </summary>
        public async void Send(string text)
        {
            text = text == null ? string.Empty : text.Trim();
            if (text.Length == 0 || _handler == null)
            {
                return;
            }

            string error = await _handler.SendAsync(text);
            if (error != null && this != null)
            {
                AddNotice($"Not sent: {error}");
            }
        }

        private void BuildWindow(VisualElement root)
        {
            // Let clicks outside the chat window through to the game
            root.pickingMode = PickingMode.Ignore;

            VisualElement window = new VisualElement();
            window.style.position = Position.Absolute;
            window.style.left = 16;
            window.style.bottom = 16;
            window.style.width = 480;
            window.style.height = 300;
            window.style.paddingLeft = 8;
            window.style.paddingRight = 8;
            window.style.paddingTop = 6;
            window.style.paddingBottom = 6;
            window.style.backgroundColor = new Color(0f, 0f, 0f, 0.65f);
            window.style.borderTopLeftRadius = 6;
            window.style.borderTopRightRadius = 6;
            window.style.borderBottomLeftRadius = 6;
            window.style.borderBottomRightRadius = 6;
            root.Add(window);

            _status = new Label();
            _status.style.fontSize = 12;
            _status.style.color = new Color(0.7f, 0.7f, 0.7f);
            _status.style.marginBottom = 4;
            window.Add(_status);

            _messages = new ScrollView(ScrollViewMode.Vertical);
            _messages.style.flexGrow = 1;
            window.Add(_messages);

            _input = new TextField();
            _input.maxLength = MaxMessageLength;
            _input.textEdition.placeholder = "Type a message and press Enter";
            _input.style.marginTop = 6;
            _input.style.marginLeft = 0;
            _input.style.marginRight = 0;
            _input.RegisterCallback<KeyDownEvent>(OnInputKeyDown, TrickleDown.TrickleDown);
            window.Add(_input);
        }

        private void OnInputKeyDown(KeyDownEvent keyEvent)
        {
            if (keyEvent.keyCode != KeyCode.Return && keyEvent.keyCode != KeyCode.KeypadEnter)
            {
                return;
            }

            keyEvent.StopPropagation();

            string text = _input.value;
            _input.value = string.Empty;
            Send(text);

            // Keep typing without having to click the box again
            _input.schedule.Execute(() => _input.Focus());
        }

        private void OnJoined(string joinedUsername)
        {
            // The server replays recent messages after every join, so start from an empty window
            _messages.Clear();

            Username = joinedUsername;
            IsConnected = true;
            UpdateStatus();
        }

        private void OnDisconnected()
        {
            IsConnected = false;
            UpdateStatus();
            AddNotice("Disconnected from chat. Reconnecting...");
        }

        private void AddNotice(string text)
        {
            AddLine(new ChatLine() { IsSystem = true, Text = text, SentAt = DateTime.Now });
        }

        private void AddLine(ChatLine line)
        {
            string time = line.SentAt.ToString("HH:mm");
            string text = line.IsSystem
                ? $"[{time}] {line.Text}"
                : $"[{time}] {line.Username}: {line.Text}";

            Label label = new Label(text);
            // Rich text is off so players can't use formatting tags in names or messages
            label.enableRichText = false;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.fontSize = 14;
            label.style.color = line.IsSystem ? SystemColor : PlayerColor;
            label.style.opacity = line.IsHistory ? 0.6f : 1f;
            _messages.Add(label);

            while (_messages.childCount > MaxLines)
            {
                _messages.RemoveAt(0);
            }

            // Scroll to the new line once it has been laid out
            _messages.schedule.Execute(() => _messages.ScrollTo(label));
        }

        private void UpdateStatus()
        {
            _status.text = IsConnected
                ? $"Chat - connected as {Username}"
                : "Chat - connecting...";
        }
    }
}

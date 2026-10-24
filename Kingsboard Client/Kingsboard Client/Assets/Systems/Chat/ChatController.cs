using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.UIElements;

namespace Kingsboard.Chat
{
    /// <summary>
    /// Handles all client-side chat in Unity: it owns the chat window (bottom-left of the screen, built with UI Toolkit)
    /// and talks to the server only through <see cref="ChatGrpcHandler"/>. All chat rules (validation, rate limiting,
    /// usernames) are enforced by the server.
    /// Lives on the Chat prefab (Assets/Prefabs/Chat/Chat.prefab), placed in the scene; set the username there.
    /// Click the input box, type a message and press Enter to send it. The box only takes the keyboard when clicked
    /// (never through keyboard navigation, so game keys such as WASD stay with the game), and lets it go on Esc or a
    /// click anywhere outside the chat window.
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
        [SerializeField, FormerlySerializedAs("username")] private string _username = "Player";

        [Tooltip("The UIDocument the chat window is built in; its panel settings (theme, scaling) are set on the Chat prefab")]
        [SerializeField] private UIDocument _document;

        private static readonly Color PlayerColor = Color.white;
        private static readonly Color SystemColor = new Color(1f, 0.85f, 0.4f);

        private ChatGrpcHandler _handler;
        private VisualElement _window;
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

        private void Start()
        {
            if (_document == null)
            {
                Debug.LogError("[Chat] The ChatController needs its UIDocument assigned. Use the Chat prefab.", this);
                enabled = false;
                return;
            }

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
            _handler.Connect(_username);
        }

        private void Update()
        {
            if (_window == null || !IsTyping())
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                StopTyping();
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
            {
                return;
            }

            // A click outside the chat window hands the keyboard back to the game
            Vector2 pointer = mouse.position.ReadValue();
            Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(_window.panel, new Vector2(pointer.x, Screen.height - pointer.y));
            if (!_window.worldBound.Contains(panelPoint))
            {
                StopTyping();
            }
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
            _window = window;
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
            _input.RegisterCallback<PointerDownEvent>(OnInputPointerDown, TrickleDown.TrickleDown);
            _input.RegisterCallback<FocusOutEvent>(OnInputFocusOut);
            window.Add(_input);

            // Keyboard navigation (which UI Toolkit maps to WASD and the arrows) would otherwise focus the box or the
            // scrollbar and swallow the game's keys, so nothing in the window is focusable and the box only becomes
            // focusable while clicked
            window.Query<VisualElement>().ForEach(element => element.focusable = false);
        }

        private void OnInputPointerDown(PointerDownEvent pointerEvent)
        {
            SetInputFocusable(true);
            _input.Focus();
        }

        private void OnInputFocusOut(FocusOutEvent focusEvent)
        {
            // Focus moving between the text field and its inner text element doesn't count as leaving
            if (focusEvent.relatedTarget is VisualElement next && _input.Contains(next))
            {
                return;
            }
            SetInputFocusable(false);
        }

        /// <summary>
        /// True while the input box has the keyboard
        /// </summary>
        private bool IsTyping()
        {
            VisualElement focused = _input?.focusController?.focusedElement as VisualElement;
            return focused != null && (focused == _input || _input.Contains(focused));
        }

        private void StopTyping()
        {
            if (_input.focusController?.focusedElement is Focusable focused)
            {
                focused.Blur();
            }
            SetInputFocusable(false);
        }

        private void SetInputFocusable(bool focusable)
        {
            _input.Query<VisualElement>().ForEach(element => element.focusable = focusable);
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

using System;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UIElements;

namespace Kingsboard.TurnTimer
{
    /// <summary>
    /// Shows the turn timer at the top of the screen (built with UI Toolkit): the turn number, the phase the turn is in,
    /// and, during the Player phase, how much time is left to play as a countdown and a draining bar.
    /// The server sends the time remaining whenever a phase starts; the countdown carries on locally between messages,
    /// so it runs smoothly and isn't affected by differences between the client's and server's clocks.
    /// Talks to the server only through <see cref="TurnTimerGrpcHandler"/>.
    /// Lives on the TurnTimer prefab (Assets/Prefabs/TurnTimer/TurnTimer.prefab), placed in the scene.
    /// </summary>
    public class TurnTimerController : MonoBehaviour
    {
        [Tooltip("Seconds left in the Player phase at which the countdown turns to the warning colour")]
        [SerializeField, FormerlySerializedAs("warningSeconds")] private float _warningSeconds = 1f;

        [Tooltip("Colour of the countdown and bar while there's plenty of time")]
        [SerializeField, FormerlySerializedAs("normalColour")] private Color _normalColour = new Color(0.55f, 0.85f, 1f);

        [Tooltip("Colour of the countdown and bar once time is nearly up")]
        [SerializeField, FormerlySerializedAs("warningColour")] private Color _warningColour = new Color(1f, 0.45f, 0.35f);

        [Tooltip("The UIDocument the timer is built in; its panel settings (theme, scaling) are set on the TurnTimer prefab")]
        [SerializeField] private UIDocument _document;

        private TurnTimerGrpcHandler _handler;
        private Label _turnLabel;
        private Label _phaseLabel;
        private Label _countdownLabel;
        private VisualElement _bar;
        private VisualElement _barFill;

        /// <summary>
        /// The latest turn state from the server, or null before one has arrived
        /// </summary>
        public TurnInfo Turn { get; private set; }

        /// <summary>
        /// True while the turn is in the Player phase
        /// </summary>
        public bool IsPlayerPhase => Turn != null && Turn.Phase == TurnPhase.Player;

        /// <summary>
        /// Seconds left in the Player phase, counting down locally (0 outside the Player phase)
        /// </summary>
        public float PlayerTimeRemaining =>
            IsPlayerPhase ? Mathf.Max(0f, Turn.RemainingSeconds - (Time.realtimeSinceStartup - Turn.ReceivedAt)) : 0f;

        /// <summary>
        /// Raised whenever the server sends a new turn state (turn started or ended, or a phase started)
        /// </summary>
        public event Action<TurnInfo> TurnUpdated;

        private void Start()
        {
            if (_document == null)
            {
                Debug.LogError("[TurnTimer] The TurnTimerController needs its UIDocument assigned. Use the TurnTimer prefab.", this);
                enabled = false;
                return;
            }

            VisualElement root = _document.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("[TurnTimer] The turn timer could not be created (UIDocument has no root element).");
                return;
            }

            BuildWindow(root);
            ShowWaiting("Connecting...");

            _handler = new TurnTimerGrpcHandler();
            _handler.TurnUpdated += OnTurnUpdated;
            _handler.Disconnected += OnDisconnected;
            _handler.Connect();
        }

        private void Update()
        {
            RefreshCountdown();
        }

        /// <summary>
        /// Updates the countdown text, colour and bar from the time left in the Player phase
        /// </summary>
        private void RefreshCountdown()
        {
            if (!IsPlayerPhase || _countdownLabel == null)
            {
                return;
            }

            float remaining = PlayerTimeRemaining;
            Color colour = remaining <= _warningSeconds ? _warningColour : _normalColour;

            _countdownLabel.text = FormatSeconds(remaining);
            _countdownLabel.style.color = colour;

            float fraction = Turn.DurationSeconds > 0f ? Mathf.Clamp01(remaining / Turn.DurationSeconds) : 0f;
            _barFill.style.width = Length.Percent(fraction * 100f);
            _barFill.style.backgroundColor = colour;
        }

        private void OnDestroy()
        {
            if (_handler != null)
            {
                _handler.TurnUpdated -= OnTurnUpdated;
                _handler.Disconnected -= OnDisconnected;
                _handler.Dispose();
                _handler = null;
            }
        }

        private void OnTurnUpdated(TurnInfo turn)
        {
            Turn = turn;
            _turnLabel.text = $"Turn {turn.TurnId}";
            _phaseLabel.text = DescribePhase(turn.Phase);

            bool playing = turn.Phase == TurnPhase.Player;
            _countdownLabel.style.display = playing ? DisplayStyle.Flex : DisplayStyle.None;
            _bar.style.display = playing ? DisplayStyle.Flex : DisplayStyle.None;
            RefreshCountdown();

            TurnUpdated?.Invoke(turn);
        }

        private void OnDisconnected()
        {
            Turn = null;
            ShowWaiting("Reconnecting...");
        }

        private void ShowWaiting(string message)
        {
            _turnLabel.text = "Turn timer";
            _phaseLabel.text = message;
            _countdownLabel.style.display = DisplayStyle.None;
            _bar.style.display = DisplayStyle.None;
        }

        private void BuildWindow(VisualElement root)
        {
            // Let clicks outside the timer through to the game
            root.pickingMode = PickingMode.Ignore;

            VisualElement window = new VisualElement();
            window.pickingMode = PickingMode.Ignore;
            window.style.position = Position.Absolute;
            window.style.top = 16;
            window.style.left = Length.Percent(50);
            window.style.translate = new Translate(Length.Percent(-50), 0);
            window.style.width = 280;
            window.style.paddingLeft = 12;
            window.style.paddingRight = 12;
            window.style.paddingTop = 8;
            window.style.paddingBottom = 10;
            window.style.alignItems = Align.Center;
            window.style.backgroundColor = new Color(0f, 0f, 0f, 0.65f);
            window.style.borderTopLeftRadius = 6;
            window.style.borderTopRightRadius = 6;
            window.style.borderBottomLeftRadius = 6;
            window.style.borderBottomRightRadius = 6;
            root.Add(window);

            _turnLabel = CreateLabel(14, new Color(0.7f, 0.7f, 0.7f));
            window.Add(_turnLabel);

            _phaseLabel = CreateLabel(18, Color.white);
            window.Add(_phaseLabel);

            _countdownLabel = CreateLabel(36, _normalColour);
            _countdownLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            window.Add(_countdownLabel);

            _bar = new VisualElement();
            _bar.pickingMode = PickingMode.Ignore;
            _bar.style.width = Length.Percent(100);
            _bar.style.height = 6;
            _bar.style.marginTop = 4;
            _bar.style.backgroundColor = new Color(1f, 1f, 1f, 0.15f);
            window.Add(_bar);

            _barFill = new VisualElement();
            _barFill.pickingMode = PickingMode.Ignore;
            _barFill.style.height = Length.Percent(100);
            _barFill.style.width = Length.Percent(100);
            _barFill.style.backgroundColor = _normalColour;
            _bar.Add(_barFill);
        }

        private static Label CreateLabel(int fontSize, Color colour)
        {
            Label label = new Label();
            label.pickingMode = PickingMode.Ignore;
            label.enableRichText = false;
            label.style.fontSize = fontSize;
            label.style.color = colour;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            return label;
        }

        private static string DescribePhase(TurnPhase phase)
        {
            switch (phase)
            {
                case TurnPhase.Start: return "Turn starting";
                case TurnPhase.Upkeep: return "Upkeep";
                case TurnPhase.Planning: return "Pawns are planning";
                case TurnPhase.Player: return "Your move";
                case TurnPhase.Resolution: return "Resolving moves";
                case TurnPhase.End: return "Turn over";
                default: return "Waiting";
            }
        }

        /// <summary>
        /// Seconds as "3.2" under ten seconds, otherwise "m:ss"
        /// </summary>
        private static string FormatSeconds(float seconds)
        {
            if (seconds < 10f)
            {
                return seconds.ToString("0.0");
            }

            int whole = Mathf.CeilToInt(seconds);
            return $"{whole / 60}:{whole % 60:00}";
        }
    }
}

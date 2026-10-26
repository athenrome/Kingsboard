using System.Collections.Generic;
using Kingsboard.Shared.Models.Cards;
using Kingsboard.Shared.Models.Enumerations;
using Kingsboard.Shared.Models.Pawns;
using Kingsboard.TurnTimer;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kingsboard.Pawns
{
    /// <summary>
    /// Shows the selected pawn's hand of cards at the bottom right of the screen (built with UI Toolkit): each card's
    /// name and description, with the card the pawn will play highlighted. It follows the pawn as the server streams new
    /// state, and hides when no pawn is selected. Anyone's pawn can be looked at; for a pawn we control, clicking a card
    /// in the Player phase chooses it (a card that needs a tile then waits for a tile to be clicked, through
    /// <see cref="PawnManager.BeginTargeting"/>). The server decides whether a choice is allowed.
    /// Lives on the PawnHand prefab (Assets/Prefabs/Pawns/PawnHand.prefab), nested in the Pawns prefab so its
    /// <see cref="PawnManager"/> reference is set there.
    /// </summary>
    public class PawnHandController : MonoBehaviour
    {
        [Tooltip("The PawnManager whose selected pawn's hand is shown (the root of the Pawns prefab)")]
        [SerializeField] private PawnManager _pawnManager;

        [Tooltip("The UIDocument the hand is built in; its panel settings (theme, scaling) are set on the PawnHand prefab")]
        [SerializeField] private UIDocument _document;

        [Tooltip("Seconds a message from the server (e.g. why a card couldn't be chosen) stays on show")]
        [SerializeField] private float _errorSeconds = 4f;

        private static readonly Color CardColour = new Color(0.12f, 0.12f, 0.16f, 0.95f);
        private static readonly Color CardHoverColour = new Color(0.2f, 0.2f, 0.28f, 0.95f);
        private static readonly Color ChosenBorderColour = new Color(1f, 0.8f, 0.25f);
        private static readonly Color TargetingBorderColour = new Color(0.4f, 0.8f, 1f);
        private static readonly Color PlainBorderColour = new Color(1f, 1f, 1f, 0.15f);
        private static readonly Color HintColour = new Color(0.8f, 0.8f, 0.8f);
        private static readonly Color ErrorColour = new Color(1f, 0.45f, 0.4f);

        private VisualElement _window;
        private Label _titleLabel;
        private Label _ownerLabel;
        private Label _deckLabel;
        private VisualElement _cardRow;
        private Label _statusLabel;

        private TurnTimerController _turnTimer;

        // What's on show, so the hand is only rebuilt when something changes
        private PawnController _shownPawn;
        private Pawn _shownState;
        private bool _shownCanChoose;
        private Card _shownTargetingCard;
        private string _error;
        private float _errorUntil;
        private bool _dirty = true;

        /// <summary>
        /// True if <paramref name="screenPoint"/> (in screen pixels, origin bottom-left, as the Input System gives it) is over
        /// the hand, so clicks there aren't also treated as clicks on the board
        /// </summary>
        public bool ContainsScreenPoint(Vector2 screenPoint)
        {
            if (_window == null || _window.panel == null || _window.style.display.value == DisplayStyle.None)
            {
                return false;
            }

            Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(_window.panel, new Vector2(screenPoint.x, Screen.height - screenPoint.y));
            return _window.worldBound.Contains(panelPoint);
        }

        private void Start()
        {
            if (_pawnManager == null || _document == null)
            {
                Debug.LogError("[Pawns] The PawnHandController needs its PawnManager and UIDocument assigned. Use the PawnHand prefab inside the Pawns prefab.", this);
                enabled = false;
                return;
            }

            VisualElement root = _document.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("[Pawns] The pawn hand could not be created (UIDocument has no root element).");
                enabled = false;
                return;
            }

            _turnTimer = FindAnyObjectByType<TurnTimerController>();
            BuildWindow(root);

            _pawnManager.TargetingChanged += MarkDirty;
            _pawnManager.CardSelectFailed += OnCardSelectFailed;
        }

        private void OnDestroy()
        {
            if (_pawnManager != null)
            {
                _pawnManager.TargetingChanged -= MarkDirty;
                _pawnManager.CardSelectFailed -= OnCardSelectFailed;
            }
        }

        private void LateUpdate()
        {
            PawnController pawn = _pawnManager.SelectedPawn;
            Pawn state = pawn != null ? pawn.Pawn : null;
            bool canChoose = pawn != null && pawn.IsOwnedByLocalPlayer && IsPlayerPhase();

            if (_error != null && Time.unscaledTime > _errorUntil)
            {
                _error = null;
                _dirty = true;
            }

            // The PawnManager replaces a pawn's state object whenever the server sends an update, so a new reference means new data
            if (!_dirty && pawn == _shownPawn && state == _shownState && canChoose == _shownCanChoose && _pawnManager.TargetingCard == _shownTargetingCard)
            {
                return;
            }

            if (pawn != _shownPawn)
            {
                _error = null;
            }

            _shownPawn = pawn;
            _shownState = state;
            _shownCanChoose = canChoose;
            _shownTargetingCard = _pawnManager.TargetingCard;
            _dirty = false;
            Show(pawn, canChoose);
        }

        private void MarkDirty()
        {
            _dirty = true;
        }

        private void OnCardSelectFailed(string error)
        {
            _error = error;
            _errorUntil = Time.unscaledTime + _errorSeconds;
            _dirty = true;
        }

        private bool IsPlayerPhase()
        {
            if (_turnTimer == null)
            {
                _turnTimer = FindAnyObjectByType<TurnTimerController>();
            }
            return _turnTimer != null && _turnTimer.IsPlayerPhase;
        }

        /// <summary>
        /// Fills the window with <paramref name="pawn"/>'s hand, or hides it when no pawn is selected
        /// </summary>
        private void Show(PawnController pawn, bool canChoose)
        {
            if (pawn == null || pawn.Pawn == null)
            {
                _window.style.display = DisplayStyle.None;
                return;
            }

            Pawn state = pawn.Pawn;
            _window.style.display = DisplayStyle.Flex;
            _titleLabel.text = $"{state.Name}'s hand";
            _ownerLabel.text = pawn.IsOwnedByLocalPlayer ? "You control this pawn"
                : state.OwnerId.HasValue ? $"Controlled by {state.OwnerName}"
                : "Acts on its own";
            _deckLabel.text = $"Deck {state.Deck.Count}";

            _cardRow.Clear();
            foreach (Card card in state.Hand)
            {
                _cardRow.Add(CreateCard(pawn, card, canChoose));
            }
            if (state.Hand.Count == 0)
            {
                Label empty = CreateLabel(13, HintColour, "No cards in hand. A new hand is drawn at the start of the next turn.");
                empty.style.whiteSpace = WhiteSpace.Normal;
                _cardRow.Add(empty);
            }

            ShowStatus(pawn, canChoose);
        }

        /// <summary>
        /// The line under the cards: an error from the server, what to do next while targeting, or when cards can be chosen
        /// </summary>
        private void ShowStatus(PawnController pawn, bool canChoose)
        {
            if (_error != null)
            {
                _statusLabel.text = _error;
                _statusLabel.style.color = ErrorColour;
                return;
            }

            _statusLabel.style.color = HintColour;
            if (_pawnManager.IsTargeting && _pawnManager.TargetingPawn == pawn)
            {
                _statusLabel.text = $"Click a tile to aim {_pawnManager.TargetingCard.Name}. Right-click or Esc to cancel.";
            }
            else if (canChoose)
            {
                _statusLabel.text = "Click a card to play it this turn.";
            }
            else if (pawn.IsOwnedByLocalPlayer)
            {
                _statusLabel.text = "You can change this pawn's card in the Player phase.";
            }
            else
            {
                _statusLabel.text = "You can look at this hand, but you don't control this pawn.";
            }
        }

        private VisualElement CreateCard(PawnController pawn, Card card, bool canChoose)
        {
            Pawn state = pawn.Pawn;
            bool chosen = state.SelectedCard != null && state.SelectedCard.CardId == card.PublicId;
            bool targeting = _pawnManager.IsTargeting && _pawnManager.TargetingPawn == pawn && _pawnManager.TargetingCard.PublicId == card.PublicId;

            VisualElement element = new VisualElement();
            element.pickingMode = canChoose ? PickingMode.Position : PickingMode.Ignore;
            element.style.width = 120;
            element.style.flexShrink = 0;
            element.style.minHeight = 140;
            element.style.marginLeft = 4;
            element.style.marginRight = 4;
            element.style.paddingLeft = 8;
            element.style.paddingRight = 8;
            element.style.paddingTop = 8;
            element.style.paddingBottom = 8;
            element.style.backgroundColor = CardColour;
            SetBorder(element, chosen || targeting ? 3 : 1, targeting ? TargetingBorderColour : chosen ? ChosenBorderColour : PlainBorderColour);
            SetRadius(element, 6);
            if (!canChoose && !chosen)
            {
                element.style.opacity = 0.85f;
            }

            Label name = CreateLabel(14, Color.white, card.Name);
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.style.whiteSpace = WhiteSpace.Normal;
            name.style.marginBottom = 4;
            element.Add(name);

            Label description = CreateLabel(12, HintColour, card.Description);
            description.style.whiteSpace = WhiteSpace.Normal;
            element.Add(description);

            VisualElement spacer = new VisualElement();
            spacer.pickingMode = PickingMode.Ignore;
            spacer.style.flexGrow = 1;
            element.Add(spacer);

            List<string> tags = new List<string>();
            if (card.TargetType == CardTargetType.Tile)
            {
                tags.Add(chosen && state.SelectedCard.Target != null
                    ? $"Aimed at {state.SelectedCard.Target.X}, {state.SelectedCard.Target.Y}"
                    : "Aim at a tile");
            }
            if (chosen)
            {
                tags.Add("Chosen");
            }
            if (tags.Count > 0)
            {
                Label tag = CreateLabel(11, chosen ? ChosenBorderColour : HintColour, string.Join(" · ", tags));
                tag.style.whiteSpace = WhiteSpace.Normal;
                tag.style.marginTop = 6;
                element.Add(tag);
            }

            if (canChoose)
            {
                element.RegisterCallback<PointerEnterEvent>(_ => element.style.backgroundColor = CardHoverColour);
                element.RegisterCallback<PointerLeaveEvent>(_ => element.style.backgroundColor = CardColour);
                element.RegisterCallback<ClickEvent>(_ => OnCardClicked(pawn, card));
            }
            return element;
        }

        /// <summary>
        /// Chooses the card straight away if it needs no target; otherwise waits for a tile to be clicked
        /// </summary>
        private async void OnCardClicked(PawnController pawn, Card card)
        {
            _error = null;
            if (card.TargetType == CardTargetType.Tile)
            {
                _pawnManager.BeginTargeting(pawn, card);
                return;
            }

            _pawnManager.CancelTargeting();
            await _pawnManager.SelectCard(pawn, card, null);
        }

        private void BuildWindow(VisualElement root)
        {
            // Let clicks outside the window through to the game
            root.pickingMode = PickingMode.Ignore;

            _window = new VisualElement();
            _window.style.position = Position.Absolute;
            _window.style.right = 16;
            _window.style.bottom = 16;
            _window.style.paddingLeft = 12;
            _window.style.paddingRight = 12;
            _window.style.paddingTop = 10;
            _window.style.paddingBottom = 10;
            _window.style.backgroundColor = new Color(0f, 0f, 0f, 0.65f);
            SetRadius(_window, 6);
            _window.style.display = DisplayStyle.None;
            root.Add(_window);

            VisualElement header = new VisualElement();
            header.pickingMode = PickingMode.Ignore;
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 8;
            _window.Add(header);

            _titleLabel = CreateLabel(16, Color.white);
            _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _titleLabel.style.marginRight = 10;
            header.Add(_titleLabel);

            _ownerLabel = CreateLabel(13, HintColour);
            _ownerLabel.style.flexGrow = 1;
            header.Add(_ownerLabel);

            _deckLabel = CreateLabel(13, HintColour);
            _deckLabel.style.marginLeft = 10;
            header.Add(_deckLabel);

            _cardRow = new VisualElement();
            _cardRow.pickingMode = PickingMode.Ignore;
            _cardRow.style.flexDirection = FlexDirection.Row;
            _cardRow.style.flexWrap = Wrap.NoWrap;
            _cardRow.style.marginLeft = -4;
            _cardRow.style.marginRight = -4;
            _window.Add(_cardRow);

            _statusLabel = CreateLabel(12, HintColour);
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.marginTop = 8;
            _window.Add(_statusLabel);
        }

        private static void SetBorder(VisualElement element, float width, Color colour)
        {
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopColor = colour;
            element.style.borderBottomColor = colour;
            element.style.borderLeftColor = colour;
            element.style.borderRightColor = colour;
        }

        private static void SetRadius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        private static Label CreateLabel(int fontSize, Color colour, string text = "")
        {
            Label label = new Label(text);
            label.pickingMode = PickingMode.Ignore;
            label.enableRichText = false;
            label.style.fontSize = fontSize;
            label.style.color = colour;
            label.style.paddingLeft = 0;
            label.style.paddingRight = 0;
            return label;
        }
    }
}

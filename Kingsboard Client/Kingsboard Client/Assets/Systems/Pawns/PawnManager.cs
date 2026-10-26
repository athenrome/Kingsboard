using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kingsboard.Board;
using Kingsboard.Players;
using Kingsboard.Shared.Models.Board;
using Kingsboard.Shared.Models.Cards;
using Kingsboard.Shared.Models.Pawns;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.InputSystem;

namespace Kingsboard.Pawns
{
    /// <summary>
    /// Looks after the pawns as a group on the client: receives them from the server (through <see cref="PawnGrpcHandler"/>),
    /// spawns a <see cref="PawnController"/> for each new pawn and despawns pawns that are gone, passes each pawn its own
    /// plans, moves, speech and turn ends, and keeps one pawn selected at a time (click a pawn to select it, click elsewhere
    /// to deselect). It also sends our card choices to the server, including asking for a tile first for cards that need
    /// one (<see cref="BeginTargeting"/>). Everything an individual pawn does is up to its <see cref="PawnController"/>.
    /// Lives on the Pawns prefab (Assets/Prefabs/Pawns/Pawns.prefab), placed in the scene. Every pawn is an instance of the
    /// Pawn prefab, created under the prefab's Pawn Views child.
    /// </summary>
    public class PawnManager : MonoBehaviour
    {
        [Tooltip("Prefab spawned for every pawn (Assets/Prefabs/Pawns/Pawn.prefab)")]
        [SerializeField, FormerlySerializedAs("pawnPrefab")] private PawnController _pawnPrefab;

        [Tooltip("Where pawns are spawned (the Pawn Views child of the Pawns prefab)")]
        [SerializeField, FormerlySerializedAs("pawnRoot")] private Transform _pawnRoot;

        [Tooltip("The selected pawn's hand (the nested PawnHand prefab). Clicks on it aren't treated as clicks on the board.")]
        [SerializeField] private PawnHandController _hand;

        [Tooltip("Select pawns by clicking them with the left mouse button")]
        [SerializeField, FormerlySerializedAs("clickToSelect")] private bool _clickToSelect = true;

        [Tooltip("Furthest a click can reach into the scene, in world units")]
        [SerializeField, FormerlySerializedAs("maxClickDistance")] private float _maxClickDistance = 500f;

        private PawnGrpcHandler _handler;
        private BoardController _board;
        private PlayerController _player;
        private readonly Dictionary<Guid, PawnController> _pawns = new Dictionary<Guid, PawnController>();

        /// <summary>
        /// Every spawned pawn's controller, by pawn id
        /// </summary>
        public IReadOnlyDictionary<Guid, PawnController> Pawns => _pawns;

        /// <summary>
        /// The selected pawn, or null if none is
        /// </summary>
        public PawnController SelectedPawn { get; private set; }

        /// <summary>
        /// The card waiting for a tile to be clicked (see <see cref="BeginTargeting"/>), or null when not targeting
        /// </summary>
        public Card TargetingCard { get; private set; }

        /// <summary>
        /// The pawn the <see cref="TargetingCard"/> is for, or null when not targeting
        /// </summary>
        public PawnController TargetingPawn { get; private set; }

        /// <summary>
        /// True while waiting for a tile to be clicked
        /// </summary>
        public bool IsTargeting => TargetingCard != null;

        /// <summary>
        /// Our player id, or null while we aren't joined to the game
        /// </summary>
        public Guid? LocalPlayerId => _player != null && _player.Player != null ? _player.Player.PlayerId : (Guid?)null;

        /// <summary>
        /// Raised after a pawn is spawned
        /// </summary>
        public event Action<PawnController> PawnSpawned;

        /// <summary>
        /// Raised just before a pawn is despawned
        /// </summary>
        public event Action<PawnController> PawnDespawning;

        /// <summary>
        /// Raised when the selected pawn changes (null when nothing is selected)
        /// </summary>
        public event Action<PawnController> SelectionChanged;

        /// <summary>
        /// Raised when targeting starts or stops
        /// </summary>
        public event Action TargetingChanged;

        /// <summary>
        /// Raised when the server turns down a card choice we sent, with its reason
        /// </summary>
        public event Action<string> CardSelectFailed;

        /// <summary>
        /// Selects <paramref name="pawn"/>, deselecting whichever pawn was selected before. Pass null to deselect everything.
        /// Selecting a pawn directly through <see cref="PawnController.Select"/> has the same effect.
        /// </summary>
        public void Select(PawnController pawn)
        {
            if (pawn == null)
            {
                if (SelectedPawn != null)
                {
                    SelectedPawn.Deselect();
                }
                return;
            }

            // The pawn raises Selected, and OnPawnSelected does the bookkeeping
            pawn.Select();
        }

        /// <summary>
        /// Asks the server for all of one pawn's details (see <see cref="PawnGrpcHandler.GetPawnByIdAsync"/>).
        /// Returns null if no pawn has that id, the request fails, or the pawns aren't connected.
        /// </summary>
        public Task<Pawn> GetPawnById(Guid id)
        {
            if (_handler == null)
            {
                return Task.FromResult<Pawn>(null);
            }
            return _handler.GetPawnByIdAsync(id);
        }

        /// <summary>
        /// Asks the server to have <paramref name="pawn"/> play <paramref name="card"/> this turn, aimed at
        /// <paramref name="target"/> (null for cards that need no target). Returns null if accepted, or the reason it
        /// wasn't (also raised as <see cref="CardSelectFailed"/>). The server decides; this only asks.
        /// </summary>
        public async Task<string> SelectCard(PawnController pawn, Card card, BoardPosition target)
        {
            string error;
            if (_handler == null || _player == null || _player.Player == null)
            {
                error = "Not joined to the game yet";
            }
            else if (pawn == null || pawn.Pawn == null || card == null)
            {
                error = "No pawn or card to choose";
            }
            else
            {
                error = await _handler.SelectCardAsync(_player.Player.SessionId, pawn.Pawn.PublicId, card.PublicId, target);
            }

            if (error != null && this != null)
            {
                CardSelectFailed?.Invoke(error);
            }
            return error;
        }

        /// <summary>
        /// Waits for a tile to be clicked to aim <paramref name="card"/>, then asks the server for it as
        /// <paramref name="pawn"/>'s card. Right-click or Escape cancels. Replaces any targeting already under way.
        /// </summary>
        public void BeginTargeting(PawnController pawn, Card card)
        {
            TargetingPawn = pawn;
            TargetingCard = card;
            if (_player != null)
            {
                _player.State.Begin(PlayerAction.TargetingCard);
            }
            TargetingChanged?.Invoke();
        }

        /// <summary>
        /// Stops waiting for a tile. Does nothing if not targeting.
        /// </summary>
        public void CancelTargeting()
        {
            if (!IsTargeting)
            {
                return;
            }

            TargetingPawn = null;
            TargetingCard = null;
            if (_player != null)
            {
                _player.State.End(PlayerAction.TargetingCard);
            }
            TargetingChanged?.Invoke();
        }

        private void Start()
        {
            if (_pawnPrefab == null || _pawnRoot == null)
            {
                Debug.LogError("[Pawns] The PawnManager needs its Pawn prefab and Pawn Views root assigned to spawn pawns. Use the Pawns prefab.", this);
                enabled = false;
                return;
            }

            _player = FindAnyObjectByType<PlayerController>();
            if (_player != null)
            {
                _player.Joined += OnPlayerJoined;
                _player.Left += OnPlayerLeft;
            }
            else
            {
                Debug.LogWarning("[Pawns] No PlayerController in the scene, so we can't control any pawns. Place the Player prefab.", this);
            }

            _handler = new PawnGrpcHandler();
            _handler.PawnsReset += OnPawnsReset;
            _handler.PawnsUpdated += OnPawnsUpdated;
            _handler.PawnsPlanned += OnPawnsPlanned;
            _handler.PawnsResolved += OnPawnsResolved;
            _handler.TurnEnded += OnTurnEnded;
            _handler.Disconnected += OnDisconnected;
            _handler.Connect();
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (IsTargeting)
            {
                // Escape while typing in chat only stops the typing
                bool enteringText = _player != null && _player.State.IsEnteringText;
                bool cancel = (mouse != null && mouse.rightButton.wasPressedThisFrame)
                              || (!enteringText && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame);
                if (cancel)
                {
                    CancelTargeting();
                    return;
                }
            }

            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
            {
                return;
            }

            Vector2 pointer = mouse.position.ReadValue();
            if (_hand != null && _hand.ContainsScreenPoint(pointer))
            {
                // The hand handles its own clicks
                return;
            }

            if (IsTargeting)
            {
                AimAtPointer(pointer);
            }
            else if (_clickToSelect)
            {
                SelectUnderPointer(pointer);
            }
        }

        private void OnDestroy()
        {
            if (_handler != null)
            {
                _handler.PawnsReset -= OnPawnsReset;
                _handler.PawnsUpdated -= OnPawnsUpdated;
                _handler.PawnsPlanned -= OnPawnsPlanned;
                _handler.PawnsResolved -= OnPawnsResolved;
                _handler.TurnEnded -= OnTurnEnded;
                _handler.Disconnected -= OnDisconnected;
                _handler.Dispose();
                _handler = null;
            }

            if (_player != null)
            {
                _player.Joined -= OnPlayerJoined;
                _player.Left -= OnPlayerLeft;
                _player.State.End(PlayerAction.TargetingCard);
            }

            if (_board != null)
            {
                _board.SectionLoaded -= OnSectionLoaded;
            }
        }

        /// <summary>
        /// The server sent a full set of pawns: despawn pawns that aren't in it, update the ones that are, spawn the new ones
        /// </summary>
        private void OnPawnsReset(IReadOnlyList<Pawn> pawns)
        {
            CancelTargeting();

            HashSet<Guid> ids = new HashSet<Guid>(pawns.Select(p => p.PublicId));
            foreach (Guid gone in _pawns.Keys.Where(id => !ids.Contains(id)).ToList())
            {
                Despawn(gone);
            }

            BoardController board = FindBoard();
            foreach (Pawn pawn in pawns)
            {
                if (_pawns.TryGetValue(pawn.PublicId, out PawnController existing))
                {
                    existing.Initialise(pawn, board);
                }
                else
                {
                    Spawn(pawn, board);
                }
            }
        }

        private void OnPawnsUpdated(IReadOnlyList<Pawn> pawns)
        {
            foreach (Pawn pawn in pawns)
            {
                if (_pawns.TryGetValue(pawn.PublicId, out PawnController controller))
                {
                    controller.UpdatePawn(pawn);
                }
            }

            // Stop targeting for a pawn we no longer control
            if (IsTargeting && (TargetingPawn == null || !TargetingPawn.IsOwnedByLocalPlayer))
            {
                CancelTargeting();
            }
        }

        private void OnPawnsPlanned(int turnId, IReadOnlyList<Pawn> pawns)
        {
            foreach (Pawn pawn in pawns)
            {
                if (_pawns.TryGetValue(pawn.PublicId, out PawnController controller))
                {
                    controller.PlanMove(pawn);
                }
            }
        }

        private void OnPawnsResolved(int turnId, IReadOnlyList<PawnMovement> movements, IReadOnlyList<PawnSpeech> speeches, IReadOnlyList<Pawn> pawns)
        {
            // Cards can't be chosen once they're being played
            CancelTargeting();

            Dictionary<Guid, PawnMovement> moved = movements.ToDictionary(m => m.PawnId);
            foreach (Pawn pawn in pawns)
            {
                if (!_pawns.TryGetValue(pawn.PublicId, out PawnController controller))
                {
                    continue;
                }

                if (moved.TryGetValue(pawn.PublicId, out PawnMovement movement))
                {
                    controller.Move(movement, pawn);
                }
                else
                {
                    controller.UpdatePawn(pawn);
                }
            }

            foreach (PawnSpeech speech in speeches)
            {
                if (_pawns.TryGetValue(speech.PawnId, out PawnController controller))
                {
                    controller.Say(speech.Text, turnId);
                }
            }
        }

        private void OnTurnEnded(int turnId, IReadOnlyList<Pawn> pawns)
        {
            CancelTargeting();

            foreach (Pawn pawn in pawns)
            {
                if (_pawns.TryGetValue(pawn.PublicId, out PawnController controller))
                {
                    controller.UpdatePawn(pawn);
                    controller.EndTurn(turnId);
                }
            }
        }

        private void OnDisconnected()
        {
            // Keep showing the last pawns; the server sends them all again when we reconnect
            Debug.LogWarning("[Pawns] Disconnected from the pawn server. Reconnecting...");
        }

        private void OnPlayerJoined(PlayerInfo player)
        {
            RefreshLocalPlayer();
        }

        private void OnPlayerLeft()
        {
            CancelTargeting();
            RefreshLocalPlayer();
        }

        private void RefreshLocalPlayer()
        {
            Guid? localPlayerId = LocalPlayerId;
            foreach (PawnController pawn in _pawns.Values)
            {
                pawn.LocalPlayerId = localPlayerId;
            }
        }

        private void OnSectionLoaded(BoardSection section)
        {
            foreach (PawnController pawn in _pawns.Values)
            {
                pawn.RefreshVisibility();
            }
        }

        private void Spawn(Pawn pawn, BoardController board)
        {
            PawnController controller = Instantiate(_pawnPrefab, _pawnRoot, false);
            controller.Initialise(pawn, board);
            controller.LocalPlayerId = LocalPlayerId;
            controller.Selected += OnPawnSelected;
            controller.Deselected += OnPawnDeselected;
            _pawns[pawn.PublicId] = controller;
            PawnSpawned?.Invoke(controller);
        }

        private void Despawn(Guid pawnId)
        {
            if (!_pawns.TryGetValue(pawnId, out PawnController controller))
            {
                return;
            }

            controller.Deselect();
            PawnDespawning?.Invoke(controller);
            controller.Selected -= OnPawnSelected;
            controller.Deselected -= OnPawnDeselected;
            _pawns.Remove(pawnId);
            Destroy(controller.gameObject);
        }

        /// <summary>
        /// Keeps one pawn selected at a time: a newly selected pawn deselects the previous one
        /// </summary>
        private void OnPawnSelected(PawnController pawn)
        {
            if (SelectedPawn == pawn)
            {
                return;
            }

            CancelTargeting();
            PawnController previous = SelectedPawn;
            SelectedPawn = pawn;
            if (previous != null)
            {
                previous.Deselect();
            }
            SelectionChanged?.Invoke(pawn);
        }

        private void OnPawnDeselected(PawnController pawn)
        {
            if (SelectedPawn != pawn)
            {
                return;
            }

            CancelTargeting();
            SelectedPawn = null;
            SelectionChanged?.Invoke(null);
        }

        /// <summary>
        /// Selects the pawn under a screen point, or deselects if there isn't one
        /// </summary>
        private void SelectUnderPointer(Vector2 screenPoint)
        {
            Select(RaycastPointer(screenPoint, out RaycastHit hitInfo) ? hitInfo.collider.GetComponentInParent<PawnController>() : null);
        }

        /// <summary>
        /// Aims the card being targeted at the board position under a screen point, then stops targeting. Clicking
        /// anything that isn't the board (or a pawn standing on it) keeps waiting.
        /// </summary>
        private async void AimAtPointer(Vector2 screenPoint)
        {
            BoardController board = FindBoard();
            if (board == null || !RaycastPointer(screenPoint, out RaycastHit hitInfo))
            {
                return;
            }

            // Nudge into whatever was hit, so a click on a tile's top face picks that tile's column
            BoardPosition target = board.WorldToPosition(hitInfo.point - hitInfo.normal * 0.01f);
            PawnController pawn = TargetingPawn;
            Card card = TargetingCard;
            CancelTargeting();

            await SelectCard(pawn, card, target);
        }

        private bool RaycastPointer(Vector2 screenPoint, out RaycastHit hitInfo)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                hitInfo = default;
                return false;
            }
            return Physics.Raycast(camera.ScreenPointToRay(screenPoint), out hitInfo, _maxClickDistance);
        }

        /// <summary>
        /// The board pawns stand on, found in the scene the first time it's needed
        /// </summary>
        private BoardController FindBoard()
        {
            if (_board == null)
            {
                _board = FindAnyObjectByType<BoardController>();
                if (_board != null)
                {
                    _board.SectionLoaded += OnSectionLoaded;
                }
            }
            return _board;
        }
    }
}

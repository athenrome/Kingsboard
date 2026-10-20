using System;
using System.Collections.Generic;
using System.Linq;
using Kingsboard.Board;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.InputSystem;

namespace Kingsboard.Pawns
{
    /// <summary>
    /// Looks after the pawns as a group on the client: receives them from the server (through <see cref="PawnGrpcHandler"/>),
    /// spawns a <see cref="PawnController"/> for each new pawn and despawns pawns that are gone, passes each pawn its own
    /// plans and moves, and keeps one pawn selected at a time (click a pawn to select it, click elsewhere to deselect).
    /// Everything an individual pawn does is up to its <see cref="PawnController"/>.
    /// Lives on the Pawns prefab (Assets/Prefabs/Pawns/Pawns.prefab), placed in the scene. Every pawn is an instance of the
    /// Pawn prefab, created under the prefab's Pawn Views child.
    /// </summary>
    public class PawnManager : MonoBehaviour
    {
        [Tooltip("Prefab spawned for every pawn (Assets/Prefabs/Pawns/Pawn.prefab)")]
        [SerializeField, FormerlySerializedAs("pawnPrefab")] private PawnController _pawnPrefab;

        [Tooltip("Where pawns are spawned (the Pawn Views child of the Pawns prefab)")]
        [SerializeField, FormerlySerializedAs("pawnRoot")] private Transform _pawnRoot;

        [Tooltip("Select pawns by clicking them with the left mouse button")]
        [SerializeField, FormerlySerializedAs("clickToSelect")] private bool _clickToSelect = true;

        [Tooltip("Furthest a click can reach into the scene, in world units")]
        [SerializeField, FormerlySerializedAs("maxClickDistance")] private float _maxClickDistance = 500f;

        private PawnGrpcHandler _handler;
        private BoardController _board;
        private readonly Dictionary<string, PawnController> _pawns = new Dictionary<string, PawnController>();

        /// <summary>
        /// Every spawned pawn's controller, by pawn id
        /// </summary>
        public IReadOnlyDictionary<string, PawnController> Pawns => _pawns;

        /// <summary>
        /// The selected pawn, or null if none is
        /// </summary>
        public PawnController SelectedPawn { get; private set; }

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

        private void Start()
        {
            if (_pawnPrefab == null || _pawnRoot == null)
            {
                Debug.LogError("[Pawns] The PawnManager needs its Pawn prefab and Pawn Views root assigned to spawn pawns. Use the Pawns prefab.", this);
                enabled = false;
                return;
            }

            _handler = new PawnGrpcHandler();
            _handler.PawnsReset += OnPawnsReset;
            _handler.PawnsPlanned += OnPawnsPlanned;
            _handler.PawnsMoved += OnPawnsMoved;
            _handler.Disconnected += OnDisconnected;
            _handler.Connect();
        }

        private void Update()
        {
            if (_clickToSelect && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                SelectUnderPointer(Mouse.current.position.ReadValue());
            }
        }

        private void OnDestroy()
        {
            if (_handler != null)
            {
                _handler.PawnsReset -= OnPawnsReset;
                _handler.PawnsPlanned -= OnPawnsPlanned;
                _handler.PawnsMoved -= OnPawnsMoved;
                _handler.Disconnected -= OnDisconnected;
                _handler.Dispose();
                _handler = null;
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
            HashSet<string> ids = new HashSet<string>(pawns.Select(p => p.Id));
            foreach (string gone in _pawns.Keys.Where(id => !ids.Contains(id)).ToList())
            {
                Despawn(gone);
            }

            BoardController board = FindBoard();
            foreach (Pawn pawn in pawns)
            {
                if (_pawns.TryGetValue(pawn.Id, out PawnController existing))
                {
                    existing.Initialise(pawn, board);
                }
                else
                {
                    Spawn(pawn, board);
                }
            }
        }

        private void OnPawnsPlanned(int turnId, IReadOnlyList<Pawn> pawns)
        {
            foreach (Pawn pawn in pawns)
            {
                if (_pawns.TryGetValue(pawn.Id, out PawnController controller))
                {
                    controller.PlanMove(pawn);
                }
            }
        }

        private void OnPawnsMoved(int turnId, IReadOnlyList<PawnMovement> movements, IReadOnlyList<Pawn> pawns)
        {
            Dictionary<string, Pawn> after = pawns.ToDictionary(p => p.Id);
            foreach (PawnMovement movement in movements)
            {
                if (_pawns.TryGetValue(movement.PawnId, out PawnController controller) && after.TryGetValue(movement.PawnId, out Pawn pawn))
                {
                    controller.Move(movement, pawn);
                }
            }
        }

        private void OnDisconnected()
        {
            // Keep showing the last pawns; the server sends them all again when we reconnect
            Debug.LogWarning("[Pawns] Disconnected from the pawn server. Reconnecting...");
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
            controller.Selected += OnPawnSelected;
            controller.Deselected += OnPawnDeselected;
            _pawns[pawn.Id] = controller;
            PawnSpawned?.Invoke(controller);
        }

        private void Despawn(string pawnId)
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

            SelectedPawn = null;
            SelectionChanged?.Invoke(null);
        }

        /// <summary>
        /// Selects the pawn under a screen point, or deselects if there isn't one
        /// </summary>
        private void SelectUnderPointer(Vector2 screenPoint)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            Ray ray = camera.ScreenPointToRay(screenPoint);
            PawnController hit = null;
            if (Physics.Raycast(ray, out RaycastHit hitInfo, _maxClickDistance))
            {
                hit = hitInfo.collider.GetComponentInParent<PawnController>();
            }
            Select(hit);
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

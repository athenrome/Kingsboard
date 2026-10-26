using System;
using System.Collections;
using Kingsboard.Board;
using Kingsboard.Shared.Models.Board;
using Kingsboard.Shared.Models.Pawns;
using UnityEngine;
using UnityEngine.Serialization;

namespace Kingsboard.Pawns
{
    /// <summary>
    /// Drives one pawn on the client: its look (name, colour and who controls it), showing the move it has planned, walking
    /// its path when moves resolve, saying things in a speech bubble, hiding while it's outside the loaded board section,
    /// and being selected. Every pawn has its own.
    /// Lives on the root of the Pawn prefab (Assets/Prefabs/Pawns/Pawn.prefab); the <see cref="PawnManager"/> spawns one per
    /// pawn the server sends and passes it that pawn's updates. The server makes every decision; this only shows them.
    /// The prefab's pivot is at the pawn's feet, so placing its transform at a standing point stands it on the board.
    /// </summary>
    public class PawnController : MonoBehaviour
    {
        [Tooltip("The pawn's body; tinted with the pawn's colour")]
        [SerializeField, FormerlySerializedAs("body")] private Renderer _body;

        [Tooltip("Name label above the pawn; turned to face the camera every frame")]
        [SerializeField, FormerlySerializedAs("label")] private TextMesh _label;

        [Tooltip("Line showing the pawn's planned path (the nested PawnPath prefab). Drawn in world space, so it stays put while the pawn walks.")]
        [SerializeField, FormerlySerializedAs("plannedPath")] private LineRenderer _plannedPath;

        [Tooltip("Shown while the pawn is selected")]
        [SerializeField, FormerlySerializedAs("selectionIndicator")] private GameObject _selectionIndicator;

        [Tooltip("Speech bubble over the pawn's head (the nested SpeechBubble prefab), shown when a card makes it talk")]
        [SerializeField] private SpeechBubbleController _speechBubble;

        [Tooltip("Seconds the pawn takes to walk one step of its path")]
        [SerializeField, FormerlySerializedAs("secondsPerStep")] private float _secondsPerStep = 0.25f;

        [Tooltip("How high the pawn hops on each step, in world units")]
        [SerializeField, FormerlySerializedAs("hopHeight")] private float _hopHeight = 0.15f;

        [Tooltip("Seconds the pawn takes to turn to face a new step; capped at the time the step takes")]
        [SerializeField] private float _secondsPerTurn = 0.1f;

        [Tooltip("Degrees to turn the pawn about its up axis so the model faces the way it walks (the miniature model faces -Z, hence 180)")]
        [SerializeField] private float _modelFacingYaw = 180f;

        [Tooltip("Show the pawn's planned path as a line")]
        [SerializeField, FormerlySerializedAs("showPlannedPath")] private bool _showPlannedPath = true;

        private BoardController _board;
        private Material _bodyMaterial;
        private Material _pathMaterial;
        private Coroutine _walk;
        private Guid? _localPlayerId;
        private int _spokeInTurn;

        /// <summary>
        /// The latest state of this pawn from the server
        /// </summary>
        public Pawn Pawn { get; private set; }

        /// <summary>
        /// Our player id (null while we aren't joined), so the pawn can show whether we control it.
        /// Set by the <see cref="PawnManager"/>.
        /// </summary>
        public Guid? LocalPlayerId
        {
            get => _localPlayerId;
            set
            {
                _localPlayerId = value;
                RefreshLabel();
            }
        }

        /// <summary>
        /// True if we control this pawn, so we can choose its cards
        /// </summary>
        public bool IsOwnedByLocalPlayer => Pawn != null && Pawn.OwnerId.HasValue && Pawn.OwnerId == _localPlayerId;

        /// <summary>
        /// True while the pawn is selected
        /// </summary>
        public bool IsSelected { get; private set; }

        /// <summary>
        /// True while the pawn is walking its path
        /// </summary>
        public bool IsMoving => _walk != null;

        /// <summary>
        /// Raised when the pawn becomes selected
        /// </summary>
        public event Action<PawnController> Selected;

        /// <summary>
        /// Raised when the pawn stops being selected
        /// </summary>
        public event Action<PawnController> Deselected;

        /// <summary>
        /// Sets the pawn up: its look, where it stands, and any plan it already has.
        /// <paramref name="board"/> is used to place it on the board and to know which section is loaded (it may be null).
        /// </summary>
        public void Initialise(Pawn pawn, BoardController board)
        {
            _board = board;
            UpdatePawn(pawn);
            StandAt(pawn.Position);
            SetSelected(false);
            if (_speechBubble != null)
            {
                _speechBubble.Hide();
            }
        }

        /// <summary>
        /// Takes the pawn's latest state from the server without moving it (e.g. after a reconnect, a new hand or a new
        /// owner). Recolours it (a pawn takes its owner's colour), and shows its current plan, if any.
        /// </summary>
        public void UpdatePawn(Pawn pawn)
        {
            Pawn = pawn;
            ApplyLook(pawn);
            ShowPlan();
            RefreshLabel();
        }

        /// <summary>
        /// Shows <paramref name="text"/> in a speech bubble over the pawn, said during turn <paramref name="turnId"/>.
        /// It stays up until the end of the next turn (see <see cref="EndTurn"/>), then fades.
        /// </summary>
        public void Say(string text, int turnId)
        {
            if (_speechBubble == null)
            {
                return;
            }

            _spokeInTurn = turnId;
            _speechBubble.Show(text);
        }

        /// <summary>
        /// Turn <paramref name="turnId"/> has ended. A pawn speaks during Resolution, just before its turn ends, so its
        /// speech bubble is kept through the following turn and fades at that turn's end.
        /// </summary>
        public void EndTurn(int turnId)
        {
            if (_speechBubble != null && _speechBubble.IsShowing && turnId > _spokeInTurn)
            {
                _speechBubble.FadeOut();
            }
        }

        /// <summary>
        /// The pawn has chosen its card for the turn (Planning phase): show the path it intends to walk, if the card moves it
        /// </summary>
        public void PlanMove(Pawn pawn)
        {
            UpdatePawn(pawn);
        }

        /// <summary>
        /// The pawn's move has resolved (Resolution phase): walk it along the path it took, then settle it where it ended up
        /// </summary>
        public void Move(PawnMovement movement, Pawn pawn)
        {
            Pawn = pawn;
            if (_plannedPath != null)
            {
                _plannedPath.positionCount = 0;
            }

            if (_walk != null)
            {
                StopCoroutine(_walk);
                _walk = null;
            }

            // A hidden (inactive) pawn can't run a coroutine, so just put it where it ended up
            if (!isActiveAndEnabled)
            {
                StandAt(movement.Position);
                return;
            }
            _walk = StartCoroutine(Walk(movement));
        }

        /// <summary>
        /// Selects the pawn: shows its selection indicator and raises <see cref="Selected"/>. Does nothing if it's already selected.
        /// </summary>
        public void Select()
        {
            if (!IsSelected)
            {
                SetSelected(true);
                Selected?.Invoke(this);
            }
        }

        /// <summary>
        /// Deselects the pawn: hides its selection indicator and raises <see cref="Deselected"/>. Does nothing if it isn't selected.
        /// </summary>
        public void Deselect()
        {
            if (IsSelected)
            {
                SetSelected(false);
                Deselected?.Invoke(this);
            }
        }

        /// <summary>
        /// Puts the pawn back on the board after a new section loads. The board slides to centre each section, so this
        /// cuts short any walk, stands the pawn where it is now, redraws its plan, and shows it only while it stands inside
        /// the loaded section (pawns elsewhere have no ground drawn under them).
        /// </summary>
        public void RefreshPlacement()
        {
            if (_walk != null)
            {
                StopCoroutine(_walk);
                _walk = null;
            }

            StandAt(Pawn != null ? Pawn.Position : new BoardPosition(0, 0, 0));
            ShowPlan();
        }

        private void LateUpdate()
        {
            // Keep the name label facing the camera
            Camera camera = Camera.main;
            if (_label != null && camera != null)
            {
                _label.transform.rotation = camera.transform.rotation;
            }
        }

        private void OnDisable()
        {
            // Coroutines stop when the pawn is hidden; forget the walk so it isn't treated as still moving
            _walk = null;
        }

        private void OnDestroy()
        {
            if (_bodyMaterial != null)
            {
                Destroy(_bodyMaterial);
            }
            if (_pathMaterial != null)
            {
                Destroy(_pathMaterial);
            }
        }

        /// <summary>
        /// Steps through each position of the path, turning to face each step, and hides the pawn when it walks out of the
        /// loaded section. The pawn keeps facing its last step once it stops.
        /// </summary>
        private IEnumerator Walk(PawnMovement movement)
        {
            float stepSeconds = Mathf.Max(0.01f, _secondsPerStep);
            float turnFraction = Mathf.Clamp(_secondsPerTurn / stepSeconds, 0.01f, 1f);
            foreach (BoardPosition step in movement.Path)
            {
                Vector3 from = transform.position;
                Vector3 to = StandingPointOf(step);
                Quaternion fromRotation = transform.rotation;
                Quaternion toRotation = FacingRotation(to - from, fromRotation);
                for (float t = 0f; t < 1f; t += Time.deltaTime / stepSeconds)
                {
                    // A small hop on each step, so movement between levels reads clearly
                    transform.position = Vector3.Lerp(from, to, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * _hopHeight;
                    transform.rotation = Quaternion.Slerp(fromRotation, toRotation, t / turnFraction);
                    yield return null;
                }

                transform.position = to;
                transform.rotation = toRotation;
                if (!IsInLoadedSection(step))
                {
                    break;
                }
            }

            _walk = null;
            StandAt(movement.Position);
        }

        /// <summary>
        /// Rotation that faces the pawn along <paramref name="movement"/>, ignoring any climb or drop.
        /// A purely vertical move has no direction to face, so it keeps <paramref name="current"/>.
        /// </summary>
        private Quaternion FacingRotation(Vector3 movement, Quaternion current)
        {
            Vector3 up = _board != null ? _board.transform.up : Vector3.up;
            Vector3 flat = Vector3.ProjectOnPlane(movement, up);
            if (flat.sqrMagnitude < 0.0001f)
            {
                return current;
            }
            return Quaternion.LookRotation(flat, up) * Quaternion.Euler(0f, _modelFacingYaw, 0f);
        }

        private void StandAt(BoardPosition position)
        {
            transform.position = StandingPointOf(position);
            SetInSection(IsInLoadedSection(position));
        }

        private void ShowPlan()
        {
            if (_plannedPath == null)
            {
                return;
            }

            if (!_showPlannedPath || Pawn == null || Pawn.PlannedPath.Count == 0)
            {
                _plannedPath.positionCount = 0;
                return;
            }

            Vector3 lift = Vector3.up * 0.05f;
            _plannedPath.positionCount = Pawn.PlannedPath.Count + 1;
            _plannedPath.SetPosition(0, StandingPointOf(Pawn.Position) + lift);
            for (int i = 0; i < Pawn.PlannedPath.Count; i++)
            {
                _plannedPath.SetPosition(i + 1, StandingPointOf(Pawn.PlannedPath[i]) + lift);
            }
        }

        /// <summary>
        /// Names the pawn and tints its body, label and path in the colour it's shown in: its owner's while a player controls
        /// it, otherwise its own (each pawn gets its own material copies)
        /// </summary>
        private void ApplyLook(Pawn pawn)
        {
            Color colour = PawnService.GetColour(pawn);
            gameObject.name = $"Pawn {pawn.Name}";

            if (_body != null)
            {
                if (_bodyMaterial == null)
                {
                    _bodyMaterial = new Material(_body.sharedMaterial);
                    _body.sharedMaterial = _bodyMaterial;
                }
                _bodyMaterial.color = colour;
            }

            if (_label != null)
            {
                _label.color = colour;
            }

            if (_plannedPath != null)
            {
                if (_pathMaterial == null)
                {
                    _pathMaterial = new Material(_plannedPath.sharedMaterial);
                    _plannedPath.sharedMaterial = _pathMaterial;
                }
                _pathMaterial.color = colour;
                _plannedPath.positionCount = 0;
            }
        }

        /// <summary>
        /// Names the pawn on its label, with who controls it underneath: "(you)" for us, the player's name for anyone else,
        /// nothing when it acts on its own
        /// </summary>
        private void RefreshLabel()
        {
            if (_label == null || Pawn == null)
            {
                return;
            }

            if (IsOwnedByLocalPlayer)
            {
                _label.text = $"{Pawn.Name}\n(you)";
            }
            else if (Pawn.OwnerId.HasValue)
            {
                _label.text = $"{Pawn.Name}\n({Pawn.OwnerName})";
            }
            else
            {
                _label.text = Pawn.Name;
            }
        }

        private void SetSelected(bool selected)
        {
            IsSelected = selected;
            if (_selectionIndicator != null)
            {
                _selectionIndicator.SetActive(selected);
            }
        }

        private void SetInSection(bool inSection)
        {
            gameObject.SetActive(inSection);
        }

        private bool IsInLoadedSection(BoardPosition position)
        {
            return _board == null || (_board.Section != null && BoardService.Contains(_board.Section.Region, position));
        }

        /// <summary>
        /// World point the pawn's feet rest on when it stands at <paramref name="position"/>: the bottom of that empty space
        /// </summary>
        private Vector3 StandingPointOf(BoardPosition position)
        {
            if (_board == null)
            {
                return new Vector3(position.X, position.Z * BoardController.LevelHeight, position.Y);
            }
            return _board.PositionToWorld(position) - _board.transform.up * (BoardController.LevelHeight / 2f);
        }
    }
}

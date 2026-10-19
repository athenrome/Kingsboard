using System;
using System.Collections;
using Kingsboard.Board;
using UnityEngine;

namespace Kingsboard.Pawns
{
    /// <summary>
    /// Drives one pawn on the client: its look (name and colour), showing the move it has planned, walking its path when
    /// moves resolve, hiding while it's outside the loaded board section, and being selected. Every pawn has its own.
    /// Lives on the root of the Pawn prefab (Assets/Prefabs/Pawns/Pawn.prefab); the <see cref="PawnManager"/> spawns one per
    /// pawn the server sends and passes it that pawn's updates. The server makes every decision; this only shows them.
    /// The prefab's pivot is at the pawn's feet, so placing its transform at a standing point stands it on the board.
    /// </summary>
    public class PawnController : MonoBehaviour
    {
        [Tooltip("The pawn's body; tinted with the pawn's colour")]
        [SerializeField] private Renderer body;

        [Tooltip("Name label above the pawn; turned to face the camera every frame")]
        [SerializeField] private TextMesh label;

        [Tooltip("Line showing the pawn's planned path (the nested PawnPath prefab). Drawn in world space, so it stays put while the pawn walks.")]
        [SerializeField] private LineRenderer plannedPath;

        [Tooltip("Shown while the pawn is selected")]
        [SerializeField] private GameObject selectionIndicator;

        [Tooltip("Seconds the pawn takes to walk one step of its path")]
        [SerializeField] private float secondsPerStep = 0.25f;

        [Tooltip("How high the pawn hops on each step, in world units")]
        [SerializeField] private float hopHeight = 0.15f;

        [Tooltip("Show the pawn's planned path as a line")]
        [SerializeField] private bool showPlannedPath = true;

        private BoardController _board;
        private Material _bodyMaterial;
        private Material _pathMaterial;
        private Coroutine _walk;

        /// <summary>
        /// The latest state of this pawn from the server
        /// </summary>
        public Pawn Pawn { get; private set; }

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
            ApplyLook(pawn);
            UpdatePawn(pawn);
            StandAt(pawn.Position);
            SetSelected(false);
        }

        /// <summary>
        /// Takes the pawn's latest state from the server without moving it (e.g. after a reconnect)
        /// </summary>
        public void UpdatePawn(Pawn pawn)
        {
            Pawn = pawn;
            ShowPlan();
        }

        /// <summary>
        /// The pawn has planned its move for the turn (Planning phase): show the path it intends to walk
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
            if (plannedPath != null)
            {
                plannedPath.positionCount = 0;
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
        /// Shows the pawn only while it stands inside the loaded board section (pawns elsewhere have no ground drawn under them).
        /// Call when a new section loads.
        /// </summary>
        public void RefreshVisibility()
        {
            BoardPosition position = Pawn != null ? Pawn.Position : new BoardPosition();
            SetInSection(IsInLoadedSection(position));
        }

        private void LateUpdate()
        {
            // Keep the name label facing the camera
            Camera camera = Camera.main;
            if (label != null && camera != null)
            {
                label.transform.rotation = camera.transform.rotation;
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
        /// Steps through each position of the path, hiding the pawn when it walks out of the loaded section
        /// </summary>
        private IEnumerator Walk(PawnMovement movement)
        {
            foreach (BoardPosition step in movement.Path)
            {
                Vector3 from = transform.position;
                Vector3 to = StandingPointOf(step);
                for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(0.01f, secondsPerStep))
                {
                    // A small hop on each step, so movement between levels reads clearly
                    transform.position = Vector3.Lerp(from, to, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * hopHeight;
                    yield return null;
                }

                transform.position = to;
                if (!IsInLoadedSection(step))
                {
                    break;
                }
            }

            _walk = null;
            StandAt(movement.Position);
        }

        private void StandAt(BoardPosition position)
        {
            transform.position = StandingPointOf(position);
            SetInSection(IsInLoadedSection(position));
        }

        private void ShowPlan()
        {
            if (plannedPath == null)
            {
                return;
            }

            if (!showPlannedPath || Pawn == null || Pawn.PlannedPath.Count == 0)
            {
                plannedPath.positionCount = 0;
                return;
            }

            Vector3 lift = Vector3.up * 0.05f;
            plannedPath.positionCount = Pawn.PlannedPath.Count + 1;
            plannedPath.SetPosition(0, StandingPointOf(Pawn.Position) + lift);
            for (int i = 0; i < Pawn.PlannedPath.Count; i++)
            {
                plannedPath.SetPosition(i + 1, StandingPointOf(Pawn.PlannedPath[i]) + lift);
            }
        }

        /// <summary>
        /// Names the pawn and tints its body, label and path in its colour (each pawn gets its own material copies)
        /// </summary>
        private void ApplyLook(Pawn pawn)
        {
            gameObject.name = $"Pawn {pawn.Name}";

            if (body != null)
            {
                if (_bodyMaterial == null)
                {
                    _bodyMaterial = new Material(body.sharedMaterial);
                    body.sharedMaterial = _bodyMaterial;
                }
                _bodyMaterial.color = pawn.Colour;
            }

            if (label != null)
            {
                label.text = pawn.Name;
                label.color = pawn.Colour;
            }

            if (plannedPath != null)
            {
                if (_pathMaterial == null)
                {
                    _pathMaterial = new Material(plannedPath.sharedMaterial);
                    plannedPath.sharedMaterial = _pathMaterial;
                }
                _pathMaterial.color = pawn.Colour;
                plannedPath.positionCount = 0;
            }
        }

        private void SetSelected(bool selected)
        {
            IsSelected = selected;
            if (selectionIndicator != null)
            {
                selectionIndicator.SetActive(selected);
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

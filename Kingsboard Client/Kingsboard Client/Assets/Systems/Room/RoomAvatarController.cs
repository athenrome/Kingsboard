using System.Collections.Generic;
using Kingsboard.Players;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Kingsboard.Room
{
    /// <summary>
    /// The local player's figure in the room. It walks the room's grid (<see cref="RoomController"/>) one cell at a time:
    /// WASD or the arrow keys step north, west, south and east as seen from the camera, holding a key keeps walking,
    /// and Shift walks faster. It can't step off the grid or onto cells a blocking object covers. Next to the table, E
    /// sits down so the board and pawns can be used (<see cref="TableController.Sit"/>); E again stands up.
    /// It's shown only to this client and takes no part in the game on the server. It wears the player's colour once
    /// joined, and its figure is hidden while seated so the board can be seen. Keys are ignored while text is being typed (<see cref="PlayerStateTracker.IsEnteringText"/>).
    /// Lives on the RoomAvatar prefab (Assets/Prefabs/Room/RoomAvatar.prefab), placed in the scene where it should start.
    /// </summary>
    public class RoomAvatarController : MonoBehaviour
    {
        [Tooltip("The renderer tinted in the player's colour")]
        [SerializeField] private Renderer _body;

        [Tooltip("The avatar's visible figure (the Model child). Hidden while seated, so it doesn't block the view of the board.")]
        [SerializeField] private GameObject _model;

        [Tooltip("The prompt that tells the player what they can do here (the nested RoomPrompt)")]
        [SerializeField] private RoomPromptController _prompt;

        [Tooltip("Seconds one step from cell to cell takes")]
        [SerializeField, Min(0.01f)] private float _stepDuration = 0.16f;

        [Tooltip("Seconds one step takes while Shift is held")]
        [SerializeField, Min(0.01f)] private float _fastStepDuration = 0.09f;

        [Tooltip("How quickly the avatar turns to face where it's going. Higher is snappier.")]
        [SerializeField] private float _turnSmoothing = 18f;

        [Tooltip("How high the camera looks at, above the avatar's feet, in room units")]
        [SerializeField] private float _focusHeight = 16f;

        private RoomController _room;
        private TableController _table;
        private PlayerController _player;
        private Material _bodyMaterial;
        private readonly List<Vector2Int> _heldDirections = new List<Vector2Int>();
        private bool _isStepping;
        private Vector3 _stepFrom;
        private Vector3 _stepTo;
        private float _stepTime;
        private float _currentStepDuration;
        private Quaternion _targetRotation;

        /// <summary>
        /// The cell the avatar is standing on (or, mid-step, the one it's stepping onto)
        /// </summary>
        public Vector2Int Cell { get; private set; }

        /// <summary>
        /// The grid direction the avatar faces (one of <see cref="RoomGridService.Directions"/>)
        /// </summary>
        public Vector2Int Facing { get; private set; } = Vector2Int.up;

        /// <summary>
        /// True while the avatar is partway through a step
        /// </summary>
        public bool IsStepping => _isStepping;

        /// <summary>
        /// The world point the room camera should look at: just above the avatar
        /// </summary>
        public Vector3 FocusPoint => transform.position + Vector3.up * _focusHeight;

        /// <summary>
        /// True if the avatar is close enough to the table to sit at it
        /// </summary>
        public bool CanReachTable => _table != null && !_isStepping && _table.IsInReach(Cell);

        /// <summary>
        /// Puts the avatar straight onto <paramref name="cell"/>, or the nearest free cell to it, without walking there
        /// </summary>
        public void Teleport(Vector2Int cell)
        {
            if (_room == null)
            {
                return;
            }
            Vector2Int? free = _room.FindNearestWalkable(cell);
            if (!free.HasValue)
            {
                Debug.LogWarning("[Room] Every cell in the room is taken, so the avatar has nowhere to stand.", this);
                return;
            }

            _isStepping = false;
            Cell = free.Value;
            transform.position = _room.CellToWorld(Cell);
        }

        private void Start()
        {
            _room = FindAnyObjectByType<RoomController>();
            if (_room == null)
            {
                Debug.LogError("[Room] No RoomController in the scene, so the avatar has nowhere to walk. Place the Room prefab.", this);
                enabled = false;
                return;
            }

            _table = FindAnyObjectByType<TableController>();
            if (_table == null)
            {
                Debug.LogWarning("[Room] No TableController in the scene, so the avatar can't sit down to play. Place the Table prefab.", this);
            }

            _player = FindAnyObjectByType<PlayerController>();
            if (_player != null)
            {
                _player.Joined += OnPlayerJoined;
                if (_player.Player != null)
                {
                    OnPlayerJoined(_player.Player);
                }
            }
            else
            {
                Debug.LogWarning("[Room] No PlayerController in the scene, so the avatar can't tell when keys are for typing. Place the Player prefab.", this);
            }

            _room.GridChanged += OnGridChanged;
            _room.LayoutChanged += OnLayoutChanged;

            _targetRotation = transform.rotation;
            Facing = RoomGridService.NearestDirection(_room.transform.InverseTransformDirection(transform.forward));
            Teleport(_room.WorldToCell(transform.position));
        }

        private void OnDestroy()
        {
            if (_room != null)
            {
                _room.GridChanged -= OnGridChanged;
                _room.LayoutChanged -= OnLayoutChanged;
            }
            if (_player != null)
            {
                _player.Joined -= OnPlayerJoined;
            }
            if (_bodyMaterial != null)
            {
                Destroy(_bodyMaterial);
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            bool enteringText = _player != null && _player.State.IsEnteringText;
            bool seated = _table != null && _table.IsSeated;
            Keyboard keyboard = Keyboard.current;

            if (keyboard != null && !enteringText && keyboard.eKey.wasPressedThisFrame)
            {
                if (seated)
                {
                    _table.Stand();
                    seated = false;
                }
                else if (CanReachTable)
                {
                    FaceTowards(_table.GetFacingPoint());
                    _table.Sit();
                    seated = true;
                }
            }

            ReadHeldDirections(keyboard, enteringText || seated);
            bool fast = keyboard != null && keyboard.shiftKey.isPressed;

            if (_isStepping)
            {
                _stepTime += dt;
                float t = Mathf.Clamp01(_stepTime / _currentStepDuration);
                transform.position = Vector3.Lerp(_stepFrom, _stepTo, t);
                if (t >= 1f)
                {
                    _isStepping = false;
                    // Carry the leftover time into the next step so holding a key walks at an even pace
                    TryStartStep(fast, _stepTime - _currentStepDuration);
                }
            }
            else
            {
                TryStartStep(fast, 0f);
            }

            transform.rotation = Quaternion.Slerp(transform.rotation, _targetRotation, 1f - Mathf.Exp(-_turnSmoothing * dt));
            if (_model != null && _model.activeSelf == seated)
            {
                _model.SetActive(!seated);
            }
            RefreshPrompt(seated);
        }

        /// <summary>
        /// Keeps the held movement keys in the order they were pressed, so the latest one pressed wins
        /// </summary>
        private void ReadHeldDirections(Keyboard keyboard, bool blocked)
        {
            if (keyboard == null || blocked)
            {
                _heldDirections.Clear();
                return;
            }

            TrackKey(Vector2Int.up, keyboard.wKey, keyboard.upArrowKey);
            TrackKey(Vector2Int.down, keyboard.sKey, keyboard.downArrowKey);
            TrackKey(Vector2Int.left, keyboard.aKey, keyboard.leftArrowKey);
            TrackKey(Vector2Int.right, keyboard.dKey, keyboard.rightArrowKey);
        }

        private void TrackKey(Vector2Int direction, UnityEngine.InputSystem.Controls.KeyControl key, UnityEngine.InputSystem.Controls.KeyControl altKey)
        {
            bool held = key.isPressed || altKey.isPressed;
            bool pressed = key.wasPressedThisFrame || altKey.wasPressedThisFrame;
            if (!held)
            {
                _heldDirections.Remove(direction);
            }
            else if (pressed || !_heldDirections.Contains(direction))
            {
                _heldDirections.Remove(direction);
                _heldDirections.Add(direction);
            }
        }

        /// <summary>
        /// Starts a step in the direction of the latest held key, turned to match where the camera faces. Turns to face a
        /// blocked direction without moving.
        /// </summary>
        private void TryStartStep(bool fast, float headStart)
        {
            if (_heldDirections.Count == 0)
            {
                return;
            }

            Vector2Int direction = ToRoomDirection(_heldDirections[_heldDirections.Count - 1]);
            Face(direction);

            Vector2Int target = Cell + direction;
            if (!_room.IsWalkable(target))
            {
                return;
            }

            _isStepping = true;
            _stepFrom = transform.position;
            _stepTo = _room.CellToWorld(target);
            _currentStepDuration = fast ? _fastStepDuration : _stepDuration;
            _stepTime = Mathf.Clamp(headStart, 0f, _currentStepDuration);
            Cell = target;
            transform.position = Vector3.Lerp(_stepFrom, _stepTo, _stepTime / _currentStepDuration);
        }

        /// <summary>
        /// Turns a key's direction on screen (up is away from the camera) into a direction on the room's grid
        /// </summary>
        private Vector2Int ToRoomDirection(Vector2Int screenDirection)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return screenDirection;
            }

            Vector3 forward = _room.transform.InverseTransformDirection(camera.transform.forward);
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = _room.transform.InverseTransformDirection(camera.transform.up);
            }
            Vector2Int up = RoomGridService.NearestDirection(forward);
            Vector2Int right = new Vector2Int(up.y, -up.x);
            return up * screenDirection.y + right * screenDirection.x;
        }

        private void Face(Vector2Int direction)
        {
            Facing = direction;
            Vector3 world = _room.transform.TransformDirection(new Vector3(direction.x, 0f, direction.y));
            _targetRotation = Quaternion.LookRotation(world, Vector3.up);
        }

        private void FaceTowards(Vector3 worldPoint)
        {
            Face(RoomGridService.NearestDirection(_room.transform.InverseTransformDirection(worldPoint - transform.position)));
        }

        private void RefreshPrompt(bool seated)
        {
            if (_prompt == null)
            {
                return;
            }

            if (seated)
            {
                _prompt.Show("E", "Leave the table");
            }
            else if (CanReachTable)
            {
                _prompt.Show("E", "Sit at the table");
            }
            else
            {
                _prompt.Hide();
            }
        }

        /// <summary>
        /// The grid moved or resized: stand on whichever free cell is now nearest
        /// </summary>
        private void OnGridChanged(RoomGrid grid)
        {
            Teleport(_room.WorldToCell(_isStepping ? _stepTo : transform.position));
        }

        /// <summary>
        /// Something moved in the room: if it landed on the avatar's cell, step aside to the nearest free one
        /// </summary>
        private void OnLayoutChanged()
        {
            if (!_room.IsWalkable(Cell))
            {
                Teleport(Cell);
            }
        }

        private void OnPlayerJoined(PlayerInfo player)
        {
            if (_body == null || !ColorUtility.TryParseHtmlString(player.Colour, out Color colour))
            {
                return;
            }
            if (_bodyMaterial == null)
            {
                _bodyMaterial = new Material(_body.sharedMaterial);
                _body.sharedMaterial = _bodyMaterial;
            }
            _bodyMaterial.color = colour;
        }
    }
}

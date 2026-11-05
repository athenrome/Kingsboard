using System.Collections.Generic;
using Kingsboard.Board;
using Kingsboard.Players;
using Kingsboard.Room;
using Kingsboard.Shared.Models.Board;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Kingsboard.CameraControl
{
    /// <summary>
    /// Moves the main camera. It has two views:
    /// <list type="bullet">
    /// <item><b>Room view</b> (while the local player isn't seated at the table): follows the room avatar
    /// (<see cref="RoomAvatarController"/>) from behind and above, and stays inside the room's walls and under its beams.
    /// The avatar takes the movement keys; the mouse wheel zooms.</item>
    /// <item><b>Board view</b> (while seated, <see cref="PlayerStateTracker.IsAtTable"/>): looks down at a focus point on
    /// the board from a fixed angle. WASD (or the arrow keys) slides the focus point across the board, and the mouse wheel
    /// zooms in and out by moving the camera closer to or further from it. Hold Shift to move faster. The focus point
    /// stays inside the loaded board section, and the camera frames a new section when one arrives that the focus point
    /// isn't in.</item>
    /// </list>
    /// The camera glides between the two. With no avatar in the scene it always uses the board view.
    /// Keys are ignored while the local player's state says text entry blocks the camera
    /// (<see cref="PlayerStateTracker.BlocksCameraMovement"/>, e.g. while typing in chat), and the wheel is ignored over UI.
    /// Lives on the Camera prefab (Assets/Prefabs/Camera/Camera.prefab), placed in the scene.
    /// </summary>
    public class CameraController : MonoBehaviour
    {
        [Tooltip("The camera this moves (the Camera on this prefab)")]
        [SerializeField] private Camera _camera;

        [Tooltip("How far the camera tilts down from the horizon, in degrees")]
        [SerializeField, Range(10f, 90f)] private float _pitch = 50f;

        [Tooltip("Which way the camera faces around the vertical axis, in degrees (0 looks along +z, up the board)")]
        [SerializeField] private float _yaw = 0f;

        [Tooltip("Pan speed at a distance of 10 world units, in world units per second. Panning scales with distance so it feels the same at any zoom.")]
        [SerializeField] private float _panSpeed = 12f;

        [Tooltip("How much faster panning is while Shift is held")]
        [SerializeField] private float _fastMultiplier = 2.5f;

        [Tooltip("How much one notch of the mouse wheel changes the distance, as a fraction of the current distance")]
        [SerializeField] private float _zoomStep = 0.1f;

        [Tooltip("Closest the camera can get to the focus point, in world units")]
        [SerializeField] private float _minDistance = 4f;

        [Tooltip("Furthest the camera can get from the focus point, in world units")]
        [SerializeField] private float _maxDistance = 80f;

        [Tooltip("How quickly the camera catches up with panning and zooming. Higher is snappier.")]
        [SerializeField] private float _smoothing = 12f;

        [Header("Room view")]
        [Tooltip("How far the camera tilts down from the horizon while following the avatar, in degrees")]
        [SerializeField, Range(10f, 90f)] private float _roomPitch = 40f;

        [Tooltip("How far the camera starts from the avatar, in world units")]
        [SerializeField] private float _roomDistance = 110f;

        [Tooltip("Closest the camera can get to the avatar, in world units")]
        [SerializeField] private float _roomMinDistance = 30f;

        [Tooltip("Furthest the camera can get from the avatar, in world units")]
        [SerializeField] private float _roomMaxDistance = 220f;

        [Tooltip("How far the camera keeps from the floor and the top of the room, in world units")]
        [SerializeField] private float _roomMargin = 2f;

        private BoardController _board;
        private PlayerController _player;
        private RoomAvatarController _avatar;
        private RoomController _room;
        private float _currentPitch;
        private readonly List<UIDocument> _documents = new List<UIDocument>();
        private Vector3 _focus;
        private float _distance = 20f;
        private Vector3 _currentFocus;
        private float _currentDistance;
        private bool _hasFramed;

        /// <summary>
        /// The world point the camera is looking at
        /// </summary>
        public Vector3 Focus => _focus;

        /// <summary>
        /// How far the camera is (or is heading to be) from <see cref="Focus"/>, in world units
        /// </summary>
        public float Distance => _distance;

        /// <summary>
        /// True while the camera follows the avatar around the room, false while it looks at the board
        /// </summary>
        public bool IsRoomView => _avatar != null && !(_player != null && _player.State.IsAtTable);

        /// <summary>
        /// Sets where the board view looks: a world point from the given distance (clamped to the zoom limits and kept
        /// inside the loaded section). In the room view this is remembered for when the player sits down.
        /// </summary>
        /// <param name="focus">The world point to look at</param>
        /// <param name="distance">How far back to sit from it, in world units</param>
        /// <param name="instant">True to jump there at once, false to glide there (ignored in the room view)</param>
        public void LookAt(Vector3 focus, float distance, bool instant)
        {
            _focus = ClampToSection(focus);
            _distance = Mathf.Clamp(distance, _minDistance, _maxDistance);
            if (instant && !IsRoomView)
            {
                _currentFocus = _focus;
                _currentDistance = _distance;
                _currentPitch = _pitch;
                ApplyTransform();
            }
        }

        private void Start()
        {
            if (_camera == null)
            {
                Debug.LogError("[Camera] The CameraController needs its Camera assigned. Use the Camera prefab.", this);
                enabled = false;
                return;
            }

            _documents.AddRange(FindObjectsByType<UIDocument>());

            _player = FindAnyObjectByType<PlayerController>();
            if (_player == null)
            {
                Debug.LogWarning("[Camera] No PlayerController in the scene, so the camera can't tell when keys are for typing. Place the Player prefab.", this);
            }

            _room = FindAnyObjectByType<RoomController>();
            _avatar = FindAnyObjectByType<RoomAvatarController>();
            if (_avatar == null)
            {
                Debug.LogWarning("[Camera] No RoomAvatarController in the scene, so the camera stays on the board. Place the RoomAvatar prefab.", this);
            }

            _currentFocus = TargetFocus();
            _currentDistance = TargetDistance();
            _currentPitch = TargetPitch();
            ApplyTransform();

            _board = FindAnyObjectByType<BoardController>();
            if (_board != null)
            {
                _board.SectionLoaded += OnSectionLoaded;
                if (_board.Section != null)
                {
                    OnSectionLoaded(_board.Section);
                }
            }
            else
            {
                Debug.LogWarning("[Camera] No BoardController in the scene, so the camera can't frame the board. Place the Board prefab.", this);
            }
        }

        private void OnDestroy()
        {
            if (_board != null)
            {
                _board.SectionLoaded -= OnSectionLoaded;
            }
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            Keyboard keyboard = Keyboard.current;
            bool roomView = IsRoomView;
            bool blocked = _player != null && _player.State.BlocksCameraMovement;
            // In the room view the avatar has the movement keys
            if (keyboard != null && !blocked && !roomView)
            {
                Vector2 input = Vector2.zero;
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) input.y += 1f;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) input.y -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) input.x += 1f;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) input.x -= 1f;

                if (input != Vector2.zero)
                {
                    // Move across the board relative to where the camera faces, ignoring its tilt
                    Quaternion facing = Quaternion.Euler(0f, _yaw, 0f);
                    Vector3 move = facing * new Vector3(input.x, 0f, input.y).normalized;
                    float speed = _panSpeed * (_distance / 10f);
                    if (keyboard.shiftKey.isPressed)
                    {
                        speed *= _fastMultiplier;
                    }
                    _focus = ClampToSection(_focus + move * speed * dt);
                }
            }

            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                // The wheel reports 120 per notch on Windows and 1 on some other platforms, so only its sign counts
                float scroll = mouse.scroll.ReadValue().y;
                if (scroll != 0f && !IsPointerOverUi(mouse.position.ReadValue()))
                {
                    float factor = scroll > 0f ? 1f - _zoomStep : 1f + _zoomStep;
                    if (roomView)
                    {
                        _roomDistance = Mathf.Clamp(_roomDistance * factor, _roomMinDistance, _roomMaxDistance);
                    }
                    else
                    {
                        _distance = Mathf.Clamp(_distance * factor, _minDistance, _maxDistance);
                    }
                }
            }

            float t = 1f - Mathf.Exp(-_smoothing * dt);
            _currentFocus = Vector3.Lerp(_currentFocus, TargetFocus(), t);
            _currentDistance = Mathf.Lerp(_currentDistance, TargetDistance(), t);
            _currentPitch = Mathf.Lerp(_currentPitch, TargetPitch(), t);
            ApplyTransform();
        }

        private Vector3 TargetFocus()
        {
            return IsRoomView ? _avatar.FocusPoint : _focus;
        }

        private float TargetDistance()
        {
            return IsRoomView ? Mathf.Clamp(_roomDistance, _roomMinDistance, _roomMaxDistance) : _distance;
        }

        private float TargetPitch()
        {
            return IsRoomView ? _roomPitch : _pitch;
        }

        private void ApplyTransform()
        {
            Quaternion rotation = Quaternion.Euler(_currentPitch, _yaw, 0f);
            Vector3 position = _currentFocus - rotation * Vector3.forward * _currentDistance;
            Vector3 inside = ClampToRoom(position);
            if (inside != position)
            {
                // Pulled in from a wall or the beams: keep looking at the same point
                position = inside;
                Vector3 look = _currentFocus - position;
                if (look.sqrMagnitude > 0.0001f)
                {
                    rotation = Quaternion.LookRotation(look, Vector3.up);
                }
            }
            _camera.transform.SetPositionAndRotation(position, rotation);
        }

        /// <summary>
        /// Keeps a world point inside the room: over its grid, above its floor and below its headroom.
        /// Returns the point unchanged if there's no room.
        /// </summary>
        private Vector3 ClampToRoom(Vector3 point)
        {
            if (_room == null)
            {
                return point;
            }

            RoomGrid grid = _room.Grid;
            Vector3 local = _room.transform.InverseTransformPoint(point);
            local.x = Mathf.Clamp(local.x, grid.Origin.x, grid.Origin.x + grid.Width * grid.CellSize);
            local.z = Mathf.Clamp(local.z, grid.Origin.z, grid.Origin.z + grid.Depth * grid.CellSize);
            local.y = Mathf.Clamp(local.y, grid.Origin.y + _roomMargin, grid.Origin.y + Mathf.Max(_roomMargin, _room.Headroom - _roomMargin));
            return _room.transform.TransformPoint(local);
        }

        /// <summary>
        /// Frames a newly loaded section, unless the camera is already looking at somewhere inside it
        /// </summary>
        private void OnSectionLoaded(BoardSection section)
        {
            if (_hasFramed && IsInSection(_focus))
            {
                return;
            }

            BoardRegion region = section.Region;
            Vector3 centre = _board.transform.TransformPoint(new Vector3(
                region.Origin.X + (region.Width - 1) / 2f,
                section.BoardHeight * BoardController.LevelHeight / 2f,
                region.Origin.Y + (region.Depth - 1) / 2f));
            float size = Mathf.Max(region.Width, region.Depth);
            LookAt(centre, size * 1.1f, !_hasFramed);
            _hasFramed = true;
        }

        private bool IsInSection(Vector3 point)
        {
            return ClampToSection(point) == point;
        }

        /// <summary>
        /// Keeps a world point over the loaded section, so the camera can't wander off into empty space
        /// </summary>
        private Vector3 ClampToSection(Vector3 point)
        {
            if (_board == null || _board.Section == null)
            {
                return point;
            }

            BoardRegion region = _board.Section.Region;
            Vector3 local = _board.transform.InverseTransformPoint(point);
            local.x = Mathf.Clamp(local.x, region.Origin.X - 0.5f, region.Origin.X + region.Width - 0.5f);
            local.z = Mathf.Clamp(local.z, region.Origin.Y - 0.5f, region.Origin.Y + region.Depth - 0.5f);
            return _board.transform.TransformPoint(local);
        }

        /// <summary>
        /// True if the pointer is over a UI element, such as the chat box, which scrolls itself
        /// </summary>
        private bool IsPointerOverUi(Vector2 screenPoint)
        {
            foreach (UIDocument document in _documents)
            {
                VisualElement root = document != null ? document.rootVisualElement : null;
                if (root == null || root.panel == null)
                {
                    continue;
                }

                Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screenPoint.x, Screen.height - screenPoint.y));
                VisualElement picked = root.panel.Pick(panelPoint);
                if (picked != null && picked != root && picked != root.panel.visualTree && picked.resolvedStyle.display != DisplayStyle.None)
                {
                    return true;
                }
            }
            return false;
        }
    }
}

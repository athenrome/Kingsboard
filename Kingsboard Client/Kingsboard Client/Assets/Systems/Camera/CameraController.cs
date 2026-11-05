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
    /// Moves the main camera. It has two views, picked by the local player's mode (<see cref="PlayerStateTracker.Mode"/>):
    /// <list type="bullet">
    /// <item><b>First person</b> (<see cref="PlayerMode.Avatar"/>): looks out of the room avatar's eyes
    /// (<see cref="RoomAvatarController.EyePoint"/>). The mouse looks around while the cursor is captured; the avatar takes
    /// the movement keys and steps the way the camera faces. Esc frees the cursor (e.g. to click the chat box), and a click
    /// on the game captures it again. The cursor is also freed while typing.</item>
    /// <item><b>Board view</b> (<see cref="PlayerMode.InGame"/>): looks down at a focus point on the board from a fixed
    /// angle. WASD (or the arrow keys) slides the focus point across the board, and the mouse wheel zooms in and out by
    /// moving the camera closer to or further from it. Hold Shift to move faster. The focus point stays inside the loaded
    /// board section, and the camera frames a new section when one arrives that the focus point isn't in.</item>
    /// </list>
    /// Switching between the two glides the camera from one to the other. With no avatar in the scene it always uses the
    /// board view.
    /// Keys are ignored while the local player's state says text entry blocks the camera
    /// (<see cref="PlayerStateTracker.BlocksCameraMovement"/>, e.g. while typing in chat), and the wheel is ignored over UI.
    /// Lives on the Camera prefab (Assets/Prefabs/Camera/Camera.prefab), placed in the scene.
    /// </summary>
    public class CameraController : MonoBehaviour
    {
        [Tooltip("The camera this moves (the Camera on this prefab)")]
        [SerializeField] private Camera _camera;

        [Header("Board view")]
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

        [Header("First person")]
        [Tooltip("Degrees the view turns per pixel the mouse moves")]
        [SerializeField, Min(0f)] private float _lookSensitivity = 0.12f;

        [Tooltip("How far up or down the view can tilt from level, in degrees")]
        [SerializeField, Range(0f, 89f)] private float _maxLookPitch = 80f;

        [Tooltip("How far down the view tilts when the game starts, in degrees")]
        [SerializeField, Range(-89f, 89f)] private float _startLookPitch = 10f;

        [Tooltip("Seconds the camera takes to glide between first person and the board view")]
        [SerializeField, Min(0f)] private float _transitionDuration = 0.6f;

        private BoardController _board;
        private PlayerController _player;
        private RoomAvatarController _avatar;
        private readonly List<UIDocument> _documents = new List<UIDocument>();
        private Vector3 _focus;
        private float _distance = 20f;
        private Vector3 _currentFocus;
        private float _currentDistance;
        private bool _hasFramed;
        private float _lookYaw;
        private float _lookPitch;
        private bool _cursorReleased;
        private bool _cursorLocked;
        private bool _wasFirstPerson;
        private bool _transitioning;
        private float _transitionTime;
        private Vector3 _transitionFromPosition;
        private Quaternion _transitionFromRotation;

        /// <summary>
        /// The world point the board view is looking at
        /// </summary>
        public Vector3 Focus => _focus;

        /// <summary>
        /// How far the board view is (or is heading to be) from <see cref="Focus"/>, in world units
        /// </summary>
        public float Distance => _distance;

        /// <summary>
        /// True while the camera looks out of the avatar's eyes, false while it looks at the board
        /// </summary>
        public bool IsFirstPerson => _avatar != null && !(_player != null && _player.State.IsInGame);

        /// <summary>
        /// Which way the first-person view faces around the vertical axis, in degrees (0 looks along +z)
        /// </summary>
        public float LookYaw => _lookYaw;

        /// <summary>
        /// How far the first-person view tilts down from level, in degrees (negative looks up)
        /// </summary>
        public float LookPitch => _lookPitch;

        /// <summary>
        /// True while the mouse cursor is captured for looking around in first person
        /// </summary>
        public bool IsCursorCaptured => _cursorLocked;

        /// <summary>
        /// Sets where the board view looks: a world point from the given distance (clamped to the zoom limits and kept
        /// inside the loaded section). In first person this is remembered for when the player sits down.
        /// </summary>
        /// <param name="focus">The world point to look at</param>
        /// <param name="distance">How far back to sit from it, in world units</param>
        /// <param name="instant">True to jump there at once, false to glide there (ignored in first person)</param>
        public void LookAt(Vector3 focus, float distance, bool instant)
        {
            _focus = ClampToSection(focus);
            _distance = Mathf.Clamp(distance, _minDistance, _maxDistance);
            if (instant)
            {
                _currentFocus = _focus;
                _currentDistance = _distance;
                if (!IsFirstPerson)
                {
                    _transitioning = false;
                    ApplyPose(false);
                }
            }
        }

        /// <summary>
        /// Turns the first-person view to face a direction, in degrees (yaw 0 looks along +z; positive pitch looks down)
        /// </summary>
        public void SetLook(float yaw, float pitch)
        {
            _lookYaw = Mathf.Repeat(yaw, 360f);
            _lookPitch = Mathf.Clamp(pitch, -_maxLookPitch, _maxLookPitch);
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

            _avatar = FindAnyObjectByType<RoomAvatarController>();
            if (_avatar != null)
            {
                SetLook(_avatar.transform.eulerAngles.y, _startLookPitch);
            }
            else
            {
                Debug.LogWarning("[Camera] No RoomAvatarController in the scene, so the camera stays on the board. Place the RoomAvatar prefab.", this);
            }

            _currentFocus = _focus;
            _currentDistance = _distance;
            _wasFirstPerson = IsFirstPerson;
            ApplyPose(_wasFirstPerson);

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
            SetCursorLocked(false);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            // Unity frees the cursor when the window loses focus; capture it again on the next click
            if (!hasFocus)
            {
                _cursorLocked = false;
            }
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            bool firstPerson = IsFirstPerson;
            if (firstPerson != _wasFirstPerson)
            {
                BeginTransition();
                _wasFirstPerson = firstPerson;
                if (firstPerson)
                {
                    // Standing up from the table takes the mouse back for looking around
                    _cursorReleased = false;
                }
            }

            UpdateCursor(firstPerson);
            if (firstPerson)
            {
                UpdateLook();
            }
            else
            {
                UpdateBoardControls(dt);
            }

            float t = 1f - Mathf.Exp(-_smoothing * dt);
            _currentFocus = Vector3.Lerp(_currentFocus, _focus, t);
            _currentDistance = Mathf.Lerp(_currentDistance, _distance, t);

            if (_transitioning)
            {
                _transitionTime += dt;
                if (_transitionTime >= _transitionDuration)
                {
                    _transitioning = false;
                }
            }
            ApplyPose(firstPerson);
        }

        /// <summary>
        /// Captures the cursor for looking around in first person, unless the player has freed it (Esc), is typing, or
        /// isn't in first person. A click on the game (not on UI) captures it again after Esc.
        /// </summary>
        private void UpdateCursor(bool firstPerson)
        {
            bool enteringText = _player != null && _player.State.IsEnteringText;
            if (firstPerson && !enteringText)
            {
                Keyboard keyboard = Keyboard.current;
                Mouse mouse = Mouse.current;
                if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                {
                    _cursorReleased = true;
                }
                else if (mouse != null && mouse.leftButton.wasPressedThisFrame && !IsPointerOverUi(mouse.position.ReadValue()))
                {
                    _cursorReleased = false;
                }
            }

            SetCursorLocked(firstPerson && !enteringText && !_cursorReleased && Application.isFocused);
        }

        private void SetCursorLocked(bool locked)
        {
            if (_cursorLocked == locked && (UnityEngine.Cursor.lockState == CursorLockMode.Locked) == locked)
            {
                return;
            }
            _cursorLocked = locked;
            UnityEngine.Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            UnityEngine.Cursor.visible = !locked;
        }

        /// <summary>
        /// Turns the first-person view with the mouse while the cursor is captured
        /// </summary>
        private void UpdateLook()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || !_cursorLocked)
            {
                return;
            }

            Vector2 delta = mouse.delta.ReadValue() * _lookSensitivity;
            SetLook(_lookYaw + delta.x, _lookPitch - delta.y);
        }

        /// <summary>
        /// Pans and zooms the board view
        /// </summary>
        private void UpdateBoardControls(float dt)
        {
            Keyboard keyboard = Keyboard.current;
            bool blocked = _player != null && _player.State.BlocksCameraMovement;
            if (keyboard != null && !blocked)
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
                    _distance = Mathf.Clamp(_distance * factor, _minDistance, _maxDistance);
                }
            }
        }

        /// <summary>
        /// Starts gliding from where the camera is now to the view it's switching to
        /// </summary>
        private void BeginTransition()
        {
            _transitionFromPosition = _camera.transform.position;
            _transitionFromRotation = _camera.transform.rotation;
            _transitionTime = 0f;
            _transitioning = _transitionDuration > 0f;
        }

        /// <summary>
        /// Puts the camera where the current view says, partway along the glide if one is under way
        /// </summary>
        private void ApplyPose(bool firstPerson)
        {
            Vector3 position;
            Quaternion rotation;
            if (firstPerson)
            {
                position = _avatar.EyePoint;
                rotation = Quaternion.Euler(_lookPitch, _lookYaw, 0f);
            }
            else
            {
                rotation = Quaternion.Euler(_pitch, _yaw, 0f);
                position = _currentFocus - rotation * Vector3.forward * _currentDistance;
            }

            if (_transitioning)
            {
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_transitionTime / _transitionDuration));
                position = Vector3.Lerp(_transitionFromPosition, position, t);
                rotation = Quaternion.Slerp(_transitionFromRotation, rotation, t);
            }
            _camera.transform.SetPositionAndRotation(position, rotation);
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

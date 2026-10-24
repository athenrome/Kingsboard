using System.Collections.Generic;
using Kingsboard.Board;
using Kingsboard.Shared.Models.Board;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Kingsboard.CameraControl
{
    /// <summary>
    /// Moves the main camera around the board. The camera looks down at a focus point on the board from a fixed angle:
    /// WASD (or the arrow keys) slides the focus point across the board, and the mouse wheel zooms in and out by moving
    /// the camera closer to or further from it. Hold Shift to move faster. The focus point stays inside the loaded board
    /// section, and the camera frames a new section when one arrives that the focus point isn't in.
    /// Keys are ignored while a text field (such as the chat box) has focus, and the wheel is ignored over UI.
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

        private BoardController _board;
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
        /// Looks at a world point from the given distance (clamped to the zoom limits and kept inside the loaded section)
        /// </summary>
        /// <param name="focus">The world point to look at</param>
        /// <param name="distance">How far back to sit from it, in world units</param>
        /// <param name="instant">True to jump there at once, false to glide there</param>
        public void LookAt(Vector3 focus, float distance, bool instant)
        {
            _focus = ClampToSection(focus);
            _distance = Mathf.Clamp(distance, _minDistance, _maxDistance);
            if (instant)
            {
                _currentFocus = _focus;
                _currentDistance = _distance;
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

            _currentFocus = _focus;
            _currentDistance = _distance;
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
            if (keyboard != null && !IsTyping())
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

            float t = 1f - Mathf.Exp(-_smoothing * dt);
            _currentFocus = Vector3.Lerp(_currentFocus, _focus, t);
            _currentDistance = Mathf.Lerp(_currentDistance, _distance, t);
            ApplyTransform();
        }

        private void ApplyTransform()
        {
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            _camera.transform.SetPositionAndRotation(_currentFocus - rotation * Vector3.forward * _currentDistance, rotation);
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
        /// True while a text field in any UI panel has keyboard focus, so typed letters don't move the camera
        /// </summary>
        private bool IsTyping()
        {
            foreach (UIDocument document in _documents)
            {
                IPanel panel = document != null && document.rootVisualElement != null ? document.rootVisualElement.panel : null;
                if (panel?.focusController?.focusedElement is VisualElement focused
                    && focused.GetFirstOfType<TextInputBaseField<string>>() != null)
                {
                    return true;
                }
            }
            return false;
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

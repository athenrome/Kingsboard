using UnityEngine;
using UnityEngine.UIElements;

namespace Kingsboard.Room
{
    /// <summary>
    /// Shows a short hint at the bottom of the screen about what the local player can do where they stand, such as
    /// "[E] Sit at the table" (built with UI Toolkit), hidden when there's nothing to say, and a crosshair dot in the middle
    /// of the screen while in first person.
    /// Lives on the RoomPrompt prefab (Assets/Prefabs/Room/RoomPrompt.prefab), nested in the RoomAvatar prefab, which
    /// decides what it says.
    /// </summary>
    public class RoomPromptController : MonoBehaviour
    {
        [Tooltip("The UIDocument the prompt is built in; its panel settings (theme, scaling) are set on the RoomPrompt prefab")]
        [SerializeField] private UIDocument _document;

        private VisualElement _window;
        private VisualElement _crosshair;
        private Label _keyLabel;
        private Label _textLabel;
        private string _key;
        private string _text;

        /// <summary>
        /// Shows the prompt with a key to press and what it does, e.g. "E" and "Sit at the table". Showing the same
        /// prompt again does nothing.
        /// </summary>
        public void Show(string key, string text)
        {
            if (_window == null || (_key == key && _text == text && _window.style.display == DisplayStyle.Flex))
            {
                return;
            }
            _key = key;
            _text = text;
            _keyLabel.text = key;
            _textLabel.text = text;
            _window.style.display = DisplayStyle.Flex;
        }

        /// <summary>
        /// Shows or hides the small dot in the middle of the screen that marks where the first-person view is looking
        /// </summary>
        public void ShowCrosshair(bool visible)
        {
            if (_crosshair == null)
            {
                return;
            }
            DisplayStyle display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (_crosshair.style.display != display)
            {
                _crosshair.style.display = display;
            }
        }

        /// <summary>
        /// Hides the prompt
        /// </summary>
        public void Hide()
        {
            if (_window != null)
            {
                _window.style.display = DisplayStyle.None;
            }
        }

        private void Awake()
        {
            if (_document == null)
            {
                Debug.LogError("[Room] The RoomPromptController needs its UIDocument assigned. Use the RoomPrompt prefab.", this);
                enabled = false;
                return;
            }

            VisualElement root = _document.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("[Room] The room prompt could not be created (UIDocument has no root element).", this);
                return;
            }
            BuildWindow(root);
        }

        private void BuildWindow(VisualElement root)
        {
            // Clicks pass through to the game; the prompt only shows text
            root.pickingMode = PickingMode.Ignore;

            _crosshair = new VisualElement();
            _crosshair.pickingMode = PickingMode.Ignore;
            _crosshair.style.position = Position.Absolute;
            _crosshair.style.left = Length.Percent(50);
            _crosshair.style.top = Length.Percent(50);
            _crosshair.style.width = 6;
            _crosshair.style.height = 6;
            _crosshair.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
            _crosshair.style.backgroundColor = new Color(1f, 1f, 1f, 0.8f);
            _crosshair.style.borderTopLeftRadius = 3;
            _crosshair.style.borderTopRightRadius = 3;
            _crosshair.style.borderBottomLeftRadius = 3;
            _crosshair.style.borderBottomRightRadius = 3;
            _crosshair.style.display = DisplayStyle.None;
            root.Add(_crosshair);

            _window = new VisualElement();
            _window.pickingMode = PickingMode.Ignore;
            _window.style.position = Position.Absolute;
            _window.style.bottom = 24;
            _window.style.left = Length.Percent(50);
            _window.style.translate = new Translate(Length.Percent(-50), 0);
            _window.style.flexDirection = FlexDirection.Row;
            _window.style.alignItems = Align.Center;
            _window.style.paddingLeft = 10;
            _window.style.paddingRight = 14;
            _window.style.paddingTop = 6;
            _window.style.paddingBottom = 6;
            _window.style.backgroundColor = new Color(0f, 0f, 0f, 0.65f);
            _window.style.borderTopLeftRadius = 6;
            _window.style.borderTopRightRadius = 6;
            _window.style.borderBottomLeftRadius = 6;
            _window.style.borderBottomRightRadius = 6;
            _window.style.display = DisplayStyle.None;
            root.Add(_window);

            _keyLabel = new Label();
            _keyLabel.pickingMode = PickingMode.Ignore;
            _keyLabel.style.fontSize = 16;
            _keyLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _keyLabel.style.color = new Color(0.1f, 0.1f, 0.1f);
            _keyLabel.style.backgroundColor = new Color(0.92f, 0.86f, 0.7f);
            _keyLabel.style.paddingLeft = 7;
            _keyLabel.style.paddingRight = 7;
            _keyLabel.style.paddingTop = 1;
            _keyLabel.style.paddingBottom = 1;
            _keyLabel.style.marginRight = 10;
            _keyLabel.style.borderTopLeftRadius = 3;
            _keyLabel.style.borderTopRightRadius = 3;
            _keyLabel.style.borderBottomLeftRadius = 3;
            _keyLabel.style.borderBottomRightRadius = 3;
            _window.Add(_keyLabel);

            _textLabel = new Label();
            _textLabel.pickingMode = PickingMode.Ignore;
            _textLabel.style.fontSize = 18;
            _textLabel.style.color = Color.white;
            _window.Add(_textLabel);
        }
    }
}

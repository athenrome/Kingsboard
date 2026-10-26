using System;
using Kingsboard.Shared.Models.Pawns;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kingsboard.Pawns
{
    /// <summary>
    /// Shows the selected pawn's details in a panel at the top right of the screen (built with UI Toolkit): its name,
    /// health, Physique and attributes. When a pawn is selected its details are fetched in one call (GetPawnById, through
    /// <see cref="PawnManager.GetPawnById"/>); after that the panel follows the pawn as the server streams new state for it.
    /// The panel is hidden while no pawn is selected.
    /// Lives on the PawnDetails prefab (Assets/Prefabs/Pawns/PawnDetails.prefab), nested in the Pawns prefab so its
    /// <see cref="PawnManager"/> reference is set there.
    /// </summary>
    public class PawnDetailsController : MonoBehaviour
    {
        [Tooltip("The PawnManager whose selected pawn is shown (the root of the Pawns prefab)")]
        [SerializeField] private PawnManager _pawnManager;

        [Tooltip("The UIDocument the panel is built in; its panel settings (theme, scaling) are set on the PawnDetails prefab")]
        [SerializeField] private UIDocument _document;

        private VisualElement _window;
        private VisualElement _swatch;
        private Label _nameLabel;
        private Label _healthLabel;
        private VisualElement _healthFill;
        private Label _physiqueValue;
        private Label _strengthValue;
        private Label _dexterityValue;
        private Label _intelligenceValue;
        private Label _wisdomValue;
        private Label _constitutionValue;

        // The pawn whose details are on show, and the streamed state of it seen last (to spot new updates from the server)
        private PawnController _selected;
        private Pawn _streamed;
        private bool _shownSinceSelected;

        private void Start()
        {
            if (_pawnManager == null || _document == null)
            {
                Debug.LogError("[Pawns] The PawnDetailsController needs its PawnManager and UIDocument assigned. Use the PawnDetails prefab inside the Pawns prefab.", this);
                enabled = false;
                return;
            }

            VisualElement root = _document.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("[Pawns] The pawn details panel could not be created (UIDocument has no root element).");
                enabled = false;
                return;
            }

            BuildWindow(root);
            Show(null);
        }

        private void LateUpdate()
        {
            PawnController selected = _pawnManager.SelectedPawn;
            if (selected != _selected)
            {
                // A new selection: fetch the pawn's full details from the server in one call
                _selected = selected;
                _streamed = selected != null ? selected.Pawn : null;
                _shownSinceSelected = false;
                Show(null);
                if (selected != null)
                {
                    LoadDetails(selected);
                }
                return;
            }

            // The PawnManager replaces a pawn's state object whenever the server sends an update, so a new reference means new data
            if (selected != null && selected.Pawn != _streamed)
            {
                _streamed = selected.Pawn;
                Show(_streamed);
            }
        }

        /// <summary>
        /// Gets <paramref name="pawn"/>'s details with GetPawnById and shows them, unless the selection has moved on or a
        /// newer streamed update was shown while waiting. Falls back to the streamed state if the request fails.
        /// </summary>
        private async void LoadDetails(PawnController pawn)
        {
            Guid id = pawn.Pawn != null ? pawn.Pawn.PublicId : Guid.Empty;
            Pawn details = await _pawnManager.GetPawnById(id);

            // The panel may have been destroyed or the selection changed while waiting
            if (this == null || pawn == null || _selected != pawn || _shownSinceSelected)
            {
                return;
            }
            Show(details ?? pawn.Pawn);
        }

        /// <summary>
        /// Fills the panel with <paramref name="pawn"/>'s details, or hides it when <paramref name="pawn"/> is null
        /// </summary>
        private void Show(Pawn pawn)
        {
            _shownSinceSelected = pawn != null;
            if (pawn == null)
            {
                _window.style.display = DisplayStyle.None;
                return;
            }

            _window.style.display = DisplayStyle.Flex;
            _swatch.style.backgroundColor = PawnService.GetColour(pawn);
            _nameLabel.text = pawn.Name;

            _healthLabel.text = $"Health {pawn.Health} / {pawn.MaxHealth}";
            float fraction = pawn.MaxHealth > 0 ? Mathf.Clamp01((float)pawn.Health / pawn.MaxHealth) : 0f;
            _healthFill.style.width = Length.Percent(fraction * 100f);

            _physiqueValue.text = pawn.Attributes.Physique.ToString();
            _strengthValue.text = pawn.Attributes.Strength.ToString();
            _dexterityValue.text = pawn.Attributes.Dexterity.ToString();
            _intelligenceValue.text = pawn.Attributes.Intelligence.ToString();
            _wisdomValue.text = pawn.Attributes.Wisdom.ToString();
            _constitutionValue.text = pawn.Attributes.Constitution.ToString();
        }

        private void BuildWindow(VisualElement root)
        {
            // Let clicks outside the panel through to the game
            root.pickingMode = PickingMode.Ignore;

            _window = new VisualElement();
            _window.pickingMode = PickingMode.Ignore;
            _window.style.position = Position.Absolute;
            _window.style.top = 16;
            _window.style.right = 16;
            _window.style.width = 220;
            _window.style.paddingLeft = 12;
            _window.style.paddingRight = 12;
            _window.style.paddingTop = 10;
            _window.style.paddingBottom = 10;
            _window.style.backgroundColor = new Color(0f, 0f, 0f, 0.65f);
            _window.style.borderTopLeftRadius = 6;
            _window.style.borderTopRightRadius = 6;
            _window.style.borderBottomLeftRadius = 6;
            _window.style.borderBottomRightRadius = 6;
            root.Add(_window);

            VisualElement header = new VisualElement();
            header.pickingMode = PickingMode.Ignore;
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 6;
            _window.Add(header);

            _swatch = new VisualElement();
            _swatch.pickingMode = PickingMode.Ignore;
            _swatch.style.width = 12;
            _swatch.style.height = 12;
            _swatch.style.marginRight = 8;
            _swatch.style.borderTopLeftRadius = 6;
            _swatch.style.borderTopRightRadius = 6;
            _swatch.style.borderBottomLeftRadius = 6;
            _swatch.style.borderBottomRightRadius = 6;
            header.Add(_swatch);

            _nameLabel = CreateLabel(18, Color.white);
            _nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(_nameLabel);

            _healthLabel = CreateLabel(13, new Color(0.85f, 0.85f, 0.85f));
            _window.Add(_healthLabel);

            VisualElement healthBar = new VisualElement();
            healthBar.pickingMode = PickingMode.Ignore;
            healthBar.style.height = 6;
            healthBar.style.marginTop = 2;
            healthBar.style.marginBottom = 8;
            healthBar.style.backgroundColor = new Color(1f, 1f, 1f, 0.15f);
            _window.Add(healthBar);

            _healthFill = new VisualElement();
            _healthFill.pickingMode = PickingMode.Ignore;
            _healthFill.style.height = Length.Percent(100);
            _healthFill.style.backgroundColor = new Color(0.4f, 0.85f, 0.4f);
            healthBar.Add(_healthFill);

            _physiqueValue = AddStatRow("Physique", true);
            _strengthValue = AddStatRow("Strength", false);
            _dexterityValue = AddStatRow("Dexterity", false);
            _intelligenceValue = AddStatRow("Intelligence", false);
            _wisdomValue = AddStatRow("Wisdom", false);
            _constitutionValue = AddStatRow("Constitution", false);
        }

        /// <summary>
        /// Adds a "name ... value" row to the panel and returns the value label
        /// </summary>
        private Label AddStatRow(string name, bool highlight)
        {
            VisualElement row = new VisualElement();
            row.pickingMode = PickingMode.Ignore;
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.alignItems = Align.Center;
            row.style.minHeight = 20;
            if (highlight)
            {
                row.style.marginBottom = 4;
            }
            _window.Add(row);

            Color colour = highlight ? Color.white : new Color(0.75f, 0.75f, 0.75f);
            row.Add(CreateLabel(14, colour, name));

            Label value = CreateLabel(14, Color.white);
            value.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(value);
            return value;
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

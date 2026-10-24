using System.Collections;
using System.Text;
using UnityEngine;

namespace Kingsboard.Pawns
{
    /// <summary>
    /// A speech bubble over a pawn's head: shows what the pawn said, turned to face the camera, until it is told to fade.
    /// Lives on the SpeechBubble prefab (Assets/Prefabs/Pawns/SpeechBubble.prefab), nested in the Pawn prefab; the pawn's
    /// <see cref="PawnController"/> decides when to show and hide it. The prefab starts hidden.
    /// </summary>
    public class SpeechBubbleController : MonoBehaviour
    {
        [Tooltip("The text of the bubble")]
        [SerializeField] private TextMesh _text;

        [Tooltip("The bubble's background (a quad behind the text); stretched to fit the text")]
        [SerializeField] private Renderer _background;

        [Tooltip("Space around the text inside the bubble, in world units")]
        [SerializeField] private Vector2 _padding = new Vector2(0.12f, 0.08f);

        [Tooltip("Longest line before the text wraps onto the next one, in characters")]
        [SerializeField] private int _maxLineLength = 24;

        [Tooltip("Seconds the bubble takes to fade away")]
        [SerializeField] private float _fadeSeconds = 0.75f;

        private Material _backgroundMaterial;
        private Color _textColour;
        private Color _backgroundColour;
        private Coroutine _fade;
        private bool _hideWhenShown;

        /// <summary>
        /// True while the bubble is on show (including while it fades)
        /// </summary>
        public bool IsShowing => gameObject.activeSelf;

        /// <summary>
        /// Shows <paramref name="text"/> in the bubble at full opacity, replacing anything it was showing
        /// </summary>
        public void Show(string text)
        {
            CacheColours();
            StopFade();
            _hideWhenShown = false;

            gameObject.SetActive(true);
            _text.text = Wrap(text);
            SetOpacity(1f);
            FitBackground();
        }

        /// <summary>
        /// Fades the bubble out, then hides it. Does nothing if it is hidden or already fading.
        /// </summary>
        public void FadeOut()
        {
            if (!gameObject.activeSelf || _fade != null)
            {
                return;
            }

            // A bubble on a hidden pawn can't run a coroutine; there's nothing to see fade anyway
            if (!isActiveAndEnabled)
            {
                Hide();
                return;
            }
            _fade = StartCoroutine(Fade());
        }

        /// <summary>
        /// Hides the bubble straight away
        /// </summary>
        public void Hide()
        {
            StopFade();
            _hideWhenShown = false;
            gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            // A bubble shown while its pawn was hidden couldn't measure its text then
            if (_backgroundMaterial != null)
            {
                FitBackground();
            }
        }

        private void LateUpdate()
        {
            // A fade cut short by the pawn being hidden ends hidden (state can't change while being disabled, so it's done here)
            if (_hideWhenShown)
            {
                _hideWhenShown = false;
                gameObject.SetActive(false);
                return;
            }

            // Face the camera, like the pawn's name label
            Camera camera = Camera.main;
            if (camera != null)
            {
                transform.rotation = camera.transform.rotation;
            }
        }

        private void OnDisable()
        {
            // Coroutines stop when the bubble or its pawn is hidden
            if (_fade != null)
            {
                _fade = null;
                _hideWhenShown = true;
            }
        }

        private void OnDestroy()
        {
            if (_backgroundMaterial != null)
            {
                Destroy(_backgroundMaterial);
            }
        }

        private IEnumerator Fade()
        {
            for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(0.01f, _fadeSeconds))
            {
                SetOpacity(1f - t);
                yield return null;
            }

            _fade = null;
            gameObject.SetActive(false);
        }

        private void StopFade()
        {
            if (_fade != null)
            {
                StopCoroutine(_fade);
                _fade = null;
            }
        }

        /// <summary>
        /// Remembers the colours set on the prefab and gives the background its own material, so fading one bubble
        /// doesn't fade them all
        /// </summary>
        private void CacheColours()
        {
            if (_backgroundMaterial != null)
            {
                return;
            }

            _textColour = _text.color;
            _backgroundMaterial = new Material(_background.sharedMaterial);
            _background.sharedMaterial = _backgroundMaterial;
            _backgroundColour = _backgroundMaterial.color;
        }

        private void SetOpacity(float opacity)
        {
            _text.color = new Color(_textColour.r, _textColour.g, _textColour.b, _textColour.a * opacity);
            _backgroundMaterial.color = new Color(_backgroundColour.r, _backgroundColour.g, _backgroundColour.b, _backgroundColour.a * opacity);
        }

        /// <summary>
        /// Stretches the background to the text's size plus padding. The text is measured while the bubble is unrotated,
        /// because its renderer's bounds are an axis-aligned box in world space.
        /// </summary>
        private void FitBackground()
        {
            Quaternion rotation = transform.rotation;
            transform.rotation = Quaternion.identity;

            Bounds bounds = _text.GetComponent<Renderer>().bounds;
            Vector3 scale = transform.lossyScale;
            float width = bounds.size.x / Mathf.Max(0.0001f, scale.x) + _padding.x * 2f;
            float height = bounds.size.y / Mathf.Max(0.0001f, scale.y) + _padding.y * 2f;
            Vector3 centre = transform.InverseTransformPoint(bounds.center);

            transform.rotation = rotation;
            _background.transform.localScale = new Vector3(width, height, 1f);
            // Centred on the text and just behind it (the bubble faces the camera, so +Z is away from it)
            _background.transform.localPosition = new Vector3(centre.x, centre.y, _background.transform.localPosition.z);
        }

        /// <summary>
        /// Breaks the text into lines of at most <see cref="_maxLineLength"/> characters at spaces (TextMesh doesn't wrap)
        /// </summary>
        private string Wrap(string text)
        {
            if (string.IsNullOrEmpty(text) || _maxLineLength <= 0)
            {
                return text ?? string.Empty;
            }

            StringBuilder wrapped = new StringBuilder(text.Length + 8);
            int lineLength = 0;
            foreach (string word in text.Split(' '))
            {
                if (lineLength > 0 && lineLength + 1 + word.Length > _maxLineLength)
                {
                    wrapped.Append('\n');
                    lineLength = 0;
                }
                else if (lineLength > 0)
                {
                    wrapped.Append(' ');
                    lineLength++;
                }

                wrapped.Append(word);
                lineLength += word.Length;
            }
            return wrapped.ToString();
        }
    }
}

using UnityEngine;
using UnityEngine.UI;

namespace KnitSprite.UI.HowToKnit
{
    /// <summary>
    /// The keycap cue drawn over the animation: a big pixel keycap with a "Press" / "Hold" tag.
    ///
    ///  Press / Release  blinks between up and pressed (0.3s each) until the player acts
    ///  Hold             sits pressed in
    ///  GetReady         the NEXT key, faded to 60% and still - shown a moment early
    /// </summary>
    public class HtkKeyPrompt
    {
        public enum Mode { Hidden, Press, Hold, Release, GetReady }

        const float Size = 150f, Cap = 88f, BlinkInterval = 0.3f, PressedDrop = 7f;

        readonly RectTransform root;
        readonly CanvasGroup group;
        readonly Image cap;
        readonly Text letter, tag;
        readonly RectTransform letterRt;
        readonly Sprite up, down;
        Mode mode = Mode.Hidden;
        float blinkTimer;
        bool blinkDown;

        public HtkKeyPrompt(RectTransform parent)
        {
            root = HtkUI.AtCentre(HtkUI.Node("KeyPrompt", parent), 0f, 0f, Size, Size + 40f);
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            up = HtkTheme.UI("keycap_up");
            down = HtkTheme.UI("keycap_down");

            cap = HtkUI.Img(root, "Keycap", up, (Size - Cap) / 2f, 31f, Cap, Cap);
            letter = HtkUI.Label(cap.transform, "A", HtkTheme.PixelBold, 44, HtkTheme.Ink, 0f, 0f, Cap, Cap - 7f,
                                 TextAnchor.MiddleCenter);
            letterRt = letter.rectTransform;

            var pill = HtkUI.Img(root, "Tag", HtkTheme.UI("pill_outline"), (Size - 120f) / 2f, 31f + Cap + 16f, 120f, 34f);
            tag = HtkUI.Label(pill.transform, "Press", HtkTheme.Pixel, 16, HtkTheme.Ink, 0f, 0f, 120f, 34f,
                              TextAnchor.MiddleCenter);

            Set(Mode.Hidden, ' ', Vector2.zero);
        }

        /// <param name="centre">Position in the parent, as a fraction of its size from the top-left
        /// (e.g. 0.84, 0.28) - the same percentages the canvas uses.</param>
        public void Set(Mode newMode, char key, Vector2 centre)
        {
            if (newMode != mode) { blinkTimer = 0f; blinkDown = false; }
            mode = newMode;

            root.gameObject.SetActive(mode != Mode.Hidden);
            if (mode == Mode.Hidden) return;

            var parentSize = ((RectTransform)root.parent).rect.size;
            root.anchoredPosition = new Vector2(centre.x * parentSize.x, -centre.y * parentSize.y);

            letter.text = key.ToString();
            group.alpha = mode == Mode.GetReady ? 0.6f : 1f;
            tag.text = mode == Mode.Hold ? "Hold" : mode == Mode.Release ? "Let go"
                     : mode == Mode.GetReady ? "Get ready" : "Press";
            Apply(mode == Mode.Hold);
        }

        public void Tick(float dt)
        {
            if (mode != Mode.Press && mode != Mode.Release) return;
            blinkTimer += dt;
            if (blinkTimer >= BlinkInterval)
            {
                blinkTimer -= BlinkInterval;
                blinkDown = !blinkDown;
                Apply(blinkDown);
            }
        }

        void Apply(bool pressed)
        {
            cap.sprite = pressed ? down : up;
            // keycap_down draws the cap 7px lower inside the same box; move the letter with it
            letterRt.anchoredPosition = new Vector2(0f, pressed ? -PressedDrop : 0f);
        }
    }
}

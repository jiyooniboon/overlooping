using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KnitSprite.UI.HowToKnit
{
    /// <summary>
    /// Screen 1 - title page. Add to an empty GameObject in a scene and press Play.
    ///
    /// "How to / Knit" heading, Start learning + Settings buttons, two tilted art cards
    /// (cast-on drawing in front, knit drawing behind). Start learning plays the dog-shadow walk,
    /// then loads <see cref="lessonSelectScene"/> (if set) and fires <see cref="onStart"/>.
    /// </summary>
    public class HtkTitlePage : MonoBehaviour
    {
        [Header("Navigation")]
        [Tooltip("Scene to load after the dog walks past. Leave empty to only fire onStart.")]
        public string lessonSelectScene = "";
        public UnityEvent onStart;
        public UnityEvent onSettings;

        [Header("Look")]
        public Color accent = HtkTheme.Yarn;
        public int sortingOrder = 100;

        [Header("Trailer")]
        [Tooltip("Play the dog-shadow walk before leaving the title screen. Off for now - trailer only.")]
        public bool playDogWalk = false;

        HtkDogShadow dog;

        void Awake()
        {
            HtkUI.EnsureEventSystem();
            var screen = HtkUI.Canvas(transform, "TitleCanvas", sortingOrder);
            HtkBrowserChrome.Build(screen, "/", HtkBrowserChrome.Tab.About,
                                   tab => { if (tab == HtkBrowserChrome.Tab.Tutorials) StartLearning(); });

            // Layout: main padding 90/120, two equal columns 60px apart.
            const float top = HtkBrowserChrome.ContentTop + 90f;   // 254
            const float left = 120f;

            // ---- heading ----
            HtkUI.Label(screen, "How to", HtkTheme.PixelBold, 172, HtkTheme.Ink, left, top - 10f, 900f, 172f,
                        TextAnchor.UpperLeft);
            HtkUI.Label(screen, "Knit", HtkTheme.PixelBold, 172, accent, left, top + 145f, 900f, 172f,
                        TextAnchor.UpperLeft);

            // ---- buttons ----
            float by = top + 310f + 64f;  // 628
            MenuButton(screen, "01", "Start learning", left, by, true, StartLearning);
            MenuButton(screen, "02", "Settings", left, by + 94f, false, () => onSettings?.Invoke());

            // ---- art cards (CSS rotate(5deg) / rotate(-3deg)) ----
            const float colX = 990f;

            var back = HtkUI.MaskedCard(screen, "BackCard", colX + 170f, top, 640f, 370f, HtkTheme.Card,
                                        "card_outline", centrePivot: true);
            var backRoot = (RectTransform)back.parent.parent;
            backRoot.localEulerAngles = new Vector3(0, 0, -5f);   // CSS rotate(5deg)
            backRoot.gameObject.AddComponent<CanvasGroup>().alpha = 0.55f;
            // padding 18px, contain, nudged up 12% of the card height
            HtkUI.FitArt(back, HtkTheme.Art("title_card_knit"), 18f, 18f, 640f - 36f, 370f - 36f,
                         contain: true, offsetY: -0.12f * 366f);

            HtkUI.RotateCss(HtkUI.Shadow(screen, colX, top + 120f, 760f, 480f, 0.16f, 30f, 60f).rectTransform, -3f);
            var front = HtkUI.MaskedCard(screen, "FrontCard", colX, top + 120f, 760f, 480f, HtkTheme.Card,
                                         "card_outline", centrePivot: true);
            ((RectTransform)front.parent.parent).localEulerAngles = new Vector3(0, 0, 3f);  // CSS rotate(-3deg)
            // no padding, contain, scaled 1.18 and nudged down
            HtkUI.FitArt(front, HtkTheme.Art("title_card_caston"), 0f, 0f, 760f, 480f,
                         contain: true, scale: 1.18f, offsetY: 28f);

            // ---- dog shadow overlay (on top of everything) ----
            if (playDogWalk)
            {
                dog = gameObject.AddComponent<HtkDogShadow>();
                dog.Build(screen);
            }
        }

        void StartLearning()
        {
            if (dog != null) dog.Play(GoToLessons);
            else GoToLessons();
        }

        void GoToLessons()
        {
            onStart?.Invoke();
            if (!string.IsNullOrEmpty(lessonSelectScene)) SceneManager.LoadScene(lessonSelectScene);
        }

        void MenuButton(RectTransform parent, string num, string label, float x, float y, bool primary,
                        System.Action onClick)
        {
            const float w = 540f, h = 76f;
            var root = HtkUI.Box(parent, "Button_" + label, x, y, w, h);

            // hard hover shadow sits 5px down-right of the lifted face
            var hard = HtkUI.Img(root, "HardShadow", HtkTheme.UI("button_dark"), 2f, 2f, w, h);

            var face = HtkUI.Box(root, "Face", 0f, 0f, w, h);
            HtkUI.AddImage(face, HtkTheme.UI(primary ? "button_dark" : "button_light"));
            Color fg = primary ? HtkTheme.Card : HtkTheme.Ink;
            HtkUI.Label(face, num, HtkTheme.Pixel, 17, HtkTheme.WithAlpha(fg, 0.7f), 28f, 0f, 40f, h);
            HtkUI.Label(face, label, HtkTheme.Pixel, 24, fg, 28f + 40f, 0f, w - 140f, h);
            HtkUI.Icon(face, "icon_arrow_right", w - 28f - 26f, (h - 26f) / 2f, 26f, fg);

            var lift = root.gameObject.AddComponent<HtkHoverLift>();
            lift.face = face;
            lift.hardShadow = hard;

            var hit = root.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            HtkUI.MakeButton(root.gameObject, onClick);
        }
    }
}

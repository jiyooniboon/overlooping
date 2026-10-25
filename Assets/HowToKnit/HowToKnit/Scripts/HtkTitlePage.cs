using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace KnitSprite.UI.HowToKnit
{
    /// <summary>
    /// Screen 1 - title page with mode select built in. Add to an empty GameObject and press Play.
    ///
    /// "How to / Knit" heading with Start game + Settings. Start game slides the two art cards off
    /// to the right and slides in three mode buttons (Story, Creative, Practice) in their place;
    /// Back reverses it. Picking a mode loads that mode's scene (optionally after the dog walk).
    /// The title screen has no tab row.
    /// </summary>
    public class HtkTitlePage : MonoBehaviour
    {
        public enum Mode { Story = 0, Creative = 1, Practice = 2 }

        [Header("Navigation")]
        [Tooltip("Story -> lessonSelect, Creative -> creative, Practice -> practice.")]
        public HtkScenes scenes = new HtkScenes();
        [Tooltip("Fired with the mode picked (0 Story, 1 Creative, 2 Practice) before its scene loads.")]
        public UnityEvent<int> onModePicked;
        public UnityEvent onSettings;

        [Header("Trailer")]
        [Tooltip("Play the dog-shadow walk after a mode is picked, before leaving the title screen.")]
        public bool playDogWalk = false;

        [Header("Motion (seconds)")]
        public float cardsOutTime = 0.55f;
        public float cardsInTime = 0.6f;
        public float modesDelay = 0.35f;
        public float modesStagger = 0.08f;
        public float modeSlideTime = 0.45f;

        [Header("Look")]
        public Color accent = HtkTheme.Yarn;
        public int sortingOrder = 100;

        HtkDogShadow dog;
        RectTransform cards, modes;
        readonly RectTransform[] modeItems = new RectTransform[4];
        readonly CanvasGroup[] modeGroups = new CanvasGroup[4];
        readonly Vector2[] modeRest = new Vector2[4];
        Vector2 cardsRest;
        enum State { Closed, Opening, Open, Closing }
        State state = State.Closed;
        float timer;
        bool leaving;

        const float Top = HtkBrowserChrome.ContentTop + 90f;   // 254, same as with the tab row
        const float Left = 120f, ColX = 990f;
        const float CardsTravel = 1150f, CardsTilt = -10f;      // CSS translateX(1150px) rotate(10deg)

        void Awake()
        {
            HtkUI.EnsureEventSystem();
            var screen = HtkUI.Canvas(transform, "TitleCanvas", sortingOrder);
            HtkBrowserChrome.Build(screen, "/", HtkBrowserChrome.Tab.None, scenes, null, showTabs: false);

            // ---- heading ----
            HtkUI.Label(screen, "How to", HtkTheme.PixelBold, 172, HtkTheme.Ink, Left, Top - 10f, 900f, 172f, TextAnchor.UpperLeft);
            HtkUI.Label(screen, "Knit", HtkTheme.PixelBold, 172, accent, Left, Top + 145f, 900f, 172f, TextAnchor.UpperLeft);

            // ---- buttons ----
            float by = Top + 310f + 64f;  // 628
            MenuButton(screen, "01", "Start game", Left, by, true, OpenModes);
            MenuButton(screen, "02", "Settings", Left, by + 94f, false, () => onSettings?.Invoke());

            // ---- art cards, grouped so they leave together ----
            cards = HtkUI.Box(screen, "Cards", ColX, Top, 830f, 640f);
            cards.pivot = new Vector2(0.5f, 0.5f);
            cards.anchoredPosition += new Vector2(415f, -320f);
            cardsRest = cards.anchoredPosition;

            var back = HtkUI.MaskedCard(cards, "BackCard", 170f, 0f, 640f, 370f, HtkTheme.Card, "card_outline", centrePivot: true);
            var backRoot = (RectTransform)back.parent.parent;
            backRoot.localEulerAngles = new Vector3(0, 0, -5f);    // CSS rotate(5deg)
            backRoot.gameObject.AddComponent<CanvasGroup>().alpha = 0.55f;
            HtkUI.FitArt(back, HtkTheme.Art("title_card_knit"), 18f, 18f, 604f, 334f, contain: true, offsetY: -0.12f * 366f);

            HtkUI.RotateCss(HtkUI.Shadow(cards, 0f, 120f, 760f, 480f, 0.16f, 30f, 60f).rectTransform, -3f);
            var front = HtkUI.MaskedCard(cards, "FrontCard", 0f, 120f, 760f, 480f, HtkTheme.Card, "card_outline", centrePivot: true);
            ((RectTransform)front.parent.parent).localEulerAngles = new Vector3(0, 0, 3f);  // CSS rotate(-3deg)
            HtkUI.FitArt(front, HtkTheme.Art("title_card_caston"), 0f, 0f, 760f, 480f, contain: true, scale: 1.18f, offsetY: 28f);

            // ---- mode select (hidden until Start game) ----
            BuildModes(screen);

            if (playDogWalk)
            {
                dog = gameObject.AddComponent<HtkDogShadow>();
                dog.Build(screen);
            }
        }

        void BuildModes(RectTransform screen)
        {
            modes = HtkUI.Box(screen, "ModeSelect", ColX, Top + 20f, 780f, 600f);

            // header row: "Pick a mode" + Back
            var head = HtkUI.Box(modes, "Header", 0f, 0f, 780f, 48f);
            HtkUI.Label(head, "Pick a mode", HtkTheme.PixelBold, 40, HtkTheme.Ink, 0f, 0f, 600f, 48f);
            var back = HtkUI.Img(head, "Back", HtkTheme.UI("button_light"), 780f - 120f, 2f, 120f, 44f);
            HtkUI.Icon(back.transform, "icon_arrow_left", 16f, 12f, 20f, HtkTheme.Ink);
            HtkUI.Label(back.transform, "Back", HtkTheme.Pixel, 15, HtkTheme.Ink, 44f, 0f, 70f, 44f);
            HtkUI.MakeButton(back.gameObject, CloseModes);
            Register(0, head);

            ModeButton(1, Mode.Story, "01", "Story", "Follow the tutorials one move at a time, from slip knot to first scarf.",
                       "lesson_caston", true);
            ModeButton(2, Mode.Creative, "02", "Creative", "Paint your own colourwork chart, or let the generator start one.",
                       "mode_creative_thumb", false);
            ModeButton(3, Mode.Practice, "03", "Practice", "Drill the timing on its own and see if you're early or late.",
                       "mode_practice_thumb", false);

            modes.gameObject.SetActive(false);
        }

        void ModeButton(int slot, Mode mode, string num, string title, string desc, string art, bool cover)
        {
            const float w = 780f, h = 160f;
            float y = 64f + (slot - 1) * (h + 20f);
            var root = HtkUI.Box(modes, "Mode_" + title, 0f, y, w, h);

            var hard = HtkUI.Img(root, "HardShadow", HtkTheme.UI("button_dark"), 2f, 2f, w, h);
            var face = HtkUI.Box(root, "Face", 0f, 0f, w, h);
            HtkUI.AddImage(face, HtkTheme.UI("card"));

            // thumbnail
            var thumb = HtkUI.MaskedCard(face, "Thumb", 18f, 19f, 200f, 122f, Color.white);
            var sprite = HtkTheme.Art(art);
            if (cover) HtkUI.FitArt(thumb, sprite, 0f, 0f, 200f, 122f, contain: false, focusY: 0.14f);
            else       HtkUI.FitArt(thumb, sprite, 10f, 8f, 180f, 106f, contain: true);

            HtkUI.Label(face, "Mode " + num, HtkTheme.Pixel, 14, HtkTheme.Muted, 244f, 22f, 300f, 20f);
            HtkUI.Label(face, title, HtkTheme.PixelBold, 34, HtkTheme.Ink, 244f, 46f, 420f, 40f);
            HtkUI.Label(face, desc, HtkTheme.Mono, 17, HtkTheme.Ink, 244f, 94f, 440f, 50f, TextAnchor.UpperLeft, wrap: true, lineSpacing: 1.15f);
            HtkUI.Icon(face, "icon_arrow_right", w - 26f - 26f, (h - 26f) / 2f, 26f, HtkTheme.Ink);

            var lift = root.gameObject.AddComponent<HtkHoverLift>();
            lift.face = face;
            lift.hardShadow = hard;
            var hit = root.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            HtkUI.MakeButton(root.gameObject, () => Pick(mode));
            Register(slot, root);
        }

        void Register(int slot, RectTransform rt)
        {
            modeItems[slot] = rt;
            modeRest[slot] = rt.anchoredPosition;
            modeGroups[slot] = rt.gameObject.AddComponent<CanvasGroup>();
        }

        // ---- flow -------------------------------------------------------------------------------

        void OpenModes()
        {
            if (state == State.Open || state == State.Opening || leaving) return;
            state = State.Opening;
            timer = 0f;
            modes.gameObject.SetActive(true);
            SetModes(0f);
        }

        void CloseModes()
        {
            if (state != State.Open || leaving) return;
            state = State.Closing;
            timer = 0f;
            modes.gameObject.SetActive(false);
        }

        void Pick(Mode mode)
        {
            if (leaving || state != State.Open) return;
            leaving = true;
            System.Action go = () =>
            {
                onModePicked?.Invoke((int)mode);
                HtkScenes.Load(mode == Mode.Story ? scenes.lessonSelect
                             : mode == Mode.Creative ? scenes.creative : scenes.practice);
                leaving = false;
            };
            if (dog != null) dog.Play(go); else go();
        }

        void Update()
        {
            if (state == State.Closed || state == State.Open) return;
            timer += Time.unscaledDeltaTime;

            if (state == State.Opening)
            {
                // cards: accelerate out to the right with a tilt
                float u = Mathf.Clamp01(timer / cardsOutTime);
                float e = u * u * u;
                cards.anchoredPosition = cardsRest + new Vector2(CardsTravel * e, 0f);
                cards.localEulerAngles = new Vector3(0f, 0f, CardsTilt * e);
                SetModes(timer);
                if (timer >= modesDelay + modesStagger * 3f + modeSlideTime && u >= 1f)
                {
                    state = State.Open;
                    cards.gameObject.SetActive(false);
                }
            }
            else // Closing: cards glide back in
            {
                cards.gameObject.SetActive(true);
                float u = Mathf.Clamp01(timer / cardsInTime);
                float e = 1f - Mathf.Pow(1f - u, 3f);
                cards.anchoredPosition = cardsRest + new Vector2(CardsTravel * (1f - e), 0f);
                cards.localEulerAngles = new Vector3(0f, 0f, CardsTilt * (1f - e));
                if (u >= 1f) state = State.Closed;
            }
        }

        void SetModes(float t)
        {
            for (int i = 0; i < modeItems.Length; i++)
            {
                if (modeItems[i] == null) continue;
                float u = Mathf.Clamp01((t - modesDelay - i * modesStagger) / modeSlideTime);
                float e = 1f - Mathf.Pow(1f - u, 3f);
                modeItems[i].anchoredPosition = modeRest[i] + new Vector2(160f * (1f - e), 0f);
                modeGroups[i].alpha = e;
                modeGroups[i].blocksRaycasts = u >= 1f;
            }
        }

        void MenuButton(RectTransform parent, string num, string label, float x, float y, bool primary,
                        System.Action onClick)
        {
            const float w = 540f, h = 76f;
            var root = HtkUI.Box(parent, "Button_" + label, x, y, w, h);
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

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KnitSprite.UI.HowToKnit
{
    /// <summary>
    /// Screen 2 - lesson select ("Tutorials"). Six lesson cards in a 3x2 grid, a progress strip,
    /// and a bar along the bottom with the selected lesson's details and a Start button.
    ///
    /// Lessons, their state and art are editable in the Inspector. Start loads the lesson's
    /// scene (if set) and fires onStartLesson with the lesson index.
    /// </summary>
    public class HtkLessonSelectPage : MonoBehaviour
    {
        public enum LessonState { Done, Next, Locked }

        [Serializable]
        public class Lesson
        {
            public string title;
            [TextArea] public string description;
            public LessonState state;
            [Tooltip("Resources/HowToKnit/Art file name, e.g. lesson_caston. Empty = blank card.")]
            public string art;
            [Tooltip("Cover crop: 0 = top, 1 = bottom. The slip-knot card uses a zoomed crop instead.")]
            [Range(0f, 1f)] public float focusY = 0.5f;
            public float zoom = 1f;
            [Tooltip("Scene to load for this lesson. Empty = only fire onStartLesson.")]
            public string scene;
        }

        public List<Lesson> lessons = new List<Lesson>
        {
            new Lesson { title = "Slip Knot",   state = LessonState.Done,   art = "lesson_slipknot", zoom = 1.25f, focusY = 0.13f,
                         description = "3 steps · tie the very first loop on your needle" },
            new Lesson { title = "Cast On",     state = LessonState.Next,   art = "lesson_caston", focusY = 0.12f,
                         description = "5 steps · build a row of loops to knit into" },
            new Lesson { title = "Knit Stitch", state = LessonState.Locked, description = "4 steps · insert, wrap, pull through, slip off" },
            new Lesson { title = "Purl Stitch", state = LessonState.Locked, description = "4 steps · the knit stitch, mirrored" },
            new Lesson { title = "Bind Off",    state = LessonState.Locked, description = "3 steps · finish an edge so nothing unravels" },
            new Lesson { title = "First Scarf", state = LessonState.Locked, description = "Project · put every stitch together" },
        };

        public int selected = 1;
        public UnityEvent<int> onStartLesson;
        [Tooltip("Scenes for the tabs and the About dialog.")]
        public HtkScenes scenes = new HtkScenes();
        public Color accent = HtkTheme.Yarn;
        public int sortingOrder = 100;

        // runtime
        readonly List<CardView> cards = new List<CardView>();
        Text barTitle, barDesc, ctaLabel;
        GameObject ctaButton, lockedNote;
        Text lockedText;

        class CardView
        {
            public RectTransform root;
            public Image outline;
            public Vector2 rest;
        }

        void Awake()
        {
            HtkUI.EnsureEventSystem();
            var screen = HtkUI.Canvas(transform, "LessonSelectCanvas", sortingOrder);
            GameObject about = null;
            HtkBrowserChrome.Build(screen, "/tutorials", HtkBrowserChrome.Tab.Tutorials, scenes,
                                   () => { if (about != null) about.SetActive(true); });

            const float left = 60f, width = 1800f;
            float top = HtkBrowserChrome.ContentTop + 40f; // 204

            // ---- header ----
            HtkUI.Label(screen, "Tutorials", HtkTheme.PixelBold, 64, HtkTheme.Ink, left, top, 900f, 70f, TextAnchor.UpperLeft);
            HtkUI.Label(screen, "Pick a lesson. Each one teaches a single move.", HtkTheme.Mono, 20, HtkTheme.Muted,
                        left, top + 82f, 900f, 28f, TextAnchor.UpperLeft);

            int done = 0;
            foreach (var l in lessons) if (l.state == LessonState.Done) done++;
            HtkUI.Label(screen, $"{done} of {lessons.Count} lessons", HtkTheme.Pixel, 20, HtkTheme.Ink,
                        left + width - 400f, top + 46f, 400f, 26f, TextAnchor.MiddleRight);
            float segX = left + width - (lessons.Count * 48f - 8f);
            for (int i = 0; i < lessons.Count; i++)
                HtkUI.Img(screen, "Seg", HtkTheme.UI(i < done ? "tick_filled" : "tick_empty"),
                          segX + i * 48f, top + 84f, 40f, 16f, i < done ? AccentTint() : Color.white);

            // ---- cards ----
            float gridTop = top + 126f;
            const float cardW = 580f, cardH = 286f, gap = 30f, thumbH = 168f;
            for (int i = 0; i < lessons.Count; i++)
            {
                int col = i % 3, row = i / 3;
                cards.Add(BuildCard(screen, i, left + col * (cardW + gap), gridTop + row * (cardH + gap), cardW, cardH, thumbH));
            }

            // ---- bottom bar ----
            float barTop = gridTop + 2 * cardH + gap + 34f;
            var bar = HtkUI.Img(screen, "DetailBar", HtkTheme.UI("card"), left, barTop, width, 104f).rectTransform;
            barTitle = HtkUI.Label(bar, "", HtkTheme.PixelBold, 30, HtkTheme.Ink, 34f, 16f, 1200f, 38f);
            barDesc = HtkUI.Label(bar, "", HtkTheme.Mono, 19, HtkTheme.Muted, 34f, 58f, 1200f, 28f);

            var cta = HtkUI.Box(bar, "Start", width - 22f - 300f, 18f, 300f, 68f);
            HtkUI.AddImage(cta, HtkTheme.UI("button_dark"));
            ctaLabel = HtkUI.Label(cta, "Start lesson", HtkTheme.Pixel, 22, HtkTheme.Card, 30f, 0f, 220f, 68f);
            HtkUI.Icon(cta, "icon_arrow_right", 300f - 30f - 26f, 21f, 26f, HtkTheme.Card);
            HtkUI.MakeButton(cta.gameObject, StartSelected);
            ctaButton = cta.gameObject;

            var locked = HtkUI.Box(bar, "Locked", width - 22f - 400f, 18f, 400f, 68f);
            var lockImg = HtkUI.AddImage(locked, HtkTheme.UI("button_dashed"));
            lockImg.type = Image.Type.Tiled;
            HtkUI.Icon(locked, "icon_lock", 26f, 23f, 22f, HtkTheme.Ink);
            lockedText = HtkUI.Label(locked, "", HtkTheme.Mono, 19, HtkTheme.Ink, 60f, 0f, 330f, 68f);
            lockedNote = locked.gameObject;

            Select(Mathf.Clamp(selected, 0, lessons.Count - 1));

            about = HtkAboutDialog.Build(screen, scenes);   // built last so it draws on top
        }

        CardView BuildCard(RectTransform parent, int index, float x, float y, float w, float h, float thumbH)
        {
            var l = lessons[index];
            bool isLocked = l.state == LessonState.Locked;
            string outline = isLocked ? "card_dashed" : "card_outline";

            var content = HtkUI.MaskedCard(parent, "Lesson_" + l.title, x, y, w, h, HtkTheme.Card, outline);
            var root = (RectTransform)content.parent.parent;

            // thumbnail
            var thumb = HtkUI.Box(content, "Thumb", 0f, 0f, w, thumbH);
            thumb.gameObject.AddComponent<RectMask2D>();
            if (!string.IsNullOrEmpty(l.art))
            {
                var art = HtkTheme.Art(l.art);
                // cover crop; zoom > 1 enlarges and anchors to the right (slip knot: focus on the right hand)
                HtkUI.FitArt(thumb, art, 0f, 0f, w, thumbH, contain: false, scale: l.zoom,
                             focusX: l.zoom > 1f ? 1f : 0.5f, focusY: l.focusY);
            }
            if (isLocked) HtkUI.Icon(thumb, "icon_lock", (w - 40f) / 2f, (thumbH - 40f) / 2f, 40f, HtkTheme.Ink);
            HtkUI.Fill(content, "ThumbRule", 0f, thumbH - 2f, w, 2f, HtkTheme.Ink);

            // text
            float ty = thumbH + 18f;
            var chip = HtkUI.Img(content, "Chip", HtkTheme.UI("pill"), 24f, ty, 120f, 32f);
            HtkUI.Label(chip.transform, $"Lesson {index + 1:00}", HtkTheme.Pixel, 16, HtkTheme.Ink, 0, 0, 120f, 32f, TextAnchor.MiddleCenter);

            string status = l.state == LessonState.Done ? "Done" : l.state == LessonState.Next ? "Up next" : "Locked";
            float statusW = HtkBrowserChrome.TextWidth(status, HtkTheme.Mono, 17);
            HtkUI.Label(content, status, HtkTheme.Mono, 17, HtkTheme.Muted, w - 24f - statusW, ty, statusW + 4f, 32f);
            if (l.state == LessonState.Done)
                HtkUI.Icon(content, "icon_check", w - 24f - statusW - 28f, ty + 6f, 20f, HtkTheme.Muted);

            HtkUI.Label(content, l.title, HtkTheme.PixelBold, 28, HtkTheme.Ink, 24f, ty + 44f, w - 48f, 36f);

            // selection outline (drawn over the normal one)
            var sel = HtkUI.AddImage(HtkUI.Stretch(HtkUI.Node("Selected", root)), HtkTheme.UI("card_outline_selected"));
            sel.color = AccentTint();
            sel.enabled = false;

            var hit = root.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            HtkUI.MakeButton(root.gameObject, () => Select(index));

            return new CardView { root = root, outline = sel, rest = root.anchoredPosition };
        }

        // card_outline_selected is drawn in yarn blue; tint relative to it for a custom accent.
        Color AccentTint() => accent == HtkTheme.Yarn ? Color.white
            : new Color(accent.r / HtkTheme.Yarn.r, accent.g / HtkTheme.Yarn.g, accent.b / HtkTheme.Yarn.b, 1f);

        public void Select(int index)
        {
            selected = index;
            for (int i = 0; i < cards.Count; i++)
            {
                bool on = i == index;
                cards[i].outline.enabled = on;
                cards[i].root.anchoredPosition = cards[i].rest + (on ? new Vector2(0f, 6f) : Vector2.zero);
            }

            var l = lessons[index];
            barTitle.text = $"Lesson {index + 1:00} · {l.title}";
            barDesc.text = l.description;

            bool isLocked = l.state == LessonState.Locked;
            ctaButton.SetActive(!isLocked);
            lockedNote.SetActive(isLocked);
            ctaLabel.text = l.state == LessonState.Done ? "Replay lesson" : "Start lesson";

            int firstOpen = lessons.FindLastIndex(x => x.state != LessonState.Locked);
            lockedText.text = $"Finish Lesson {firstOpen + 1:00} first";
        }

        void StartSelected()
        {
            onStartLesson?.Invoke(selected);
            string scene = lessons[selected].scene;
            if (!string.IsNullOrEmpty(scene)) SceneManager.LoadScene(scene);
        }
    }
}

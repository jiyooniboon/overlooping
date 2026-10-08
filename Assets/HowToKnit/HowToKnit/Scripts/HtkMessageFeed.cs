using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KnitSprite.UI.HowToKnit
{
    /// <summary>
    /// Text-message bubbles that stack up on the right of the gameplay screen.
    ///
    /// Each message is preceded by a typing bubble (three bouncing dots). A new bubble first opens
    /// a gap - pushing the older ones up - then slides in from the right. Messages stay once shown.
    /// Timings match the canvas: one every 3.2s, typing dots for the first 1.3s of each slot.
    /// </summary>
    public class HtkMessageFeed
    {
        public float slot = 3.2f;
        public float typingTime = 1.3f;
        public float growTime = 0.36f;
        public float slideTime = 0.44f;

        const float BubbleW = 440f, PadX = 24f, PadY = 18f, Gap = 16f, SlideDistance = 480f;
        const int FontSize = 21;

        readonly RectTransform area;
        readonly float rightX, bottomY;
        readonly List<string> messages;
        readonly List<Entry> entries = new List<Entry>();
        Entry typing;
        float clock = -1f;
        int shown;

        class Entry
        {
            public RectTransform root, slider;
            public CanvasGroup group;
            public float height, width, born;
            public RectTransform[] dots;
        }

        /// <param name="parent">The 1920x1080 screen rect.</param>
        /// <param name="right">Right edge of the bubbles, in screen px from the left.</param>
        /// <param name="bottom">Bottom of the newest bubble, in screen px from the top.</param>
        public HtkMessageFeed(RectTransform parent, List<string> messages, float right, float bottom)
        {
            this.messages = messages;
            rightX = right;
            bottomY = bottom;
            area = HtkUI.Stretch(HtkUI.Node("Messages", parent));
            area.gameObject.AddComponent<RectMask2D>().padding = new Vector4(0f, 0f, 0f, 0f);
        }

        public bool Running => clock >= 0f;

        public void Begin() { if (clock < 0f) clock = 0f; }

        public void Clear()
        {
            foreach (var e in entries) Object.Destroy(e.root.gameObject);
            if (typing != null) Object.Destroy(typing.root.gameObject);
            entries.Clear();
            typing = null;
            clock = -1f;
            shown = 0;
        }

        public void Tick(float dt)
        {
            if (clock < 0f) return;
            clock += dt;

            // schedule
            int k = Mathf.FloorToInt(clock / slot);
            float within = clock - k * slot;
            int due = Mathf.Min(messages.Count, k + (within >= typingTime ? 1 : 0));
            bool wantTyping = k < messages.Count && within < typingTime;

            if (wantTyping && typing == null) typing = MakeTyping();
            if (!wantTyping && typing != null) { Object.Destroy(typing.root.gameObject); typing = null; }
            while (shown < due) entries.Add(MakeBubble(messages[shown++]));

            Layout();
        }

        void Layout()
        {
            float y = bottomY;
            if (typing != null) y = Place(typing, y);
            for (int i = entries.Count - 1; i >= 0; i--) y = Place(entries[i], y);

            if (typing != null)
            {
                for (int d = 0; d < typing.dots.Length; d++)
                {
                    float p = (clock - typing.born) / 0.9f * Mathf.PI * 2f - d * 1.05f;
                    float b = (1f - Mathf.Cos(p)) * 0.5f;
                    typing.dots[d].anchoredPosition = new Vector2(typing.dots[d].anchoredPosition.x, -18f + b * 4f);
                    typing.dots[d].GetComponent<Image>().color = HtkTheme.WithAlpha(Color.white, 0.25f + 0.75f * b);
                }
            }
        }

        /// Positions an entry with its bottom at `bottom`; returns the top of the space it used.
        float Place(Entry e, float bottom)
        {
            float age = clock - e.born;
            float grow = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / growTime));
            float slideT = Mathf.Clamp01((age - growTime) / slideTime);
            float slide = 1f - Mathf.Pow(1f - slideT, 3f); // ease-out

            float used = e.height * grow;
            e.root.anchoredPosition = new Vector2(rightX - e.width, -(bottom - e.height));
            e.slider.anchoredPosition = new Vector2(SlideDistance * (1f - slide), 0f);
            e.group.alpha = grow;
            // While growing, keep the bubble hidden below its slot so the gap opens smoothly.
            e.root.gameObject.SetActive(true);
            return bottom - used - Gap * grow;
        }

        Entry MakeBubble(string text)
        {
            float textW = BubbleW - PadX * 2f;
            var label = new GameObject("measure").AddComponent<Text>();
            label.font = HtkTheme.Mono; label.fontSize = FontSize; label.lineSpacing = 1.15f;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            var gen = label.GetGenerationSettings(new Vector2(textW, 0f));
            float textH = label.cachedTextGeneratorForLayout.GetPreferredHeight(text, gen) / label.pixelsPerUnit;
            Object.Destroy(label.gameObject);

            float h = textH + PadY * 2f;
            var e = Shell(BubbleW, h);
            HtkUI.Label(e.slider, text, HtkTheme.Mono, FontSize, HtkTheme.Ink, PadX, PadY, textW, textH + 4f,
                        TextAnchor.UpperLeft, wrap: true, lineSpacing: 1.15f);
            return e;
        }

        Entry MakeTyping()
        {
            var e = Shell(98f, 46f);
            e.dots = new RectTransform[3];
            for (int i = 0; i < 3; i++)
                e.dots[i] = HtkUI.Img(e.slider, "Dot", HtkTheme.UI("typing_dot"), 24f + i * 18f, 18f, 10f, 10f).rectTransform;
            return e;
        }

        Entry Shell(float w, float h)
        {
            var root = HtkUI.Box(area, "Bubble", 0f, 0f, w, h);
            var slider = HtkUI.Stretch(HtkUI.Node("Slide", root));
            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;

            // two-layer shadow like the canvas: 0 20px 44px .38 + 0 5px 12px .22
            HtkUI.Shadow(slider, 0f, 0f, w, h, 0.38f, 20f, 44f);
            HtkUI.Shadow(slider, 0f, 0f, w, h, 0.22f, 5f, 12f);
            HtkUI.Img(slider, "Bubble", HtkTheme.UI("bubble"), 0f, 0f, w, h);

            return new Entry { root = root, slider = slider, group = group, width = w, height = h, born = clock };
        }
    }
}

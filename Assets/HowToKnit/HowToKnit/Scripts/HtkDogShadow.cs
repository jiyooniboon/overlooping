using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace KnitSprite.UI.HowToKnit
{
    /// <summary>
    /// The soft dog shadow that walks across the title screen after "Start learning".
    ///
    /// Right to left, slows to a stop, squishes horizontally (turning to look at the screen),
    /// un-squishes, walks out, then waits a beat before calling back. Timings match the canvas:
    /// 4.2s walk + 0.7s pause.
    ///
    /// Uses Art/dog_shadow_blurred (pre-blurred, so no blur shader is needed).
    /// </summary>
    public class HtkDogShadow : MonoBehaviour
    {
        [Header("Timing (seconds)")]
        public float walkDuration = 4.2f;
        public float pauseAfter = 0.7f;

        [Header("Look")]
        [Range(0f, 1f)] public float opacity = 0.22f;
        public float bobHeight = 18f;
        public float bobPeriod = 0.5f;
        [Range(0.5f, 1f)] public float squish = 0.8f;

        [Tooltip("Skip the walk (e.g. for a reduced-motion setting) and call back immediately.")]
        public bool skip;

        RectTransform walker, bobber, turner;
        Image image;
        bool playing;

        // Keyframes of the walk, as (time fraction, x of the dog's left edge in screen px).
        static readonly Vector2[] WalkKeys =
        {
            new Vector2(0.00f, 2000f),
            new Vector2(0.34f,  470f),
            new Vector2(0.66f,  330f),
            new Vector2(1.00f, -1450f),
        };

        // Squish: hold 1 -> ease to `squish` -> hold -> ease back, as time fractions.
        const float SquishIn0 = 0.36f, SquishIn1 = 0.44f, SquishOut0 = 0.56f, SquishOut1 = 0.64f;

        // Pre-blurred art has 80px padding around the 1300x778 silhouette.
        const float ArtW = 1460f, ArtH = 938f, Pad = 80f, BottomOverhang = 60f;

        /// Build the overlay inside a 1920x1080 screen rect. Called by HtkTitlePage.
        public void Build(RectTransform screen)
        {
            var root = HtkUI.Stretch(HtkUI.Node("DogShadow", screen));
            root.gameObject.AddComponent<RectMask2D>();

            float top = HtkTheme.Reference.y - (ArtH - Pad) + BottomOverhang;
            walker = HtkUI.Box(root, "Walk", 0f, top, ArtW, ArtH);

            bobber = HtkUI.Stretch(HtkUI.Node("Bob", walker));
            turner = HtkUI.Stretch(HtkUI.Node("Turn", bobber));
            turner.pivot = new Vector2(0.5f, Pad / ArtH); // squish around the bottom of the silhouette

            image = HtkUI.AddImage(turner, HtkTheme.Art("dog_shadow_blurred"),
                                   HtkTheme.WithAlpha(Color.white, opacity));
            image.type = Image.Type.Simple;

            root.gameObject.SetActive(false);
        }

        public void Play(Action onDone)
        {
            if (playing) return;
            if (skip || walker == null) { onDone?.Invoke(); return; }
            StartCoroutine(Run(onDone));
        }

        IEnumerator Run(Action onDone)
        {
            playing = true;
            walker.parent.gameObject.SetActive(true);

            float t = 0f;
            while (t < walkDuration)
            {
                t += Time.unscaledDeltaTime;
                float f = Mathf.Clamp01(t / walkDuration);

                walker.anchoredPosition = new Vector2(WalkX(f) - Pad, walker.anchoredPosition.y);

                float phase = t / bobPeriod * Mathf.PI * 2f;
                float bob = (1f - Mathf.Cos(phase)) * 0.5f;              // 0..1..0
                bobber.anchoredPosition = new Vector2(0f, bob * bobHeight);
                bobber.localEulerAngles = new Vector3(0f, 0f, bob * 1.5f); // CSS rotate(-1.5deg)

                turner.localScale = new Vector3(SquishAt(f), 1f, 1f);
                yield return null;
            }

            walker.parent.gameObject.SetActive(false);
            if (pauseAfter > 0f) yield return new WaitForSecondsRealtime(pauseAfter);
            playing = false;
            onDone?.Invoke();
        }

        static float WalkX(float f)
        {
            for (int i = 1; i < WalkKeys.Length; i++)
            {
                if (f <= WalkKeys[i].x)
                {
                    var a = WalkKeys[i - 1]; var b = WalkKeys[i];
                    float u = Mathf.InverseLerp(a.x, b.x, f);
                    return Mathf.Lerp(a.y, b.y, Mathf.SmoothStep(0f, 1f, u)); // ease-in-out per segment
                }
            }
            return WalkKeys[WalkKeys.Length - 1].y;
        }

        float SquishAt(float f)
        {
            if (f < SquishIn0 || f > SquishOut1) return 1f;
            if (f < SquishIn1) return Mathf.Lerp(1f, squish, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SquishIn0, SquishIn1, f)));
            if (f < SquishOut0) return squish;
            return Mathf.Lerp(squish, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SquishOut0, SquishOut1, f)));
        }
    }
}

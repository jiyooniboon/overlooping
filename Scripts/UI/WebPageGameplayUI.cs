using UnityEngine;
using UnityEngine.UI;
using KnitSprite.Records;

namespace KnitSprite.UI
{
    /// <summary>
    /// Presents story mode as a knitting-tutorial web page, with the hand animation playing inside
    /// an embedded "video" frame.
    ///
    /// The animation is NOT reparented into the UI. A dedicated orthographic camera films the
    /// existing sprite into a RenderTexture, and that texture is shown on a RawImage inside the
    /// page. This is how you would embed real video, and it means the Animator, the .anim clips
    /// and ProgresstoAnimation all keep working exactly as they do now - nothing about the
    /// gameplay object changes.
    ///
    /// Styling is line-art: everything is drawn as outlines via UIOutlineBox / UIOutlineCircle
    /// rather than filled blocks, so the chrome reads as drawn rather than as solid UI panels and
    /// sits alongside hand-drawn artwork. The only two solid elements are deliberate - the
    /// playhead (it is a line, not a shape) and the record dot when active - and both are one
    /// field away from being hollow too.
    ///
    /// Everything is built at runtime, so the scene needs one component and no wiring.
    ///
    /// The nicest part of the illusion: the video scrubber is not decorative. The playhead shows
    /// the current timing phase's position within its window, with the "perfect" band outlined -
    /// so the thing that looks like a progress bar is actually the mechanic.
    /// </summary>
    public class WebPageGameplayUI : MonoBehaviour
    {
        [Header("Wiring (all optional - found automatically)")]
        public ProgressManager progressManager;

        [Tooltip("The animated sprite to film. Defaults to whatever has ProgresstoAnimation.")]
        public Transform animationTarget;

        [Header("Video capture")]
        public int renderWidth = 960;
        public int renderHeight = 540;

        [Tooltip("Orthographic size for the capture camera. 0 = frame the target automatically.")]
        public float captureSize = 0f;

        [Tooltip("Extra room around the subject when auto-framing. 1 = tight.")]
        public float framePadding = 1.35f;

        [Tooltip("Leave empty to film everything. Set a layer name once the scene has objects " +
                 "that shouldn't appear in the video.")]
        public string captureLayer = "";

        [Header("Line-art style")]
        [Tooltip("Line weight for most outlines.")]
        public float lineWeight = 2f;

        [Tooltip("Heavier weight for the main frames (page border, video frame).")]
        public float heavyLineWeight = 3f;

        public Color ink = new Color(0.13f, 0.12f, 0.14f);
        public Color inkMuted = new Color(0.48f, 0.46f, 0.44f);
        public Color paper = new Color(0.99f, 0.98f, 0.96f);
        public Color accent = new Color(0.16f, 0.45f, 0.30f);
        public Color recordInk = new Color(0.72f, 0.25f, 0.22f);

        [Tooltip("Fill the record dot while a stitch is in progress. Untick for pure outlines.")]
        public bool solidRecordDot = true;

        [Header("Page content")]
        public string siteName = "knittutorials.com";
        public string pageUrl = "knittutorials.com/basics/cast-on";
        public string pageTitle = "How to Cast On";
        public string pageSubtitle = "Beginner · 4 steps · updated today";

        // --- runtime ---
        private Camera captureCamera;
        private RenderTexture renderTexture;
        private Text timeText, statusText, captionText, counterText;
        private RectTransform scrubPlayhead, scrubPerfect, scrubTrack;
        private UIOutlineCircle statusDot;
        private Text[] stepRows;
        private UIOutlineBox[] stepHighlights;
        private Font font;

        // Changing fillColor needs a manual mesh rebuild, so only do it when the state flips
        // rather than dirtying the ring's geometry every single frame.
        private bool dotStateKnown;
        private bool dotActive;

        private static readonly string[] StepLabels =
        {
            "1.  Hold A  —  ready the yarn",
            "2.  Press Y  —  move the needle in",
            "3.  Press I  —  bring the loop through",
            "4.  Release A  —  tighten the stitch",
        };

        void Awake()
        {
            if (progressManager == null) progressManager = FindFirstObjectByType<ProgressManager>();
            if (animationTarget == null)
            {
                var anim = FindFirstObjectByType<ProgresstoAnimation>();
                if (anim != null) animationTarget = anim.transform;
            }

            if (animationTarget == null)
                Debug.LogWarning("WebPageGameplayUI: no animation target found - the video frame " +
                                 "will render an empty scene.", this);

            BuildCaptureCamera();
            BuildPage();
        }

        void OnDestroy()
        {
            // RenderTextures are native resources and are not garbage collected.
            if (captureCamera != null) captureCamera.targetTexture = null;
            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
            }
        }

        void Update()
        {
            if (progressManager == null) return;

            bool active = progressManager.TryGetActivePhase(out string phaseId, out float elapsed,
                                                            out PhaseWindow window);
            UpdatePlayer(active, phaseId, elapsed, window);
            UpdateSteps(progressManager.CurrentStep);

            if (counterText != null)
                counterText.text = $"{progressManager.CompletedStitches} stitches cast on";
        }

        // ---- capture ---------------------------------------------------------------

        void BuildCaptureCamera()
        {
            renderTexture = new RenderTexture(renderWidth, renderHeight, 16)
            {
                name = "TutorialVideoRT",
                filterMode = FilterMode.Bilinear,
            };
            renderTexture.Create();

            var go = new GameObject("TutorialVideoCamera");
            go.transform.SetParent(transform, false);

            captureCamera = go.AddComponent<Camera>();
            captureCamera.orthographic = true;
            captureCamera.clearFlags = CameraClearFlags.SolidColor;
            // Paper, so the filmed sprite sits on the same stock as the page around it.
            captureCamera.backgroundColor = paper;
            captureCamera.targetTexture = renderTexture;
            captureCamera.depth = -10;   // render before the main camera

            if (!string.IsNullOrEmpty(captureLayer))
            {
                int layer = LayerMask.NameToLayer(captureLayer);
                if (layer >= 0) captureCamera.cullingMask = 1 << layer;
                else Debug.LogWarning($"WebPageGameplayUI: layer \"{captureLayer}\" doesn't exist - " +
                                      "filming all layers instead.", this);
            }

            FrameTarget();
        }

        void FrameTarget()
        {
            Vector3 centre = animationTarget != null ? animationTarget.position : Vector3.zero;
            float size = captureSize;

            if (size <= 0f && animationTarget != null)
            {
                // Auto-frame from the renderer bounds. Sprite bounds change as clips play, so this
                // is a one-time estimate with padding rather than a per-frame fit - a camera that
                // rescaled itself every frame would make the animation look like it was breathing.
                var renderer = animationTarget.GetComponentInChildren<Renderer>();
                if (renderer != null)
                {
                    centre = renderer.bounds.center;
                    // Derive aspect from the render texture rather than Camera.aspect, which
                    // isn't reliably populated before the camera's first render.
                    float aspect = renderHeight > 0 ? (float)renderWidth / renderHeight : 16f / 9f;
                    float half = Mathf.Max(renderer.bounds.extents.x / aspect,
                                           renderer.bounds.extents.y);
                    size = Mathf.Max(0.5f, half * framePadding);
                }
            }

            captureCamera.orthographicSize = size > 0f ? size : 5f;
            captureCamera.transform.position = new Vector3(centre.x, centre.y, centre.z - 10f);
        }

        // ---- page ------------------------------------------------------------------

        void BuildPage()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasGo = new GameObject("WebPageCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var canvasRect = (RectTransform)canvasGo.transform;

            // The page itself stays a solid fill - it's paper, not a shape, and it's what hides
            // whatever the main camera draws so the sprite is only seen through the video frame.
            var page = Stretch(NewRect("Page", canvasRect));
            Box(page, paper, 0f, paper);

            // Window frame: a drawn border around the whole browser.
            var frame = Stretch(NewRect("WindowFrame", page));
            frame.offsetMin = new Vector2(18f, 18f);
            frame.offsetMax = new Vector2(-18f, -18f);
            Box(frame, ink, heavyLineWeight);

            BuildBrowserChrome(frame);
            BuildArticle(frame);
        }

        void BuildBrowserChrome(RectTransform frame)
        {
            var chrome = NewRect("BrowserChrome", frame);
            chrome.anchorMin = new Vector2(0f, 1f);
            chrome.anchorMax = new Vector2(1f, 1f);
            chrome.pivot = new Vector2(0.5f, 1f);
            chrome.sizeDelta = new Vector2(0f, 104f);
            chrome.anchoredPosition = Vector2.zero;
            // Rule beneath the chrome instead of a filled toolbar.
            Box(chrome, ink, lineWeight, default, UIOutlineBox.Edges.Bottom);

            // Window dots, hollow.
            for (int i = 0; i < 3; i++)
            {
                var dot = NewRect($"Dot{i}", chrome);
                dot.anchorMin = dot.anchorMax = new Vector2(0f, 1f);
                dot.pivot = new Vector2(0.5f, 0.5f);
                dot.sizeDelta = new Vector2(17f, 17f);
                dot.anchoredPosition = new Vector2(30f + i * 27f, -28f);
                Ring(dot, ink, lineWeight);
            }

            // Tab: outlined, open at the bottom so it reads as joined to the page.
            var tab = NewRect("Tab", chrome);
            tab.anchorMin = tab.anchorMax = new Vector2(0f, 1f);
            tab.pivot = new Vector2(0f, 1f);
            tab.sizeDelta = new Vector2(360f, 42f);
            tab.anchoredPosition = new Vector2(126f, -8f);
            Box(tab, ink, lineWeight, default,
                UIOutlineBox.Edges.Top | UIOutlineBox.Edges.Left | UIOutlineBox.Edges.Right);
            Label(tab, $"{pageTitle} – {siteName}", 17, FontStyle.Normal,
                  new Vector2(16f, 0f), TextAnchor.MiddleLeft);

            // Back / forward
            Label(chrome, "<", 26, FontStyle.Bold, new Vector2(30f, -54f), TextAnchor.MiddleLeft,
                  new Vector2(30f, 36f), new Vector2(0f, 1f));
            Label(chrome, ">", 26, FontStyle.Bold, new Vector2(66f, -54f), TextAnchor.MiddleLeft,
                  new Vector2(30f, 36f), new Vector2(0f, 1f));

            // Address bar
            var address = NewRect("AddressBar", chrome);
            address.anchorMin = new Vector2(0f, 1f);
            address.anchorMax = new Vector2(1f, 1f);
            address.pivot = new Vector2(0.5f, 1f);
            address.offsetMin = new Vector2(112f, 0f);
            address.offsetMax = new Vector2(-30f, 0f);
            address.sizeDelta = new Vector2(address.sizeDelta.x, 38f);
            address.anchoredPosition = new Vector2(address.anchoredPosition.x, -54f);
            Box(address, inkMuted, lineWeight);
            Label(address, "  " + pageUrl, 17, FontStyle.Normal, new Vector2(16f, 0f),
                  TextAnchor.MiddleLeft, null, null, inkMuted);
        }

        void BuildArticle(RectTransform frame)
        {
            var article = NewRect("Article", frame);
            article.anchorMin = new Vector2(0f, 0f);
            article.anchorMax = new Vector2(1f, 1f);
            article.offsetMin = new Vector2(80f, 40f);
            article.offsetMax = new Vector2(-80f, -128f);

            Label(article, pageTitle, 46, FontStyle.Bold, new Vector2(0f, -8f),
                  TextAnchor.UpperLeft, new Vector2(900f, 56f), new Vector2(0f, 1f));
            Label(article, pageSubtitle, 18, FontStyle.Italic, new Vector2(4f, -66f),
                  TextAnchor.UpperLeft, new Vector2(900f, 26f), new Vector2(0f, 1f), inkMuted);

            // Rule under the header, like an article separator.
            var rule = NewRect("HeaderRule", article);
            rule.anchorMin = new Vector2(0f, 1f);
            rule.anchorMax = new Vector2(1f, 1f);
            rule.pivot = new Vector2(0.5f, 1f);
            rule.sizeDelta = new Vector2(0f, 2f);
            rule.anchoredPosition = new Vector2(0f, -98f);
            Box(rule, inkMuted, lineWeight, default, UIOutlineBox.Edges.Bottom);

            // ---- video player ----
            var player = NewRect("VideoPlayer", article);
            player.anchorMin = new Vector2(0f, 1f);
            player.anchorMax = new Vector2(0f, 1f);
            player.pivot = new Vector2(0f, 1f);
            player.sizeDelta = new Vector2(1060f, 640f);
            player.anchoredPosition = new Vector2(0f, -120f);
            Box(player, ink, heavyLineWeight);

            var videoScreen = NewRect("Screen", player);
            videoScreen.anchorMin = new Vector2(0f, 0f);
            videoScreen.anchorMax = new Vector2(1f, 1f);
            videoScreen.offsetMin = new Vector2(10f, 76f);
            videoScreen.offsetMax = new Vector2(-10f, -10f);

            var raw = videoScreen.gameObject.AddComponent<RawImage>();
            raw.texture = renderTexture;
            raw.raycastTarget = false;

            // Divider between the picture and the transport controls.
            var divider = NewRect("PlayerDivider", player);
            divider.anchorMin = new Vector2(0f, 0f);
            divider.anchorMax = new Vector2(1f, 0f);
            divider.pivot = new Vector2(0.5f, 0f);
            divider.sizeDelta = new Vector2(-20f, 2f);
            divider.anchoredPosition = new Vector2(0f, 76f);
            Box(divider, ink, lineWeight, default, UIOutlineBox.Edges.Bottom);

            BuildPlayerBar(player);

            // ---- caption + steps ----
            captionText = Label(article, "", 22, FontStyle.Normal, new Vector2(4f, -776f),
                                TextAnchor.UpperLeft, new Vector2(1060f, 34f), new Vector2(0f, 1f));

            counterText = Label(article, "", 18, FontStyle.Normal, new Vector2(4f, -812f),
                                TextAnchor.UpperLeft, new Vector2(1060f, 28f), new Vector2(0f, 1f),
                                inkMuted);

            BuildStepList(article);
        }

        void BuildPlayerBar(RectTransform player)
        {
            var bar = NewRect("PlayerBar", player);
            bar.anchorMin = new Vector2(0f, 0f);
            bar.anchorMax = new Vector2(1f, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.sizeDelta = new Vector2(-20f, 56f);
            bar.anchoredPosition = new Vector2(0f, 10f);

            // Hollow ring; it only fills while a stitch is being worked (and only if you want it).
            var dotRect = Sized(NewRect("StatusDot", bar), new Vector2(15f, 15f),
                                new Vector2(0f, 0.5f), new Vector2(16f, 0f));
            statusDot = Ring(dotRect, inkMuted, lineWeight);

            statusText = Label(bar, "PAUSED", 15, FontStyle.Bold, new Vector2(40f, 0f),
                               TextAnchor.MiddleLeft, new Vector2(120f, 26f), new Vector2(0f, 0.5f),
                               inkMuted);

            timeText = Label(bar, "0.00 / 0.00", 15, FontStyle.Normal, new Vector2(-16f, 0f),
                             TextAnchor.MiddleRight, new Vector2(150f, 26f), new Vector2(1f, 0.5f),
                             inkMuted);

            scrubTrack = NewRect("ScrubTrack", bar);
            scrubTrack.anchorMin = new Vector2(0f, 0.5f);
            scrubTrack.anchorMax = new Vector2(1f, 0.5f);
            scrubTrack.pivot = new Vector2(0.5f, 0.5f);
            scrubTrack.offsetMin = new Vector2(166f, 0f);
            scrubTrack.offsetMax = new Vector2(-176f, 0f);
            scrubTrack.sizeDelta = new Vector2(scrubTrack.sizeDelta.x, 16f);
            Box(scrubTrack, inkMuted, 1.5f);

            // Target band: outlined, so it marks the region without becoming a solid bar.
            scrubPerfect = BarPart("PerfectBand", scrubTrack);
            Box(scrubPerfect, accent, lineWeight);

            // Playhead is deliberately solid - it is a line, and an outlined 3px sliver would
            // just read as noise.
            scrubPlayhead = BarPart("Playhead", scrubTrack);
            Box(scrubPlayhead, ink, 0f, ink);
            scrubPlayhead.sizeDelta = new Vector2(3f, 0f);
        }

        void BuildStepList(RectTransform article)
        {
            var list = NewRect("Steps", article);
            list.anchorMin = new Vector2(1f, 1f);
            list.anchorMax = new Vector2(1f, 1f);
            list.pivot = new Vector2(1f, 1f);
            list.sizeDelta = new Vector2(620f, 400f);
            list.anchoredPosition = new Vector2(0f, -120f);

            Label(list, "Steps", 26, FontStyle.Bold, new Vector2(18f, -10f), TextAnchor.UpperLeft,
                  new Vector2(400f, 34f), new Vector2(0f, 1f));

            stepRows = new Text[StepLabels.Length];
            stepHighlights = new UIOutlineBox[StepLabels.Length];

            for (int i = 0; i < StepLabels.Length; i++)
            {
                var row = NewRect($"Step{i}", list);
                row.anchorMin = new Vector2(0f, 1f);
                row.anchorMax = new Vector2(1f, 1f);
                row.pivot = new Vector2(0.5f, 1f);
                row.sizeDelta = new Vector2(0f, 52f);
                row.anchoredPosition = new Vector2(0f, -56f - i * 58f);

                // Drawn box around the active step rather than a highlight fill.
                stepHighlights[i] = Box(row, ink, lineWeight);
                stepHighlights[i].enabled = false;

                stepRows[i] = Label(row, StepLabels[i], 21, FontStyle.Normal, new Vector2(18f, 0f),
                                    TextAnchor.MiddleLeft);
            }
        }

        // ---- live updates ----------------------------------------------------------

        void UpdatePlayer(bool active, string phaseId, float elapsed, PhaseWindow window)
        {
            bool timed = active && window != null && window.max > 0f;

            if (statusDot != null && (!dotStateKnown || dotActive != active))
            {
                statusDot.color = active ? recordInk : inkMuted;
                statusDot.fillColor = (active && solidRecordDot) ? recordInk : Color.clear;
                statusDot.SetVerticesDirty();

                dotActive = active;
                dotStateKnown = true;
            }

            if (statusText != null)
            {
                statusText.text = active ? "RECORDING" : "PAUSED";
                statusText.color = active ? recordInk : inkMuted;
            }

            if (timeText != null)
                timeText.text = timed
                    ? $"{elapsed:0.00} / {window.max:0.00}s"
                    : (active ? $"{elapsed:0.00}s" : "0.00 / 0.00");

            if (scrubTrack == null) return;

            float width = scrubTrack.rect.width;

            scrubPlayhead.gameObject.SetActive(timed);
            scrubPerfect.gameObject.SetActive(timed);
            if (!timed) return;

            scrubPlayhead.anchoredPosition =
                new Vector2(width * Mathf.Clamp01(elapsed / window.max), 0f);

            float from = Mathf.Clamp01((window.perfect - window.margin) / window.max);
            float to   = Mathf.Clamp01((window.perfect + window.margin) / window.max);
            scrubPerfect.anchoredPosition = new Vector2(width * from, 0f);
            scrubPerfect.sizeDelta = new Vector2(width * (to - from), 0f);
        }

        void UpdateSteps(ProgressManager.StitchStep step)
        {
            if (stepRows == null) return;

            // Highlight the action the player should take NEXT, which is what a tutorial reader
            // wants to see - not the state they are already in.
            int activeRow;
            switch (step)
            {
                case ProgressManager.StitchStep.Idle:         activeRow = 0; break;
                case ProgressManager.StitchStep.ReadyingYarn: activeRow = 1; break;
                case ProgressManager.StitchStep.MovedToA:     activeRow = 2; break;
                default:                                      activeRow = 3; break;
            }

            for (int i = 0; i < stepRows.Length; i++)
            {
                bool on = i == activeRow;
                stepHighlights[i].enabled = on;
                stepRows[i].fontStyle = on ? FontStyle.Bold : FontStyle.Normal;
                stepRows[i].color = on ? ink : inkMuted;
            }

            if (captionText != null) captionText.text = StepLabels[activeRow];
        }

        // ---- tiny uGUI helpers -----------------------------------------------------

        static RectTransform NewRect(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static RectTransform Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        static RectTransform Sized(RectTransform rt, Vector2 size, Vector2 anchor, Vector2 pos)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            return rt;
        }

        static UIOutlineBox Box(RectTransform rt, Color lineColor, float weight,
                                Color fill = default,
                                UIOutlineBox.Edges edges = UIOutlineBox.Edges.All)
        {
            var box = rt.gameObject.AddComponent<UIOutlineBox>();
            box.color = lineColor;
            box.thickness = weight;
            box.fillColor = fill;
            box.edges = edges;
            box.raycastTarget = false;   // nothing here is interactive
            return box;
        }

        static UIOutlineCircle Ring(RectTransform rt, Color lineColor, float weight)
        {
            var ring = rt.gameObject.AddComponent<UIOutlineCircle>();
            ring.color = lineColor;
            ring.thickness = weight;
            ring.fillColor = Color.clear;
            ring.raycastTarget = false;
            return ring;
        }

        static RectTransform BarPart(string name, RectTransform track)
        {
            var rt = NewRect(name, track);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        Text Label(RectTransform parent, string content, int size, FontStyle style, Vector2 pos,
                   TextAnchor anchor, Vector2? explicitSize = null, Vector2? pivotAnchor = null,
                   Color? color = null)
        {
            var rt = NewRect("Label", parent);

            if (explicitSize.HasValue)
            {
                Vector2 a = pivotAnchor ?? new Vector2(0f, 1f);
                rt.anchorMin = rt.anchorMax = a;
                rt.pivot = a;
                rt.sizeDelta = explicitSize.Value;
                rt.anchoredPosition = pos;
            }
            else
            {
                Stretch(rt);
                rt.offsetMin = new Vector2(pos.x, 0f);
                rt.offsetMax = new Vector2(-8f, 0f);
            }

            var t = rt.gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.fontStyle = style;
            t.text = content;
            t.alignment = anchor;
            t.color = color ?? ink;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }
    }
}

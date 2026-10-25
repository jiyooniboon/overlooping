using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using KnitSprite.Records;
using KnitSprite.Knitting;

namespace KnitSprite.UI.HowToKnit
{
    /// <summary>
    /// Screen 3 - the cast-on lesson in the "How to Knit" web-page style.
    ///
    /// Same approach as WebPageGameplayUI: the existing animated sprite is filmed by its own
    /// camera into a RenderTexture and shown inside the page, so ProgressManager
    /// and ProgresstoAnimation keep working untouched. Use this INSTEAD of WebPageGameplayUI
    /// (disable that one), not alongside it.
    ///
    /// Reads ProgressManager every frame (read-only):
    ///   Idle         -> stage 1 "Ready yarn"             Hold A
    ///   ReadyingYarn -> stage 2 "Needle under"           Press Y
    ///   MovedToA     -> stage 3 "Needle over"            Press I   (bar = Y->I timing window)
    ///   MovedToB     -> stage 4 "Pull through & tighten" Let go of A shown early ("Get ready")
    ///   Tightening   -> stage 4                          Let go A  (bar = tighten window)
    /// </summary>
    public class HtkGameplayPage : MonoBehaviour
    {
        [Header("Wiring (optional - found automatically)")]
        public ProgressManager progressManager;
        [Tooltip("The animated sprite to film. Defaults to whatever has ProgresstoAnimation.")]
        public Transform animationTarget;

        [Header("Video capture")]
        public int renderWidth = 1280;
        public int renderHeight = 720;
        [Tooltip("Orthographic size for the capture camera. 0 = frame the target automatically.")]
        public float captureSize = 0f;
        public float framePadding = 1.2f;
        [Tooltip("Leave empty to film everything.")]
        public string captureLayer = "";

        [Header("Lesson")]
        public string lessonTitle = "cast on";
        public string lessonNumber = "02";
        public string urlPath = "/tutorials/cast-on";
        public int stitchesToComplete = 25;

        [Header("Messages")]
        [Tooltip("Show the chat-bubble messages. Off for now - trailer only.")]
        public bool showMessages = false;
        [Tooltip("Messages start once this many stitches are done (3 = while making the 4th).")]
        public int messagesAfterStitches = 3;
        [TextArea]
        public List<string> messages = new List<string>
        {
            "I hope you found this website!",
            "I wanted to make you something to keep you company, even when I'm not there.",
            "Although... maybe you just wanted a treat?",
        };

        [Header("Navigation")]
        [Tooltip("Scenes for the tabs and the About dialog.")]
        public HtkScenes scenes = new HtkScenes();
        public string nextLessonScene = "";
        public UnityEvent onLessonComplete;

        [Header("Look")]
        public Color accent = HtkTheme.Yarn;
        public int sortingOrder = 100;

        // ---- runtime ----
        Camera captureCamera;
        RenderTexture renderTexture;
        RectTransform cardRoot;
        HtkKeyPrompt prompt;
        HtkMessageFeed feed;
        Text stepOf, stageName, verbText, keyText, statusText;
        RectTransform keySmall;
        Image progressFill;
        readonly List<RowView> rows = new List<RowView>();
        readonly List<Image> ticks = new List<Image>();
        GameObject dialog, aboutDialog;
        int baseline, lastCount = -1;

        // Pattern mode: set when creative mode hands over a chart (KnitSession). Null = story lesson.
        KnitPattern pattern;
        HtkPatternPanel patternPanel;
        Text dialogBody;
        ProgressManager.StitchStep lastStep = (ProgressManager.StitchStep)(-1);

        class RowView { public Image highlight, box; public Text number, name; public Image check; }

        struct Stage
        {
            public string name, key; public string verb;
            public Stage(string n, string k, string v) { name = n; key = k; verb = v; }
        }

        static readonly Stage[] Stages =
        {
            new Stage("Ready yarn", "A", "Hold"),
            new Stage("Needle under", "Y", "Press"),
            new Stage("Needle over", "I", "Press"),
            new Stage("Pull through & tighten", "A", "Let go"),
        };

        // Where the keycap sits on the animation, as fractions of the card (from the canvas).
        static readonly Vector2[] CuePositions =
        {
            new Vector2(0.22f, 0.30f), new Vector2(0.84f, 0.28f), new Vector2(0.84f, 0.28f), new Vector2(0.85f, 0.36f),
        };

        // ---- setup ----------------------------------------------------------------------------

        void Awake()
        {
            if (progressManager == null) progressManager = FindFirstObjectByType<ProgressManager>();
            if (animationTarget == null)
            {
                var anim = FindFirstObjectByType<ProgresstoAnimation>();
                if (anim != null) animationTarget = anim.transform;
            }
            if (progressManager == null)
                Debug.LogWarning("HtkGameplayPage: no ProgressManager in the scene - the page will sit idle.", this);

            pattern = KnitSession.Consume();
            if (pattern != null)
            {
                stitchesToComplete = pattern.Count;
                lessonTitle = "your pattern";
                urlPath = "/create/knit";
                showMessages = false;
            }

            HtkUI.EnsureEventSystem();
            BuildCaptureCamera();
            BuildPage();
            baseline = progressManager != null ? progressManager.CompletedStitches : 0;
            if (pattern != null) ShowNextStitch("");
        }

        void OnEnable()
        {
            if (progressManager != null) progressManager.OnStitchComplete += HandleStitchComplete;
        }

        void OnDisable()
        {
            if (progressManager != null) progressManager.OnStitchComplete -= HandleStitchComplete;
        }

        void OnDestroy()
        {
            patternPanel?.Destroy();
            if (captureCamera != null) captureCamera.targetTexture = null;
            if (renderTexture != null) { renderTexture.Release(); Destroy(renderTexture); }
        }

        void BuildCaptureCamera()
        {
            renderTexture = new RenderTexture(renderWidth, renderHeight, 16) { name = "HowToKnitVideoRT" };
            renderTexture.Create();

            var go = new GameObject("HowToKnitVideoCamera");
            go.transform.SetParent(transform, false);
            captureCamera = go.AddComponent<Camera>();
            captureCamera.orthographic = true;
            captureCamera.clearFlags = CameraClearFlags.SolidColor;
            captureCamera.backgroundColor = Color.white;
            captureCamera.targetTexture = renderTexture;
            captureCamera.depth = -10;

            if (!string.IsNullOrEmpty(captureLayer))
            {
                int layer = LayerMask.NameToLayer(captureLayer);
                if (layer >= 0) captureCamera.cullingMask = 1 << layer;
            }

            Vector3 centre = animationTarget != null ? animationTarget.position : Vector3.zero;
            float size = captureSize;
            if (size <= 0f && animationTarget != null)
            {
                var r = animationTarget.GetComponentInChildren<Renderer>();
                if (r != null)
                {
                    centre = r.bounds.center;
                    float aspect = (float)renderWidth / renderHeight;
                    size = Mathf.Max(r.bounds.extents.x / aspect, r.bounds.extents.y) * framePadding;
                }
            }
            captureCamera.orthographicSize = size > 0f ? size : 5f;
            captureCamera.transform.position = new Vector3(centre.x, centre.y, centre.z - 10f);
        }

        void BuildPage()
        {
            var screen = HtkUI.Canvas(transform, "GameplayCanvas", sortingOrder);
            HtkBrowserChrome.Build(screen, urlPath,
                                   pattern != null ? HtkBrowserChrome.Tab.Create : HtkBrowserChrome.Tab.Tutorials, scenes,
                                   () => { if (aboutDialog != null) aboutDialog.SetActive(true); });

            const float left = 60f;
            float top = HtkBrowserChrome.ContentTop + 24f;  // 188

            // ---- heading ----
            HtkUI.Label(screen, pattern != null ? "Create / Knit your chart" : $"Tutorials / Lesson {lessonNumber}", HtkTheme.Mono, 18, HtkTheme.Muted, left, top, 600f, 24f);
            HtkUI.Label(screen, lessonTitle, HtkTheme.PixelBold, 46, HtkTheme.Ink, left, top + 30f, 1000f, 52f);

            // ---- animation card (1280x720) ----
            const float cardY = 282f, cardW = 1280f, cardH = 720f;
            HtkUI.Shadow(screen, left, cardY, cardW, cardH, 0.13f, 24f, 52f);
            var content = HtkUI.MaskedCard(screen, "AnimationCard", left, cardY, cardW, cardH, Color.white);
            cardRoot = (RectTransform)content.parent.parent;
            var raw = HtkUI.Stretch(HtkUI.Node("Video", content)).gameObject.AddComponent<RawImage>();
            raw.texture = renderTexture;
            raw.raycastTarget = false;
            prompt = new HtkKeyPrompt(cardRoot);

            statusText = HtkUI.Label(screen, "", HtkTheme.Mono, 20, HtkTheme.Ink, left, cardY + cardH + 14f, cardW, 28f);

            // ---- right column ----
            const float colX = 1380f, colW = 480f;

            var panel = HtkUI.Img(screen, "StepPanel", HtkTheme.UI("card"), colX, cardY, colW, 206f).rectTransform;
            stepOf = HtkUI.Label(panel, "", HtkTheme.Pixel, 16, HtkTheme.Muted, 26f, 22f, 400f, 22f);
            stageName = HtkUI.Label(panel, "", HtkTheme.PixelBold, 34, HtkTheme.Ink, 26f, 50f, 430f, 40f);
            verbText = HtkUI.Label(panel, "", HtkTheme.Mono, 22, HtkTheme.Ink, 26f, 104f, 120f, 52f);
            keySmall = HtkUI.Img(panel, "Key", HtkTheme.UI("keycap_small"), 110f, 104f, 52f, 52f).rectTransform;
            keyText = HtkUI.Label(keySmall, "", HtkTheme.PixelBold, 24, HtkTheme.Ink, 0f, 0f, 52f, 47f, TextAnchor.MiddleCenter);
            HtkUI.Img(panel, "Track", HtkTheme.UI("progress_track"), 26f, 172f, colW - 52f, 12f);
            progressFill = HtkUI.Img(panel, "Fill", HtkTheme.UI("progress_fill"), 28f, 174f, colW - 56f, 8f, AccentTint());
            progressFill.type = Image.Type.Filled;
            progressFill.fillMethod = Image.FillMethod.Horizontal;
            progressFill.fillAmount = 0f;

            var list = HtkUI.Img(screen, "StageList", HtkTheme.UI("card"), colX, cardY + 226f, colW, 248f).rectTransform;
            for (int i = 0; i < Stages.Length; i++)
            {
                var row = HtkUI.Box(list, "Row" + i, 22f, 18f + i * 54f, colW - 44f, 50f);
                var v = new RowView();
                v.highlight = HtkUI.AddImage(HtkUI.Stretch(HtkUI.Node("Highlight", row)), HtkTheme.UI("row_highlight"));
                v.box = HtkUI.Img(row, "Box", HtkTheme.UI("step_box"), 10f, 8f, 34f, 34f);
                v.number = HtkUI.Label(v.box.transform, (i + 1).ToString(), HtkTheme.Pixel, 16, HtkTheme.Ink, 0, 0, 34f, 34f, TextAnchor.MiddleCenter);
                v.check = HtkUI.Icon(v.box.transform, "icon_check", 8f, 8f, 18f, HtkTheme.Card);
                v.name = HtkUI.Label(row, Stages[i].name, HtkTheme.Mono, 19, HtkTheme.Ink, 58f, 0f, 280f, 50f);
                HtkUI.Label(row, $"{Stages[i].verb} {Stages[i].key}", HtkTheme.Mono, 15, HtkTheme.Muted, 0f, 0f, colW - 44f - 10f, 50f,
                            TextAnchor.MiddleRight);
                rows.Add(v);
            }

            // pattern mode: a live mini chart instead of the 25-tick counter
            if (pattern != null)
                patternPanel = new HtkPatternPanel(screen, pattern, colX, cardY + 496f, colW, cardH - 496f);
            else
            {
            // 25-stitch counter
            float tickW = (colW - 3f * (stitchesToComplete - 1)) / stitchesToComplete;
            for (int i = 0; i < stitchesToComplete; i++)
                ticks.Add(HtkUI.Img(screen, "Tick", HtkTheme.UI("tick_empty"), colX + i * (tickW + 3f), cardY + 496f, tickW, 22f));
            }

            // ---- messages (over the right column) ----
            feed = new HtkMessageFeed(screen, messages, 1920f - 59f - 30f, 259f + 790f - 50f);

            BuildDialog(screen);
            aboutDialog = HtkAboutDialog.Build(screen, scenes);   // built last so it draws on top
        }

        void BuildDialog(RectTransform screen)
        {
            var root = HtkUI.Stretch(HtkUI.Node("CompleteDialog", screen));
            HtkUI.AddImage(root, HtkTheme.UI("pixel"), HtkTheme.WithAlpha(HtkTheme.Paper, 0.86f)).raycastTarget = true;

            const float w = 640f, h = 340f;
            float x = (1920f - w) / 2f, y = (1080f - h) / 2f;
            HtkUI.Shadow(root, x, y, w, h, 0.2f, 30f, 64f);
            var card = HtkUI.Img(root, "Dialog", HtkTheme.UI("card"), x, y, w, h).rectTransform;

            if (pattern != null) { BuildPatternDialog(root, card, w); return; }

            var chip = HtkUI.Img(card, "Chip", HtkTheme.UI("pill"), 48f, 48f, 250f, 34f);
            HtkUI.Label(chip.transform, $"Lesson {lessonNumber} complete", HtkTheme.Pixel, 18, HtkTheme.Ink, 0, 0, 250f, 34f, TextAnchor.MiddleCenter);

            HtkUI.Label(card, "Cast ", HtkTheme.PixelBold, 56, HtkTheme.Ink, 48f, 100f, 300f, 60f);
            float castW = HtkBrowserChrome.TextWidth("Cast ", HtkTheme.PixelBold, 56);
            HtkUI.Label(card, "on!", HtkTheme.PixelBold, 56, accent, 48f + castW, 100f, 200f, 60f);

            HtkUI.Label(card, $"{stitchesToComplete} stitches cast on — a full row, ready to knit. Next up: the knit stitch.",
                        HtkTheme.Mono, 21, HtkTheme.Ink, 48f, 176f, w - 96f, 64f, TextAnchor.UpperLeft, wrap: true, lineSpacing: 1.2f);

            DialogButton(card, "Next lesson", true, 48f, 250f, 250f, () => { onLessonComplete?.Invoke(); LoadScene(nextLessonScene); });
            DialogButton(card, "Cast on again", false, 48f + 264f, 250f, 280f, Restart);

            dialog = root.gameObject;
            dialog.SetActive(false);
        }

        void BuildPatternDialog(RectTransform root, RectTransform card, float w)
        {
            var chip = HtkUI.Img(card, "Chip", HtkTheme.UI("pill"), 48f, 48f, 250f, 34f);
            HtkUI.Label(chip.transform, "Pattern complete", HtkTheme.Pixel, 18, HtkTheme.Ink, 0, 0, 250f, 34f, TextAnchor.MiddleCenter);

            HtkUI.Label(card, "All ", HtkTheme.PixelBold, 56, HtkTheme.Ink, 48f, 100f, 300f, 60f);
            float allW = HtkBrowserChrome.TextWidth("All ", HtkTheme.PixelBold, 56);
            HtkUI.Label(card, "done!", HtkTheme.PixelBold, 56, accent, 48f + allW, 100f, 300f, 60f);

            dialogBody = HtkUI.Label(card, "", HtkTheme.Mono, 21, HtkTheme.Ink, 48f, 176f, w - 96f, 64f,
                                     TextAnchor.UpperLeft, wrap: true, lineSpacing: 1.2f);

            DialogButton(card, "Back to chart", true, 48f, 250f, 260f, () => HtkScenes.Load(scenes.creative));
            DialogButton(card, "Knit again", false, 48f + 274f, 250f, 240f, Restart);

            dialog = root.gameObject;
            dialog.SetActive(false);
        }

        /// Status line in pattern mode: what was just made, then what's next and in which yarn.
        void ShowNextStitch(string justMade)
        {
            int n = progressManager != null ? progressManager.CompletedStitches - baseline : 0;
            string next = "";
            if (n < pattern.Count)
            {
                var s = pattern[n];
                next = $"Next: {pattern.Describe(n)} · {pattern.YarnName(s.yarnIndex)} yarn";
                if (s.type != StitchType.CastOn) next += " (cast-on motion for now)";
            }
            statusText.text = string.IsNullOrEmpty(justMade) ? next : $"{justMade}   {next}";
        }

        void DialogButton(RectTransform parent, string label, bool primary, float x, float y, float w, System.Action onClick)
        {
            var b = HtkUI.Img(parent, label, HtkTheme.UI(primary ? "button_dark" : "button_light"), x, y, w, 68f);
            HtkUI.Label(b.transform, label, HtkTheme.Pixel, 21, primary ? HtkTheme.Card : HtkTheme.Ink, 0, 0, w, 68f, TextAnchor.MiddleCenter);
            HtkUI.MakeButton(b.gameObject, onClick);
        }

        // ---- per frame --------------------------------------------------------------------------

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            prompt.Tick(dt);
            feed.Tick(dt);
            patternPanel?.Tick(dt);
            if (progressManager == null) return;

            int count = progressManager.CompletedStitches - baseline;
            if (count != lastCount) { lastCount = count; RefreshTicks(count); }
            if (showMessages && count >= messagesAfterStitches && !feed.Running && count < stitchesToComplete) feed.Begin();

            if (count >= stitchesToComplete && !dialog.activeSelf)
            {
                if (pattern != null && dialogBody != null)
                {
                    int loose = 0;
                    foreach (int r in pattern.results) if (r == (int)ProgressManager.StitchQuality.Miss) loose++;
                    dialogBody.text = $"{pattern.Count} stitches - cast on plus {pattern.chartRows} rows of your chart. " +
                                      (loose == 0 ? "Every stitch came out even." : $"{loose} came out a little loose.");
                }
                dialog.SetActive(true);
                progressManager.enabled = false;   // stop input while the dialog is up
                feed.Clear();
                return;
            }

            var step = progressManager.CurrentStep;
            int stage = StageFor(step);
            if (step != lastStep) { lastStep = step; ApplyStep(step, stage); }

            // progress bar = the timing window currently being judged
            float fill = 0f;
            if (progressManager.TryGetActivePhase(out _, out float elapsed, out PhaseWindow window) &&
                window != null && window.max > 0f)
                fill = Mathf.Clamp01(elapsed / window.max);
            progressFill.fillAmount = fill;
        }

        static int StageFor(ProgressManager.StitchStep s)
        {
            switch (s)
            {
                case ProgressManager.StitchStep.Idle:         return 0;
                case ProgressManager.StitchStep.ReadyingYarn: return 1;
                case ProgressManager.StitchStep.MovedToA:     return 2;
                default:                                      return 3;
            }
        }

        void ApplyStep(ProgressManager.StitchStep step, int stage)
        {
            var s = Stages[stage];
            stepOf.text = $"Step {stage + 1} of {Stages.Length}";
            stageName.text = s.name;
            verbText.text = s.verb;
            keyText.text = s.key;
            keySmall.anchoredPosition = new Vector2(26f + HtkBrowserChrome.TextWidth(s.verb, HtkTheme.Mono, 22) + 14f, -104f);

            for (int i = 0; i < rows.Count; i++)
            {
                bool done = i < stage, cur = i == stage;
                var r = rows[i];
                r.highlight.enabled = cur;
                r.box.sprite = HtkTheme.UI(done ? "step_box_done" : "step_box");
                r.number.enabled = !done;
                r.check.enabled = done;
                r.name.color = done ? HtkTheme.Muted : HtkTheme.Ink;
            }

            HtkKeyPrompt.Mode mode;
            switch (step)
            {
                case ProgressManager.StitchStep.Idle:      mode = HtkKeyPrompt.Mode.Hold; break;
                case ProgressManager.StitchStep.MovedToB:  mode = HtkKeyPrompt.Mode.GetReady; break;
                case ProgressManager.StitchStep.Tightening: mode = HtkKeyPrompt.Mode.Release; break;
                default:                                   mode = HtkKeyPrompt.Mode.Press; break;
            }
            prompt.Set(mode, s.key[0], CuePositions[stage]);
        }

        void RefreshTicks(int count)
        {
            if (patternPanel != null) { patternPanel.SetProgress(count); return; }
            for (int i = 0; i < ticks.Count; i++)
            {
                bool on = i < count;
                ticks[i].sprite = HtkTheme.UI(on ? "tick_filled" : "tick_empty");
                ticks[i].color = on ? AccentTint() : Color.white;
            }
        }

        void HandleStitchComplete(ProgressManager.StitchQuality quality)
        {
            int n = progressManager.CompletedStitches - baseline;
            if (pattern != null)
            {
                if (n >= 1 && n <= pattern.Count) pattern.results[n - 1] = (int)quality;
                bool missed = quality == ProgressManager.StitchQuality.Miss;
                ShowNextStitch(missed ? "Loose stitch." : $"{quality}!");
                statusText.color = missed ? HtkTheme.Error : HtkTheme.Ink;
                return;
            }
            statusText.text = quality == ProgressManager.StitchQuality.Miss
                ? $"Stitch {n} of {stitchesToComplete} · a little loose - try the timing again"
                : $"Stitch {n} of {stitchesToComplete} · {quality}!";
            statusText.color = quality == ProgressManager.StitchQuality.Miss ? HtkTheme.Error : HtkTheme.Ink;
        }

        void Restart()
        {
            baseline = progressManager != null ? progressManager.CompletedStitches : 0;
            lastCount = -1;
            dialog.SetActive(false);
            statusText.text = "";
            feed.Clear();
            if (pattern != null) { pattern.ResetResults(); ShowNextStitch(""); statusText.color = HtkTheme.Ink; }
            if (progressManager != null) progressManager.enabled = true;
            var art = animationTarget != null ? animationTarget.GetComponent<ProgresstoAnimation>() : null;
            if (art != null) art.ResetStitchArt(); // back to the empty needle
        }

        // tick_filled / progress_fill are drawn in yarn blue; tint relative to it for a custom accent
        Color AccentTint() => accent == HtkTheme.Yarn ? Color.white
            : new Color(accent.r / HtkTheme.Yarn.r, accent.g / HtkTheme.Yarn.g, accent.b / HtkTheme.Yarn.b, 1f);

        static void LoadScene(string scene)
        {
            if (!string.IsNullOrEmpty(scene)) SceneManager.LoadScene(scene);
        }
    }
}

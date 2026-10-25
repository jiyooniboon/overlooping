using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using KnitSprite.Records;

namespace KnitSprite.UI.HowToKnit
{
    /// <summary>
    /// Practice mode: the cast-on animation with live timing feedback, built on PRACTICE_MODE_PLAN.
    ///
    /// Like HtkGameplayPage it films your existing animated sprite and only READS ProgressManager -
    /// except for one thing: the Wide / Normal / Tight buttons scale ProgressManager's two "perfect"
    /// margins (perfectYIMargin, perfectTightenMargin) at runtime. The values in the Inspector /
    /// scene are restored when this page is destroyed.
    ///
    /// Raw timings come from ProgressManager.OnStitchOutcome (yToI and tighten phases), so dropped
    /// and timed-out stitches are counted too.
    /// </summary>
    public class HtkPracticePage : MonoBehaviour
    {
        [Header("Wiring (optional - found automatically)")]
        public ProgressManager progressManager;
        public Transform animationTarget;

        [Header("Video capture")]
        public int renderWidth = 960;
        public int renderHeight = 540;
        public float captureSize = 0f;
        public float framePadding = 1.2f;
        public string captureLayer = "";

        [Header("Navigation")]
        public HtkScenes scenes = new HtkScenes();

        [Header("Look")]
        public Color accent = HtkTheme.Yarn;
        public int sortingOrder = 100;

        // ---- data ----
        class Attempt
        {
            public float yi = -1f, ti = -1f;   // seconds, -1 = not reached
            public int qy, qt, q;              // 0 Miss, 1 Good, 2 Perfect
            public OutcomeKind kind;
        }

        readonly List<Attempt> attempts = new List<Attempt>();
        static readonly float[] WindowScale = { 1.6f, 1f, 0.6f };
        static readonly string[] WindowNames = { "Wide", "Normal", "Tight" };
        int windowIndex = 1;
        /// 0 Wide, 1 Normal, 2 Tight.
        public int TimingWindow => windowIndex;
        float baseYIMargin, baseTightenMargin;
        bool marginsCaptured;

        // ---- runtime UI ----
        Camera captureCamera;
        RenderTexture renderTexture;
        RectTransform cardRoot;
        HtkKeyPrompt prompt;
        GameObject aboutDialog, flash;
        Image flashBg;
        Text flashText, tipText;
        float flashTimer;
        Meter yiMeter, tiMeter;
        readonly Image[] windowPills = new Image[3];
        readonly Text[] statValue = new Text[4], statSub = new Text[4];
        readonly List<Text[]> historyRows = new List<Text[]>();
        readonly List<Image> historyChips = new List<Image>();
        Text emptyHistory;
        ProgressManager.StitchStep lastStep = (ProgressManager.StitchStep)(-1);

        class Meter
        {
            public RectTransform track, band, bandEdges, minLine, maxLine, live;
            public Image[] dots;
            public Text last, scale;
            public float scaleMax;
        }

        static readonly string[] CueKeys = { "A", "Y", "I", "A" };
        static readonly Vector2[] CuePositions =
        {
            new Vector2(0.22f, 0.30f), new Vector2(0.84f, 0.28f), new Vector2(0.84f, 0.28f), new Vector2(0.85f, 0.36f),
        };

        // ---- setup ------------------------------------------------------------------------------

        void Awake()
        {
            if (progressManager == null) progressManager = FindFirstObjectByType<ProgressManager>();
            if (animationTarget == null)
            {
                var anim = FindFirstObjectByType<ProgresstoAnimation>();
                if (anim != null) animationTarget = anim.transform;
            }
            if (progressManager != null)
            {
                baseYIMargin = progressManager.perfectYIMargin;
                baseTightenMargin = progressManager.perfectTightenMargin;
                marginsCaptured = true;
            }
            else Debug.LogWarning("HtkPracticePage: no ProgressManager in the scene - nothing to practise.", this);

            HtkUI.EnsureEventSystem();
            BuildCaptureCamera();
            BuildPage();
            SetWindow(1);
            RefreshStats();
        }

        void OnEnable()  { if (progressManager != null) progressManager.OnStitchOutcome += HandleOutcome; }
        void OnDisable() { if (progressManager != null) progressManager.OnStitchOutcome -= HandleOutcome; }

        void OnDestroy()
        {
            if (marginsCaptured && progressManager != null)
            {
                progressManager.perfectYIMargin = baseYIMargin;
                progressManager.perfectTightenMargin = baseTightenMargin;
            }
            if (captureCamera != null) captureCamera.targetTexture = null;
            if (renderTexture != null) { renderTexture.Release(); Destroy(renderTexture); }
        }

        void BuildCaptureCamera()
        {
            renderTexture = new RenderTexture(renderWidth, renderHeight, 16) { name = "HowToKnitPracticeRT" };
            renderTexture.Create();
            var go = new GameObject("HowToKnitPracticeCamera");
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
            var screen = HtkUI.Canvas(transform, "PracticeCanvas", sortingOrder);
            HtkBrowserChrome.Build(screen, "/practice", HtkBrowserChrome.Tab.Practice, scenes,
                                   () => { if (aboutDialog != null) aboutDialog.SetActive(true); });

            const float left = 60f, top = HtkBrowserChrome.ContentTop + 24f;
            HtkUI.Label(screen, "Practice / Cast on timing", HtkTheme.Mono, 18, HtkTheme.Muted, left, top, 600f, 24f);
            HtkUI.Label(screen, "practice", HtkTheme.PixelBold, 46, HtkTheme.Ink, left, top + 30f, 1000f, 52f);

            // ---- animation ----
            const float cardY = 282f, cardW = 960f, cardH = 540f;
            HtkUI.Shadow(screen, left, cardY, cardW, cardH, 0.13f, 24f, 52f);
            var content = HtkUI.MaskedCard(screen, "AnimationCard", left, cardY, cardW, cardH, Color.white);
            cardRoot = (RectTransform)content.parent.parent;
            var raw = HtkUI.Stretch(HtkUI.Node("Video", content)).gameObject.AddComponent<RawImage>();
            raw.texture = renderTexture;
            raw.raycastTarget = false;
            prompt = new HtkKeyPrompt(cardRoot);

            flashBg = HtkUI.Img(cardRoot, "Flash", HtkTheme.UI("pill_outline"), (cardW - 320f) / 2f, 28f, 320f, 48f);
            flashText = HtkUI.Label(flashBg.transform, "", HtkTheme.Pixel, 22, HtkTheme.Ink, 0f, 0f, 320f, 48f, TextAnchor.MiddleCenter);
            flash = flashBg.gameObject;
            flash.SetActive(false);

            // ---- timing meters ----
            yiMeter = BuildMeter(screen, "Y → I interval", left, 848f, cardW);
            tiMeter = BuildMeter(screen, "Tighten hold (let go of A)", left, 944f, cardW);

            // ---- right column ----
            const float colX = 1060f, colW = 800f;

            var win = HtkUI.Img(screen, "TimingWindow", HtkTheme.UI("card"), colX, cardY, colW, 110f).rectTransform;
            HtkUI.Label(win, "Timing window", HtkTheme.PixelBold, 22, HtkTheme.Ink, 26f, 24f, 380f, 30f);
            HtkUI.Label(win, "How close to perfect counts as a Perfect.", HtkTheme.Mono, 15, HtkTheme.Muted, 26f, 60f, 400f, 24f);
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var p = HtkUI.Img(win, WindowNames[i], HtkTheme.UI("pill_light"), colW - 26f - (3 - i) * 118f + 8f, 31f, 110f, 48f);
                HtkUI.Label(p.transform, WindowNames[i], HtkTheme.Pixel, 15, HtkTheme.Ink, 0f, 0f, 110f, 48f, TextAnchor.MiddleCenter);
                HtkUI.MakeButton(p.gameObject, () => SetWindow(idx));
                windowPills[i] = p;
            }

            string[] statLabels = { "Stitches", "Perfect", "Y → I average", "Tighten average" };
            float tileW = (colW - 3f * 14f) / 4f;
            for (int i = 0; i < 4; i++)
            {
                var tile = HtkUI.Img(screen, "Stat", HtkTheme.UI("card"), colX + i * (tileW + 14f), cardY + 130f, tileW, 120f).rectTransform;
                HtkUI.Label(tile, statLabels[i], HtkTheme.Mono, 14, HtkTheme.Muted, 20f, 18f, tileW - 30f, 20f);
                statValue[i] = HtkUI.Label(tile, "-", HtkTheme.PixelBold, 30, HtkTheme.Ink, 20f, 44f, tileW - 30f, 36f);
                statSub[i] = HtkUI.Label(tile, "", HtkTheme.Mono, 13, HtkTheme.Muted, 20f, 86f, tileW - 30f, 20f);
            }

            var tip = HtkUI.Img(screen, "Tip", HtkTheme.UI("card"), colX, cardY + 270f, colW, 110f).rectTransform;
            var chip = HtkUI.Img(tip, "Chip", HtkTheme.UI("pill"), 26f, 22f, 64f, 32f);
            HtkUI.Label(chip.transform, "Tip", HtkTheme.Pixel, 15, HtkTheme.Ink, 0f, 0f, 64f, 32f, TextAnchor.MiddleCenter);
            tipText = HtkUI.Label(tip, "", HtkTheme.Mono, 19, HtkTheme.Ink, 110f, 20f, colW - 136f, 72f, TextAnchor.UpperLeft, wrap: true, lineSpacing: 1.2f);

            float hy = cardY + 400f;
            var hist = HtkUI.Img(screen, "History", HtkTheme.UI("card"), colX, hy, colW, 1040f - hy).rectTransform;
            HtkUI.Label(hist, "Recent stitches", HtkTheme.PixelBold, 20, HtkTheme.Ink, 26f, 20f, 400f, 30f);
            var reset = HtkUI.Img(hist, "Reset", HtkTheme.UI("button_light"), colW - 26f - 90f, 18f, 90f, 36f);
            HtkUI.Label(reset.transform, "Reset", HtkTheme.Pixel, 13, HtkTheme.Ink, 0f, 0f, 90f, 36f, TextAnchor.MiddleCenter);
            HtkUI.MakeButton(reset.gameObject, () => { attempts.Clear(); RefreshStats(); });

            float[] colXs = { 26f, 110f, 330f, 560f };
            string[] heads = { "#", "Y → I", "Tighten", "Result" };
            for (int c = 0; c < 4; c++) HtkUI.Label(hist, heads[c], HtkTheme.Mono, 15, HtkTheme.Muted, colXs[c], 66f, 200f, 24f);
            for (int r = 0; r < 6; r++)
            {
                float ry = 98f + r * 38f;
                var row = new Text[4];
                for (int c = 0; c < 3; c++) row[c] = HtkUI.Label(hist, "", HtkTheme.Mono, 15, HtkTheme.Ink, colXs[c], ry, 200f, 30f);
                var chipImg = HtkUI.Img(hist, "Result", HtkTheme.UI("pill"), colXs[3], ry + 2f, 120f, 26f);
                row[3] = HtkUI.Label(chipImg.transform, "", HtkTheme.Pixel, 12, HtkTheme.Ink, 0f, 0f, 120f, 26f, TextAnchor.MiddleCenter);
                historyRows.Add(row);
                historyChips.Add(chipImg);
            }
            emptyHistory = HtkUI.Label(hist, "Your stitches will show up here.", HtkTheme.Mono, 15, HtkTheme.Muted, 26f, 98f, 500f, 30f);

            aboutDialog = HtkAboutDialog.Build(screen, scenes);   // built last so it draws on top
        }

        Meter BuildMeter(RectTransform parent, string label, float x, float y, float w)
        {
            var m = new Meter();
            HtkUI.Label(parent, label, HtkTheme.Pixel, 16, HtkTheme.Ink, x, y, 500f, 20f);
            m.last = HtkUI.Label(parent, "no stitches yet", HtkTheme.Mono, 16, HtkTheme.Muted, x + w - 500f, y, 500f, 20f, TextAnchor.MiddleRight);

            m.track = HtkUI.Img(parent, "Track", HtkTheme.UI("meter_track"), x, y + 24f, w, 30f).rectTransform;
            m.band = HtkUI.Fill(m.track, "PerfectBand", 0f, 2f, 10f, 26f, HtkTheme.WithAlpha(accent, 0.22f)).rectTransform;
            m.bandEdges = HtkUI.Box(m.track, "BandEdges", 0f, -8f, 10f, 46f);
            HtkUI.Fill(m.bandEdges, "L", 0f, 0f, 2f, 46f, accent).rectTransform.anchorMin = new Vector2(0f, 1f);
            var right = HtkUI.Fill(m.bandEdges, "R", 0f, 0f, 2f, 46f, accent).rectTransform;
            right.anchorMin = right.anchorMax = new Vector2(1f, 1f);
            right.pivot = new Vector2(1f, 1f);
            right.anchoredPosition = Vector2.zero;
            m.minLine = HtkUI.Fill(m.track, "Min", 0f, 2f, 2f, 26f, HtkTheme.WithAlpha(HtkTheme.Ink, 0.4f)).rectTransform;
            m.maxLine = HtkUI.Fill(m.track, "Max", 0f, 2f, 2f, 26f, HtkTheme.WithAlpha(HtkTheme.Ink, 0.4f)).rectTransform;
            m.dots = new Image[12];
            for (int i = 0; i < m.dots.Length; i++)
            {
                m.dots[i] = HtkUI.Img(m.track, "Dot", HtkTheme.UI("dot"), 0f, 9f, 12f, 12f);
                m.dots[i].enabled = false;
            }
            m.live = HtkUI.Fill(m.track, "Live", 0f, -6f, 3f, 42f, HtkTheme.Ink).rectTransform;

            HtkUI.Label(parent, "early", HtkTheme.Mono, 13, HtkTheme.Muted, x, y + 60f, 100f, 18f);
            m.scale = HtkUI.Label(parent, "", HtkTheme.Mono, 13, HtkTheme.Muted, x + w / 2f - 100f, y + 60f, 200f, 18f, TextAnchor.MiddleCenter);
            HtkUI.Label(parent, "late", HtkTheme.Mono, 13, HtkTheme.Muted, x + w - 100f, y + 60f, 100f, 18f, TextAnchor.MiddleRight);
            return m;
        }

        // ---- behaviour ----------------------------------------------------------------------------

        void SetWindow(int i)
        {
            windowIndex = i;
            for (int k = 0; k < 3; k++) windowPills[k].sprite = HtkTheme.UI(k == i ? "pill_selected" : "pill_light");
            if (marginsCaptured && progressManager != null)
            {
                progressManager.perfectYIMargin = baseYIMargin * WindowScale[i];
                progressManager.perfectTightenMargin = baseTightenMargin * WindowScale[i];
            }
        }

        void HandleOutcome(StitchOutcome o)
        {
            var a = new Attempt { kind = o.kind };
            foreach (var p in o.phases)
            {
                if (p.phaseId == PhaseIds.YToI)    { a.yi = p.elapsed; a.qy = p.judged ? p.quality : 0; }
                if (p.phaseId == PhaseIds.Tighten) { a.ti = p.judged ? p.elapsed : -1f; a.qt = p.judged ? p.quality : 0; }
            }
            if (a.yi < 0f && a.ti < 0f) return;   // let go before Y - not a timing attempt
            a.q = o.Completed ? o.overallQuality : 0;
            attempts.Add(a);
            if (attempts.Count > 40) attempts.RemoveAt(0);

            string label = o.kind == OutcomeKind.TimedOut ? "Too slow - press I sooner"
                         : o.kind == OutcomeKind.DroppedEarly ? "Dropped - keep holding A"
                         : a.q == 2 ? "Perfect!" : a.q == 1 ? "Good" : "Miss";
            flashText.text = label;
            flashText.color = a.q == 0 ? HtkTheme.Error : HtkTheme.Ink;
            flashBg.sprite = HtkTheme.UI(a.q == 2 ? "pill_selected" : "pill_outline");
            float w = HtkBrowserChrome.TextWidth(label, HtkTheme.Pixel, 22) + 48f;
            flashBg.rectTransform.sizeDelta = new Vector2(w, 48f);
            flashBg.rectTransform.anchoredPosition = new Vector2((960f - w) / 2f, -28f);
            flashText.rectTransform.sizeDelta = new Vector2(w, 48f);
            flash.SetActive(true);
            flashTimer = 1.1f;

            RefreshStats();
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            prompt.Tick(dt);
            if (flashTimer > 0f) { flashTimer -= dt; if (flashTimer <= 0f) flash.SetActive(false); }
            if (progressManager == null) return;

            var step = progressManager.CurrentStep;
            if (step != lastStep) { lastStep = step; ApplyStep(step); }

            bool active = progressManager.TryGetActivePhase(out string phase, out float elapsed, out _);
            UpdateMeter(yiMeter, "yi", active && phase == PhaseIds.YToI, elapsed);
            UpdateMeter(tiMeter, "ti", active && phase == PhaseIds.Tighten, elapsed);
        }

        void ApplyStep(ProgressManager.StitchStep step)
        {
            int stage = step == ProgressManager.StitchStep.Idle ? 0 : step == ProgressManager.StitchStep.ReadyingYarn ? 1
                      : step == ProgressManager.StitchStep.MovedToA ? 2 : 3;
            var mode = step == ProgressManager.StitchStep.Idle ? HtkKeyPrompt.Mode.Hold
                     : step == ProgressManager.StitchStep.MovedToB ? HtkKeyPrompt.Mode.GetReady
                     : step == ProgressManager.StitchStep.Tightening ? HtkKeyPrompt.Mode.Release
                     : HtkKeyPrompt.Mode.Press;
            prompt.Set(mode, CueKeys[stage][0], CuePositions[stage]);
        }

        void Window(string key, out float min, out float perfect, out float max, out float margin)
        {
            var pm = progressManager;
            if (key == "yi") { min = pm.minYIInterval; perfect = pm.perfectYITime; max = pm.maxYIInterval; margin = pm.perfectYIMargin; }
            else             { min = pm.minTightenTime; perfect = pm.perfectTightenTime; max = pm.maxTightenTime; margin = pm.perfectTightenMargin; }
        }

        void UpdateMeter(Meter m, string key, bool live, float liveT)
        {
            Window(key, out float min, out float perfect, out float max, out float margin);
            m.scaleMax = Mathf.Max(0.5f, Mathf.Ceil(max * 1.25f * 10f) / 10f);
            float W = m.track.rect.width;
            System.Func<float, float> X = v => Mathf.Clamp01(v / m.scaleMax) * W;

            m.band.anchoredPosition = new Vector2(X(perfect - margin), -2f);
            m.band.sizeDelta = new Vector2(X(perfect + margin) - X(perfect - margin), 26f);
            m.bandEdges.anchoredPosition = new Vector2(X(perfect - margin), 8f);
            m.bandEdges.sizeDelta = new Vector2(X(perfect + margin) - X(perfect - margin), 46f);
            m.minLine.anchoredPosition = new Vector2(X(min), -2f);
            m.maxLine.anchoredPosition = new Vector2(X(max), -2f);
            m.scale.text = $"0 - {m.scaleMax:0.0}s";

            m.live.gameObject.SetActive(live);
            if (live) m.live.anchoredPosition = new Vector2(X(liveT), 6f);

            var list = new List<Attempt>();
            foreach (var a in attempts) if ((key == "yi" ? a.yi : a.ti) >= 0f) list.Add(a);
            int start = Mathf.Max(0, list.Count - m.dots.Length);
            for (int i = 0; i < m.dots.Length; i++)
            {
                int li = start + i;
                bool on = li < list.Count;
                m.dots[i].enabled = on;
                if (!on) continue;
                float v = key == "yi" ? list[li].yi : list[li].ti;
                bool newest = li == list.Count - 1;
                m.dots[i].rectTransform.anchoredPosition = new Vector2(X(v) - 6f, -9f - ((li % 3) - 1) * 5f);
                m.dots[i].color = new Color(1f, 1f, 1f, newest ? 1f : 0.25f + 0.5f * (li - start) / Mathf.Max(1, list.Count - start));
            }

            if (list.Count == 0) { m.last.text = "no stitches yet"; m.last.color = HtkTheme.Muted; return; }
            var lastA = list[list.Count - 1];
            float val = key == "yi" ? lastA.yi : lastA.ti, err = val - perfect;
            int q = key == "yi" ? lastA.qy : lastA.qt;
            string word = Mathf.Abs(err) < 0.005f ? "on time" : err > 0f ? "late" : "early";
            m.last.text = $"{(q == 2 ? "Perfect" : q == 1 ? "Good" : "Miss")} · {(err >= 0 ? "+" : "-")}{Mathf.Abs(err):0.00}s {word}";
            m.last.color = q == 0 ? HtkTheme.Error : HtkTheme.Ink;
        }

        void RefreshStats()
        {
            int n = attempts.Count, perfect = 0;
            float yiSum = 0f, tiSum = 0f; int yiN = 0, tiN = 0;
            foreach (var a in attempts)
            {
                if (a.q == 2) perfect++;
                if (a.yi >= 0f && a.kind != OutcomeKind.TimedOut) { yiSum += a.yi; yiN++; }
                if (a.ti >= 0f) { tiSum += a.ti; tiN++; }
            }
            float? ay = null, at = null;
            if (progressManager != null)
            {
                if (yiN > 0) ay = yiSum / yiN - progressManager.perfectYITime;
                if (tiN > 0) at = tiSum / tiN - progressManager.perfectTightenTime;
            }
            System.Func<float?, string> Fmt = e => e == null ? "-" : (e >= 0 ? "+" : "-") + Mathf.Abs(e.Value).ToString("0.00") + "s";

            statValue[0].text = n.ToString(); statSub[0].text = "this session";
            statValue[1].text = n > 0 ? Mathf.RoundToInt(100f * perfect / n) + "%" : "-"; statSub[1].text = $"{perfect} of {n}";
            statValue[1].color = accent;
            statValue[2].text = Fmt(ay); statSub[2].text = ay == null ? "vs perfect" : (ay > 0 ? "late" : "early") + " vs perfect";
            statValue[3].text = Fmt(at); statSub[3].text = at == null ? "vs perfect" : (at > 0 ? "long" : "short") + " vs perfect";

            string tip = "Hold A to ready the yarn, press Y then I, and let go of A when the loop is snug.";
            if (n >= 3)
            {
                float ey = Mathf.Abs(ay ?? 0f), et = Mathf.Abs(at ?? 0f);
                if (Mathf.Max(ey, et) < 0.06f) tip = "Your timing is steady. Try the Tight window to make Perfects harder to get.";
                else if (ey >= et) tip = ay > 0 ? $"You press I a little late, about {ey:0.00}s. Press it just as the needle finishes going under."
                                               : $"You press I a little early, about {ey:0.00}s. Wait for the needle to finish going under.";
                else tip = at > 0 ? $"You hold A too long at the end, about {et:0.00}s. Let go as soon as the loop is snug."
                                  : $"You let go of A too soon, about {et:0.00}s. Hold until the loop is pulled snug.";
            }
            tipText.text = tip;

            emptyHistory.enabled = n == 0;
            for (int r = 0; r < historyRows.Count; r++)
            {
                int ai = n - 1 - r;
                bool on = ai >= 0;
                var row = historyRows[r];
                historyChips[r].enabled = on;
                for (int c = 0; c < 4; c++) row[c].enabled = on;
                if (!on) continue;
                var a = attempts[ai];
                row[0].text = (ai + 1).ToString();
                row[1].text = a.yi >= 0f ? a.yi.ToString("0.00") + "s" : "-";
                row[2].text = a.ti >= 0f ? a.ti.ToString("0.00") + "s" : "-";
                row[3].text = a.kind == OutcomeKind.TimedOut ? "Too slow" : a.kind == OutcomeKind.DroppedEarly ? "Dropped"
                            : a.q == 2 ? "Perfect" : a.q == 1 ? "Good" : "Miss";
                row[3].color = a.q == 0 ? HtkTheme.Error : HtkTheme.Ink;
            }
        }
    }
}

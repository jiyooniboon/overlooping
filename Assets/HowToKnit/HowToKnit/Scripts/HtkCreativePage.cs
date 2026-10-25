using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using KnitSprite.CreativeMode;
using KnitSprite.CreativeMode.AI;
using KnitSprite.Knitting;

namespace KnitSprite.UI.HowToKnit
{
    /// <summary>
    /// Creative mode in the "How to Knit" web-page style. Use it INSTEAD of CreativeModeBootstrap
    /// in creativeModeScene (disable the bootstrap). It drives your existing creative-mode scripts
    /// unchanged:
    ///   ChartEditor               - painting, fill, eyedropper, brush size, undo/redo, hotkeys
    ///   ChartRenderer             - draws the chart into the page
    ///   ChartGenerationController - keyword -> motif (AI shapes via Ollama, placeholder as fallback)
    ///
    /// Layout: tool rail | chart card | yarns + stitch shape | generate + shortcuts.
    /// </summary>
    public class HtkCreativePage : MonoBehaviour
    {
        [Header("Chart size (stitches)")]
        [Tooltip("Kept small on purpose: every stitch is played by hand when the chart is knit.")]
        public int chartWidth = 12;
        public int chartHeight = 12;

        [Tooltip("Optional. Leave empty to use the placeholder palette.")]
        public YarnPalette palette;

        [Header("Display")]
        [Tooltip("Largest screen pixels per stitch row. Reduced automatically so the whole canvas fits the card.")]
        public float displayCellHeight = 40f;

        [Header("Navigation")]
        public HtkScenes scenes = new HtkScenes();
        [Tooltip("Gameplay scene that knits the chart (the cast-on scene runs in pattern mode).")]
        public string knitScene = "altSequenceScene";

        [Header("AI generation (plan v2 §1.5)")]
        [Tooltip("Off = placeholder motifs only, no model needed.")]
        public bool useAI = true;
        [Tooltip("Ollama model tag. Change it here to compare models (§2.3); `ollama list` shows what's pulled.")]
        public string ollamaModel = "qwen3.5:4b";
        public string ollamaUrl = "http://localhost:11434";
        [Tooltip("Sends \"think\": false. Untick for a model that rejects the field.")]
        public bool ollamaDisableThinking = true;
        [Tooltip("Seconds to wait for the shapes reply before falling back to the placeholder.")]
        public float shapeTimeoutSeconds = 20f;
        [Tooltip("Send a tiny request when the page opens so the model is loaded before the first Generate.")]
        public bool warmUpModel = true;

        [Header("AI image model (Draw Things, tried first)")]
        [Tooltip("Off = skip the image model and go straight to AI shapes.")]
        public bool useImageModel = true;
        [Tooltip("Draw Things: Settings > API Server, HTTP. Same API as AUTOMATIC1111 / Forge.")]
        public string imageModelUrl = "http://127.0.0.1:7860";
        [Tooltip("0 = use the steps set in Draw Things. FLUX.2 Klein needs about 4.")]
        public int imageSteps = 0;
        [Tooltip("-1 = a new design each time. Any other number repeats the same image for a keyword.")]
        public int imageSeed = -1;
        [Tooltip("Seconds to wait for an image before falling back to AI shapes.")]
        public float imageTimeoutSeconds = 90f;
        [Tooltip("Also save every generated image as PNG (for tuning and the writeup). Path is logged.")]
        public bool saveGeneratedImages = false;

        [Header("Look")]
        public Color accent = HtkTheme.Yarn;
        public int sortingOrder = 100;

        // runtime
        ChartEditor editor;
        ChartRenderer chartRenderer;
        ChartGenerationController generation;
        GameObject aboutDialog;
        RectTransform chartRt, chartFrame;
        Text cursorText, usedText, statusText, responseText, countText, sizeText;
        RectTransform chartCard, rowLabels;
        int labelledHeight = -1;
        bool restored;
        const float ChartCardH = 740f;
        InputField keywordField;
        GameObject cancelButton;
        CanvasGroup generateGroup;
        Image gaugeKnit, gaugeSquare;
        readonly Image[] toolBg = new Image[3];
        readonly List<Image> yarnBg = new List<Image>();
        float usedTimer;
        float lastAspect = -1f;

        static readonly ChartEditor.Tool[] Tools = { ChartEditor.Tool.Brush, ChartEditor.Tool.Fill, ChartEditor.Tool.Eyedropper };

        void Awake()
        {
            if (palette == null) palette = YarnPalette.CreateDefault();

            // Shrink the cells so a bigger canvas (20-30 stitches) still fits the 760 x 740 card,
            // leaving room for the frame, row numbers and footer. Stitches are 1.25x wider than tall.
            displayCellHeight = Mathf.Min(displayCellHeight,
                                          640f / Mathf.Max(1, chartHeight),
                                          600f / (Mathf.Max(1, chartWidth) * 1.25f));
            HtkUI.EnsureEventSystem();

            var screen = HtkUI.Canvas(transform, "CreativeCanvas", sortingOrder);
            HtkBrowserChrome.Build(screen, "/create", HtkBrowserChrome.Tab.Create, scenes,
                                   () => { if (aboutDialog != null) aboutDialog.SetActive(true); });

            const float left = 60f, top = HtkBrowserChrome.ContentTop + 24f, rowY = 282f;
            HtkUI.Label(screen, "Create / Colorwork chart", HtkTheme.Mono, 18, HtkTheme.Muted, left, top, 600f, 24f);
            HtkUI.Label(screen, "creative mode", HtkTheme.PixelBold, 46, HtkTheme.Ink, left, top + 30f, 1000f, 52f);

            BuildChartCard(screen, 210f, rowY);
            BuildRail(screen, left, rowY);
            BuildYarns(screen, 1000f, rowY);
            BuildGenerate(screen, 1410f, rowY);

            // Hook up the existing creative-mode components (same order as CreativeModeBootstrap).
            editor = gameObject.AddComponent<ChartEditor>();
            editor.chartWidth = chartWidth;
            editor.chartHeight = chartHeight;
            editor.palette = palette;
            editor.chartRenderer = chartRenderer;
            editor.uiCamera = null; // Screen Space Overlay

            generation = gameObject.AddComponent<ChartGenerationController>();
            generation.editor = editor;
            generation.conversion.targetColumns = chartWidth;

            aboutDialog = HtkAboutDialog.Build(screen, scenes);   // built last so it draws on top
        }

        OllamaClient llm;
        ShapeSource shapeSource;
        A1111Client imageModel;
        DiffusionSource diffusionSource;

        void Start()
        {
            if (useAI) SetUpAI();
            generation.OnStateChanged += HandleState;
            statusText.text = $"Source: {generation.SourceName}";
        }

        void SetUpAI()
        {
            llm = new OllamaClient { baseUrl = ollamaUrl, model = ollamaModel, disableThinking = ollamaDisableThinking };
            shapeSource = new ShapeSource(llm, palette) { timeoutSeconds = shapeTimeoutSeconds };

            var chain = new List<IChartImageSource>();
            if (useImageModel)
            {
                imageModel = new A1111Client { baseUrl = imageModelUrl };
                diffusionSource = new DiffusionSource(imageModel, palette)
                {
                    steps = imageSteps, seed = imageSeed, timeoutSeconds = imageTimeoutSeconds,
                    saveImagesTo = saveGeneratedImages
                        ? System.IO.Path.Combine(Application.persistentDataPath, "gen-images") : null
                };
                chain.Add(diffusionSource);      // keyword -> flat icon (FLUX.2 Klein) -> chart
            }
            chain.Add(shapeSource);              // §3: LLM -> shapes -> raster
            chain.Add(new PlaceholderImageSource()); // always works
            generation.SetImageSource(new FallbackSource(chain.ToArray()));

            // The first request loads the weights (10s+). Do it now, silently, so the player's
            // first Generate is fast. Errors are only logged: if Ollama isn't running, Generate
            // will say so and fall back anyway.
            if (warmUpModel)
                StartCoroutine(llm.CompleteJson("Reply with an empty JSON object.", "{\"type\":\"object\"}", 120f, r =>
                {
                    if (r.ok) Debug.Log($"[AI] {llm.DisplayName} warmed up in {r.seconds:0.0}s.");
                    else Debug.LogWarning($"[AI] Warm-up failed: {r.error}");
                }));

            // Same for the image model: the first image loads FLUX into memory.
            if (warmUpModel && imageModel != null)
                StartCoroutine(imageModel.Txt2Img(
                    new ImageRequest { prompt = "a simple flat icon of a dot", width = 256, height = 256, steps = 1, timeoutSeconds = 180f },
                    r =>
                    {
                        if (r.success) Debug.Log($"[AI] {imageModel.DisplayName} warmed up. {r.message}");
                        else Debug.LogWarning($"[AI] Image model warm-up failed: {r.message}");
                        if (r.texture != null) Destroy(r.texture);
                    }));
        }

        /// Right-click the component header in Play mode. Logs the prompt and result.
        [ContextMenu("AI: test image model with \"bunny\"")]
        void TestImageModel()
        {
            if (!Application.isPlaying) { Debug.LogWarning("Enter Play mode first."); return; }
            if (diffusionSource == null) { Debug.LogWarning("Use Image Model is off."); return; }
            Debug.Log($"[AI] Prompt: {diffusionSource.BuildPrompt("bunny")}");
            StartCoroutine(diffusionSource.Generate("bunny", r =>
            {
                if (r.success) Debug.Log($"[AI] OK: {r.message} ({r.texture.width}x{r.texture.height})");
                else Debug.LogWarning($"[AI] Failed: {r.message}");
                if (r.texture != null) Destroy(r.texture);
            }));
        }

        /// Right-click the component header in Play mode. Logs the model's raw reply and the result.
        [ContextMenu("AI: test shapes with \"heart\"")]
        void TestShapes()
        {
            if (!Application.isPlaying) { Debug.LogWarning("Enter Play mode first."); return; }
            if (shapeSource == null) SetUpAI();
            StartCoroutine(shapeSource.Generate("heart", r =>
            {
                if (r.success) Debug.Log($"[AI] OK: {r.message}\nYarns used: {r.colorCountHint}");
                else Debug.LogWarning($"[AI] Failed: {r.message}");
                if (r.texture != null) Destroy(r.texture);
            }));
        }

        void OnDestroy()
        {
            if (generation != null) generation.OnStateChanged -= HandleState;
            llm?.Abort(); // don't leave a request running after leaving the page
            imageModel?.Abort();
        }

        // ---- build --------------------------------------------------------------------------

        void BuildChartCard(RectTransform screen, float x, float y)
        {
            const float w = 760f, h = 740f;
            HtkUI.Shadow(screen, x, y, w, h, 0.13f, 24f, 52f);
            var card = HtkUI.Img(screen, "ChartCard", HtkTheme.UI("card_white"), x, y, w, h).rectTransform;
            chartCard = card;

            chartFrame = HtkUI.Node("ChartFrame", card);
            chartFrame.anchorMin = chartFrame.anchorMax = chartFrame.pivot = new Vector2(0.5f, 0.5f);
            chartFrame.anchoredPosition = new Vector2(-18f, 0f);
            HtkUI.AddImage(chartFrame, HtkTheme.UI("pixel"), HtkTheme.Ink);

            var go = new GameObject("ChartView", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(card, false);
            chartRt = (RectTransform)go.transform;
            chartRt.anchorMin = chartRt.anchorMax = chartRt.pivot = new Vector2(0.5f, 0.5f);
            chartRt.anchoredPosition = new Vector2(-18f, 0f);
            chartRenderer = go.AddComponent<ChartRenderer>();
            chartRenderer.displayCellHeight = displayCellHeight;
            chartRenderer.stitchAspect = 1.25f;

            // row numbers are built in RebuildRowLabels, since a generated chart can change the height
            rowLabels = HtkUI.Stretch(HtkUI.Node("RowLabels", card));

            sizeText = HtkUI.Label(card, "", HtkTheme.Mono, 15, HtkTheme.Muted, 40f, h - 34f, 300f, 22f);
            cursorText = HtkUI.Label(card, "", HtkTheme.Mono, 15, HtkTheme.Muted, w - 340f, h - 34f, 300f, 22f, TextAnchor.MiddleRight);
        }

        /// Row numbers down the right of the chart, counted from the bottom like a printed chart.
        void RebuildRowLabels(int height, int width)
        {
            labelledHeight = height;
            for (int i = rowLabels.childCount - 1; i >= 0; i--) Destroy(rowLabels.GetChild(i).gameObject);

            float chartTop = (ChartCardH - height * displayCellHeight) / 2f;
            float chartRight = 760f / 2f - 18f + width * displayCellHeight * chartRenderer.stitchAspect / 2f;
            for (int m = 1; m <= height; m++)
            {
                if (m != 1 && m % 5 != 0) continue;
                float yy = chartTop + (height - m) * displayCellHeight + (displayCellHeight - 16f) / 2f;
                HtkUI.Label(rowLabels, m.ToString(), HtkTheme.Mono, 13, HtkTheme.Muted, chartRight + 8f, yy, 30f, 16f);
            }
            sizeText.text = $"{width} × {height} stitches · {width * (height + 1)} to knit";
        }

        void BuildRail(RectTransform screen, float x, float y)
        {
            var rail = HtkUI.Img(screen, "Tools", HtkTheme.UI("card"), x, y, 120f, 730f).rectTransform;
            string[] names = { "Brush", "Fill", "Pick" };
            string[] icons = { "icon_brush", "icon_fill", "icon_pick" };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var b = HtkUI.Img(rail, names[i], HtkTheme.UI("button_light"), 18f, 16f + i * 96f, 84f, 84f);
                toolBg[i] = b;
                HtkUI.Icon(b.transform, icons[i], 28f, 14f, 28f, HtkTheme.Ink);
                HtkUI.Label(b.transform, names[i], HtkTheme.Pixel, 12, HtkTheme.Ink, 0f, 52f, 84f, 20f, TextAnchor.MiddleCenter);
                HtkUI.MakeButton(b.gameObject, () => editor.activeTool = Tools[idx]);
            }

            HtkUI.Fill(rail, "Rule", 18f, 304f, 84f, 2f, HtkTheme.WithAlpha(HtkTheme.Ink, 0.2f));
            HtkUI.Label(rail, "Size", HtkTheme.Pixel, 12, HtkTheme.Muted, 0f, 316f, 120f, 18f, TextAnchor.MiddleCenter);
            SmallButton(rail, "icon_plus", 38f, 340f, () => editor.brushSize = Mathf.Min(8, editor.brushSize + 1));
            brushLabel = HtkUI.Label(rail, "1", HtkTheme.PixelBold, 26, HtkTheme.Ink, 0f, 390f, 120f, 32f, TextAnchor.MiddleCenter);
            SmallButton(rail, "icon_minus", 38f, 428f, () => editor.brushSize = Mathf.Max(1, editor.brushSize - 1));
            HtkUI.Fill(rail, "Rule", 18f, 486f, 84f, 2f, HtkTheme.WithAlpha(HtkTheme.Ink, 0.2f));

            ActionButton(rail, "Undo", "icon_undo", 500f, () => editor.Undo());
            ActionButton(rail, "Redo", "icon_redo", 576f, () => editor.Redo());
            ActionButton(rail, "Clear", "icon_clear", 652f, () => editor.ClearChart());
        }
        Text brushLabel;

        void SmallButton(RectTransform parent, string icon, float x, float y, System.Action onClick)
        {
            var b = HtkUI.Img(parent, icon, HtkTheme.UI("button_light"), x, y, 44f, 44f);
            HtkUI.Icon(b.transform, icon, 11f, 11f, 22f, HtkTheme.Ink);
            HtkUI.MakeButton(b.gameObject, onClick);
        }

        void ActionButton(RectTransform parent, string label, string icon, float y, System.Action onClick)
        {
            var b = HtkUI.Img(parent, label, HtkTheme.UI("button_light"), 18f, y, 84f, 64f);
            HtkUI.Icon(b.transform, icon, 31f, 10f, 22f, HtkTheme.Ink);
            HtkUI.Label(b.transform, label, HtkTheme.Pixel, 11, HtkTheme.Ink, 0f, 38f, 84f, 18f, TextAnchor.MiddleCenter);
            HtkUI.MakeButton(b.gameObject, onClick);
        }

        void BuildYarns(RectTransform screen, float x, float y)
        {
            float h = 64f + palette.Count * 62f + 16f;
            var card = HtkUI.Img(screen, "Yarns", HtkTheme.UI("card"), x, y, 380f, h).rectTransform;
            HtkUI.Label(card, "Yarns", HtkTheme.PixelBold, 22, HtkTheme.Ink, 22f, 18f, 200f, 30f);
            usedText = HtkUI.Label(card, "", HtkTheme.Mono, 15, HtkTheme.Muted, 180f, 18f, 178f, 30f, TextAnchor.MiddleRight);

            for (int i = 0; i < palette.Count; i++)
            {
                int idx = i;
                var row = HtkUI.Box(card, "Yarn_" + palette.GetName(i), 22f, 60f + i * 62f, 336f, 56f);
                var bg = HtkUI.AddImage(HtkUI.Stretch(HtkUI.Node("Selected", row)), HtkTheme.UI("button_selected"));
                yarnBg.Add(bg);
                HtkUI.Img(row, "Swatch", HtkTheme.UI("swatch_fill"), 12f, 8f, 40f, 40f, (Color)palette.GetColor(i));
                HtkUI.Img(row, "SwatchOutline", HtkTheme.UI("swatch_outline"), 12f, 8f, 40f, 40f);
                HtkUI.Label(row, palette.GetName(i), HtkTheme.Mono, 19, HtkTheme.Ink, 66f, 0f, 170f, 56f);
                if (i == 0)
                    HtkUI.Label(row, "background", HtkTheme.Mono, 13, HtkTheme.Muted, 190f, 0f, 134f, 56f, TextAnchor.MiddleRight);
                else if (i <= 9)
                {
                    var k = HtkUI.Img(row, "Key", HtkTheme.UI("keycap_small"), 336f - 12f - 30f, 13f, 30f, 30f);
                    HtkUI.Label(k.transform, i.ToString(), HtkTheme.Pixel, 13, HtkTheme.Ink, 0f, 0f, 30f, 27f, TextAnchor.MiddleCenter);
                }
                var hit = row.gameObject.AddComponent<Image>();
                hit.color = Color.clear;
                HtkUI.MakeButton(row.gameObject, () => editor.activeYarnIndex = idx);
            }

            // stitch shape
            float sy = y + h + 20f;
            var shape = HtkUI.Img(screen, "StitchShape", HtkTheme.UI("card"), x, sy, 380f, 190f).rectTransform;
            HtkUI.Label(shape, "Stitch shape", HtkTheme.PixelBold, 22, HtkTheme.Ink, 22f, 18f, 300f, 30f);
            gaugeKnit = Pill(shape, "Knit gauge", 22f, 60f, 164f, () => SetAspect(1.25f));
            gaugeSquare = Pill(shape, "Square", 194f, 60f, 164f, () => SetAspect(1f));
            HtkUI.Label(shape, "Knit stitches are wider than they are tall, so the chart previews the real fabric at 1.25 : 1.",
                        HtkTheme.Mono, 15, HtkTheme.Muted, 22f, 120f, 336f, 56f, TextAnchor.UpperLeft, wrap: true, lineSpacing: 1.15f);
        }

        Image Pill(RectTransform parent, string label, float x, float y, float w, System.Action onClick)
        {
            var p = HtkUI.Img(parent, label, HtkTheme.UI("pill_light"), x, y, w, 48f);
            HtkUI.Label(p.transform, label, HtkTheme.Pixel, 15, HtkTheme.Ink, 0f, 0f, w, 48f, TextAnchor.MiddleCenter);
            HtkUI.MakeButton(p.gameObject, onClick);
            return p;
        }

        void SetAspect(float aspect)
        {
            chartRenderer.stitchAspect = aspect;
            chartRenderer.RebuildTexture();
        }

        void BuildGenerate(RectTransform screen, float x, float y)
        {
            const float w = 450f, h = 404f;
            var card = HtkUI.Img(screen, "Generate", HtkTheme.UI("card"), x, y, w, h).rectTransform;
            HtkUI.Icon(card, "icon_spark", 26f, 24f, 24f, HtkTheme.Ink);
            HtkUI.Label(card, "Generate a design", HtkTheme.PixelBold, 24, HtkTheme.Ink, 60f, 20f, 360f, 32f);
            HtkUI.Label(card, "Describe a motif, e.g. \"snowflake\" or \"bunny\"", HtkTheme.Mono, 16, HtkTheme.Muted, 26f, 62f, 398f, 24f);

            // keyword field
            var fieldRt = HtkUI.Box(card, "KeywordField", 26f, 98f, 398f, 52f);
            var fieldBg = HtkUI.AddImage(fieldRt, HtkTheme.UI("url_bar"));
            fieldBg.raycastTarget = true;
            var text = HtkUI.Label(fieldRt, "", HtkTheme.Mono, 18, HtkTheme.Ink, 18f, 0f, 300f, 52f);
            text.supportRichText = false;
            var placeholder = HtkUI.Label(fieldRt, "type a keyword...", HtkTheme.Mono, 18, HtkTheme.Muted, 18f, 0f, 300f, 52f);
            countText = HtkUI.Label(fieldRt, "0/40", HtkTheme.Mono, 13, HtkTheme.Muted, 398f - 80f, 0f, 62f, 52f, TextAnchor.MiddleRight);
            keywordField = fieldRt.gameObject.AddComponent<InputField>();
            keywordField.characterLimit = KeywordFilter.MaxLength;
            keywordField.lineType = InputField.LineType.SingleLine;
            keywordField.textComponent = text;
            keywordField.placeholder = placeholder;
            keywordField.targetGraphic = fieldBg;
            keywordField.onEndEdit.AddListener(_ =>
            {
                // onEndEdit also fires on focus loss; only Enter submits.
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Submit();
            });

            var gen = HtkUI.Img(card, "GenerateButton", HtkTheme.UI("button_dark"), 26f, 166f, 191f, 56f);
            HtkUI.Label(gen.transform, "Generate", HtkTheme.Pixel, 18, HtkTheme.Card, 0f, 0f, 191f, 56f, TextAnchor.MiddleCenter);
            HtkUI.MakeButton(gen.gameObject, Submit);
            generateGroup = gen.gameObject.AddComponent<CanvasGroup>();

            var cancel = HtkUI.Img(card, "CancelButton", HtkTheme.UI("button_light"), 229f, 166f, 195f, 56f);
            HtkUI.Label(cancel.transform, "Cancel", HtkTheme.Pixel, 18, HtkTheme.Ink, 0f, 0f, 195f, 56f, TextAnchor.MiddleCenter);
            HtkUI.MakeButton(cancel.gameObject, () => generation.Cancel());
            cancelButton = cancel.gameObject;
            cancelButton.SetActive(false);

            statusText = HtkUI.Label(card, "", HtkTheme.Mono, 15, HtkTheme.Muted, 26f, 234f, 398f, 24f);
            var box = HtkUI.Img(card, "Response", HtkTheme.UI("response_box"), 26f, 268f, 398f, 112f);
            box.type = Image.Type.Tiled;
            responseText = HtkUI.Label(box.transform, "AI response here", HtkTheme.Mono, 15, HtkTheme.Muted, 16f, 14f, 366f, 84f,
                                       TextAnchor.UpperLeft, wrap: true, lineSpacing: 1.2f);

            // shortcuts
            float sy = y + h + 20f;
            var keys = HtkUI.Img(screen, "Shortcuts", HtkTheme.UI("card"), x, sy, w, 210f).rectTransform;
            HtkUI.Label(keys, "Shortcuts", HtkTheme.PixelBold, 18, HtkTheme.Ink, 26f, 18f, 300f, 26f);
            Shortcut(keys, 26f, 60f, new[] { "L-drag" }, "paint");
            Shortcut(keys, 236f, 60f, new[] { "R-drag" }, "erase");
            Shortcut(keys, 26f, 106f, new[] { "1", "6" }, "yarn", "–");
            Shortcut(keys, 236f, 106f, new[] { "[", "]" }, "size");
            Shortcut(keys, 26f, 152f, new[] { "B", "G", "I" }, "tools");
            Shortcut(keys, 236f, 152f, new[] { "Ctrl", "Z", "Y" }, "undo / redo");

            // knit the chart
            var knit = HtkUI.Img(screen, "KnitThis", HtkTheme.UI("button_dark"), x, sy + 230f, w, 72f);
            HtkUI.Label(knit.transform, "Knit this chart", HtkTheme.Pixel, 22, HtkTheme.Card, 28f, 0f, w - 90f, 72f);
            HtkUI.Icon(knit.transform, "icon_arrow_right", w - 28f - 26f, 23f, 26f, HtkTheme.Card);
            HtkUI.MakeButton(knit.gameObject, KnitThis);
        }

        void KnitThis()
        {
            if (editor == null || editor.Chart == null) return;
            if (generation != null && generation.CurrentState == ChartGenerationController.State.Generating) return;
            var pattern = KnitPattern.FromChart(editor.Chart, editor.palette);
            if (pattern == null) return;
            KnitSession.Begin(pattern);
            HtkScenes.Load(knitScene);
        }

        void Shortcut(RectTransform parent, float x, float y, string[] keys, string label, string joiner = "")
        {
            float cx = x;
            for (int i = 0; i < keys.Length; i++)
            {
                float kw = Mathf.Max(30f, HtkBrowserChrome.TextWidth(keys[i], HtkTheme.Pixel, 13) + 14f);
                var k = HtkUI.Img(parent, "Key", HtkTheme.UI("keycap_small"), cx, y, kw, 30f);
                HtkUI.Label(k.transform, keys[i], HtkTheme.Pixel, 13, HtkTheme.Ink, 0f, 0f, kw, 27f, TextAnchor.MiddleCenter);
                cx += kw + 4f;
                if (joiner != "" && i < keys.Length - 1) { HtkUI.Label(parent, joiner, HtkTheme.Mono, 14, HtkTheme.Ink, cx, y, 12f, 30f); cx += 14f; }
            }
            HtkUI.Label(parent, label, HtkTheme.Mono, 14, HtkTheme.Ink, cx + 6f, y, 140f, 30f);
        }

        // ---- per frame ------------------------------------------------------------------------

        void Update()
        {
            if (editor == null) return;

            // Coming back from knitting: put the chart that was knit back in the editor.
            if (!restored && editor.Chart != null)
            {
                restored = true;
                if (KnitSession.Current != null) editor.LoadChart(KnitSession.Current.sourceChart.Clone());
            }
            if (editor.Chart != null && (editor.Chart.Height != labelledHeight || chartRenderer.stitchAspect != lastAspect))
            {
                lastAspect = chartRenderer.stitchAspect;
                RebuildRowLabels(editor.Chart.Height, editor.Chart.Width);
            }

            // Typing a keyword must not trigger the editor's B / G / I / number hotkeys.
            editor.enabled = keywordField == null || !keywordField.isFocused;

            for (int i = 0; i < 3; i++)
                toolBg[i].sprite = HtkTheme.UI(editor.activeTool == Tools[i] ? "button_selected" : "button_light");
            for (int i = 0; i < yarnBg.Count; i++)
                yarnBg[i].enabled = editor.activeYarnIndex == i;
            brushLabel.text = editor.brushSize.ToString();

            bool knit = chartRenderer.stitchAspect > 1.01f;
            gaugeKnit.sprite = HtkTheme.UI(knit ? "pill_selected" : "pill_light");
            gaugeSquare.sprite = HtkTheme.UI(knit ? "pill_light" : "pill_selected");

            // black frame hugging the chart (its size follows the gauge)
            chartFrame.sizeDelta = chartRt.sizeDelta + new Vector2(4f, 4f);

            countText.text = $"{keywordField.text.Length}/{KeywordFilter.MaxLength}";

            cursorText.text = chartRenderer.ScreenToCell(Input.mousePosition, null, out int cx, out int cy)
                ? $"Stitch {editor.Chart.Width - cx}, row {cy + 1}" : "";

            usedTimer -= Time.unscaledDeltaTime;
            if (usedTimer <= 0f && editor.Chart != null)
            {
                usedTimer = 0.25f;
                var seen = new HashSet<int>();
                for (int yy = 0; yy < editor.Chart.Height; yy++)
                    for (int xx = 0; xx < editor.Chart.Width; xx++)
                        seen.Add(editor.Chart.Get(xx, yy));
                usedText.text = $"{seen.Count} of {palette.Count} used";
            }
        }

        void Submit()
        {
            if (generation != null && keywordField != null) generation.Submit(keywordField.text);
        }

        void HandleState(ChartGenerationController.State state, string message)
        {
            bool busy = state == ChartGenerationController.State.Generating;
            cancelButton.SetActive(busy);
            generateGroup.alpha = busy ? 0.5f : 1f;
            generateGroup.interactable = !busy;
            keywordField.interactable = !busy;

            statusText.text = state == ChartGenerationController.State.Done ? "Done - edit it by hand from here." : message;
            statusText.color =
                state == ChartGenerationController.State.Error ? HtkTheme.Error :
                state == ChartGenerationController.State.Done ? HtkTheme.Hex("#2d6a45") :
                busy ? HtkTheme.Hex("#2e4f8f") : HtkTheme.Muted;
            if (state == ChartGenerationController.State.Done) responseText.text = message;
        }
    }
}

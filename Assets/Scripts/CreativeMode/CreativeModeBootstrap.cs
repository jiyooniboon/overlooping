using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KnitSprite.CreativeMode
{
    /// <summary>
    /// Builds the whole Creative Mode UI hierarchy at runtime so creativeModeScene only needs a
    /// single empty GameObject with this component on it. Nothing to wire by hand in the
    /// Inspector, which also means the setup can't drift out of sync with the scripts.
    ///
    /// Once the layout settles you can replace this with a hand-built prefab - but for now,
    /// generating it keeps the scene file trivial and merge-friendly (useful on a team project
    /// where Unity scene files conflict badly in git).
    ///
    /// Creates:
    ///   Canvas (Screen Space - Overlay, scales with resolution)
    ///     ChartView   - RawImage + ChartRenderer, centered
    ///     PaletteBar  - one swatch button per yarn, bottom of screen
    ///     HelpText    - controls reminder
    ///   ChartEditor lives on this GameObject.
    /// </summary>
    public class CreativeModeBootstrap : MonoBehaviour
    {
        [Header("Chart size (stitches)")]
        public int chartWidth = 40;
        public int chartHeight = 50;

        [Tooltip("Optional. Leave empty to use placeholder colors.")]
        public YarnPalette palette;

        [Header("Display")]
        [Tooltip("Screen pixels per stitch row. Chart width is scaled by gauge on top of this.")]
        public float displayCellHeight = 12f;

        [Tooltip("1.25 previews true knitting gauge; 1 shows a plain square grid.")]
        public float stitchAspect = 1.25f;

        private ChartEditor editor;
        private ChartRenderer chartRenderer;
        private ChartGenerationController generation;
        private GenerationPanel generationPanel;

        void Awake()
        {
            if (palette == null) palette = YarnPalette.CreateDefault();

            EnsureEventSystem();

            Canvas canvas = BuildCanvas();
            chartRenderer = BuildChartView(canvas);

            editor = gameObject.AddComponent<ChartEditor>();
            editor.chartWidth = chartWidth;
            editor.chartHeight = chartHeight;
            editor.palette = palette;
            editor.chartRenderer = chartRenderer;
            editor.uiCamera = null; // Screen Space Overlay -> null camera is correct

            generation = gameObject.AddComponent<ChartGenerationController>();
            generation.editor = editor;
            generation.conversion.targetColumns = chartWidth;

            BuildPaletteBar(canvas);
            BuildHelpText(canvas);

            generationPanel = gameObject.AddComponent<GenerationPanel>();
            generationPanel.Build((RectTransform)canvas.transform, generation);
        }

        /// <summary>
        /// Buttons and input fields receive nothing without an EventSystem in the scene. Creating
        /// one here means the scene needs no manual setup at all - and a missing EventSystem is a
        /// famously silent failure, where the UI renders perfectly and simply ignores every click.
        /// </summary>
        void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;

            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            go.transform.SetParent(transform, false);
        }

        Canvas BuildCanvas()
        {
            var go = new GameObject("CreativeModeCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            return canvas;
        }

        ChartRenderer BuildChartView(Canvas canvas)
        {
            var go = new GameObject("ChartView", typeof(RawImage), typeof(ChartRenderer));
            go.transform.SetParent(canvas.transform, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, 40f); // nudge up to clear the palette bar

            var r = go.GetComponent<ChartRenderer>();
            r.displayCellHeight = displayCellHeight;
            r.stitchAspect = stitchAspect;
            return r;
        }

        void BuildPaletteBar(Canvas canvas)
        {
            var bar = new GameObject("PaletteBar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            bar.transform.SetParent(canvas.transform, false);

            var rt = (RectTransform)bar.transform;
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 24f);
            rt.sizeDelta = new Vector2(palette.Count * 76f, 64f);

            var layout = bar.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            for (int i = 0; i < palette.Count; i++)
            {
                int index = i; // capture per-iteration for the closure
                var swatch = new GameObject($"Yarn_{i}_{palette.GetName(i)}",
                    typeof(Image), typeof(Button), typeof(LayoutElement));
                swatch.transform.SetParent(bar.transform, false);

                swatch.GetComponent<Image>().color = palette.GetColor(i);

                var le = swatch.GetComponent<LayoutElement>();
                le.preferredWidth = 64f;
                le.preferredHeight = 64f;

                swatch.GetComponent<Button>().onClick.AddListener(() =>
                {
                    if (editor != null) editor.activeYarnIndex = index;
                });
            }
        }

        void BuildHelpText(Canvas canvas)
        {
            // Uses legacy UI Text so this has no TextMeshPro dependency. Swap for TMP later if
            // the rest of the game's UI standardizes on it.
            var go = new GameObject("HelpText", typeof(Text));
            go.transform.SetParent(canvas.transform, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(24f, -24f);
            rt.sizeDelta = new Vector2(620f, 140f);

            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 20;
            text.color = new Color(0.15f, 0.15f, 0.18f);
            text.raycastTarget = false; // must not swallow clicks meant for the chart
            text.text =
                "LMB paint   RMB erase\n" +
                "1-9 yarn color    B brush   G fill   I eyedropper\n" +
                "[ ] brush size    Ctrl+Z undo   Ctrl+Y redo";
        }
    }
}

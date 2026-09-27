using UnityEngine;
using UnityEngine.UI;

namespace KnitSprite.CreativeMode
{
    /// <summary>
    /// Draws a ColorworkChart into a single Texture2D shown on a RawImage. This is the only
    /// script that touches the texture - ChartEditor mutates chart data and calls MarkDirty.
    ///
    /// Why one texture instead of a GameObject per cell: a full-size chart is easily 100x125 =
    /// 12,500 cells. That many SpriteRenderers is a real performance problem, while a single
    /// texture upload handles it without effort. The tradeoff is that individual stitches can't
    /// animate independently - acceptable here, since a chart is a static design surface.
    ///
    /// Each chart cell is drawn as a block of cellPixelSize texture pixels so that grid lines fit
    /// between cells. Without grid lines a colorwork chart is genuinely hard to read/count.
    ///
    /// Aspect: knit stitches are wider than they are tall (typical gauge ~4 sts/inch by
    /// ~5 rows/inch). stitchAspect stretches the DISPLAY horizontally so the editor previews what
    /// the knitted fabric will actually look like rather than a misleading square grid. It only
    /// affects the RectTransform size, never the chart data.
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public class ChartRenderer : MonoBehaviour
    {
        [Header("Cell appearance")]
        [Tooltip("Texture pixels per chart cell. Higher = thinner-looking grid lines relative to cells.")]
        [Range(2, 32)] public int cellPixelSize = 10;

        [Tooltip("Draw 1px separator lines between cells.")]
        public bool showGridLines = true;

        public Color32 gridLineColor = new Color32(0, 0, 0, 40);

        [Header("Gauge preview")]
        [Tooltip("Display width/height ratio of one stitch. 1.25 approximates real knitting gauge " +
                 "(4 sts/inch by 5 rows/inch). Set to 1 to view as a plain square grid.")]
        public float stitchAspect = 1.25f;

        [Tooltip("Overall on-screen scale, in screen pixels per chart cell height.")]
        public float displayCellHeight = 6f;

        private RawImage rawImage;
        private RectTransform rectTransform;
        private Texture2D texture;
        private Color32[] pixels;
        private ColorworkChart chart;
        private YarnPalette palette;
        private bool dirty;

        public Texture2D Texture => texture;

        void Awake()
        {
            rawImage = GetComponent<RawImage>();
            rectTransform = GetComponent<RectTransform>();

            // The chart is drawn as UI but behaves as a canvas, not a widget. Disabling raycast
            // lets ChartEditor use IsPointerOverGameObject to mean "over a real UI control".
            rawImage.raycastTarget = false;
        }

        public void Bind(ColorworkChart chart, YarnPalette palette)
        {
            this.chart = chart;
            this.palette = palette;
            RebuildTexture();
        }

        /// <summary>Call after changing cellPixelSize, chart dimensions, or the palette asset.</summary>
        public void RebuildTexture()
        {
            if (chart == null) return;

            int texW = chart.Width * cellPixelSize;
            int texH = chart.Height * cellPixelSize;

            if (texture == null || texture.width != texW || texture.height != texH)
            {
                if (texture != null) Destroy(texture);

                texture = new Texture2D(texW, texH, TextureFormat.RGBA32, false)
                {
                    // Point filtering is essential - bilinear turns crisp stitch cells into mush.
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                pixels = new Color32[texW * texH];
                rawImage.texture = texture;
            }

            RepaintAll();
            ApplyDisplaySize();
        }

        void ApplyDisplaySize()
        {
            if (chart == null) return;
            float h = chart.Height * displayCellHeight;
            float w = chart.Width * displayCellHeight * stitchAspect;
            rectTransform.sizeDelta = new Vector2(w, h);
        }

        public void RepaintAll()
        {
            if (chart == null || texture == null) return;

            for (int y = 0; y < chart.Height; y++)
                for (int x = 0; x < chart.Width; x++)
                    WriteCell(x, y, chart.Get(x, y));

            dirty = true;
        }

        /// <summary>Repaint a single cell. Much cheaper than RepaintAll during brush strokes.</summary>
        public void RepaintCell(int x, int y)
        {
            if (chart == null || texture == null || !chart.InBounds(x, y)) return;
            WriteCell(x, y, chart.Get(x, y));
            dirty = true;
        }

        public void RepaintCellByIndex(int flatIndex)
        {
            if (chart == null) return;
            RepaintCell(flatIndex % chart.Width, flatIndex / chart.Width);
        }

        void WriteCell(int cx, int cy, int paletteIndex)
        {
            Color32 c = palette != null ? palette.GetColor(paletteIndex) : new Color32(255, 0, 255, 255);

            int originX = cx * cellPixelSize;
            int originY = cy * cellPixelSize;
            int texW = texture.width;

            for (int py = 0; py < cellPixelSize; py++)
            {
                int row = (originY + py) * texW + originX;
                for (int px = 0; px < cellPixelSize; px++)
                {
                    bool isEdge = showGridLines && (px == 0 || py == 0);
                    pixels[row + px] = isEdge ? Blend(c, gridLineColor) : c;
                }
            }
        }

        static Color32 Blend(Color32 baseColor, Color32 overlay)
        {
            float a = overlay.a / 255f;
            return new Color32(
                (byte)(baseColor.r * (1 - a) + overlay.r * a),
                (byte)(baseColor.g * (1 - a) + overlay.g * a),
                (byte)(baseColor.b * (1 - a) + overlay.b * a),
                255);
        }

        void LateUpdate()
        {
            // Batch every edit made this frame into one upload. Calling Apply() per painted cell
            // during a drag would stall on the GPU upload dozens of times per frame.
            if (!dirty || texture == null) return;
            texture.SetPixels32(pixels);
            texture.Apply(false);
            dirty = false;
        }

        /// <summary>
        /// Converts a screen point to a chart cell. Returns false if the point is outside the
        /// chart. Uses RectTransformUtility so it stays correct under any Canvas scaling mode.
        /// </summary>
        public bool ScreenToCell(Vector2 screenPoint, Camera cam, out int x, out int y)
        {
            x = y = -1;
            if (chart == null) return false;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rectTransform, screenPoint, cam, out Vector2 local))
                return false;

            Rect r = rectTransform.rect;
            float u = (local.x - r.xMin) / r.width;   // 0..1 left->right
            float v = (local.y - r.yMin) / r.height;  // 0..1 bottom->top
            if (u < 0f || u >= 1f || v < 0f || v >= 1f) return false;

            x = Mathf.FloorToInt(u * chart.Width);
            y = Mathf.FloorToInt(v * chart.Height);
            return chart.InBounds(x, y);
        }

        void OnDestroy()
        {
            if (texture != null) Destroy(texture);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            // Live-update the preview when tweaking gauge/scale in the Inspector at runtime.
            if (Application.isPlaying && chart != null) ApplyDisplaySize();
        }
#endif
    }
}

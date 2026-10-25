using UnityEngine;
using UnityEngine.UI;
using KnitSprite.Knitting;

namespace KnitSprite.UI.HowToKnit
{
    /// <summary>
    /// Small live copy of the player's chart for the gameplay page in pattern mode. Replaces the
    /// 25-tick stitch counter: stitches not made yet are pale, made stitches show their yarn,
    /// loose (Miss) stitches are washed out, and the stitch being made blinks with an ink outline.
    /// The cast-on row sits under the chart.
    ///
    /// Lives outside the HowToKnit kit folder on purpose, so copying a new kit version over that
    /// folder can't overwrite it.
    /// </summary>
    public class HtkPatternPanel
    {
        const int CellPx = 6;               // texture pixels per stitch (incl. 1px grid line)
        const float StitchAspect = 1.25f;   // knit stitches are wider than tall, same as the chart editor

        readonly KnitPattern pattern;
        readonly Texture2D texture;
        readonly Color32[] pixels;
        readonly RectTransform chartRt, cursor;
        readonly Text rowText, yarnText;
        readonly Image swatch;
        readonly float cellW, cellH;
        int current = -1;
        float blink;

        static readonly Color32 Grid = new Color32(26, 28, 34, 40);

        public HtkPatternPanel(RectTransform parent, KnitPattern pattern, float x, float y, float w, float h)
        {
            this.pattern = pattern;
            var card = HtkUI.Img(parent, "PatternPanel", HtkTheme.UI("card"), x, y, w, h).rectTransform;

            HtkUI.Label(card, "Your pattern", HtkTheme.PixelBold, 20, HtkTheme.Ink, 22f, 14f, 260f, 28f);
            rowText = HtkUI.Label(card, "", HtkTheme.Mono, 15, HtkTheme.Muted, w - 222f, 14f, 200f, 28f, TextAnchor.MiddleRight);

            // chart area: left part of the card, fit by height and width
            float areaX = 22f, areaY = 50f, areaW = w * 0.55f, areaH = h - 50f - 16f;
            int cols = pattern.width, rows = pattern.TotalRows;
            float ch = Mathf.Min(areaH / rows, areaW / (cols * StitchAspect));
            cellH = ch; cellW = ch * StitchAspect;
            float cw = cols * cellW, chh = rows * cellH;

            texture = new Texture2D(cols * CellPx, rows * CellPx, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "PatternProgress"
            };
            pixels = new Color32[texture.width * texture.height];

            var frame = HtkUI.Fill(card, "ChartFrame", areaX - 2f, areaY - 2f, cw + 4f, chh + 4f, HtkTheme.Ink);
            chartRt = HtkUI.Box(card, "Chart", areaX, areaY, cw, chh);
            var raw = chartRt.gameObject.AddComponent<RawImage>();
            raw.texture = texture;
            raw.raycastTarget = false;

            // blinking outline around the current stitch (4 thin bars)
            cursor = HtkUI.Box(chartRt, "Current", 0f, 0f, cellW + 4f, cellH + 4f);
            float t = 2f;
            HtkUI.Fill(cursor, "T", 0f, 0f, cellW + 4f, t, HtkTheme.Ink);
            HtkUI.Fill(cursor, "B", 0f, cellH + 4f - t, cellW + 4f, t, HtkTheme.Ink);
            HtkUI.Fill(cursor, "L", 0f, 0f, t, cellH + 4f, HtkTheme.Ink);
            HtkUI.Fill(cursor, "R", cellW + 4f - t, 0f, t, cellH + 4f, HtkTheme.Ink);

            // next-yarn readout on the right
            float rx = areaX + cw + 24f;
            HtkUI.Label(card, "Next yarn", HtkTheme.Pixel, 14, HtkTheme.Muted, rx, areaY, w - rx - 16f, 22f);
            swatch = HtkUI.Img(card, "Swatch", HtkTheme.UI("swatch_fill"), rx, areaY + 28f, 40f, 40f);
            HtkUI.Img(card, "SwatchOutline", HtkTheme.UI("swatch_outline"), rx, areaY + 28f, 40f, 40f);
            yarnText = HtkUI.Label(card, "", HtkTheme.Mono, 17, HtkTheme.Ink, rx + 50f, areaY + 28f, w - rx - 66f, 40f);

            SetProgress(0);
        }

        /// <param name="done">How many stitches of the pattern have been made.</param>
        public void SetProgress(int done)
        {
            current = done;
            for (int i = 0; i < pattern.Count; i++) PaintCell(i, i < done);
            texture.SetPixels32(pixels);
            texture.Apply(false);

            bool finished = done >= pattern.Count;
            cursor.gameObject.SetActive(!finished);
            if (finished)
            {
                rowText.text = "finished";
                yarnText.text = "-";
                return;
            }

            var s = pattern[done];
            rowText.text = s.row == 0 ? "cast-on row" : $"row {s.row} of {pattern.chartRows}";
            swatch.color = (Color)pattern.YarnColor(s.yarnIndex);
            yarnText.text = pattern.YarnName(s.yarnIndex);

            // texture row 0 is the bottom, UI y grows downward from the top-left
            int rows = pattern.TotalRows;
            cursor.anchoredPosition = new Vector2(s.column * cellW - 2f, -((rows - 1 - s.row) * cellH) + 2f);
        }

        void PaintCell(int i, bool made)
        {
            var s = pattern[i];
            Color32 c = pattern.YarnColor(s.yarnIndex);
            if (!made) c = Color32.Lerp(c, new Color32(255, 255, 255, 255), 0.72f);
            else if (pattern.results[i] == 0) c = Color32.Lerp(c, new Color32(255, 255, 255, 255), 0.4f); // Miss = loose

            int x0 = s.column * CellPx, y0 = s.row * CellPx, tw = texture.width;
            for (int py = 0; py < CellPx; py++)
                for (int px = 0; px < CellPx; px++)
                    pixels[(y0 + py) * tw + x0 + px] = (px == 0 || py == 0) ? Grid : c;
        }

        /// Blink the current-stitch outline.
        public void Tick(float dt)
        {
            if (current < 0 || current >= pattern.Count) return;
            blink += dt;
            cursor.gameObject.SetActive((blink % 0.8f) < 0.55f);
        }

        public void Destroy()
        {
            if (texture != null) Object.Destroy(texture);
        }
    }
}

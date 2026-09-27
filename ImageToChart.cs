using System.Collections.Generic;
using UnityEngine;

namespace KnitSprite.CreativeMode
{
    /// <summary>
    /// Converts an arbitrary image into a knittable ColorworkChart.
    ///
    /// This is the substantive half of AI generation and it contains no AI: it works on any
    /// Texture2D, so it can be built, tested and demoed against hand-picked PNGs long before a
    /// model is involved. Swapping the placeholder image source for Stable Diffusion later
    /// changes nothing in here.
    ///
    /// Pipeline, in order (each step exists to fix a specific failure mode):
    ///   1. Background removal  - corner color-key, so empty chart cells stay empty
    ///   2. Auto-crop           - fill the grid with the subject, not with blank margin
    ///   3. Area downsample     - AVERAGE each cell's footprint; nearest-neighbour sampling
    ///                            picks one arbitrary pixel and turns stray highlights into cells
    ///   4. Gauge correction    - knit stitches are wider than tall, so a square image needs MORE
    ///                            rows than columns or the knitted result comes out stretched
    ///   5. Lab k-means         - cluster to a small yarn count perceptually, not in RGB
    ///   6. Palette snap        - map clusters onto real yarn colors
    ///   7. Despeckle           - remove isolated cells that read as mistakes rather than design
    ///
    /// Runs synchronously on the main thread. That is fine at chart resolution (quantization
    /// happens on ~2,000 cells, not on the source image's millions of pixels) - downsampling
    /// first is what keeps this cheap.
    /// </summary>
    public static class ImageToChart
    {
        [System.Serializable]
        public class Settings
        {
            [Tooltip("Chart width in stitches. Row count is derived from this via gauge.")]
            public int targetColumns = 40;

            [Tooltip("Gauge: stitches per inch (horizontal).")]
            public float stitchesPerInch = 4f;

            [Tooltip("Gauge: rows per inch (vertical). Higher than stitchesPerInch because knit " +
                     "stitches are wider than they are tall.")]
            public float rowsPerInch = 5f;

            [Tooltip("Number of yarn colors to reduce to (excluding background).")]
            [Range(2, 6)] public int colorCount = 4;

            public bool removeBackground = true;

            [Tooltip("How close a pixel must be to the corner color to count as background. " +
                     "Lab distance, squared.")]
            public float backgroundTolerance = 90f;

            [Tooltip("A cell becomes background unless at least this fraction of it is foreground.")]
            [Range(0f, 1f)] public float coverageThreshold = 0.5f;

            public bool despeckle = true;

            [Tooltip("Give every cluster a different yarn. Without this, clusters that are close " +
                     "in color all snap to the same nearest yarn and the chart silently collapses " +
                     "to fewer colors than the player asked for.")]
            public bool forceDistinctYarns = true;

            [Tooltip("k-means iterations. 12 is plenty at chart resolution.")]
            public int kMeansIterations = 12;
        }

        public static ColorworkChart Convert(Texture2D source, YarnPalette palette, Settings s)
        {
            if (source == null || palette == null || palette.Count < 2)
            {
                Debug.LogError("ImageToChart: needs a source texture and a palette with at least 2 colors.");
                return new ColorworkChart(s?.targetColumns ?? 40, 50);
            }

            Color32[] src = source.GetPixels32();
            int sw = source.width, sh = source.height;

            // --- 1. Background mask -------------------------------------------------------
            bool[] isForeground = new bool[sw * sh];
            if (s.removeBackground)
                BuildForegroundMask(src, sw, sh, s.backgroundTolerance, isForeground);
            else
                for (int i = 0; i < isForeground.Length; i++) isForeground[i] = true;

            // --- 2. Auto-crop to the subject ---------------------------------------------
            if (!FindBounds(isForeground, sw, sh, out int minX, out int minY, out int maxX, out int maxY))
            {
                // Nothing survived background removal - treat the whole image as subject rather
                // than returning an empty chart, which would look like a crash to the player.
                minX = 0; minY = 0; maxX = sw - 1; maxY = sh - 1;
                for (int i = 0; i < isForeground.Length; i++) isForeground[i] = true;
            }

            int cropW = maxX - minX + 1;
            int cropH = maxY - minY + 1;

            // --- 3/4. Grid size with gauge correction ------------------------------------
            int cols = Mathf.Max(2, s.targetColumns);
            float gauge = s.rowsPerInch / Mathf.Max(0.01f, s.stitchesPerInch);
            int rows = Mathf.Max(2, Mathf.RoundToInt(cols * ((float)cropH / cropW) * gauge));

            var cellColor = new Color32[cols * rows];
            var cellIsForeground = new bool[cols * rows];
            AreaDownsample(src, sw, isForeground, minX, minY, cropW, cropH,
                           cols, rows, s.coverageThreshold, cellColor, cellIsForeground);

            // --- 5. Cluster foreground cells in Lab --------------------------------------
            var fgIndices = new List<int>();
            for (int i = 0; i < cellColor.Length; i++)
                if (cellIsForeground[i]) fgIndices.Add(i);

            var chart = new ColorworkChart(cols, rows); // all background by default
            if (fgIndices.Count == 0) return chart;

            var fgLab = new Lab[fgIndices.Count];
            for (int i = 0; i < fgIndices.Count; i++)
                fgLab[i] = ColorMath.RgbToLab(cellColor[fgIndices[i]]);

            // Never ask for more clusters than there are non-background yarns to put them in.
            int maxYarns = palette.Count - 1;
            int k = Mathf.Clamp(s.colorCount, 1, Mathf.Min(maxYarns, fgIndices.Count));
            int[] assignment = KMeans(fgLab, k, s.kMeansIterations, out Lab[] centroids);

            // --- 6. Snap each cluster to a real yarn -------------------------------------
            // Largest clusters choose first, so the dominant colors of the design get their best
            // yarn match and any compromise lands on small detail areas instead.
            Lab[] paletteLab = ColorMath.PaletteToLab(palette);
            var clusterToYarn = new int[k];
            var clusterSize = new int[k];
            foreach (int a in assignment) clusterSize[a]++;

            var order = new int[k];
            for (int i = 0; i < k; i++) order[i] = i;
            System.Array.Sort(order, (x, y) => clusterSize[y].CompareTo(clusterSize[x]));

            var yarnUsed = new bool[paletteLab.Length];
            foreach (int c in order)
            {
                int best = -1;
                float bestD = float.MaxValue;

                // Start at 1: never snap foreground onto the background yarn, or the subject
                // dissolves into the fabric.
                for (int j = 1; j < paletteLab.Length; j++)
                {
                    if (s.forceDistinctYarns && yarnUsed[j]) continue;
                    float d = Lab.SqrDistance(centroids[c], paletteLab[j]);
                    if (d < bestD) { bestD = d; best = j; }
                }

                if (best < 0) best = ColorMath.NearestPaletteIndex(centroids[c], paletteLab, 1);
                yarnUsed[best] = true;
                clusterToYarn[c] = best;
            }

            for (int i = 0; i < fgIndices.Count; i++)
            {
                int cell = fgIndices[i];
                chart.Cells[cell] = clusterToYarn[assignment[i]];
            }

            // --- 7. Despeckle -------------------------------------------------------------
            if (s.despeckle) Despeckle(chart);

            return chart;
        }

        /// <summary>
        /// Flood-fills inward from all four corners, marking everything within tolerance of the
        /// corner color as background. Flood rather than a global color match so a subject that
        /// happens to contain the background color internally is not punched full of holes.
        /// </summary>
        static void BuildForegroundMask(Color32[] src, int w, int h, float tolerance, bool[] isForeground)
        {
            for (int i = 0; i < isForeground.Length; i++) isForeground[i] = true;

            Lab bg = ColorMath.RgbToLab(src[0]); // bottom-left corner as the reference
            var stack = new Stack<int>();
            var visited = new bool[src.Length];

            void Seed(int x, int y)
            {
                int i = y * w + x;
                if (!visited[i]) stack.Push(i);
            }

            Seed(0, 0); Seed(w - 1, 0); Seed(0, h - 1); Seed(w - 1, h - 1);

            while (stack.Count > 0)
            {
                int i = stack.Pop();
                if (visited[i]) continue;
                visited[i] = true;

                if (Lab.SqrDistance(ColorMath.RgbToLab(src[i]), bg) > tolerance) continue;

                isForeground[i] = false;

                int x = i % w, y = i / w;
                if (x > 0)     stack.Push(i - 1);
                if (x < w - 1) stack.Push(i + 1);
                if (y > 0)     stack.Push(i - w);
                if (y < h - 1) stack.Push(i + w);
            }
        }

        static bool FindBounds(bool[] mask, int w, int h, out int minX, out int minY, out int maxX, out int maxY)
        {
            minX = w; minY = h; maxX = -1; maxY = -1;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (!mask[y * w + x]) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            return maxX >= minX && maxY >= minY;
        }

        /// <summary>
        /// Averages every source pixel falling inside each cell's footprint. Foreground coverage
        /// is tracked separately so partially-covered edge cells can be dropped rather than
        /// producing a halo of half-blended stitches around the subject.
        /// </summary>
        static void AreaDownsample(Color32[] src, int srcWidth, bool[] isForeground,
                                   int cropX, int cropY, int cropW, int cropH,
                                   int cols, int rows, float coverageThreshold,
                                   Color32[] outColor, bool[] outForeground)
        {
            for (int cy = 0; cy < rows; cy++)
            {
                int y0 = cropY + Mathf.FloorToInt((float)cy / rows * cropH);
                int y1 = cropY + Mathf.FloorToInt((float)(cy + 1) / rows * cropH);
                if (y1 <= y0) y1 = y0 + 1;

                for (int cx = 0; cx < cols; cx++)
                {
                    int x0 = cropX + Mathf.FloorToInt((float)cx / cols * cropW);
                    int x1 = cropX + Mathf.FloorToInt((float)(cx + 1) / cols * cropW);
                    if (x1 <= x0) x1 = x0 + 1;

                    long r = 0, g = 0, b = 0;
                    int fgCount = 0, total = 0;

                    for (int y = y0; y < y1; y++)
                    {
                        for (int x = x0; x < x1; x++)
                        {
                            int i = y * srcWidth + x;
                            total++;
                            if (!isForeground[i]) continue;
                            fgCount++;
                            r += src[i].r; g += src[i].g; b += src[i].b;
                        }
                    }

                    int cell = cy * cols + cx;
                    bool covered = total > 0 && (float)fgCount / total >= coverageThreshold;
                    outForeground[cell] = covered && fgCount > 0;
                    outColor[cell] = fgCount > 0
                        ? new Color32((byte)(r / fgCount), (byte)(g / fgCount), (byte)(b / fgCount), 255)
                        : new Color32(0, 0, 0, 0);
                }
            }
        }

        /// <summary>
        /// k-means over Lab points. Seeded deterministically by spreading initial centroids along
        /// sorted lightness rather than at random - two runs on the same image give the same
        /// chart, which matters when a player regenerates and expects consistency.
        /// </summary>
        static int[] KMeans(Lab[] points, int k, int iterations, out Lab[] centroids)
        {
            centroids = new Lab[k];

            var order = new int[points.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            System.Array.Sort(order, (x, y) => points[x].L.CompareTo(points[y].L));

            for (int c = 0; c < k; c++)
            {
                int pick = Mathf.Min(points.Length - 1, Mathf.FloorToInt((c + 0.5f) * points.Length / k));
                centroids[c] = points[order[pick]];
            }

            var assignment = new int[points.Length];
            var sumL = new float[k];
            var sumA = new float[k];
            var sumB = new float[k];
            var count = new int[k];

            for (int iter = 0; iter < iterations; iter++)
            {
                bool changed = false;

                for (int i = 0; i < points.Length; i++)
                {
                    int best = 0;
                    float bestD = float.MaxValue;
                    for (int c = 0; c < k; c++)
                    {
                        float d = Lab.SqrDistance(points[i], centroids[c]);
                        if (d < bestD) { bestD = d; best = c; }
                    }
                    if (assignment[i] != best) { assignment[i] = best; changed = true; }
                }

                System.Array.Clear(sumL, 0, k);
                System.Array.Clear(sumA, 0, k);
                System.Array.Clear(sumB, 0, k);
                System.Array.Clear(count, 0, k);

                for (int i = 0; i < points.Length; i++)
                {
                    int c = assignment[i];
                    sumL[c] += points[i].L; sumA[c] += points[i].a; sumB[c] += points[i].b;
                    count[c]++;
                }

                for (int c = 0; c < k; c++)
                {
                    if (count[c] == 0) continue; // keep an empty cluster where it is
                    centroids[c] = new Lab(sumL[c] / count[c], sumA[c] / count[c], sumB[c] / count[c]);
                }

                if (!changed) break; // converged
            }

            return assignment;
        }

        /// <summary>
        /// Mode filter: a cell whose 8 neighbours strongly disagree with it is replaced by the
        /// neighbourhood majority. Quantization reliably leaves isolated stray cells, and on a
        /// knitting chart a lone off-color stitch reads as a mistake rather than as design.
        /// Reads from a snapshot so the filter is not influenced by its own output.
        /// </summary>
        static void Despeckle(ColorworkChart chart)
        {
            int w = chart.Width, h = chart.Height;
            var snapshot = (int[])chart.Cells.Clone();
            var counts = new Dictionary<int, int>();

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int self = snapshot[y * w + x];
                    counts.Clear();

                    int neighbours = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;

                            int v = snapshot[ny * w + nx];
                            counts.TryGetValue(v, out int c);
                            counts[v] = c + 1;
                            neighbours++;
                        }
                    }

                    int majority = self, majorityCount = 0;
                    foreach (var kv in counts)
                        if (kv.Value > majorityCount) { majorityCount = kv.Value; majority = kv.Key; }

                    counts.TryGetValue(self, out int selfCount);

                    // Only rewrite when the cell is genuinely isolated: a clear majority disagrees
                    // and almost no neighbour agrees. A looser rule erodes thin lines and outlines.
                    if (majority != self && majorityCount >= neighbours * 0.625f && selfCount <= 1)
                        chart.Cells[y * w + x] = majority;
                }
            }
        }
    }
}

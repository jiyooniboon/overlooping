using System;

namespace KnitSprite.CreativeMode.AI
{
    /// <summary>One shape as the LLM returns it (field names match the JSON schema).</summary>
    [Serializable]
    public class Shape
    {
        public string kind;
        public string color;
        public int x, y, w, h, x2, y2, x3, y3;
    }

    /// <summary>The LLM's whole reply (field names match the JSON schema).</summary>
    [Serializable]
    public class ShapeDesign
    {
        public string parts;      // the model's plan in words, written before the shapes
        public string symmetry;
        public Shape[] shapes;
    }

    /// <summary>
    /// Turns a ShapeDesign into a grid of palette indices. Pure C# (no Unity types), so it can be
    /// tested outside the Editor.
    ///
    /// The schema only guarantees structure, not sensible values, so everything is checked here:
    /// shapes are clipped to the grid (one entirely off the grid is dropped, rather than squashed
    /// into a corner where it would throw off ImageToChart's auto-crop), sizes are clamped to
    /// sane ranges, and unknown kinds and colours are skipped.
    /// Grid convention matches the prompt: origin top-left, y down.
    /// </summary>
    public static class ShapeRasterizer
    {
        public struct Stats
        {
            public int drawn;          // shapes that made it onto the grid
            public int dropped;        // unknown kind/colour, degenerate, or entirely off the grid
            public int offGrid;        // shapes with a coordinate outside the grid (drawn clipped)
            public bool mirrored;
            public int filledCells;    // cells that aren't background
            public int yarnsUsed;      // distinct non-background yarns
            public float Coverage(int size) => filledCells / (float)(size * size);
        }

        /// <param name="colorIndex">Maps a colour name to a palette index, or -1 if unknown.</param>
        /// <returns>size*size palette indices, row 0 = TOP row.</returns>
        public static int[] Rasterize(ShapeDesign design, int size, Func<string, int> colorIndex, out Stats stats)
        {
            stats = default;
            var cells = new int[size * size]; // 0 = background yarn
            int max = size - 1;

            if (design?.shapes != null)
            {
                foreach (var s in design.shapes)
                {
                    if (s == null) { stats.dropped++; continue; }
                    int c = colorIndex(s.color);
                    if (c < 0) { stats.dropped++; continue; }

                    if (Outside(s.x, max) || Outside(s.y, max) || Outside(s.x2, max) ||
                        Outside(s.y2, max) || Outside(s.x3, max) || Outside(s.y3, max)) stats.offGrid++;

                    // Clip, don't clamp: the limits only keep the loops short.
                    int x = Clamp(s.x, -size, 2 * size), y = Clamp(s.y, -size, 2 * size);
                    int x2 = Clamp(s.x2, -size, 2 * size), y2 = Clamp(s.y2, -size, 2 * size);
                    int x3 = Clamp(s.x3, -size, 2 * size), y3 = Clamp(s.y3, -size, 2 * size);
                    int half = Math.Max(1, size / 2);

                    bool ok;
                    switch ((s.kind ?? "").Trim().ToLowerInvariant())
                    {
                        case "circle":
                            ok = FillEllipse(cells, size, c, x, y, Clamp(s.w, 1, half), Clamp(s.w, 1, half));
                            break;
                        case "ellipse":
                            int rx = Clamp(s.w, 1, half);
                            ok = FillEllipse(cells, size, c, x, y, rx, s.h > 0 ? Clamp(s.h, 1, half) : rx);
                            break;
                        case "rect":
                        case "rectangle":
                        case "square":
                            int rw = Clamp(s.w, 1, size);
                            ok = FillRect(cells, size, c, x, y, rw, s.h > 0 ? Clamp(s.h, 1, size) : rw);
                            break;
                        case "line":
                            ok = DrawLine(cells, size, c, x, y, x2, y2, Clamp(s.w, 1, 4));
                            break;
                        case "triangle":
                            ok = FillTriangle(cells, size, c, x, y, x2, y2, x3, y3);
                            break;
                        default:
                            ok = false;
                            break;
                    }

                    if (ok) stats.drawn++; else stats.dropped++;
                }
            }

            if (string.Equals(design?.symmetry?.Trim(), "mirror", StringComparison.OrdinalIgnoreCase))
            {
                stats.mirrored = true;
                // Copy the left half onto the right (most motifs are left-right symmetric, and it
                // halves what the model has to get right).
                for (int yy = 0; yy < size; yy++)
                    for (int xx = 0; xx < size / 2; xx++)
                        cells[yy * size + (max - xx)] = cells[yy * size + xx];
            }

            var seen = new bool[256];
            foreach (int v in cells)
            {
                if (v == 0) continue;
                stats.filledCells++;
                if (v < seen.Length && !seen[v]) { seen[v] = true; stats.yarnsUsed++; }
            }
            return cells;
        }

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
        static bool Outside(int v, int max) => v < 0 || v > max;

        static bool FillEllipse(int[] cells, int size, int c, int cx, int cy, int rx, int ry)
        {
            // Test each cell centre. "+ r" (about (r+0.5)^2) gives rounder small circles than a
            // strict r^2, which looks pointy at radius 2-4.
            float ex = rx * rx + rx, ey = ry * ry + ry;
            bool any = false;
            for (int y = Math.Max(0, cy - ry); y <= Math.Min(size - 1, cy + ry); y++)
            {
                for (int x = Math.Max(0, cx - rx); x <= Math.Min(size - 1, cx + rx); x++)
                {
                    float dx = x - cx, dy = y - cy;
                    if (dx * dx / ex + dy * dy / ey <= 1f) { cells[y * size + x] = c; any = true; }
                }
            }
            return any;
        }

        static bool FillRect(int[] cells, int size, int c, int x0, int y0, int w, int h)
        {
            bool any = false;
            for (int y = Math.Max(0, y0); y < Math.Min(size, y0 + h); y++)
                for (int x = Math.Max(0, x0); x < Math.Min(size, x0 + w); x++) { cells[y * size + x] = c; any = true; }
            return any;
        }

        static bool DrawLine(int[] cells, int size, int c, int x0, int y0, int x1, int y1, int thickness)
        {
            int steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
            int lo = -(thickness - 1) / 2, hi = thickness / 2;
            bool any = false;
            for (int i = 0; i <= steps; i++)
            {
                float t = steps == 0 ? 0f : i / (float)steps;
                int px = (int)Math.Round(x0 + (x1 - x0) * t), py = (int)Math.Round(y0 + (y1 - y0) * t);
                for (int oy = lo; oy <= hi; oy++)
                    for (int ox = lo; ox <= hi; ox++)
                    {
                        int xx = px + ox, yy = py + oy;
                        if (xx >= 0 && xx < size && yy >= 0 && yy < size) { cells[yy * size + xx] = c; any = true; }
                    }
            }
            return any;
        }

        static bool FillTriangle(int[] cells, int size, int c, int ax, int ay, int bx, int by, int qx, int qy)
        {
            long area = Edge(ax, ay, bx, by, qx, qy);
            if (area == 0) return false; // all three points on a line

            int minX = Math.Max(0, Math.Min(ax, Math.Min(bx, qx))), maxX = Math.Min(size - 1, Math.Max(ax, Math.Max(bx, qx)));
            int minY = Math.Max(0, Math.Min(ay, Math.Min(by, qy))), maxY = Math.Min(size - 1, Math.Max(ay, Math.Max(by, qy)));
            bool any = false;
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    long e0 = Edge(bx, by, qx, qy, x, y), e1 = Edge(qx, qy, ax, ay, x, y), e2 = Edge(ax, ay, bx, by, x, y);
                    // Inside (or on an edge) when all three agree with the winding of the triangle.
                    bool inside = area > 0 ? (e0 >= 0 && e1 >= 0 && e2 >= 0) : (e0 <= 0 && e1 <= 0 && e2 <= 0);
                    if (inside) { cells[y * size + x] = c; any = true; }
                }
            }
            return any;
        }

        static long Edge(int ax, int ay, int bx, int by, int px, int py) =>
            (long)(bx - ax) * (py - ay) - (long)(by - ay) * (px - ax);
    }
}

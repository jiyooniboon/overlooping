using System.Collections.Generic;
using UnityEngine;
using KnitSprite.CreativeMode;
using KnitSprite.Records;

namespace KnitSprite.Knitting
{
    /// <summary>One stitch the player has to make, in the order they make it.</summary>
    public struct KnitInstruction
    {
        /// 0 = the cast-on row, 1..N = chart rows counted from the bottom (chart y = row - 1).
        public int row;
        /// Chart column (x), 0 = left edge as drawn.
        public int column;
        public StitchType type;
        public int yarnIndex;

        /// Position along the row as a knitter counts it (1 = first stitch worked in this row).
        public int stitchInRow;
    }

    /// <summary>
    /// A colorwork chart turned into the ordered list of stitches needed to knit it flat.
    ///
    /// Order follows how a printed chart is read:
    ///   - Cast-on row: one stitch per chart column, in the background yarn (index 0). Worked left
    ///     to right so it ends at the right edge, where row 1 begins.
    ///   - Chart rows from the bottom up, alternating direction (serpentine):
    ///       odd rows (1, 3, ...) are right-side rows, read right to left, knit;
    ///       even rows (2, 4, ...) are wrong-side rows, read left to right, purl
    ///     - plain stockinette, the usual fabric for colorwork.
    ///   - Each stitch uses the yarn of its chart cell.
    ///
    /// Pure data: no MonoBehaviour, no scene references. Results (per-stitch quality) are stored
    /// here too so the finished piece can be shown or saved later.
    /// </summary>
    public class KnitPattern
    {
        public readonly int width;
        public readonly int chartRows;
        public readonly string[] yarnNames;
        public readonly Color32[] yarnColors;
        public readonly ColorworkChart sourceChart;

        readonly List<KnitInstruction> instructions = new List<KnitInstruction>();

        /// Per stitch: -1 = not made yet, otherwise (int)ProgressManager.StitchQuality.
        public readonly int[] results;

        public int Count => instructions.Count;
        public KnitInstruction this[int i] => instructions[i];

        /// Total rows including the cast-on row.
        public int TotalRows => chartRows + 1;

        KnitPattern(ColorworkChart chart, YarnPalette palette)
        {
            sourceChart = chart.Clone();
            width = chart.Width;
            chartRows = chart.Height;

            int n = palette != null ? palette.Count : 0;
            yarnNames = new string[n];
            yarnColors = new Color32[n];
            for (int i = 0; i < n; i++) { yarnNames[i] = palette.GetName(i); yarnColors[i] = palette.GetColor(i); }

            // cast-on row, left to right
            for (int x = 0; x < width; x++)
                instructions.Add(new KnitInstruction
                {
                    row = 0, column = x, type = StitchType.CastOn, yarnIndex = 0, stitchInRow = x + 1
                });

            // chart rows, serpentine
            for (int r = 1; r <= chartRows; r++)
            {
                bool rightSide = (r % 2) == 1;
                for (int k = 0; k < width; k++)
                {
                    int x = rightSide ? width - 1 - k : k;
                    instructions.Add(new KnitInstruction
                    {
                        row = r,
                        column = x,
                        type = rightSide ? StitchType.Knit : StitchType.Purl,
                        yarnIndex = chart.Get(x, r - 1),
                        stitchInRow = k + 1,
                    });
                }
            }

            results = new int[instructions.Count];
            for (int i = 0; i < results.Length; i++) results[i] = -1;
        }

        public static KnitPattern FromChart(ColorworkChart chart, YarnPalette palette)
        {
            if (chart == null || chart.Width <= 0 || chart.Height <= 0) return null;
            return new KnitPattern(chart, palette);
        }

        public string YarnName(int index) =>
            index >= 0 && index < yarnNames.Length ? yarnNames[index] : $"Yarn {index}";

        public Color32 YarnColor(int index) =>
            index >= 0 && index < yarnColors.Length ? yarnColors[index] : new Color32(255, 255, 255, 255);

        /// "Cast on, stitch 4 of 12" / "Row 3 (wrong side, purl), stitch 7 of 12"
        public string Describe(int i)
        {
            var s = instructions[i];
            if (s.row == 0) return $"Cast on, stitch {s.stitchInRow} of {width}";
            string side = s.type == StitchType.Knit ? "right side, knit" : "wrong side, purl";
            return $"Row {s.row} ({side}), stitch {s.stitchInRow} of {width}";
        }

        public void ResetResults()
        {
            for (int i = 0; i < results.Length; i++) results[i] = -1;
        }
    }

    /// <summary>
    /// Carries a pattern from creative mode into the gameplay scene (and the chart back again).
    /// Static so it survives SceneManager.LoadScene; it's cleared by nothing but the game itself.
    /// </summary>
    public static class KnitSession
    {
        /// The pattern being knit, or last knit. Creative mode reads sourceChart from it to restore
        /// the chart when the player comes back.
        public static KnitPattern Current { get; private set; }

        /// True between "Knit this" in creative mode and the gameplay page picking it up. Keeps
        /// the story-mode lesson from accidentally running a leftover pattern.
        public static bool Pending { get; private set; }

        public static void Begin(KnitPattern pattern)
        {
            Current = pattern;
            Pending = pattern != null;
        }

        /// Called by the gameplay page. Returns the pattern to run, or null for a normal lesson.
        public static KnitPattern Consume()
        {
            if (!Pending) return null;
            Pending = false;
            return Current;
        }
    }
}

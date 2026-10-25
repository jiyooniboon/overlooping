using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace KnitSprite.CreativeMode.AI
{
    /// <summary>
    /// The LLM draws with shapes (plan §3). LLMs can't lay out a 2D grid cell by cell, but they
    /// are reasonably good at describing a picture as a few positioned shapes, the way they
    /// write SVG. This code turns those shapes into pixels, so the image is always clean, flat
    /// and in the exact yarn colours: the ideal input for ImageToChart.
    ///
    /// The model first writes "parts" (what makes the subject recognizable, in words) and only
    /// then the shapes. Small models otherwise jump straight to coordinates and return two blobs.
    ///
    /// The prompt and worked examples live in Assets/Resources/AI/ (ShapePrompt.txt,
    /// ShapeExamples.json) so they can be tuned without recompiling, and so AITools/shape_test.py
    /// tests exactly what the game sends.
    ///
    /// Fails (so FallbackSource moves on) if the model is unreachable, the reply doesn't parse,
    /// or the drawing is nearly empty.
    /// </summary>
    public class ShapeSource : IChartImageSource, ICancellableSource
    {
        public const string PromptResource = "AI/ShapePrompt";
        public const string ExamplesResource = "AI/ShapeExamples";

        /// <summary>Grid the model draws on. ImageToChart crops and fits it to the chart size.</summary>
        public int gridSize = 32;

        /// <summary>Texture pixels per grid cell. 8 -> 256x256 for a 32 grid.</summary>
        public int cellPixels = 8;

        /// <summary>Fewest shapes the reply may contain (enforced by the schema).</summary>
        public int minShapes = 4;

        /// <summary>
        /// Seconds to wait for the reply. Without thinking, ~2-10s for a 4-9B model on Apple
        /// Silicon once warm; with thinking, allow 60+.
        /// </summary>
        public float timeoutSeconds = 20f;

        /// <summary>Below this fraction of non-background cells the drawing counts as a failure.</summary>
        public float minCoverage = 0.05f;

        /// <summary>Log every raw reply to the Console (useful when tuning the prompt).</summary>
        public bool logReplies = true;

        /// <summary>The last reply, for debugging.</summary>
        public string LastJson { get; private set; }
        public float LastSeconds { get; private set; }

        readonly ILlmClient llm;
        readonly YarnPalette palette;
        int runId;              // bumped on Abort, so a stale reply is never applied
        string template;        // loaded once
        string examplesText;    // built once per grid size
        int examplesGrid = -1;

        public ShapeSource(ILlmClient llm, YarnPalette palette)
        {
            this.llm = llm;
            this.palette = palette;
        }

        public string DisplayName => $"AI shapes ({llm.ModelName})";

        public void Abort()
        {
            runId++;
            llm.Abort();
        }

        public IEnumerator Generate(string keyword, Action<ImageResult> onComplete)
        {
            int myRun = ++runId;

            if (palette == null || palette.Count < 2)
            {
                onComplete(ImageResult.Fail("ShapeSource needs a palette with at least 2 yarns."));
                yield break;
            }

            string prompt = null, setupError = null;
            try { prompt = BuildPrompt(keyword); }
            catch (Exception e) { Debug.LogException(e); setupError = e.Message; }
            if (prompt == null)
            {
                onComplete(ImageResult.Fail(setupError ?? "Couldn't build the shape prompt."));
                yield break;
            }

            LlmResult reply = default;
            bool got = false;
            yield return llm.CompleteJson(prompt, BuildSchema(), timeoutSeconds, r => { reply = r; got = true; });

            if (myRun != runId) { onComplete(ImageResult.Cancelled()); yield break; }
            if (!got) { onComplete(ImageResult.Fail($"{llm.DisplayName} returned nothing.")); yield break; }
            if (!reply.ok) { onComplete(ImageResult.Fail(reply.error)); yield break; }

            LastJson = reply.json;
            LastSeconds = reply.seconds;
            if (logReplies) Debug.Log($"[ShapeSource] {llm.ModelName} \"{keyword}\" ({reply.seconds:0.0}s):\n{reply.json}");

            // No yield inside try/catch (C# forbids it), so results are carried out of the block.
            Texture2D tex = null;
            string error = null;
            ShapeDesign design = null;
            ShapeRasterizer.Stats stats = default;
            try
            {
                design = JsonUtility.FromJson<ShapeDesign>(reply.json);
                if (design?.shapes == null || design.shapes.Length == 0)
                {
                    error = $"{llm.ModelName} replied without any shapes.";
                }
                else
                {
                    int[] cells = ShapeRasterizer.Rasterize(design, gridSize, ColorIndex, out stats);
                    if (stats.drawn == 0)
                        error = $"None of the {design.shapes.Length} shapes from {llm.ModelName} were usable.";
                    else if (stats.Coverage(gridSize) < minCoverage)
                        error = $"{llm.ModelName}'s drawing was almost empty ({stats.Coverage(gridSize):P0} filled).";
                    else
                        tex = ToTexture(cells);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                error = $"Couldn't read {llm.ModelName}'s reply.";
            }

            if (tex == null)
            {
                onComplete(ImageResult.Fail(error ?? "Shape drawing failed."));
                yield break;
            }

            // Show what the model did: it makes the AI visible in the UI and in the demo.
            var msg = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(design.parts))
            {
                string plan = design.parts.Trim();
                if (plan.Length > 110) plan = plan.Substring(0, 107) + "...";  // the response box is small
                msg.Append($"Plan: {plan}\n");
            }
            msg.Append($"{llm.ModelName} drew \"{keyword}\" with {stats.drawn} shape{(stats.drawn == 1 ? "" : "s")}");
            if (stats.mirrored) msg.Append(" (mirror)");
            msg.Append($" in {reply.seconds:0.0}s.");
            if (stats.dropped > 0) msg.Append($" {stats.dropped} unusable shape{(stats.dropped == 1 ? " was" : "s were")} skipped.");
            msg.Append(" Edit it by hand from here.");

            var result = ImageResult.Ok(tex, msg.ToString());
            result.colorCountHint = Mathf.Max(1, stats.yarnsUsed);
            onComplete(result);
        }

        // ---- prompt ----------------------------------------------------------------------------

        string BuildPrompt(string keyword)
        {
            if (template == null)
            {
                var t = Resources.Load<TextAsset>(PromptResource);
                if (t == null) throw new InvalidOperationException($"Missing Assets/Resources/{PromptResource}.txt");
                template = t.text;
            }
            if (examplesText == null || examplesGrid != gridSize)
            {
                examplesText = BuildExamples();
                examplesGrid = gridSize;
            }

            var names = DrawableNames();
            return template
                .Replace("{size}", gridSize.ToString())
                .Replace("{max}", (gridSize - 1).ToString())
                .Replace("{halfmax}", (gridSize / 2 - 1).ToString())
                .Replace("{half}", (gridSize / 2).ToString())
                .Replace("{background}", palette.GetName(0).Trim().ToLowerInvariant())
                .Replace("{colors}", string.Join(", ", names))
                .Replace("{examples}", examplesText)
                .Replace("{keyword}", keyword);
        }

        [Serializable] class ExampleSet { public Example[] examples; }
        [Serializable] class Example { public string keyword; public ShapeDesign design; }

        /// <summary>
        /// The worked examples, scaled to gridSize and with each target colour swapped for a
        /// real yarn name. Must match build_examples() in AITools/shape_test.py.
        /// </summary>
        string BuildExamples()
        {
            var asset = Resources.Load<TextAsset>(ExamplesResource);
            if (asset == null) throw new InvalidOperationException($"Missing Assets/Resources/{ExamplesResource}.json");
            var set = JsonUtility.FromJson<ExampleSet>(asset.text);
            if (set?.examples == null || set.examples.Length == 0)
                throw new InvalidOperationException($"{ExamplesResource}.json has no examples.");

            float k = gridSize / 32f;
            int S(int v) => Mathf.RoundToInt(v * k);
            var sb = new StringBuilder();
            foreach (var ex in set.examples)
            {
                var d = ex.design;
                if (d?.shapes == null) continue;
                var colorOf = MapExampleColors(d.shapes);

                if (sb.Length > 0) sb.Append("\n\n");
                sb.Append($"Example for \"{ex.keyword}\":\n");
                sb.Append("{\"parts\":").Append(Json.Quote(d.parts ?? ""))
                  .Append(",\"symmetry\":").Append(Json.Quote(d.symmetry ?? "none"))
                  .Append(",\"shapes\":[\n");
                for (int i = 0; i < d.shapes.Length; i++)
                {
                    var s = d.shapes[i];
                    var scaled = new Shape
                    {
                        kind = s.kind, x = S(s.x), y = S(s.y), x2 = S(s.x2), y2 = S(s.y2), x3 = S(s.x3), y3 = S(s.y3),
                        w = Mathf.Max(1, S(s.w)), h = Mathf.Max(1, S(s.h))
                    };
                    sb.Append(' ').Append(CompactShape(scaled, palette.GetName(colorOf[s.color]).Trim().ToLowerInvariant()));
                    sb.Append(i < d.shapes.Length - 1 ? ",\n" : "]}");
                }
            }
            return sb.ToString();
        }

        /// <summary>Only the fields a kind uses, so the model never sees meaningless zeros.</summary>
        static string CompactShape(Shape s, string colorName)
        {
            var sb = new StringBuilder();
            sb.Append("{\"kind\":").Append(Json.Quote(s.kind)).Append(",\"color\":").Append(Json.Quote(colorName));
            void F(string n, int v) => sb.Append(",\"").Append(n).Append("\":").Append(v);
            F("x", s.x); F("y", s.y);
            switch (s.kind)
            {
                case "circle": F("w", s.w); break;
                case "ellipse": case "rect": F("w", s.w); F("h", s.h); break;
                case "line": F("x2", s.x2); F("y2", s.y2); F("w", s.w); break;
                case "triangle": F("x2", s.x2); F("y2", s.y2); F("x3", s.x3); F("y3", s.y3); break;
            }
            return sb.Append('}').ToString();
        }

        /// <summary>
        /// Maps one example's target colours ("#rrggbb") to yarn indices: each distinct colour, in
        /// order of first use, takes the closest yarn not yet taken in this example (never the
        /// background); once every yarn is taken, the closest yarn overall. This keeps details a
        /// different yarn from the body they sit on, even with a small palette.
        /// </summary>
        Dictionary<string, int> MapExampleColors(Shape[] shapes)
        {
            var paletteLab = ColorMath.PaletteToLab(palette);
            var taken = new HashSet<int>();
            var map = new Dictionary<string, int>();
            foreach (var s in shapes)
            {
                if (s.color == null || map.ContainsKey(s.color)) continue;
                if (!ColorUtility.TryParseHtmlString(s.color, out Color c)) { map[s.color] = 1; continue; }
                Lab target = ColorMath.RgbToLab((Color32)c);

                int best = -1;
                float bestD = float.MaxValue;
                bool anyFree = false;
                for (int i = 1; i < paletteLab.Length; i++) if (!taken.Contains(i)) anyFree = true;
                for (int i = 1; i < paletteLab.Length; i++)
                {
                    if (anyFree && taken.Contains(i)) continue;
                    float d = Lab.SqrDistance(target, paletteLab[i]);
                    if (d < bestD) { bestD = d; best = i; }
                }
                if (best < 0) best = 1;
                taken.Add(best);
                map[s.color] = best;
            }
            return map;
        }

        // ---- palette <-> names -----------------------------------------------------------------

        /// <summary>Case-insensitive yarn name -> palette index, or -1.</summary>
        int ColorIndex(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return -1;
            string n = name.Trim();
            for (int i = 0; i < palette.Count; i++)
                if (string.Equals(palette.GetName(i).Trim(), n, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>
        /// Yarns the model may draw with: everything except the background (index 0). ImageToChart
        /// never maps foreground onto the background yarn, so a background-coloured detail would
        /// come out as some other yarn anyway; leaving it out of the enum avoids that surprise.
        /// </summary>
        List<string> DrawableNames()
        {
            var names = new List<string>();
            for (int i = 1; i < palette.Count; i++) names.Add(palette.GetName(i).Trim().ToLowerInvariant());
            return names;
        }

        // ---- schema ----------------------------------------------------------------------------

        /// <summary>
        /// Built at runtime so the colour enum is the actual palette. "parts" comes first so the
        /// model writes its plan before any coordinates. Must match build_schema() in shape_test.py.
        /// </summary>
        string BuildSchema()
        {
            var colorEnum = new StringBuilder();
            foreach (var n in DrawableNames())
            {
                if (colorEnum.Length > 0) colorEnum.Append(',');
                colorEnum.Append(Json.Quote(n));
            }

            return
                "{\"type\":\"object\",\"properties\":{" +
                  "\"parts\":{\"type\":\"string\"}," +
                  "\"symmetry\":{\"type\":\"string\",\"enum\":[\"none\",\"mirror\"]}," +
                  "\"shapes\":{\"type\":\"array\",\"minItems\":" + Mathf.Max(1, minShapes) + ",\"maxItems\":20,\"items\":{" +
                    "\"type\":\"object\",\"properties\":{" +
                      "\"kind\":{\"type\":\"string\",\"enum\":[\"circle\",\"ellipse\",\"rect\",\"line\",\"triangle\"]}," +
                      "\"color\":{\"type\":\"string\",\"enum\":[" + colorEnum + "]}," +
                      "\"x\":{\"type\":\"integer\"},\"y\":{\"type\":\"integer\"}," +
                      "\"w\":{\"type\":\"integer\"},\"h\":{\"type\":\"integer\"}," +
                      "\"x2\":{\"type\":\"integer\"},\"y2\":{\"type\":\"integer\"}," +
                      "\"x3\":{\"type\":\"integer\"},\"y3\":{\"type\":\"integer\"}" +
                    "},\"required\":[\"kind\",\"color\",\"x\",\"y\"]}}" +
                "},\"required\":[\"parts\",\"symmetry\",\"shapes\"]}";
        }

        // ---- grid -> texture -------------------------------------------------------------------

        Texture2D ToTexture(int[] cells)
        {
            int px = gridSize * cellPixels;
            var tex = new Texture2D(px, px, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            var colors = new Color32[palette.Count];
            for (int i = 0; i < colors.Length; i++) colors[i] = palette.GetColor(i);

            var pixels = new Color32[px * px];
            for (int cy = 0; cy < gridSize; cy++)
            {
                // The grid is top-down (as in the prompt); textures are bottom-up.
                int texRow0 = (gridSize - 1 - cy) * cellPixels;
                for (int cx = 0; cx < gridSize; cx++)
                {
                    Color32 col = colors[Mathf.Clamp(cells[cy * gridSize + cx], 0, colors.Length - 1)];
                    for (int oy = 0; oy < cellPixels; oy++)
                    {
                        int row = (texRow0 + oy) * px + cx * cellPixels;
                        for (int ox = 0; ox < cellPixels; ox++) pixels[row + ox] = col;
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false);
            return tex;
        }
    }
}

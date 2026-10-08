using System;
using System.Collections;
using UnityEngine;

namespace KnitSprite.CreativeMode
{
    /// <summary>
    /// Stand-in for the real image model. Draws a symmetric blocky motif on a solid background,
    /// seeded from the keyword so the same word always produces the same design.
    ///
    /// This is deliberately not a random noise field. It mimics the *shape* of what a well-prompted
    /// flat-icon generation returns - centered subject, solid background, few flat colors - so the
    /// whole downstream pipeline (background key, auto-crop, area downsample, Lab quantize,
    /// despeckle) is genuinely exercised rather than merely executed. When the real model is
    /// plugged in, the only thing that changes is where the pixels come from.
    ///
    /// It also fakes latency and honours cancellation, so the loading/cancel UI is real work
    /// rather than something to be wired up later against an unpredictable dependency.
    /// </summary>
    public class PlaceholderImageSource : IChartImageSource
    {
        public string DisplayName => "Placeholder (no model)";

        /// <summary>Simulated generation time. Real local SD is closer to 5-30s.</summary>
        public float fakeLatencySeconds = 1.5f;

        /// <summary>Set by the controller to abort an in-flight generation.</summary>
        public bool CancelRequested { get; set; }

        const int TextureSize = 256;
        const int MotifCells = 16;   // per quadrant, before mirroring -> 32x32 motif

        public IEnumerator Generate(string keyword, Action<ImageResult> onComplete)
        {
            CancelRequested = false;

            float elapsed = 0f;
            while (elapsed < fakeLatencySeconds)
            {
                if (CancelRequested)
                {
                    onComplete(ImageResult.Cancelled());
                    yield break;
                }
                elapsed += Time.deltaTime;
                yield return null;
            }

            // Note: no `yield` inside this try/catch - C# forbids yielding from a try block that
            // has a catch clause, so the failure path is handled after the block instead.
            Texture2D tex = null;
            try
            {
                tex = DrawMotif(keyword);
            }
            catch (Exception e)
            {
                Debug.LogException(e); // never let a drawing bug surface as a hung UI
            }

            if (tex == null)
            {
                onComplete(ImageResult.Fail("Placeholder generation failed."));
                yield break;
            }

            onComplete(ImageResult.Ok(tex,
                $"AI response here - placeholder motif for \"{keyword}\". " +
                "No model is connected yet."));
        }

        Texture2D DrawMotif(string keyword)
        {
            // Deterministic per keyword: same word -> same motif, which makes the placeholder
            // useful for eyeballing pipeline changes side by side.
            var rng = new System.Random(StableHash(keyword));

            // Pick a background and two contrasting motif colors in HSV so they always read as
            // distinct after quantization.
            // Separated in hue AND value so the two motif colors survive clustering as genuinely
            // distinct yarns rather than collapsing into one.
            float bgHue = (float)rng.NextDouble();
            Color32 bg = Color.HSVToRGB(bgHue, 0.08f, 0.96f);
            Color32 main = Color.HSVToRGB((bgHue + 0.45f) % 1f, 0.70f, 0.80f);
            Color32 accent = Color.HSVToRGB((bgHue + 0.80f) % 1f, 0.55f, 0.32f);

            // Build one quadrant, then mirror both ways for a symmetric motif.
            int q = MotifCells;
            var quad = new int[q * q];
            for (int y = 0; y < q; y++)
            {
                for (int x = 0; x < q; x++)
                {
                    // Bias toward the centre so the motif has a solid core and ragged edges,
                    // rather than dust scattered across the whole square.
                    float dist = Mathf.Sqrt(x * x + y * y) / (q * 1.15f);
                    double p = 0.85 - dist;
                    int v = rng.NextDouble() < p ? 1 : 0;
                    if (v == 1 && rng.NextDouble() < 0.28) v = 2; // accent speckles
                    quad[y * q + x] = v;
                }
            }

            int motif = q * 2;
            var motifCells = new int[motif * motif];
            for (int y = 0; y < q; y++)
            {
                for (int x = 0; x < q; x++)
                {
                    int v = quad[y * q + x];
                    // Mirror about the motif centre: the quadrant occupies columns q..2q-1, so
                    // its reflection is q-1-x, NOT motif-1-x (which lands back in the same half).
                    int rx = q - 1 - x, ry = q - 1 - y;
                    motifCells[(q + y) * motif + (q + x)] = v;
                    motifCells[(q + y) * motif + rx] = v;
                    motifCells[ry * motif + (q + x)] = v;
                    motifCells[ry * motif + rx] = v;
                }
            }

            var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            var px = new Color32[TextureSize * TextureSize];
            for (int i = 0; i < px.Length; i++) px[i] = bg;

            // Centre the motif with a generous margin so auto-crop has something real to trim.
            int drawSize = Mathf.RoundToInt(TextureSize * 0.62f);
            int origin = (TextureSize - drawSize) / 2;
            float cellPx = drawSize / (float)motif;

            for (int y = 0; y < drawSize; y++)
            {
                int my = Mathf.Clamp((int)(y / cellPx), 0, motif - 1);
                for (int x = 0; x < drawSize; x++)
                {
                    int mx = Mathf.Clamp((int)(x / cellPx), 0, motif - 1);
                    int v = motifCells[my * motif + mx];
                    if (v == 0) continue;

                    px[(origin + y) * TextureSize + (origin + x)] = v == 1 ? main : accent;
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false);
            return tex;
        }

        /// <summary>
        /// string.GetHashCode is randomized per process in modern .NET, so the same keyword would
        /// give a different motif each run. This hash is stable across runs.
        /// </summary>
        static int StableHash(string s)
        {
            unchecked
            {
                int hash = 23;
                foreach (char c in s) hash = hash * 31 + c;
                return hash;
            }
        }
    }
}
